using System;

namespace Sperlich.Text {

	/// <summary>
	/// The package's only touchpoint with "which input device is active" -- deliberately just a string key
	/// set by the consuming project (from its own Rewired / Unity Input System / custom detection code), so
	/// this package never depends on a specific input backend. `&lt;glyph:ActionName&gt;` / `@ActionName@`
	/// tags resolve against <see cref="ActiveDeviceId"/> unless they carry their own `device=` override.
	/// Device ids are matched case-insensitively everywhere they're compared (here and in
	/// <see cref="GlyphActionRegistry"/>), so "Xbox"/"xbox"/"XBOX" are all the same device.
	/// </summary>
	public static class GlyphDeviceContext {

		private static string explicitDeviceId = string.Empty;

		/// <summary>Current device key (e.g. "Keyboard", "Xbox", "PlayStation") -- whatever ids your
		/// <see cref="GlyphActionRegistry"/> entries use. Empty until the game calls <see cref="SetActiveDevice"/>
		/// -- except in the Editor, where it falls back to <see cref="STextSettings.editorDefaultDeviceId"/> if
		/// set, so glyphs resolve to something sensible while testing before your own input-detection code runs.
		/// That fallback is Editor-only and never applies in a build.</summary>
		public static string ActiveDeviceId {
			get {
				if (!string.IsNullOrEmpty(explicitDeviceId)) return explicitDeviceId;
#if UNITY_EDITOR
				return STextSettings.GetOrDefault()?.editorDefaultDeviceId ?? string.Empty;
#else
				return string.Empty;
#endif
			}
		}

		/// <summary>Raised only when the value actually changes. <c>SText</c> instances showing a
		/// device-resolved glyph subscribe to this and re-layout (no re-parse needed) in response.</summary>
		public static event Action DeviceChanged;

		/// <summary>Sets the active device and fires <see cref="DeviceChanged"/> if it differs (case-insensitively)
		/// from the current one.</summary>
		public static void SetActiveDevice(string deviceId) {
			deviceId ??= string.Empty;
			if (string.Equals(explicitDeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) return;
			explicitDeviceId = deviceId;
			DeviceChanged?.Invoke();
		}

		/// <summary>Forces <see cref="DeviceChanged"/> to fire even though <see cref="ActiveDeviceId"/> itself
		/// didn't change -- used by <c>STextSettingsEditor</c> when <see cref="STextSettings.editorDefaultDeviceId"/>
		/// is edited, so every visible <c>SText</c> re-layouts immediately for a live preview instead of only
		/// picking up the new fallback the next time something else happens to mark it dirty.</summary>
		public static void RefreshEditorDefault() => DeviceChanged?.Invoke();
	}
}
