using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Base class for custom user text effects written as standard Unity MonoBehaviours.
	/// Automatically attaches to a <see cref="SText"/> component on the same GameObject
	/// and participates in the mesh modification pipeline every frame.
	/// </summary>
	[RequireComponent(typeof(SText))]
	[ExecuteAlways]
	public abstract class TextEffectBehaviour : MonoBehaviour, ITextEffect {

		private SText targetText;

		/// <summary>The <see cref="SText"/> instance this effect is attached to.</summary>
		public SText Text {
			get {
				if (targetText == null) targetText = GetComponent<SText>();
				return targetText;
			}
		}

		protected virtual void Awake() {
			targetText = GetComponent<SText>();
		}

		protected virtual void OnEnable() {
			targetText = GetComponent<SText>();
			if (targetText != null) {
				targetText.Effects.AddScript(this);
				targetText.SetVerticesDirty();
			}
		}

		protected virtual void OnDisable() {
			if (targetText != null) {
				targetText.Effects.RemoveScript(this);
				targetText.SetVerticesDirty();
			}
		}

		/// <summary>
		/// Called every frame after text layout and built-in effects have run, before mesh upload.
		/// Mutates vertex positions, colors, UVs, or scales in place.
		/// </summary>
		/// <param name="ctx">Context providing access to glyph centers, counts, and transformation methods.</param>
		public abstract void Apply(TextEffectContext ctx);
	}
}
