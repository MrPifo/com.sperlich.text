// C# port of msdfgen/core/Contour.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT).
using System.Collections.Generic;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	/// <summary>A single closed contour of a <see cref="Shape"/>.</summary>
	public sealed class Contour {

		/// <summary>The sequence of edges that make up the contour.</summary>
		public readonly List<EdgeSegment> Edges = new List<EdgeSegment>();

		public void AddEdge(EdgeSegment edge) => Edges.Add(edge);

		private static double Shoelace(Vector2 a, Vector2 b) => (b.x - a.x) * (a.y + b.y);

		private static void BoundPoint(ref double xMin, ref double yMin, ref double xMax, ref double yMax, Vector2 p) {
			if (p.x < xMin) xMin = p.x;
			if (p.y < yMin) yMin = p.y;
			if (p.x > xMax) xMax = p.x;
			if (p.y > yMax) yMax = p.y;
		}

		public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
			for (int i = 0; i < Edges.Count; i++)
				Edges[i].Bound(ref xMin, ref yMin, ref xMax, ref yMax);
		}

		public void BoundMiters(ref double xMin, ref double yMin, ref double xMax, ref double yMax,
			double border, double miterLimit, int polarity) {
			if (Edges.Count == 0) return;
			Vector2 prevDir = Edges[Edges.Count - 1].Direction(1).Normalize(true);
			for (int i = 0; i < Edges.Count; i++) {
				Vector2 dir = -Edges[i].Direction(0).Normalize(true);
				if (polarity * CrossProduct(prevDir, dir) >= 0) {
					double miterLength = miterLimit;
					double q = 0.5 * (1 - DotProduct(prevDir, dir));
					if (q > 0)
						miterLength = System.Math.Min(1 / System.Math.Sqrt(q), miterLimit);
					Vector2 miter = Edges[i].Point(0) + border * miterLength * (prevDir + dir).Normalize(true);
					BoundPoint(ref xMin, ref yMin, ref xMax, ref yMax, miter);
				}
				prevDir = Edges[i].Direction(1).Normalize(true);
			}
		}

		/// <summary>Winding of the contour: 1 for positive, -1 for negative, 0 for empty.</summary>
		public int Winding() {
			if (Edges.Count == 0) return 0;
			double total = 0;
			if (Edges.Count == 1) {
				Vector2 a = Edges[0].Point(0), b = Edges[0].Point(1 / 3.0), c = Edges[0].Point(2 / 3.0);
				total += Shoelace(a, b);
				total += Shoelace(b, c);
				total += Shoelace(c, a);
			} else if (Edges.Count == 2) {
				Vector2 a = Edges[0].Point(0), b = Edges[0].Point(0.5), c = Edges[1].Point(0), d = Edges[1].Point(0.5);
				total += Shoelace(a, b);
				total += Shoelace(b, c);
				total += Shoelace(c, d);
				total += Shoelace(d, a);
			} else {
				Vector2 prev = Edges[Edges.Count - 1].Point(0);
				for (int i = 0; i < Edges.Count; i++) {
					Vector2 cur = Edges[i].Point(0);
					total += Shoelace(prev, cur);
					prev = cur;
				}
			}
			return Sign(total);
		}

		public void Reverse() {
			int n = Edges.Count;
			for (int i = n / 2; i > 0; --i)
				(Edges[i - 1], Edges[n - i]) = (Edges[n - i], Edges[i - 1]);
			for (int i = 0; i < n; i++)
				Edges[i].Reverse();
		}
	}
}
