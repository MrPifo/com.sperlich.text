namespace Sperlich.Text {

	/// <summary>
	/// One resolved glyph as handed across the <see cref="IFontFaceSource"/> seam, independent of the
	/// backend (TMP dynamic SDF or the baked MTSDF atlas). Plain blittable fields only so the same shape
	/// can later feed a Burst job. All metric values are in sampling-point-size units; the atlas rect is
	/// in atlas pixels with a <b>bottom-left</b> origin and the SDF spread already folded into it, so
	/// <see cref="GlyphStore"/> holds no backend-specific rect math.
	/// </summary>
	public struct GlyphEntry {

		/// <summary>Index of the owning face in the fallback chain (0 = primary).</summary>
		public int FaceIndex;

		/// <summary>Backend glyph id inside that face (FontEngine index, or a bake-local id).</summary>
		public uint GlyphIndex;

		/// <summary>Horizontal pen advance.</summary>
		public float Advance;

		/// <summary>Tight glyph size (no spread), width then height.</summary>
		public float Width, Height;

		/// <summary>Left / top bearing from the baseline pen position.</summary>
		public float BearingX, BearingY;

		/// <summary>Atlas rect in pixels, bottom-left origin, SDF spread included on every side.</summary>
		public float RectX, RectY, RectW, RectH;

		/// <summary>Spread in pixels baked into each side of the rect.</summary>
		public float Padding;
	}
}
