using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Authoring asset for a text style family: one primary <see cref="Font"/> plus an ordered
	/// fallback chain. No offline bake step; faces are loaded through <see cref="FontAccess"/> at runtime.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Font Definition", fileName = "FontDefinition")]
	public sealed class FontDefinition : ScriptableObject {

		[Header("Faces")]
		[Tooltip("Primary font. Must be an importable .ttf/.otf Font asset.")]
		public Font primary;

		[Tooltip("Tried in order when the primary font lacks a glyph. Last resort is the tofu box.")]
		public List<Font> fallbacks = new();

		[Header("Rasterizer")]
		[Tooltip("Point size the SDF atlas is sampled at. Higher = sharper small text, more atlas memory.")]
		[Range(24, 160)] public int samplingPointSize = 90;

		[Tooltip("SDF spread in pixels. Larger allows thicker outlines/glow before clipping.")]
		[Range(4, 32)] public int sdfPadding = 9;

		/// <summary>Allowed square atlas edge lengths, in pixels. Fixed set so the value stays a
		/// power of two that the GPU and TMP are happy with.</summary>
		public enum AtlasResolution {
			_512 = 512,
			_1024 = 1024,
			_2048 = 2048,
			_4096 = 4096,
			_8192 = 8192
		}

		[Header("Atlas")]
		[Tooltip("Square atlas edge length in pixels.")]
		public AtlasResolution atlasResolution = AtlasResolution._2048;

		/// <summary>Square atlas edge length in pixels (from <see cref="atlasResolution"/>).</summary>
		public int atlasSize => (int)atlasResolution;

		[Tooltip("Distance field kind. Only SDF is produced in v1; MTSDF is reserved for the native plugin path.")]
		public GlyphFieldKind fieldKind = GlyphFieldKind.SDF;

		/// <summary>Enumerates primary + fallbacks, skipping nulls, primary first.</summary>
		public IEnumerable<Font> EnumerateFaces() {
			if (primary != null) yield return primary;
			for (int i = 0; i < fallbacks.Count; i++) {
				if (fallbacks[i] != null && fallbacks[i] != primary) yield return fallbacks[i];
			}
		}

#if UNITY_EDITOR
		private static bool s_rebuildQueued;

		/// <summary>
		/// Editor-only. The runtime <see cref="FontAccess"/> bakes sampling size / SDF padding / atlas
		/// size / face list when it is built and never re-reads this asset. So an inspector edit would
		/// otherwise do nothing to labels already on screen. Here we clamp the numeric fields and queue
		/// a one-shot rebuild of every live <see cref="SperlichText"/>.
		/// </summary>
		private void OnValidate() {
			samplingPointSize = Mathf.Clamp(samplingPointSize, 24, 160);
			sdfPadding = Mathf.Clamp(sdfPadding, 4, 32);

			if (s_rebuildQueued) return;
			s_rebuildQueued = true;
			UnityEditor.EditorApplication.delayCall += RebuildLiveLabels;
		}

		private static void RebuildLiveLabels() {
			s_rebuildQueued = false;
			GlyphStoreRegistry.EditorPurgeAll();
			foreach (SperlichText label in FindObjectsByType<SperlichText>(
				         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
				label.EditorRebindFont();
			}
		}
#endif
	}
}
