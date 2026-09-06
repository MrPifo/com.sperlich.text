using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Project-wide master list of input actions used by <c>&lt;glyph:ActionName&gt;</c> / <c>@ActionName@</c>
	/// tags -- one single asset. Actions and devices are each defined exactly once (<see cref="Actions"/>,
	/// <see cref="Devices"/>); <see cref="Icons"/> is a flat device×action→icon lookup table so the editor can
	/// present it per-device (pick a device, fill in every action's icon as one array) without ever re-typing
	/// an action or device name. One singleton per project, same "exactly one Resources-folder asset" rule as
	/// <see cref="STextSettings"/>.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Glyph Action Registry", fileName = "GlyphActionRegistry")]
	public sealed class GlyphActionRegistry : ScriptableObject {

		[Tooltip("Used by the inspector to offer a searchable icon picker (with preview) instead of free-typed " +
			"names. Should be the same SpriteGlyphAsset your SText components resolve <sprite>/<glyph> icons " +
			"against at runtime -- this field itself isn't read at runtime, only by the custom editor.")]
		public SpriteGlyphAsset ReferenceSpriteAsset;

		[System.Serializable]
		public struct ActionDefinition {
			public string Name;

			[Tooltip("Shown as plain text when no device has an icon for this action, e.g. \"[Jump]\" or " +
				"\"Space\". Falls back to \"[Name]\" if left empty.")]
			public string DefaultFallbackLabel;
		}

		[System.Serializable]
		public struct DeviceDefinition {
			[Tooltip("Display name only (editor convenience).")]
			public string Title;

			[Tooltip("Matched against GlyphDeviceContext.ActiveDeviceId or a tag's own device=\"...\" override.")]
			public string DeviceId;

			[Tooltip("Editor-only: restricts this device's icon picker (in the \"Icons\" tab below) to just this " +
				"asset's icons instead of the shared ReferenceSpriteAsset -- handy when a device has its own " +
				"dedicated icon set (e.g. a \"Xbox Icons\" module) and picking from the full project list would " +
				"just be noise. Leave empty to use ReferenceSpriteAsset like every other device. Not read at runtime.")]
			public SpriteGlyphAsset IconSource;
		}

		[System.Serializable]
		public struct IconBinding {
			public string DeviceId;
			public string Action;
			public string SpriteName;
		}

		public List<ActionDefinition> Actions = new();
		public List<DeviceDefinition> Devices = new();

		[Tooltip("Flat device x action -> icon lookup. Maintained by the custom editor's per-device icon " +
			"grid, not meant to be hand-edited here.")]
		public List<IconBinding> Icons = new();

		// Action/device names are matched case-insensitively everywhere (markup tags, the Icons grid, and
		// GlyphDeviceContext.ActiveDeviceId are all free-typed strings from different places -- e.g. an
		// action defined as "Jump" should still resolve <glyph:jump>). Dictionary keys are lower-invariant'd
		// at both insert and lookup time rather than fighting a custom IEqualityComparer for the tuple key.
		private Dictionary<string, string> fallbackLookup;   // action (lower) -> label
		private Dictionary<(string device, string action), string> iconLookup; // (device, action), both lower -> sprite name

		private static GlyphActionRegistry cached;
		private static bool triedLoad;

		/// <summary>Project-wide singleton, same Resources-folder / "exactly one" rule as <see cref="STextSettings"/>.</summary>
		public static GlyphActionRegistry GetDefault() {
			if (cached != null) return cached;
			if (triedLoad) return cached;
			triedLoad = true;
			cached = ProjectAssetResolver.FindSingle<GlyphActionRegistry>("GlyphActionRegistry");
			return cached;
		}

		/// <summary>
		/// Resolves an action for a device: an icon name if that device has one registered, otherwise a
		/// fallback text label (never both null -- an unknown action still gets a "[Name]" style label instead
		/// of silently vanishing, same philosophy as the sprite-not-found notdef box).
		/// </summary>
		public void TryResolve(string deviceId, string action, out string spriteName, out string fallbackLabel) {
			spriteName = null;
			fallbackLabel = $"[{action}]";
			if (string.IsNullOrEmpty(action)) return;
			string actionKey = action.ToLowerInvariant();

			if (fallbackLookup == null || fallbackLookup.Count != Actions.Count) RebuildFallbackLookup();
			if (fallbackLookup.TryGetValue(actionKey, out string label) && !string.IsNullOrEmpty(label)) fallbackLabel = label;

			if (string.IsNullOrEmpty(deviceId)) return;
			if (iconLookup == null || iconLookup.Count != Icons.Count) RebuildIconLookup();
			iconLookup.TryGetValue((deviceId.ToLowerInvariant(), actionKey), out spriteName);
		}

		private void RebuildFallbackLookup() {
			fallbackLookup = new Dictionary<string, string>(Actions.Count);
			foreach (ActionDefinition a in Actions) {
				if (!string.IsNullOrEmpty(a.Name)) fallbackLookup[a.Name.ToLowerInvariant()] = a.DefaultFallbackLabel;
			}
		}

		private void RebuildIconLookup() {
			iconLookup = new Dictionary<(string, string), string>(Icons.Count);
			foreach (IconBinding b in Icons) {
				if (string.IsNullOrEmpty(b.SpriteName) || string.IsNullOrEmpty(b.DeviceId) || string.IsNullOrEmpty(b.Action)) continue;
				iconLookup[(b.DeviceId.ToLowerInvariant(), b.Action.ToLowerInvariant())] = b.SpriteName;
			}
		}

		private void OnValidate() {
			fallbackLookup = null;
			iconLookup = null;
		}
	}
}
