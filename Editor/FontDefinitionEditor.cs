using System;
using Sperlich.EditorKit;
using Sperlich.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Inspector for <see cref="FontDefinition"/>, rebuilt on UI Toolkit with the shared Sperlich EditorKit.
	/// The <c>fieldKind</c> is a segmented control; the MTSDF bake parameters + a bake button + a status
	/// card appear only for <see cref="GlyphFieldKind.MTSDF"/>. Diagnostic bake toggles live in a collapsed
	/// "Advanced" section.
	/// </summary>
	[CustomEditor(typeof(FontDefinition))]
	public sealed class FontDefinitionEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;

		private VisualElement mtsdfWrap;
		private VisualElement statusCard;
		private Label statusText;
		private Label statusBadge;
		private Button bakeButton;

		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement { style = { paddingTop = 2, paddingBottom = 4 } };

			// ---- Faces -----------------------------------------------------------------------------
			var faces = Section(root, "FACES", true);
			faces.Add(new PropertyField(serializedObject.FindProperty("primary"), "Primary"));
			faces.Add(new PropertyField(serializedObject.FindProperty("fallbacks"), "Fallbacks"));

			// ---- SDF rasterizer / atlas ----------------------------------------------------------
			var raster = Section(root, "SDF RASTERIZER", true);
			raster.Add(new PropertyField(serializedObject.FindProperty("samplingPointSize"), "Sampling Point Size"));
			raster.Add(new PropertyField(serializedObject.FindProperty("sdfPadding"), "SDF Padding"));
			raster.Add(new PropertyField(serializedObject.FindProperty("atlasResolution"), "Atlas Resolution"));

			// ---- Field kind --------------------------------------------------------------------------
			var kindSec = Section(root, "FIELD KIND", true);
			SerializedProperty kindProp = serializedObject.FindProperty("fieldKind");
			kindSec.Add(SperlichEditorWidgets.CreateSegmentedControl(
				kindProp, Enum.GetNames(typeof(GlyphFieldKind)), Accent, RefreshMtsdf));
			kindSec.Add(Hint("SDF = TMP dynamic atlas, any code point at runtime. MTSDF = pre-baked atlas, " +
				"sharper corners + outline/glow, fixed charset."));

			// ---- MTSDF bake ----------------------------------------------------------------------
			mtsdfWrap = new VisualElement();
			root.Add(mtsdfWrap);
			var bake = Section(mtsdfWrap, "MTSDF BAKE", true);
			foreach ((string prop, string label) in new[] {
				("msdfCharset", "Charset"),
				("msdfExtraChars", "Extra Characters"),
				("msdfEmSize", "EM Size"),
				("msdfPixelRange", "Pixel Range"),
				("msdfGlowPadding", "Glow Padding"),
				("msdfMaxAtlas", "Max Atlas"),
			}) {
				SerializedProperty sp = serializedObject.FindProperty(prop);
				if (sp != null) bake.Add(new PropertyField(sp, label));
			}

			statusCard = SperlichEditorWidgets.CreateBox(3, SperlichEditorTheme.BorderSubtle);
			statusCard.style.flexDirection = FlexDirection.Row;
			statusCard.style.alignItems = Align.FlexStart;
			statusCard.style.paddingTop = 6;
			statusCard.style.paddingBottom = 6;
			statusCard.style.paddingLeft = 8;
			statusCard.style.paddingRight = 8;
			statusCard.style.marginTop = 6;
			statusCard.style.marginBottom = 6;
			statusCard.style.backgroundColor = SperlichEditorTheme.BgDark;
			statusBadge = SperlichEditorWidgets.CreateBadge("—", SperlichEditorTheme.BadgeNeutralBg);
			statusBadge.style.marginRight = 8;
			statusBadge.style.flexShrink = 0;
			statusText = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10, color = SperlichEditorTheme.TextSecondary, flexGrow = 1 } };
			statusCard.Add(statusBadge);
			statusCard.Add(statusText);
			bake.Add(statusCard);

			bakeButton = SperlichEditorWidgets.MakeButton("Bake MTSDF Atlas", 0, () => {
				MsdfBaker.BakeAsset((FontDefinition) target);
				RefreshStatus();
			}, isAccent: true);
			bakeButton.style.height = 26;
			bakeButton.style.marginBottom = 2;
			bake.Add(bakeButton);

			// ---- Advanced (diagnostic bake toggles) --------------------------------------------
			var (advHeader, advBody, _) = SperlichEditorWidgets.CreateChevronSection(
				"ADVANCED / DIAGNOSTIC", false, SperlichEditorTheme.BgStepBody, null, nameof(FontDefinitionEditor));
			advBody.style.paddingLeft = 8;
			advBody.style.paddingRight = 8;
			advBody.style.paddingTop = 4;
			advBody.style.paddingBottom = 6;
			advBody.Add(Hint("Defaults are correct for almost every font. Only touch these to diagnose a " +
				"specific baked-glyph artefact."));
			foreach ((string prop, string label) in new[] {
				("msdfEdgeAngle", "Edge Angle (rad)"),
				("msdfResolveOverlaps", "Resolve Overlaps"),
				("msdfErrorCorrection", "Error Correction"),
				("msdfAggressiveErrorCorrection", "Aggressive Error Correction"),
				("msdfSignCorrection", "Sign Correction"),
			}) {
				SerializedProperty sp = serializedObject.FindProperty(prop);
				if (sp == null) continue;
				advBody.Add(sp.propertyType == SerializedPropertyType.Boolean
					? BoolRow(sp, label)
					: new PropertyField(sp, label));
			}
			var advWrap = new VisualElement { style = { marginTop = 2 } };
			advWrap.Add(advHeader);
			advWrap.Add(advBody);
			bake.Add(advWrap);

			// keep the MTSDF block + status in sync
			RefreshMtsdf();
			root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
			root.schedule.Execute(RefreshStatus);

			return root;
		}

		// ============================ building blocks =========================================

		private static VisualElement Section(VisualElement parent, string title, bool expanded) {
			var (header, body, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(FontDefinitionEditor));
			body.style.paddingLeft = 8;
			body.style.paddingRight = 8;
			body.style.paddingTop = 4;
			body.style.paddingBottom = 6;
			var wrap = new VisualElement { style = { marginBottom = 4 } };
			wrap.Add(header);
			wrap.Add(body);
			parent.Add(wrap);
			return body;
		}

		private static Label Hint(string text) {
			var l = new Label(text) {
				style = {
					whiteSpace = WhiteSpace.Normal, fontSize = 9,
					color = SperlichEditorTheme.TextMuted,
					marginTop = 2, marginBottom = 4,
				}
			};
			return l;
		}

		private VisualElement BoolRow(SerializedProperty prop, string label) {
			var pill = new PillToggle(prop.boolValue);
			pill.Clicked += () => {
				prop.boolValue = !prop.boolValue;
				serializedObject.ApplyModifiedProperties();
				pill.SetValue(prop.boolValue);
			};
			VisualElement row = SperlichEditorWidgets.CreateAlignedRow(label, pill);
			row.TrackPropertyValue(prop, p => pill.SetValue(p.boolValue));
			return row;
		}

		// ============================ state refresh ===========================================

		private void RefreshMtsdf() {
			bool isMtsdf = serializedObject.FindProperty("fieldKind").enumValueIndex == (int) GlyphFieldKind.MTSDF;
			if (mtsdfWrap != null) mtsdfWrap.style.display = isMtsdf ? DisplayStyle.Flex : DisplayStyle.None;
			RefreshStatus();
		}

		private void RefreshStatus() {
			if (statusText == null || target == null) return;
			var def = (FontDefinition) target;
			serializedObject.Update();

			bool noFont = def.primary == null;
			if (bakeButton != null) {
				bakeButton.SetEnabled(!noFont);
				bakeButton.text = def.HasBakedData ? "Re-bake MTSDF Atlas" : "Bake MTSDF Atlas";
			}

			if (noFont) {
				SetBadge("NO FONT", SperlichEditorTheme.BadgeWarnBg);
				statusText.text = "Assign a primary Font (.ttf/.otf) before baking.";
				return;
			}

			MsdfFontData data = serializedObject.FindProperty("bakedData").objectReferenceValue as MsdfFontData;
			if (data == null || data.atlas == null) {
				SetBadge("NOT BAKED", SperlichEditorTheme.BadgeNeutralBg);
				statusText.text = "No atlas baked yet. Press 'Bake MTSDF Atlas'.";
				return;
			}

			int glyphs = data.glyphs != null ? data.glyphs.Length : 0;
			string msg = $"{glyphs} glyphs · atlas {data.atlasSize}px · range {data.pixelRange:0.#}px · em {data.emSize:0}px";
			bool stale;
			try { stale = data.sourceHash != MsdfBaker.ComputeHash(def, MsdfBakeParams.From(def)); }
			catch { stale = false; }

			if (stale) {
				SetBadge("OUT OF DATE", SperlichEditorTheme.BadgeWarnBg);
				statusText.text = msg + "\nThe font file or a bake parameter changed — re-bake.";
			} else {
				SetBadge("BAKED", SperlichEditorTheme.ButtonAccent);
				statusText.text = msg;
			}
		}

		private void SetBadge(string text, Color bg) {
			statusBadge.text = text;
			statusBadge.style.backgroundColor = bg;
		}
	}
}
