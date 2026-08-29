using System;
using NUnit.Framework;
using Sperlich.Text.Rasterizer;
using Range = Sperlich.Text.Rasterizer.Range;

namespace Sperlich.Text.Tests {

	public class MsdfRasterizerTests {

		// ---- EquationSolver -----------------------------------------------------------------------

		[Test]
		public void QuadraticTwoRoots() {
			double[] x = new double[2];
			int n = EquationSolver.SolveQuadratic(x, 1, -5, 6); // (x-2)(x-3)
			Assert.AreEqual(2, n);
			double lo = Math.Min(x[0], x[1]), hi = Math.Max(x[0], x[1]);
			Assert.AreEqual(2.0, lo, 1e-9);
			Assert.AreEqual(3.0, hi, 1e-9);
		}

		[Test]
		public void QuadraticDoubleRoot() {
			double[] x = new double[2];
			int n = EquationSolver.SolveQuadratic(x, 1, -4, 4); // (x-2)^2
			Assert.AreEqual(1, n);
			Assert.AreEqual(2.0, x[0], 1e-9);
		}

		[Test]
		public void QuadraticNoRealRoot() {
			double[] x = new double[2];
			Assert.AreEqual(0, EquationSolver.SolveQuadratic(x, 1, 0, 1));
		}

		[Test]
		public void QuadraticDegeneratesToLinearAndConstant() {
			double[] x = new double[2];
			Assert.AreEqual(1, EquationSolver.SolveQuadratic(x, 0, 2, -4));
			Assert.AreEqual(2.0, x[0], 1e-12);
			Assert.AreEqual(-1, EquationSolver.SolveQuadratic(x, 0, 0, 0)); // 0 == 0
			Assert.AreEqual(0, EquationSolver.SolveQuadratic(x, 0, 0, 5));  // 5 == 0
		}

		[Test]
		public void CubicThreeRoots() {
			double[] x = new double[3];
			int n = EquationSolver.SolveCubic(x, 1, -6, 11, -6); // (x-1)(x-2)(x-3)
			Assert.AreEqual(3, n);
			Array.Sort(x, 0, 3);
			Assert.AreEqual(1.0, x[0], 1e-7);
			Assert.AreEqual(2.0, x[1], 1e-7);
			Assert.AreEqual(3.0, x[2], 1e-7);
		}

		// ---- EdgeSegment signed distance --------------------------------------------------------

		[Test]
		public void LinearPerpendicularDistanceAndSign() {
			var e = new EdgeSegment.LinearSegment(new Vector2(0, 0), new Vector2(10, 0));
			SignedDistance above = e.SignedDistanceTo(new Vector2(5, 3), out _);
			SignedDistance below = e.SignedDistanceTo(new Vector2(5, -3), out _);
			Assert.AreEqual(3.0, Math.Abs(above.distance), 1e-9);
			Assert.AreEqual(3.0, Math.Abs(below.distance), 1e-9);
			Assert.AreNotEqual(Math.Sign(above.distance), Math.Sign(below.distance));
		}

		[Test]
		public void LinearEndpointDistance() {
			var e = new EdgeSegment.LinearSegment(new Vector2(0, 0), new Vector2(10, 0));
			SignedDistance sd = e.SignedDistanceTo(new Vector2(-5, 0), out double param);
			Assert.Less(param, 0.0);
			Assert.AreEqual(5.0, Math.Abs(sd.distance), 1e-9);
		}

		[Test]
		public void QuadraticApexDistance() {
			// symmetric arc peaking at (0,1); nearest point to (0,3) is the apex, distance 2
			var e = new EdgeSegment.QuadraticSegment(new Vector2(-1, 0), new Vector2(0, 2), new Vector2(1, 0));
			SignedDistance sd = e.SignedDistanceTo(new Vector2(0, 3), out _);
			Assert.AreEqual(2.0, Math.Abs(sd.distance), 1e-6);
		}

		// ---- Edge coloring -------------------------------------------------------------------------

		private static Shape MakeSquare() {
			Shape s = new Shape { InverseYAxis = false };
			Contour c = new Contour();
			Vector2 a = new Vector2(2, 2), b = new Vector2(8, 2), d = new Vector2(8, 8), e = new Vector2(2, 8);
			c.AddEdge(new EdgeSegment.LinearSegment(a, b));
			c.AddEdge(new EdgeSegment.LinearSegment(b, d));
			c.AddEdge(new EdgeSegment.LinearSegment(d, e));
			c.AddEdge(new EdgeSegment.LinearSegment(e, a));
			s.AddContour(c);
			return s;
		}

