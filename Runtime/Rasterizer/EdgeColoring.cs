// C# port of msdfgen/core/edge-coloring.cpp (edgeColoringSimple only) — https://github.com/Chlumsky/msdfgen (MIT).
using System;
using System.Collections.Generic;
using static Sperlich.Text.Rasterizer.MsdfMath;

namespace Sperlich.Text.Rasterizer {

	public static class EdgeColoring {

		// Trichotomy: for position < n returns -1 / 0 / 1 (closer to start / middle / end), balanced over 0..n-1.
		private static int SymmetricalTrichotomy(int position, int n) =>
			(int) (3 + 2.875 * position / (n - 1) - 1.4375 + 0.5) - 3;

		private static bool IsCorner(Vector2 aDir, Vector2 bDir, double crossThreshold) =>
			DotProduct(aDir, bDir) <= 0 || Math.Abs(CrossProduct(aDir, bDir)) > crossThreshold;

		private static int SeedExtract2(ref ulong seed) {
			int v = (int) (seed & 1);
			seed >>= 1;
			return v;
		}

		private static int SeedExtract3(ref ulong seed) {
			int v = (int) (seed % 3);
			seed /= 3;
			return v;
		}

		private static EdgeColor InitColor(ref ulong seed) {
			EdgeColor[] colors = { EdgeColor.Cyan, EdgeColor.Magenta, EdgeColor.Yellow };
			return colors[SeedExtract3(ref seed)];
		}

		private static void SwitchColor(ref EdgeColor color, ref ulong seed) {
			int shifted = (int) color << (1 + SeedExtract2(ref seed));
			color = (EdgeColor) ((shifted | (shifted >> 3)) & (int) EdgeColor.White);
		}

		private static void SwitchColor(ref EdgeColor color, ref ulong seed, EdgeColor banned) {
			int combined = (int) color & (int) banned;
			if (combined == (int) EdgeColor.Red || combined == (int) EdgeColor.Green || combined == (int) EdgeColor.Blue)
				color = (EdgeColor) (combined ^ (int) EdgeColor.White);
			else
				SwitchColor(ref color, ref seed);
		}

		/// <summary>
		/// Assigns R/G/B channel colours to the shape's edges for the multi-channel technique, splitting
		/// edges where required. <paramref name="angleThreshold"/> is the max corner angle in radians
		/// (e.g. 3 ≈ 172°).
		/// </summary>
		public static void EdgeColoringSimple(Shape shape, double angleThreshold, ulong seed = 0) {
			double crossThreshold = Math.Sin(angleThreshold);
			EdgeColor color = InitColor(ref seed);
			List<int> corners = new List<int>();

			foreach (Contour contour in shape.Contours) {
				if (contour.Edges.Count == 0) continue;

				// Identify corners
				corners.Clear();
				{
					Vector2 prevDirection = contour.Edges[contour.Edges.Count - 1].Direction(1);
					for (int index = 0; index < contour.Edges.Count; index++) {
						EdgeSegment edge = contour.Edges[index];
						if (IsCorner(prevDirection.Normalize(), edge.Direction(0).Normalize(), crossThreshold))
							corners.Add(index);
						prevDirection = edge.Direction(1);
					}
				}

				if (corners.Count == 0) {
					// Smooth contour
					SwitchColor(ref color, ref seed);
					foreach (EdgeSegment edge in contour.Edges)
						edge.Color = color;
				} else if (corners.Count == 1) {
					// "Teardrop"
					EdgeColor[] colors = new EdgeColor[3];
					SwitchColor(ref color, ref seed);
					colors[0] = color;
					colors[1] = EdgeColor.White;
					SwitchColor(ref color, ref seed);
					colors[2] = color;
					int corner = corners[0];
					if (contour.Edges.Count >= 3) {
						int m = contour.Edges.Count;
						for (int i = 0; i < m; ++i)
							contour.Edges[(corner + i) % m].Color = colors[1 + SymmetricalTrichotomy(i, m)];
					} else if (contour.Edges.Count >= 1) {
						// Fewer than three segments for three colours => split
						EdgeSegment[] parts = new EdgeSegment[7];
						contour.Edges[0].SplitInThirds(out parts[0 + 3 * corner], out parts[1 + 3 * corner], out parts[2 + 3 * corner]);
						if (contour.Edges.Count >= 2) {
							contour.Edges[1].SplitInThirds(out parts[3 - 3 * corner], out parts[4 - 3 * corner], out parts[5 - 3 * corner]);
							parts[0].Color = parts[1].Color = colors[0];
							parts[2].Color = parts[3].Color = colors[1];
							parts[4].Color = parts[5].Color = colors[2];
						} else {
							parts[0].Color = colors[0];
							parts[1].Color = colors[1];
							parts[2].Color = colors[2];
						}
						contour.Edges.Clear();
						for (int i = 0; i < parts.Length && parts[i] != null; ++i)
							contour.Edges.Add(parts[i]);
					}
				} else {
					// Multiple corners
					int cornerCount = corners.Count;
					int spline = 0;
					int start = corners[0];
					int m = contour.Edges.Count;
					SwitchColor(ref color, ref seed);
					EdgeColor initialColor = color;
					for (int i = 0; i < m; ++i) {
						int index = (start + i) % m;
						if (spline + 1 < cornerCount && corners[spline + 1] == index) {
							++spline;
							SwitchColor(ref color, ref seed,
								(EdgeColor) ((spline == cornerCount - 1 ? 1 : 0) * (int) initialColor));
						}
						contour.Edges[index].Color = color;
					}
				}
			}
		}
	}
}
