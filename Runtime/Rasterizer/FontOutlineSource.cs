using System;
using System.Collections.Generic;
using System.IO;
using Typography.OpenFont;
using Unity.Mathematics;

namespace Sperlich.Text.Rasterizer {

	/// <summary>Segment kind inside a <see cref="RawContour"/>.</summary>
	public enum RawSegmentKind { Line, Quadratic, Cubic }

	/// <summary>
	/// One path segment in font units. The start point is the previous segment's <see cref="End"/>
	/// (or the contour's <see cref="RawContour.Start"/> for the first). <see cref="C1"/> is the control
	/// point for <see cref="RawSegmentKind.Quadratic"/>; <see cref="C1"/> and <see cref="C2"/> for
	/// <see cref="RawSegmentKind.Cubic"/>.
	/// </summary>
	public struct RawSegment {
		public RawSegmentKind Kind;
		public float2 C1;
		public float2 C2;
		public float2 End;
	}

	/// <summary>A single closed contour of a glyph outline, in font units, y-up as stored in the font.</summary>
	public sealed class RawContour {
		public float2 Start;
		public readonly List<RawSegment> Segments = new();
	}

	/// <summary>
	/// A whole glyph outline plus the metrics needed to place and scale it, all in font units
	/// (divide by <see cref="UnitsPerEm"/> to get em fractions). Produced by <see cref="FontOutlineSource"/>.
	/// </summary>
	public sealed class RawGlyphOutline {
		public int UnitsPerEm;
		public ushort GlyphIndex;
		public float AdvanceWidth;
		public float LeftSideBearing;
		public readonly List<RawContour> Contours = new();

		/// <summary>True for a glyph with no ink (space, control) — a valid result, not a failure.</summary>
		public bool IsBlank => Contours.Count == 0;
	}

	/// <summary>
	/// Reads glyph outlines (TrueType <c>glyf</c> and CFF/OTF) from raw font bytes via the vendored
	/// <c>Typography.OpenFont</c> parser. This is the only bridge into that library; the msdfgen port
	/// consumes <see cref="RawGlyphOutline"/>, never an OpenFont type. Editor/bake use only.
	/// </summary>
	public sealed class FontOutlineSource : IDisposable {

		private readonly Typeface typeface;

		public int UnitsPerEm => typeface.UnitsPerEm;
		public short Ascender => typeface.Ascender;
		public short Descender => typeface.Descender;
		public short LineGap => typeface.LineGap;
		public bool IsCff => typeface.IsCffFont;

		public short CapHeight => typeface.OS2Table != null ? typeface.OS2Table.sCapHeight : (short) 0;
		public short XHeight => typeface.OS2Table != null ? typeface.OS2Table.sxHeight : (short) 0;
		public short UnderlinePosition => typeface.UnderlinePosition;
		public short StrikeoutPosition => typeface.OS2Table != null ? typeface.OS2Table.yStrikeoutPosition : (short) 0;
		public short StrikeoutSize => typeface.OS2Table != null ? typeface.OS2Table.yStrikeoutSize : (short) 0;

		public FontOutlineSource(byte[] fontBytes) {
			if (fontBytes == null || fontBytes.Length == 0) throw new ArgumentException("empty font bytes", nameof(fontBytes));
			using MemoryStream ms = new MemoryStream(fontBytes, false);
			typeface = new OpenFontReader().Read(ms) ?? throw new InvalidDataException("OpenFont could not parse the font");
		}

		/// <summary>Glyph id for a Unicode code point; 0 when the font has no glyph for it.</summary>
		public ushort GetGlyphIndex(uint codepoint) => typeface.GetGlyphIndex((int)codepoint);

		/// <summary>
		/// Outline for a code point. Returns false only when the font has no glyph for it; a blank
		/// glyph (e.g. space) returns true with <see cref="RawGlyphOutline.IsBlank"/>.
		/// </summary>
		public bool TryGetOutline(uint codepoint, out RawGlyphOutline outline) {
			ushort gi = typeface.GetGlyphIndex((int)codepoint);
			if (gi == 0 && codepoint != 0) { outline = null; return false; }
			outline = BuildOutline(gi);
			return true;
		}

		/// <summary>Outline for an explicit glyph id (used for fallback / composite work).</summary>
		public RawGlyphOutline GetOutlineByIndex(ushort glyphIndex) => BuildOutline(glyphIndex);

