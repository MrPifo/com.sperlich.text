using Sperlich.EditorKit;
using Sperlich.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Inspector for <see cref="STextSettings"/>, built on UI Toolkit with the shared Sperlich EditorKit --
	/// same conventions as <c>SpriteGlyphAssetEditor</c>/<c>GlyphActionRegistryEditor</c>.
	/// </summary>
	[CustomEditor(typeof(STextSettings))]
	public sealed class STextSettingsEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;
		private readonly SperlichFieldColumn col = new(140f);

		public override VisualElement CreateInspectorGUI() {
			var settings = (STextSettings)target;
			var root = new VisualElement { style = { paddingTop = 2, paddingBottom = 4, marginLeft = -15, marginRight = -4 } };

			var errorHost = new VisualElement();
			root.Add(errorHost);
			RefreshMultipleInstancesError(errorHost, settings);

			var defaultsSection = Section(root, "DEFAULTS", true);
			defaultsSection.Add(col.Row("Font", SperlichEditorWidgets.CreateAssetDropdown<FontDefinition>(serializedObject.FindProperty("defaultFont"), Accent)));
			defaultsSection.Add(col.Property(serializedObject.FindProperty("defaultFontSize"), "Font Size"));
			defaultsSection.Add(col.Property(serializedObject.FindProperty("defaultColor"), "Color"));

			var pipelineSection = Section(root, "GLYPH PIPELINE", true,
				"Max glyphs rasterised per frame (amortised generation), and whether to queue the printable " +
				"ASCII + Latin-1 range on startup so the first display has no pop-in.");
			pipelineSection.Add(col.Property(serializedObject.FindProperty("glyphsPerFrame"), "Glyphs / Frame"));
			pipelineSection.Add(col.Property(serializedObject.FindProperty("prewarmLatin1"), "Prewarm Latin-1"));

			var shaderSection = Section(root, "SHADER", true,
				"Optional override for the runtime material's shader. Leave empty -- Shader.Find(\"Sperlich/Text SDF\") " +
				"is used automatically and ships with the package, so this is only needed for a custom shader variant.");
			shaderSection.Add(col.Row("SDF Shader", MakeShaderField(serializedObject.FindProperty("sdfShader"))));

			var spritesSection = Section(root, "SPRITES", true,
				"Optional pin for the project's default SpriteGlyphAsset. Leave empty and SText auto-resolves the " +
				"one SpriteGlyphAsset with Role = Main in the project -- set this only to override that auto-discovery.");
			spritesSection.Add(col.Row("Sprite Asset", GlyphEditorPickers.MakeSpriteAssetDropdown(serializedObject.FindProperty("defaultSpriteAsset"), Accent)));

			var devicesSection = Section(root, "GLYPH DEVICES", true,
				"Fallback for GlyphDeviceContext.ActiveDeviceId used only in the Editor, only while nothing has " +
				"called SetActiveDevice yet (e.g. testing in Play Mode before your own input-detection code runs). " +
				"Never used in a build.");
			devicesSection.Add(col.Row("Editor Default Device", MakeDeviceIdDropdown(serializedObject.FindProperty("editorDefaultDeviceId"))));

			return root;
		}

		private static VisualElement MakeShaderField(SerializedProperty prop) {
			var field = new ObjectField { objectType = typeof(Shader), style = { flexGrow = 1 } };
			field.BindProperty(prop);
			SperlichFieldColumn.HideInternalLabel(field);
			return field;
		}

		/// <summary>Device picker for <c>editorDefaultDeviceId</c>, sourced from the project's
		/// <see cref="GlyphActionRegistry"/> devices instead of free text -- keeps the value in sync with
		/// whatever device IDs are actually registered (a typo here would otherwise silently never match).</summary>
		private static VisualElement MakeDeviceIdDropdown(SerializedProperty prop) {
			var devices = new List<GlyphActionRegistry.DeviceDefinition>();

			void Rescan() {
				devices.Clear();
				devices.Add(new GlyphActionRegistry.DeviceDefinition { Title = "None", DeviceId = "" });
				GlyphActionRegistry registry = GlyphActionRegistry.GetDefault();
				if (registry == null) {
					string[] guids = AssetDatabase.FindAssets("t:" + nameof(GlyphActionRegistry));
					if (guids.Length > 0) {
						string path = AssetDatabase.GUIDToAssetPath(guids[0]);
						registry = AssetDatabase.LoadAssetAtPath<GlyphActionRegistry>(path);
					}
				}
				if (registry != null) {
					foreach (GlyphActionRegistry.DeviceDefinition d in registry.Devices) {
						if (!string.IsNullOrEmpty(d.DeviceId)) devices.Add(d);
					}
				}
			}
			Rescan();

			int Count() => devices.Count;

			string LabelFor(int i) {
				if (i < 0 || i >= devices.Count) return "—";
				GlyphActionRegistry.DeviceDefinition d = devices[i];
				return string.IsNullOrEmpty(d.DeviceId) ? d.Title : $"{(string.IsNullOrEmpty(d.Title) ? d.DeviceId : d.Title)} ({d.DeviceId})";
			}

			int Selected() {
				string current = prop.stringValue ?? "";
				for (int i = 0; i < devices.Count; i++) {
					if (string.Equals(devices[i].DeviceId, current, System.StringComparison.OrdinalIgnoreCase)) return i;
				}
				return 0;
			}

			void Pick(int i) {
				if (i >= 0 && i < devices.Count) {
					prop.stringValue = devices[i].DeviceId;
					prop.serializedObject.ApplyModifiedProperties();
					GlyphDeviceContext.RefreshEditorDefault();
				}
			}

			VisualElement field = SperlichEditorWidgets.BuildDropdown(Count, LabelFor, Selected, Pick, Accent);
			field.RegisterCallback<PointerDownEvent>(_ => Rescan(), TrickleDown.TrickleDown);
			SperlichFieldColumn.HideInternalLabel(field);
			return field;
		}

		private static void RefreshMultipleInstancesError(VisualElement host, STextSettings self) {
			host.Clear();
			STextSettings[] all = Resources.LoadAll<STextSettings>("");
			if (all.Length <= 1) return;
			var list = new System.Collections.Generic.List<string>();
			foreach (STextSettings s in all) list.Add(s == self ? $"{s.name} (this one)" : s.name);
			host.Add(SperlichEditorWidgets.CreateMessageBox(
				$"Multiple STextSettings assets found under a Resources folder ({all.Length}): {string.Join(", ", list)}. " +
				"Keep exactly one in your project -- which one gets used is otherwise undefined.",
				SperlichEditorWidgets.MessageKind.Error));
		}

		private static VisualElement Section(VisualElement parent, string title, bool expanded, string tooltip = null) {
			var (header, sectionBody, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(STextSettingsEditor));
			if (!string.IsNullOrEmpty(tooltip)) header.Add(SperlichEditorWidgets.CreateInfoIcon(tooltip));
			sectionBody.style.paddingLeft = 6;
			sectionBody.style.paddingRight = 6;
			sectionBody.style.paddingTop = 4;
			sectionBody.style.paddingBottom = 6;
			var wrap = new VisualElement { style = { marginBottom = 4 } };
			wrap.Add(header);
			wrap.Add(sectionBody);
			parent.Add(wrap);
			return sectionBody;
		}
	}
}
