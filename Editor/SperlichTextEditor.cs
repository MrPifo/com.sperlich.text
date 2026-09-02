using Sperlich.EditorKit;
using Sperlich.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Inspector for <see cref="SText"/>, built on UI Toolkit with the shared Sperlich EditorKit:
	/// collapsible sections, pill toggles, the custom enum dropdown, Word-style icon buttons for the two
	/// alignments, and a resizable / scrolling markup editor with a tag-insert toolbar.
	/// </summary>
	[CustomEditor(typeof(SText))]
	public sealed class SperlichTextEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;

		private TextField textField;
		private float editorHeight = 110f;

		/// <summary>Eine gemeinsame Label-Spalte für den ganzen Inspektor — jede Zeile (PropertyField, Enum,
		/// PillToggle, Icon-Buttons) beginnt das Control an exakt derselben X-Position.</summary>
		private readonly SperlichFieldColumn col = new(142f);

		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement {
				style = {
					paddingTop = 2,
					paddingBottom = 4,
					marginLeft = -15,
					marginRight = -4
				}
			};

			// ---- Content -------------------------------------------------------------------------
			var content = Section(root, "CONTENT", true);
			content.Add(BuildTextArea());
			content.Add(col.Row("Font Style", SperlichEditorWidgets.CreateFlagButtons(
				serializedObject.FindProperty("m_fontStyle"),
				new[] { "B", "I", "U", "S", "AB", "ab", "Ab" },
				new[] { "Bold", "Italic", "Underline", "Strikethrough", "UPPERCASE", "lowercase", "SmallCaps" },
				Accent,
				new[] { new[] { 4, 5, 6 } },      // Uppercase / Lowercase / SmallCaps are mutually exclusive
				new[] { 4 })));                   // divider between the text-style and the case buttons
			content.Add(BoolRow("m_richText", "Rich Text"));
			content.Add(col.Row("Font", SperlichEditorWidgets.CreateAssetDropdown<FontDefinition>(serializedObject.FindProperty("m_font"), Accent)));
			content.Add(Field("m_Color", "Base Color"));

			// ---- Layout (sizing folded in) -----------------------------------------------------
			var layout = Section(root, "LAYOUT", true);
			layout.Add(col.Property(serializedObject.FindProperty("m_fontSize"), "Font Size"));
			layout.Add(BoolRow("m_autoSize", "Auto Size"));
			var autoBox = new VisualElement();
			autoBox.Add(col.Property(serializedObject.FindProperty("m_autoSizeMin"), "Min", indent: 1));
			autoBox.Add(col.Property(serializedObject.FindProperty("m_autoSizeMax"), "Max", indent: 1));
			layout.Add(autoBox);
			layout.Add(AlignedRow("Align", AlignButtons(serializedObject.FindProperty("m_align"), false,
				new[] { "Left", "Center", "Right", "Justified", "Flush (justify last line too)", "Geometry Center" })));
			layout.Add(AlignedRow("Vertical Align", AlignButtons(serializedObject.FindProperty("m_verticalAlign"), true,
				new[] { "Top", "Middle", "Bottom", "Baseline" })));
			layout.Add(EnumRow("m_wrap", "Wrap"));
			layout.Add(EnumRow("m_overflow", "Overflow"));

			layout.Add(col.Row("Spacing Options (em)", SperlichEditorWidgets.CreateFieldCluster(120,
				SperlichEditorWidgets.CreateCompactField("Character", serializedObject.FindProperty("m_extraTrackingEm")),
				SperlichEditorWidgets.CreateCompactField("Word", serializedObject.FindProperty("m_wordSpacingEm")),
				SperlichEditorWidgets.CreateCompactField("Line", serializedObject.FindProperty("m_lineSpacing")),
				SperlichEditorWidgets.CreateCompactField("Paragraph", serializedObject.FindProperty("m_paragraphSpacing")))));

			layout.Add(col.Row("Margins", SperlichEditorWidgets.CreateFieldCluster(52,
				SperlichEditorWidgets.CreateCompactField("Left", serializedObject.FindProperty("m_marginLeft"), captionAbove: true),
				SperlichEditorWidgets.CreateCompactField("Top", serializedObject.FindProperty("m_marginTop"), captionAbove: true),
				SperlichEditorWidgets.CreateCompactField("Right", serializedObject.FindProperty("m_marginRight"), captionAbove: true),
				SperlichEditorWidgets.CreateCompactField("Bottom", serializedObject.FindProperty("m_marginBottom"), captionAbove: true))));

			// ---- Effects ----------------------------------------------------------------------
			var fx = Section(root, "EFFECTS", false);
			fx.Add(BuildBuiltinEffectsList());
			fx.Add(BoolRow("m_typewriter", "Typewriter Reveal"));
			var revealBox = new VisualElement();
			revealBox.Add(SperlichFieldColumn.Raw(serializedObject.FindProperty("m_reveal"), "Reveal"));
			fx.Add(revealBox);

			// ---- Material FX: one titled collapsible each (no inner heading) ------------------
			var face = Section(root, "FACE", false);
			face.Add(Field("m_faceDilate", "Dilate"));
			face.Add(Field("m_sharpness", "Sharpness"));

			var outline = EffectSection(root, "OUTLINE", "m_outline", false);
			outline.Add(Field("m_outlineColor", "Color"));
			outline.Add(Field("m_outlineWidth", "Width (px)"));
			outline.Add(EnumRow("m_outlineMode", "Placement"));

			var shadow = EffectSection(root, "DROP SHADOW", "m_shadow", false);
			shadow.Add(Field("m_shadowColor", "Color"));
			shadow.Add(col.Row("Offset", SperlichEditorWidgets.CreateRadialVector2Field(
				serializedObject.FindProperty("m_shadowOffset"), 30f, Accent)));
			shadow.Add(Field("m_shadowSoftness", "Softness (px)"));
			shadow.Add(Field("m_shadowDilate", "Dilate"));
			shadow.Add(EnumRow("m_shadowQuality", "Quality"));

			var glow = EffectSection(root, "GLOW", "m_glow", false);
			glow.Add(Field("m_glowColor", "Color"));
			glow.Add(Field("m_glowPower", "Power"));
			glow.Add(Field("m_glowOuter", "Outer"));
			glow.Add(EnumRow("m_glowQuality", "Quality"));

			var bloomSec = EffectSection(root, "BLOOM", "m_bloom", false);
			bloomSec.Add(Field("m_bloomColor", "Color"));
			bloomSec.Add(Field("m_bloomRadius", "Radius"));
			bloomSec.Add(Field("m_bloomIntensity", "Intensity"));
			bloomSec.Add(Field("m_bloomFallOff", "Falloff"));
			bloomSec.Add(Field("m_bloomSamples", "Samples"));

			// ---- uGUI -----------------------------------------------------------------------
			var ugui = Section(root, "uGUI", false);
			ugui.Add(BoolRow("m_RaycastTarget", "Raycast Target"));
			ugui.Add(BoolRow("m_Maskable", "Maskable"));

			ShowWhen(autoBox, "m_autoSize");
			ShowWhen(revealBox, "m_typewriter");

			root.TrackSerializedObjectValue(serializedObject, _ => {
				foreach (UnityEngine.Object t in targets)
					if (t is SText st) st.SetAllDirty();
			});

			// keep the inspector scroll position across Undo/Redo rebuilds instead of snapping to top
			SperlichInspectorScroll.Preserve(root, target);
			return root;
		}

		// ============================ sections & rows =========================================

		private static VisualElement Section(VisualElement parent, string title, bool expanded) {
			var (header, body, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(SperlichTextEditor));
			body.style.paddingLeft = 6;
			body.style.paddingRight = 6;
			body.style.paddingTop = 4;
			body.style.paddingBottom = 6;
			var wrap = new VisualElement { style = { marginBottom = 4 } };
			wrap.Add(header);
			wrap.Add(body);
			parent.Add(wrap);
			return body;
		}

		private VisualElement EffectSection(VisualElement parent, string title, string boolProp, bool expanded) {
			SerializedProperty p = serializedObject.FindProperty(boolProp);
			var (header, body, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(SperlichTextEditor));
			body.style.paddingLeft = 6;
			body.style.paddingRight = 6;
			body.style.paddingTop = 4;
			body.style.paddingBottom = 6;

			var chk = new Toggle();
			chk.style.marginLeft = StyleKeyword.Auto;
			chk.style.marginRight = 4;
			chk.style.paddingTop = 0;
			chk.style.paddingBottom = 0;
			chk.style.marginTop = 0;
			chk.style.marginBottom = 0;
			chk.value = p != null && p.boolValue;
			chk.RegisterValueChangedCallback(evt => {
				if (p != null) {
					p.boolValue = evt.newValue;
					serializedObject.ApplyModifiedProperties();
				}
			});
			chk.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
			chk.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
			header.Add(chk);

			var paramsBox = new VisualElement { style = { marginTop = 2 } };
			body.Add(paramsBox);

			void UpdateState() {
				bool on = p != null && p.boolValue;
				chk.SetValueWithoutNotify(on);
				header.style.opacity = on ? 1.0f : 0.6f;
				paramsBox.SetEnabled(on);
				paramsBox.style.opacity = on ? 1.0f : 0.45f;
			}

			UpdateState();
			if (p != null) body.TrackPropertyValue(p, _ => UpdateState());

			var wrap = new VisualElement { style = { marginBottom = 4 } };
			wrap.Add(header);
			wrap.Add(body);
			parent.Add(wrap);
			return paramsBox;
		}

		/// <summary>Einfaches PropertyField als Column-Zeile (gemeinsame Label-Spalte, internes Feld-Label entfernt).</summary>
		private VisualElement Field(string prop, string label = null) {
			SerializedProperty sp = serializedObject.FindProperty(prop);
			if (sp == null) return new VisualElement();
			return col.Property(sp, label);
		}

		private VisualElement AlignedRow(string label, VisualElement control) => col.Row(label, control);

		private VisualElement EnumRow(string prop, string label) =>
			col.Row(label, SperlichEditorWidgets.CreateEnumDropdown(serializedObject.FindProperty(prop), Accent));

		private VisualElement BoolRow(string prop, string label) {
			SerializedProperty p = serializedObject.FindProperty(prop);
			if (p == null) return new VisualElement();
			var pill = new PillToggle(p.boolValue);
			pill.Clicked += () => {
				p.boolValue = !p.boolValue;
				serializedObject.ApplyModifiedProperties();
				pill.SetValue(p.boolValue);
			};
			VisualElement row = col.Row(label, pill);
			row.TrackPropertyValue(p, sp => pill.SetValue(sp.boolValue));
			return row;
		}

		// ============================ built-in effects list ==================================

		/// <summary>Schmalere Label-Spalte für die Effekt-Karten (die sind schon eingerückt).</summary>
		private readonly SperlichFieldColumn effectCol = new(104f);

		/// <summary>Custom list for <c>m_builtinEffects</c>: eine Karte pro Effekt mit Typ-Dropdown, Löschen-Button
		/// und NUR den Feldern, die der gewählte Effekt tatsächlich nutzt (blenden dynamisch um).</summary>
		private VisualElement BuildBuiltinEffectsList() {
			SerializedProperty listProp = serializedObject.FindProperty("m_builtinEffects");
			var root = new VisualElement { style = { marginBottom = 4 } };

			var title = new Label("Built-in Effects") {
				style = { fontSize = 12, color = SperlichEditorTheme.TextSecondary, unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 3, marginBottom = 2 }
			};
			root.Add(title);

			var itemsHost = new VisualElement();
			root.Add(itemsHost);

			int lastCount = -1;
			void Rebuild() {
				lastCount = listProp.arraySize;
				itemsHost.Clear();
				if (listProp.arraySize == 0) {
					itemsHost.Add(new Label("No effects — add one below.") {
						style = { fontSize = 10, color = SperlichEditorTheme.TextMuted, unityFontStyleAndWeight = FontStyle.Italic, marginLeft = 3, marginBottom = 3 }
					});
				}
				for (int i = 0; i < listProp.arraySize; i++) itemsHost.Add(BuildEffectCard(listProp, i, Rebuild));
			}

			var addBtn = SperlichEditorWidgets.MakeButton("+ Add Effect", 0, () => {
				int n = listProp.arraySize;
				listProp.arraySize = n + 1;
				SerializedProperty e = listProp.GetArrayElementAtIndex(n);
				ApplyBuiltinEffectDefaults(e, BuiltinEffect.Wave, forceEnable: true);
				serializedObject.ApplyModifiedProperties();
				Rebuild();
			}, isAccent: true);
			addBtn.style.marginTop = 4;
			addBtn.style.marginLeft = 3;
			root.Add(addBtn);

			Rebuild();
			root.TrackSerializedObjectValue(serializedObject, _ => { if (listProp.arraySize != lastCount) Rebuild(); });
			return root;
		}

		/// <summary>Wendet Standardwerte für einen Built-in-Effekt an.</summary>
		private void ApplyBuiltinEffectDefaults(SerializedProperty el, BuiltinEffect effect, bool forceEnable = false) {
			SerializedProperty effectProp = el.FindPropertyRelative("Effect");
			SerializedProperty enabledProp = el.FindPropertyRelative("Enabled");
			SerializedProperty amp = el.FindPropertyRelative("Amplitude");
			SerializedProperty freq = el.FindPropertyRelative("Frequency");
			SerializedProperty spd = el.FindPropertyRelative("Speed");
			SerializedProperty ca = el.FindPropertyRelative("ColorA");
			SerializedProperty cb = el.FindPropertyRelative("ColorB");
			SerializedProperty ramp = el.FindPropertyRelative("Ramp");

			if (forceEnable) enabledProp.boolValue = true;
			effectProp.enumValueIndex = (int)effect;

			switch (effect) {
				case BuiltinEffect.Wave:
					amp.floatValue = 6f;
					freq.floatValue = 0.35f;
					spd.floatValue = 6f;
					el.FindPropertyRelative("WaveStyle").enumValueIndex = (int)WaveStyle.Sine;
					el.FindPropertyRelative("Inverse").boolValue = false;
					el.FindPropertyRelative("Once").boolValue = false;
					el.FindPropertyRelative("Progress").floatValue = 0f;
					break;
				case BuiltinEffect.Shake:
					amp.floatValue = 2.5f;
					freq.floatValue = 30f;
					spd.floatValue = 1f;
					break;
				case BuiltinEffect.Pulse:
					amp.floatValue = 0.25f;
					freq.floatValue = 0.35f;
					spd.floatValue = 5f;
					el.FindPropertyRelative("ScaleStyle").enumValueIndex = (int)ScaleStyle.SquashAndStretch;
					el.FindPropertyRelative("Easing").enumValueIndex = (int)TextEasing.EaseOutBack;
					el.FindPropertyRelative("Angle").floatValue = 0f;
					el.FindPropertyRelative("Inverse").boolValue = false;
					el.FindPropertyRelative("Once").boolValue = false;
					el.FindPropertyRelative("Progress").floatValue = 0f;
					break;
				case BuiltinEffect.Rotate:
					amp.floatValue = 20f;
					freq.floatValue = 0.35f;
					spd.floatValue = 4f;
					el.FindPropertyRelative("RotateStyle").enumValueIndex = (int)RotateStyle.Wobble;
					el.FindPropertyRelative("Inverse").boolValue = false;
					el.FindPropertyRelative("Once").boolValue = false;
					el.FindPropertyRelative("Progress").floatValue = 0f;
					break;
				case BuiltinEffect.Rainbow:
					amp.floatValue = 1f;
					freq.floatValue = 0.04f;
					spd.floatValue = 1.5f;
					el.FindPropertyRelative("Inverse").boolValue = false;
					ramp.gradientValue = BuiltinEffectParams.CreateRainbowGradient();
					break;
				case BuiltinEffect.Glow:
					amp.floatValue = 0.25f;
					freq.floatValue = 1f;
					spd.floatValue = 2f;
					ca.colorValue = new Color(0.55f, 0.55f, 0.55f, 1f);
					cb.colorValue = Color.white;
					el.FindPropertyRelative("GlowStyle").enumValueIndex = (int)GlowStyle.Fade;
					el.FindPropertyRelative("Angle").floatValue = 25f;
					if (IsDefaultOrEmptyGradient(ramp.gradientValue)) {
						ramp.gradientValue = BuiltinEffectParams.CreateGoldShimmerGradient();
					}
					el.FindPropertyRelative("Inverse").boolValue = false;
					el.FindPropertyRelative("Once").boolValue = false;
					el.FindPropertyRelative("Progress").floatValue = 0f;
					break;
				case BuiltinEffect.Glitch:
					amp.floatValue = 3f;
					freq.floatValue = 12f;
					spd.floatValue = 1f;
					el.FindPropertyRelative("GlitchStyle").enumValueIndex = (int)GlitchStyle.Glitch;
					el.FindPropertyRelative("Amount").floatValue = 0.25f;
					el.FindPropertyRelative("ScrambleCharacters").stringValue = "";
					if (IsDefaultOrEmptyGradient(ramp.gradientValue)) {
						ramp.gradientValue = BuiltinEffectParams.CreateRainbowGradient();
					}
					el.FindPropertyRelative("Inverse").boolValue = false;
					el.FindPropertyRelative("Once").boolValue = false;
					el.FindPropertyRelative("Progress").floatValue = 0f;
					break;
			}
		}

		private VisualElement BuildEffectCard(SerializedProperty listProp, int index, System.Action rebuildAll) {
			SerializedProperty el = listProp.GetArrayElementAtIndex(index);
			SerializedProperty effectProp = el.FindPropertyRelative("Effect");
			SerializedProperty enabledProp = el.FindPropertyRelative("Enabled");

			var card = SperlichEditorWidgets.CreateBox(4, SperlichEditorTheme.BorderSubtle);
			card.style.backgroundColor = SperlichEditorTheme.BgStepBody;
			card.style.marginTop = 3;
			card.style.paddingLeft = 6;
			card.style.paddingRight = 6;
			card.style.paddingTop = 4;
			card.style.paddingBottom = 6;

			var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

			var pill = new PillToggle(enabledProp.boolValue);
			pill.style.marginRight = 6;
			pill.Clicked += () => {
				enabledProp.boolValue = !enabledProp.boolValue;
				serializedObject.ApplyModifiedProperties();
				pill.SetValue(enabledProp.boolValue);
			};
			top.Add(pill);

			var dd = SperlichEditorWidgets.CreateEnumDropdown(effectProp, Accent, newIdx => {
				ApplyBuiltinEffectDefaults(el, (BuiltinEffect)newIdx);
				serializedObject.ApplyModifiedProperties();
				rebuildAll();
			});
			dd.style.flexGrow = 1;
			top.Add(dd);

			var del = SperlichEditorWidgets.MakeButton("✕", 22, () => {
				listProp.DeleteArrayElementAtIndex(index);
				serializedObject.ApplyModifiedProperties();
				rebuildAll();
			});
			del.style.marginLeft = 4;
			top.Add(del);
			card.Add(top);

			var paramHost = new VisualElement { style = { marginTop = 3 } };
			PopulateEffectParams(paramHost, el, (BuiltinEffect)effectProp.enumValueIndex);
			card.Add(paramHost);

			void UpdateDisabledStyle() {
				paramHost.style.opacity = enabledProp.boolValue ? 1f : 0.45f;
				paramHost.SetEnabled(enabledProp.boolValue);
			}
			UpdateDisabledStyle();
			card.TrackPropertyValue(enabledProp, sp => {
				pill.SetValue(sp.boolValue);
				UpdateDisabledStyle();
			});

			// changing the effect type reshapes the whole list (fresh dropdown label + fields)
			card.TrackPropertyValue(effectProp, _ => rebuildAll());
			return card;
		}

		private static bool IsDefaultOrEmptyGradient(Gradient g) {
			if (g == null || g.colorKeys == null || g.colorKeys.Length == 0) return true;
			if (g.colorKeys.Length == 2 && g.colorKeys[0].color == Color.white && g.colorKeys[1].color == Color.white) return true;
			return false;
		}

		private VisualElement RampRow(SerializedProperty prop) {
			var gf = new UnityEditor.UIElements.GradientField { style = { flexGrow = 1 } };
			gf.BindProperty(prop);
			SperlichFieldColumn.HideInternalLabel(gf);
			return effectCol.Row("Color Ramp", gf);
		}

		private VisualElement ColorRow(SerializedProperty prop, string label) {
			var cf = new UnityEditor.UIElements.ColorField { style = { flexGrow = 1 }, showAlpha = true };
			cf.BindProperty(prop);
			SperlichFieldColumn.HideInternalLabel(cf);
			return effectCol.Row(label, cf);
		}

		/// <summary>"Percent Slider"-Zeile: Slider von 0..1 mit Prozentanzeige (0..100%).</summary>
		private VisualElement PercentSliderRow(SerializedProperty prop, string label, float maxScale = 1f) {
			var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
			var slider = new Slider(0f, 1f) { style = { flexGrow = 1 } };
			if (Mathf.Approximately(maxScale, 1f)) {
				slider.BindProperty(prop);
			} else {
				slider.value = Mathf.Clamp01(prop.floatValue / maxScale);
				slider.RegisterValueChangedCallback(evt => {
					prop.floatValue = evt.newValue * maxScale;
					prop.serializedObject.ApplyModifiedProperties();
				});
				slider.TrackPropertyValue(prop, _ => {
					float target = Mathf.Clamp01(prop.floatValue / maxScale);
					if (!Mathf.Approximately(slider.value, target)) slider.value = target;
				});
			}
			SperlichFieldColumn.HideInternalLabel(slider);

			var percentLabel = new Label {
				style = {
					width = 42,
					fontSize = 11,
					unityTextAlign = TextAnchor.MiddleRight,
					color = SperlichEditorTheme.TextSecondary,
					marginLeft = 4
				}
			};

			void UpdateText() => percentLabel.text = $"{Mathf.RoundToInt(Mathf.Clamp01(prop.floatValue / maxScale) * 100f)} %";
			UpdateText();
			row.TrackPropertyValue(prop, _ => UpdateText());

			row.Add(slider);
			row.Add(percentLabel);
			return effectCol.Row(label, row);
		}

		/// <summary>Slider-Zeile für Float-Werte mit min/max Grenzen und Zahlenfeld.</summary>
		private VisualElement FloatSliderRow(SerializedProperty prop, string label, float min, float max) {
			return effectCol.Slider(prop, label, min, max);
		}

		/// <summary>Textfeld-Zeile für Strings.</summary>
		private VisualElement TextRow(SerializedProperty prop, string label) {
			var tf = new TextField { style = { flexGrow = 1 } };
			tf.BindProperty(prop);
			SperlichFieldColumn.HideInternalLabel(tf);
			return effectCol.Row(label, tf);
		}

		private void PopulateEffectParams(VisualElement host, SerializedProperty el, BuiltinEffect effect) {
			SerializedProperty amp = el.FindPropertyRelative("Amplitude");
			SerializedProperty freq = el.FindPropertyRelative("Frequency");
			SerializedProperty spd = el.FindPropertyRelative("Speed");
			SerializedProperty ca = el.FindPropertyRelative("ColorA");
			SerializedProperty cb = el.FindPropertyRelative("ColorB");
			SerializedProperty ramp = el.FindPropertyRelative("Ramp");
			SerializedProperty amount = el.FindPropertyRelative("Amount");
			SerializedProperty inv = el.FindPropertyRelative("Inverse");
			SerializedProperty once = el.FindPropertyRelative("Once");
			SerializedProperty progress = el.FindPropertyRelative("Progress");

			switch (effect) {
				case BuiltinEffect.Wave: {
					host.Add(effectCol.Property(el.FindPropertyRelative("WaveStyle"), "Wave Style"));
					host.Add(effectCol.Property(amp, "Height"));
					host.Add(effectCol.Property(freq, "Wavelength"));
					host.Add(effectCol.Property(spd, "Speed"));
					host.Add(effectCol.Property(inv, "Inverse"));
					host.Add(effectCol.Property(once, "Once"));

					var progressRow = PercentSliderRow(progress, "Progress");
					host.Add(progressRow);
					void UpdateProg() => progressRow.style.display = once.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
					UpdateProg();
					progressRow.TrackPropertyValue(once, _ => UpdateProg());
					break;
				}
				case BuiltinEffect.Shake:
					host.Add(effectCol.Property(amp, "Distance"));
					host.Add(effectCol.Property(freq, "Shake Rate"));
					break;
				case BuiltinEffect.Pulse: {
					SerializedProperty scaleStyle = el.FindPropertyRelative("ScaleStyle");
					host.Add(effectCol.Property(scaleStyle, "Scale Style"));

					var easingRow = effectCol.Property(el.FindPropertyRelative("Easing"), "Easing");
					host.Add(easingRow);

					var ampRow = effectCol.Property(amp, "Scale Amount");
					host.Add(ampRow);

					var rotRow = effectCol.DragNumber(el.FindPropertyRelative("Angle"), "Initial Rotation (°)");
					host.Add(rotRow);

					host.Add(effectCol.Property(freq, "Wavelength"));
					host.Add(effectCol.Property(spd, "Speed"));
					host.Add(effectCol.Property(inv, "Inverse"));
					host.Add(effectCol.Property(once, "Once"));

					var progressRow = PercentSliderRow(progress, "Progress");
					host.Add(progressRow);

					void UpdateScaleVisibility() {
						ScaleStyle st = (ScaleStyle)scaleStyle.enumValueIndex;
						bool isTransition = (st == ScaleStyle.PopIn || st == ScaleStyle.PopOut);
						easingRow.style.display = (isTransition || st == ScaleStyle.SquashAndStretch) ? DisplayStyle.Flex : DisplayStyle.None;
						ampRow.style.display = isTransition ? DisplayStyle.None : DisplayStyle.Flex;
						rotRow.style.display = isTransition ? DisplayStyle.Flex : DisplayStyle.None;
						progressRow.style.display = (once.boolValue || isTransition) ? DisplayStyle.Flex : DisplayStyle.None;
					}
					UpdateScaleVisibility();
					host.TrackPropertyValue(scaleStyle, _ => UpdateScaleVisibility());
					host.TrackPropertyValue(once, _ => UpdateScaleVisibility());
					break;
				}
				case BuiltinEffect.Rotate: {
					SerializedProperty rotateStyle = el.FindPropertyRelative("RotateStyle");
					host.Add(effectCol.Property(rotateStyle, "Rotate Style"));

					var ampRow = effectCol.DragNumber(amp, "Rotation Angle (°)");
					host.Add(ampRow);

					host.Add(effectCol.Property(freq, "Wavelength"));
					host.Add(effectCol.Property(spd, "Speed"));
					host.Add(effectCol.Property(inv, "Inverse"));
					host.Add(effectCol.Property(once, "Once"));

					var progressRow = PercentSliderRow(progress, "Progress");
					host.Add(progressRow);

					void UpdateRotateVisibility() {
						RotateStyle rs = (RotateStyle)rotateStyle.enumValueIndex;
						ampRow.style.display = (rs == RotateStyle.Wobble) ? DisplayStyle.Flex : DisplayStyle.None;
						progressRow.style.display = once.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
					}
					UpdateRotateVisibility();
					host.TrackPropertyValue(rotateStyle, _ => UpdateRotateVisibility());
					host.TrackPropertyValue(once, _ => UpdateRotateVisibility());
					break;
				}
				case BuiltinEffect.Rainbow:
					host.Add(PercentSliderRow(freq, "Spread", 0.1f));
					host.Add(effectCol.Property(spd, "Speed"));
					host.Add(RampRow(ramp));
					host.Add(effectCol.Property(inv, "Inverse"));
					break;
				case BuiltinEffect.Glow: {
					SerializedProperty glowStyle = el.FindPropertyRelative("GlowStyle");
					host.Add(effectCol.Property(glowStyle, "Glow Style"));

					// Fade rows
					var fadeCa = ColorRow(ca, "Color A");
					var fadeCb = ColorRow(cb, "Color B");
					var fadeSharp = effectCol.Property(freq, "Fade Sharpness");
					var fadeSpd = effectCol.Property(spd, "Fade Speed");
					var fadeInv = effectCol.Property(inv, "Inverse");

					// Shimmer rows
					var shimmerRamp = RampRow(ramp);
					var shimmerWidth = FloatSliderRow(amp, "Beam Width", 0.02f, 0.8f);
					var shimmerCount = effectCol.SliderInt(freq, "Beam Count", 1, 10);
					var shimmerAngle = FloatSliderRow(el.FindPropertyRelative("Angle"), "Beam Angle", -80f, 80f);
					var shimmerSpd = effectCol.Property(spd, "Speed");
					var shimmerInv = effectCol.Property(inv, "Inverse");
					var shimmerOnce = effectCol.Property(once, "Once");
					var shimmerProg = PercentSliderRow(progress, "Progress");

					// Neon Flicker rows
					var flickerCa = ColorRow(ca, "Dim / Off Color");
					var flickerCb = ColorRow(cb, "Bright / On Color");
					var flickerSpd = effectCol.Property(spd, "Flicker Speed");
					var flickerRate = effectCol.Property(freq, "Flicker Rate");
					var flickerAmount = PercentSliderRow(amount, "Flicker Drop");

					host.Add(fadeCa);
					host.Add(fadeCb);
					host.Add(fadeSharp);
					host.Add(fadeSpd);
					host.Add(fadeInv);

					host.Add(shimmerRamp);
					host.Add(shimmerWidth);
					host.Add(shimmerCount);
					host.Add(shimmerAngle);
					host.Add(shimmerSpd);
					host.Add(shimmerInv);
					host.Add(shimmerOnce);
					host.Add(shimmerProg);

					host.Add(flickerCa);
					host.Add(flickerCb);
					host.Add(flickerSpd);
					host.Add(flickerRate);
					host.Add(flickerAmount);

					void UpdateGlowVisibility() {
						GlowStyle gs = (GlowStyle)glowStyle.enumValueIndex;
						bool isFade = gs == GlowStyle.Fade;
						bool isShimmer = gs == GlowStyle.Shimmer;
						bool isFlicker = gs == GlowStyle.NeonFlicker;

						fadeCa.style.display = isFade ? DisplayStyle.Flex : DisplayStyle.None;
						fadeCb.style.display = isFade ? DisplayStyle.Flex : DisplayStyle.None;
						fadeSharp.style.display = isFade ? DisplayStyle.Flex : DisplayStyle.None;
						fadeSpd.style.display = isFade ? DisplayStyle.Flex : DisplayStyle.None;
						fadeInv.style.display = isFade ? DisplayStyle.Flex : DisplayStyle.None;

						shimmerRamp.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerWidth.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerCount.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerAngle.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerSpd.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerInv.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerOnce.style.display = isShimmer ? DisplayStyle.Flex : DisplayStyle.None;
						shimmerProg.style.display = (isShimmer && once.boolValue) ? DisplayStyle.Flex : DisplayStyle.None;

						flickerCa.style.display = isFlicker ? DisplayStyle.Flex : DisplayStyle.None;
						flickerCb.style.display = isFlicker ? DisplayStyle.Flex : DisplayStyle.None;
						flickerSpd.style.display = isFlicker ? DisplayStyle.Flex : DisplayStyle.None;
						flickerRate.style.display = isFlicker ? DisplayStyle.Flex : DisplayStyle.None;
						flickerAmount.style.display = isFlicker ? DisplayStyle.Flex : DisplayStyle.None;
					}
					UpdateGlowVisibility();
					host.TrackPropertyValue(glowStyle, _ => UpdateGlowVisibility());
					host.TrackPropertyValue(once, _ => UpdateGlowVisibility());
					break;
				}
				case BuiltinEffect.Glitch: {
					SerializedProperty glitchStyle = el.FindPropertyRelative("GlitchStyle");
					host.Add(effectCol.Property(glitchStyle, "Glitch Style"));

					// Glitch rows
					var glitchAmp = effectCol.DragNumber(amp, "Jitter Distance", min: 0f);
					var glitchFreq = effectCol.Property(freq, "Glitch Rate");
					var glitchAmount = PercentSliderRow(amount, "Glitch Amount");
					var glitchSpd = effectCol.DragNumber(spd, "Color Cycle", min: 0f);
					var glitchRamp = RampRow(ramp);

					// Matrix rows
					var matrixAmount = PercentSliderRow(amount, "Active Characters");
					var matrixSpd = effectCol.DragNumber(spd, "Scramble Speed", min: 0f);
					var matrixJitter = effectCol.DragNumber(amp, "Jitter Distance", min: 0f);
					var matrixRamp = RampRow(ramp);
					var matrixChars = TextRow(el.FindPropertyRelative("ScrambleCharacters"), "Custom Chars");
					var matrixInv = effectCol.Property(inv, "Inverse");
					var matrixOnce = effectCol.Property(once, "Once");
					var matrixProg = PercentSliderRow(progress, "Progress");

					host.Add(glitchAmp);
					host.Add(glitchFreq);
					host.Add(glitchAmount);
					host.Add(glitchSpd);
					host.Add(glitchRamp);

					host.Add(matrixAmount);
					host.Add(matrixSpd);
					host.Add(matrixJitter);
					host.Add(matrixRamp);
					host.Add(matrixChars);
					host.Add(matrixInv);
					host.Add(matrixOnce);
					host.Add(matrixProg);

					void UpdateGlitchVisibility() {
						GlitchStyle gs = (GlitchStyle)glitchStyle.enumValueIndex;
						bool isMatrix = gs == GlitchStyle.Matrix;

						glitchAmp.style.display = !isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						glitchFreq.style.display = !isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						glitchAmount.style.display = !isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						glitchSpd.style.display = !isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						glitchRamp.style.display = !isMatrix ? DisplayStyle.Flex : DisplayStyle.None;

						matrixAmount.style.display = (isMatrix && !once.boolValue) ? DisplayStyle.Flex : DisplayStyle.None;
						matrixSpd.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixJitter.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixRamp.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixChars.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixInv.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixOnce.style.display = isMatrix ? DisplayStyle.Flex : DisplayStyle.None;
						matrixProg.style.display = (isMatrix && once.boolValue) ? DisplayStyle.Flex : DisplayStyle.None;
					}
					UpdateGlitchVisibility();
					host.TrackPropertyValue(glitchStyle, _ => UpdateGlitchVisibility());
					host.TrackPropertyValue(once, _ => UpdateGlitchVisibility());
					break;
				}
				default:
					host.Add(new Label("This entry does nothing (effect = None).") {
						style = { fontSize = 10, color = SperlichEditorTheme.TextMuted, unityFontStyleAndWeight = FontStyle.Italic }
					});
					break;
			}
		}

		private void ShowWhen(VisualElement box, string boolProp) {
			SerializedProperty p = serializedObject.FindProperty(boolProp);
			if (p == null) return;
			void Apply() => box.style.display = p.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
			Apply();
			box.TrackPropertyValue(p, _ => Apply());
		}

		// ============================ icon-button enum ========================================

		/// <summary>Word-processor style icon toggle row for a small enum property drawn with Painter2D.</summary>
		private static VisualElement AlignButtons(SerializedProperty enumProp, bool isVertical, string[] tips) {
			var bar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
			int n = tips.Length;
			var btns = new VisualElement[n];

			void Refresh() {
				for (int i = 0; i < n; i++) {
					bool on = enumProp.enumValueIndex == i;
					btns[i].style.backgroundColor = on ? new Color(Accent.r, Accent.g, Accent.b, 0.16f) : SperlichEditorTheme.ButtonBg;
					SperlichEditorWidgets.SetBorderColor(btns[i], on ? Accent : SperlichEditorTheme.ButtonBorder);
					btns[i].MarkDirtyRepaint();
				}
			}

			for (int i = 0; i < n; i++) {
				int idx = i;
				var b = new VisualElement { pickingMode = PickingMode.Position, tooltip = tips[i] };
				b.style.width = 26;
				b.style.height = 20;
				b.style.marginRight = i < n - 1 ? 3 : 0;
				b.style.borderTopWidth = 1;
				b.style.borderBottomWidth = 1;
				b.style.borderLeftWidth = 1;
				b.style.borderRightWidth = 1;
				b.style.justifyContent = Justify.Center;
				b.style.alignItems = Align.Center;
				SperlichEditorWidgets.SetRadius(b, 3);
				SperlichEditorWidgets.SetHoverCursor(b, MouseCursor.Link);

				b.generateVisualContent += mgc => {
					Painter2D p = mgc.painter2D;
					bool on = enumProp.enumValueIndex == idx;
					Color stroke = on ? Accent : SperlichEditorTheme.TextSecondary;
					p.strokeColor = stroke;
					p.lineWidth = 1.3f;
					p.lineCap = LineCap.Round;

					void Line(float x1, float y1, float x2, float y2) {
						p.BeginPath();
						p.MoveTo(new Vector2(x1, y1));
						p.LineTo(new Vector2(x2, y2));
						p.Stroke();
					}

					if (!isVertical) {
						switch (idx) {
							case 0: // Left
								Line(7f, 6f, 19f, 6f);
								Line(7f, 10f, 14f, 10f);
								Line(7f, 14f, 17f, 14f);
								break;
							case 1: // Center
								Line(7f, 6f, 19f, 6f);
								Line(10f, 10f, 16f, 10f);
								Line(8.5f, 14f, 17.5f, 14f);
								break;
							case 2: // Right
								Line(7f, 6f, 19f, 6f);
								Line(12f, 10f, 19f, 10f);
								Line(9f, 14f, 19f, 14f);
								break;
							case 3: // Justified
								Line(7f, 6f, 19f, 6f);
								Line(7f, 10f, 19f, 10f);
								Line(7f, 14f, 13f, 14f);
								break;
							case 4: // Flush
								Line(7f, 6f, 19f, 6f);
								Line(7f, 10f, 19f, 10f);
								Line(7f, 14f, 19f, 14f);
								break;
							case 5: // Geometry Center
								Line(8f, 6f, 18f, 6f);
								Line(10f, 10f, 16f, 10f);
								Line(8f, 14f, 18f, 14f);
								Line(13f, 3.5f, 13f, 5f);
								Line(13f, 15f, 13f, 16.5f);
								break;
						}
					} else {
						switch (idx) {
							case 0: // Top
								p.lineWidth = 1.6f;
								Line(7f, 5.5f, 19f, 5.5f);
								p.lineWidth = 1.3f;
								Line(10f, 8f, 10f, 15f);
								Line(16f, 8f, 16f, 12f);
								break;
							case 1: // Middle
								Line(8f, 6f, 18f, 6f);
								Line(7f, 10f, 19f, 10f);
								Line(8f, 14f, 18f, 14f);
								break;
							case 2: // Bottom
								p.lineWidth = 1.6f;
								Line(7f, 14.5f, 19f, 14.5f);
								p.lineWidth = 1.3f;
								Line(10f, 5f, 10f, 12f);
								Line(16f, 8f, 16f, 12f);
								break;
							case 3: // Baseline
								p.lineWidth = 1.0f;
								Line(6f, 14.5f, 20f, 14.5f);
								p.lineWidth = 1.3f;
								Line(9f, 13f, 13f, 6f);
								Line(13f, 6f, 17f, 13f);
								Line(11f, 10.5f, 15f, 10.5f);
								break;
						}
					}
				};

				b.RegisterCallback<ClickEvent>(_ => {
					enumProp.enumValueIndex = idx;
					enumProp.serializedObject.ApplyModifiedProperties();
					Refresh();
				});
				btns[i] = b;
				bar.Add(b);
			}
			bar.TrackPropertyValue(enumProp, _ => Refresh());
			Refresh();
			return bar;
		}

		// ============================ markup editor ===========================================

		private VisualElement BuildTextArea() {
			// one bordered widget: [ scrolling text ] on top, [ tag toolbar + resize grip ] footer below
			var box = new VisualElement { style = { marginBottom = 4, overflow = Overflow.Hidden } };
			SperlichEditorWidgets.SetRadius(box, 3);
			SperlichEditorWidgets.SetBorderColor(box, SperlichEditorTheme.BorderSubtle);
			box.style.borderTopWidth = 1;
			box.style.borderBottomWidth = 1;
			box.style.borderLeftWidth = 1;
			box.style.borderRightWidth = 1;

			var scroll = new ScrollView(ScrollViewMode.Vertical) {
				verticalScrollerVisibility = ScrollerVisibility.Auto,
				horizontalScrollerVisibility = ScrollerVisibility.Hidden,
			};
			scroll.style.minHeight = 38;
			scroll.style.maxHeight = 220;
			// wheel over the editor scrolls the editor, never the inspector behind it
			scroll.RegisterCallback<WheelEvent>(e => e.StopPropagation());

			textField = new TextField { multiline = true };
			textField.BindProperty(serializedObject.FindProperty("m_text"));
			textField.style.whiteSpace = WhiteSpace.Normal;
			textField.style.marginTop = 0;
			textField.style.marginBottom = 0;
			textField.style.flexShrink = 0;
			textField.style.width = Length.Percent(100);
			VisualElement input = textField.Q("unity-text-input");
			if (input != null) {
				input.style.whiteSpace = WhiteSpace.Normal;
				input.style.minHeight = 20;
				input.style.unityTextAlign = TextAnchor.UpperLeft;
			}
			scroll.Add(textField);
			box.Add(scroll);

			// footer: tag buttons on the empty line, resize grip pinned to its right edge
			var footer = new VisualElement {
				style = {
					position = Position.Relative,
					flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center,
					backgroundColor = SperlichEditorTheme.BgStep,
					borderTopWidth = 1, paddingLeft = 4, paddingTop = 3, paddingBottom = 3, paddingRight = 22,
				}
			};
			SperlichEditorWidgets.SetBorderColor(footer, SperlichEditorTheme.BorderSubtle);

			void Tag(string cap, string open, string close) {
				var b = SperlichEditorWidgets.MakeButton(cap, 0, () => InsertTag(open, close));
				b.style.height = 17;
				b.style.marginRight = 3;
				b.style.marginBottom = 2;
				b.style.marginTop = 2;
				b.style.paddingLeft = 6;
				b.style.paddingRight = 6;
				b.style.fontSize = 10;
				footer.Add(b);
			}
			Tag("B", "<b>", "</b>");
			Tag("I", "<i>", "</i>");
			Tag("U", "<u>", "</u>");
			Tag("S", "<s>", "</s>");
			Tag("Color", "<color=#ffcc00>", "</color>");
			Tag("Grad", "<gradient=#ffffff,#3388ff>", "</gradient>");
			Tag("Size", "<size=150%>", "</size>");
			Tag("Mark", "<mark=#ffff0059>", "</mark>");
			Tag("Glow", "<glow=#ffcc66,1.0,1.6>", "</glow>");
			Tag("Bloom", "<bloom=#ff8a1e,1.0,2.4>", "</bloom>");
			Tag("Link", "<link=\"id\">", "</link>");
			Tag("Glyph", "<glyph:Jump>", "");

			var grip = new Label("◢") {
				tooltip = "Drag to resize",
				pickingMode = PickingMode.Position,
				style = {
					position = Position.Absolute, right = 3, bottom = 3,
					width = 15, height = 15, fontSize = 11,
					color = SperlichEditorTheme.TextMuted,
					backgroundColor = SperlichEditorTheme.ButtonBg,
					unityTextAlign = TextAnchor.LowerRight,
				}
			};
			SperlichEditorWidgets.SetRadius(grip, 3);
			SperlichEditorWidgets.SetBorderColor(grip, SperlichEditorTheme.ButtonBorder);
			grip.style.borderTopWidth = 1;
			grip.style.borderBottomWidth = 1;
			grip.style.borderLeftWidth = 1;
			grip.style.borderRightWidth = 1;
			SperlichEditorWidgets.SetHoverCursor(grip, MouseCursor.ResizeUpLeft);

			bool dragging = false;
			float startY = 0f, startH = 0f;
			grip.RegisterCallback<PointerDownEvent>(e => {
				dragging = true;
				startY = e.position.y;
				startH = scroll.resolvedStyle.height > 0 ? scroll.resolvedStyle.height : (editorHeight > 0 ? editorHeight : 38f);
				scroll.style.maxHeight = StyleKeyword.None;
				grip.CapturePointer(e.pointerId);
				e.StopPropagation();
			});
			grip.RegisterCallback<PointerMoveEvent>(e => {
				if (!dragging) return;
				editorHeight = Mathf.Clamp(startH + (e.position.y - startY), 38f, 500f);
				scroll.style.height = editorHeight;
				e.StopPropagation();
			});
			grip.RegisterCallback<PointerUpEvent>(e => {
				if (!dragging) return;
				dragging = false;
				grip.ReleasePointer(e.pointerId);
			});
			footer.Add(grip);

			box.Add(footer);
			return box;
		}

		/// <summary>Appends a tag by editing the <see cref="TextField"/> value in place (the binding then
		/// persists it) — a SerializedProperty round-trip is what made the field jump to the top.</summary>
		private void InsertTag(string open, string close) {
			if (textField == null) return;
			string s = textField.value ?? string.Empty;
			textField.value = s + (string.IsNullOrEmpty(close) ? open : open + "text" + close);
			textField.schedule.Execute(() => textField.Focus());
		}
	}
}
