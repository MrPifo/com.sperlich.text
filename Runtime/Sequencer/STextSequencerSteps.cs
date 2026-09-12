using System;
using UnityEngine;
using PrimeTween;
using Sperlich.Sequencer;

namespace Sperlich.Text {

	// ==========================================================================================
	// First external AnimSequencer extension module: mirrors the sequencer's built-in TMP_Text
	// steps (TypeWriter, TextCounter, SetText) for SText. Nothing in com.sperlich.sequencer
	// references this file or this assembly — the dependency runs one way, from this assembly's
	// asmdef (Sperlich.Text.Sequencer) onto Sperlich.Sequencer's public IAnimStep/ITweenStep/
	// IInstantStep contracts and AnimStepMenuAttribute. Discovery in the Editor's "Add Step" menu
	// happens via TypeCache scanning for IAnimStep-derived types, so nothing here needs to be
	// registered anywhere by hand.
	//
	// None of these attributes set a Group, so all three fall under the "Customs" top-level
	// category with the shared "SText" subgroup — and since they're defined outside the sequencer's
	// own assembly, the Editor also marks them as external automatically.
	// ==========================================================================================

	[AnimStepMenu(subGroup: "SText", tooltip: "Instantly sets an SText string.")]
	public class STextSetTextStep : IInstantStep {
		public SText target;
		public string text = "";

		public float GetScheduleDuration() => 0.001f;
		public string GetSummary() => "<b>SText: SetText</b>";
		public void Execute(AnimSequencer owner) {
			if (target != null) target.SetText(text);
		}
	}

	[AnimStepMenu(subGroup: "SText", tooltip: "Counts an SText number from -> to over the duration.")]
	public class STextTextCounterStep : ITweenStep, ISnapToStart {
		public SText target;
		public bool animateFromCurrent = false;
		public float from = 0f;
		public float to = 100f;
		public string format = "{0}";
		public bool roundToInt = true;
		[Min(0f)] public float duration = 0.3f;
		public Ease ease = Ease.OutCubic;
		public AnimationCurve customCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		[NonSerialized] float _currentValue;

		public float GetScheduleDuration() => Mathf.Max(duration, 0f);
		public string GetSummary() => $"<b>SText: TextCounter</b>  {AnimStepUtil.Dur(duration)}";

		public Tween CreateTween(AnimSequencer owner, float absoluteDelay) {
			if (target == null) return Tween.Delay(Mathf.Max(duration + absoluteDelay, 0f));
			float fromNum = animateFromCurrent ? _currentValue : from;
			if (!animateFromCurrent) {
				_currentValue = fromNum;
				target.SetText(string.Format(format, roundToInt ? Mathf.RoundToInt(fromNum) : fromNum));
			}
			var settings = AnimStepUtil.MakeSettings(duration, ease, customCurve, absoluteDelay);
			return Tween.Custom(target, new TweenSettings<float>(fromNum, to, settings), (obj, v) => {
				_currentValue = v;
				obj.SetText(string.Format(format, roundToInt ? Mathf.RoundToInt(v) : v));
			});
		}
		public void SnapToStart(AnimSequencer owner) {
			if (animateFromCurrent || target == null) return;
			_currentValue = from;
			target.SetText(string.Format(format, roundToInt ? Mathf.RoundToInt(from) : from));
		}
	}

	[AnimStepMenu(subGroup: "SText", tooltip: "Reveals an SText label character by character at a given speed.")]
	public class STextTypeWriterStep : ITweenStep, ISnapToStart {
		public SText target;
		public string text = "";
		public float charsPerSecond = 20f;

		public float GetScheduleDuration() {
			string t = string.IsNullOrEmpty(text) ? target != null ? target.text : null : text;
			return Mathf.Max((t ?? "").Length, 1f) / Mathf.Max(charsPerSecond, 1f);
		}
		public string GetSummary() => "<b>SText: TypeWriter</b>";

		// SText drives its own character reveal internally (RevealController, ticked once per frame from
		// SText's own render update) instead of exposing a TMP-style "maxVisibleCharacters" a tween can
		// puppeteer frame-by-frame. So rather than lerping a visible-character count like the built-in TMP
		// TypeWriterStep does, this configures SText's own reveal speed/text and lets it run on its own clock,
		// wrapped in a plain timed tween sized to match (text length / charsPerSecond) for correct scheduling
		// alongside other steps — and forces a full reveal via SkipTypewriter() when that tween completes, so
		// a force-completed sequence (PrimeTween's Complete() jumps straight to v=1) can't leave the reveal
		// visually stuck partway through.
		public Tween CreateTween(AnimSequencer owner, float absoluteDelay) {
			if (target == null) return Tween.Delay(Mathf.Max(absoluteDelay, 0f));
			if (!string.IsNullOrEmpty(text)) target.text = text;
			target.Reveal.charsPerSecond = charsPerSecond;
			target.TypewriterEnabled = true;

			float estimatedDur = GetScheduleDuration();
			var settings = AnimStepUtil.MakeSettings(estimatedDur, Ease.Linear, null, absoluteDelay);
			var capturedTarget = target;
			bool skipped = false;
			return Tween.Custom(target, new TweenSettings<float>(0f, 1f, settings), (obj, v) => {
				if (v >= 1f && !skipped) {
					skipped = true;
					capturedTarget.SkipTypewriter();
				}
			});
		}
		public void SnapToStart(AnimSequencer owner) {
			if (target == null) return;
			if (!string.IsNullOrEmpty(text)) target.text = text;
			target.Reveal.charsPerSecond = charsPerSecond;
			target.TypewriterEnabled = true;
		}
	}
}
