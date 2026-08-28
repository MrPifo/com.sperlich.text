using Unity.Mathematics;

namespace Sperlich.Text {

	/// <summary>
	/// Everything the layout + mesh stage needs about one resolved glyph, independent of
	/// which font in the fallback chain produced it. Metrics are in sampling-point-size units.
	/// </summary>
	public struct GlyphData {

		/// <summary>Index of the owning face in the active fallback chain (0 = primary).</summary>
		public int FaceIndex;

		/// <summary>FontEngine glyph index inside that face.</summary>
		public uint GlyphIndex;

		/// <summary>Unicode code point this was resolved from.</summary>
		public uint Unicode;

		/// <summary>Horizontal pen advance.</summary>
		public float Advance;

		/// <summary>Glyph quad size (width, height) excluding SDF padding.</summary>
		public float2 Size;

		/// <summary>Left / top bearing from the pen position at the baseline.</summary>
		public float2 Bearing;

		/// <summary>Atlas rect in pixels (x, y, width, height), padding included.</summary>
		public float4 AtlasRect;

		/// <summary>SDF padding in pixels baked around the glyph inside <see cref="AtlasRect"/>.</summary>
		public float Padding;

		/// <summary>True once the real atlas entry exists; false while a tofu placeholder is shown.</summary>
		public bool IsResolved;

		/// <summary>True when this glyph carries no visible ink (space, control char).</summary>
		public bool IsWhitespace;

		public static GlyphData Whitespace(uint unicode, float advance) => new GlyphData {
			Unicode = unicode,
			Advance = advance,
			IsWhitespace = true,
			IsResolved = true
		};
	}
}
