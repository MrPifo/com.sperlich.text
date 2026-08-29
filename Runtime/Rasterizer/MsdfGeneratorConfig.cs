// C# port of msdfgen/core/generator-config.h + SDFTransformation.h — https://github.com/Chlumsky/msdfgen (MIT).

namespace Sperlich.Text.Rasterizer {

	/// <summary>Spatial (<see cref="Projection"/>) + value (<see cref="DistanceMapping"/>) transform.</summary>
	public struct SDFTransformation {
		public Projection Projection;
		public DistanceMapping DistanceMapping;

		public SDFTransformation(Projection projection, DistanceMapping distanceMapping) {
			Projection = projection;
			DistanceMapping = distanceMapping;
		}

		public SDFTransformation(Projection projection, Range range)
			: this(projection, Sperlich.Text.Rasterizer.DistanceMapping.FromRange(range)) { }

		public Vector2 Unproject(Vector2 coord) => Projection.Unproject(coord);
	}

	/// <summary>Error-correction pass configuration (used in the MSDF/MTSDF path — Phase 4).</summary>
	public struct ErrorCorrectionConfig {

		public const double DefaultMinDeviationRatio = 1.11111111111111111;
		public const double DefaultMinImproveRatio = 1.11111111111111111;

		public enum Mode { Disabled, Indiscriminate, EdgePriority, EdgeOnly }
		public enum DistanceCheckMode { DoNotCheckDistance, CheckDistanceAtEdge, AlwaysCheckDistance }

		public Mode mode;
		public DistanceCheckMode distanceCheckMode;
		public double minDeviationRatio;
		public double minImproveRatio;

		public static ErrorCorrectionConfig Default => new ErrorCorrectionConfig {
			mode = Mode.EdgePriority,
			distanceCheckMode = DistanceCheckMode.CheckDistanceAtEdge,
			minDeviationRatio = DefaultMinDeviationRatio,
			minImproveRatio = DefaultMinImproveRatio
		};
	}

	public struct GeneratorConfig {
		public bool overlapSupport;
		public GeneratorConfig(bool overlapSupport) { this.overlapSupport = overlapSupport; }
	}

	public struct MSDFGeneratorConfig {
		public bool overlapSupport;
		public ErrorCorrectionConfig errorCorrection;

		public MSDFGeneratorConfig(bool overlapSupport, ErrorCorrectionConfig errorCorrection) {
			this.overlapSupport = overlapSupport;
			this.errorCorrection = errorCorrection;
		}

		public static MSDFGeneratorConfig Default => new MSDFGeneratorConfig(true, ErrorCorrectionConfig.Default);
	}
}
