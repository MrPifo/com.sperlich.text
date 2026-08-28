using UnityEditor;
using UnityEngine;
using Sperlich.Text;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Drives edit-mode animation: while not in Play mode it nudges every visible <see cref="SperlichText"/>
	/// that has an animated effect (Wave/Shake/Glitch/… span or component effect, or the typewriter) so the
	/// Scene / Game view shows the motion live. Idle when nothing animates — no constant repaints.
	/// </summary>
	[InitializeOnLoad]
	internal static class SperlichTextEditorTicker {

		private static double lastTick;

		static SperlichTextEditorTicker() {
			EditorApplication.update += Tick;
		}

		private static void Tick() {
			if (Application.isPlaying) return;

			// ~60 fps cap for the editor preview
			double now = EditorApplication.timeSinceStartup;
			if (now - lastTick < 1.0 / 60.0) return;
			lastTick = now;

			SperlichText[] all = Object.FindObjectsByType<SperlichText>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			bool any = false;
			for (int i = 0; i < all.Length; i++) {
				SperlichText t = all[i];
				if (t == null || !t.isActiveAndEnabled || !t.HasAnimatedEffects) continue;
				t.EditorAnimateTick();
				any = true;
			}

			if (any) {
				Canvas.ForceUpdateCanvases();
				SceneView.RepaintAll();
				EditorApplication.QueuePlayerLoopUpdate();
			}
		}
	}
}
