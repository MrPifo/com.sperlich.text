// C# port of msdfgen/core/convergent-curve-ordering.cpp — https://github.com/Chlumsky/msdfgen (MIT).
// Pointer arithmetic in the original is expressed here as an array with a moving base index.
using System;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	internal static class ConvergentCurveOrdering {

		private static void SimplifyDegenerateCurve(Vector2[] cp, int baseIdx, ref int order) {
			if (order == 3 &&
				(cp[baseIdx + 1] == cp[baseIdx + 0] || cp[baseIdx + 1] == cp[baseIdx + 3]) &&
				(cp[baseIdx + 2] == cp[baseIdx + 0] || cp[baseIdx + 2] == cp[baseIdx + 3])) {
				cp[baseIdx + 1] = cp[baseIdx + 3];
				order = 1;
			}
			if (order == 2 && (cp[baseIdx + 1] == cp[baseIdx + 0] || cp[baseIdx + 1] == cp[baseIdx + 2])) {
				cp[baseIdx + 1] = cp[baseIdx + 2];
				order = 1;
			}
			if (order == 1 && cp[baseIdx + 0] == cp[baseIdx + 1])
				order = 0;
		}

		// corner points into cp; cp[corner-k] are the "before" control points, cp[corner+k] the "after" ones.
		private static int Ordering(Vector2[] cp, int corner, int controlPointsBefore, int controlPointsAfter) {
			if (!(controlPointsBefore > 0 && controlPointsAfter > 0))
				return 0;
			Vector2 a1 = new Vector2(0), a2 = new Vector2(0), a3 = new Vector2(0);
			Vector2 b1 = new Vector2(0), b2 = new Vector2(0), b3 = new Vector2(0);
			a1 = cp[corner - 1] - cp[corner];
			b1 = cp[corner + 1] - cp[corner];
			if (controlPointsBefore >= 2)
				a2 = cp[corner - 2] - cp[corner - 1] - a1;
			if (controlPointsAfter >= 2)
				b2 = cp[corner + 2] - cp[corner + 1] - b1;
			if (controlPointsBefore >= 3) {
				a3 = cp[corner - 3] - cp[corner - 2] - (cp[corner - 2] - cp[corner - 1]) - a2;
				a2 *= 3;
			}
			if (controlPointsAfter >= 3) {
				b3 = cp[corner + 3] - cp[corner + 2] - (cp[corner + 2] - cp[corner + 1]) - b2;
				b2 *= 3;
			}
			a1 *= controlPointsBefore;
			b1 *= controlPointsAfter;

			// Non-degenerate case
			if (a1.IsNonZero && b1.IsNonZero) {
				double as1 = a1.Length;
				double bs = b1.Length;
				double d = as1 * CrossProduct(a1, b2) + bs * CrossProduct(a2, b1);
				if (d != 0) return Sign(d);
				d = as1 * as1 * CrossProduct(a1, b3) + as1 * bs * CrossProduct(a2, b2) + bs * bs * CrossProduct(a3, b1);
				if (d != 0) return Sign(d);
				d = as1 * CrossProduct(a2, b3) + bs * CrossProduct(a3, b2);
				if (d != 0) return Sign(d);
				return Sign(CrossProduct(a3, b3));
			}

			int s = 1;
			if (a1.IsNonZero) { // !b1 — swap the a/b roles, then fall into the b1 branch
				b1 = a1;
				(a2, b2) = (b2, a2);
				(a3, b3) = (b3, a3);
				s = -1;
			}
			if (b1.IsNonZero) { // !a1
				double d = CrossProduct(a3, b1);
				if (d != 0) return s * Sign(d);
				d = CrossProduct(a2, b2);
				if (d != 0) return s * Sign(d);
				d = CrossProduct(a3, b2);
				if (d != 0) return s * Sign(d);
				d = CrossProduct(a2, b3);
				if (d != 0) return s * Sign(d);
				return s * Sign(CrossProduct(a3, b3));
			}
			{ // !a1 && !b1
				double d = Math.Sqrt(a2.Length) * CrossProduct(a2, b3) + Math.Sqrt(b2.Length) * CrossProduct(a3, b2);
				if (d != 0) return Sign(d);
				return Sign(CrossProduct(a3, b3));
			}
		}

		public static int Compute(EdgeSegment a, EdgeSegment b) {
			Vector2[] cp = new Vector2[12];
			const int corner = 4;
			const int aCpTmp = 8;
			int aOrder = a.Type;
			int bOrder = b.Type;
			if (!(aOrder >= 1 && aOrder <= 3 && bOrder >= 1 && bOrder <= 3))
				return 0;
			for (int i = 0; i <= aOrder; ++i) cp[aCpTmp + i] = a.ControlPoints[i];
			for (int i = 0; i <= bOrder; ++i) cp[corner + i] = b.ControlPoints[i];
			if (cp[aCpTmp + aOrder] != cp[corner])
				return 0;
			SimplifyDegenerateCurve(cp, aCpTmp, ref aOrder);
			SimplifyDegenerateCurve(cp, corner, ref bOrder);
			for (int i = 0; i < aOrder; ++i)
				cp[corner + i - aOrder] = cp[aCpTmp + i];
			return Ordering(cp, corner, aOrder, bOrder);
		}
	}
}
