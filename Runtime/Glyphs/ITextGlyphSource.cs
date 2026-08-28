using System;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Abstraction for dynamic input-prompt icons (plan module 10). The core never references Rewired or
	/// the Unity Input System; an adapter implements this. A <c>&lt;glyph:ActionName&gt;</c> tag resolves
	/// through the active source, and a device change raises <see cref="DeviceChanged"/> so the renderer re-meshes.
	/// </summary>
	public interface ITextGlyphSource {

		/// <summary>Raised when the active input device changes (keyboard &lt;-&gt; pad etc.).</summary>
		event Action DeviceChanged;

		/// <summary>
		/// Returns the sprite for <paramref name="actionName"/> on the current device, or null if unknown.
		/// The renderer draws it inline at the line's cap height.
		/// </summary>
		bool TryGetGlyph(string actionName, out Sprite sprite);

		/// <summary>Short human label for the current binding (fallback when no sprite exists).</summary>
		string GetFallbackLabel(string actionName);
	}

	/// <summary>Null-object glyph source: no icons, labels echo the action name in brackets.</summary>
	public sealed class NullTextGlyphSource : ITextGlyphSource {
		public event Action DeviceChanged { add { } remove { } }
		public bool TryGetGlyph(string actionName, out Sprite sprite) { sprite = null; return false; }
		public string GetFallbackLabel(string actionName) => $"[{actionName}]";
	}
}
