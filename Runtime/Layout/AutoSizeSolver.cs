using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Finds the largest font size in [min, max] whose laid-out block fits a target box, by interval
	/// bisection (plan module 5.3). Pure: it only calls the supplied measure delegate, so it is unit-tested
	/// with a synthetic function.
	/// </summary>
	public static class AutoSizeSolver {

		public delegate Vector2 MeasureDelegate(float fontSize);

		/// <summary>
		/// Returns the best fitting size. <paramref name="targetWidth"/> / <paramref name="targetHeight"/>
		/// of 0 or less mean "unconstrained on that axis".
		/// </summary>
		public static float Solve(MeasureDelegate measure, float minSize, float maxSize,
			float targetWidth, float targetHeight, int iterations = 12, float tolerance = 0.25f) {

			if (measure == null) return maxSize;
			minSize = Mathf.Max(1f, minSize);
			maxSize = Mathf.Max(minSize, maxSize);

			if (Fits(measure(maxSize), targetWidth, targetHeight)) return maxSize;
			if (!Fits(measure(minSize), targetWidth, targetHeight)) return minSize;

			float lo = minSize;
			float hi = maxSize;
			for (int i = 0; i < iterations && (hi - lo) > tolerance; i++) {
				float mid = 0.5f * (lo + hi);
				if (Fits(measure(mid), targetWidth, targetHeight)) lo = mid;
				else hi = mid;
			}
			return lo;
		}

		private static bool Fits(Vector2 size, float targetWidth, float targetHeight) {
			bool wOk = targetWidth <= 0f || size.x <= targetWidth + 0.01f;
			bool hOk = targetHeight <= 0f || size.y <= targetHeight + 0.01f;
			return wOk && hOk;
		}
	}
}
