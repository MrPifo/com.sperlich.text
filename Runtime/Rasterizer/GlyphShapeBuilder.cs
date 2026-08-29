// Bridges a parsed font outline (Typography.OpenFont, via FontOutlineSource) into an msdfgen Shape.
// Font design space is already Y-up, matching msdfgen's default Shape orientation — no flip.

namespace Sperlich.Text.Rasterizer {

	public static class GlyphShapeBuilder {

		/// <summary>
		/// Builds a normalised <see cref="Shape"/> in font units from a raw glyph outline. Returns an
		/// empty shape (no contours) for a blank glyph.
		/// <para>
		/// <paramref name="reorient"/> is <c>false</c> by default: real TrueType/CFF fonts already wind
		/// their contours for the non-zero fill rule (solid pieces one way, holes the other), which is
		/// exactly what we want, and overlapping same-direction pieces (common in geometric fonts such
		/// as Comfortaa — e.g. 'h' is three overlapping stems) are correct as-is. Running
		/// <see cref="Shape.OrientContours"/> tries to decide each contour independently and gets those
		/// overlaps wrong (fills holes, hollows stems). Only set <c>true</c> for a source whose raw
		/// winding is known to be inconsistent.
		/// </para>
		/// </summary>
		public static Shape Build(RawGlyphOutline outline, bool reorient = false, bool resolveOverlaps = true) {
			Shape shape = new Shape { InverseYAxis = false };
			if (outline == null || outline.Contours.Count == 0)
				return shape;

			foreach (RawContour rc in outline.Contours) {
				Contour c = new Contour();
				Vector2 cur = new Vector2(rc.Start.x, rc.Start.y);
				foreach (RawSegment s in rc.Segments) {
					Vector2 end = new Vector2(s.End.x, s.End.y);
					switch (s.Kind) {
						case RawSegmentKind.Line:
							if (end != cur)
								c.AddEdge(new EdgeSegment.LinearSegment(cur, end));
							break;
						case RawSegmentKind.Quadratic:
							c.AddEdge(EdgeSegment.Create(cur, new Vector2(s.C1.x, s.C1.y), end));
							break;
						case RawSegmentKind.Cubic:
							c.AddEdge(EdgeSegment.Create(cur, new Vector2(s.C1.x, s.C1.y), new Vector2(s.C2.x, s.C2.y), end));
							break;
					}
					cur = end;
				}
				if (c.Edges.Count > 0)
					shape.AddContour(c);
			}

			shape.Normalize();
			if (resolveOverlaps) shape = ShapeResolver.Resolve(shape);
			else if (reorient) shape.OrientContours();
			return shape;
		}
	}
}
