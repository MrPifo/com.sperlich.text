// C# port of msdfgen/core/edge-segments.cpp / .h — https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky).
// Independent port, not a wrapper. Kept structurally close to the original for reviewability.
using System;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	/// <summary>An abstract edge segment (line, quadratic or cubic Bezier).</summary>
	public abstract class EdgeSegment {

		// Iterative closest-point search parameters for cubic curves.
		private const int CubicSearchStarts = 4;
		private const int CubicSearchSteps = 4;

		public EdgeColor Color;

		protected EdgeSegment(EdgeColor color) { Color = color; }

		public static EdgeSegment Create(Vector2 p0, Vector2 p1, EdgeColor color = EdgeColor.White) =>
			new LinearSegment(p0, p1, color);

		public static EdgeSegment Create(Vector2 p0, Vector2 p1, Vector2 p2, EdgeColor color = EdgeColor.White) {
			if (CrossProduct(p1 - p0, p2 - p1) == 0)
				return new LinearSegment(p0, p2, color);
			return new QuadraticSegment(p0, p1, p2, color);
		}

		public static EdgeSegment Create(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, EdgeColor color = EdgeColor.White) {
			Vector2 p12 = p2 - p1;
			if (CrossProduct(p1 - p0, p12) == 0 && CrossProduct(p12, p3 - p2) == 0)
				return new LinearSegment(p0, p3, color);
			Vector2 t = 1.5 * p1 - 0.5 * p0;
			if (t == 1.5 * p2 - 0.5 * p3)
				return new QuadraticSegment(p0, t, p3, color);
			return new CubicSegment(p0, p1, p2, p3, color);
		}

		public abstract EdgeSegment Clone();
		/// <summary>1 = linear, 2 = quadratic, 3 = cubic.</summary>
		public abstract int Type { get; }
		/// <summary>The control-point array (length 2 / 3 / 4).</summary>
		public abstract Vector2[] ControlPoints { get; }
		public abstract Vector2 Point(double param);
		public abstract Vector2 Direction(double param);
		public abstract Vector2 DirectionChange(double param);
		public abstract SignedDistance SignedDistanceTo(Vector2 origin, out double param);
		public abstract int ScanlineIntersections(double[] x, int[] dy, double y);
		public abstract void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax);
		public abstract void Reverse();
		public abstract void MoveStartPoint(Vector2 to);
		public abstract void MoveEndPoint(Vector2 to);
		public abstract void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2);

		/// <summary>Converts a previously retrieved signed distance from origin to perpendicular distance.</summary>
		public void DistanceToPerpendicularDistance(ref SignedDistance distance, Vector2 origin, double param) {
			if (param < 0) {
				Vector2 dir = Direction(0).Normalize();
				Vector2 aq = origin - Point(0);
				double ts = DotProduct(aq, dir);
				if (ts < 0) {
					double perpendicularDistance = CrossProduct(aq, dir);
					if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance)) {
						distance.distance = perpendicularDistance;
						distance.dot = 0;
					}
				}
			} else if (param > 1) {
				Vector2 dir = Direction(1).Normalize();
				Vector2 bq = origin - Point(1);
				double ts = DotProduct(bq, dir);
				if (ts > 0) {
					double perpendicularDistance = CrossProduct(bq, dir);
					if (Math.Abs(perpendicularDistance) <= Math.Abs(distance.distance)) {
						distance.distance = perpendicularDistance;
						distance.dot = 0;
					}
				}
			}
		}

		protected static void PointBounds(Vector2 p, ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
			if (p.x < xMin) xMin = p.x;
			if (p.y < yMin) yMin = p.y;
			if (p.x > xMax) xMax = p.x;
			if (p.y > yMax) yMax = p.y;
		}

		// ----------------------------------------------------------------------------------------------

		public sealed class LinearSegment : EdgeSegment {

			public readonly Vector2[] p = new Vector2[2];

			public LinearSegment(Vector2 p0, Vector2 p1, EdgeColor color = EdgeColor.White) : base(color) {
				p[0] = p0; p[1] = p1;
			}

			public override EdgeSegment Clone() => new LinearSegment(p[0], p[1], Color);
			public override int Type => 1;
			public override Vector2[] ControlPoints => p;

			public override Vector2 Point(double param) => Mix(p[0], p[1], param);
			public override Vector2 Direction(double param) => p[1] - p[0];
			public override Vector2 DirectionChange(double param) => new Vector2(0);

			public double Length() => (p[1] - p[0]).Length;

			public override SignedDistance SignedDistanceTo(Vector2 origin, out double param) {
				Vector2 aq = origin - p[0];
				Vector2 ab = p[1] - p[0];
				param = DotProduct(aq, ab) / DotProduct(ab, ab);
				Vector2 eq = p[param > 0.5 ? 1 : 0] - origin;
				double endpointDistance = eq.Length;
				if (param > 0 && param < 1) {
					double orthoDistance = DotProduct(ab.GetOrthonormal(false), aq);
					if (Math.Abs(orthoDistance) < endpointDistance)
						return new SignedDistance(orthoDistance, 0);
				}
				return new SignedDistance(
					NonZeroSign(CrossProduct(aq, ab)) * endpointDistance,
					Math.Abs(DotProduct(ab.Normalize(), eq.Normalize())));
			}

			public override int ScanlineIntersections(double[] x, int[] dy, double y) {
				if ((y >= p[0].y && y < p[1].y) || (y >= p[1].y && y < p[0].y)) {
					double param = (y - p[0].y) / (p[1].y - p[0].y);
					x[0] = Mix(p[0].x, p[1].x, param);
					dy[0] = Sign(p[1].y - p[0].y);
					return 1;
				}
				return 0;
			}

			public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
				PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
				PointBounds(p[1], ref xMin, ref yMin, ref xMax, ref yMax);
			}

			public override void Reverse() { (p[0], p[1]) = (p[1], p[0]); }
			public override void MoveStartPoint(Vector2 to) { p[0] = to; }
			public override void MoveEndPoint(Vector2 to) { p[1] = to; }

			public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2) {
				part0 = new LinearSegment(p[0], Point(1 / 3.0), Color);
				part1 = new LinearSegment(Point(1 / 3.0), Point(2 / 3.0), Color);
				part2 = new LinearSegment(Point(2 / 3.0), p[1], Color);
			}
		}

		// ----------------------------------------------------------------------------------------------

		public sealed class QuadraticSegment : EdgeSegment {

			public readonly Vector2[] p = new Vector2[3];

			public QuadraticSegment(Vector2 p0, Vector2 p1, Vector2 p2, EdgeColor color = EdgeColor.White) : base(color) {
				p[0] = p0; p[1] = p1; p[2] = p2;
			}

			public override EdgeSegment Clone() => new QuadraticSegment(p[0], p[1], p[2], Color);
			public override int Type => 2;
			public override Vector2[] ControlPoints => p;

			public override Vector2 Point(double param) =>
				Mix(Mix(p[0], p[1], param), Mix(p[1], p[2], param), param);

			public override Vector2 Direction(double param) {
				Vector2 tangent = Mix(p[1] - p[0], p[2] - p[1], param);
				if (!tangent.IsNonZero) return p[2] - p[0];
				return tangent;
			}

			public override Vector2 DirectionChange(double param) => (p[2] - p[1]) - (p[1] - p[0]);

			public double Length() {
				Vector2 ab = p[1] - p[0];
				Vector2 br = p[2] - p[1] - ab;
				double abab = DotProduct(ab, ab);
				double abbr = DotProduct(ab, br);
				double brbr = DotProduct(br, br);
				double abLen = Math.Sqrt(abab);
				double brLen = Math.Sqrt(brbr);
				double crs = CrossProduct(ab, br);
				double h = Math.Sqrt(abab + abbr + abbr + brbr);
				return (
					brLen * ((abbr + brbr) * h - abbr * abLen) +
					crs * crs * Math.Log((brLen * h + abbr + brbr) / (brLen * abLen + abbr))
				) / (brbr * brLen);
			}

			public override SignedDistance SignedDistanceTo(Vector2 origin, out double param) {
				Vector2 qa = p[0] - origin;
				Vector2 ab = p[1] - p[0];
				Vector2 br = p[2] - p[1] - ab;
				double a = DotProduct(br, br);
				double b = 3 * DotProduct(ab, br);
				double c = 2 * DotProduct(ab, ab) + DotProduct(qa, br);
				double d = DotProduct(qa, ab);
				double[] t = new double[3];
				int solutions = EquationSolver.SolveCubic(t, a, b, c, d);

				Vector2 epDir = Direction(0);
				double minDistance = NonZeroSign(CrossProduct(epDir, qa)) * qa.Length; // distance from A
				param = -DotProduct(qa, epDir) / DotProduct(epDir, epDir);
				{
					double distance = (p[2] - origin).Length; // distance from B
					if (distance < Math.Abs(minDistance)) {
						epDir = Direction(1);
						minDistance = NonZeroSign(CrossProduct(epDir, p[2] - origin)) * distance;
						param = DotProduct(origin - p[1], epDir) / DotProduct(epDir, epDir);
					}
				}
				for (int i = 0; i < solutions; ++i) {
					if (t[i] > 0 && t[i] < 1) {
						Vector2 qe = qa + 2 * t[i] * ab + t[i] * t[i] * br;
						double distance = qe.Length;
						if (distance <= Math.Abs(minDistance)) {
							minDistance = NonZeroSign(CrossProduct(ab + t[i] * br, qe)) * distance;
							param = t[i];
						}
					}
				}

				if (param >= 0 && param <= 1)
					return new SignedDistance(minDistance, 0);
				if (param < 0.5)
					return new SignedDistance(minDistance, Math.Abs(DotProduct(Direction(0).Normalize(), qa.Normalize())));
				return new SignedDistance(minDistance, Math.Abs(DotProduct(Direction(1).Normalize(), (p[2] - origin).Normalize())));
			}

			public override int ScanlineIntersections(double[] x, int[] dy, double y) {
				int total = 0;
				int nextDY = y > p[0].y ? 1 : -1;
				x[total] = p[0].x;
				if (p[0].y == y) {
					if (p[0].y < p[1].y || (p[0].y == p[1].y && p[0].y < p[2].y))
						dy[total++] = 1;
					else
						nextDY = 1;
				}
				{
					Vector2 ab = p[1] - p[0];
					Vector2 br = p[2] - p[1] - ab;
					double[] t = new double[2];
					int solutions = EquationSolver.SolveQuadratic(t, br.y, 2 * ab.y, p[0].y - y);
					if (solutions >= 2 && t[0] > t[1]) (t[0], t[1]) = (t[1], t[0]);
					for (int i = 0; i < solutions && total < 2; ++i) {
						if (t[i] >= 0 && t[i] <= 1) {
							x[total] = p[0].x + 2 * t[i] * ab.x + t[i] * t[i] * br.x;
							if (nextDY * (ab.y + t[i] * br.y) >= 0) {
								dy[total++] = nextDY;
								nextDY = -nextDY;
							}
						}
					}
				}
				if (p[2].y == y) {
					if (nextDY > 0 && total > 0) {
						--total;
						nextDY = -1;
					}
					if ((p[2].y < p[1].y || (p[2].y == p[1].y && p[2].y < p[0].y)) && total < 2) {
						x[total] = p[2].x;
						if (nextDY < 0) {
							dy[total++] = -1;
							nextDY = 1;
						}
					}
				}
				if (nextDY != (y >= p[2].y ? 1 : -1)) {
					if (total > 0)
						--total;
					else {
						if (Math.Abs(p[2].y - y) < Math.Abs(p[0].y - y))
							x[total] = p[2].x;
						dy[total++] = nextDY;
					}
				}
				return total;
			}

			public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
				PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
				PointBounds(p[2], ref xMin, ref yMin, ref xMax, ref yMax);
				Vector2 bot = (p[1] - p[0]) - (p[2] - p[1]);
				if (bot.x != 0) {
					double param = (p[1].x - p[0].x) / bot.x;
					if (param > 0 && param < 1) PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
				}
				if (bot.y != 0) {
					double param = (p[1].y - p[0].y) / bot.y;
					if (param > 0 && param < 1) PointBounds(Point(param), ref xMin, ref yMin, ref xMax, ref yMax);
				}
			}

			public override void Reverse() { (p[0], p[2]) = (p[2], p[0]); }

			public override void MoveStartPoint(Vector2 to) {
				Vector2 origSDir = p[0] - p[1];
				Vector2 origP1 = p[1];
				p[1] += CrossProduct(p[0] - p[1], to - p[0]) / CrossProduct(p[0] - p[1], p[2] - p[1]) * (p[2] - p[1]);
				p[0] = to;
				if (DotProduct(origSDir, p[0] - p[1]) < 0)
					p[1] = origP1;
			}

			public override void MoveEndPoint(Vector2 to) {
				Vector2 origEDir = p[2] - p[1];
				Vector2 origP1 = p[1];
				p[1] += CrossProduct(p[2] - p[1], to - p[2]) / CrossProduct(p[2] - p[1], p[0] - p[1]) * (p[0] - p[1]);
				p[2] = to;
				if (DotProduct(origEDir, p[2] - p[1]) < 0)
					p[1] = origP1;
			}

			public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2) {
				part0 = new QuadraticSegment(p[0], Mix(p[0], p[1], 1 / 3.0), Point(1 / 3.0), Color);
				part1 = new QuadraticSegment(Point(1 / 3.0),
					Mix(Mix(p[0], p[1], 5 / 9.0), Mix(p[1], p[2], 4 / 9.0), 0.5), Point(2 / 3.0), Color);
				part2 = new QuadraticSegment(Point(2 / 3.0), Mix(p[1], p[2], 2 / 3.0), p[2], Color);
			}

			public EdgeSegment ConvertToCubic() =>
				new CubicSegment(p[0], Mix(p[0], p[1], 2 / 3.0), Mix(p[1], p[2], 1 / 3.0), p[2], Color);
		}

		// ----------------------------------------------------------------------------------------------

		public sealed class CubicSegment : EdgeSegment {

			public readonly Vector2[] p = new Vector2[4];

			public CubicSegment(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, EdgeColor color = EdgeColor.White) : base(color) {
				p[0] = p0; p[1] = p1; p[2] = p2; p[3] = p3;
			}

			public override EdgeSegment Clone() => new CubicSegment(p[0], p[1], p[2], p[3], Color);
			public override int Type => 3;
			public override Vector2[] ControlPoints => p;

			public override Vector2 Point(double param) {
				Vector2 p12 = Mix(p[1], p[2], param);
				return Mix(Mix(Mix(p[0], p[1], param), p12, param),
					Mix(p12, Mix(p[2], p[3], param), param), param);
			}

			public override Vector2 Direction(double param) {
				Vector2 tangent = Mix(Mix(p[1] - p[0], p[2] - p[1], param), Mix(p[2] - p[1], p[3] - p[2], param), param);
				if (!tangent.IsNonZero) {
					if (param == 0) return p[2] - p[0];
					if (param == 1) return p[3] - p[1];
				}
				return tangent;
			}

			public override Vector2 DirectionChange(double param) =>
				Mix((p[2] - p[1]) - (p[1] - p[0]), (p[3] - p[2]) - (p[2] - p[1]), param);

			public override SignedDistance SignedDistanceTo(Vector2 origin, out double param) {
				Vector2 qa = p[0] - origin;
				Vector2 ab = p[1] - p[0];
				Vector2 br = p[2] - p[1] - ab;
				Vector2 as_ = (p[3] - p[2]) - (p[2] - p[1]) - br;

				Vector2 epDir = Direction(0);
				double minDistance = NonZeroSign(CrossProduct(epDir, qa)) * qa.Length; // distance from A
				param = -DotProduct(qa, epDir) / DotProduct(epDir, epDir);
				{
					double distance = (p[3] - origin).Length; // distance from B
					if (distance < Math.Abs(minDistance)) {
						epDir = Direction(1);
						minDistance = NonZeroSign(CrossProduct(epDir, p[3] - origin)) * distance;
						param = DotProduct(epDir - (p[3] - origin), epDir) / DotProduct(epDir, epDir);
					}
				}
				// Iterative minimum distance search
				for (int i = 0; i <= CubicSearchStarts; ++i) {
					double t = 1.0 / CubicSearchStarts * i;
					Vector2 qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * as_;
					Vector2 d1 = 3 * ab + 6 * t * br + 3 * t * t * as_;
					Vector2 d2 = 6 * br + 6 * t * as_;
					double improvedT = t - DotProduct(qe, d1) / (DotProduct(d1, d1) + DotProduct(qe, d2));
					if (improvedT > 0 && improvedT < 1) {
						int remainingSteps = CubicSearchSteps;
						do {
							t = improvedT;
							qe = qa + 3 * t * ab + 3 * t * t * br + t * t * t * as_;
							d1 = 3 * ab + 6 * t * br + 3 * t * t * as_;
							if (--remainingSteps == 0) break;
							d2 = 6 * br + 6 * t * as_;
							improvedT = t - DotProduct(qe, d1) / (DotProduct(d1, d1) + DotProduct(qe, d2));
						} while (improvedT > 0 && improvedT < 1);
						double distance = qe.Length;
						if (distance < Math.Abs(minDistance)) {
							minDistance = NonZeroSign(CrossProduct(d1, qe)) * distance;
							param = t;
						}
					}
				}

				if (param >= 0 && param <= 1)
					return new SignedDistance(minDistance, 0);
				if (param < 0.5)
					return new SignedDistance(minDistance, Math.Abs(DotProduct(Direction(0).Normalize(), qa.Normalize())));
				return new SignedDistance(minDistance, Math.Abs(DotProduct(Direction(1).Normalize(), (p[3] - origin).Normalize())));
			}

			public override int ScanlineIntersections(double[] x, int[] dy, double y) {
				int total = 0;
				int nextDY = y > p[0].y ? 1 : -1;
				x[total] = p[0].x;
				if (p[0].y == y) {
					if (p[0].y < p[1].y || (p[0].y == p[1].y && (p[0].y < p[2].y || (p[0].y == p[2].y && p[0].y < p[3].y))))
						dy[total++] = 1;
					else
						nextDY = 1;
				}
				{
					Vector2 ab = p[1] - p[0];
					Vector2 br = p[2] - p[1] - ab;
					Vector2 as_ = (p[3] - p[2]) - (p[2] - p[1]) - br;
					double[] t = new double[3];
					int solutions = EquationSolver.SolveCubic(t, as_.y, 3 * br.y, 3 * ab.y, p[0].y - y);
					if (solutions >= 2) {
						if (t[0] > t[1]) (t[0], t[1]) = (t[1], t[0]);
						if (solutions >= 3 && t[1] > t[2]) {
							(t[1], t[2]) = (t[2], t[1]);
							if (t[0] > t[1]) (t[0], t[1]) = (t[1], t[0]);
						}
					}
					for (int i = 0; i < solutions && total < 3; ++i) {
						if (t[i] >= 0 && t[i] <= 1) {
							x[total] = p[0].x + 3 * t[i] * ab.x + 3 * t[i] * t[i] * br.x + t[i] * t[i] * t[i] * as_.x;
							if (nextDY * (ab.y + 2 * t[i] * br.y + t[i] * t[i] * as_.y) >= 0) {
								dy[total++] = nextDY;
								nextDY = -nextDY;
							}
						}
					}
				}
				if (p[3].y == y) {
					if (nextDY > 0 && total > 0) {
						--total;
						nextDY = -1;
					}
					if ((p[3].y < p[2].y || (p[3].y == p[2].y && (p[3].y < p[1].y || (p[3].y == p[1].y && p[3].y < p[0].y)))) && total < 3) {
						x[total] = p[3].x;
						if (nextDY < 0) {
							dy[total++] = -1;
							nextDY = 1;
						}
					}
				}
				if (nextDY != (y >= p[3].y ? 1 : -1)) {
					if (total > 0)
						--total;
					else {
						if (Math.Abs(p[3].y - y) < Math.Abs(p[0].y - y))
							x[total] = p[3].x;
						dy[total++] = nextDY;
					}
				}
				return total;
			}

			public override void Bound(ref double xMin, ref double yMin, ref double xMax, ref double yMax) {
				PointBounds(p[0], ref xMin, ref yMin, ref xMax, ref yMax);
				PointBounds(p[3], ref xMin, ref yMin, ref xMax, ref yMax);
				Vector2 a0 = p[1] - p[0];
				Vector2 a1 = 2 * (p[2] - p[1] - a0);
				Vector2 a2 = p[3] - 3 * p[2] + 3 * p[1] - p[0];
				double[] prm = new double[2];
				int solutions = EquationSolver.SolveQuadratic(prm, a2.x, a1.x, a0.x);
				for (int i = 0; i < solutions; ++i)
					if (prm[i] > 0 && prm[i] < 1) PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
				solutions = EquationSolver.SolveQuadratic(prm, a2.y, a1.y, a0.y);
				for (int i = 0; i < solutions; ++i)
					if (prm[i] > 0 && prm[i] < 1) PointBounds(Point(prm[i]), ref xMin, ref yMin, ref xMax, ref yMax);
			}

			public override void Reverse() {
				(p[0], p[3]) = (p[3], p[0]);
				(p[1], p[2]) = (p[2], p[1]);
			}

			public override void MoveStartPoint(Vector2 to) {
				p[1] += to - p[0];
				p[0] = to;
			}

			public override void MoveEndPoint(Vector2 to) {
				p[2] += to - p[3];
				p[3] = to;
			}

			public override void SplitInThirds(out EdgeSegment part0, out EdgeSegment part1, out EdgeSegment part2) {
				part0 = new CubicSegment(p[0],
					p[0] == p[1] ? p[0] : Mix(p[0], p[1], 1 / 3.0),
					Mix(Mix(p[0], p[1], 1 / 3.0), Mix(p[1], p[2], 1 / 3.0), 1 / 3.0),
					Point(1 / 3.0), Color);
				part1 = new CubicSegment(Point(1 / 3.0),
					Mix(Mix(Mix(p[0], p[1], 1 / 3.0), Mix(p[1], p[2], 1 / 3.0), 1 / 3.0),
						Mix(Mix(p[1], p[2], 1 / 3.0), Mix(p[2], p[3], 1 / 3.0), 1 / 3.0), 2 / 3.0),
					Mix(Mix(Mix(p[0], p[1], 2 / 3.0), Mix(p[1], p[2], 2 / 3.0), 2 / 3.0),
						Mix(Mix(p[1], p[2], 2 / 3.0), Mix(p[2], p[3], 2 / 3.0), 2 / 3.0), 1 / 3.0),
					Point(2 / 3.0), Color);
				part2 = new CubicSegment(Point(2 / 3.0),
					Mix(Mix(p[1], p[2], 2 / 3.0), Mix(p[2], p[3], 2 / 3.0), 2 / 3.0),
					p[2] == p[3] ? p[3] : Mix(p[2], p[3], 2 / 3.0),
					p[3], Color);
			}
		}
	}
}
