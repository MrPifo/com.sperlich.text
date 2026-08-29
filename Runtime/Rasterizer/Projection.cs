// C# port of msdfgen/core/Projection.cpp, Range.hpp, DistanceMapping.cpp — https://github.com/Chlumsky/msdfgen (MIT).

namespace Sperlich.Text.Rasterizer {

	/// <summary>The range between two real values (e.g. representable signed distances).</summary>
	public struct Range {
		public double lower, upper;

		public Range(double symmetricalWidth) { lower = -0.5 * symmetricalWidth; upper = 0.5 * symmetricalWidth; }
		public Range(double lowerBound, double upperBound) { lower = lowerBound; upper = upperBound; }

		public static Range operator *(Range r, double factor) => new Range(r.lower * factor, r.upper * factor);
		public static Range operator *(double factor, Range r) => new Range(factor * r.lower, factor * r.upper);
		public static Range operator /(Range r, double divisor) => new Range(r.lower / divisor, r.upper / divisor);
	}

	/// <summary>Linear transformation of signed distance values.</summary>
	public struct DistanceMapping {

		private readonly double scale;
		private readonly double translate;

		private DistanceMapping(double scale, double translate) { this.scale = scale; this.translate = translate; }

		public static DistanceMapping FromRange(Range range) =>
			new DistanceMapping(1.0 / (range.upper - range.lower), -range.lower);

		public static DistanceMapping Inverse(Range range) {
			double rangeWidth = range.upper - range.lower;
			return new DistanceMapping(rangeWidth, range.lower / (rangeWidth != 0 ? rangeWidth : 1));
		}

		/// <summary>Maps an absolute distance.</summary>
		public double Map(double d) => scale * (d + translate);

		/// <summary>Maps a distance delta (no translate).</summary>
		public double MapDelta(double d) => scale * d;

		public DistanceMapping GetInverse() => new DistanceMapping(1.0 / scale, -scale * translate);
	}

	/// <summary>A transformation from shape coordinates to pixel coordinates.</summary>
	public struct Projection {

		private readonly Vector2 scale;
		private readonly Vector2 translate;

		public static readonly Projection Identity = new Projection(new Vector2(1), new Vector2(0));

		public Projection(Vector2 scale, Vector2 translate) { this.scale = scale; this.translate = translate; }

		public Vector2 Project(Vector2 coord) => scale * (coord + translate);
		public Vector2 Unproject(Vector2 coord) => coord / scale - translate;
		public Vector2 ProjectVector(Vector2 vector) => scale * vector;
		public Vector2 UnprojectVector(Vector2 vector) => vector / scale;
		public double ProjectX(double x) => scale.x * (x + translate.x);
		public double ProjectY(double y) => scale.y * (y + translate.y);
		public double UnprojectX(double x) => x / scale.x - translate.x;
		public double UnprojectY(double y) => y / scale.y - translate.y;
	}
}
