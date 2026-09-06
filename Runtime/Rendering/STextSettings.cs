using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Project-wide defaults for the text renderer. Optional: a component works without one, falling back
	/// to hard defaults. Put a copy named "STextSettings" under a Resources folder to auto-load it.
	/// The package itself never ships one — only a project-owned copy is ever discovered, so there is
	/// no package-vs-project collision to worry about.
	/// </summary>
	[CreateAssetMenu(menuName = "Sperlich/Text/Settings", fileName = "STextSettings")]
	public sealed class STextSettings : ScriptableObject {

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
		[Tooltip("Optional override for the runtime material's shader. Leave empty — Shader.Find(\"Sperlich/Text SDF\") " +
			"is used automatically and ships with the package, so this field is only needed for a custom shader variant.")]
		public Shader sdfShader;

		[Header("Sprites")]
		[Tooltip("Optional override for the project's default SpriteGlyphAsset. Leave empty and SText auto-resolves " +
			"the one SpriteGlyphAsset with Role = Main in the project (same as before this field existed) -- set " +
			"this only if you want to pin a specific asset without relying on that auto-discovery.")]
		public SpriteGlyphAsset defaultSpriteAsset;

		[Header("Glyph Devices")]
		[Tooltip("Fallback for GlyphDeviceContext.ActiveDeviceId used only in the Editor, only while nothing has " +
			"called SetActiveDevice yet (e.g. testing in Play Mode before your own input-detection code runs). " +
			"Never used in a build -- there, an unset ActiveDeviceId genuinely means no device is known yet.")]
		public string editorDefaultDeviceId = "";

		private static STextSettings cached;
		private static bool triedLoad;

		/// <summary>
		/// Resolves the single project-owned settings asset, if any. Only ever looks under Resources
		/// folders (never a hardcoded package path), so a package copy can never shadow or collide with
		/// a project copy. If more than one is found, that is treated as a project configuration error:
		/// logs which paths were found and falls back to a deterministic pick instead of silently using
		/// whichever Unity happened to enumerate first.
		/// </summary>
		public static STextSettings GetOrDefault() {
			if (cached != null) return cached;
			if (triedLoad) return cached;
			triedLoad = true;
			cached = ProjectAssetResolver.FindSingle<STextSettings>("STextSettings");
			return cached;
		}

		public Shader ResolveShader() => sdfShader != null ? sdfShader : Shader.Find("Sperlich/Text SDF");

		/// <summary>The pinned <see cref="defaultSpriteAsset"/> if set, otherwise the project's auto-discovered
		/// Role = Main <see cref="SpriteGlyphAsset"/> (<see cref="SpriteGlyphAsset.GetDefault"/>).</summary>
		public SpriteGlyphAsset ResolveSpriteAsset() => defaultSpriteAsset != null ? defaultSpriteAsset : SpriteGlyphAsset.GetDefault();
	}
}
