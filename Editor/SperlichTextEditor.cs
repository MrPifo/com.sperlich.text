using UnityEditor;
using UnityEngine;
using Sperlich.Text;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Inspector for <see cref="SperlichText"/> (plan module 13): grouped fields, a tag-insert toolbar so
	/// the markup syntax need not be memorised, and a linter-style readability warning when the size drops
	/// below the platform accessibility minimum. Live preview is automatic — the component is a uGUI Graphic
	/// and rebuilds in edit mode.
	/// </summary>
	[CustomEditor(typeof(SperlichText))]
	public sealed class SperlichTextEditor : Editor {

		private SerializedProperty text, font, fontSize, richText;
		private SerializedProperty align, verticalAlign, wrap, overflow;
		private SerializedProperty autoSize, autoSizeMin, autoSizeMax;
		private SerializedProperty lineSpacing, paragraphSpacing, extraTrackingEm;
		private SerializedProperty builtinEffects, color, raycastTarget, maskable;
		private SerializedProperty typewriter, reveal;

		private PlatformContext lintContext = PlatformContext.PcFullHd;

		private void OnEnable() {
			text = serializedObject.FindProperty("m_text");
			font = serializedObject.FindProperty("m_font");
			fontSize = serializedObject.FindProperty("m_fontSize");
			richText = serializedObject.FindProperty("m_richText");
			align = serializedObject.FindProperty("m_align");
			verticalAlign = serializedObject.FindProperty("m_verticalAlign");
			wrap = serializedObject.FindProperty("m_wrap");
			overflow = serializedObject.FindProperty("m_overflow");
			autoSize = serializedObject.FindProperty("m_autoSize");
			autoSizeMin = serializedObject.FindProperty("m_autoSizeMin");
			autoSizeMax = serializedObject.FindProperty("m_autoSizeMax");
			lineSpacing = serializedObject.FindProperty("m_lineSpacing");
			paragraphSpacing = serializedObject.FindProperty("m_paragraphSpacing");
			extraTrackingEm = serializedObject.FindProperty("m_extraTrackingEm");
			builtinEffects = serializedObject.FindProperty("m_builtinEffects");
			typewriter = serializedObject.FindProperty("m_typewriter");
			reveal = serializedObject.FindProperty("m_reveal");
			color = serializedObject.FindProperty("m_Color");
			raycastTarget = serializedObject.FindProperty("m_RaycastTarget");
			maskable = serializedObject.FindProperty("m_Maskable");
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			EditorGUILayout.LabelField("Content", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(text);
			DrawTagToolbar();
			EditorGUILayout.PropertyField(richText);
			EditorGUILayout.PropertyField(font);
			if (color != null) EditorGUILayout.PropertyField(color, new GUIContent("Base Color"));

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Sizing", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(fontSize);
			EditorGUILayout.PropertyField(autoSize, new GUIContent("Auto Size"));
			if (autoSize.boolValue) {
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(autoSizeMin, new GUIContent("Min"));
				EditorGUILayout.PropertyField(autoSizeMax, new GUIContent("Max"));
				EditorGUI.indentLevel--;
			}
			DrawReadabilityLint();

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(align);
			EditorGUILayout.PropertyField(verticalAlign);
			EditorGUILayout.PropertyField(wrap);
			EditorGUILayout.PropertyField(overflow);
			EditorGUILayout.PropertyField(lineSpacing, new GUIContent("Line Spacing x"));
			EditorGUILayout.PropertyField(paragraphSpacing, new GUIContent("Paragraph Spacing x"));
			EditorGUILayout.PropertyField(extraTrackingEm, new GUIContent("Extra Tracking (em)"));

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Effects (built-in Burst catalog)", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(builtinEffects, true);
			EditorGUILayout.PropertyField(typewriter, new GUIContent("Typewriter Reveal"));
			if (typewriter.boolValue) {
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(reveal, true);
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Face & Material FX (whole label)", EditorStyles.boldLabel);
			DrawByName("m_faceDilate", "m_sharpness",
				"m_outlineColor", "m_outlineWidth",
				"m_shadowColor", "m_shadowOffset", "m_shadowSoftness", "m_shadowDilate",
				"m_glowColor", "m_glowPower", "m_glowOuter");

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("uGUI", EditorStyles.boldLabel);
			if (raycastTarget != null) EditorGUILayout.PropertyField(raycastTarget);
			if (maskable != null) EditorGUILayout.PropertyField(maskable);

			if (serializedObject.ApplyModifiedProperties()) {
				// OnValidate on the component pushes the new material props; we just force a re-mesh.
				foreach (Object t in targets) {
					if (t is SperlichText st) st.SetAllDirty();
				}
			}

			if (Application.isPlaying == false) {
				EditorGUILayout.Space();
				EditorGUILayout.HelpBox("Preview updates live in the Scene/Game view without entering Play mode.", MessageType.None);
			}
		}

		private void DrawTagToolbar() {
			if (!richText.boolValue) return;
			using (new EditorGUILayout.HorizontalScope()) {
				EditorGUILayout.LabelField("Insert tag", GUILayout.Width(70));
				InsertButton("B", "<b>", "</b>");
				InsertButton("I", "<i>", "</i>");
				InsertButton("U", "<u>", "</u>");
				InsertButton("S", "<s>", "</s>");
				InsertButton("Color", "<color=#ffcc00>", "</color>");
				InsertButton("Grad", "<gradient=#ffffff,#3388ff>", "</gradient>");
				InsertButton("Size", "<size=150%>", "</size>");
				InsertButton("Mark", "<mark=#ffff0059>", "</mark>");
				InsertButton("Link", "<link=\"id\">", "</link>");
				InsertButton("Glyph", "<glyph:Jump>", "");
			}
		}

		private void DrawByName(params string[] names) {
			foreach (string n in names) {
				SerializedProperty sp = serializedObject.FindProperty(n);
				if (sp != null) EditorGUILayout.PropertyField(sp);
			}
		}

		private void InsertButton(string label, string open, string close) {
			if (GUILayout.Button(label, EditorStyles.miniButton)) {
				text.stringValue += string.IsNullOrEmpty(close) ? open : open + "text" + close;
				GUI.FocusControl(null);
			}
		}

		private void DrawReadabilityLint() {
			lintContext = (PlatformContext)EditorGUILayout.EnumPopup(
				new GUIContent("Lint target", "Advisory only: compares the font size to the platform's accessibility minimum (Xbox Accessibility Guidelines 101). Changes nothing at runtime."),
				lintContext);

			float min = TypographyDefaults.MinReadablePx(lintContext);
			float effective = autoSize.boolValue ? autoSizeMin.floatValue : fontSize.floatValue;

			if (effective < min) {
				EditorGUILayout.HelpBox(
					$"{effective:0.#}px is BELOW the {min:0}px readability minimum for {lintContext} (XAG 101). Advisory only.",
					MessageType.Warning);
			} else {
				EditorGUILayout.HelpBox(
					$"{effective:0.#}px meets the {min:0}px readability minimum for {lintContext}.",
					MessageType.Info);
			}
		}
	}
}
