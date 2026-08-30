using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Sperlich.Text.Rasterizer;
using UnityEditor;
using UnityEngine;
using Range = Sperlich.Text.Rasterizer.Range;
using Vector2 = Sperlich.Text.Rasterizer.Vector2;

namespace Sperlich.Text.EditorTools {

	public struct MsdfBakeParams {
		public MsdfCharset charset;
		public string extraChars;
		public int emSize;
		public float pixelRange;
		public int glowPadding;
		public int maxAtlas;
		public float edgeAngle;
		public bool errorCorrection;
		public bool aggressiveErrorCorrection;
		public bool signCorrection;
		public bool resolveOverlaps;

		public static MsdfBakeParams From(FontDefinition d) => new MsdfBakeParams {
			charset = d.msdfCharset,
			extraChars = d.msdfExtraChars,
			emSize = d.msdfEmSize,
			pixelRange = d.msdfPixelRange,
			glowPadding = d.msdfGlowPadding,
			maxAtlas = (int) d.msdfMaxAtlas,
			edgeAngle = d.msdfEdgeAngle,
			errorCorrection = d.msdfErrorCorrection,
			aggressiveErrorCorrection = d.msdfAggressiveErrorCorrection,
			signCorrection = d.msdfSignCorrection,
			resolveOverlaps = d.msdfResolveOverlaps
		};
	}

	public sealed class MsdfBakeResult {
		public bool ok;
		public string error;
		public int atlasSize;
		public Color32[] pixels;                          // RGBA32, row 0 = bottom
		public float emSize;
		public float pixelRange;
		public readonly List<MsdfFontData.FaceRecord> faces = new();
		public readonly List<MsdfFontData.GlyphRecord> glyphs = new();
		public readonly List<uint> dropped = new();
		public float occupancy;
	}

	/// <summary>
	/// Editor-time MTSDF atlas baker: parses the fonts (Typography.OpenFont), generates a per-glyph
	/// MTSDF via the msdfgen port, packs with <see cref="ShelfPacker"/>, and writes the atlas +
	/// <see cref="MsdfFontData"/> as sub-assets of the <see cref="FontDefinition"/>.
	/// </summary>
	public static class MsdfBaker {

		private sealed class Pending {
			public int face;
			public uint codepoint;
			public uint glyphIndex;
			public float advance, bearingX, bearingY;
			public Shape shape;
			public int wCell, hCell, margin;
			public FloatBitmap generated;
		}

		public static MsdfBakeResult BakeToMemory(FontDefinition def, MsdfBakeParams p) {
			MsdfBakeResult res = new MsdfBakeResult { emSize = p.emSize, pixelRange = p.pixelRange };
			Rasterizer.MsdfGenerator.SignCorrectionEnabled = p.signCorrection;

			// -- collect faces ------------------------------------------------------------------------
			List<Font> faceFonts = new List<Font>();
			foreach (Font f in def.EnumerateFaces()) faceFonts.Add(f);
			if (faceFonts.Count == 0) { res.error = "FontDefinition has no faces."; return res; }

			List<FontOutlineSource> sources = new List<FontOutlineSource>();
			foreach (Font f in faceFonts) {
				string path = AssetDatabase.GetAssetPath(f);
				if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
					res.error = $"Face '{f?.name}' is not an on-disk .ttf/.otf asset (path='{path}').";
					return res;
				}
				try {
					FontOutlineSource src = new FontOutlineSource(File.ReadAllBytes(path));
					sources.Add(src);
					res.faces.Add(new MsdfFontData.FaceRecord { assetPath = path, metrics = MetricsFor(src, p.emSize) });
				} catch (Exception e) {
					res.error = $"Failed to parse '{path}': {e.Message}";
					return res;
				}
			}

			// -- resolve which (face, codepoint) pairs to bake --------------------------------------
			List<uint> codepoints = new List<uint>(MsdfCharsetPresets.CodePoints(p.charset, p.extraChars));
			HashSet<uint> covered = new HashSet<uint>();
			List<Pending> pendings = new List<Pending>();

