using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>Immutable style state for a run of characters, produced by <see cref="MarkupParser"/>.</summary>
	public struct StyleState {

		public float4 Color;
		public bool HasGradient;
		public GradientScope GradientScope;
		public float4 GradientTopLeft, GradientTopRight, GradientBottomLeft, GradientBottomRight;

		public float SizeMultiplier;
		public float AbsoluteSizePx; // 0 = unset; when > 0 it overrides SizeMultiplier
		public FontSynthesis Synthesis;
		public TextCase Case;
		public float LetterSpacingEm;
		public float BaselineShift;   // in em, positive = up (superscript)
		public float ScaleMultiplier; // sub/sup shrink

		public bool Underline;
		public bool Strikethrough;
		public bool HasMark;
		public float4 MarkColor;

		public BuiltinEffect SpanEffect; // animated effect applied to this run (None = off)

		public bool HasOutline;
		public float4 OutlineColor;
		public float OutlineWidth;    // SDF units, ~0.05..0.4

		public bool HasGlow;
		public float4 GlowColor;
		public float GlowRadius;      // SDF units, ~0.2..1
		public float GlowIntensity;

		public bool HasShadow;
		public float4 ShadowColor;
		public float2 ShadowOffsetEm; // offset in em (x right, y up)
		public float ShadowSoftness;  // SDF units

		public int LinkId;            // -1 = none, otherwise index into the parsed link table

		public static StyleState Default => new StyleState {
			Color = new float4(1, 1, 1, 1),
			SizeMultiplier = 1f,
			ScaleMultiplier = 1f,
			Synthesis = FontSynthesis.None,
			Case = TextCase.None,
			LinkId = -1
		};

		public bool StyleEquals(in StyleState o) {
			return Color.Equals(o.Color)
				&& HasGradient == o.HasGradient
				&& GradientScope == o.GradientScope
				&& GradientTopLeft.Equals(o.GradientTopLeft) && GradientTopRight.Equals(o.GradientTopRight)
				&& GradientBottomLeft.Equals(o.GradientBottomLeft) && GradientBottomRight.Equals(o.GradientBottomRight)
				&& Mathf.Approximately(SizeMultiplier, o.SizeMultiplier)
				&& Mathf.Approximately(AbsoluteSizePx, o.AbsoluteSizePx)
				&& Synthesis == o.Synthesis
				&& Case == o.Case
				&& Mathf.Approximately(LetterSpacingEm, o.LetterSpacingEm)
				&& Mathf.Approximately(BaselineShift, o.BaselineShift)
				&& Mathf.Approximately(ScaleMultiplier, o.ScaleMultiplier)
				&& Underline == o.Underline
				&& Strikethrough == o.Strikethrough
				&& HasMark == o.HasMark
				&& MarkColor.Equals(o.MarkColor)
				&& SpanEffect == o.SpanEffect
				&& HasOutline == o.HasOutline && OutlineColor.Equals(o.OutlineColor) && Mathf.Approximately(OutlineWidth, o.OutlineWidth)
				&& HasGlow == o.HasGlow && GlowColor.Equals(o.GlowColor)
					&& Mathf.Approximately(GlowRadius, o.GlowRadius) && Mathf.Approximately(GlowIntensity, o.GlowIntensity)
				&& HasShadow == o.HasShadow && ShadowColor.Equals(o.ShadowColor)
					&& ShadowOffsetEm.Equals(o.ShadowOffsetEm) && Mathf.Approximately(ShadowSoftness, o.ShadowSoftness)
				&& LinkId == o.LinkId;
		}
	}

	/// <summary>A contiguous run of <see cref="StyleState"/> over the stripped display text.</summary>
	public struct StyleSpan {
		public int Start;
		public int Length;
		public StyleState Style;
		public int End => Start + Length;
	}

	/// <summary>Inline sprite / action-glyph insertion point in the stripped display text.</summary>
	public struct InlineInsert {
		public int CharIndex;        // position in the stripped text (occupies one placeholder char)
		public bool IsActionGlyph;   // true: resolve via ITextGlyphSource; false: named sprite
		public string Name;          // sprite name or input action name
	}

	/// <summary>A clickable region declared by a &lt;link&gt; tag. Char range is in stripped text.</summary>
	public struct LinkRegion {
		public string Id;
		public int Start;
		public int Length;
		public int End => Start + Length;
	}
}
