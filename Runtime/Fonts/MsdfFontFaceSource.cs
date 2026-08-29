using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// The baked-atlas <see cref="IFontFaceSource"/>: serves glyphs from a pre-generated
	/// <see cref="MsdfFontData"/> (MTSDF texture + metrics). Fully static — no runtime rasterisation,
	/// no font bytes, no TMP. Code points that were not baked are reported missing, so
	/// <see cref="GlyphStore"/> draws a tofu box once and never retries (its static-source guard).
	/// Main-thread only.
	/// </summary>
	public sealed class MsdfFontFaceSource : IFontFaceSource {

		private readonly FontDefinition definition;
		private MsdfFontData data;

		public MsdfFontFaceSource(FontDefinition definition) {
			this.definition = definition;
			data = definition != null ? definition.bakedData : null;
		}

		public FontDefinition Definition => definition;

		public bool IsReady =>
			data != null && data.atlas != null && data.glyphs != null && data.glyphs.Length > 0;

		public bool SupportsDynamicGeneration => false;

		public GlyphFieldKind FieldKind => data != null ? data.fieldKind : GlyphFieldKind.MTSDF;

		public Texture AtlasTexture => data != null ? data.atlas : null;

		public int AtlasSize => data != null ? data.atlasSize : 0;

		/// <summary>
		/// Half the baked msdfgen pixel range. The stored 0..1 field spans the full range, so its
		/// half-spread is <c>pixelRange / 2</c> atlas pixels — the same quantity
		/// <see cref="FontAccess.DistanceRange"/> reports as <c>atlasPadding + 1</c>. The mesh builder
		/// folds it into the per-vertex screen-space AA scale.
		/// </summary>
		public float DistanceRange => data != null ? Mathf.Max(1f, data.pixelRange * 0.5f) : 1f;

		public FaceMetrics GetMetrics(int faceIndex) => data != null ? data.MetricsOf(faceIndex) : default;

		public FaceMetrics PrimaryMetrics => GetMetrics(0);

		public bool TryGetGlyph(uint unicode, out GlyphEntry entry) {
			if (data != null && data.TryGetGlyph(unicode, out MsdfFontData.GlyphRecord r)) {
				entry = new GlyphEntry {
					FaceIndex = r.face,
					GlyphIndex = r.glyphIndex,
					Advance = r.advance,
					Width = r.width,
					Height = r.height,
					BearingX = r.bearingX,
					BearingY = r.bearingY,
					RectX = r.rectX,
					RectY = r.rectY,
					RectW = r.rectW,
					RectH = r.rectH,
					Padding = r.padding
				};
				return true;
			}
			entry = default;
			return false;
		}

		/// <summary>Static atlas — nothing to add.</summary>
		public bool TryAddGlyphs(uint[] unicodes) => false;

		public float GetKerning(int faceIndex, uint firstGlyph, uint secondGlyph) =>
			data != null ? data.GetKerning(faceIndex, firstGlyph, secondGlyph) : 0f;

		/// <summary>Static atlas — nothing to clear.</summary>
		public void ClearDynamicData() { }

		public void Dispose() => data = null;
	}
}
