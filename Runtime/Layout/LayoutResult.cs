using System.Collections.Generic;
using Unity.Mathematics;

namespace Sperlich.Text {

	/// <summary>One glyph placed by the layout engine, in local text space (origin at the layout rect's top-left, +y up).</summary>
	public struct PositionedGlyph {
		public int SourceIndex;   // index into the stripped display text
		public int SpanIndex;     // index into the style span list
		public int LineIndex;
		public float2 Pen;        // pen position at the baseline before bearing
		public float FontSize;    // resolved size for this glyph
		public float UnitScale;   // fontSize / owning face sampling size; converts glyph metrics to local units
		public float Rotation;    // radians, non-zero only for curved baselines
		public float4 Color;
		public GlyphData Glyph;
		public bool Visible;      // false when clipped / not yet revealed
		public bool IsInlineObject; // sprite / action glyph placeholder
	}

	/// <summary>Metrics for one visual line.</summary>
	public struct LineInfo {
		public int FirstGlyph;
		public int GlyphCount;
		public float Width;
		public float Ascent;
		public float Descent;
		public float BaselineY;   // local space, negative going down from the top
		public bool EndsWithHardBreak;
	}

	/// <summary>Full output of <see cref="TextLayoutEngine.Layout"/>. Buffers are owned by the engine and reused.</summary>
	public sealed class LayoutResult {
		public readonly List<PositionedGlyph> Glyphs = new(128);
		public readonly List<LineInfo> Lines = new(8);

		public float2 Size;           // measured content size (width, height), always positive
		public float ResolvedFontSize;
		public bool Truncated;        // ellipsis / clip removed content
		public bool HasUnresolvedGlyphs; // at least one glyph is still a placeholder
		public int GlyphStoreVersion;

		public void Clear() {
			Glyphs.Clear();
			Lines.Clear();
			Size = default;
			ResolvedFontSize = 0f;
			Truncated = false;
			HasUnresolvedGlyphs = false;
		}
	}
}
