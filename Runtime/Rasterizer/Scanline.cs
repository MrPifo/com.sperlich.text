// C# port of msdfgen/core/Scanline.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT).
using System;
using System.Collections.Generic;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	/// <summary>How an intersection total is interpreted during rasterization.</summary>
	public enum FillRule {
		NonZero,
		Odd, // "even-odd"
		Positive,
		Negative
	}

	/// <summary>A horizontal scanline intersecting a shape.</summary>
	public sealed class Scanline {

		public struct Intersection {
			public double x;
			/// <summary>Normalised Y direction of the oriented edge at the intersection.</summary>
			public int direction;
		}

		private readonly List<Intersection> intersections = new List<Intersection>();
		private int lastIndex;

		/// <summary>Resolves an intersection total to a binary fill value.</summary>
		public static bool InterpretFillRule(int intersections, FillRule fillRule) {
			switch (fillRule) {
				case FillRule.NonZero: return intersections != 0;
				case FillRule.Odd: return (intersections & 1) != 0;
				case FillRule.Positive: return intersections > 0;
				case FillRule.Negative: return intersections < 0;
			}
			return false;
		}

		public static double Overlap(Scanline a, Scanline b, double xFrom, double xTo, FillRule fillRule) {
			double total = 0;
			bool aInside = false, bInside = false;
			int ai = 0, bi = 0;
			double ax = a.intersections.Count != 0 ? a.intersections[ai].x : xTo;
			double bx = b.intersections.Count != 0 ? b.intersections[bi].x : xTo;
			while (ax < xFrom || bx < xFrom) {
				double xNext = Math.Min(ax, bx);
				if (ax == xNext && ai < a.intersections.Count) {
					aInside = InterpretFillRule(a.intersections[ai].direction, fillRule);
					ax = ++ai < a.intersections.Count ? a.intersections[ai].x : xTo;
				}
				if (bx == xNext && bi < b.intersections.Count) {
					bInside = InterpretFillRule(b.intersections[bi].direction, fillRule);
					bx = ++bi < b.intersections.Count ? b.intersections[bi].x : xTo;
				}
			}
			double x = xFrom;
			while (ax < xTo || bx < xTo) {
				double xNext = Math.Min(ax, bx);
				if (aInside == bInside)
					total += xNext - x;
				if (ax == xNext && ai < a.intersections.Count) {
					aInside = InterpretFillRule(a.intersections[ai].direction, fillRule);
					ax = ++ai < a.intersections.Count ? a.intersections[ai].x : xTo;
				}
				if (bx == xNext && bi < b.intersections.Count) {
					bInside = InterpretFillRule(b.intersections[bi].direction, fillRule);
					bx = ++bi < b.intersections.Count ? b.intersections[bi].x : xTo;
				}
				x = xNext;
			}
			if (aInside == bInside)
				total += xTo - x;
			return total;
		}

		public void SetIntersections(List<Intersection> newIntersections) {
			intersections.Clear();
			intersections.AddRange(newIntersections);
			Preprocess();
		}

		private void Preprocess() {
			lastIndex = 0;
			if (intersections.Count != 0) {
				intersections.Sort((p, q) => Sign(p.x - q.x));
				int totalDirection = 0;
				for (int i = 0; i < intersections.Count; i++) {
					Intersection it = intersections[i];
					totalDirection += it.direction;
					it.direction = totalDirection;
					intersections[i] = it;
				}
			}
		}

		private int MoveTo(double x) {
			if (intersections.Count == 0)
				return -1;
			int index = lastIndex;
			if (x < intersections[index].x) {
				do {
					if (index == 0) {
						lastIndex = 0;
						return -1;
					}
					--index;
				} while (x < intersections[index].x);
			} else {
				while (index < intersections.Count - 1 && x >= intersections[index + 1].x)
					++index;
			}
			lastIndex = index;
			return index;
		}

		public int CountIntersections(double x) => MoveTo(x) + 1;

		public int SumIntersections(double x) {
			int index = MoveTo(x);
			if (index >= 0)
				return intersections[index].direction;
			return 0;
		}

		public bool Filled(double x, FillRule fillRule) => InterpretFillRule(SumIntersections(x), fillRule);
	}
}
