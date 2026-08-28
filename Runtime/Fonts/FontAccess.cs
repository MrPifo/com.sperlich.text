using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Sperlich.Text {

	/// <summary>
	/// Owns the runtime SDF font data for one <see cref="FontDefinition"/>. v1 sources the distance field
	/// through <see cref="TMP_FontAsset"/> in dynamic mode — that is the only public Unity 6 API that can
	/// rasterise glyphs to an SDF atlas (raw <c>FontEngine</c> rendering is internal). The msdfgen native
	/// plugin path from the plan would replace only this class.
	/// Main-thread only.
	/// </summary>
	public sealed class FontAccess {

		private readonly FontDefinition definition;
		private readonly List<TMP_FontAsset> assets = new();
		private readonly FaceMetrics[] metricsCache;
		private readonly bool[] metricsLoaded;

		public FontDefinition Definition => definition;
		public int FaceCount => assets.Count;
		public bool IsReady => assets.Count > 0 && assets[0] != null;

		public TMP_FontAsset Primary => assets.Count > 0 ? assets[0] : null;
		public Texture AtlasTexture => Primary != null ? Primary.atlasTexture : null;
		public int AtlasSize => Primary != null ? Primary.atlasWidth : definition.atlasSize;
		public int Padding => Primary != null ? Primary.atlasPadding : definition.sdfPadding;

		public FontAccess(FontDefinition definition) {
			this.definition = definition;
			BuildAssets();
			metricsCache = new FaceMetrics[Mathf.Max(1, assets.Count)];
			metricsLoaded = new bool[metricsCache.Length];
		}

		private void BuildAssets() {
			GlyphRenderMode mode = GlyphRenderMode.SDFAA;
			int sampling = Mathf.Clamp(definition.samplingPointSize, 16, 200);
			int padding = Mathf.Clamp(definition.sdfPadding, 4, 32);
			int atlas = Mathf.Clamp(Mathf.NextPowerOfTwo(definition.atlasSize), 256, 8192);

			foreach (Font face in definition.EnumerateFaces()) {
				if (face == null) continue;
				TMP_FontAsset a = null;
				try {
					a = TMP_FontAsset.CreateFontAsset(
						face, sampling, padding, mode, atlas, atlas,
						AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
				} catch (System.Exception e) {
					Debug.LogError($"[SperlichText] TMP_FontAsset.CreateFontAsset failed for font '{face.name}': {e.Message}");
				}
				if (a == null) {
					Debug.LogWarning($"[SperlichText] Could not build a runtime font asset from '{face.name}'. " +
						"It must be an imported dynamic Font (.ttf/.otf), not an OS font, and TMP Essential Resources must be imported.");
					continue;
				}
				a.hideFlags = HideFlags.DontSave;
				assets.Add(a);
			}

			if (assets.Count > 1) {
				List<TMP_FontAsset> table = assets[0].fallbackFontAssetTable ?? new List<TMP_FontAsset>();
				for (int i = 1; i < assets.Count; i++) table.Add(assets[i]);
				assets[0].fallbackFontAssetTable = table;
			}
		}

		/// <summary>Face metrics for chain entry <paramref name="faceIndex"/> (0 = primary).</summary>
		public FaceMetrics GetMetrics(int faceIndex) {
			if (faceIndex < 0 || faceIndex >= metricsCache.Length) return default;
			if (metricsLoaded[faceIndex]) return metricsCache[faceIndex];

			FaceMetrics m = default;
			if (faceIndex < assets.Count && assets[faceIndex] != null) {
				FaceInfo fi = assets[faceIndex].faceInfo;
				m.SamplingPointSize = fi.pointSize > 0 ? fi.pointSize : definition.samplingPointSize;
				m.Scale = fi.scale <= 0 ? 1f : fi.scale;
				m.LineHeight = fi.lineHeight > 0 ? fi.lineHeight : m.SamplingPointSize * 1.2f;
				m.AscentLine = fi.ascentLine != 0 ? fi.ascentLine : m.SamplingPointSize * 0.8f;
				m.CapLine = fi.capLine;
				m.MeanLine = fi.meanLine;
				m.Baseline = fi.baseline;
				m.DescentLine = fi.descentLine != 0 ? fi.descentLine : -m.SamplingPointSize * 0.2f;
				m.UnderlineOffset = fi.underlineOffset;
				m.UnderlineThickness = Mathf.Max(1f, fi.underlineThickness);
				m.StrikethroughOffset = fi.strikethroughOffset != 0 ? fi.strikethroughOffset : m.CapLine * 0.4f;
				m.StrikethroughThickness = Mathf.Max(1f, fi.strikethroughThickness);
				m.SuperscriptOffset = fi.superscriptOffset;
				m.SuperscriptSize = fi.superscriptSize <= 0 ? 0.5f : fi.superscriptSize;
				m.SubscriptOffset = fi.subscriptOffset;
				m.SubscriptSize = fi.subscriptSize <= 0 ? 0.5f : fi.subscriptSize;
				m.TabWidth = fi.tabWidth > 0 ? fi.tabWidth : m.SamplingPointSize * 2f;
			}
			metricsCache[faceIndex] = m;
			metricsLoaded[faceIndex] = true;
			return m;
		}

		public FaceMetrics PrimaryMetrics => GetMetrics(0);

		/// <summary>
		/// Looks up an already-present character across the chain. Returns false when it is not in any
		/// atlas yet (caller queues it via <see cref="TryAddCharacters"/>).
		/// </summary>
		public bool TryGetCharacter(uint unicode, out int faceIndex, out TMP_Character character) {
			for (int i = 0; i < assets.Count; i++) {
				if (assets[i] != null && assets[i].characterLookupTable.TryGetValue(unicode, out character)) {
					faceIndex = i;
					return true;
				}
			}
			faceIndex = -1;
			character = null;
			return false;
		}

		/// <summary>Adds a batch of code points to the primary atlas (dynamic SDF generation).</summary>
		public bool TryAddCharacters(uint[] unicodes) {
			if (Primary == null || unicodes == null || unicodes.Length == 0) return false;
			return Primary.TryAddCharacters(unicodes, out _, includeFontFeatures: false);
		}

		/// <summary>True when the primary atlas is out of room for the last add.</summary>
		public bool AtlasLikelyFull(uint unicode) {
			return Primary != null && Primary.characterLookupTable.ContainsKey(unicode) == false;
		}

		/// <summary>Kerning advance in sampling-point-size units. v1: 0 (see README "Kerning").</summary>
		public float GetKerning(int faceIndex, uint firstGlyph, uint secondGlyph) => 0f;

		/// <summary>Clears dynamically added glyphs so the atlas can be repopulated.</summary>
		public void ClearDynamicData() {
			for (int i = 0; i < assets.Count; i++) assets[i]?.ClearFontAssetData(setAtlasSizeToZero: false);
		}

		public void Dispose() {
			for (int i = 0; i < assets.Count; i++) {
				if (assets[i] == null) continue;
				if (Application.isPlaying) Object.Destroy(assets[i]);
				else Object.DestroyImmediate(assets[i]);
			}
			assets.Clear();
		}
	}
}
