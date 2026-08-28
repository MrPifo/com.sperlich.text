using System.Collections.Generic;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Authoring asset for a text style family: one primary <see cref="Font"/> plus an ordered
	/// fallback chain. No offline bake step; faces are loaded through <see cref="FontAccess"/> at runtime.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Font Definition", fileName = "FontDefinition")]
	public sealed class FontDefinition : ScriptableObject {

		[Header("Faces")]
		[Tooltip("Primary font. Must be an importable .ttf/.otf Font asset.")]
		public Font primary;

		[Tooltip("Tried in order when the primary font lacks a glyph. Last resort is the tofu box.")]
		public List<Font> fallbacks = new();

		[Header("Rasterizer")]
		[Tooltip("Point size the SDF atlas is sampled at. Higher = sharper small text, more atlas memory.")]
		[Range(24, 160)] public int samplingPointSize = 90;

		[Tooltip("SDF spread in pixels. Larger allows thicker outlines/glow before clipping.")]
		[Range(4, 32)] public int sdfPadding = 9;

		[Header("Atlas")]
		[Tooltip("Square atlas edge length in pixels.")]
		public int atlasSize = 2048;

		[Tooltip("Distance field kind. Only SDF is produced in v1; MTSDF is reserved for the native plugin path.")]
		public GlyphFieldKind fieldKind = GlyphFieldKind.SDF;

		/// <summary>Enumerates primary + fallbacks, skipping nulls, primary first.</summary>
		public IEnumerable<Font> EnumerateFaces() {
			if (primary != null) yield return primary;
			for (int i = 0; i < fallbacks.Count; i++) {
				if (fallbacks[i] != null && fallbacks[i] != primary) yield return fallbacks[i];
			}
		}
	}
}
