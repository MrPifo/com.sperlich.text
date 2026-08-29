using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Single time source for every animated part of the text renderer (effects, reveal, caret blink).
	/// The package has no dependency on any pause system: by default the clock just follows
	/// <see cref="Time.deltaTime"/>. To make it pause-aware, point <see cref="IsPausedProvider"/> at
	/// your own pause state (one line, e.g. <c>SperlichTextClock.IsPausedProvider = () =&gt; MyPause.IsPaused;</c>),
	/// or replace <see cref="DeltaTimeProvider"/> / <see cref="TimeProvider"/> entirely.
	/// </summary>
	public static class SperlichTextClock {

		/// <summary>
		/// Optional pause hook. Returns <c>true</c> while animated text should hold still.
		/// Defaults to "never paused" so the package works stand-alone.
		/// </summary>
		public static System.Func<bool> IsPausedProvider = () => false;

		/// <summary>Delta time provider. Defaults to a pause-gated <see cref="Time.deltaTime"/>.</summary>
		public static System.Func<float> DeltaTimeProvider = DefaultDelta;

		/// <summary>Absolute time provider. Defaults to a pause-gated accumulator.</summary>
		public static System.Func<float> TimeProvider = DefaultTime;

		private static float accumulated;
		private static int lastAccumulatedFrame = -1;

		/// <summary>Seconds since the last frame, or 0 while <see cref="IsPausedProvider"/> reports paused.</summary>
		public static float DeltaTime => DeltaTimeProvider();

		/// <summary>Monotonic seconds that stops advancing while <see cref="IsPausedProvider"/> reports paused.</summary>
		public static float Time => TimeProvider();

		private static bool IsPaused => IsPausedProvider != null && IsPausedProvider();

		private static float DefaultDelta() {
			return IsPaused ? 0f : UnityEngine.Time.deltaTime;
		}

		private static float DefaultTime() {
			if (lastAccumulatedFrame != UnityEngine.Time.frameCount) {
				lastAccumulatedFrame = UnityEngine.Time.frameCount;
				if (IsPaused == false) {
					accumulated += UnityEngine.Time.deltaTime;
				}
			}
			return accumulated;
		}
	}
}
