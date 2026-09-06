using System;
using System.Linq;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Shared "exactly one project-owned singleton asset under a Resources folder" lookup, used by
	/// <see cref="STextSettings"/> and the default <see cref="SpriteGlyphAsset"/> lookup. Multiple hits are
	/// a project configuration error (logged), not a silent "first one wins".
	/// </summary>
	internal static class ProjectAssetResolver {

		public static T FindSingle<T>(string typeLabel) where T : UnityEngine.Object => FindSingle<T>(typeLabel, null);

		/// <summary>Same "exactly one" rule, restricted to assets matching <paramref name="predicate"/> first
		/// (e.g. <see cref="SpriteGlyphAsset"/> filters to <see cref="SpriteGlyphAssetRole.Main"/> so a
		/// <see cref="SpriteGlyphAssetRole.Module"/> asset sitting under Resources for its own reasons doesn't
		/// count towards the uniqueness check).</summary>
		public static T FindSingle<T>(string typeLabel, Func<T, bool> predicate) where T : UnityEngine.Object {
			T[] all = Resources.LoadAll<T>("");
#if UNITY_EDITOR
			if (all.Length == 0) {
				string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name);
				var list = new System.Collections.Generic.List<T>();
				foreach (string g in guids) {
					string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
					if (path.Replace('\\', '/').IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0) {
						var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
						if (obj != null) list.Add(obj);
					}
				}
				all = list.ToArray();
			}
#endif
			if (predicate != null) all = all.Where(predicate).ToArray();
			if (all.Length > 1) {
				var names = new System.Text.StringBuilder();
				foreach (T a in all) names.Append("\n  - ").Append(a.name);
				Debug.LogError($"[Sperlich.Text] Multiple {typeLabel} assets found under a Resources folder ({all.Length}). " +
					$"Keep exactly one in your project (never inside the package). Found:{names}\nFalling back to the first one.");
			}
			return all.Length > 0 ? all[0] : null;
		}
	}
}
