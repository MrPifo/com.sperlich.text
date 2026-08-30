// C# port of msdfgen/core/equation-solver.cpp — https://github.com/Chlumsky/msdfgen (MIT).
using System;

namespace Sperlich.Text.Rasterizer {

	public static class EquationSolver {

		private const double M_PI = Math.PI;

		public struct Roots3 {
			public double r0, r1, r2;
			public int count;

			public Roots3(double r0) { this.r0 = r0; this.r1 = 0; this.r2 = 0; this.count = 1; }
			public Roots3(double r0, double r1) { this.r0 = r0; this.r1 = r1; this.r2 = 0; this.count = 2; }
			public Roots3(double r0, double r1, double r2) { this.r0 = r0; this.r1 = r1; this.r2 = r2; this.count = 3; }
		}

		public static Roots3 SolveQuadraticNoAlloc(double a, double b, double c) {
			if (a == 0 || Math.Abs(b) > 1e12 * Math.Abs(a)) {
				if (b == 0) {
					return new Roots3 { count = (c == 0 ? -1 : 0) };
				}
				return new Roots3(-c / b);
			}
			double dscr = b * b - 4 * a * c;
			if (dscr > 0) {
				dscr = Math.Sqrt(dscr);
				return new Roots3((-b + dscr) / (2 * a), (-b - dscr) / (2 * a));
			} else if (dscr == 0) {
				return new Roots3(-b / (2 * a));
			}
			return default;
		}

		private static Roots3 SolveCubicNormedNoAlloc(double a, double b, double c) {
			double a2 = a * a;
			double q = 1 / 9.0 * (a2 - 3 * b);
			double r = 1 / 54.0 * (a * (2 * a2 - 9 * b) + 27 * c);
			double r2 = r * r;
			double q3 = q * q * q;
			a *= 1 / 3.0;
			if (r2 < q3) {
				double t = r / Math.Sqrt(q3);
				if (t < -1) t = -1;
				if (t > 1) t = 1;
				t = Math.Acos(t);
				q = -2 * Math.Sqrt(q);
				return new Roots3(
					q * Math.Cos(1 / 3.0 * t) - a,
					q * Math.Cos(1 / 3.0 * (t + 2 * M_PI)) - a,
					q * Math.Cos(1 / 3.0 * (t - 2 * M_PI)) - a
				);
			} else {
				double u = (r < 0 ? 1 : -1) * Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1 / 3.0);
				double v = u == 0 ? 0 : q / u;
				double x0 = (u + v) - a;
				if (u == v || Math.Abs(u - v) < 1e-12 * Math.Abs(u + v)) {
					return new Roots3(x0, -0.5 * (u + v) - a);
				}
				return new Roots3(x0);
			}
		}

		public static Roots3 SolveCubicNoAlloc(double a, double b, double c, double d) {
			if (a != 0) {
				double bn = b / a;
				if (Math.Abs(bn) < 1e6)
					return SolveCubicNormedNoAlloc(bn, c / a, d / a);
			}
			return SolveQuadraticNoAlloc(b, c, d);
		}

		/// <summary>ax^2 + bx + c = 0. Fills <paramref name="x"/> (length ≥ 2), returns root count (-1 = all reals).</summary>
		public static int SolveQuadratic(double[] x, double a, double b, double c) {
			Roots3 r = SolveQuadraticNoAlloc(a, b, c);
			if (r.count >= 1) x[0] = r.r0;
			if (r.count >= 2) x[1] = r.r1;
			return r.count;
		}

		/// <summary>ax^3 + bx^2 + cx + d = 0. Fills <paramref name="x"/> (length ≥ 3), returns root count.</summary>
		public static int SolveCubic(double[] x, double a, double b, double c, double d) {
			Roots3 r = SolveCubicNoAlloc(a, b, c, d);
			if (r.count >= 1) x[0] = r.r0;
			if (r.count >= 2) x[1] = r.r1;
			if (r.count >= 3) x[2] = r.r2;
			return r.count;
		}
	}
}
