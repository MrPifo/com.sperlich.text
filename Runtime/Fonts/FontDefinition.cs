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

		/// <summary>Allowed square atlas edge lengths, in pixels. Fixed set so the value stays a
		/// power of two that the GPU and TMP are happy with.</summary>
		public enum AtlasResolution {
			_512 = 512,
			_1024 = 1024,
			_2048 = 2048,
			_4096 = 4096,
			_8192 = 8192
		}

		[Header("Atlas")]
		[Tooltip("Square atlas edge length in pixels.")]
		public AtlasResolution atlasResolution = AtlasResolution._2048;

		/// <summary>Square atlas edge length in pixels (from <see cref="atlasResolution"/>).</summary>
		public int atlasSize => (int)atlasResolution;

		[Tooltip("SDF: TMP dynamic atlas (any code point at runtime). MTSDF: a pre-baked atlas — sharper " +
			"corners, fixed charset. Run 'Bake MTSDF Atlas' in the inspector after choosing MTSDF.")]
		public GlyphFieldKind fieldKind = GlyphFieldKind.SDF;

		[Header("MTSDF bake (fieldKind = MTSDF)")]
		[Tooltip("Which characters to bake (standards-based, strictly nested presets). 'Extra characters' " +
			"are added on top. LatinExtended covers all European Latin-script languages + typographic " +
			"punctuation; pick Wgl4 only if you need Greek / Cyrillic / arrows / math.")]
		public MsdfCharset msdfCharset = MsdfCharset.LatinExtended;

		[Tooltip("Characters to bake in addition to the preset.")]
		public string msdfExtraChars = "";

		[Tooltip("EM size in pixels each glyph is generated at. 48 is a good default.")]
		[Range(24, 128)] public int msdfEmSize = 48;

		[Tooltip("msdfgen distance range in pixels — the anti-aliased edge band and how far the sharp/soft " +
			"field is exact. 6 is a good default; higher mainly helps very large outline widths. Does not " +
			"need to be large for <glow> — that is 'Msdf Glow Padding'.")]
		[Range(2, 24)] public float msdfPixelRange = 6f;

		[Tooltip("Extra transparent pixels baked around every glyph cell. This is the room the <glow> / " +
			"<shadow> blur has to spread into — raise it for a bigger, softer halo (12 = subtle, 24–40 = " +
			"strong neon). Independent of 'Msdf Pixel Range', so it does not affect edge sharpness or thin " +
			"stems. Costs atlas space. 0 = no glow room.")]
		[Range(0, 48)] public int msdfGlowPadding = 16;

		[Tooltip("Upper bound for the baked atlas; the baker steps up to here as needed.")]
		public AtlasResolution msdfMaxAtlas = AtlasResolution._2048;

		[Tooltip("Edge-coloring angle threshold in radians (~3 = 172°).")]
		[Range(0.5f, 3.14f)] public float msdfEdgeAngle = 3f;

		[Tooltip("Run the msdfgen error-correction pass. Turn OFF to check whether it is the source of " +
			"colour-fringe wedges on baked glyphs.")]
		public bool msdfErrorCorrection = true;

		[Tooltip("Run the scanline sign-correction pass (fixes self-intersecting / overlapping contours). " +
			"Turn OFF to check whether it is the source of a junction artefact.")]
		public bool msdfSignCorrection = true;

		[Tooltip("Aggressive error correction: check every texel, not just those next to an edge. " +
			"Reduces the faint seams where overlapping stroke pieces meet (geometric fonts). May very " +
			"slightly round genuine hairline corners. Raise 'Msdf Em Size' too for the crispest result.")]
		public bool msdfAggressiveErrorCorrection = false;

		[Tooltip("Merge overlapping / self-intersecting contours into clean outlines before baking " +
			"(pure-C# stand-in for msdfgen's Skia resolveShapeGeometry). Needed for geometric fonts " +
			"built from overlapping stroke pieces (Comfortaa, many display faces) — removes the internal " +
			"seams at stroke joins. Turn OFF only to compare or if it misbehaves on an unusual font.")]
		public bool msdfResolveOverlaps = true;

		/// <summary>The baked atlas + metrics, produced by the editor "Bake MTSDF Atlas" button.</summary>
		[SerializeField, HideInInspector] internal MsdfFontData bakedData;

		/// <summary>Whether a usable MTSDF bake exists for this definition.</summary>
		public bool HasBakedData => bakedData != null && bakedData.atlas != null;

		/// <summary>Enumerates primary + fallbacks, skipping nulls, primary first.</summary>
		public IEnumerable<Font> EnumerateFaces() {
			if (primary != null) yield return primary;
			for (int i = 0; i < fallbacks.Count; i++) {
				if (fallbacks[i] != null && fallbacks[i] != primary) yield return fallbacks[i];
			}
		}

#if UNITY_EDITOR
		private static bool s_rebuildQueued;

		/// <summary>
		/// Editor-only. The runtime <see cref="FontAccess"/> bakes sampling size / SDF padding / atlas
		/// size / face list when it is built and never re-reads this asset. So an inspector edit would
		/// otherwise do nothing to labels already on screen. Here we clamp the numeric fields and queue
		/// a one-shot rebuild of every live <see cref="SText"/>.
		/// </summary>
		private void OnValidate() {
			samplingPointSize = Mathf.Clamp(samplingPointSize, 24, 160);
			sdfPadding = Mathf.Clamp(sdfPadding, 4, 32);

			if (s_rebuildQueued) return;
			s_rebuildQueued = true;
			UnityEditor.EditorApplication.delayCall += RebuildLiveLabels;
		}

		private static void RebuildLiveLabels() {
			s_rebuildQueued = false;
			GlyphStoreRegistry.EditorPurgeAll();
			foreach (SText label in FindObjectsByType<SText>(
				         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
				label.EditorRebindFont();
			}
		}
#endif
	}
}
