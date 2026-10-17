using System;
using UnityEngine;
using PrimeTween;
using Sperlich.Easing;
using Sperlich.EditorKit;
using Sperlich.Sequencer;

namespace Sperlich.Text {

	// External AnimSequencer module: SText versions of the built-in TMP_Text steps (TypeWriter, TextCounter,
	// SetText). The dependency runs one way (this assembly -> Sperlich.Sequencer); the Editor finds these types
	// via TypeCache and lists them under "Customs/SText".

	[Serializable]
	[AnimStepMenu(subGroup: "SText", tooltip: "Instantly sets an SText string.")]
	public class STextSetTextStep : IInstantStep, IRestorableState {
		public SText target;
		[TextArea] public string text = "";

		[NonSerialized] string _snapshot;

		public float GetScheduleDuration(AnimSequencer owner) => 0f;
		public string GetSummary() => "<b>SText: Set Text</b>";
		public void Execute(AnimSequencer owner) {
			if (target != null) target.SetText(text);
		}
		public void CaptureState(AnimSequencer owner) {
			if (target != null) _snapshot = target.text;
		}
		public void RestoreState(AnimSequencer owner) {
			if (target != null) target.SetText(_snapshot);
		}
	}

	[Serializable]
	[AnimStepMenu(subGroup: "SText", tooltip: "Counts an SText number from -> to over the duration.")]
	public class STextTextCounterStep : ITweenStep, ISnapToStart, IRestorableState, IRerollable {
		public SText target;
		[HideIf(nameof(animateFromCurrent))] public AnimFloat from = 0f;
		public AnimFloat to = 100f;
		public string format = "{0}";
		public bool roundToInt = true;
		[AnimLimit(0f)] public AnimFloat duration = 0.3f;
		public Ease ease = Ease.OutCubic;
		[ShowIf(nameof(ease), Ease.Custom)] public SCurve customCurve = AnimStepUtil.DefaultCurve();
		[Tooltip("Start from the last value this step showed.")]
		public bool animateFromCurrent;

		[NonSerialized] float _currentValue;
		[NonSerialized] float _start;
		[NonSerialized] bool _started;
		[NonSerialized] string _snapshot;

		public float GetScheduleDuration(AnimSequencer owner) => Mathf.Max(duration.Value, 0f);
		public void Reroll() {
			from.Reroll();
			to.Reroll();
			duration.Reroll();
		}
		public string GetSummary() => $"<b>SText: Counter</b>  {AnimStepUtil.Dur(duration)}";

		public Tween CreateTween(AnimSequencer owner, float startDelay) {
			if (target == null) return Tween.Delay(startDelay + duration.Value);
			_started = false;
			var settings = AnimStepUtil.MakeSettings(duration.Value, ease, customCurve, startDelay);
			return Tween.Custom(this, new TweenSettings<float>(0f, 1f, settings), (s, v) => s.Apply(v));
		}

		void Apply(float t) {
			if (target == null) return;
			if (!_started) {
				_start = animateFromCurrent ? _currentValue : from.Value;
				_started = true;
			}
			Show(Mathf.LerpUnclamped(_start, to.Value, t));
		}

		void Show(float value) {
			_currentValue = value;
			target.SetText(string.Format(format, roundToInt ? Mathf.RoundToInt(value) : value));
		}

		public void SnapToStart(AnimSequencer owner) {
			if (!animateFromCurrent && target != null) Show(from.Value);
		}
		public void CaptureState(AnimSequencer owner) {
			if (target != null) _snapshot = target.text;
		}
		public void RestoreState(AnimSequencer owner) {
			if (target != null) target.SetText(_snapshot);
		}
	}

	[Serializable]
	[AnimStepMenu(subGroup: "SText", tooltip: "Reveals an SText label character by character.")]
	public class STextTypeWriterStep : ITweenStep, ISnapToStart, IInterruptible, IRerollable {
		public SText target;
		[TextArea] public string text = "";
		[AnimLimit(0.01f)] public AnimFloat charsPerSecond = 20f;

		public float GetScheduleDuration(AnimSequencer owner) {
			string t = string.IsNullOrEmpty(text) ? target != null ? target.text : null : text;
			float cps = AnimValues.IsEstimatingMax ? charsPerSecond.Min : charsPerSecond.Value;
			return Mathf.Max((t ?? "").Length, 1f) / Mathf.Max(cps, 0.01f);
		}
		public void Reroll() => charsPerSecond.Reroll();
		public string GetSummary() => "<b>SText: Type Writer</b>";

		// SText runs its own reveal (RevealController). This step configures it and wraps it in a timed tween
		// of the same length, and forces a full reveal when that tween ends (also on Complete()).
		public Tween CreateTween(AnimSequencer owner, float startDelay) {
			if (target == null) return Tween.Delay(startDelay);
			if (!string.IsNullOrEmpty(text)) target.text = text;
			target.Reveal.charsPerSecond = charsPerSecond.Value;
			target.TypewriterEnabled = true;

			var settings = AnimStepUtil.MakeSettings(GetScheduleDuration(owner), Ease.Linear, null, startDelay);
			return Tween.Custom(this, new TweenSettings<float>(0f, 1f, settings), (s, v) => {
				if (v >= 1f && s.target != null) s.target.SkipTypewriter();
			});
		}
		public void SnapToStart(AnimSequencer owner) {
			if (target == null) return;
			if (!string.IsNullOrEmpty(text)) target.text = text;
			target.Reveal.charsPerSecond = charsPerSecond.Value;
			target.TypewriterEnabled = true;
		}
		public void OnInterrupted(AnimSequencer owner) {
			if (target != null) target.SkipTypewriter();
		}
	}
}