		private RawGlyphOutline BuildOutline(ushort glyphIndex) {
			Glyph g = typeface.GetGlyph(glyphIndex);
			RawGlyphOutline outline = new RawGlyphOutline {
				UnitsPerEm = typeface.UnitsPerEm,
				GlyphIndex = glyphIndex,
				AdvanceWidth = typeface.GetAdvanceWidthFromGlyphIndex(glyphIndex),
				LeftSideBearing = typeface.GetLeftSideBearing(glyphIndex)
			};

			ContourCollector tx = new ContourCollector(outline);
			if (g.IsCffGlyph) {
				new Typography.OpenFont.CFF.CffEvaluationEngine().Run(tx, g.GetCff1GlyphData(), 1f);
			} else if (g.GlyphPoints != null && g.EndPoints != null && g.EndPoints.Length > 0) {
				tx.Read(g.GlyphPoints, g.EndPoints, 1f); // IGlyphReaderExtensions
			}
			// else: blank glyph, no contours — fine.
			return outline;
		}

		/// <summary>
		/// Diagnostic: dumps the raw <c>glyf</c>-level structure (contour end indices, on/off-curve points)
		/// for one code point. Editor-only debugging aid, not part of the bake path.
		/// </summary>
		public string DebugDescribe(uint codepoint) {
			ushort gi = typeface.GetGlyphIndex((int) codepoint);
			Glyph g = typeface.GetGlyph(gi);
			var sb = new System.Text.StringBuilder();
			sb.Append($"U+{codepoint:X4} gi={gi} isCff={g.IsCffGlyph} ");
			if (g.GlyphPoints == null || g.EndPoints == null) { sb.Append("no TT points"); return sb.ToString(); }
			sb.Append($"points={g.GlyphPoints.Length} endPts=[");
			for (int i = 0; i < g.EndPoints.Length; i++) sb.Append(g.EndPoints[i] + (i + 1 < g.EndPoints.Length ? "," : ""));
			sb.Append("]\n");
			int start = 0;
			for (int c = 0; c < g.EndPoints.Length; c++) {
				int end = g.EndPoints[c];
				sb.Append($"  contour {c}: pts {start}..{end} ({end - start + 1})  first={g.GlyphPoints[start]}  last={g.GlyphPoints[System.Math.Min(end, g.GlyphPoints.Length - 1)]}\n");
				start = end + 1;
			}
			return sb.ToString();
		}

		public void Dispose() { }

		/// <summary>
		/// <see cref="IGlyphTranslator"/> sink that records the emitted MoveTo / LineTo / Curve3 /
		/// Curve4 stream as <see cref="RawContour"/> lists. The library already normalises TrueType's
		/// implicit on-curve midpoints into explicit quadratics before calling us.
		/// </summary>
		private sealed class ContourCollector : IGlyphTranslator {

			private readonly RawGlyphOutline outline;
			private RawContour current;
			private float2 pen;

			public ContourCollector(RawGlyphOutline outline) => this.outline = outline;

			public void BeginRead(int contourCount) { }
			public void EndRead() => FlushOpenContour();

			public void MoveTo(float x0, float y0) {
				FlushOpenContour();
				current = new RawContour { Start = new float2(x0, y0) };
				pen = current.Start;
			}

			public void LineTo(float x1, float y1) {
				float2 end = new float2(x1, y1);
				current?.Segments.Add(new RawSegment { Kind = RawSegmentKind.Line, End = end });
				pen = end;
			}

			public void Curve3(float x1, float y1, float x2, float y2) {
				float2 end = new float2(x2, y2);
				current?.Segments.Add(new RawSegment {
					Kind = RawSegmentKind.Quadratic, C1 = new float2(x1, y1), End = end
				});
				pen = end;
			}

			public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3) {
				float2 end = new float2(x3, y3);
				current?.Segments.Add(new RawSegment {
					Kind = RawSegmentKind.Cubic, C1 = new float2(x1, y1), C2 = new float2(x2, y2), End = end
				});
				pen = end;
			}

			public void CloseContour() {
				if (current == null) return;
				// Close the loop with an explicit line if the last point does not meet the start.
				if (!math.all(pen == current.Start)) {
					current.Segments.Add(new RawSegment { Kind = RawSegmentKind.Line, End = current.Start });
				}
				if (current.Segments.Count > 0) outline.Contours.Add(current);
				current = null;
			}

			private void FlushOpenContour() {
				if (current != null && current.Segments.Count > 0) outline.Contours.Add(current);
				current = null;
			}
		}
	}
}