		private static Shape MakeSmoothQuadLoop() {
			// four quadratics forming a rounded loop with no hard corners
			Shape s = new Shape { InverseYAxis = false };
			Contour c = new Contour();
			Vector2 top = new Vector2(0, 5), right = new Vector2(5, 0), bot = new Vector2(0, -5), left = new Vector2(-5, 0);
			c.AddEdge(new EdgeSegment.QuadraticSegment(top, new Vector2(5, 5), right));
			c.AddEdge(new EdgeSegment.QuadraticSegment(right, new Vector2(5, -5), bot));
			c.AddEdge(new EdgeSegment.QuadraticSegment(bot, new Vector2(-5, -5), left));
			c.AddEdge(new EdgeSegment.QuadraticSegment(left, new Vector2(-5, 5), top));
			s.AddContour(c);
			return s;
		}

		[Test]
		public void EdgeColoringIsDeterministic() {
			Shape s1 = MakeSquare(); s1.Normalize(); s1.OrientContours();
			Shape s2 = MakeSquare(); s2.Normalize(); s2.OrientContours();
			EdgeColoring.EdgeColoringSimple(s1, 3.0, 0);
			EdgeColoring.EdgeColoringSimple(s2, 3.0, 0);
			for (int i = 0; i < s1.Contours[0].Edges.Count; i++)
				Assert.AreEqual(s1.Contours[0].Edges[i].Color, s2.Contours[0].Edges[i].Color, $"edge {i}");
		}

		[Test]
		public void SquareUsesMoreThanOneColor() {
			Shape s = MakeSquare(); s.Normalize(); s.OrientContours();
			EdgeColoring.EdgeColoringSimple(s, 3.0, 0);
			var edges = s.Contours[0].Edges;
			bool anyDifferent = false;
			for (int i = 1; i < edges.Count; i++)
				if (edges[i].Color != edges[0].Color) anyDifferent = true;
			Assert.IsTrue(anyDifferent, "a 4-corner square must not be single-coloured");
			foreach (var e in edges)
				Assert.AreNotEqual(EdgeColor.Black, e.Color, "no edge may be left uncoloured (BLACK)");
		}

		[Test]
		public void SmoothContourIsSingleColor() {
			Shape s = MakeSmoothQuadLoop(); s.Normalize(); s.OrientContours();
			EdgeColoring.EdgeColoringSimple(s, 3.0, 0);
			var edges = s.Contours[0].Edges;
			EdgeColor c0 = edges[0].Color;
			foreach (var e in edges)
				Assert.AreEqual(c0, e.Color, "a corner-free contour must be one colour");
			Assert.AreNotEqual(EdgeColor.Black, c0);
			Assert.AreNotEqual(EdgeColor.White, c0);
		}

		// ---- MTSDF generator --------------------------------------------------------------------

		[Test]
		public void MtsdfSquareInsideOutsideSignAndAlpha() {
			Shape s = MakeSquare();          // square [2,8]^2
			s.Normalize();
			s.OrientContours();
			EdgeColoring.EdgeColoringSimple(s, 3.0, 0);

			var bmp = new FloatBitmap(10, 10, 4);
			var t = new SDFTransformation(new Projection(new Vector2(1, 1), new Vector2(0, 0)), new Range(8));
			MsdfGenerator.GenerateMTSDF(bmp, s, t, MSDFGeneratorConfig.Default);

			double MedianAt(int x, int y) => MsdfMath.Median(bmp[x, y, 0], bmp[x, y, 1], bmp[x, y, 2]);

			// centre pixel (5,5) -> shape (5.5,5.5): well inside -> > 0.5 ; corner (0,0) -> outside -> < 0.5
			Assert.Greater(MedianAt(5, 5), 0.5, "interior median must read as filled");
			Assert.Less(MedianAt(0, 0), 0.5, "exterior median must read as empty");

			// MTSDF alpha is a true SDF; for a convex square it tracks the median closely at the centre
			Assert.Greater(bmp[5, 5, 3], 0.5);
			Assert.Less(bmp[0, 0, 3], 0.5);
			Assert.AreEqual(MedianAt(5, 5), bmp[5, 5, 3], 0.12, "median vs true-SDF mismatch at centre");
		}

		[Test]
		public void MtsdfChannelsAreFinite() {
			Shape s = MakeSquare();
			s.Normalize();
			s.OrientContours();
			EdgeColoring.EdgeColoringSimple(s, 3.0, 0);
			var bmp = new FloatBitmap(12, 12, 4);
			var t = new SDFTransformation(new Projection(new Vector2(1, 1), new Vector2(0, 0)), new Range(8));
			MsdfGenerator.GenerateMTSDF(bmp, s, t, MSDFGeneratorConfig.Default);
			foreach (float v in bmp.Data)
				Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), "non-finite channel value");
		}
	}
}
