using System.Collections.Generic;
using Sperlich.EditorKit;
using Sperlich.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Actions and devices are each defined exactly once (top sections); the "Icons" section below is a
	/// per-device grid -- pick a device tab, every action is already listed as one row, just fill in the
	/// icon. Built on UI Toolkit with the shared Sperlich EditorKit (collapsible sections, field column,
	/// theme), same conventions as <c>SperlichTextEditor</c>.
	/// </summary>
	[CustomEditor(typeof(GlyphActionRegistry))]
	public sealed class GlyphActionRegistryEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;
		private readonly SperlichFieldColumn col = new(110f);
		private int selectedDeviceTab;

		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement { style = { paddingTop = 2, paddingBottom = 4, marginLeft = -15, marginRight = -4 } };

			var spriteAssetSection = Section(root, "SPRITE ASSET", true,
				"Define every action and every device once below. Then pick a device tab in \"Icons\" and fill in " +
				"each action's icon for that device. Reference from markup as <glyph:Name> / @Name@.");
			spriteAssetSection.Add(col.Row("Sprite Asset",
				GlyphEditorPickers.MakeSpriteAssetDropdown(serializedObject.FindProperty("ReferenceSpriteAsset"), Accent)));

			BuildActionsSection(root);
			BuildDevicesSection(root);
			BuildIconsSection(root);

			return root;
		}

		// -- Actions ------------------------------------------------------------------------------

		private void BuildActionsSection(VisualElement root) {
			var section = Section(root, "ACTIONS", true);
			SerializedProperty actionsProp = serializedObject.FindProperty("Actions");
			var list = new VisualElement();
			section.Add(list);

			void Rebuild() {
				list.Clear();
				for (int i = 0; i < actionsProp.arraySize; i++) list.Add(BuildActionCard(actionsProp, i, Rebuild));
			}
			Rebuild();

			var addBtn = SperlichEditorWidgets.MakeButton("Add Action", 100, () => {
				actionsProp.InsertArrayElementAtIndex(actionsProp.arraySize);
				serializedObject.ApplyModifiedProperties();
				Rebuild();
			});
			addBtn.style.marginTop = 4;
			section.Add(addBtn);

			int lastCount = actionsProp.arraySize;
			section.TrackSerializedObjectValue(serializedObject, _ => {
				if (actionsProp.arraySize != lastCount) { lastCount = actionsProp.arraySize; Rebuild(); }
			});
		}

		/// <summary>Field height for the Action card's two text fields -- a bit taller than Unity's ~18px
		/// default for readability, without ballooning the whole card the way 32px did.</summary>
		private const float ActionFieldHeight = 22f;

		private static TextField MakeActionField(SerializedProperty prop) {
			var field = new TextField { style = { flexGrow = 1, height = ActionFieldHeight } };
			field.BindProperty(prop);
			SperlichFieldColumn.HideInternalLabel(field);
			return field;
		}

		private VisualElement BuildActionCard(SerializedProperty actionsProp, int index, System.Action rebuild) {
			SerializedProperty el = actionsProp.GetArrayElementAtIndex(index);
			SerializedProperty nameProp = el.FindPropertyRelative("Name");
			SerializedProperty fallbackProp = el.FindPropertyRelative("DefaultFallbackLabel");

			var card = SperlichEditorWidgets.CreateBox(4, SperlichEditorTheme.BorderSubtle);
			card.style.backgroundColor = SperlichEditorTheme.BgStepBody;
			card.style.marginBottom = 1;
			card.style.paddingLeft = 6; card.style.paddingRight = 24; card.style.paddingTop = 2; card.style.paddingBottom = 2;
			card.style.position = Position.Relative;

			VisualElement removeBtn = SperlichEditorWidgets.CreateRemoveButton(() => {
				actionsProp.DeleteArrayElementAtIndex(index);
				actionsProp.serializedObject.ApplyModifiedProperties();
				rebuild();
			}, 16);
			removeBtn.style.position = Position.Absolute;
			removeBtn.style.top = 2;
			removeBtn.style.right = 2;
			card.Add(removeBtn);

			var nameRow = col.Row("Action", MakeActionField(nameProp));
			nameRow.style.marginTop = 0; nameRow.style.marginBottom = 0;
			card.Add(nameRow);

			var fallbackRow = col.Row("Fallback Label", MakeActionField(fallbackProp));
			fallbackRow.style.marginTop = 0; fallbackRow.style.marginBottom = 0;
			card.Add(fallbackRow);

			return card;
		}

		// -- Devices --------------------------------------------------------------------------------

		private void BuildDevicesSection(VisualElement root) {
			var section = Section(root, "DEVICES", true);
			SerializedProperty devicesProp = serializedObject.FindProperty("Devices");
			var list = new VisualElement();
			section.Add(list);

			void Rebuild() {
				list.Clear();
				for (int i = 0; i < devicesProp.arraySize; i++) list.Add(BuildDeviceCard(devicesProp, i, Rebuild));
			}
			Rebuild();

			var addBtn = SperlichEditorWidgets.MakeButton("Add Device", 100, () => {
				devicesProp.InsertArrayElementAtIndex(devicesProp.arraySize);
				serializedObject.ApplyModifiedProperties();
				Rebuild();
			});
			addBtn.style.marginTop = 4;
			section.Add(addBtn);

			int lastCount = devicesProp.arraySize;
			section.TrackSerializedObjectValue(serializedObject, _ => {
				if (devicesProp.arraySize != lastCount) { lastCount = devicesProp.arraySize; Rebuild(); }
			});
		}

		private VisualElement BuildDeviceCard(SerializedProperty devicesProp, int index, System.Action rebuild) {
			SerializedProperty el = devicesProp.GetArrayElementAtIndex(index);
			SerializedProperty titleProp = el.FindPropertyRelative("Title");
			SerializedProperty idProp = el.FindPropertyRelative("DeviceId");
			SerializedProperty iconSourceProp = el.FindPropertyRelative("IconSource");

			var card = SperlichEditorWidgets.CreateBox(4, SperlichEditorTheme.BorderSubtle);
			card.style.backgroundColor = SperlichEditorTheme.BgStepBody;
			card.style.marginBottom = 4;
			card.style.paddingLeft = 6; card.style.paddingRight = 28; card.style.paddingTop = 6; card.style.paddingBottom = 6;
			card.style.position = Position.Relative;

			VisualElement removeBtn = SperlichEditorWidgets.CreateRemoveButton(() => {
				devicesProp.DeleteArrayElementAtIndex(index);
				devicesProp.serializedObject.ApplyModifiedProperties();
				rebuild();
			});
			removeBtn.style.position = Position.Absolute;
			removeBtn.style.top = 6;
			removeBtn.style.right = 6;
			card.Add(removeBtn);

			var titleField = new TextField { style = { flexGrow = 1 } };
			titleField.BindProperty(titleProp);
			SperlichFieldColumn.HideInternalLabel(titleField);
			card.Add(col.Row("Title", titleField));

			var idField = new TextField { style = { flexGrow = 1 } };
			idField.BindProperty(idProp);
			SperlichFieldColumn.HideInternalLabel(idField);
			card.Add(col.Row("Device Id", idField));

			VisualElement iconSourceField = GlyphEditorPickers.MakeSpriteAssetDropdown(iconSourceProp, Accent);
			VisualElement iconSourceRow = col.Row("Icon Source", iconSourceField);
			iconSourceRow.tooltip = "Optional: restricts this device's icon picker to just this asset's icons " +
				"instead of the shared Sprite Asset above. Leave as None to use that one like every other device.";
			card.Add(iconSourceRow);

			return card;
		}

		// -- Icons grid -------------------------------------------------------------------------------

		private void BuildIconsSection(VisualElement root) {
			var section = Section(root, "ICONS", true);
			var body = new VisualElement();
			section.Add(body);

			void Rebuild() => RebuildIconsGrid(body);
			Rebuild();

			SerializedProperty devicesProp = serializedObject.FindProperty("Devices");
			SerializedProperty actionsProp = serializedObject.FindProperty("Actions");
			int lastDevices = devicesProp.arraySize;
			int lastActions = actionsProp.arraySize;
			section.TrackSerializedObjectValue(serializedObject, _ => {
				if (devicesProp.arraySize != lastDevices || actionsProp.arraySize != lastActions) {
					lastDevices = devicesProp.arraySize;
					lastActions = actionsProp.arraySize;
					Rebuild();
				}
			});
		}

		private void RebuildIconsGrid(VisualElement body) {
			body.Clear();
			var reg = (GlyphActionRegistry)target;

			if (reg.Devices.Count == 0) {
				body.Add(new Label("Add a device above first.") { style = { color = SperlichEditorTheme.TextMuted, fontSize = 11, paddingLeft = 4 } });
				return;
			}
			if (reg.Actions.Count == 0) {
				body.Add(new Label("Add an action above first.") { style = { color = SperlichEditorTheme.TextMuted, fontSize = 11, paddingLeft = 4 } });
				return;
			}

			selectedDeviceTab = Mathf.Clamp(selectedDeviceTab, 0, reg.Devices.Count - 1);
			var tabs = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 6 } };
			for (int i = 0; i < reg.Devices.Count; i++) {
				int index = i;
				GlyphActionRegistry.DeviceDefinition d = reg.Devices[i];
				string label = string.IsNullOrEmpty(d.Title) ? (string.IsNullOrEmpty(d.DeviceId) ? $"Device {i}" : d.DeviceId) : d.Title;
				bool active = index == selectedDeviceTab;

				var tab = new VisualElement { pickingMode = PickingMode.Position, style = {
					height = 20, paddingLeft = 8, paddingRight = 8, marginRight = 3,
					borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
					justifyContent = Justify.Center, alignItems = Align.Center,
				} };
				SperlichEditorWidgets.SetRadius(tab, 3);
				SperlichEditorWidgets.SetBorderColor(tab, active ? Accent : SperlichEditorTheme.ButtonBorder);
				tab.style.backgroundColor = active ? new Color(Accent.r, Accent.g, Accent.b, 0.15f) : SperlichEditorTheme.ButtonBg;
				SperlichEditorWidgets.SetHoverCursor(tab, MouseCursor.Link);
				tab.Add(new Label(label) { pickingMode = PickingMode.Ignore, style = {
					fontSize = 11, color = active ? Accent : SperlichEditorTheme.TextSecondary,
					unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal,
				} });
				// Same hover tint + scale "juice" as every other small button in this editor (tag toolbar,
				// font style, align) -- consistency, not just this one tab bar.
				tab.RegisterCallback<MouseEnterEvent>(_ => {
					tab.style.backgroundColor = active
						? new Color(Accent.r, Accent.g, Accent.b, 0.26f)
						: Color.Lerp(SperlichEditorTheme.ButtonBg, Color.white, 0.07f);
				});
				tab.RegisterCallback<MouseLeaveEvent>(_ => {
					tab.style.backgroundColor = active ? new Color(Accent.r, Accent.g, Accent.b, 0.15f) : SperlichEditorTheme.ButtonBg;
				});
				SperlichEditorWidgets.ApplyHoverJuice(tab, "background-color", "border-color");
				tab.RegisterCallback<ClickEvent>(_ => {
					if (selectedDeviceTab == index) return;
					selectedDeviceTab = index;
					RebuildIconsGrid(body);
				});
				tabs.Add(tab);
			}
			body.Add(tabs);

			GlyphActionRegistry.DeviceDefinition device = reg.Devices[selectedDeviceTab];
			string deviceId = device.DeviceId;
			SpriteGlyphAsset iconAsset = device.IconSource != null ? device.IconSource : reg.ReferenceSpriteAsset;

			// Alphabetical, not declaration order -- with more than a handful of actions the Actions section's
			// insertion order stops being a useful scan order once you're just hunting for one action's icon.
			var actionNames = new List<string>();
			foreach (GlyphActionRegistry.ActionDefinition a in reg.Actions) {
				if (!string.IsNullOrEmpty(a.Name)) actionNames.Add(a.Name);
			}
			actionNames.Sort((x, y) => string.Compare(x, y, System.StringComparison.OrdinalIgnoreCase));

			foreach (string action in actionNames) {
				int bindingIdx = reg.Icons.FindIndex(b => b.DeviceId == deviceId && b.Action == action);
				string current = bindingIdx >= 0 ? reg.Icons[bindingIdx].SpriteName : string.Empty;

				var pickerRow = BuildIconPickerRow(iconAsset, current, picked => {
					Undo.RecordObject(reg, "Set Glyph Icon");
					int idx = reg.Icons.FindIndex(b => b.DeviceId == deviceId && b.Action == action);
					if (string.IsNullOrEmpty(picked)) {
						if (idx >= 0) reg.Icons.RemoveAt(idx);
					} else if (idx >= 0) {
						GlyphActionRegistry.IconBinding b = reg.Icons[idx];
						b.SpriteName = picked;
						reg.Icons[idx] = b;
					} else {
						reg.Icons.Add(new GlyphActionRegistry.IconBinding { DeviceId = deviceId, Action = action, SpriteName = picked });
					}
					EditorUtility.SetDirty(reg);
					serializedObject.Update();
					RebuildIconsGrid(body);
				});
				body.Add(col.Row(action, pickerRow));
			}
		}

		/// <summary>A flat, EditorKit-styled button showing the currently picked icon's small thumbnail (or
		/// "&lt;None&gt;"); click opens <see cref="OpenIconPickerPopup"/> -- a bespoke EditorKit-style popup
		/// (bigger icons + a search field), not <c>AdvancedDropdown</c> (IMGUI, small fixed icon size, no
		/// search box).</summary>
		private static VisualElement BuildIconPickerRow(SpriteGlyphAsset asset, string currentName, System.Action<string> onPick) {
			Texture2D thumb = null;
			string label = string.IsNullOrEmpty(currentName) ? "<None>" : currentName;
			if (asset != null && !string.IsNullOrEmpty(currentName) && asset.TryGet(currentName, out SpriteGlyphEntry entry)) {
				thumb = SpriteThumbnailCache.Get(asset, entry, SpriteThumbnailCache.SmallSize);
			}

			var field = new VisualElement { pickingMode = PickingMode.Position, style = {
				flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween,
				backgroundColor = SperlichEditorTheme.BgDark, paddingLeft = 6, paddingRight = 6, height = 20, flexGrow = 1,
			} };
			SperlichEditorWidgets.SetRadius(field, 3);
			SperlichEditorWidgets.ApplyColorTransition(field, 100, "background-color");
			SperlichEditorWidgets.SetHoverCursor(field, MouseCursor.Link);
			field.RegisterCallback<MouseEnterEvent>(_ => field.style.backgroundColor = Color.Lerp(SperlichEditorTheme.BgDark, Color.white, 0.06f));
			field.RegisterCallback<MouseLeaveEvent>(_ => field.style.backgroundColor = SperlichEditorTheme.BgDark);

			if (thumb != null) {
				var img = new Image { image = thumb, style = { width = 14, height = 14, marginRight = 4, flexShrink = 0 } };
				field.Add(img);
			}
			field.Add(new Label(label) { pickingMode = PickingMode.Ignore, style = {
				fontSize = 11, color = SperlichEditorTheme.TextPrimary, flexGrow = 1, flexShrink = 1,
				whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = UnityEngine.UIElements.TextOverflow.Ellipsis,
			} });
			field.Add(new Label("▾") { pickingMode = PickingMode.Ignore, style = { fontSize = 9, color = SperlichEditorTheme.TextMuted, marginLeft = 4, flexShrink = 0 } });

			field.RegisterCallback<ClickEvent>(_ => OpenIconPickerPopup(field, asset, onPick));
			return field;
		}

		/// <summary>Bespoke EditorKit-style icon picker popup: a search field, then a scrolling grid of
		/// entries sorted alphabetically, each row with a 32px preview -- built the same way
		/// <see cref="SperlichEditorWidgets.BuildDropdown"/> builds its popup (same positioning /
		/// dismiss-on-outside-click / dismiss-on-focus-change plumbing), just with search + bigger icons,
		/// neither of which the shared flat dropdown supports.</summary>
		private static void OpenIconPickerPopup(VisualElement anchor, SpriteGlyphAsset asset, System.Action<string> onPick) {
			VisualElement panelRoot = SperlichEditorWidgets.ResolveOverlayRoot(anchor);
			if (panelRoot == null) return;

			var popup = SperlichEditorWidgets.CreateBox(4, SperlichEditorTheme.BorderStrong);
			popup.style.position = Position.Absolute;
			popup.style.backgroundColor = SperlichEditorTheme.BgPanel;
			popup.style.maxHeight = 340;
			float popupMinWidth = Mathf.Max(anchor.worldBound.width, 240f);
			popup.style.minWidth = popupMinWidth;
			if (EditorStyles.label?.font != null) popup.style.unityFont = EditorStyles.label.font;
			for (var p = anchor; p != null; p = p.hierarchy.parent) {
				for (int s = 0; s < p.styleSheets.count; s++) {
					var sheet = p.styleSheets[s];
					if (!popup.styleSheets.Contains(sheet)) popup.styleSheets.Add(sheet);
				}
			}

			var search = new TextField { style = { marginLeft = 4, marginRight = 4, marginTop = 4, marginBottom = 4 } };
			search.Q(className: "unity-text-field__input").style.fontSize = 11;
			popup.Add(search);

			var optionHost = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 280 } };
			popup.Add(optionHost);

			List<SpriteGlyphEntry> sorted = null;
			if (asset != null) {
				sorted = new List<SpriteGlyphEntry>(asset.Entries);
				sorted.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
			}

			void ClosePopup() => popup.RemoveFromHierarchy();

			VisualElement BuildRow(string label, Texture2D thumb, System.Action onClick) {
				var row = new VisualElement { pickingMode = PickingMode.Position, style = {
					flexDirection = FlexDirection.Row, alignItems = Align.Center,
					paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 3,
				} };
				SperlichEditorWidgets.ApplyColorTransition(row, 80, "background-color");
				SperlichEditorWidgets.SetHoverCursor(row, MouseCursor.Link);
				row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f));
				row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = Color.clear);
				row.RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); onClick(); ClosePopup(); });

				var thumbHolder = new VisualElement { style = {
					width = 32, height = 32, marginRight = 8, flexShrink = 0, alignItems = Align.Center, justifyContent = Justify.Center,
					backgroundColor = SperlichEditorTheme.BgDark,
				} };
				SperlichEditorWidgets.SetRadius(thumbHolder, 3);
				if (thumb != null) thumbHolder.Add(new Image { image = thumb, style = { width = 28, height = 28 } });
				row.Add(thumbHolder);

				row.Add(new Label(label) { pickingMode = PickingMode.Ignore, style = {
					fontSize = 12, color = SperlichEditorTheme.TextPrimary, flexGrow = 1,
					whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = UnityEngine.UIElements.TextOverflow.Ellipsis,
				} });
				return row;
			}

			void Populate(string filter) {
				optionHost.Clear();
				optionHost.Add(BuildRow("<None>", null, () => onPick(string.Empty)));
				if (sorted == null) {
					optionHost.Add(new Label("(assign a Sprite Asset first)") { style = { color = SperlichEditorTheme.TextMuted, fontSize = 11, paddingLeft = 6, paddingTop = 4 } });
					return;
				}
				bool hasFilter = !string.IsNullOrEmpty(filter);
				foreach (SpriteGlyphEntry e in sorted) {
					if (string.IsNullOrEmpty(e.Name)) continue;
					if (hasFilter && e.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
					Texture2D thumb = SpriteThumbnailCache.Get(asset, e, SpriteThumbnailCache.LargeSize);
					optionHost.Add(BuildRow(e.Name, thumb, () => onPick(e.Name)));
				}
			}
			Populate(string.Empty);
			search.RegisterValueChangedCallback(evt => Populate(evt.newValue));

			panelRoot.Add(popup);
			popup.BringToFront();

			const float margin = 4f;
			Rect anchorBound = anchor.worldBound;
			Vector2 topLeft = panelRoot.WorldToLocal(new Vector2(anchorBound.xMin, anchorBound.yMax));
			float panelWidth = panelRoot.contentRect.width;
			float targetLeft = topLeft.x;
			if (panelWidth > 0f && targetLeft + popupMinWidth > panelWidth - margin) {
				targetLeft = Mathf.Max(margin, panelWidth - popupMinWidth - margin);
			}
			popup.style.left = targetLeft;
			popup.style.top = topLeft.y + 2;

			search.schedule.Execute(() => search.Q(className: "unity-text-field__input")?.Focus());

			VisualElement dismissTree = anchor.panel?.visualTree;
			EventCallback<PointerDownEvent> dismissHandler = null;
			EditorApplication.CallbackFunction focusWatch = null;
			if (dismissTree != null) {
				dismissHandler = evt => {
					var t = evt.target as VisualElement;
					if (t != null && (t == popup || popup.Contains(t))) return;
					ClosePopup();
				};
				dismissTree.RegisterCallback(dismissHandler, TrickleDown.TrickleDown);
			}
			EditorWindow triggerWindow = EditorWindow.focusedWindow;
			focusWatch = () => { if (EditorWindow.focusedWindow != triggerWindow) ClosePopup(); };
			EditorApplication.update += focusWatch;

			popup.RegisterCallback<DetachFromPanelEvent>(_ => {
				if (dismissTree != null && dismissHandler != null) dismissTree.UnregisterCallback(dismissHandler, TrickleDown.TrickleDown);
				EditorApplication.update -= focusWatch;
			});
		}

		/// <summary>Cropped-and-downscaled preview thumbnails, cached per (asset, entry name, size) for the
		/// lifetime of the editor session -- avoids re-cropping the atlas on every repaint. Two call sites use
		/// different sizes (a small one for the collapsed picker button, a larger one for the picker popup's
		/// rows), so the cache key includes the requested size.</summary>
		private static class SpriteThumbnailCache {
			public const int SmallSize = 16;
			public const int LargeSize = 32;

			private static readonly Dictionary<(SpriteGlyphAsset, string, int), Texture2D> cache = new();

			public static Texture2D Get(SpriteGlyphAsset asset, SpriteGlyphEntry entry, int size) {
				var key = (asset, entry.Name, size);
				if (cache.TryGetValue(key, out Texture2D t) && t != null) return t;
				t = Crop(asset.Atlas, entry.PixelRect, size);
				cache[key] = t;
				return t;
			}

			private static Texture2D Crop(Texture2D atlas, Rect pixelRect, int size) {
				if (atlas == null || pixelRect.width <= 0f || pixelRect.height <= 0f) return null;
				if (!atlas.isReadable) return null; // baked atlas wasn't marked Read/Write Enabled -- no preview, name still shown
				var thumb = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
				for (int y = 0; y < size; y++) {
					for (int x = 0; x < size; x++) {
						float u = (x + 0.5f) / size;
						float v = (y + 0.5f) / size;
						float px = (pixelRect.x + u * pixelRect.width) / atlas.width;
						float py = (pixelRect.y + v * pixelRect.height) / atlas.height;
						thumb.SetPixel(x, y, atlas.GetPixelBilinear(px, py));
					}
				}
				thumb.Apply();
				return thumb;
			}
		}

		private static VisualElement Section(VisualElement parent, string title, bool expanded, string tooltip = null) {
			var (header, sectionBody, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(GlyphActionRegistryEditor));
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
