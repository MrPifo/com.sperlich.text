// C# port of msdfgen/core/equation-solver.cpp — https://github.com/Chlumsky/msdfgen (MIT).
using System;

namespace Sperlich.Text.Rasterizer {

	public static class EquationSolver {

		private const double M_PI = Math.PI;

		/// <summary>ax^2 + bx + c = 0. Fills <paramref name="x"/> (length ≥ 2), returns root count (-1 = all reals).</summary>
		public static int SolveQuadratic(double[] x, double a, double b, double c) {
			// a == 0 -> linear equation
			if (a == 0 || Math.Abs(b) > 1e12 * Math.Abs(a)) {
				// a == 0, b == 0 -> no solution
				if (b == 0) {
					if (c == 0) return -1; // 0 == 0
					return 0;
				}
				x[0] = -c / b;
				return 1;
			}
			double dscr = b * b - 4 * a * c;
			if (dscr > 0) {
				dscr = Math.Sqrt(dscr);
				x[0] = (-b + dscr) / (2 * a);
				x[1] = (-b - dscr) / (2 * a);
				return 2;
			} else if (dscr == 0) {
				x[0] = -b / (2 * a);
				return 1;
			}
			return 0;
		}

		private static int SolveCubicNormed(double[] x, double a, double b, double c) {
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
				x[0] = q * Math.Cos(1 / 3.0 * t) - a;
				x[1] = q * Math.Cos(1 / 3.0 * (t + 2 * M_PI)) - a;
				x[2] = q * Math.Cos(1 / 3.0 * (t - 2 * M_PI)) - a;
				return 3;
			} else {
				double u = (r < 0 ? 1 : -1) * Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1 / 3.0);
				double v = u == 0 ? 0 : q / u;
				x[0] = (u + v) - a;
				if (u == v || Math.Abs(u - v) < 1e-12 * Math.Abs(u + v)) {
					x[1] = -0.5 * (u + v) - a;
					return 2;
				}
				return 1;
			}
		}

		/// <summary>ax^3 + bx^2 + cx + d = 0. Fills <paramref name="x"/> (length ≥ 3), returns root count.</summary>
		public static int SolveCubic(double[] x, double a, double b, double c, double d) {
			if (a != 0) {
				double bn = b / a;
				if (Math.Abs(bn) < 1e6) // above this ratio the numerical error exceeds treating a as zero
					return SolveCubicNormed(x, bn, c / a, d / a);
			}
			return SolveQuadratic(x, b, c, d);
		}
	}
}
