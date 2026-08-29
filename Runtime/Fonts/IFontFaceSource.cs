using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// The atlas backend behind <see cref="GlyphStore"/>. Two implementations:
	/// <see cref="FontAccess"/> (TMP dynamic SDF, adds glyphs on demand) and <c>MsdfFontFaceSource</c>
	/// (a pre-baked MTSDF atlas, fully static). Everything above <see cref="GlyphStore"/> talks only to
	/// this interface and to <see cref="GlyphEntry"/> / <see cref="GlyphData"/> — no backend type leaks
	/// through. Main-thread only.
	/// </summary>
	public interface IFontFaceSource : System.IDisposable {

		/// <summary>The authoring asset this source was built from.</summary>
		FontDefinition Definition { get; }

		/// <summary>True once the primary face is usable.</summary>
		bool IsReady { get; }

		/// <summary>
		/// True when <see cref="TryAddGlyphs"/> can rasterise new code points at runtime (TMP path).
		/// False for a baked atlas — <see cref="GlyphStore"/> then serves unbaked code points a tofu box
		/// once and never retries.
		/// </summary>
		bool SupportsDynamicGeneration { get; }

		/// <summary>Field kind actually stored in <see cref="AtlasTexture"/> (drives the shader keyword).</summary>
		GlyphFieldKind FieldKind { get; }

		/// <summary>The GPU atlas the mesh samples. Square (<see cref="AtlasSize"/> on both axes).</summary>
		Texture AtlasTexture { get; }

		/// <summary>Atlas edge length in pixels.</summary>
		int AtlasSize { get; }

		/// <summary>
		/// Screen-space normalisation of the distance field, in atlas pixels: how many pixels the raw
		/// 0..1 field's half-spread covers. Backend-specific (TMP: <c>atlasPadding + 1</c>; MTSDF: the
		/// baked pixel range). The mesh builder folds this into the per-vertex AA scale.
		/// </summary>
		float DistanceRange { get; }

		/// <summary>Face metrics for chain entry <paramref name="faceIndex"/> (0 = primary), sampling-point-size units.</summary>
		FaceMetrics GetMetrics(int faceIndex);

		/// <summary>Shorthand for <c>GetMetrics(0)</c>.</summary>
		FaceMetrics PrimaryMetrics { get; }

		/// <summary>
		/// Looks up an already-present code point across the chain. False when it is not in the atlas
		/// (yet, for a dynamic source; ever, for a baked one).
		/// </summary>
		bool TryGetGlyph(uint unicode, out GlyphEntry entry);

		/// <summary>Rasterises a batch into the atlas. No-op returning false for a static source.</summary>
		bool TryAddGlyphs(uint[] unicodes);

		/// <summary>Kerning advance in sampling-point-size units (0 when unavailable).</summary>
		float GetKerning(int faceIndex, uint firstGlyph, uint secondGlyph);

		/// <summary>Drops dynamically added glyphs so the atlas can be repopulated. No-op for a static source.</summary>
		void ClearDynamicData();
	}
}
