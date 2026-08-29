// Pure-C# overlap / self-intersection resolver for glyph Shapes — stands in for msdfgen's
// Skia-backed resolveShapeGeometry(). No external dependency.
//
// Pipeline:
//   1. Every edge is split at each parameter where it meets another edge (line×line analytic,
//      line×curve via polynomial roots, curve×curve via adaptive bounding-box subdivision).
//   2. Each resulting sub-edge is kept only if the non-zero winding of the ORIGINAL shape differs
//      across it (one side filled, the other empty). Sub-edges buried in the interior of an overlap
//      — the internal arcs that cause the '8' waist seam / the '9' join bulge — vanish here.
//   3. Sub-edges are oriented so the filled region sits on their right, coincident duplicates
//      (contours that share an exact edge) are merged, and the survivors are walked into clean
//      non-overlapping, non-self-intersecting contours with their original curve pieces intact.
//
// The output keeps this codebase's convention: outer contours wind Winding()==+1, holes ==-1,
// so edge colouring / MSDF generation / sign correction run unchanged afterwards.

using System;
using System.Collections.Generic;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	public static class ShapeResolver {

		// Node coalescing distance in font units. Must exceed the position error of a subdivision
		// intersection (~0.5–1 unit at 1e-4 param termination on a long segment) so the boundary walk
		// can rejoin split points; still far below one atlas texel at any sane em size.
		private const double PointEps = 2.0;
		private const double ParamEps = 1e-4;   // ignore split params this close to an endpoint
		private const int MaxDepth = 40;        // subdivision recursion guard

		/// <summary>
		/// Diagnostic only: why the last <see cref="Resolve"/> call returned what it did
		/// ("ok", "fallback: walk not clean", "fallback: silhouette mismatch", …). Read by the
		/// editor debug-dump tool; never affects output.
		/// </summary>
		public static string LastNote = "";

		/// <summary>
		/// Returns a new <see cref="Shape"/> with all contour overlaps and self-intersections removed.
		/// On any internal failure it returns <paramref name="shape"/> unchanged (never throws).
		/// </summary>
		public static Shape Resolve(Shape shape) {
			LastNote = "ok";
			try {
				return ResolveCore(shape);
			} catch (Exception e) {
				LastNote = "fallback: exception (" + e.GetType().Name + ")";
				return shape;
			}
		}

		private static Shape ResolveCore(Shape shape) {
			// Pre-pass: many fonts draw one filled region as several contours that abut along a shared
			// edge (Comfortaa's 'h' ascender = two stacked boxes meeting at y=223). That shared edge sits
			// on the fill on both sides, so the boundary walk sees a 4-way node it cannot order. Splice
			// such contour pairs back into one — a plain un-split, no geometry change — before resolving.
			WeldSharedContourEdges(shape);

			List<EdgeSegment> segs = new List<EdgeSegment>();
			foreach (Contour c in shape.Contours)
				foreach (EdgeSegment e in c.Edges)
					if (e != null) segs.Add(e);
			if (segs.Count < 2) { LastNote = "passthrough: <2 edges"; return shape; }

			// -- 1. intersection parameters per segment ---------------------------------------------
			List<SortedSet<double>> cuts = new List<SortedSet<double>>(segs.Count);
			for (int i = 0; i < segs.Count; i++) cuts.Add(new SortedSet<double>());

			// cheap AABB per segment for pair pruning
			double[] bxl = new double[segs.Count], byl = new double[segs.Count];
			double[] bxh = new double[segs.Count], byh = new double[segs.Count];
			for (int i = 0; i < segs.Count; i++)
				SampleBounds(segs[i], 0, 1, out bxl[i], out byl[i], out bxh[i], out byh[i]);

			List<(double, double)> hits = new List<(double, double)>();
			for (int i = 0; i < segs.Count; i++) {
				for (int j = i + 1; j < segs.Count; j++) {
					double pad = PointEps;
					if (bxl[i] - pad > bxh[j] || bxl[j] - pad > bxh[i] ||
						byl[i] - pad > byh[j] || byl[j] - pad > byh[i]) continue;

					// Collinear/coincident overlap (a stem edge that is a sub-piece of another stem
					// edge — Comfortaa 'h'/'k'): cut both at the overlap boundaries and skip the
					// transversal test, which would otherwise flood tiny cuts along the shared line.
					if (OverlapRange(segs[i], segs[j], out double aLo, out double aHi, out double bLo, out double bHi)) {
						AddCut(cuts[i], aLo); AddCut(cuts[i], aHi);
						AddCut(cuts[j], bLo); AddCut(cuts[j], bHi);
						continue;
					}
					hits.Clear();
					Intersect(segs[i], segs[j], hits);
					foreach ((double ta, double tb) in hits) {
						AddCut(cuts[i], ta);
						AddCut(cuts[j], tb);
					}
				}
			}

			// -- 2. split into sub-edges ---------------------------------------------------------------
			List<EdgeSegment> subs = new List<EdgeSegment>();
			for (int i = 0; i < segs.Count; i++) {
				if (cuts[i].Count == 0) { subs.Add(segs[i]); continue; }
				SplitMulti(segs[i], new List<double>(cuts[i]), subs);
			}

			// -- 3. keep boundary sub-edges (winding differs across), filled on the RIGHT ------------
			List<EdgeSegment> boundary = new List<EdgeSegment>();
			foreach (EdgeSegment e in subs) {
				Vector2 a = e.Point(0), b = e.Point(1);
				if ((b - a).Length < PointEps) continue; // collapsed by a near-endpoint cut
				Vector2 dir = e.Direction(0.5);
				if (!dir.IsNonZero) dir = b - a;
				Vector2 left = dir.GetOrthonormal(true, true);
				if (!left.IsNonZero) continue;
				Vector2 mid = e.Point(0.5);
				double probe = Clamp((b - a).Length * 0.25, 0.3, 4.0);
				bool leftFilled = Winding(shape, mid + probe * left) != 0;
				bool rightFilled = Winding(shape, mid - probe * left) != 0;
				if (leftFilled == rightFilled) continue;             // interior edge → drop
				if (rightFilled) boundary.Add(e);                    // already right-filled
				else { EdgeSegment r = e.Clone(); r.Reverse(); boundary.Add(r); }
			}
			if (boundary.Count < 2) { LastNote = "fallback: <2 boundary edges"; return shape; }

			DedupCoincident(boundary);

			// -- 4. walk the boundary into contours -------------------------------------------------
			Shape result = WalkContours(boundary, shape.InverseYAxis, out bool cleanWalk);
			if (result == null || result.Contours.Count == 0) { LastNote = "fallback: empty walk"; return shape; }

			// Safety gate. The boundary walk can take the wrong branch at a dense multi-contour
			// junction (the € sign: a C-arc plus two bars that punch clean through it). Fall back to
			// the unresolved shape — a faint overlap seam beats a scrambled glyph — whenever the walk
			// left edges unused / hit a dead end, or the resolved silhouette no longer covers the
			// same area as the original.
			if (!cleanWalk) { LastNote = "fallback: walk not clean (" + WalkReason + ")"; return shape; }
			result.Normalize();
			if (!SilhouetteMatches(shape, result)) { LastNote = "fallback: silhouette mismatch"; return shape; }
			LastNote = "ok: resolved " + result.Contours.Count + " contour(s)";
			return result;
		}

		private static void AddCut(SortedSet<double> set, double t) {
			if (t > ParamEps && t < 1 - ParamEps) set.Add(t);
		}

		// ============================ weld abutting contours =================================

		/// <summary>
		/// Merges any two same-winding contours that share exactly one edge traversed in opposite
		/// directions (a designer-split of one filled region). The shared edge is removed from both and
		/// the two edge chains are spliced into a single contour. Repeats until nothing more merges.
		/// Leaves the shape untouched when no such pair exists.
		/// </summary>
		private static void WeldSharedContourEdges(Shape shape) {
			List<Contour> cs = shape.Contours;
			int guard = 0;
			bool again = true;
			while (again && guard++ < 32) {
				again = false;
				for (int ci = 0; ci < cs.Count && !again; ci++) {
					for (int cj = ci + 1; cj < cs.Count && !again; cj++) {
						if (cs[ci].Edges.Count < 2 || cs[cj].Edges.Count < 2) continue;
						if (cs[ci].Winding() != cs[cj].Winding()) continue;
						if (!SingleSharedEdge(cs[ci], cs[cj], out int ei, out int ej)) continue;
						cs[ci] = Splice(cs[ci], ei, cs[cj], ej);
						cs.RemoveAt(cj);
						again = true;
					}
				}
			}
		}

		/// <summary>True when exactly one edge of <paramref name="a"/> is coincident with exactly one
		/// edge of <paramref name="b"/> in the opposite direction; outputs those edge indices.</summary>
		private static bool SingleSharedEdge(Contour a, Contour b, out int ai, out int bi) {
			ai = bi = -1;
			int found = 0;
			for (int i = 0; i < a.Edges.Count; i++) {
				for (int j = 0; j < b.Edges.Count; j++) {
					if (!CoincidentOpposite(a.Edges[i], b.Edges[j])) continue;
					found++;
					if (found > 1) return false; // shared border is more than one edge — leave it
					ai = i; bi = j;
				}
			}
			return found == 1;
		}

		private static bool CoincidentOpposite(EdgeSegment e1, EdgeSegment e2) {
			if ((e1.Point(0) - e2.Point(1)).Length > PointEps) return false;
			if ((e1.Point(1) - e2.Point(0)).Length > PointEps) return false;
			for (int k = 1; k <= 5; k++) {
				double t = k / 6.0;
				if ((e1.Point(t) - e2.Point(1 - t)).Length > PointEps) return false;
			}
			return true;
		}

		/// <summary>Drops edge <paramref name="ai"/> of <paramref name="a"/> and edge
		/// <paramref name="bi"/> of <paramref name="b"/> and joins the remaining chains into one contour,
		/// starting after the removed edge on each side.</summary>
		private static Contour Splice(Contour a, int ai, Contour b, int bi) {
			Contour c = new Contour();
			for (int k = 1; k < a.Edges.Count; k++) c.Edges.Add(a.Edges[(ai + k) % a.Edges.Count]);
			for (int k = 1; k < b.Edges.Count; k++) c.Edges.Add(b.Edges[(bi + k) % b.Edges.Count]);
			for (int i = 0; i + 1 < c.Edges.Count; i++) {
				Vector2 gapA = c.Edges[i].Point(1), gapB = c.Edges[i + 1].Point(0);
				if ((gapA - gapB).Length > 1e-9 && (gapA - gapB).Length < PointEps * 2.0) {
					Vector2 join = 0.5 * (gapA + gapB);
					c.Edges[i].MoveEndPoint(join);
					c.Edges[i + 1].MoveStartPoint(join);
				}
			}
			return c;
		}

		// ============================ collinear / coincident overlap ==========================

		/// <summary>
		/// If segment <paramref name="a"/> and <paramref name="b"/> lie on top of each other over a
		/// contiguous stretch (not merely crossing at a point), returns that stretch as parameter
		/// ranges on both. Sampled — precise enough to cut at, the shared sub-edges then dedup.
		/// </summary>
		private static bool OverlapRange(EdgeSegment a, EdgeSegment b,
			out double aLo, out double aHi, out double bLo, out double bHi) {
			aLo = aHi = bLo = bHi = 0;
			const int S = 32;
			int runStart = -1, runEnd = -1;
			double bStart = 0, bEnd = 0;
			int bestLen = 0, bestS = -1, bestE = -1;
			double bestBS = 0, bestBE = 0;

			for (int i = 0; i <= S; i++) {
				double ta = (double) i / S;
				bool on = NearestOnSegment(b, a.Point(ta), out double tb, out double dist) && dist < PointEps;
				if (on) {
					if (runStart < 0) { runStart = i; bStart = tb; }
					runEnd = i; bEnd = tb;
				}
				if ((!on || i == S) && runStart >= 0) {
					if (runEnd - runStart > bestLen) {
						bestLen = runEnd - runStart; bestS = runStart; bestE = runEnd;
						bestBS = bStart; bestBE = bEnd;
					}
					runStart = -1;
				}
			}
			if (bestLen < 4) return false; // < 1/8 of the segment coincident — treat as a crossing

			aLo = (double) bestS / S; aHi = (double) bestE / S;
			bLo = Math.Min(bestBS, bestBE); bHi = Math.Max(bestBS, bestBE);
			return true;
		}

		private static bool NearestOnSegment(EdgeSegment seg, Vector2 p, out double t, out double dist) {
			if (seg.Type == 1) {
				Vector2[] q = seg.ControlPoints;
				Vector2 d = q[1] - q[0];
				double ll = d.SquaredLength;
				t = ll > 1e-18 ? Clamp01(Vector2.Dot(p - q[0], d) / ll) : 0.0;
				dist = (seg.Point(t) - p).Length;
				return true;
			}
			double bt = 0, bd = double.MaxValue;
			const int K = 24;
			for (int i = 0; i <= K; i++) {
				double tt = (double) i / K;
				double dd = (seg.Point(tt) - p).SquaredLength;
				if (dd < bd) { bd = dd; bt = tt; }
			}
			double step = 1.0 / K;
			for (int iter = 0; iter < 18; iter++) {
				step *= 0.5;
				double tl = Clamp01(bt - step), tr = Clamp01(bt + step);
				double dl = (seg.Point(tl) - p).SquaredLength, dr = (seg.Point(tr) - p).SquaredLength;
				if (dl < bd) { bd = dl; bt = tl; }
				if (dr < bd) { bd = dr; bt = tr; }
			}
			t = bt; dist = Math.Sqrt(bd);
			return true;
		}

		// ============================ intersection ============================================

		private static void Intersect(EdgeSegment a, EdgeSegment b, List<(double, double)> outHits) {
			Vector2[] pa = a.ControlPoints, pb = b.ControlPoints;
			if (a.Type == 1 && b.Type == 1) {
				if (LineLine(pa[0], pa[1], pb[0], pb[1], out double ta, out double tb)) outHits.Add((ta, tb));
				return;
			}
			if (a.Type == 1) { LineCurve(pa[0], pa[1], b, false, outHits); return; }
			if (b.Type == 1) { LineCurve(pb[0], pb[1], a, true, outHits); return; }
			SubdivIntersect(a, 0, 1, b, 0, 1, 0, outHits);
			MergeClose(outHits);
		}

		private static bool LineLine(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out double ta, out double tb) {
			ta = tb = 0;
			Vector2 da = a1 - a0, db = b1 - b0;
			double denom = Vector2.Cross(da, db);
			if (Math.Abs(denom) < 1e-12) return false;
			Vector2 d = b0 - a0;
			ta = Vector2.Cross(d, db) / denom;
			tb = Vector2.Cross(d, da) / denom;
			return ta >= -ParamEps && ta <= 1 + ParamEps && tb >= -ParamEps && tb <= 1 + ParamEps;
		}

		// Curve as C(t); line as P0 + s*(P1-P0). Solve N·(C(t)-P0)=0 with N ⟂ line, then recover s.
		private static void LineCurve(Vector2 l0, Vector2 l1, EdgeSegment curve, bool curveIsA, List<(double, double)> outHits) {
			Vector2 ld = l1 - l0;
			double ll = ld.SquaredLength;
			if (ll < 1e-18) return;
			Vector2 n = new Vector2(-ld.y, ld.x);
			Vector2[] p = curve.ControlPoints;
			double[] roots = new double[3];
			int nr;
			if (curve.Type == 2) {
				// N·C(t) = A t^2 + B t + C  with C(t) = (1-t)^2 p0 + 2(1-t)t p1 + t^2 p2
				double c0 = Vector2.Dot(n, p[0] - l0);
				double c1 = Vector2.Dot(n, p[1] - l0);
				double c2 = Vector2.Dot(n, p[2] - l0);
				double A = c0 - 2 * c1 + c2;
				double B = -2 * c0 + 2 * c1;
				double C = c0;
				nr = EquationSolver.SolveQuadratic(roots, A, B, C);
			} else {
				double c0 = Vector2.Dot(n, p[0] - l0);
				double c1 = Vector2.Dot(n, p[1] - l0);
				double c2 = Vector2.Dot(n, p[2] - l0);
				double c3 = Vector2.Dot(n, p[3] - l0);
				double A = -c0 + 3 * c1 - 3 * c2 + c3;
				double B = 3 * c0 - 6 * c1 + 3 * c2;
				double C = -3 * c0 + 3 * c1;
				double D = c0;
				nr = EquationSolver.SolveCubic(roots, A, B, C, D);
			}
			for (int i = 0; i < nr; i++) {
				double t = roots[i];
				if (t < -ParamEps || t > 1 + ParamEps) continue;
				t = Clamp01(t);
				Vector2 cp = curve.Point(t);
				double s = Vector2.Dot(cp - l0, ld) / ll;
				if (s < -ParamEps || s > 1 + ParamEps) continue;
				s = Clamp01(s);
				outHits.Add(curveIsA ? (t, s) : (s, t));
			}
		}

		private static void SubdivIntersect(EdgeSegment a, double a0, double a1, EdgeSegment b, double b0, double b1,
			int depth, List<(double, double)> outHits) {
			if (outHits.Count > 64) return; // runaway (near-coincident curves) — bail
			if (!BoundsOverlap(a, a0, a1, b, b0, b1)) return;
			double aSpan = a1 - a0, bSpan = b1 - b0;
			if (depth >= MaxDepth || (aSpan < 1e-4 && bSpan < 1e-4)) {
				outHits.Add((0.5 * (a0 + a1), 0.5 * (b0 + b1)));
				return;
			}
			if (aSpan >= bSpan) {
				double am = 0.5 * (a0 + a1);
				SubdivIntersect(a, a0, am, b, b0, b1, depth + 1, outHits);
				SubdivIntersect(a, am, a1, b, b0, b1, depth + 1, outHits);
			} else {
				double bm = 0.5 * (b0 + b1);
				SubdivIntersect(a, a0, a1, b, b0, bm, depth + 1, outHits);
				SubdivIntersect(a, a0, a1, b, bm, b1, depth + 1, outHits);
			}
		}

		private static bool BoundsOverlap(EdgeSegment a, double a0, double a1, EdgeSegment b, double b0, double b1) {
			SampleBounds(a, a0, a1, out double axl, out double ayl, out double axh, out double ayh);
			SampleBounds(b, b0, b1, out double bxl, out double byl, out double bxh, out double byh);
			double pad = PointEps;
			return axl - pad <= bxh && bxl - pad <= axh && ayl - pad <= byh && byl - pad <= ayh;
		}

		private static void SampleBounds(EdgeSegment e, double t0, double t1,
			out double xl, out double yl, out double xh, out double yh) {
			xl = yl = double.MaxValue;
			xh = yh = double.MinValue;
			const int N = 6;
			for (int i = 0; i <= N; i++) {
				Vector2 p = e.Point(t0 + (t1 - t0) * i / N);
				if (p.x < xl) xl = p.x; if (p.x > xh) xh = p.x;
				if (p.y < yl) yl = p.y; if (p.y > yh) yh = p.y;
			}
		}

		private static void MergeClose(List<(double, double)> hits) {
			if (hits.Count < 2) return;
			hits.Sort((u, v) => u.Item1.CompareTo(v.Item1));
			for (int i = hits.Count - 1; i > 0; i--)
				if (Math.Abs(hits[i].Item1 - hits[i - 1].Item1) < 3e-3 &&
					Math.Abs(hits[i].Item2 - hits[i - 1].Item2) < 3e-3)
					hits.RemoveAt(i);
		}

		// ============================ splitting ==============================================

		private static void SplitMulti(EdgeSegment seg, List<double> paramsAsc, List<EdgeSegment> outList) {
			EdgeSegment rest = seg;
			double consumed = 0;
			foreach (double tAbs in paramsAsc) {
				double tLocal = (tAbs - consumed) / (1 - consumed);
				if (!(tLocal > ParamEps && tLocal < 1 - ParamEps)) continue;
				SplitAt(rest, tLocal, out EdgeSegment head, out EdgeSegment tail);
				outList.Add(head);
				rest = tail;
				consumed = tAbs;
			}
			outList.Add(rest);
		}

		private static void SplitAt(EdgeSegment seg, double t, out EdgeSegment head, out EdgeSegment tail) {
			Vector2[] p = seg.ControlPoints;
			EdgeColor col = seg.Color;
			if (seg.Type == 1) {
				Vector2 m = Lerp(p[0], p[1], t);
				head = new EdgeSegment.LinearSegment(p[0], m, col);
				tail = new EdgeSegment.LinearSegment(m, p[1], col);
			} else if (seg.Type == 2) {
				Vector2 a = Lerp(p[0], p[1], t);
				Vector2 b = Lerp(p[1], p[2], t);
				Vector2 m = Lerp(a, b, t);
				head = new EdgeSegment.QuadraticSegment(p[0], a, m, col);
				tail = new EdgeSegment.QuadraticSegment(m, b, p[2], col);
			} else {
				Vector2 a = Lerp(p[0], p[1], t);
				Vector2 b = Lerp(p[1], p[2], t);
				Vector2 c = Lerp(p[2], p[3], t);
				Vector2 d = Lerp(a, b, t);
				Vector2 e = Lerp(b, c, t);
				Vector2 m = Lerp(d, e, t);
				head = new EdgeSegment.CubicSegment(p[0], a, d, m, col);
				tail = new EdgeSegment.CubicSegment(m, e, c, p[3], col);
			}
		}

		private static Vector2 Lerp(Vector2 a, Vector2 b, double t) => a + (b - a) * t;

		// ============================ winding ================================================

		[ThreadStatic] private static double[] _wx;
		[ThreadStatic] private static int[] _wdy;

		private static int Winding(Shape shape, Vector2 p) {
			_wx ??= new double[3];
			_wdy ??= new int[3];
			int w = 0;
			foreach (Contour c in shape.Contours)
				foreach (EdgeSegment e in c.Edges) {
					int n = e.ScanlineIntersections(_wx, _wdy, p.y);
					for (int k = 0; k < n; k++)
						if (_wx[k] < p.x) w += _wdy[k];
				}
			return w;
		}

		// ============================ safety gate ===========================================

		private static bool SilhouetteMatches(Shape orig, Shape res) {
			double ao = CoverageArea(orig);
			if (ao <= 1e-6) return true;
			double ar = CoverageArea(res);
			return Math.Abs(ar - ao) <= 0.06 * ao;
		}

		private static double CoverageArea(Shape s) {
			double xl = double.MaxValue, yl = double.MaxValue, xh = double.MinValue, yh = double.MinValue;
			foreach (Contour c in s.Contours) c.Bound(ref xl, ref yl, ref xh, ref yh);
			if (!(xh > xl && yh > yl)) return 0;

			double[] xs = new double[3];
			int[] dy = new int[3];
			List<(double x, int d)> cross = new List<(double, int)>();
			double total = 0;
			const int NY = 48;
			for (int iy = 0; iy < NY; iy++) {
				double y = yl + (yh - yl) * (iy + 0.5) / NY;
				cross.Clear();
				foreach (Contour c in s.Contours)
					foreach (EdgeSegment e in c.Edges) {
						int n = e.ScanlineIntersections(xs, dy, y);
						for (int k = 0; k < n; k++) cross.Add((xs[k], dy[k]));
					}
				if (cross.Count < 2) continue;
				cross.Sort((u, v) => u.x.CompareTo(v.x));
				int w = 0;
				double prevX = cross[0].x;
				foreach ((double cx, int cd) in cross) {
					if (w != 0) total += cx - prevX;
					w += cd;
					prevX = cx;
				}
			}
			return total;
		}

		// ============================ dedup + walk ==========================================

		/// <summary>
		/// Drops a boundary sub-edge whose whole body lies on top of another sub-edge (contours that
		/// share an exact edge, or a coincident-overlap stretch that was cut to the same span). Keeps
		/// the first occurrence.
		/// </summary>
		private static void DedupCoincident(List<EdgeSegment> edges) {
			double tol = PointEps * 1.5;
			for (int i = edges.Count - 1; i >= 0; i--) {
				bool dup = false;
				for (int j = 0; j < i && !dup; j++)
					if (BodyLiesOn(edges[i], edges[j], tol)) dup = true; // i's body sits on j -> redundant
				if (dup) edges.RemoveAt(i);
			}
		}

		/// <summary>True when several interior samples of <paramref name="a"/> all sit within
		/// <paramref name="tol"/> of <paramref name="b"/>'s body.</summary>
		private static bool BodyLiesOn(EdgeSegment a, EdgeSegment b, double tol) {
			for (int k = 1; k <= 5; k++) {
				Vector2 pa = a.Point(k / 6.0);
				if (!(NearestOnSegment(b, pa, out _, out double d) && d < tol)) return false;
			}
			return true;
		}

		private static Shape WalkContours(List<EdgeSegment> edges, bool inverseY, out bool clean) {
			int n = edges.Count;
			Vector2[] starts = new Vector2[n];
			Vector2[] ends = new Vector2[n];
			for (int i = 0; i < n; i++) { starts[i] = edges[i].Point(0); ends[i] = edges[i].Point(1); }

			bool[] used = new bool[n];
			Shape shape = new Shape { InverseYAxis = inverseY };
			clean = true;
			WalkReason = "";

			for (int seed = 0; seed < n; seed++) {
				if (used[seed]) continue;
				Contour contour = new Contour();
				int cur = seed;
				int guard = 0;
				bool closed = false;
				while (cur >= 0 && !used[cur]) {
					if (guard++ > n + 4) { clean = false; Note("guard"); break; }
					used[cur] = true;
					contour.Edges.Add(edges[cur]);
					Vector2 tail = ends[cur];
					if ((tail - starts[seed]).Length < PointEps * 2.0) { closed = true; break; }

					Vector2 inDir = edges[cur].Direction(1);
					if (!inDir.IsNonZero) inDir = ends[cur] - starts[cur];
					inDir = inDir.Normalize(true);

					int best = -1, second = -1;
					int candidates = 0;
					double bestScore = double.NegativeInfinity, secondScore = double.NegativeInfinity;
					for (int k = 0; k < n; k++) {
						if (used[k] || (starts[k] - tail).Length >= PointEps * 2.0) continue;
						candidates++;
						Vector2 outDir = edges[k].Direction(0);
						if (!outDir.IsNonZero) outDir = ends[k] - starts[k];
						outDir = outDir.Normalize(true);
						// keep filled region on the right → at a junction pick the sharpest right turn:
						// maximise the clockwise turn from inDir to outDir (cross < 0 = clockwise in y-up).
						double cross = Vector2.Cross(inDir, outDir);
						double dot = Vector2.Dot(inDir, outDir);
						double turn = Math.Atan2(cross, dot);      // (-π, π], negative = clockwise
						double score = -turn;                        // larger = sharper right turn
						if (score > bestScore) { secondScore = bestScore; second = best; bestScore = score; best = k; }
						else if (score > secondScore) { secondScore = score; second = k; }
					}
					// A 3+-way junction is fine as long as the sharpest right turn is unambiguous — that
					// is the normal case for a font built from stacked/overlapping stroke pieces (the 'h'
					// ascender meeting the bowl). Only bail when the two best continuations leave within
					// ~11° of each other: the dense contour tangle in the € sign, where the walk genuinely
					// cannot tell which edge is the outline.
					if (best >= 0 && second >= 0 && bestScore - secondScore < 0.20) { clean = false; Note("ambiguous junction"); }
					cur = best;
				}

				if (!closed) { clean = false; Note("dead end"); }   // ran out of edges before closing
				if (contour.Edges.Count >= 2) {
					WeldLoop(contour);
					shape.Contours.Add(contour);
				} else if (contour.Edges.Count > 0) {
					clean = false; Note("stub contour");
				}
			}

			for (int i = 0; i < n; i++) if (!used[i]) { clean = false; Note("orphan edge"); }
			return shape;
		}

		/// <summary>Diagnostic: which check(s) marked the last <see cref="WalkContours"/> unclean.</summary>
		private static string WalkReason = "";
		private static void Note(string r) { if (WalkReason.IndexOf(r, StringComparison.Ordinal) < 0) WalkReason = WalkReason.Length == 0 ? r : WalkReason + "," + r; }

		/// <summary>Snaps each edge's endpoints to the shared join point so the contour is exactly closed.</summary>
		private static void WeldLoop(Contour contour) {
			int m = contour.Edges.Count;
			for (int i = 0; i + 1 < m; i++) {
				EdgeSegment cur = contour.Edges[i];
				EdgeSegment nxt = contour.Edges[i + 1];
				Vector2 join = 0.5 * (cur.Point(1) + nxt.Point(0));
				cur.MoveEndPoint(join);
				nxt.MoveStartPoint(join);
			}
			EdgeSegment last = contour.Edges[m - 1], first = contour.Edges[0];
			if ((last.Point(1) - first.Point(0)).Length < 8.0 * PointEps) {
				Vector2 join = 0.5 * (last.Point(1) + first.Point(0));
				last.MoveEndPoint(join);
				first.MoveStartPoint(join);
			} else {
				// walk did not close cleanly — bridge the gap so the contour is still valid
				contour.Edges.Add(new EdgeSegment.LinearSegment(last.Point(1), first.Point(0), first.Color));
			}
		}
	}
}
