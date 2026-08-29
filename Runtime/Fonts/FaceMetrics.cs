namespace Sperlich.Text {

	/// <summary>
	/// Plain-data copy of the FontEngine <c>FaceInfo</c> fields the layout engine needs.
	/// All values are in font units at the sampling point size (see <see cref="SamplingPointSize"/>);
	/// multiply by <c>fontSize / SamplingPointSize</c> to get local space units.
	/// </summary>
	[System.Serializable]
	public struct FaceMetrics {

		public float SamplingPointSize;
		public float Scale;
		public float LineHeight;
		public float AscentLine;
		public float CapLine;
		public float MeanLine;
		public float Baseline;
		public float DescentLine;
		public float UnderlineOffset;
		public float UnderlineThickness;
		public float StrikethroughOffset;
		public float StrikethroughThickness;
		public float SuperscriptOffset;
		public float SuperscriptSize;
		public float SubscriptOffset;
		public float SubscriptSize;
		public float TabWidth;

		public bool IsValid => SamplingPointSize > 0f && LineHeight > 0f;

		/// <summary>Scale factor from font-unit metrics to a rendered <paramref name="fontSize"/>.</summary>
		public float UnitScale(float fontSize) => SamplingPointSize > 0f ? fontSize / SamplingPointSize : 0f;
	}
}
