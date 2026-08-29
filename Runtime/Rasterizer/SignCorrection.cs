// C# port of msdfgen/core/sign-correction.cpp — https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky).
// A per-pixel scanline fill test that flips the field sign wherever the generated distance disagrees
// with the shape's true (non-zero winding) fill. This is what lets arbitrary real-font geometry work
// without a Skia-style path resolver: self-intersecting single contours and overlapping contours are
// handled purely from the winding number, which is a global geometric truth the distance finders miss.
//
// Extension (not in upstream): where the scanline winding magnitude is >= 2 the texel is covered by
// two or more same-sense contours with no hole, i.e. unambiguously well inside the union. The distance
// finders only see the nearby *internal* overlap edge there, so the field dips toward 0.5 and renders
// as a hairline seam (the '8' waist, the '9' loop/tail join in geometric fonts). Those texels are
// clamped solidly inside. A real geometry resolver would delete the internal edges instead.

using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	public static class SignCorrection {

		/// <summary>How far inside overlap texels (|winding| >= 2) are pulled. 0.5 = edge, 1 = far inside.</summary>
		public const float DeepOverlapFloor = 0.82f;

		/// <summary>Single-channel (SDF / PSDF) sign correction.</summary>
		public static void Apply1(FloatBitmap sdf, Shape shape, SDFTransformation t, FillRule fillRule) {
			Scanline scanline = new Scanline();
			for (int y = 0; y < sdf.Height; ++y) {
				int row = shape.InverseYAxis ? sdf.Height - y - 1 : y;
				shape.GetScanline(scanline, t.Projection.UnprojectY(y + 0.5));
				for (int x = 0; x < sdf.Width; ++x) {
					double sx = t.Projection.UnprojectX(x + 0.5);
					int winding = scanline.SumIntersections(sx);
					bool fill = Scanline.InterpretFillRule(winding, fillRule);
					int b = sdf.PixelBase(x, row);
					float sd = sdf.Data[b];

					if ((sd > 0.5f) != fill)
						sd = 1f - sd;
					if ((winding >= 2 || winding <= -2) && sd < DeepOverlapFloor)
						sd = DeepOverlapFloor;
					sdf.Data[b] = sd;
				}
			}
		}

		/// <summary>
		/// Multi-channel (MSDF / MTSDF) sign correction. The RGB median decides inside/outside; when it
		/// disagrees with the scanline fill, all present channels (incl. the MTSDF true-SDF alpha) are
		/// mirrored around 0.5. Deep-overlap texels are then clamped inside (see file header).
		/// </summary>
		public static void Apply3(FloatBitmap sdf, Shape shape, SDFTransformation t, FillRule fillRule) {
			int channels = sdf.Channels;
			Scanline scanline = new Scanline();
			for (int y = 0; y < sdf.Height; ++y) {
				int row = shape.InverseYAxis ? sdf.Height - y - 1 : y;
				shape.GetScanline(scanline, t.Projection.UnprojectY(y + 0.5));
				for (int x = 0; x < sdf.Width; ++x) {
					double sx = t.Projection.UnprojectX(x + 0.5);
					int winding = scanline.SumIntersections(sx);
					bool fill = Scanline.InterpretFillRule(winding, fillRule);
					int b = sdf.PixelBase(x, row);
					float med = (float) Median(sdf.Data[b], sdf.Data[b + 1], sdf.Data[b + 2]);

					if (med != 0.5f && (med > 0.5f) != fill)
						for (int c = 0; c < channels; ++c)
							sdf.Data[b + c] = 1f - sdf.Data[b + c];

					if (winding >= 2 || winding <= -2)
						for (int c = 0; c < channels; ++c)
							if (sdf.Data[b + c] < DeepOverlapFloor)
								sdf.Data[b + c] = DeepOverlapFloor;
				}
			}
		}
	}
}
