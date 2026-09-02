using UnityEditor;
using UnityEngine;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Weist dem SText-MonoScript automatisch das native Unity Legacy-Text-Icon zu.
	/// </summary>
	[InitializeOnLoad]
	internal static class STextScriptIcon {

		static STextScriptIcon() {
			EditorApplication.delayCall += AssignIcon;
		}

		/// <summary>
		/// Sucht das MonoScript von SText und weist das Legacy Text Icon zu.
		/// </summary>
		private static void AssignIcon() {
			Texture2D icon = EditorGUIUtility.IconContent("Text Icon")?.image as Texture2D;
			if (icon == null) {
				return;
			}

			string[] guids = AssetDatabase.FindAssets("t:MonoScript SText");
			foreach (string guid in guids) {
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
				if (monoScript != null && monoScript.GetClass() == typeof(SText)) {
					var currentIcon = EditorGUIUtility.GetIconForObject(monoScript);
					if (currentIcon != icon) {
						var importer = AssetImporter.GetAtPath(path) as MonoImporter;
						if (importer != null) {
							importer.SetIcon(icon);
							importer.SaveAndReimport();
						}
					}
					break;
				}
			}
		}
	}
}