			for (int fi = 0; fi < sources.Count; fi++) {
				FontOutlineSource src = sources[fi];
				float s = (float) p.emSize / Mathf.Max(1, src.UnitsPerEm); // font units -> em pixels (float divide!)
				// field half-range for the exact SDF, plus transparent room for the <glow>/<shadow> blur
				int margin = Mathf.CeilToInt(p.pixelRange * 0.5f) + 1 + Mathf.Max(0, p.glowPadding);

				foreach (uint cp in codepoints) {
					if (covered.Contains(cp)) continue;
					if (!src.TryGetOutline(cp, out RawGlyphOutline raw)) continue; // face lacks it
					covered.Add(cp);

					float advance = raw.AdvanceWidth * s;
					float bearingX = raw.LeftSideBearing * s;

					if (raw.IsBlank) {
						res.glyphs.Add(new MsdfFontData.GlyphRecord {
							face = fi, codepoint = cp, glyphIndex = raw.GlyphIndex,
							advance = advance, width = 0, height = 0, bearingX = bearingX, bearingY = 0,
							rectX = 0, rectY = 0, rectW = 0, rectH = 0, padding = 0
						});
						continue;
					}

					Shape shape = GlyphShapeBuilder.Build(raw, reorient: false, resolveOverlaps: p.resolveOverlaps);
					if (shape.Contours.Count == 0) {
						res.glyphs.Add(new MsdfFontData.GlyphRecord {
							face = fi, codepoint = cp, glyphIndex = raw.GlyphIndex,
							advance = advance, width = 0, height = 0, bearingX = bearingX, bearingY = 0
						});
						continue;
					}
					EdgeColoring.EdgeColoringSimple(shape, p.edgeAngle);

					Shape.Bounds bb = shape.GetBounds();
					float glyphWpx = (float) (bb.r - bb.l) * s;
					float glyphHpx = (float) (bb.t - bb.b) * s;

					pendings.Add(new Pending {
						face = fi, codepoint = cp, glyphIndex = raw.GlyphIndex,
						advance = advance,
						bearingX = (float) bb.l * s,   // ink left edge in em-px units
						bearingY = (float) bb.t * s,   // ink top edge above baseline
						shape = shape,
						margin = margin,
						wCell = Mathf.CeilToInt(glyphWpx) + 2 * margin,
						hCell = Mathf.CeilToInt(glyphHpx) + 2 * margin,
						// projection origin stashed on the shape via GetBounds re-read at gen time
					});
				}
			}

			foreach (uint cp in codepoints)
				if (!covered.Contains(cp)) res.dropped.Add(cp);

			// -- pack ------------------------------------------------------------------------------
			pendings.Sort((a, b) => b.hCell.CompareTo(a.hCell));
			int[] sizes = { 256, 512, 1024, 2048, 4096, 8192 };
			int largestAllowed = 256;
			foreach (int sz in sizes) if (sz <= p.maxAtlas) largestAllowed = sz;

			ShelfPacker packer = null;
			int chosen = 0;
			int[] placeX = null, placeY = null;
			foreach (int sz in sizes) {
				if (sz > p.maxAtlas) break;
				packer = new ShelfPacker(sz, sz, 1);
				placeX = new int[pendings.Count];
				placeY = new int[pendings.Count];
				bool allFit = true;
				for (int i = 0; i < pendings.Count; i++) {
					if (packer.TryInsert(pendings[i].wCell, pendings[i].hCell, out int px, out int py)) {
						placeX[i] = px; placeY[i] = py;
					} else {
						placeX[i] = -1; placeY[i] = -1;
						allFit = false;
					}
				}
				chosen = sz;
				if (allFit || sz == largestAllowed) break;
			}

			res.atlasSize = chosen;
			res.occupancy = packer != null ? packer.Occupancy : 0f;
			res.pixels = new Color32[chosen * chosen]; // zero-initialised (transparent black)

			// -- generate + blit (multi-threaded across all CPU cores) ---------------------------
			MsdfFontData.GlyphRecord?[] glyphRecords = new MsdfFontData.GlyphRecord?[pendings.Count];

