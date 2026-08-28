using System;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Typewriter reveal state (plan module 9.3). Kept as Ebene 1 logic: time-stepped with per-char
	/// callbacks and punctuation pauses, driven by <see cref="SperlichTextClock"/> so it pauses with the game.
	/// The renderer feeds <see cref="VisibleChars"/> into <see cref="TextEffectStack.RevealVisibleChars"/>.
	/// </summary>
	[Serializable]
	public sealed class RevealController {

		[Tooltip("Characters revealed per second.")]
		public float charsPerSecond = 30f;

		[Tooltip("Extra seconds to wait after sentence punctuation ( . ! ? ).")]
		public float sentencePause = 0.28f;

		[Tooltip("Extra seconds to wait after clause punctuation ( , ; : ).")]
		public float clausePause = 0.12f;

		[Tooltip("How many characters the leading edge fades over.")]
		public float fadeChars = 1.5f;

		private string source = string.Empty;
		private float revealed;
		private float pauseTimer;
		private bool running;
		private int lastReportedChar = -1;

		/// <summary>Raised once per newly fully-revealed character with its stripped-text index.</summary>
		public event Action<int> CharRevealed;

		/// <summary>Raised when the whole string is revealed.</summary>
		public event Action Completed;

		public bool IsRunning => running;
		public bool IsComplete => running == false && revealed >= source.Length;
		public int VisibleChars => Mathf.Clamp(Mathf.FloorToInt(revealed), 0, source.Length);
		public float FadeChars => fadeChars;

		/// <summary>Starts (or restarts) the reveal for <paramref name="strippedText"/>.</summary>
		public void Begin(string strippedText) {
			source = strippedText ?? string.Empty;
			revealed = 0f;
			pauseTimer = 0f;
			lastReportedChar = -1;
			running = source.Length > 0;
			if (running == false) Completed?.Invoke();
		}

		/// <summary>Reveals everything immediately.</summary>
		public void SkipToEnd() {
			if (source.Length == 0) return;
			int from = VisibleChars;
			revealed = source.Length;
			pauseTimer = 0f;
			for (int i = from; i < source.Length; i++) CharRevealed?.Invoke(i);
			lastReportedChar = source.Length - 1;
			if (running) { running = false; Completed?.Invoke(); }
		}

		public void Pause() => running = false;
		public void Resume() { if (revealed < source.Length) running = true; }

		/// <summary>Advances the reveal. Call every frame; uses the pause-aware clock delta by default.</summary>
		public void Tick(float? deltaOverride = null) {
			if (running == false) return;
			float dt = deltaOverride ?? SperlichTextClock.DeltaTime;
			if (dt <= 0f) return;

			if (pauseTimer > 0f) {
				pauseTimer -= dt;
				return;
			}

			float before = revealed;
			revealed = Mathf.Min(source.Length, revealed + charsPerSecond * dt);

			int fromChar = Mathf.FloorToInt(before);
			int toChar = Mathf.FloorToInt(revealed);
			for (int i = fromChar; i < toChar && i < source.Length; i++) {
				if (i <= lastReportedChar) continue;
				lastReportedChar = i;
				CharRevealed?.Invoke(i);
				char c = source[i];
				if (c == '.' || c == '!' || c == '?' || c == '…') pauseTimer = Mathf.Max(pauseTimer, sentencePause);
				else if (c == ',' || c == ';' || c == ':') pauseTimer = Mathf.Max(pauseTimer, clausePause);
			}

			if (revealed >= source.Length) {
				running = false;
				Completed?.Invoke();
			}
		}
	}
}
