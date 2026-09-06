using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>Whether a <see cref="SpriteGlyphAsset"/> is the one runtime-resolved combined atlas, or an
	/// organizational contributor that gets folded into it at bake time.</summary>
	public enum SpriteGlyphAssetRole {
		/// <summary>Exactly one of these should exist under a Resources folder -- this is the asset
		/// <see cref="SpriteGlyphAsset.GetDefault"/> resolves and what <c>SText</c> actually renders from.
		/// Its "Rebuild Atlas" automatically folds in every <see cref="Module"/> asset in the project.</summary>
		Main,
		/// <summary>A purely organizational icon set (e.g. "UI Icons", "Gameplay Icons") -- never resolved or
		/// rendered from directly. Its Source Icons are automatically picked up the next time the project's
		/// <see cref="Main"/> asset rebuilds its atlas; no manual linking needed, and it doesn't need to live
		/// under a Resources folder.</summary>
		Module
	}

	/// <summary>
	/// A named-icon atlas for inline <c>&lt;sprite="name"&gt;</c> tags. Icons are plain RGBA (full colour),
	/// rendered through the "flat sprite" mode of the SDF shader (no distance-field sampling), so they sit
	/// in the same mesh/material/draw-call as the surrounding text and inherit its animated effects -- which
	/// also means a single draw call can only sample one atlas texture. <see cref="Role"/> is how you still
	/// get to organize icons into several smaller assets without that limit ever mattering: create any number
	/// of <see cref="SpriteGlyphAssetRole.Module"/> assets to organize icons by feature/category, and the
	/// single <see cref="SpriteGlyphAssetRole.Main"/> asset's "Rebuild Atlas" automatically finds and folds
	/// all of them into its one combined <see cref="Atlas"/> -- no manual per-asset linking required.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Sprite Glyph Asset", fileName = "SpriteGlyphAsset")]
	public sealed class SpriteGlyphAsset : ScriptableObject {

		[Tooltip("Main: the one asset SText actually renders from (put it under a Resources folder, exactly " +
			"one per project). Module: a purely organizational icon set that gets folded into the Main asset's " +
			"atlas automatically -- never resolved or rendered from directly, doesn't need to be in Resources.")]
		public SpriteGlyphAssetRole Role = SpriteGlyphAssetRole.Module;

		[Tooltip("Packed RGBA atlas texture all entries below reference. Embedded as a sub-asset of this " +
			"ScriptableObject by \"Rebuild Atlas\" (same principle as a Sprite nested under its source texture) " +
			"-- not a separate file next to it.")]
		public Texture2D Atlas;

		public List<SpriteGlyphEntry> Entries = new();

		/// <summary>Editor-only source icons kept alongside the packed atlas so <c>SpriteGlyphAssetEditor</c>
		/// can re-pack after adding/removing/renaming an icon. Unused at runtime.</summary>
		[System.Serializable]
		public struct SourceIcon {
			public string Name;

			/// <summary>Set when this icon came from a whole-texture drop (no slicing) -- packs the full texture.</summary>
			public Texture2D Texture;

			/// <summary>Set when this icon came from a sliced <see cref="UnityEngine.Sprite"/> (a single slice
			/// dragged in directly, or one auto-expanded from a multi-sprite tilesheet) -- only
			/// <see cref="UnityEngine.Sprite.rect"/> of <see cref="UnityEngine.Sprite.texture"/> is packed,
			/// not the whole sheet. Takes precedence over <see cref="Texture"/> when both are set.</summary>
			public Sprite Sprite;

			/// <summary>Resolves the source texture and the pixel rect within it that should be packed.</summary>
			public readonly bool TryResolve(out Texture2D texture, out Rect pixelRect) {
				if (Sprite != null && Sprite.texture != null) {
					texture = Sprite.texture;
					pixelRect = Sprite.rect;
					return true;
				}
				if (Texture != null) {
					texture = Texture;
					pixelRect = new Rect(0, 0, Texture.width, Texture.height);
					return true;
				}
				texture = null;
				pixelRect = default;
				return false;
			}
		}
		public List<SourceIcon> SourceIcons = new();

		private Dictionary<string, int> lookup;

		private static SpriteGlyphAsset cachedDefault;
		private static bool triedLoadDefault;

		/// <summary>Project-wide fallback used by an <c>SText</c> that has no <c>spriteGlyphAsset</c> assigned.
		/// Looked up once, under a Resources folder, same "exactly one" rule as <see cref="STextSettings"/> --
		/// but filtered to <see cref="SpriteGlyphAssetRole.Main"/>, so any <see cref="SpriteGlyphAssetRole.Module"/>
		/// assets that also happen to sit under a Resources folder don't trip the "multiple found" error.</summary>
		public static SpriteGlyphAsset GetDefault() {
			if (cachedDefault != null) return cachedDefault;
#if !UNITY_EDITOR
			if (triedLoadDefault) return cachedDefault;
			triedLoadDefault = true;
#endif
			cachedDefault = ProjectAssetResolver.FindSingle<SpriteGlyphAsset>(
				"SpriteGlyphAsset (Role = Main)", a => a.Role == SpriteGlyphAssetRole.Main);
			return cachedDefault;
		}

		/// <summary>Icon name lookup, matched case-insensitively (a <c>&lt;sprite="Dice"&gt;</c> tag or a
		/// GlyphActionRegistry icon binding typed with different casing than the source icon's own name still
		/// resolves).</summary>
		public bool TryGet(string name, out SpriteGlyphEntry entry) {
			entry = default;
			if (string.IsNullOrEmpty(name)) return false;
			if (lookup == null || lookup.Count != Entries.Count) RebuildLookup();
			if (lookup.TryGetValue(name, out int i)) {
				entry = Entries[i];
				return true;
			}
			return false;
		}

		private void RebuildLookup() {
			lookup = new Dictionary<string, int>(Entries.Count, System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < Entries.Count; i++) {
				if (!string.IsNullOrEmpty(Entries[i].Name)) lookup[Entries[i].Name] = i;
			}
		}

		private void OnValidate() => lookup = null;
	}
}
