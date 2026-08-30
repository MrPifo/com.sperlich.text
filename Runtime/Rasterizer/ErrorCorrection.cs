// C# port of msdfgen/core/MSDFErrorCorrection.cpp + msdf-error-correction.cpp + bitmap-interpolation.hpp
// https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky). Independent port.
//
// Simplifications vs. the C++: our bitmaps are always Y-up (no reorient), and channel count is carried
// by FloatBitmap.Channels rather than a template <int N> — the error correction only ever reads/writes
// channels 0..2 (the MSDF), leaving an MTSDF's alpha channel 3 untouched, which matches the original.
using System;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	internal static class Interpolation {
		/// <summary>Bilinear sample of channels 0..2 of <paramref name="bmp"/> at pixel-space <paramref name="pos"/>.</summary>
		public static void InterpolateRGB(FloatBitmap bmp, Vector2 pos, float[] output3) {
			pos.x = Clamp(pos.x, (double) bmp.Width);
			pos.y = Clamp(pos.y, (double) bmp.Height);
			pos.x -= 0.5; pos.y -= 0.5;
			int l = (int) Math.Floor(pos.x);
			int b = (int) Math.Floor(pos.y);
			int r = l + 1, t = b + 1;
			double lr = pos.x - l;
			double bt = pos.y - b;
			l = Clamp(l, bmp.Width - 1); r = Clamp(r, bmp.Width - 1);
			b = Clamp(b, bmp.Height - 1); t = Clamp(t, bmp.Height - 1);
			int lb = bmp.PixelBase(l, b), rb = bmp.PixelBase(r, b), lt = bmp.PixelBase(l, t), rt = bmp.PixelBase(r, t);
			float[] d = bmp.Data;
			for (int i = 0; i < 3; ++i)
				output3[i] = (float) Mix(Mix(d[lb + i], d[rb + i], lr), Mix(d[lt + i], d[rt + i], lr), bt);
		}
	}

	internal abstract class ArtifactClassifierBase {
		protected double span;
		protected bool protectedFlag;

		protected ArtifactClassifierBase(double span, bool protectedFlag) {
			this.span = span;
			this.protectedFlag = protectedFlag;
		}

		public void SetState(double s, bool p) {
			span = s;
			protectedFlag = p;
		}

		public void SetProtected(bool p) => protectedFlag = p;

		internal const int FlagCandidate = 0x01;
		internal const int FlagArtifact = 0x02;

		/// <summary>Does median xm interpolated at xt (between am@at and bm@bt) indicate an artifact?</summary>
		public int RangeTest(double at, double bt, double xt, float am, float bm, float xm) {
			if ((am > 0.5f && bm > 0.5f && xm <= 0.5f) ||
				(am < 0.5f && bm < 0.5f && xm >= 0.5f) ||
				(!protectedFlag && Median(am, bm, xm) != xm)) {
				double axSpan = (xt - at) * span, bxSpan = (bt - xt) * span;
				if (!(xm >= am - axSpan && xm <= am + axSpan && xm >= bm - bxSpan && xm <= bm + bxSpan))
					return FlagCandidate | FlagArtifact;
				return FlagCandidate;
			}
			return 0;
		}

		public abstract bool Evaluate(double t, float m, int flags);
	}

	internal sealed class BaseArtifactClassifier : ArtifactClassifierBase {
		public BaseArtifactClassifier(double span, bool protectedFlag) : base(span, protectedFlag) { }
		public override bool Evaluate(double t, float m, int flags) => (flags & FlagArtifact) != 0;
	}

	/// <summary>
	/// Uses the exact shape distance (via a perpendicular-distance finder) to reject false-positive
	/// artifact candidates. Slow; used in the CHECK_DISTANCE modes.
	/// </summary>
	internal sealed class ShapeDistanceChecker {

		private readonly ShapeDistanceFinder<OverlappingContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
			PerpendicularDistanceSelector, double, PerpEdgeCache> finderOverlap;
		private readonly ShapeDistanceFinder<SimpleContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
			PerpendicularDistanceSelector, double, PerpEdgeCache> finderSimple;
		private readonly bool useOverlap;

		private readonly FloatBitmap sdf;
		private readonly DistanceMapping distanceMapping;
		private readonly Vector2 texelSize;
		private readonly double minImproveRatio;

		public readonly ShapeArtifactClassifier c_left, c_down, c_right, c_up, c_dl, c_dr, c_ul, c_ur;

		// Per-texel state set by FindErrors before each classifier() call.
		public Vector2 ShapeCoord, SdfCoord;
		public int MsdBase;   // pixel base index into sdf.Data for the current texel
		public bool ProtectedFlag;

		public ShapeDistanceChecker(FloatBitmap sdf, Shape shape, Projection projection,
			DistanceMapping distanceMapping, double minImproveRatio, bool overlapSupport,
			double hSpan, double vSpan, double dSpan) {
			this.sdf = sdf;
			this.distanceMapping = distanceMapping;
			this.minImproveRatio = minImproveRatio;
			texelSize = projection.UnprojectVector(new Vector2(1));
			useOverlap = overlapSupport;
			if (overlapSupport)
				finderOverlap = new ShapeDistanceFinder<OverlappingContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
					PerpendicularDistanceSelector, double, PerpEdgeCache>(shape,
					new OverlappingContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>(shape));
			else
				finderSimple = new ShapeDistanceFinder<SimpleContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
					PerpendicularDistanceSelector, double, PerpEdgeCache>(shape,
					new SimpleContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>(shape));

			c_left = new ShapeArtifactClassifier(this, new Vector2(-1, 0), hSpan, false);
			c_down = new ShapeArtifactClassifier(this, new Vector2(0, -1), vSpan, false);
			c_right = new ShapeArtifactClassifier(this, new Vector2(1, 0), hSpan, false);
			c_up = new ShapeArtifactClassifier(this, new Vector2(0, 1), vSpan, false);
			c_dl = new ShapeArtifactClassifier(this, new Vector2(-1, -1), dSpan, false);
			c_dr = new ShapeArtifactClassifier(this, new Vector2(1, -1), dSpan, false);
			c_ul = new ShapeArtifactClassifier(this, new Vector2(-1, 1), dSpan, false);
			c_ur = new ShapeArtifactClassifier(this, new Vector2(1, 1), dSpan, false);
		}

		private double ShapeDistance(Vector2 p) => useOverlap ? finderOverlap.Distance(p) : finderSimple.Distance(p);

		public sealed class ShapeArtifactClassifier : ArtifactClassifierBase {
			private readonly ShapeDistanceChecker parent;
			private readonly Vector2 direction;
			private readonly float[] oldMSD = new float[3];
			private readonly float[] newMSD = new float[3];

			public ShapeArtifactClassifier(ShapeDistanceChecker parent, Vector2 direction, double span, bool protectedFlag)
				: base(span, protectedFlag) {
				this.parent = parent;
				this.direction = direction;
			}

			public override bool Evaluate(double t, float m, int flags) {
				if ((flags & FlagCandidate) == 0) return false;
				if ((flags & FlagArtifact) != 0) return true;

				Vector2 tVector = t * direction;
				int b = parent.MsdBase;
				float[] data = parent.sdf.Data;

				Vector2 sdfCoord = parent.SdfCoord + tVector;
				Interpolation.InterpolateRGB(parent.sdf, sdfCoord, oldMSD);

				double aWeight = (1 - Math.Abs(tVector.x)) * (1 - Math.Abs(tVector.y));
				float aPSD = Median(data[b + 0], data[b + 1], data[b + 2]);
				newMSD[0] = (float) (oldMSD[0] + aWeight * (aPSD - data[b + 0]));
				newMSD[1] = (float) (oldMSD[1] + aWeight * (aPSD - data[b + 1]));
				newMSD[2] = (float) (oldMSD[2] + aWeight * (aPSD - data[b + 2]));

				float oldPSD = Median(oldMSD[0], oldMSD[1], oldMSD[2]);
				float newPSD = Median(newMSD[0], newMSD[1], newMSD[2]);
				float refPSD = (float) parent.distanceMapping.Map(
					parent.ShapeDistance(parent.ShapeCoord + new Vector2(tVector.x * parent.texelSize.x, tVector.y * parent.texelSize.y)));

				return parent.minImproveRatio * Math.Abs(newPSD - refPSD) < (double) Math.Abs(oldPSD - refPSD);
			}
		}
	}

	public sealed class MSDFErrorCorrection {

		private const double ArtifactTEpsilon = 0.01;
		private const double ProtectionRadiusTolerance = 1.001;

		public const byte ERROR = 1;
		public const byte PROTECTED = 2;

		private readonly byte[] stencil;
		private readonly int width, height;
		private readonly SDFTransformation transformation;
		private double minDeviationRatio = ErrorCorrectionConfig.DefaultMinDeviationRatio;
		private double minImproveRatio = ErrorCorrectionConfig.DefaultMinImproveRatio;

		public MSDFErrorCorrection(int width, int height, SDFTransformation transformation) {
			this.width = width;
			this.height = height;
			this.transformation = transformation;
			stencil = new byte[width * height];
		}

		public void SetMinDeviationRatio(double v) => minDeviationRatio = v;
		public void SetMinImproveRatio(double v) => minImproveRatio = v;

		private int S(int x, int y) => width * y + x;

		public void ProtectCorners(Shape shape) {
			foreach (Contour contour in shape.Contours) {
				if (contour.Edges.Count == 0) continue;
				EdgeSegment prevEdge = contour.Edges[contour.Edges.Count - 1];
				foreach (EdgeSegment edge in contour.Edges) {
					int commonColor = (int) prevEdge.Color & (int) edge.Color;
					if ((commonColor & (commonColor - 1)) == 0) {
						Vector2 p = transformation.Projection.Project(edge.Point(0));
						int l = (int) Math.Floor(p.x - 0.5);
						int b = (int) Math.Floor(p.y - 0.5);
						int r = l + 1, t = b + 1;
						if (l < width && b < height && r >= 0 && t >= 0) {
							if (l >= 0 && b >= 0) stencil[S(l, b)] |= PROTECTED;
							if (r < width && b >= 0) stencil[S(r, b)] |= PROTECTED;
							if (l >= 0 && t < height) stencil[S(l, t)] |= PROTECTED;
							if (r < width && t < height) stencil[S(r, t)] |= PROTECTED;
						}
					}
					prevEdge = edge;
				}
			}
		}

		private static bool EdgeBetweenTexelsChannel(float[] d, int a, int bb, int channel) {
			double t = (d[a + channel] - 0.5) / (d[a + channel] - d[bb + channel]);
			if (t > 0 && t < 1) {
				float c0 = (float) Mix(d[a + 0], d[bb + 0], t);
				float c1 = (float) Mix(d[a + 1], d[bb + 1], t);
				float c2 = (float) Mix(d[a + 2], d[bb + 2], t);
				float med = Median(c0, c1, c2);
				return (channel == 0 && med == c0) || (channel == 1 && med == c1) || (channel == 2 && med == c2);
			}
			return false;
		}

		private static int EdgeBetweenTexels(float[] d, int a, int bb) =>
			(int) EdgeColor.Red * (EdgeBetweenTexelsChannel(d, a, bb, 0) ? 1 : 0) +
			(int) EdgeColor.Green * (EdgeBetweenTexelsChannel(d, a, bb, 1) ? 1 : 0) +
			(int) EdgeColor.Blue * (EdgeBetweenTexelsChannel(d, a, bb, 2) ? 1 : 0);

		private void ProtectExtremeChannels(int stencilIndex, float[] d, int msd, float m, int mask) {
			if (((mask & (int) EdgeColor.Red) != 0 && d[msd + 0] != m) ||
				((mask & (int) EdgeColor.Green) != 0 && d[msd + 1] != m) ||
				((mask & (int) EdgeColor.Blue) != 0 && d[msd + 2] != m))
				stencil[stencilIndex] |= PROTECTED;
		}

		public void ProtectEdges(FloatBitmap sdf) {
			float[] d = sdf.Data;
			DistanceMapping dm = transformation.DistanceMapping;
			Projection pr = transformation.Projection;

			float radius = (float) (ProtectionRadiusTolerance *
				pr.UnprojectVector(new Vector2(dm.MapDelta(1), 0)).Length);
			for (int y = 0; y < sdf.Height; ++y)
				for (int x = 0; x < sdf.Width - 1; ++x) {
					int left = sdf.PixelBase(x, y), right = sdf.PixelBase(x + 1, y);
					float lm = Median(d[left], d[left + 1], d[left + 2]);
					float rm = Median(d[right], d[right + 1], d[right + 2]);
					if (Math.Abs(lm - 0.5f) + Math.Abs(rm - 0.5f) < radius) {
						int mask = EdgeBetweenTexels(d, left, right);
						ProtectExtremeChannels(S(x, y), d, left, lm, mask);
						ProtectExtremeChannels(S(x + 1, y), d, right, rm, mask);
					}
				}

			radius = (float) (ProtectionRadiusTolerance *
				pr.UnprojectVector(new Vector2(0, dm.MapDelta(1))).Length);
			for (int y = 0; y < sdf.Height - 1; ++y)
				for (int x = 0; x < sdf.Width; ++x) {
					int bottom = sdf.PixelBase(x, y), top = sdf.PixelBase(x, y + 1);
					float bm = Median(d[bottom], d[bottom + 1], d[bottom + 2]);
					float tm = Median(d[top], d[top + 1], d[top + 2]);
					if (Math.Abs(bm - 0.5f) + Math.Abs(tm - 0.5f) < radius) {
						int mask = EdgeBetweenTexels(d, bottom, top);
						ProtectExtremeChannels(S(x, y), d, bottom, bm, mask);
						ProtectExtremeChannels(S(x, y + 1), d, top, tm, mask);
					}
				}

			radius = (float) (ProtectionRadiusTolerance *
				pr.UnprojectVector(new Vector2(dm.MapDelta(1), dm.MapDelta(1))).Length);
			for (int y = 0; y < sdf.Height - 1; ++y)
				for (int x = 0; x < sdf.Width - 1; ++x) {
					int lb = sdf.PixelBase(x, y), rb = sdf.PixelBase(x + 1, y);
					int lt = sdf.PixelBase(x, y + 1), rt = sdf.PixelBase(x + 1, y + 1);
					float mlb = Median(d[lb], d[lb + 1], d[lb + 2]);
					float mrb = Median(d[rb], d[rb + 1], d[rb + 2]);
					float mlt = Median(d[lt], d[lt + 1], d[lt + 2]);
					float mrt = Median(d[rt], d[rt + 1], d[rt + 2]);
					if (Math.Abs(mlb - 0.5f) + Math.Abs(mrt - 0.5f) < radius) {
						int mask = EdgeBetweenTexels(d, lb, rt);
						ProtectExtremeChannels(S(x, y), d, lb, mlb, mask);
						ProtectExtremeChannels(S(x + 1, y + 1), d, rt, mrt, mask);
					}
					if (Math.Abs(mrb - 0.5f) + Math.Abs(mlt - 0.5f) < radius) {
						int mask = EdgeBetweenTexels(d, rb, lt);
						ProtectExtremeChannels(S(x + 1, y), d, rb, mrb, mask);
						ProtectExtremeChannels(S(x, y + 1), d, lt, mlt, mask);
					}
				}
		}

		public void ProtectAll() {
			for (int i = 0; i < stencil.Length; i++)
				stencil[i] |= PROTECTED;
		}

		// -- artifact predicates ------------------------------------------------------------------

		private static float InterpMedian(float[] d, int a, int bb, double t) =>
			Median((float) Mix(d[a + 0], d[bb + 0], t), (float) Mix(d[a + 1], d[bb + 1], t), (float) Mix(d[a + 2], d[bb + 2], t));

		private static float InterpMedianQ(float[] a3, float[] l3, float[] q3, double t) =>
			(float) Median(
				t * (t * q3[0] + l3[0]) + a3[0],
				t * (t * q3[1] + l3[1]) + a3[1],
				t * (t * q3[2] + l3[2]) + a3[2]);

		private static bool HasLinearArtifactInner(ArtifactClassifierBase ac, float am, float bm,
			float[] d, int a, int bb, float dA, float dB) {
			double t = (double) dA / (dA - dB);
			if (t > ArtifactTEpsilon && t < 1 - ArtifactTEpsilon) {
				float xm = InterpMedian(d, a, bb, t);
				return ac.Evaluate(t, xm, ac.RangeTest(0, 1, t, am, bm, xm));
			}
			return false;
		}

		private static bool HasLinearArtifact(ArtifactClassifierBase ac, float am, float[] d, int a, int bb) {
			float bm = Median(d[bb], d[bb + 1], d[bb + 2]);
			return Math.Abs(am - 0.5f) >= Math.Abs(bm - 0.5f) && (
				HasLinearArtifactInner(ac, am, bm, d, a, bb, d[a + 1] - d[a + 0], d[bb + 1] - d[bb + 0]) ||
				HasLinearArtifactInner(ac, am, bm, d, a, bb, d[a + 2] - d[a + 1], d[bb + 2] - d[bb + 1]) ||
				HasLinearArtifactInner(ac, am, bm, d, a, bb, d[a + 0] - d[a + 2], d[bb + 0] - d[bb + 2]));
		}

		private static bool HasDiagonalArtifactInner(ArtifactClassifierBase ac, float am, float dm,
			float[] a3, float[] l3, float[] q3, float dA, float dBC, float dD, double tEx0, double tEx1) {
			double[] t = new double[2];
			int solutions = EquationSolver.SolveQuadratic(t, dD - dBC + dA, dBC - dA - dA, dA);
			for (int i = 0; i < solutions; ++i) {
				if (t[i] > ArtifactTEpsilon && t[i] < 1 - ArtifactTEpsilon) {
					float xm = InterpMedianQ(a3, l3, q3, t[i]);
					int rangeFlags = ac.RangeTest(0, 1, t[i], am, dm, xm);
					double[] tEnd = new double[2];
					float[] em = new float[2];
					if (tEx0 > 0 && tEx0 < 1) {
						tEnd[0] = 0; tEnd[1] = 1;
						em[0] = am; em[1] = dm;
						int k = tEx0 > t[i] ? 1 : 0;
						tEnd[k] = tEx0;
						em[k] = InterpMedianQ(a3, l3, q3, tEx0);
						rangeFlags |= ac.RangeTest(tEnd[0], tEnd[1], t[i], em[0], em[1], xm);
					}
					if (tEx1 > 0 && tEx1 < 1) {
						tEnd[0] = 0; tEnd[1] = 1;
						em[0] = am; em[1] = dm;
						int k = tEx1 > t[i] ? 1 : 0;
						tEnd[k] = tEx1;
						em[k] = InterpMedianQ(a3, l3, q3, tEx1);
						rangeFlags |= ac.RangeTest(tEnd[0], tEnd[1], t[i], em[0], em[1], xm);
					}
					if (ac.Evaluate(t[i], xm, rangeFlags))
						return true;
				}
			}
			return false;
		}

		private static bool HasDiagonalArtifact(ArtifactClassifierBase ac, float am,
			float[] d, int a, int bIdx, int cIdx, int dIdx) {
			float dm = Median(d[dIdx], d[dIdx + 1], d[dIdx + 2]);
			if (Math.Abs(am - 0.5f) < Math.Abs(dm - 0.5f)) return false;

			float[] abc = {
				d[a + 0] - d[bIdx + 0] - d[cIdx + 0],
				d[a + 1] - d[bIdx + 1] - d[cIdx + 1],
				d[a + 2] - d[bIdx + 2] - d[cIdx + 2]
			};
			float[] l3 = { -d[a + 0] - abc[0], -d[a + 1] - abc[1], -d[a + 2] - abc[2] };
			float[] q3 = { d[dIdx + 0] + abc[0], d[dIdx + 1] + abc[1], d[dIdx + 2] + abc[2] };
			float[] a3 = { d[a + 0], d[a + 1], d[a + 2] };
			double[] tEx = { -0.5 * l3[0] / q3[0], -0.5 * l3[1] / q3[1], -0.5 * l3[2] / q3[2] };

			return
				HasDiagonalArtifactInner(ac, am, dm, a3, l3, q3,
					d[a + 1] - d[a + 0], d[bIdx + 1] - d[bIdx + 0] + d[cIdx + 1] - d[cIdx + 0], d[dIdx + 1] - d[dIdx + 0], tEx[0], tEx[1]) ||
				HasDiagonalArtifactInner(ac, am, dm, a3, l3, q3,
					d[a + 2] - d[a + 1], d[bIdx + 2] - d[bIdx + 1] + d[cIdx + 2] - d[cIdx + 1], d[dIdx + 2] - d[dIdx + 1], tEx[1], tEx[2]) ||
				HasDiagonalArtifactInner(ac, am, dm, a3, l3, q3,
					d[a + 0] - d[a + 2], d[bIdx + 0] - d[bIdx + 2] + d[cIdx + 0] - d[cIdx + 2], d[dIdx + 0] - d[dIdx + 2], tEx[2], tEx[0]);
		}

		// -- findErrors -------------------------------------------------------------------------------

		private (double h, double v, double diag) Spans() {
			DistanceMapping dm = transformation.DistanceMapping;
			Projection pr = transformation.Projection;
			double hSpan = minDeviationRatio * pr.UnprojectVector(new Vector2(dm.MapDelta(1), 0)).Length;
			double vSpan = minDeviationRatio * pr.UnprojectVector(new Vector2(0, dm.MapDelta(1))).Length;
			double dSpan = minDeviationRatio * pr.UnprojectVector(new Vector2(dm.MapDelta(1), dm.MapDelta(1))).Length;
			return (hSpan, vSpan, dSpan);
		}

		/// <summary>SDF-only artifact detection.</summary>
		public void FindErrors(FloatBitmap sdf) {
			(double hSpan, double vSpan, double dSpan) = Spans();
			float[] d = sdf.Data;
			BaseArtifactClassifier hClass = new BaseArtifactClassifier(hSpan, false);
			BaseArtifactClassifier vClass = new BaseArtifactClassifier(vSpan, false);
			BaseArtifactClassifier dClass = new BaseArtifactClassifier(dSpan, false);

			for (int y = 0; y < sdf.Height; ++y)
				for (int x = 0; x < sdf.Width; ++x) {
					int c = sdf.PixelBase(x, y);
					float cm = Median(d[c], d[c + 1], d[c + 2]);
					bool prot = (stencil[S(x, y)] & PROTECTED) != 0;
					hClass.SetState(hSpan, prot);
					vClass.SetState(vSpan, prot);
					dClass.SetState(dSpan, prot);

					int lB = 0, bB = 0, rB = 0, tB = 0;
					bool artifact =
						(x > 0 && (HasLinearArtifact(hClass, cm, d, c, (lB = sdf.PixelBase(x - 1, y))))) ||
						(y > 0 && (HasLinearArtifact(vClass, cm, d, c, (bB = sdf.PixelBase(x, y - 1))))) ||
						(x < sdf.Width - 1 && (HasLinearArtifact(hClass, cm, d, c, (rB = sdf.PixelBase(x + 1, y))))) ||
						(y < sdf.Height - 1 && (HasLinearArtifact(vClass, cm, d, c, (tB = sdf.PixelBase(x, y + 1))))) ||
						(x > 0 && y > 0 && HasDiagonalArtifact(dClass, cm, d, c, lB, bB, sdf.PixelBase(x - 1, y - 1))) ||
						(x < sdf.Width - 1 && y > 0 && HasDiagonalArtifact(dClass, cm, d, c, rB, bB, sdf.PixelBase(x + 1, y - 1))) ||
						(x > 0 && y < sdf.Height - 1 && HasDiagonalArtifact(dClass, cm, d, c, lB, tB, sdf.PixelBase(x - 1, y + 1))) ||
						(x < sdf.Width - 1 && y < sdf.Height - 1 && HasDiagonalArtifact(dClass, cm, d, c, rB, tB, sdf.PixelBase(x + 1, y + 1)));
					if (artifact) stencil[S(x, y)] |= ERROR;
				}
		}

		/// <summary>Artifact detection cross-checked against the exact shape distance.</summary>
		public void FindErrors(FloatBitmap sdf, Shape shape, bool overlapSupport) {
			(double hSpan, double vSpan, double dSpan) = Spans();
			float[] d = sdf.Data;
			ShapeDistanceChecker checker = new ShapeDistanceChecker(sdf, shape, transformation.Projection,
				transformation.DistanceMapping, minImproveRatio, overlapSupport, hSpan, vSpan, dSpan);

			int xDirection = 1;
			for (int y = 0; y < sdf.Height; ++y) {
				int x = xDirection < 0 ? sdf.Width - 1 : 0;
				for (int col = 0; col < sdf.Width; ++col, x += xDirection) {
					if ((stencil[S(x, y)] & ERROR) != 0) continue;
					int c = sdf.PixelBase(x, y);
					checker.ShapeCoord = transformation.Unproject(new Vector2(x + 0.5, y + 0.5));
					checker.SdfCoord = new Vector2(x + 0.5, y + 0.5);
					checker.MsdBase = c;
					bool prot = (stencil[S(x, y)] & PROTECTED) != 0;
					checker.ProtectedFlag = prot;
					checker.c_left.SetProtected(prot);
					checker.c_down.SetProtected(prot);
					checker.c_right.SetProtected(prot);
					checker.c_up.SetProtected(prot);
					checker.c_dl.SetProtected(prot);
					checker.c_dr.SetProtected(prot);
					checker.c_ul.SetProtected(prot);
					checker.c_ur.SetProtected(prot);

					float cm = Median(d[c], d[c + 1], d[c + 2]);
					int lB = 0, bB = 0, rB = 0, tB = 0;
					bool artifact =
						(x > 0 && HasLinearArtifact(checker.c_left, cm, d, c, (lB = sdf.PixelBase(x - 1, y)))) ||
						(y > 0 && HasLinearArtifact(checker.c_down, cm, d, c, (bB = sdf.PixelBase(x, y - 1)))) ||
						(x < sdf.Width - 1 && HasLinearArtifact(checker.c_right, cm, d, c, (rB = sdf.PixelBase(x + 1, y)))) ||
						(y < sdf.Height - 1 && HasLinearArtifact(checker.c_up, cm, d, c, (tB = sdf.PixelBase(x, y + 1)))) ||
						(x > 0 && y > 0 && HasDiagonalArtifact(checker.c_dl, cm, d, c, lB, bB, sdf.PixelBase(x - 1, y - 1))) ||
						(x < sdf.Width - 1 && y > 0 && HasDiagonalArtifact(checker.c_dr, cm, d, c, rB, bB, sdf.PixelBase(x + 1, y - 1))) ||
						(x > 0 && y < sdf.Height - 1 && HasDiagonalArtifact(checker.c_ul, cm, d, c, lB, tB, sdf.PixelBase(x - 1, y + 1))) ||
						(x < sdf.Width - 1 && y < sdf.Height - 1 && HasDiagonalArtifact(checker.c_ur, cm, d, c, rB, tB, sdf.PixelBase(x + 1, y + 1)));
					if (artifact) stencil[S(x, y)] |= ERROR;
				}
				xDirection = -xDirection;
			}
		}

		public void Apply(FloatBitmap sdf) {
			float[] d = sdf.Data;
			for (int y = 0; y < sdf.Height; ++y)
				for (int x = 0; x < sdf.Width; ++x) {
					if ((stencil[S(x, y)] & ERROR) != 0) {
						int p = sdf.PixelBase(x, y);
						float m = Median(d[p], d[p + 1], d[p + 2]);
						d[p] = m; d[p + 1] = m; d[p + 2] = m;
					}
				}
		}

		// -- orchestration (msdf-error-correction.cpp: msdfErrorCorrectionInner) --------------------

		public static void Run(FloatBitmap sdf, Shape shape, SDFTransformation transformation, MSDFGeneratorConfig config) {
			ErrorCorrectionConfig ec = config.errorCorrection;
			if (ec.mode == ErrorCorrectionConfig.Mode.Disabled)
				return;

			MSDFErrorCorrection e = new MSDFErrorCorrection(sdf.Width, sdf.Height, transformation);
			e.SetMinDeviationRatio(ec.minDeviationRatio);
			e.SetMinImproveRatio(ec.minImproveRatio);

			switch (ec.mode) {
				case ErrorCorrectionConfig.Mode.Disabled:
				case ErrorCorrectionConfig.Mode.Indiscriminate:
					break;
				case ErrorCorrectionConfig.Mode.EdgePriority:
					e.ProtectCorners(shape);
					e.ProtectEdges(sdf);
					break;
				case ErrorCorrectionConfig.Mode.EdgeOnly:
					e.ProtectAll();
					break;
			}

			if (ec.distanceCheckMode == ErrorCorrectionConfig.DistanceCheckMode.DoNotCheckDistance ||
				(ec.distanceCheckMode == ErrorCorrectionConfig.DistanceCheckMode.CheckDistanceAtEdge &&
				 ec.mode != ErrorCorrectionConfig.Mode.EdgeOnly)) {
				e.FindErrors(sdf);
				if (ec.distanceCheckMode == ErrorCorrectionConfig.DistanceCheckMode.CheckDistanceAtEdge)
					e.ProtectAll();
			}

			if (ec.distanceCheckMode == ErrorCorrectionConfig.DistanceCheckMode.AlwaysCheckDistance ||
				ec.distanceCheckMode == ErrorCorrectionConfig.DistanceCheckMode.CheckDistanceAtEdge) {
				e.FindErrors(sdf, shape, config.overlapSupport);
			}

			e.Apply(sdf);
		}
	}
}
