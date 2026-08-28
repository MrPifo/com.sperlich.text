using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>Everything the layout engine needs for one pass. A value type so callers can tweak and re-run cheaply.</summary>
	public struct TextLayoutInput {
		public string Text;
		public List<StyleSpan> Spans;
		public GlyphStore Glyphs;

		public float BaseFontSize;
		public Vector2 RectSize;        // <= 0 on an axis means unconstrained
		public TextAlign Align;
		public TextVerticalAlign VerticalAlign;
		public TextWrap Wrap;
		public TextOverflow Overflow;

		public float LineSpacingMul;    // multiple of the natural line height
		public float ParagraphSpacingMul;
		public float ExtraTrackingEm;   // global tracking on top of per-span cspace
		public bool AutoUppercaseTracking;

		public CurvedBaseline Curve;    // null = straight baseline

		public static TextLayoutInput Default => new TextLayoutInput {
			BaseFontSize = 32f,
			Align = TextAlign.Left,
			VerticalAlign = TextVerticalAlign.Top,
			Wrap = TextWrap.Word,
			Overflow = TextOverflow.Overflow,
			LineSpacingMul = 1f,
			ParagraphSpacingMul = 1f,
			AutoUppercaseTracking = true
		};
	}

	/// <summary>
	/// Turns stripped text + style spans into positioned glyphs and line metrics: straight and curved
	/// baselines, word wrap (UAX #14 subset), alignment, auto line / paragraph spacing, clip / ellipsis
	/// overflow. Auto-size and scale-to-fit are driven from outside via <see cref="AutoSizeSolver"/>.
	/// One instance per renderer; the result buffers are reused between passes.
	/// </summary>
	public sealed class TextLayoutEngine {

		private readonly LayoutResult result = new();
		private readonly List<WorkGlyph> work = new(128);

		private struct WorkGlyph {
			public int Source;
			public int Span;
			public uint Unicode;
			public float FontSize;
			public float UnitScale;      // fontSize / face sampling size
			public float BaselineShiftPx;
			public float TrackingPx;     // added to the pen after this glyph (tracking + kerning)
			public float Ascent;         // local units
			public float Descent;        // local units, positive
			public GlyphData Data;
			public float4 Color;
			public bool IsInlineObject;
			public bool MandatoryBreakAfter;
			public bool BreakBefore;
			public bool IsSpace;
		}

		public LayoutResult Layout(in TextLayoutInput input) {
			result.Clear();
			work.Clear();

			result.ResolvedFontSize = input.BaseFontSize;
			if (input.Glyphs == null || string.IsNullOrEmpty(input.Text) || input.BaseFontSize <= 0f) {
				return result;
			}
			result.GlyphStoreVersion = input.Glyphs.Version;

			BuildWorkGlyphs(input);
			if (work.Count == 0) return result;

			BreakIntoLines(input);
			AlignLines(input);
			PlaceVertically(input);
			ApplyOverflow(input);
			if (input.Curve != null && input.Curve.IsValid) ApplyCurve(input);

			return result;
		}

		// -- stage 1: resolve every character into a work glyph -----------------------------------

		private void BuildWorkGlyphs(in TextLayoutInput input) {
			string text = input.Text;
			List<StyleSpan> spans = input.Spans;
			GlyphStore store = input.Glyphs;

			int spanIndex = 0;
			char prevChar = '\0';
			uint prevGlyphIndex = 0;
			int prevFace = -1;

			for (int i = 0; i < text.Length; i++) {
				char c = text[i];
				if (c == '\r') { prevChar = c; continue; }

				while (spanIndex + 1 < spans.Count && i >= spans[spanIndex].End) spanIndex++;
				StyleState style = SpanAt(spans, spanIndex, i);

				bool mandatory = LineBreaker.IsMandatoryBreak(c);
				bool inlineObject = c == '￼';
				bool space = c == ' ' || c == '\t' || c == ' ' || c == '　' || c == '​';

				char shaped = ApplyCase(c, style.Case);
				uint unicode = shaped;

				float fontSize = ResolveFontSize(input.BaseFontSize, style);

				FaceMetrics fm;
				GlyphData data;
				if (inlineObject) {
					fm = store.Fonts.PrimaryMetrics;
					float box = fontSize;
					data = new GlyphData {
						Unicode = unicode,
						Advance = box,
						Size = new float2(box, box),
						Bearing = new float2(0f, box * 0.8f),
						IsResolved = true
					};
				} else if (LineBreaker.IsSoftHyphen(c)) {
					fm = store.Fonts.PrimaryMetrics;
					data = GlyphData.Whitespace(unicode, 0f);
				} else {
					data = store.GetOrRequest(unicode);
					if (data.IsResolved == false) result.HasUnresolvedGlyphs = true;
					fm = store.Fonts.GetMetrics(math.max(0, data.FaceIndex));
				}

				float sampling = fm.IsValid ? fm.SamplingPointSize : store.Fonts.Definition.samplingPointSize;
				float unitScale = sampling > 0f ? fontSize / sampling : 0f;
				float ascent = fm.IsValid ? fm.AscentLine * unitScale : fontSize * 0.8f;
				float descent = fm.IsValid ? -fm.DescentLine * unitScale : fontSize * 0.2f;

				float trackingEm = style.LetterSpacingEm + input.ExtraTrackingEm;
				if (input.AutoUppercaseTracking && (style.Case == TextCase.Upper || style.Case == TextCase.SmallCaps)) {
					trackingEm += fontSize >= input.BaseFontSize * 1.4f
						? TypographyDefaults.UppercaseHeadingTrackingEm
						: TypographyDefaults.UppercaseWordTrackingEm;
				}

				float kerning = 0f;
				if (!inlineObject && data.IsResolved && data.FaceIndex == prevFace && prevGlyphIndex != 0 && data.GlyphIndex != 0) {
					kerning = store.Fonts.GetKerning(data.FaceIndex, prevGlyphIndex, data.GlyphIndex) * unitScale;
				}

				work.Add(new WorkGlyph {
					Source = i,
					Span = spanIndex,
					Unicode = unicode,
					FontSize = fontSize,
					UnitScale = unitScale,
					BaselineShiftPx = style.BaselineShift * fontSize,
					TrackingPx = trackingEm * fontSize + kerning,
					Ascent = ascent,
					Descent = descent,
					Data = data,
					Color = style.Color,
					IsInlineObject = inlineObject,
					MandatoryBreakAfter = mandatory,
					BreakBefore = LineBreaker.CanBreakBetween(prevChar, c),
					IsSpace = space
				});

				prevChar = c;
				prevGlyphIndex = data.GlyphIndex;
				prevFace = data.FaceIndex;
			}
		}

		private static StyleState SpanAt(List<StyleSpan> spans, int spanIndex, int charIndex) {
			if (spans == null || spans.Count == 0) return StyleState.Default;
			if (spanIndex >= 0 && spanIndex < spans.Count) {
				StyleSpan s = spans[spanIndex];
				if (charIndex >= s.Start && charIndex < s.End) return s.Style;
			}
			for (int k = 0; k < spans.Count; k++) {
				if (charIndex >= spans[k].Start && charIndex < spans[k].End) return spans[k].Style;
			}
			return spans[spans.Count - 1].Style;
		}

		private static float ResolveFontSize(float baseSize, in StyleState style) {
			float s = style.AbsoluteSizePx > 0f ? style.AbsoluteSizePx : baseSize * math.max(0.01f, style.SizeMultiplier);
			return s * math.max(0.05f, style.ScaleMultiplier);
		}

		private static char ApplyCase(char c, TextCase mode) {
			return mode switch {
				TextCase.Upper => char.ToUpperInvariant(c),
				TextCase.SmallCaps => char.ToUpperInvariant(c),
				TextCase.Lower => char.ToLowerInvariant(c),
				_ => c
			};
		}

		// -- stage 2: break the work glyphs into lines ------------------------------------------

		private void BreakIntoLines(in TextLayoutInput input) {
			float maxWidth = input.RectSize.x > 0f ? input.RectSize.x : float.MaxValue;
			bool wrap = input.Wrap != TextWrap.NoWrap && maxWidth < float.MaxValue;

			int lineStart = 0;
			float pen = 0f;
			int lastBreak = -1;

			for (int i = 0; i < work.Count; i++) {
				WorkGlyph g = work[i];

				if (wrap && g.BreakBefore && i > lineStart) lastBreak = i;

				float advance = g.Data.Advance * g.UnitScale + math.max(0f, g.TrackingPx);

				if (wrap && !g.IsSpace && pen + advance > maxWidth && i > lineStart) {
					int breakAt = lastBreak > lineStart
						? lastBreak
						: (input.Wrap == TextWrap.WordThenChar ? i : -1);

					if (breakAt > lineStart) {
						EmitLineRange(lineStart, breakAt, false);
						lineStart = SkipLeadingSpaces(breakAt);
						i = lineStart - 1;
						pen = 0f;
						lastBreak = -1;
						continue;
					}
				}

				pen += advance;

				if (g.MandatoryBreakAfter) {
					EmitLineRange(lineStart, i + 1, true);
					lineStart = i + 1;
					pen = 0f;
					lastBreak = -1;
				}
			}

			if (lineStart < work.Count) EmitLineRange(lineStart, work.Count, true);
		}

		private int SkipLeadingSpaces(int from) {
			int i = from;
			while (i < work.Count && work[i].IsSpace && !work[i].MandatoryBreakAfter) i++;
			return i;
		}

		private void EmitLineRange(int start, int endExclusive, bool hardBreak) {
			int firstGlyph = result.Glyphs.Count;
			float pen = 0f;
			float ascent = 0f;
			float descent = 0f;

			for (int i = start; i < endExclusive; i++) {
				WorkGlyph w = work[i];
				ascent = math.max(ascent, w.Ascent);
				descent = math.max(descent, w.Descent);

				result.Glyphs.Add(new PositionedGlyph {
					SourceIndex = w.Source,
					SpanIndex = w.Span,
					LineIndex = result.Lines.Count,
					Pen = new float2(pen, w.BaselineShiftPx),
					FontSize = w.FontSize,
					UnitScale = w.UnitScale,
					Color = w.Color,
					Glyph = w.Data,
					Visible = !w.IsInlineObject || w.Data.IsResolved,
					IsInlineObject = w.IsInlineObject
				});
				pen += w.Data.Advance * w.UnitScale + w.TrackingPx;
			}

			if (ascent <= 0f) ascent = result.ResolvedFontSize * 0.8f;
			if (descent <= 0f) descent = result.ResolvedFontSize * 0.2f;

			int lastGlyph = result.Glyphs.Count;
			result.Lines.Add(new LineInfo {
				FirstGlyph = firstGlyph,
				GlyphCount = lastGlyph - firstGlyph,
				Ascent = ascent,
				Descent = descent,
				Width = TrimmedWidth(firstGlyph, lastGlyph),
				EndsWithHardBreak = hardBreak
			});
		}

		private float TrimmedWidth(int firstGlyph, int endGlyph) {
			int last = endGlyph - 1;
			while (last >= firstGlyph && IsSpaceGlyph(result.Glyphs[last].Glyph.Unicode)) last--;
			if (last < firstGlyph) return 0f;
			PositionedGlyph g = result.Glyphs[last];
			return g.Pen.x + g.Glyph.Advance * g.UnitScale;
		}

		private static bool IsSpaceGlyph(uint u) => u == ' ' || u == '\t' || u == 0x00A0 || u == 0x3000 || u == 0x200B;

		// -- stage 4: horizontal alignment -------------------------------------------------------

		private void AlignLines(in TextLayoutInput input) {
			float rectW = input.RectSize.x > 0f ? input.RectSize.x : MaxLineWidth();
			bool haveRect = input.RectSize.x > 0f;

			for (int li = 0; li < result.Lines.Count; li++) {
				LineInfo line = result.Lines[li];
				int end = line.FirstGlyph + line.GlyphCount;

				if ((input.Align == TextAlign.Justified || input.Align == TextAlign.Flush) && haveRect) {
					bool lastLine = line.EndsWithHardBreak;
					if (input.Align == TextAlign.Flush || !lastLine) {
						JustifyLine(li, rectW);
						continue;
					}
				}

				float offset;
				if (input.Align == TextAlign.GeometryCenter) {
					float ink = InkWidth(line, out float inkMin);
					offset = (rectW - ink) * 0.5f - inkMin;
				} else {
					offset = input.Align switch {
						TextAlign.Center => (rectW - line.Width) * 0.5f,
						TextAlign.Right => rectW - line.Width,
						_ => 0f
					};
				}
				if (math.abs(offset) < 1e-4f) continue;

				for (int gi = line.FirstGlyph; gi < end; gi++) {
					PositionedGlyph g = result.Glyphs[gi];
					g.Pen.x += offset;
					result.Glyphs[gi] = g;
				}
			}
		}

		/// <summary>Widens the gaps after breaking spaces on line <paramref name="li"/> so its content fills <paramref name="targetW"/>.</summary>
		private void JustifyLine(int li, float targetW) {
			LineInfo line = result.Lines[li];
			int end = line.FirstGlyph + line.GlyphCount;
			int lastInk = end - 1;
			while (lastInk >= line.FirstGlyph && IsSpaceGlyph(result.Glyphs[lastInk].Glyph.Unicode)) lastInk--;
			if (lastInk <= line.FirstGlyph) return;

			int gaps = 0;
			for (int gi = line.FirstGlyph; gi < lastInk; gi++) {
				if (IsSpaceGlyph(result.Glyphs[gi].Glyph.Unicode)) gaps++;
			}
			if (gaps == 0) return;

			float slack = targetW - line.Width;
			if (slack <= 0f) return;
			float per = slack / gaps;

			float shift = 0f;
			for (int gi = line.FirstGlyph; gi <= lastInk; gi++) {
				PositionedGlyph g = result.Glyphs[gi];
				g.Pen.x += shift;
				result.Glyphs[gi] = g;
				if (gi < lastInk && IsSpaceGlyph(g.Glyph.Unicode)) shift += per;
			}
			line.Width = targetW;
			result.Lines[li] = line;
		}

		/// <summary>Ink extent of a line: from the left edge of the first glyph to the right edge of the last.</summary>
		private float InkWidth(in LineInfo line, out float inkMin) {
			int end = line.FirstGlyph + line.GlyphCount;
			inkMin = 0f;
			int first = line.FirstGlyph;
			while (first < end && IsSpaceGlyph(result.Glyphs[first].Glyph.Unicode)) first++;
			int last = end - 1;
			while (last >= line.FirstGlyph && IsSpaceGlyph(result.Glyphs[last].Glyph.Unicode)) last--;
			if (first > last) return 0f;

			PositionedGlyph gf = result.Glyphs[first];
			PositionedGlyph gl = result.Glyphs[last];
			inkMin = gf.Pen.x + gf.Glyph.Bearing.x * gf.UnitScale;
			float inkMax = gl.Pen.x + (gl.Glyph.Bearing.x + gl.Glyph.Size.x) * gl.UnitScale;
			return inkMax - inkMin;
		}

		private float MaxLineWidth() {
			float w = 0f;
			for (int i = 0; i < result.Lines.Count; i++) w = math.max(w, result.Lines[i].Width);
			return w;
		}

		// -- stage 5: vertical placement -------------------------------------------------------

		private void PlaceVertically(in TextLayoutInput input) {
			float lineSpacing = math.max(0.1f, input.LineSpacingMul);
			float paragraphExtra = input.BaseFontSize
				* (TypographyDefaults.ParagraphSpacing - TypographyDefaults.LineSpacing)
				* math.max(0f, input.ParagraphSpacingMul);

			float y = 0f;
			for (int li = 0; li < result.Lines.Count; li++) {
				LineInfo line = result.Lines[li];
				float natural = math.max(line.Ascent + line.Descent, input.BaseFontSize);
				float lineHeight = natural * TypographyDefaults.LineSpacing * lineSpacing;

				float baselineY = y - line.Ascent;
				line.BaselineY = baselineY;
				result.Lines[li] = line;

				int end = line.FirstGlyph + line.GlyphCount;
				for (int gi = line.FirstGlyph; gi < end; gi++) {
					PositionedGlyph g = result.Glyphs[gi];
					g.Pen.y += baselineY;
					result.Glyphs[gi] = g;
				}

				y -= lineHeight;
				if (line.EndsWithHardBreak && li + 1 < result.Lines.Count) y -= paragraphExtra;
			}

			float totalHeight = -y;
			float width = math.max(input.RectSize.x > 0f ? input.RectSize.x : 0f, MaxLineWidth());
			result.Size = new float2(width, math.max(0f, totalHeight));

			ApplyVerticalAlign(input, totalHeight);
		}

		private void ApplyVerticalAlign(in TextLayoutInput input, float totalHeight) {
			if (input.RectSize.y <= 0f || input.VerticalAlign == TextVerticalAlign.Top) return;
			float slack = input.RectSize.y - totalHeight;
			float shift = input.VerticalAlign switch {
				TextVerticalAlign.Middle => -slack * 0.5f,
				TextVerticalAlign.Bottom => -slack,
				_ => 0f
			};
			if (math.abs(shift) < 1e-4f) return;

			for (int i = 0; i < result.Glyphs.Count; i++) {
				PositionedGlyph g = result.Glyphs[i];
				g.Pen.y += shift;
				result.Glyphs[i] = g;
			}
			for (int i = 0; i < result.Lines.Count; i++) {
				LineInfo l = result.Lines[i];
				l.BaselineY += shift;
				result.Lines[i] = l;
			}
		}

		// -- stage 6: overflow -------------------------------------------------------

		private void ApplyOverflow(in TextLayoutInput input) {
			if (input.RectSize.y <= 0f) return;
			if (input.Overflow is TextOverflow.Overflow or TextOverflow.Scroll or TextOverflow.ScaleToFit) return;

			float bottomLimit = -input.RectSize.y;

			if (input.Overflow == TextOverflow.Clip) {
				for (int i = 0; i < result.Glyphs.Count; i++) {
					PositionedGlyph g = result.Glyphs[i];
					if (g.Pen.y < bottomLimit) {
						g.Visible = false;
						result.Glyphs[i] = g;
						result.Truncated = true;
					}
				}
				return;
			}

			// Ellipsis
			int lastVisibleLine = -1;
			for (int li = 0; li < result.Lines.Count; li++) {
				if (result.Lines[li].BaselineY - result.Lines[li].Descent >= bottomLimit) lastVisibleLine = li;
				else break;
			}
			if (lastVisibleLine < 0) lastVisibleLine = 0;
			if (lastVisibleLine + 1 >= result.Lines.Count) return;

			for (int li = lastVisibleLine + 1; li < result.Lines.Count; li++) {
				LineInfo l = result.Lines[li];
				for (int gi = l.FirstGlyph; gi < l.FirstGlyph + l.GlyphCount; gi++) {
					PositionedGlyph g = result.Glyphs[gi];
					g.Visible = false;
					result.Glyphs[gi] = g;
				}
			}
			result.Truncated = true;
			AppendEllipsis(input, result.Lines[lastVisibleLine], lastVisibleLine);
		}

		private void AppendEllipsis(in TextLayoutInput input, LineInfo line, int lineIndex) {
			GlyphData dot = input.Glyphs.GetOrRequest('…');
			if (dot.IsResolved == false) dot = input.Glyphs.GetOrRequest('.');

			float unit = result.ResolvedFontSize / math.max(1f, input.Glyphs.Fonts.Definition.samplingPointSize);
			float ellipsisAdvance = dot.Advance * unit;
			float limit = input.RectSize.x > 0f ? input.RectSize.x : result.Size.x;

			int end = line.FirstGlyph + line.GlyphCount - 1;
			int cut = end;
			while (cut >= line.FirstGlyph) {
				PositionedGlyph g = result.Glyphs[cut];
				if (!IsSpaceGlyph(g.Glyph.Unicode) && g.Pen.x + ellipsisAdvance <= limit) break;
				g.Visible = false;
				result.Glyphs[cut] = g;
				cut--;
			}

			float x = 0f;
			float y = line.BaselineY;
			float4 color = new float4(1, 1, 1, 1);
			int spanIndex = 0;
			if (cut >= line.FirstGlyph) {
				PositionedGlyph prev = result.Glyphs[cut];
				x = prev.Pen.x + prev.Glyph.Advance * prev.UnitScale;
				y = prev.Pen.y;
				color = prev.Color;
				spanIndex = prev.SpanIndex;
			}

			result.Glyphs.Add(new PositionedGlyph {
				SourceIndex = -1,
				SpanIndex = spanIndex,
				LineIndex = lineIndex,
				Pen = new float2(x, y),
				FontSize = result.ResolvedFontSize,
				UnitScale = unit,
				Color = color,
				Glyph = dot,
				Visible = true
			});
		}

		// -- stage 7: curved baseline -------------------------------------------------------

		private void ApplyCurve(in TextLayoutInput input) {
			CurvedBaseline curve = input.Curve;
			for (int i = 0; i < result.Glyphs.Count; i++) {
				PositionedGlyph g = result.Glyphs[i];
				float centre = g.Pen.x + g.Glyph.Advance * g.UnitScale * 0.5f;
				curve.Evaluate(centre, out float2 pos, out float angle);
				float2 normal = new float2(-math.sin(angle), math.cos(angle));
				g.Pen = pos + normal * g.Pen.y;
				g.Rotation = angle;
				result.Glyphs[i] = g;
			}
		}
	}
}
