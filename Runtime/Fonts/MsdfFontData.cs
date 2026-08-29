using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// A baked MTSDF (or MSDF) atlas plus the glyph metrics needed to place it. Produced by the editor
	/// baker and consumed at runtime by <c>MsdfFontFaceSource</c>. Fully platform-neutral — no font
	/// bytes, no generation code at runtime.
	/// </summary>
	public sealed class MsdfFontData : ScriptableObject {

		[Serializable]
		public struct FaceRecord {
			/// <summary>Project path of the source font, for the "bake out of date" check and diagnostics.</summary>
			public string assetPath;
			public FaceMetrics metrics;
		}

		[Serializable]
		public struct GlyphRecord {
			public int face;
			public uint codepoint;
			public uint glyphIndex;
			public float advance, width, height, bearingX, bearingY;
			/// <summary>Atlas rect in pixels, bottom-left origin, spread margin included.</summary>
			public float rectX, rectY, rectW, rectH;
			public float padding;
		}

		[Serializable]
		public struct KerningRecord {
			public int face;
			public uint left, right;
			public float advance;
		}

		public Texture2D atlas;
		public int atlasSize;
		/// <summary>msdfgen distance range in atlas pixels (== <c>emSize</c> space).</summary>
		public float pixelRange;
		/// <summary>EM size (px) glyphs were generated at; equals every face's <c>SamplingPointSize</c>.</summary>
		public float emSize;
		public GlyphFieldKind fieldKind = GlyphFieldKind.MTSDF;
		/// <summary>Font GUID + importer timestamp + bake params; mismatch ⇒ "bake out of date".</summary>
		public string sourceHash;

		public FaceRecord[] faces = Array.Empty<FaceRecord>();
		public GlyphRecord[] glyphs = Array.Empty<GlyphRecord>();
		public KerningRecord[] kerning = Array.Empty<KerningRecord>();

		[NonSerialized] private Dictionary<uint, int> primaryIndexByCp;
		[NonSerialized] private Dictionary<long, int> indexByFaceCp;
		[NonSerialized] private Dictionary<long, float> kerningByFacePair;
		[NonSerialized] private bool lookupsBuilt;

		private static long Key(int face, uint cp) => ((long) face << 32) | cp;
		private static long PairKey(int face, uint left, uint right) =>
			((long) face << 42) | ((long) (left & 0x1FFFFF) << 21) | (right & 0x1FFFFF);

		private void EnsureLookups() {
			if (lookupsBuilt) return;
			primaryIndexByCp = new Dictionary<uint, int>(glyphs.Length);
			indexByFaceCp = new Dictionary<long, int>(glyphs.Length);
			for (int i = 0; i < glyphs.Length; i++) {
				GlyphRecord g = glyphs[i];
				indexByFaceCp[Key(g.face, g.codepoint)] = i;
				if (g.face == 0 && !primaryIndexByCp.ContainsKey(g.codepoint))
					primaryIndexByCp[g.codepoint] = i;
			}
			kerningByFacePair = new Dictionary<long, float>(kerning.Length);
			for (int i = 0; i < kerning.Length; i++) {
				KerningRecord k = kerning[i];
				kerningByFacePair[PairKey(k.face, k.left, k.right)] = k.advance;
			}
			lookupsBuilt = true;
		}

		/// <summary>Editor-only: call after mutating arrays so the next query rebuilds the lookups.</summary>
		public void InvalidateLookups() => lookupsBuilt = false;

		/// <summary>First matching glyph for a code point across the face chain (primary first).</summary>
		public bool TryGetGlyph(uint codepoint, out GlyphRecord record) {
			EnsureLookups();
			if (primaryIndexByCp.TryGetValue(codepoint, out int idx)) {
				record = glyphs[idx];
				return true;
			}
			for (int face = 1; face < faces.Length; face++)
				if (indexByFaceCp.TryGetValue(Key(face, codepoint), out idx)) {
					record = glyphs[idx];
					return true;
				}
			record = default;
			return false;
		}

		public float GetKerning(int face, uint left, uint right) {
			EnsureLookups();
			return kerningByFacePair.TryGetValue(PairKey(face, left, right), out float a) ? a : 0f;
		}

		public FaceMetrics MetricsOf(int face) =>
			(uint) face < (uint) faces.Length ? faces[face].metrics : default;

		private void OnEnable() => lookupsBuilt = false;
	}
}
