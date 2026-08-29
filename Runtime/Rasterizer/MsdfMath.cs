// C# port of msdfgen/core/arithmetics.hpp + EdgeColor.h — https://github.com/Chlumsky/msdfgen (MIT).
using System;

namespace Sperlich.Text.Rasterizer {

	/// <summary>Which colour channels an edge belongs to (bitmask: R=1, G=2, B=4).</summary>
	public enum EdgeColor {
		Black = 0,
		Red = 1,
		Green = 2,
		Yellow = 3,
		Blue = 4,
		Magenta = 5,
		Cyan = 6,
		White = 7
	}

	/// <summary>Free functions from msdfgen's <c>arithmetics.hpp</c>, specialised to the types used.</summary>
	public static class MsdfMath {

		public static double Median(double a, double b, double c) =>
			Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

		public static float Median(float a, float b, float c) =>
			Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

		public static double Mix(double a, double b, double weight) => (1.0 - weight) * a + weight * b;

		public static Vector2 Mix(Vector2 a, Vector2 b, double weight) =>
			new Vector2((1.0 - weight) * a.x + weight * b.x, (1.0 - weight) * a.y + weight * b.y);

		/// <summary>Clamp to [0, 1] (msdfgen semantics: NaN → 0).</summary>
		public static double Clamp01(double n) => n >= 0.0 && n <= 1.0 ? n : (n > 0.0 ? 1.0 : 0.0);

		/// <summary>Clamp to [0, b].</summary>
		public static double Clamp(double n, double b) => n >= 0.0 && n <= b ? n : (n > 0.0 ? b : 0.0);

		/// <summary>Clamp to [a, b].</summary>
		public static double Clamp(double n, double a, double b) => n >= a && n <= b ? n : (n < a ? a : b);

		public static int Clamp(int n, int a, int b) => n >= a && n <= b ? n : (n < a ? a : b);

		/// <summary>Clamp to [0, b] (msdfgen 2-arg clamp).</summary>
		public static int Clamp(int n, int b) => n >= 0 && n <= b ? n : (n > 0 ? b : 0);

		/// <summary>1 for positive, -1 for negative, 0 for zero.</summary>
		public static int Sign(double n) => (0.0 < n ? 1 : 0) - (n < 0.0 ? 1 : 0);

		/// <summary>1 for non-negative, -1 for negative.</summary>
		public static int NonZeroSign(double n) => 2 * (n > 0.0 ? 1 : 0) - 1;

		public static double DotProduct(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
		public static double CrossProduct(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
	}
}
