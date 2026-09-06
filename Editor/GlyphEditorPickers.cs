using System.Collections.Generic;
using Sperlich.EditorKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Small picker widgets shared by more than one custom inspector in this package (<c>STextSettingsEditor</c>,
	/// <c>GlyphActionRegistryEditor</c>) -- kept here instead of duplicated per-editor so they stay visually and
	/// behaviourally identical everywhere a <see cref="SpriteGlyphAsset"/> is picked.
	/// </summary>
	internal static class GlyphEditorPickers {

		/// <summary>Asset picker for a <see cref="SpriteGlyphAsset"/> reference field, same rescan-on-open pattern
		/// as <see cref="SperlichEditorWidgets.CreateAssetDropdown{T}"/> but with each entry labelled
		/// "(Main)"/"(Module)" -- with two Role kinds sharing one dropdown, the name alone doesn't tell you which
		/// one you're picking.</summary>
		public static VisualElement MakeSpriteAssetDropdown(SerializedProperty objectProp, Color accent) {
			var assets = new List<SpriteGlyphAsset>();
			void Rescan() {
				assets.Clear();
				foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SpriteGlyphAsset))) {
					var a = AssetDatabase.LoadAssetAtPath<SpriteGlyphAsset>(AssetDatabase.GUIDToAssetPath(guid));
					if (a != null) assets.Add(a);
				}
				assets.Sort((x, y) => string.Compare(x.name, y.name, System.StringComparison.OrdinalIgnoreCase));
			}
			Rescan();

			int Count() => assets.Count + 1;
			string LabelFor(int i) {
				if (i == 0) return "None";
				SpriteGlyphAsset a = assets[i - 1];
				return $"{a.name} ({a.Role})";
			}
			int Selected() {
				var cur = objectProp.objectReferenceValue as SpriteGlyphAsset;
				if (cur == null) return 0;
				int ai = assets.IndexOf(cur);
				return ai >= 0 ? ai + 1 : -1;
			}
			void Pick(int i) {
				objectProp.objectReferenceValue = i == 0 ? null : (i - 1 < assets.Count ? assets[i - 1] : null);
				objectProp.serializedObject.ApplyModifiedProperties();
			}

			VisualElement field = SperlichEditorWidgets.BuildDropdown(Count, LabelFor, Selected, Pick, accent);
			field.RegisterCallback<PointerDownEvent>(_ => Rescan(), TrickleDown.TrickleDown);
			return field;
		}
	}
}
