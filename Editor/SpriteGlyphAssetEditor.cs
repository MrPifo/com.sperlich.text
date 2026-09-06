using System.Collections.Generic;
using Sperlich.EditorKit;
using Sperlich.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Drag & drop icons in, give each a name, hit "Rebuild Atlas" -- packs them into one RGBA texture via
	/// <see cref="Texture2D.PackTextures"/> and writes matching <see cref="SpriteGlyphEntry"/> pixel rects.
	/// Accepts plain <see cref="Texture2D"/> (packs the whole image), a single sliced <see cref="Sprite"/>
	/// (packs just that slice), or a whole sliced tilesheet <see cref="Texture2D"/> (Sprite Mode "Multiple") --
	/// dropping the sheet itself auto-expands into one icon per contained slice, named after the slice. Built
	/// on UI Toolkit with the shared Sperlich EditorKit, same conventions as <c>GlyphActionRegistryEditor</c>.
	/// </summary>
	[CustomEditor(typeof(SpriteGlyphAsset))]
	public sealed class SpriteGlyphAssetEditor : Editor {

		private static readonly Color Accent = SperlichEditorTheme.ButtonAccent;
		private readonly SperlichFieldColumn col = new(60f);

		public override VisualElement CreateInspectorGUI() {
			var asset = (SpriteGlyphAsset)target;
			var root = new VisualElement { style = { paddingTop = 2, paddingBottom = 4, marginLeft = -15, marginRight = -4 } };

			var errorHost = new VisualElement();
			root.Add(errorHost);

			var roleSection = Section(root, "ROLE", true);
			SerializedProperty roleProp = serializedObject.FindProperty("Role");
			VisualElement roleDropdown = SperlichEditorWidgets.CreateEnumDropdown(roleProp, Accent);
			roleSection.Add(col.Row("Role", roleDropdown));

			void RefreshRoleInfo() {
				roleDropdown.tooltip = asset.Role == SpriteGlyphAssetRole.Main
					? "Main: the asset SText actually renders from. \"Rebuild Atlas\" automatically finds and folds in every Module asset in the project -- no manual linking needed."
					: "Module: purely organizational. Its icons are picked up automatically the next time the project's Main asset rebuilds its atlas -- this asset itself is never rendered from.";
			}
			RefreshRoleInfo();

			void RefreshMultiMainError() {
				errorHost.Clear();
				if (asset.Role != SpriteGlyphAssetRole.Main) return;
				List<string> otherMains = FindOtherMainAssets(asset);
				if (otherMains.Count == 0) return;
				errorHost.Add(SperlichEditorWidgets.CreateMessageBox(
					$"Another Main SpriteGlyphAsset already exists ({string.Join(", ", otherMains)}). Only one Main " +
					"asset is allowed per project (SText.ResolvedSpriteAsset can only resolve one) -- change all but " +
					"one of them to Module.", SperlichEditorWidgets.MessageKind.Error));
			}
			RefreshMultiMainError();

			roleSection.TrackPropertyValue(roleProp, _ => { RefreshRoleInfo(); RefreshMultiMainError(); });

			var section = Section(root, "SOURCE ICONS", true,
				"Drag icon textures, sliced Sprites, or a whole sliced tilesheet into the list below, give each " +
				"a unique name, then click \"Rebuild Atlas\". Reference them from markup as <sprite=\"name\">.");
			SerializedProperty iconsProp = serializedObject.FindProperty("SourceIcons");
			var list = new VisualElement();
			// Icon cards carry a big preview each -- past a handful this section could grow past the whole
			// inspector, so cap it and let it scroll instead (same idea as the packed-entries chip grid, just
			// with cards being far taller). maxHeight (not height) so a couple of icons still sit compact and
			// only a longer list actually scrolls.
			var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 640, marginTop = 2 } };
			scroll.Add(list);
			section.Add(scroll);

			void RebuildList() {
				list.Clear();
				for (int i = 0; i < iconsProp.arraySize; i++) list.Add(BuildIconCard(iconsProp, i, RebuildList));
			}
			RebuildList();

			int lastCount = iconsProp.arraySize;
			section.TrackSerializedObjectValue(serializedObject, _ => {
				if (iconsProp.arraySize != lastCount) { lastCount = iconsProp.arraySize; RebuildList(); }
			});

			var addBtn = SperlichEditorWidgets.MakeButton("Add Icon Slot", 100, () => {
				Undo.RecordObject(asset, "Add Sprite Icon");
				asset.SourceIcons.Add(new SpriteGlyphAsset.SourceIcon { Name = $"icon{asset.SourceIcons.Count}" });
				EditorUtility.SetDirty(asset);
				serializedObject.Update();
			});
			addBtn.style.marginTop = 4;
			section.Add(addBtn);

			section.Add(BuildDropZone(asset, RebuildList));

			bool HasAnyIcons() => asset.SourceIcons.Count > 0
				|| (asset.Role == SpriteGlyphAssetRole.Main && FindAllModules().Exists(m => m.SourceIcons.Count > 0));
			var rebuildBtn = SperlichEditorWidgets.MakeButton("Rebuild Atlas", 0, () => RebuildAtlas(asset), isAccent: true);
			rebuildBtn.style.height = 28;
			rebuildBtn.style.marginTop = 8;
			rebuildBtn.style.flexGrow = 1;
			rebuildBtn.SetEnabled(HasAnyIcons());
			section.TrackSerializedObjectValue(serializedObject, _ => rebuildBtn.SetEnabled(HasAnyIcons()));
			section.Add(rebuildBtn);

			var packedSection = Section(root, "PACKED ENTRIES", true);
			var packedInfo = new Label();
			packedSection.Add(packedInfo);
			var packedGrid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
			packedSection.Add(packedGrid);

			void RebuildPacked() {
				packedInfo.text = asset.Atlas != null
					? $"{asset.Entries.Count} icon(s) in a {asset.Atlas.width}x{asset.Atlas.height} atlas"
					: "No atlas baked yet.";
				packedGrid.Clear();
				if (asset.Atlas == null) return;
				foreach (SpriteGlyphEntry e in asset.Entries) {
					packedGrid.Add(BuildPackedChip(asset, e));
				}
			}
			RebuildPacked();
			packedSection.TrackSerializedObjectValue(serializedObject, _ => RebuildPacked());

			return root;
		}

		/// <summary>Every <see cref="SpriteGlyphAssetRole.Module"/> asset in the project (editor-only project
		/// scan -- fine since this only ever runs during a "Rebuild Atlas" click, never at runtime).</summary>
		private static List<SpriteGlyphAsset> FindAllModules() {
			var result = new List<SpriteGlyphAsset>();
			foreach (string guid in AssetDatabase.FindAssets("t:SpriteGlyphAsset")) {
				var a = AssetDatabase.LoadAssetAtPath<SpriteGlyphAsset>(AssetDatabase.GUIDToAssetPath(guid));
				if (a != null && a.Role == SpriteGlyphAssetRole.Module) result.Add(a);
			}
			return result;
		}

		/// <summary>Names of every OTHER <see cref="SpriteGlyphAssetRole.Main"/> asset in the project (editor-only
		/// project scan) -- only one Main is meaningful, since <see cref="SpriteGlyphAsset.GetDefault"/> and a
		/// plain drag onto <c>SText.spriteGlyphAsset</c> can only ever point at one asset at a time; this just
		/// surfaces the mistake in the Inspector instead of letting it silently pick "whichever Resources.Load
		/// happens to return first".</summary>
		private static List<string> FindOtherMainAssets(SpriteGlyphAsset self) {
			var result = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:SpriteGlyphAsset")) {
				var a = AssetDatabase.LoadAssetAtPath<SpriteGlyphAsset>(AssetDatabase.GUIDToAssetPath(guid));
				if (a != null && a != self && a.Role == SpriteGlyphAssetRole.Main) result.Add(a.name);
			}
			return result;
		}

		private const int PreviewSize = 44;

		private VisualElement BuildIconCard(SerializedProperty iconsProp, int index, System.Action rebuild) {
			SerializedProperty el = iconsProp.GetArrayElementAtIndex(index);
			SerializedProperty nameProp = el.FindPropertyRelative("Name");
			SerializedProperty spriteProp = el.FindPropertyRelative("Sprite");
			SerializedProperty textureProp = el.FindPropertyRelative("Texture");

			var card = SperlichEditorWidgets.CreateBox(4, SperlichEditorTheme.BorderSubtle);
			card.style.backgroundColor = SperlichEditorTheme.BgStepBody;
			card.style.marginBottom = 2;
			card.style.paddingLeft = 5; card.style.paddingRight = 5; card.style.paddingTop = 4; card.style.paddingBottom = 4;
			card.style.flexDirection = FlexDirection.Row;
			card.style.alignItems = Align.Center;
			card.style.position = Position.Relative;
			card.style.paddingRight = 24;

			var kindLabel = new Label(KindLabel(spriteProp, textureProp)) { style = { fontSize = 9, color = SperlichEditorTheme.TextMuted, marginLeft = 4, flexShrink = 0 } };

			VisualElement removeBtn = SperlichEditorWidgets.CreateRemoveButton(() => {
				iconsProp.DeleteArrayElementAtIndex(index);
				iconsProp.serializedObject.ApplyModifiedProperties();
				rebuild();
			}, 16);
			removeBtn.style.position = Position.Absolute;
			removeBtn.style.top = 4;
			removeBtn.style.right = 4;
			card.Add(removeBtn);

			// Real asset-preview image (Unity's own thumbnail generator, not our atlas crop -- there's no atlas
			// yet at this stage, these are the *source* icons). Also doubles as its own drag & drop target so
			// replacing one icon doesn't require hunting for the small ObjectField.
			var previewImage = new Image { scaleMode = ScaleMode.ScaleToFit, style = { width = PreviewSize - 4, height = PreviewSize - 4 } };
			var previewHolder = new VisualElement { pickingMode = PickingMode.Position, style = {
				width = PreviewSize, height = PreviewSize, marginRight = 6, flexShrink = 0,
				alignItems = Align.Center, justifyContent = Justify.Center, backgroundColor = SperlichEditorTheme.BgDark,
			} };
			SperlichEditorWidgets.SetRadius(previewHolder, 4);
			previewHolder.Add(previewImage);
			card.Add(previewHolder);

			void RefreshPreview() {
				Object obj = spriteProp.objectReferenceValue != null ? (Object)spriteProp.objectReferenceValue : textureProp.objectReferenceValue;
				if (obj == null) { previewImage.image = null; return; }
				Texture2D prev = AssetPreview.GetAssetPreview(obj);
				previewImage.image = prev != null ? prev : AssetPreview.GetMiniThumbnail(obj);
				if (AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID())) {
					previewImage.schedule.Execute(RefreshPreview).ExecuteLater(200);
				}
			}
			RefreshPreview();

			previewHolder.RegisterCallback<DragUpdatedEvent>(_ => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
			previewHolder.RegisterCallback<DragPerformEvent>(_ => {
				DragAndDrop.AcceptDrag();
				if (DragAndDrop.objectReferences.Length == 0) return;
				Object dragged = DragAndDrop.objectReferences[0];
				if (dragged is Sprite sprite) { spriteProp.objectReferenceValue = sprite; textureProp.objectReferenceValue = null; }
				else if (dragged is Texture2D tex) { spriteProp.objectReferenceValue = null; textureProp.objectReferenceValue = tex; }
				else return;
				el.serializedObject.ApplyModifiedProperties();
				kindLabel.text = KindLabel(spriteProp, textureProp);
				RefreshPreview();
			});

			var right = new VisualElement { style = { flexGrow = 1 } };
			var nameField = new TextField { style = { flexGrow = 1 } };
			nameField.BindProperty(nameProp);
			SperlichFieldColumn.HideInternalLabel(nameField);
			var nameRow = col.Row("Name", nameField);
			nameRow.style.marginTop = 0; nameRow.style.marginBottom = 0;
			right.Add(nameRow);

			Object current = spriteProp.objectReferenceValue != null ? spriteProp.objectReferenceValue : textureProp.objectReferenceValue;
			var sourceControl = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
			var objField = new ObjectField { objectType = typeof(Object), value = current, style = { flexGrow = 1 } };
			objField.RegisterValueChangedCallback(evt => {
				spriteProp.objectReferenceValue = evt.newValue as Sprite;
				textureProp.objectReferenceValue = evt.newValue as Texture2D;
				el.serializedObject.ApplyModifiedProperties();
				kindLabel.text = KindLabel(spriteProp, textureProp);
				RefreshPreview();
			});
			SperlichFieldColumn.HideInternalLabel(objField);
			sourceControl.Add(objField);
			sourceControl.Add(kindLabel);
			var sourceRow = col.Row("Source", sourceControl);
			sourceRow.style.marginTop = 0; sourceRow.style.marginBottom = 0;
			right.Add(sourceRow);

			card.Add(right);
			return card;
		}

		private static string KindLabel(SerializedProperty spriteProp, SerializedProperty textureProp) {
			if (spriteProp.objectReferenceValue != null) return "Sprite";
			if (textureProp.objectReferenceValue != null) return "Texture";
			return "Empty";
		}

		private static VisualElement BuildDropZone(SpriteGlyphAsset asset, System.Action rebuild) {
			var zone = new VisualElement { style = {
				height = 40, marginTop = 6, borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
				alignItems = Align.Center, justifyContent = Justify.Center,
			} };
			SperlichEditorWidgets.SetRadius(zone, 4);
			SperlichEditorWidgets.SetBorderColor(zone, SperlichEditorTheme.BorderSubtle);
			zone.style.backgroundColor = SperlichEditorTheme.BgDark;
			zone.Add(new Label("Drop textures, sprites, or a sliced tilesheet here to add them") {
				style = { fontSize = 11, color = SperlichEditorTheme.TextMuted }
			});

			zone.RegisterCallback<DragUpdatedEvent>(_ => {
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
			});
			zone.RegisterCallback<DragPerformEvent>(_ => {
				DragAndDrop.AcceptDrag();
				Undo.RecordObject(asset, "Add Sprite Icons");
				foreach (Object dragged in DragAndDrop.objectReferences) {
					if (dragged is Sprite sprite) {
						asset.SourceIcons.Add(new SpriteGlyphAsset.SourceIcon { Name = sprite.name, Sprite = sprite });
						continue;
					}
					if (dragged is Texture2D tex) {
						Sprite[] slices = LoadAllSlices(tex);
						if (slices.Length > 1) {
							// whole tilesheet (Sprite Mode "Multiple") -- expand into one icon per slice instead
							// of packing the entire sheet as a single image
							foreach (Sprite s in slices) {
								asset.SourceIcons.Add(new SpriteGlyphAsset.SourceIcon { Name = s.name, Sprite = s });
							}
						} else if (slices.Length == 1) {
							// Sprite Mode "Single" -- one slice that covers the whole texture; use it directly so
							// trimmed/packed single sprites still crop correctly
							asset.SourceIcons.Add(new SpriteGlyphAsset.SourceIcon { Name = tex.name, Sprite = slices[0] });
						} else {
							asset.SourceIcons.Add(new SpriteGlyphAsset.SourceIcon { Name = tex.name, Texture = tex });
						}
					}
				}
				EditorUtility.SetDirty(asset);
				rebuild();
			});
			return zone;
		}

		/// <summary>All <see cref="Sprite"/> sub-assets sliced out of this texture's import (empty if the
		/// texture isn't imported as a Sprite / has no slices).</summary>
		private static Sprite[] LoadAllSlices(Texture2D tex) {
			string path = AssetDatabase.GetAssetPath(tex);
			if (string.IsNullOrEmpty(path)) return System.Array.Empty<Sprite>();
			Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
			var sprites = new List<Sprite>();
			foreach (Object o in all) {
				if (o is Sprite s) sprites.Add(s);
			}
			return sprites.ToArray();
		}

		private static void RebuildAtlas(SpriteGlyphAsset asset) {
			var readableCopies = new List<Texture2D>();
			var names = new List<string>();
			var seen = new HashSet<string>();
			var duplicates = new List<string>();

			void CollectFrom(SpriteGlyphAsset module) {
				foreach (SpriteGlyphAsset.SourceIcon icon in module.SourceIcons) {
					if (string.IsNullOrEmpty(icon.Name) || !icon.TryResolve(out Texture2D tex, out Rect rect)) continue;
					if (!seen.Add(icon.Name)) { duplicates.Add(icon.Name); continue; } // first occurrence wins
					readableCopies.Add(MakeReadable(tex, rect));
					names.Add(icon.Name);
				}
			}
			CollectFrom(asset);
			// Main automatically folds in every Module asset in the project -- no manual linking. A Module's
			// own "Rebuild Atlas" only bakes its own icons (a local preview atlas, never actually rendered
			// from), so it doesn't also pull in other modules.
			if (asset.Role == SpriteGlyphAssetRole.Main) {
				foreach (SpriteGlyphAsset module in FindAllModules()) CollectFrom(module);
			}

			if (duplicates.Count > 0) {
				Debug.LogWarning($"[Sperlich.Text] {duplicates.Count} duplicate icon name(s) across '{asset.name}' and its " +
					$"Module assets, kept the first one found: {string.Join(", ", duplicates)}");
			}
			if (readableCopies.Count == 0) {
				Debug.LogWarning("[Sperlich.Text] No valid icons to pack (need a texture/sprite and a name).");
				return;
			}

			// Embed the atlas as a sub-asset of this ScriptableObject (same principle as a Sprite living
			// nested under its source texture) instead of writing a separate .png next to it -- one file in
			// the Project view instead of two, and no TextureImporter step needed: a script-created,
			// uncompressed Texture2D asset stays readable (for the preview thumbnails) without an explicit
			// "Read/Write Enabled" toggle, since it never goes through the normal import/compress pipeline.
			string assetPath = AssetDatabase.GetAssetPath(asset);
			bool reuseExisting = asset.Atlas != null && AssetDatabase.GetAssetPath(asset.Atlas) == assetPath;
			Texture2D atlas = reuseExisting ? asset.Atlas : new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
			atlas.name = asset.name + "_Atlas";
			// 4px padding between packed icons (up from Unity's own PackTextures default of 4 is already this,
			// stated explicitly here) -- extra margin on top of TextLayoutEngine's half-texel UV inset, which
			// is what actually prevents edge bleeding; the padding just gives that inset more room to work with.
			Rect[] uvRects = atlas.PackTextures(readableCopies.ToArray(), 4, 2048); // resizes/repaints atlas in place

			asset.Entries.Clear();
			for (int i = 0; i < uvRects.Length; i++) {
				Rect uv = uvRects[i];
				Rect px = new Rect(uv.x * atlas.width, uv.y * atlas.height, uv.width * atlas.width, uv.height * atlas.height);
				asset.Entries.Add(new SpriteGlyphEntry { Name = names[i], PixelRect = px });
			}

			foreach (Texture2D t in readableCopies) Object.DestroyImmediate(t);

			if (!reuseExisting) {
				if (asset.Atlas != null) {
					Debug.Log($"[Sperlich.Text] '{asset.name}' now embeds its atlas as a sub-asset. The previous " +
						$"separate file at '{AssetDatabase.GetAssetPath(asset.Atlas)}' is no longer used and can be deleted.");
				}
				AssetDatabase.AddObjectToAsset(atlas, asset);
				asset.Atlas = atlas;
			}

			EditorUtility.SetDirty(atlas);
			EditorUtility.SetDirty(asset);
			AssetDatabase.SaveAssets();
			AssetDatabase.ImportAsset(assetPath);
		}

		/// <summary>Import settings often mark source icons non-readable/compressed; copy pixels through a
		/// temporary RenderTexture so packing works regardless of the source's own import settings. Only the
		/// given pixel rect (a sliced Sprite's own region, or the whole texture) is copied out, so a tilesheet
		/// slice packs just its own icon rather than the entire sheet.</summary>
		private static Texture2D MakeReadable(Texture2D source, Rect pixelRect) {
			RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			Graphics.Blit(source, rt);
			RenderTexture prev = RenderTexture.active;
			RenderTexture.active = rt;
			int w = Mathf.Max(1, Mathf.RoundToInt(pixelRect.width));
			int h = Mathf.Max(1, Mathf.RoundToInt(pixelRect.height));
			var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
			copy.ReadPixels(new Rect(pixelRect.x, pixelRect.y, w, h), 0, 0);
			copy.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);
			return copy;
		}

		// -- packed entries preview --------------------------------------------------------------------

		private static VisualElement BuildPackedChip(SpriteGlyphAsset asset, SpriteGlyphEntry entry) {
			var chip = new VisualElement { style = {
				flexDirection = FlexDirection.Column, alignItems = Align.Center, width = 56, marginRight = 6, marginBottom = 6,
			} };
			var thumbHolder = new VisualElement { style = {
				width = 40, height = 40, alignItems = Align.Center, justifyContent = Justify.Center, backgroundColor = SperlichEditorTheme.BgDark,
			} };
			SperlichEditorWidgets.SetRadius(thumbHolder, 3);
			Texture2D thumb = PackedThumbnailCache.Get(asset, entry);
			if (thumb != null) thumbHolder.Add(new Image { image = thumb, style = { width = 36, height = 36 } });
			chip.Add(thumbHolder);
			chip.Add(new Label(entry.Name) {
				style = { fontSize = 9, color = SperlichEditorTheme.TextMuted, marginTop = 2, unityTextAlign = TextAnchor.MiddleCenter,
					whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = UnityEngine.UIElements.TextOverflow.Ellipsis, maxWidth = 56 }
			});
			return chip;
		}

		private static class PackedThumbnailCache {
			private const int Size = 36;
			private static readonly Dictionary<(SpriteGlyphAsset, string), Texture2D> cache = new();

			public static Texture2D Get(SpriteGlyphAsset asset, SpriteGlyphEntry entry) {
				var key = (asset, entry.Name);
				if (cache.TryGetValue(key, out Texture2D t) && t != null) return t;
				t = Crop(asset.Atlas, entry.PixelRect);
				cache[key] = t;
				return t;
			}

			private static Texture2D Crop(Texture2D atlas, Rect pixelRect) {
				if (atlas == null || !atlas.isReadable || pixelRect.width <= 0f || pixelRect.height <= 0f) return null;
				var thumb = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
				for (int y = 0; y < Size; y++) {
					for (int x = 0; x < Size; x++) {
						float u = (x + 0.5f) / Size;
						float v = (y + 0.5f) / Size;
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
			var (header, sectionBody, _) = SperlichEditorWidgets.CreateChevronSection(title, expanded, SperlichEditorTheme.BgStep, null, nameof(SpriteGlyphAssetEditor));
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
