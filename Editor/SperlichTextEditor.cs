using Sperlich.EditorKit;
using Sperlich.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Inspector for <see cref="SperlichText"/>, built on UI Toolkit with the shared Sperlich EditorKit:
	/// collapsible sections, pill toggles, the custom enum dropdown, Word-style icon buttons for the two
	/// alignments, and a resizable / scrolling markup editor with a tag-insert toolbar.
	/// </summary>
	[CustomEditor(typeof(SperlichText))]
	public sealed class SperlichTextEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;

		private TextField textField;
		private float editorHeight = 110f;

		/// <summary>Eine gemeinsame Label-Spalte für den ganzen Inspektor — jede Zeile (PropertyField, Enum,
		/// PillToggle, Icon-Buttons) beginnt das Control an exakt derselben X-Position.</summary>
		private readonly SperlichFieldColumn col = new(142f);

		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement { style = { paddingTop = 2, paddingBottom = 4 } };

			// ---- Content -------------------------------------------------------------------------
			var content = Section(root, "CONTENT", true);
			content.Add(col.Row("Font Style", SperlichEditorWidgets.CreateFlagButtons(
				serializedObject.FindProperty("m_fontStyle"),
				new[] { "B", "I", "U", "S", "AB", "ab", "Ab" },
				new[] { "Bold", "Italic", "Underline", "Strikethrough", "UPPERCASE", "lowercase", "SmallCaps" },
				Accent,
				new[] { new[] { 4, 5, 6 } },      // Uppercase / Lowercase / SmallCaps are mutually exclusive
				new[] { 4 })));                   // divider between the text-style and the case buttons
			content.Add(BuildTextArea());
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

			// ---- uGUI -----------------------------------------------------------------------
			var ugui = Section(root, "uGUI", false);
			ugui.Add(BoolRow("m_RaycastTarget", "Raycast Target"));
			ugui.Add(BoolRow("m_Maskable", "Maskable"));

			ShowWhen(autoBox, "m_autoSize");
			ShowWhen(revealBox, "m_typewriter");

			root.TrackSerializedObjectValue(serializedObject, _ => {
				foreach (UnityEngine.Object t in targets)
					if (t is SperlichText st) st.SetAllDirty();
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
					break;
				case BuiltinEffect.Shake:
					amp.floatValue = 2.5f;
					freq.floatValue = 30f;
					spd.floatValue = 1f;
					break;
				case BuiltinEffect.Pulse:
					amp.floatValue = 0.12f;
					freq.floatValue = 2f;
					spd.floatValue = 4f;
					break;
				case BuiltinEffect.Rainbow:
					amp.floatValue = 1f;
					freq.floatValue = 0.04f;
					spd.floatValue = 1.5f;
					ramp.gradientValue = BuiltinEffectParams.CreateRainbowGradient();
					break;
				case BuiltinEffect.Glow:
					amp.floatValue = 0.4f;
					freq.floatValue = 3f;
					spd.floatValue = 1.5f;
					ca.colorValue = new Color(0.55f, 0.55f, 0.55f, 1f);
					cb.colorValue = Color.white;
					break;
				case BuiltinEffect.Glitch:
					amp.floatValue = 3f;
					freq.floatValue = 12f;
					spd.floatValue = 1f;
					el.FindPropertyRelative("Amount").floatValue = 0.25f;
					if (IsDefaultOrEmptyGradient(ramp.gradientValue)) {
						ramp.gradientValue = BuiltinEffectParams.CreateRainbowGradient();
					}
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

		/// <summary>"Color Ramp"-Zeile: ein direkt gebundenes <see cref="GradientField"/> (das PropertyField
		/// für einen Gradient in einem verschachtelten Array-Element rendert oft leer). Leerer/1-Key-Gradient
		/// oder unveränderter Standard-Gradient wird einmalig mit einem Regenbogen vorbelegt.</summary>
		private VisualElement RampRow(SerializedProperty ramp) {
			Gradient cur = ramp.gradientValue;
			if (IsDefaultOrEmptyGradient(cur)) {
				ramp.gradientValue = BuiltinEffectParams.CreateRainbowGradient();
				serializedObject.ApplyModifiedProperties();
			}
			var gf = new UnityEditor.UIElements.GradientField { style = { flexGrow = 1 } };
			gf.BindProperty(ramp);
			SperlichFieldColumn.HideInternalLabel(gf);
			return effectCol.Row("Color Ramp", gf);
		}

		/// <summary>Prüft, ob ein Gradient leer, null oder der 2-farbige Standard-Weiß-Gradient von Unity ist.</summary>
		private static bool IsDefaultOrEmptyGradient(Gradient cur) {
			if (cur == null || cur.colorKeys == null || cur.colorKeys.Length < 2) return true;
			if (cur.colorKeys.Length == 2 &&
			    cur.colorKeys[0].color == Color.white &&
			    cur.colorKeys[1].color == Color.white) {
				return true;
			}
			return false;
		}

		/// <summary>"Color"-Zeile: ein direkt gebundenes <see cref="ColorField"/> für verschachtelte Array-Elemente.</summary>
		private VisualElement ColorRow(SerializedProperty colorProp, string label) {
			var cf = new UnityEditor.UIElements.ColorField { style = { flexGrow = 1 }, showAlpha = true };
			cf.BindProperty(colorProp);
			SperlichFieldColumn.HideInternalLabel(cf);
			return effectCol.Row(label, cf);
		}

		/// <summary>"Percent Slider"-Zeile: Slider von 0..1 mit Prozentanzeige (0..100%).</summary>
		private VisualElement PercentSliderRow(SerializedProperty prop, string label) {
			var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
			var slider = new Slider(0f, 1f) { style = { flexGrow = 1 } };
			slider.BindProperty(prop);
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

			void UpdateText() => percentLabel.text = $"{Mathf.RoundToInt(Mathf.Clamp01(prop.floatValue) * 100f)} %";
			UpdateText();
			row.TrackPropertyValue(prop, _ => UpdateText());

			row.Add(slider);
			row.Add(percentLabel);
			return effectCol.Row(label, row);
		}

		private void PopulateEffectParams(VisualElement host, SerializedProperty el, BuiltinEffect effect) {
			SerializedProperty amp = el.FindPropertyRelative("Amplitude");
			SerializedProperty freq = el.FindPropertyRelative("Frequency");
			SerializedProperty spd = el.FindPropertyRelative("Speed");
			SerializedProperty ca = el.FindPropertyRelative("ColorA");
			SerializedProperty cb = el.FindPropertyRelative("ColorB");
			SerializedProperty ramp = el.FindPropertyRelative("Ramp");

			switch (effect) {
				case BuiltinEffect.Wave:
					host.Add(effectCol.Property(amp, "Height"));
					host.Add(effectCol.Property(freq, "Wavelength"));
					host.Add(effectCol.Property(spd, "Speed"));
					break;
				case BuiltinEffect.Shake:
					host.Add(effectCol.Property(amp, "Distance"));
					host.Add(effectCol.Property(freq, "Shake Rate"));
					break;
				case BuiltinEffect.Pulse:
					host.Add(effectCol.Property(amp, "Scale Amount"));
					host.Add(effectCol.Property(freq, "Phase Offset"));
					host.Add(effectCol.Property(spd, "Speed"));
					break;
				case BuiltinEffect.Rainbow:
					host.Add(effectCol.Property(freq, "Spread"));
					host.Add(effectCol.Property(spd, "Speed"));
					host.Add(RampRow(ramp));
					break;
				case BuiltinEffect.Glow:
					host.Add(ColorRow(ca, "Color A"));
					host.Add(ColorRow(cb, "Color B"));
					host.Add(effectCol.Property(freq, "Fade Sharpness"));
					host.Add(effectCol.Property(spd, "Fade Speed"));
					break;
				case BuiltinEffect.Glitch:
					host.Add(effectCol.Property(amp, "Shake Distance"));
					host.Add(effectCol.Property(freq, "Glitch Rate"));
					host.Add(PercentSliderRow(el.FindPropertyRelative("Amount"), "Glitch Amount"));
					host.Add(effectCol.Property(spd, "Color Cycle"));
					host.Add(RampRow(ramp));
					break;
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
				verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible,
				horizontalScrollerVisibility = ScrollerVisibility.Hidden,
			};
			scroll.style.height = editorHeight;
			scroll.style.minHeight = 44;
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
				input.style.minHeight = 40;
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
				dragging = true; startY = e.position.y; startH = editorHeight;
				grip.CapturePointer(e.pointerId);
				e.StopPropagation();
			});
			grip.RegisterCallback<PointerMoveEvent>(e => {
				if (!dragging) return;
				editorHeight = Mathf.Clamp(startH + (e.position.y - startY), 44f, 500f);
				scroll.style.height = editorHeight;
				e.StopPropagation();
			});
			grip.RegisterCallback<PointerUpEvent>(e => {
				if (!dragging) return;
				dragging = false; grip.ReleasePointer(e.pointerId);
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
