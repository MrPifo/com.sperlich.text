// C# port of msdfgen/core/Vector2.hpp — Multi-channel signed distance field generator.
// Original: https://github.com/Chlumsky/msdfgen (MIT, Viktor Chlumsky). Independent port, not a wrapper.
using System;

namespace Sperlich.Text.Rasterizer {

	/// <summary>A 2-dimensional euclidean double-precision vector (also used as a point).</summary>
	public struct Vector2 : IEquatable<Vector2> {

		public double x, y;

		public Vector2(double val) { x = val; y = val; }
		public Vector2(double x, double y) { this.x = x; this.y = y; }

		public double SquaredLength => x * x + y * y;
		public double Length => Math.Sqrt(x * x + y * y);

		/// <summary>True when the vector is not exactly zero (msdfgen's <c>operator bool</c>).</summary>
		public bool IsNonZero => x != 0 || y != 0;

		/// <summary>Same direction, unit length. Zero input → (0, allowZero ? 0 : 1).</summary>
		public Vector2 Normalize(bool allowZero = false) {
			double len = Length;
			if (len != 0) return new Vector2(x / len, y / len);
			return new Vector2(0, allowZero ? 0 : 1);
		}

		/// <summary>Orthogonal vector of the same length.</summary>
		public Vector2 GetOrthogonal(bool polarity = true) =>
			polarity ? new Vector2(-y, x) : new Vector2(y, -x);

		/// <summary>Orthogonal unit vector. Zero input → (0, ±(allowZero ? 0 : 1)).</summary>
		public Vector2 GetOrthonormal(bool polarity = true, bool allowZero = false) {
			double len = Length;
			if (len != 0)
				return polarity ? new Vector2(-y / len, x / len) : new Vector2(y / len, -x / len);
			double z = allowZero ? 0 : 1;
			return polarity ? new Vector2(0, z) : new Vector2(0, -z);
		}

		public static double Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

		/// <summary>2D cross product (scalar): a.x*b.y - a.y*b.x.</summary>
		public static double Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

		public static Vector2 operator +(Vector2 v) => v;
		public static Vector2 operator -(Vector2 v) => new Vector2(-v.x, -v.y);
		public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
		public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
		public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
		public static Vector2 operator /(Vector2 a, Vector2 b) => new Vector2(a.x / b.x, a.y / b.y);
		public static Vector2 operator *(double a, Vector2 b) => new Vector2(a * b.x, a * b.y);
		public static Vector2 operator /(double a, Vector2 b) => new Vector2(a / b.x, a / b.y);
		public static Vector2 operator *(Vector2 a, double b) => new Vector2(a.x * b, a.y * b);
		public static Vector2 operator /(Vector2 a, double b) => new Vector2(a.x / b, a.y / b);

		public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
		public static bool operator !=(Vector2 a, Vector2 b) => a.x != b.x || a.y != b.y;

		public bool Equals(Vector2 other) => x == other.x && y == other.y;
		public override bool Equals(object obj) => obj is Vector2 v && Equals(v);
		public override int GetHashCode() => (x, y).GetHashCode();
		public override string ToString() => $"({x}, {y})";
	}
}
