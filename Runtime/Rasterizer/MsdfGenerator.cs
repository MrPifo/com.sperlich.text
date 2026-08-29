// C# port of msdfgen/core/msdfgen.cpp (generateDistanceField + generateSDF/PSDF/MSDF/MTSDF) —
// https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky).
// NOTE: the msdfErrorCorrection() post-pass is added in a later phase; this file produces the raw field.
// The output bitmap must be Y-up (row 0 = bottom), matching a normalised, Y-up Shape.

namespace Sperlich.Text.Rasterizer {

	public static class MsdfGenerator {

		/// <summary>
		/// Diagnostic switch for the scanline sign-correction pass. Leave <c>true</c> for production;
		/// the baker flips it per bake so a FontDefinition toggle can isolate whether that pass is the
		/// source of a junction/overlap artefact.
		/// </summary>
		public static bool SignCorrectionEnabled = true;

		// Concrete closed generic aliases for the four combiner instantiations we use.
		// TrueDistance -> SDF, PerpendicularDistance -> PSDF, Multi -> MSDF, MultiAndTrue -> MTSDF.

		public static void GenerateSDF(FloatBitmap output, Shape shape, SDFTransformation t, GeneratorConfig config) {
			if (config.overlapSupport)
				Fill1(output, shape, t, new ShapeDistanceFinder<
					OverlappingContourCombiner<TrueDistanceSelector, double, TrueDistanceEdgeCache>,
					TrueDistanceSelector, double, TrueDistanceEdgeCache>(shape,
					new OverlappingContourCombiner<TrueDistanceSelector, double, TrueDistanceEdgeCache>(shape)).Distance);
			else
				Fill1(output, shape, t, new ShapeDistanceFinder<
					SimpleContourCombiner<TrueDistanceSelector, double, TrueDistanceEdgeCache>,
					TrueDistanceSelector, double, TrueDistanceEdgeCache>(shape,
					new SimpleContourCombiner<TrueDistanceSelector, double, TrueDistanceEdgeCache>(shape)).Distance);
			if (SignCorrectionEnabled) SignCorrection.Apply1(output, shape, t, FillRule.NonZero);
		}

		public static void GeneratePseudoSDF(FloatBitmap output, Shape shape, SDFTransformation t, GeneratorConfig config) {
			if (config.overlapSupport)
				Fill1(output, shape, t, new ShapeDistanceFinder<
					OverlappingContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
					PerpendicularDistanceSelector, double, PerpEdgeCache>(shape,
					new OverlappingContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>(shape)).Distance);
			else
				Fill1(output, shape, t, new ShapeDistanceFinder<
					SimpleContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>,
					PerpendicularDistanceSelector, double, PerpEdgeCache>(shape,
					new SimpleContourCombiner<PerpendicularDistanceSelector, double, PerpEdgeCache>(shape)).Distance);
			if (SignCorrectionEnabled) SignCorrection.Apply1(output, shape, t, FillRule.NonZero);
		}

		public static void GenerateMSDF(FloatBitmap output, Shape shape, SDFTransformation t, MSDFGeneratorConfig config) {
			if (config.overlapSupport)
				Fill3(output, shape, t, new ShapeDistanceFinder<
					OverlappingContourCombiner<MultiDistanceSelector, MultiDistance, PerpEdgeCache>,
					MultiDistanceSelector, MultiDistance, PerpEdgeCache>(shape,
					new OverlappingContourCombiner<MultiDistanceSelector, MultiDistance, PerpEdgeCache>(shape)).Distance);
			else
				Fill3(output, shape, t, new ShapeDistanceFinder<
					SimpleContourCombiner<MultiDistanceSelector, MultiDistance, PerpEdgeCache>,
					MultiDistanceSelector, MultiDistance, PerpEdgeCache>(shape,
					new SimpleContourCombiner<MultiDistanceSelector, MultiDistance, PerpEdgeCache>(shape)).Distance);
			if (SignCorrectionEnabled) SignCorrection.Apply3(output, shape, t, FillRule.NonZero);
			MSDFErrorCorrection.Run(output, shape, t, config);
		}

		public static void GenerateMTSDF(FloatBitmap output, Shape shape, SDFTransformation t, MSDFGeneratorConfig config) {
			if (config.overlapSupport)
				Fill4(output, shape, t, new ShapeDistanceFinder<
					OverlappingContourCombiner<MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>,
					MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>(shape,
					new OverlappingContourCombiner<MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>(shape)).Distance);
			else
				Fill4(output, shape, t, new ShapeDistanceFinder<
					SimpleContourCombiner<MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>,
					MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>(shape,
					new SimpleContourCombiner<MultiAndTrueDistanceSelector, MultiAndTrueDistance, PerpEdgeCache>(shape)).Distance);
			if (SignCorrectionEnabled) SignCorrection.Apply3(output, shape, t, FillRule.NonZero);
			MSDFErrorCorrection.Run(output, shape, t, config);
		}

		// -- per-pixel fill loops (boustrophedon, matching msdfgen's generateDistanceField) -------------

		private static void Fill1(FloatBitmap output, Shape shape, SDFTransformation t, System.Func<Vector2, double> distance) {
			DistanceMapping map = t.DistanceMapping;
			int xDirection = 1;
			for (int y = 0; y < output.Height; ++y) {
				int x = xDirection < 0 ? output.Width - 1 : 0;
				for (int col = 0; col < output.Width; ++col) {
					Vector2 p = t.Unproject(new Vector2(x + 0.5, y + 0.5));
					output[x, y, 0] = (float) map.Map(distance(p));
					x += xDirection;
				}
				xDirection = -xDirection;
			}
		}

		private static void Fill3(FloatBitmap output, Shape shape, SDFTransformation t, System.Func<Vector2, MultiDistance> distance) {
			DistanceMapping map = t.DistanceMapping;
			int xDirection = 1;
			for (int y = 0; y < output.Height; ++y) {
				int x = xDirection < 0 ? output.Width - 1 : 0;
				for (int col = 0; col < output.Width; ++col) {
					Vector2 p = t.Unproject(new Vector2(x + 0.5, y + 0.5));
					MultiDistance d = distance(p);
					int b = output.PixelBase(x, y);
					output.Data[b + 0] = (float) map.Map(d.r);
					output.Data[b + 1] = (float) map.Map(d.g);
					output.Data[b + 2] = (float) map.Map(d.b);
					x += xDirection;
				}
				xDirection = -xDirection;
			}
		}

		private static void Fill4(FloatBitmap output, Shape shape, SDFTransformation t, System.Func<Vector2, MultiAndTrueDistance> distance) {
			DistanceMapping map = t.DistanceMapping;
			int xDirection = 1;
			for (int y = 0; y < output.Height; ++y) {
				int x = xDirection < 0 ? output.Width - 1 : 0;
				for (int col = 0; col < output.Width; ++col) {
					Vector2 p = t.Unproject(new Vector2(x + 0.5, y + 0.5));
					MultiAndTrueDistance d = distance(p);
					int b = output.PixelBase(x, y);
					output.Data[b + 0] = (float) map.Map(d.r);
					output.Data[b + 1] = (float) map.Map(d.g);
					output.Data[b + 2] = (float) map.Map(d.b);
					output.Data[b + 3] = (float) map.Map(d.a);
					x += xDirection;
				}
				xDirection = -xDirection;
			}
		}
	}
}
