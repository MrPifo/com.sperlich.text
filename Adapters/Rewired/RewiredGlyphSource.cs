// Opt-in Rewired adapter for Sperlich.Text input glyphs (plan module 10.2).
// Guarded so the package always compiles without Rewired. To enable:
//   Player Settings > Scripting Define Symbols  ->  add  SPERLICH_TEXT_REWIRED
// This file lives outside any asmdef, so it compiles into Assembly-CSharp where both
// Rewired and Sperlich.Text are visible. Verify the element-label calls against your Rewired version.
#if SPERLICH_TEXT_REWIRED
using System;
using UnityEngine;
using Rewired;
using Sperlich.Text;

namespace Sperlich.Text.Adapters {

	/// <summary>
	/// Resolves <c>&lt;glyph:ActionName&gt;</c> tags through Rewired. Delegates device detection to
	/// <see cref="ReInput.ControllerHelper.GetLastActiveControllerType"/> and raises
	/// <see cref="DeviceChanged"/> when it flips, so labels re-mesh with the right prompt.
	/// </summary>
	public sealed class RewiredGlyphSource : MonoBehaviour, ITextGlyphSource {

		[Tooltip("Rewired player id whose bindings are shown.")]
		public int playerId = 0;

		public event Action DeviceChanged;

		private ControllerType lastType = ControllerType.Keyboard;

		private void Update() {
			if (!ReInput.isReady) return;
			ControllerType now = ReInput.controllers.GetLastActiveControllerType();
			if (now != lastType) {
				lastType = now;
				DeviceChanged?.Invoke();
			}
		}

		public bool TryGetGlyph(string actionName, out Sprite sprite) {
			// A sprite table is game-content; wire one here if you keep controller-glyph sprites.
			sprite = null;
			return false;
		}

		public string GetFallbackLabel(string actionName) {
			if (!ReInput.isReady) return $"[{actionName}]";
			Player player = ReInput.players.GetPlayer(playerId);
			if (player == null) return $"[{actionName}]";

			InputAction action = ReInput.mapping.GetAction(actionName);
			if (action == null) return $"[{actionName}]";

			Controller last = player.controllers.GetLastActiveController();
			ControllerType type = last != null ? last.type : lastType;

			foreach (ActionElementMap aem in player.controllers.maps.ElementMapsWithAction(action.id, true)) {
				if (last != null && aem.controllerMap != null && aem.controllerMap.controllerType != type) continue;
				if (!string.IsNullOrEmpty(aem.elementIdentifierName)) return aem.elementIdentifierName;
			}
			return $"[{actionName}]";
		}
	}
}
#endif