			Parallel.For(0, pendings.Count, i => {
				Pending pd = pendings[i];
				if (placeX[i] < 0) return;

				FontOutlineSource src = sources[pd.face];
				float s = (float) p.emSize / Mathf.Max(1, src.UnitsPerEm); // font units -> em pixels (float divide!)
				Shape.Bounds bb = pd.shape.GetBounds();

				FloatBitmap bmp = new FloatBitmap(pd.wCell, pd.hCell, 4);
				Projection projection = new Projection(
					new Vector2(s, s),
					new Vector2(-bb.l + pd.margin / s, -bb.b + pd.margin / s));
				Range range = new Range(p.pixelRange / s); // full range width in font units
				SDFTransformation t = new SDFTransformation(projection, DistanceMapping.FromRange(range));
				ErrorCorrectionConfig ec;
				if (!p.errorCorrection) {
					ec = new ErrorCorrectionConfig { mode = ErrorCorrectionConfig.Mode.Disabled };
				} else if (p.aggressiveErrorCorrection) {
					ec = ErrorCorrectionConfig.Default;
					ec.mode = ErrorCorrectionConfig.Mode.Indiscriminate;
					ec.distanceCheckMode = ErrorCorrectionConfig.DistanceCheckMode.AlwaysCheckDistance;
				} else {
					ec = ErrorCorrectionConfig.Default;
				}
				MsdfGenerator.GenerateMTSDF(bmp, pd.shape, t, new MSDFGeneratorConfig(true, ec));

				int sx = placeX[i];
				int bottomY = chosen - placeY[i] - pd.hCell;
				for (int cy = 0; cy < pd.hCell; cy++) {
					int atlasRow = (bottomY + cy) * chosen;
					int srcRow = bmp.PixelBase(0, cy);
					for (int cx = 0; cx < pd.wCell; cx++) {
						int sidx = srcRow + cx * 4;
						res.pixels[atlasRow + sx + cx] = new Color32(
							ToByte(bmp.Data[sidx + 0]), ToByte(bmp.Data[sidx + 1]),
							ToByte(bmp.Data[sidx + 2]), ToByte(bmp.Data[sidx + 3]));
					}
				}

				glyphRecords[i] = new MsdfFontData.GlyphRecord {
					face = pd.face, codepoint = pd.codepoint, glyphIndex = pd.glyphIndex,
					advance = pd.advance,
					width = (float) (bb.r - bb.l) * s,
					height = (float) (bb.t - bb.b) * s,
					bearingX = pd.bearingX,
					bearingY = pd.bearingY,
					rectX = sx, rectY = bottomY, rectW = pd.wCell, rectH = pd.hCell,
					padding = pd.margin
				};
			});

			for (int i = 0; i < pendings.Count; i++) {
				if (placeX[i] < 0) {
					res.dropped.Add(pendings[i].codepoint);
				} else if (glyphRecords[i].HasValue) {
					res.glyphs.Add(glyphRecords[i].Value);
				}
			}

			foreach (FontOutlineSource src in sources) src.Dispose();

			res.ok = string.IsNullOrEmpty(res.error);
			return res;
		}

		[MenuItem("Assets/Sperlich Text/Bake MTSDF Atlas", true)]
		private static bool BakeSelectedValidate() => Selection.activeObject is FontDefinition;

		[MenuItem("Assets/Sperlich Text/Bake MTSDF Atlas", false, 2000)]
		private static void BakeSelected() => BakeAsset((FontDefinition) Selection.activeObject);

		public static void BakeAsset(FontDefinition def) {
			MsdfBakeParams p = MsdfBakeParams.From(def);
			EditorUtility.DisplayProgressBar("Bake MTSDF Atlas", $"Baking '{def.name}'…", 0.1f);
			MsdfBakeResult res;
			try {
				res = BakeToMemory(def, p);
			} finally {
				EditorUtility.ClearProgressBar();
			}

			if (!res.ok) {
				Debug.LogError($"[SperlichText] MTSDF bake failed: {res.error}", def);
				return;
			}

			Texture2D tex = new Texture2D(res.atlasSize, res.atlasSize, TextureFormat.RGBA32, false, true) {
				name = def.name + " MTSDF Atlas",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.None
			};
			tex.SetPixels32(res.pixels);
			tex.Apply(false, false);

			MsdfFontData data = ScriptableObject.CreateInstance<MsdfFontData>();
			data.name = def.name + " MTSDF Data";
			data.atlas = tex;
			data.atlasSize = res.atlasSize;
			data.pixelRange = res.pixelRange;
			data.emSize = res.emSize;
			data.fieldKind = GlyphFieldKind.MTSDF;
			data.sourceHash = ComputeHash(def, p);
			data.faces = res.faces.ToArray();
			data.glyphs = res.glyphs.ToArray();
			data.kerning = Array.Empty<MsdfFontData.KerningRecord>();
			data.InvalidateLookups();

			// Remove EVERY previous bake sub-asset (not just the one bakedData points at — stale ones
			// accumulate otherwise and a cached store can keep rendering an old atlas).
			string defPath = AssetDatabase.GetAssetPath(def);
			foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(defPath)) {
				if (sub is MsdfFontData || (sub is Texture2D t && t.name.EndsWith("MTSDF Atlas"))) {
					AssetDatabase.RemoveObjectFromAsset(sub);
					UnityEngine.Object.DestroyImmediate(sub, true);
				}
			}

