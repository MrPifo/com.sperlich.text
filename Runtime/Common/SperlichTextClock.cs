using UnityEngine;
using PauseMgr = Sperlich.PauseManager.PauseManager;

namespace Sperlich.Text {

	/// <summary>
	/// Single time source for every animated part of the text renderer (effects, reveal, caret blink).
	/// Pause-aware by default via <c>Sperlich.PauseManager</c>; the host game can replace the provider
	/// to hook a different clock without the package taking a hard dependency on it.
	/// </summary>
	public static class SperlichTextClock {

		/// <summary>Delta time provider. Defaults to a pause-gated <see cref="Time.deltaTime"/>.</summary>
		public static System.Func<float> DeltaTimeProvider = DefaultDelta;

		/// <summary>Absolute time provider. Defaults to a pause-gated accumulator.</summary>
		public static System.Func<float> TimeProvider = DefaultTime;

		private static float accumulated;
		private static int lastAccumulatedFrame = -1;

		/// <summary>Seconds since the last frame, or 0 while the game is paused.</summary>
		public static float DeltaTime => DeltaTimeProvider();

		/// <summary>Monotonic seconds that stops advancing while the game is paused.</summary>
		public static float Time => TimeProvider();

		private static float DefaultDelta() {
			return PauseMgr.IsPaused ? 0f : UnityEngine.Time.deltaTime;
		}

		private static float DefaultTime() {
			if (lastAccumulatedFrame != UnityEngine.Time.frameCount) {
				lastAccumulatedFrame = UnityEngine.Time.frameCount;
				if (PauseMgr.IsPaused == false) {
					accumulated += UnityEngine.Time.deltaTime;
				}
			}
			return accumulated;
		}
	}
}
