// C# port of msdfgen/core/Shape.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky).
using System;
using System.Collections.Generic;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	/// <summary>Y grows up (default) or down.</summary>
	public enum YAxisOrientation { Upward, Downward }

	/// <summary>Vector shape: a list of closed contours.</summary>
	public sealed class Shape {

		// Dot product of adjacent edge directions below (this - 1) counts as convergent.
		private const double CornerDotEpsilon = 0.000001;
		// Moves control points slightly more than necessary to absorb floating-point error.
		private const double DeconvergeOvershoot = 1.11111111111111111;
		private const double LargeValue = 1e240;

		public struct Bounds { public double l, b, r, t; }

		public readonly List<Contour> Contours = new List<Contour>();

		/// <summary>Bottom-to-top (false) vs top-to-bottom (true) Y. Kept for parity with msdfgen.</summary>
		public bool InverseYAxis;

		public Contour AddContour() {
			Contour c = new Contour();
			Contours.Add(c);
			return c;
		}

		public void AddContour(Contour contour) => Contours.Add(contour);

		public bool Validate() {
			foreach (Contour contour in Contours) {
				if (contour.Edges.Count != 0) {
					Vector2 corner = contour.Edges[contour.Edges.Count - 1].Point(1);
					foreach (EdgeSegment edge in contour.Edges) {
						if (edge == null) return false;
						if (edge.Point(0) != corner) return false;
						corner = edge.Point(1);
					}
				}
			}
			return true;
		}

		private static void DeconvergeEdge(Contour contour, int edgeIndex, int param, Vector2 vector) {
			EdgeSegment e = contour.Edges[edgeIndex];
			if (e.Type == 2) {
				e = ((EdgeSegment.QuadraticSegment) e).ConvertToCubic();
				contour.Edges[edgeIndex] = e;
			}
			if (e.Type == 3) {
				Vector2[] p = ((EdgeSegment.CubicSegment) e).p;
				switch (param) {
					case 0: p[1] += (p[1] - p[0]).Length * vector; break;
					case 1: p[2] += (p[2] - p[3]).Length * vector; break;
				}
			}
		}

		public void Normalize() {
			foreach (Contour contour in Contours) {
				if (contour.Edges.Count == 1) {
					contour.Edges[0].SplitInThirds(out EdgeSegment p0, out EdgeSegment p1, out EdgeSegment p2);
					contour.Edges.Clear();
					contour.Edges.Add(p0);
					contour.Edges.Add(p1);
					contour.Edges.Add(p2);
				} else if (contour.Edges.Count != 0) {
					int prevIndex = contour.Edges.Count - 1;
					for (int i = 0; i < contour.Edges.Count; i++) {
						Vector2 prevDir = contour.Edges[prevIndex].Direction(1).Normalize();
						Vector2 curDir = contour.Edges[i].Direction(0).Normalize();
						if (DotProduct(prevDir, curDir) < CornerDotEpsilon - 1) {
							double factor = DeconvergeOvershoot *
								Math.Sqrt(1 - (CornerDotEpsilon - 1) * (CornerDotEpsilon - 1)) / (CornerDotEpsilon - 1);
							Vector2 axis = factor * (curDir - prevDir).Normalize();
							if (ConvergentCurveOrdering.Compute(contour.Edges[prevIndex], contour.Edges[i]) < 0)
								axis = -axis;
							DeconvergeEdge(contour, prevIndex, 1, axis.GetOrthogonal(true));
							DeconvergeEdge(contour, i, 0, axis.GetOrthogonal(false));
						}
						prevIndex = i;
					}
				}
			}
		}

		public void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
			foreach (Contour contour in Contours)
				contour.Bound(ref xMin, ref yMin, ref xMax, ref yMax);
		}

		public void BoundMiters(ref double xMin, ref double yMin, ref double xMax, ref double yMax,
			double border, double miterLimit, int polarity) {
			foreach (Contour contour in Contours)
				contour.BoundMiters(ref xMin, ref yMin, ref xMax, ref yMax, border, miterLimit, polarity);
		}

		public Bounds GetBounds(double border = 0, double miterLimit = 0, int polarity = 0) {
			Bounds bounds = new Bounds { l = LargeValue, b = LargeValue, r = -LargeValue, t = -LargeValue };
			Bound(ref bounds.l, ref bounds.b, ref bounds.r, ref bounds.t);
			if (border > 0) {
				bounds.l -= border; bounds.b -= border;
				bounds.r += border; bounds.t += border;
				if (miterLimit > 0)
					BoundMiters(ref bounds.l, ref bounds.b, ref bounds.r, ref bounds.t, border, miterLimit, polarity);
			}
			return bounds;
		}

		public void GetScanline(Scanline line, double y) {
			List<Scanline.Intersection> intersections = new List<Scanline.Intersection>();
			double[] x = new double[3];
			int[] dy = new int[3];
			foreach (Contour contour in Contours) {
				foreach (EdgeSegment edge in contour.Edges) {
					int n = edge.ScanlineIntersections(x, dy, y);
					for (int i = 0; i < n; ++i)
						intersections.Add(new Scanline.Intersection { x = x[i], direction = dy[i] });
				}
			}
			line.SetIntersections(intersections);
		}

		public int EdgeCount() {
			int total = 0;
			foreach (Contour contour in Contours)
				total += contour.Edges.Count;
			return total;
		}

		/// <summary>
		/// Normalises every contour to the msdfgen sign convention (top-level "outer" contours wind
		/// positive / CCW, holes wind negative / CW), independent of the source font's raw winding.
		/// <para>
		/// Unlike the upstream single-shared-scanline version, this runs one probe per contour and
		/// places that probe near the contour's own vertical extremes, retrying at several heights.
		/// A probe height is rejected as soon as any two crossings share an x — that "coincident edge"
		/// case (e.g. Comfortaa 'h', where the ascender stem piece lies exactly on the lower stem
		/// piece) is what left such contours undecided upstream, so they kept a raw clockwise winding
		/// and punched a hole through the overlap.
		/// </para>
		/// </summary>
		public void OrientContours() {
			double[] x = new double[3];
			int[] dy = new int[3];
			int[] orientations = new int[Contours.Count];
			List<(double x, int dir, int ci)> hits = new List<(double, int, int)>();

			for (int i = 0; i < Contours.Count; ++i) {
				Contour ci = Contours[i];
				if (ci.Edges.Count == 0) continue;

				double l = LargeValue, b = LargeValue, r = -LargeValue, t = -LargeValue;
				ci.Bound(ref l, ref b, ref r, ref t);
				double h = t - b;
				if (!(h > 0)) continue;

				double[] probes = { t - 0.04 * h, b + 0.04 * h, b + 0.5 * h, t - 0.27 * h, b + 0.27 * h };
				bool decided = false;

				foreach (double y in probes) {
					hits.Clear();
					for (int j = 0; j < Contours.Count; ++j)
						foreach (EdgeSegment edge in Contours[j].Edges) {
							int n = edge.ScanlineIntersections(x, dy, y);
							for (int k = 0; k < n; ++k) hits.Add((x[k], dy[k], j));
						}
					if (hits.Count < 2) continue;
					hits.Sort((p, q) => Sign(p.x - q.x));

					bool coincident = false, ciCrossed = false;
					for (int k = 0; k < hits.Count; ++k) {
						if (hits[k].ci == i) ciCrossed = true;
						if (k > 0 && hits[k].x == hits[k - 1].x) coincident = true;
					}
					if (coincident || !ciCrossed) continue;

					for (int k = 0; k < hits.Count; ++k)
						if (hits[k].ci == i)
							orientations[i] += 2 * ((k & 1) ^ (hits[k].dir > 0 ? 1 : 0)) - 1;
					decided = orientations[i] != 0;
					if (decided) break;
				}

				if (!decided) {
					// Contour i coincides with another at every probe height (they share every edge).
					// Decide from the signed winding of the OTHER contours at i's deep interior.
					double yc = b + 0.5 * h;
					hits.Clear();
					foreach (EdgeSegment edge in ci.Edges) {
						int n = edge.ScanlineIntersections(x, dy, yc);
						for (int k = 0; k < n; ++k) hits.Add((x[k], dy[k], i));
					}
					hits.Sort((p, q) => Sign(p.x - q.x));
					double probeX = double.NaN, widest = -1;
					int run = 0;
					for (int k = 0; k + 1 < hits.Count; ++k) {
						run += hits[k].dir;
						if (run != 0 && hits[k + 1].x - hits[k].x > widest) {
							widest = hits[k + 1].x - hits[k].x;
							probeX = 0.5 * (hits[k].x + hits[k + 1].x);
						}
					}
					if (!double.IsNaN(probeX)) {
						int others = 0;
						for (int j = 0; j < Contours.Count; ++j) {
							if (j == i) continue;
							foreach (EdgeSegment edge in Contours[j].Edges) {
								int n = edge.ScanlineIntersections(x, dy, yc);
								for (int k = 0; k < n; ++k) if (x[k] < probeX) others += dy[k];
							}
						}
						int want = others != 0 ? -Sign((double) others) : 1;
						orientations[i] = ci.Winding() == want ? 1 : -1;
					}
				}
			}

			for (int i = 0; i < Contours.Count; ++i)
				if (orientations[i] < 0)
					Contours[i].Reverse();
		}

		public YAxisOrientation GetYAxisOrientation() => InverseYAxis ? YAxisOrientation.Downward : YAxisOrientation.Upward;
		public void SetYAxisOrientation(YAxisOrientation o) => InverseYAxis = o != YAxisOrientation.Upward;
	}
}
