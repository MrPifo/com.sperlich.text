using UnityEngine;

namespace Sperlich.Text {

	/// <summary>One named icon inside a <see cref="SpriteGlyphAsset"/> atlas.</summary>
	[System.Serializable]
	public struct SpriteGlyphEntry {

		public string Name;

		/// <summary>Pixel rect inside <see cref="SpriteGlyphAsset.Atlas"/> (bottom-left origin, matches
		/// <see cref="GlyphData.AtlasRect"/> convention).</summary>
		public Rect PixelRect;

		/// <summary>Width / height. Drives the inline box's aspect so non-square icons don't stretch.</summary>
		public float Aspect => PixelRect.height > 0f ? PixelRect.width / PixelRect.height : 1f;
	}
}