			SerializedObject so = new SerializedObject(def);
			SerializedProperty prop = so.FindProperty("bakedData");
			AssetDatabase.AddObjectToAsset(tex, def);
			AssetDatabase.AddObjectToAsset(data, def);
			prop.objectReferenceValue = data;
			so.ApplyModifiedPropertiesWithoutUndo();

			EditorUtility.SetDirty(def);
			AssetDatabase.SaveAssets();
			AssetDatabase.ImportAsset(defPath);

			// Drop cached GlyphStores (they wrap the now-replaced MsdfFontData) and rebind live labels,
			// otherwise the scene keeps rendering the previous bake.
			GlyphStoreRegistry.EditorPurgeAll();
			foreach (SperlichText label in UnityEngine.Object.FindObjectsByType<SperlichText>(
				         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
				label.EditorRebindFont();
			}

			int dropped = res.dropped.Count;
			Debug.Log($"[SperlichText] Baked MTSDF for '{def.name}': {res.glyphs.Count} glyphs, " +
				$"atlas {res.atlasSize}px, occupancy {res.occupancy:P0}" +
				(dropped > 0 ? $", {dropped} code points dropped (atlas full at max size)" : "") + ".", def);
		}

		// -- helpers -------------------------------------------------------------------------------

		private static byte ToByte(float v) => (byte) Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

		private static FaceMetrics MetricsFor(FontOutlineSource src, float emSize) {
			float s = emSize / Mathf.Max(1, src.UnitsPerEm);
			float ascent = src.Ascender * s;
			float descent = src.Descender * s; // negative
			float cap = src.CapHeight > 0 ? src.CapHeight * s : ascent * 0.72f;
			float xh = src.XHeight > 0 ? src.XHeight * s : ascent * 0.52f;
			float ulPos = src.UnderlinePosition * s;
			float stPos = src.StrikeoutPosition != 0 ? src.StrikeoutPosition * s : cap * 0.4f;
			float stSize = src.StrikeoutSize > 0 ? src.StrikeoutSize * s : Mathf.Max(1f, emSize * 0.06f);
			return new FaceMetrics {
				SamplingPointSize = emSize,
				Scale = 1f,
				LineHeight = (src.Ascender - src.Descender + src.LineGap) * s,
				AscentLine = ascent,
				CapLine = cap,
				MeanLine = xh,
				Baseline = 0f,
				DescentLine = descent,
				UnderlineOffset = ulPos != 0 ? ulPos : -emSize * 0.1f,
				UnderlineThickness = Mathf.Max(1f, emSize * 0.06f),
				StrikethroughOffset = stPos,
				StrikethroughThickness = stSize,
				SuperscriptOffset = 0f,
				SuperscriptSize = 0.5f,
				SubscriptOffset = 0f,
				SubscriptSize = 0.5f,
				TabWidth = emSize * 2f
			};
		}

		public static string ComputeHash(FontDefinition def, MsdfBakeParams p) {
			StringBuilder sb = new StringBuilder();
			foreach (Font f in def.EnumerateFaces()) {
				string path = AssetDatabase.GetAssetPath(f);
				sb.Append(AssetDatabase.AssetPathToGUID(path)).Append(':');
				try { sb.Append(File.GetLastWriteTimeUtc(path).Ticks); } catch { sb.Append('0'); }
				sb.Append('|');
			}
			sb.Append(p.charset).Append(',').Append(p.extraChars).Append(',').Append(p.emSize).Append(',')
			  .Append(p.pixelRange).Append(',').Append(p.glowPadding).Append(',').Append(p.maxAtlas).Append(',').Append(p.edgeAngle)
			  .Append(',').Append(p.errorCorrection).Append(',').Append(p.aggressiveErrorCorrection)
			  .Append(',').Append(p.signCorrection).Append(',').Append(p.resolveOverlaps);
			return sb.ToString();
		}
	}
}
