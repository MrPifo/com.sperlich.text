// C# port of msdfgen/core/SignedDistance.hpp — https://github.com/Chlumsky/msdfgen (MIT).
using System;

namespace Sperlich.Text.Rasterizer {

	/// <summary>
	/// A signed distance plus an alignment ("dot") value; together they uniquely order the closest
	/// edge segment. Comparisons are by absolute distance first, then by dot.
	/// </summary>
	public struct SignedDistance {

		public double distance;
		public double dot;

		/// <summary>msdfgen's default: distance = -DBL_MAX, dot = 0.</summary>
		public static readonly SignedDistance Infinite = new SignedDistance(double.MinValue, 0);

		public SignedDistance(double dist, double d) { distance = dist; dot = d; }

		public static bool operator <(SignedDistance a, SignedDistance b) =>
			Math.Abs(a.distance) < Math.Abs(b.distance) ||
			(Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot < b.dot);

		public static bool operator >(SignedDistance a, SignedDistance b) =>
			Math.Abs(a.distance) > Math.Abs(b.distance) ||
			(Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot > b.dot);

		public static bool operator <=(SignedDistance a, SignedDistance b) =>
			Math.Abs(a.distance) < Math.Abs(b.distance) ||
			(Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot <= b.dot);

		public static bool operator >=(SignedDistance a, SignedDistance b) =>
			Math.Abs(a.distance) > Math.Abs(b.distance) ||
			(Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot >= b.dot);
	}
}
