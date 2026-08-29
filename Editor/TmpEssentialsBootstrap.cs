using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;
using Sperlich.Text;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Makes the package self-sufficient in a fresh project. <see cref="FontAccess"/> builds its runtime
	/// SDF atlas through <c>TMP_FontAsset.CreateFontAsset</c>, which throws a
	/// <see cref="System.NullReferenceException"/> ("Object reference not set to an instance of an object")
	/// when the "TMP Essential Resources" are missing — those supply the <c>TMP Settings</c> asset and the
	/// TMP shaders that <c>CreateFontAsset</c> dereferences.
	/// This bootstrap imports those resources automatically on the first editor load that finds them
	/// missing, then rebuilds every <see cref="SperlichText"/> so labels show up without a scene reload.
	/// Weg A only: TMP stays referenced. Replacing <see cref="FontAccess"/> with an msdfgen path (Weg B)
	/// would drop this file.
	/// </summary>
	[InitializeOnLoad]
	internal static class TmpEssentialsBootstrap {

		private const string SettingsAssetPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
		private const string PackageName = "TMP Essential Resources";
		private const string SessionGuard = "Sperlich.Text.TmpImportAttempted";

		static TmpEssentialsBootstrap() {
			// Never touch the AssetDatabase from a static ctor during a domain reload — defer one tick.
			EditorApplication.delayCall += TryImport;
		}

		private static void TryImport() {
			if (File.Exists(SettingsAssetPath)) return;                // already present
			if (SessionState.GetBool(SessionGuard, false)) return;     // don't retry on every reload
			SessionState.SetBool(SessionGuard, true);

			Debug.Log("[SperlichText] TMP essential resources are missing — importing them automatically " +
				"so the runtime SDF atlas can be built.");

			AssetDatabase.importPackageCompleted += OnImported;
			AssetDatabase.importPackageFailed += OnFailed;
			TMP_PackageResourceImporter.ImportResources(true, false, false);
		}

		private static void OnImported(string packageName) {
			if (packageName != PackageName) return;
			Unsubscribe();
			RefreshAllLabels();
			Debug.Log("[SperlichText] TMP essential resources imported. Labels rebound.");
		}

		private static void OnFailed(string packageName, string errorMessage) {
			if (packageName != PackageName) return;
			Unsubscribe();
			Debug.LogError("[SperlichText] Automatic import of TMP essential resources failed: " + errorMessage +
				"\nImport them by hand: Window > TextMeshPro > Import TMP Essential Resources.");
		}

		private static void Unsubscribe() {
			AssetDatabase.importPackageCompleted -= OnImported;
			AssetDatabase.importPackageFailed -= OnFailed;
		}

		private static void RefreshAllLabels() {
			GlyphStoreRegistry.EditorPurgeAll();
			foreach (SperlichText label in Object.FindObjectsByType<SperlichText>(
				         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
				label.EditorRebindFont();
			}
		}
	}
}
