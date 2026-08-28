using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Project-wide defaults for the text renderer. Optional: a component works without one, falling back
	/// to hard defaults. Put a copy named "SperlichTextSettings" under a Resources folder to auto-load it.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Settings", fileName = "SperlichTextSettings")]
	public sealed class SperlichTextSettings : ScriptableObject {

		[Header("Defaults")]
		public FontDefinition defaultFont;
		public float defaultFontSize = 32f;
		public Color defaultColor = Color.white;

		[Header("Glyph pipeline")]
		[Tooltip("Max glyphs rasterised per frame (amortised generation).")]
		[Range(1, 128)] public int glyphsPerFrame = 8;

		[Tooltip("Queue the printable ASCII + Latin-1 range on startup so first display has no pop-in.")]
		public bool prewarmLatin1 = true;

		[Header("Shader")]
		[Tooltip("Shader used by the runtime material. Defaults to \"Sperlich/Text SDF\".")]
		public Shader sdfShader;

		private static SperlichTextSettings cached;
		private static bool triedLoad;

		public static SperlichTextSettings GetOrDefault() {
			if (cached != null) return cached;
			if (!triedLoad) {
				triedLoad = true;
				cached = Resources.Load<SperlichTextSettings>("SperlichTextSettings");
			}
			return cached;
		}

		public Shader ResolveShader() => sdfShader != null ? sdfShader : Shader.Find("Sperlich/Text SDF");
	}
}
