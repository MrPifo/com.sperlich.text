using System.IO;
using Sperlich.Text.Rasterizer;
using UnityEditor;
using UnityEngine;
using Range = Sperlich.Text.Rasterizer.Range;
using RVector2 = Sperlich.Text.Rasterizer.Vector2;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Diagnostic: bakes a handful of glyphs one at a time at a big EM size and writes each one's
	/// MTSDF to PNGs on disk (RGB = the multi-channel field, plus the alpha / true-SDF as greyscale).
	/// Nothing here touches the real bake pipeline — it is only for eyeballing the raw generator output.
	/// </summary>
	public static class MsdfDebugDump {

		private const int EmSize = 42;      // match the real bake so artefacts show at the same scale
		private const float PixelRange = 5f;
		private const string Glyphs = "htkx483fgn";

		[MenuItem("Assets/Sperlich Text/Debug/Dump Glyph MTSDF PNGs", true)]
		private static bool Validate() => Selection.activeObject is FontDefinition;

		[MenuItem("Assets/Sperlich Text/Debug/Dump Glyph MTSDF PNGs", false, 2100)]
		private static void Dump() {
			FontDefinition def = (FontDefinition) Selection.activeObject;
			if (def.primary == null) { Debug.LogError("No primary font."); return; }

			string fontPath = AssetDatabase.GetAssetPath(def.primary);
			byte[] bytes = File.ReadAllBytes(fontPath);
			FontOutlineSource src = new FontOutlineSource(bytes);

			string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MtsdfDebug");
			Directory.CreateDirectory(outDir);

			float s = (float) EmSize / Mathf.Max(1, src.UnitsPerEm);
			int margin = Mathf.CeilToInt(PixelRange * 0.5f) + 2;

			var log = new System.Text.StringBuilder();
			log.AppendLine($"font='{def.primary.name}' upem={src.UnitsPerEm} isCff={src.IsCff} s={s:F5} margin={margin}");
			log.AppendLine("---- raw glyf structure ----");
			foreach (char ch in "htkx483fg") log.AppendLine(src.DebugDescribe(ch));
			log.AppendLine("---- shape build ----");

			foreach (char ch in Glyphs) {
				if (!src.TryGetOutline(ch, out RawGlyphOutline raw)) { log.AppendLine($"'{ch}': no glyph"); continue; }
				if (raw.IsBlank) { log.AppendLine($"'{ch}': blank"); continue; }

				Shape rawShape = GlyphShapeBuilder.Build(raw, reorient: false, resolveOverlaps: false);
				log.Append($"'{ch}': RAWwind=[");
				for (int i = 0; i < rawShape.Contours.Count; i++) log.Append(rawShape.Contours[i].Winding() + (i + 1 < rawShape.Contours.Count ? "," : ""));
				log.Append("] ");

				// PNGs use the resolved shape == the real bake path.
				Shape shape = GlyphShapeBuilder.Build(raw, resolveOverlaps: def.msdfResolveOverlaps);
				log.Append($"[{ShapeResolver.LastNote}] ");
				log.Append($"rawContours={raw.Contours.Count} resolvedContours={shape.Contours.Count} valid={shape.Validate()} REwind=[");
				for (int i = 0; i < shape.Contours.Count; i++) log.Append(shape.Contours[i].Winding() + (i + 1 < shape.Contours.Count ? "," : ""));
				log.Append("] gaps=[");
				for (int i = 0; i < shape.Contours.Count; i++) {
					var ed = shape.Contours[i].Edges;
					double gap = ed.Count == 0 ? 0 : (ed[ed.Count - 1].Point(1) - ed[0].Point(0)).Length;
					log.Append($"{gap:F3}" + (i + 1 < shape.Contours.Count ? "," : ""));
				}
				log.Append("] ");

				EdgeColoring.EdgeColoringSimple(shape, def.msdfEdgeAngle <= 0 ? 3f : def.msdfEdgeAngle);

				Shape.Bounds bb = shape.GetBounds();
				log.AppendLine($"bounds=({bb.l:F1},{bb.b:F1})-({bb.r:F1},{bb.t:F1})");

				int w = Mathf.CeilToInt((float) (bb.r - bb.l) * s) + 2 * margin;
				int h = Mathf.CeilToInt((float) (bb.t - bb.b) * s) + 2 * margin;
				if (w < 4 || h < 4 || w > 2048 || h > 2048) { log.AppendLine($"  bad size {w}x{h}"); continue; }

				FloatBitmap bmp = new FloatBitmap(w, h, 4);
				Projection projection = new Projection(
					new RVector2(s, s),
					new RVector2(-bb.l + margin / s, -bb.b + margin / s));
				SDFTransformation t = new SDFTransformation(projection, DistanceMapping.FromRange(new Range(PixelRange / s)));
				ErrorCorrectionConfig ec = def.msdfErrorCorrection
					? ErrorCorrectionConfig.Default
					: new ErrorCorrectionConfig { mode = ErrorCorrectionConfig.Mode.Disabled };
				MsdfGenerator.GenerateMTSDF(bmp, shape, t, new MSDFGeneratorConfig(true, ec));

				WritePng(bmp, w, h, false, Path.Combine(outDir, $"glyph_{(int) ch:X2}_{Safe(ch)}_rgb.png"));
				WritePng(bmp, w, h, true, Path.Combine(outDir, $"glyph_{(int) ch:X2}_{Safe(ch)}_alpha.png"));
			}

			src.Dispose();
			File.WriteAllText(Path.Combine(outDir, "dump.txt"), log.ToString());
			Debug.Log($"[MsdfDebugDump] wrote PNGs + dump.txt to:\n{outDir}\n\n{log}");
			EditorUtility.RevealInFinder(outDir);
		}

		[MenuItem("Assets/Sperlich Text/Debug/Export Baked Atlas PNG", true)]
		private static bool ValidateAtlas() => Selection.activeObject is FontDefinition;

		[MenuItem("Assets/Sperlich Text/Debug/Export Baked Atlas PNG", false, 2101)]
		private static void ExportAtlas() {
			FontDefinition def = (FontDefinition) Selection.activeObject;
			SerializedObject so = new SerializedObject(def);
			MsdfFontData data = so.FindProperty("bakedData").objectReferenceValue as MsdfFontData;
			if (data == null || data.atlas == null) { Debug.LogError("No baked atlas. Bake first."); return; }

			Texture2D atl = data.atlas;
			// Force a CPU-readable copy regardless of import/readable flags.
			RenderTexture rt = RenderTexture.GetTemporary(atl.width, atl.height, 0, RenderTextureFormat.ARGB32,
				RenderTextureReadWrite.Linear);
			Graphics.Blit(atl, rt);
			RenderTexture prev = RenderTexture.active;
			RenderTexture.active = rt;
			Texture2D copy = new Texture2D(atl.width, atl.height, TextureFormat.RGBA32, false, true);
			copy.ReadPixels(new Rect(0, 0, atl.width, atl.height), 0, 0);
			copy.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);

			string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MtsdfDebug");
			Directory.CreateDirectory(outDir);
			string rgbPath = Path.Combine(outDir, "ATLAS_rgb.png");
			File.WriteAllBytes(rgbPath, copy.EncodeToPNG());

			// keep the untouched atlas pixels for per-cell cropping (row 0 = bottom == GlyphRecord.rectY)
			Color32[] full = copy.GetPixels32();
			int W = atl.width, H = atl.height;

			// alpha (true SDF) as greyscale
			Color32[] p = (Color32[]) full.Clone();
			for (int i = 0; i < p.Length; i++) { byte a = p[i].a; p[i] = new Color32(a, a, a, 255); }
			copy.SetPixels32(p);
			copy.Apply();
			File.WriteAllBytes(Path.Combine(outDir, "ATLAS_alpha.png"), copy.EncodeToPNG());
			Object.DestroyImmediate(copy);

			var log = new System.Text.StringBuilder();
			log.AppendLine($"atlas {data.atlasSize}px  pixelRange={data.pixelRange}  emSize={data.emSize}  glyphs={data.glyphs.Length}");
			log.AppendLine($"PROJECT colorSpace = {QualitySettings.activeColorSpace}");
			log.AppendLine($"atlas graphicsFormat = {atl.graphicsFormat}  isDataSRGB = {atl.isDataSRGB}  mipCount = {atl.mipmapCount}  filter = {atl.filterMode}  wrap = {atl.wrapMode}");
			// raw byte vs colour-converted read, at a known 'inside' and 'background' texel of 'M'
			if (data.TryGetGlyph('M', out MsdfFontData.GlyphRecord gm)) {
				try {
					var raw = atl.GetRawTextureData<byte>();
					int mx = Mathf.RoundToInt(gm.rectX + gm.rectW * 0.5f);
					int my = Mathf.RoundToInt(gm.rectY + gm.rectH * 0.5f);
					int bg_x = Mathf.RoundToInt(gm.rectX + 1);
					int bg_y = Mathf.RoundToInt(gm.rectY + 1);
					int idxIn = (my * atl.width + mx) * 4;
					int idxBg = (bg_y * atl.width + bg_x) * 4;
					log.AppendLine($"'M' inside texel raw bytes  = ({raw[idxIn]},{raw[idxIn + 1]},{raw[idxIn + 2]},{raw[idxIn + 3]})");
					log.AppendLine($"'M' corner texel raw bytes  = ({raw[idxBg]},{raw[idxBg + 1]},{raw[idxBg + 2]},{raw[idxBg + 3]})");
				} catch (System.Exception e) { log.AppendLine("raw read failed: " + e.Message); }
			}

			foreach (char ch in "htnkbdMReg") {
				if (!data.TryGetGlyph(ch, out MsdfFontData.GlyphRecord g)) { log.AppendLine($"'{ch}' NOT BAKED"); continue; }
				log.AppendLine($"'{ch}' U+{(int) ch:X4}: rect=({g.rectX},{g.rectY},{g.rectW},{g.rectH}) bearing=({g.bearingX:F1},{g.bearingY:F1}) size=({g.width:F1}x{g.height:F1}) adv={g.advance:F1} pad={g.padding}");
				// crop this glyph's cell straight out of the atlas
				int rx = Mathf.RoundToInt(g.rectX), ry = Mathf.RoundToInt(g.rectY);
				int rw = Mathf.RoundToInt(g.rectW), rh = Mathf.RoundToInt(g.rectH);
				if (rw <= 0 || rh <= 0 || rx < 0 || ry < 0 || rx + rw > W || ry + rh > H) { log.AppendLine("   (rect out of atlas bounds!)"); continue; }
				Texture2D crop = new Texture2D(rw, rh, TextureFormat.RGBA32, false, true);
				Color32[] cp = new Color32[rw * rh];
				for (int yy = 0; yy < rh; yy++)
					for (int xx = 0; xx < rw; xx++)
						cp[yy * rw + xx] = full[(ry + yy) * W + (rx + xx)];
				crop.SetPixels32(cp);
				crop.Apply();
				File.WriteAllBytes(Path.Combine(outDir, $"cell_{(int) ch:X2}_{Safe(ch)}_rgb.png"), crop.EncodeToPNG());
				for (int i = 0; i < cp.Length; i++) { byte a = cp[i].a; cp[i] = new Color32(a, a, a, 255); }
				crop.SetPixels32(cp); crop.Apply();
				File.WriteAllBytes(Path.Combine(outDir, $"cell_{(int) ch:X2}_{Safe(ch)}_alpha.png"), crop.EncodeToPNG());
				Object.DestroyImmediate(crop);
			}

			// brute-force overlap scan across every baked rect
			int overlaps = 0;
			for (int i = 0; i < data.glyphs.Length && overlaps < 20; i++) {
				var a = data.glyphs[i];
				if (a.rectW <= 0) continue;
				for (int j = i + 1; j < data.glyphs.Length && overlaps < 20; j++) {
					var b = data.glyphs[j];
					if (b.rectW <= 0) continue;
					bool ov = a.rectX < b.rectX + b.rectW && b.rectX < a.rectX + a.rectW &&
					          a.rectY < b.rectY + b.rectH && b.rectY < a.rectY + a.rectH;
					if (ov) { log.AppendLine($"OVERLAP: U+{a.codepoint:X4} ({a.rectX},{a.rectY},{a.rectW},{a.rectH}) vs U+{b.codepoint:X4} ({b.rectX},{b.rectY},{b.rectW},{b.rectH})"); overlaps++; }
				}
			}
			log.AppendLine(overlaps == 0 ? "no rect overlaps" : $"{overlaps}+ overlaps found");
			File.WriteAllText(Path.Combine(outDir, "atlas_glyphs.txt"), log.ToString());
			Debug.Log($"[MsdfDebugDump] atlas exported to {outDir}\n\n{log}");
			EditorUtility.RevealInFinder(rgbPath);
		}

		[MenuItem("Assets/Sperlich Text/Debug/Dump Resolved GlyphData", true)]
		private static bool ValidateResolved() => Selection.activeObject is FontDefinition;

		[MenuItem("Assets/Sperlich Text/Debug/Dump Resolved GlyphData", false, 2102)]
		private static void DumpResolved() {
			FontDefinition def = (FontDefinition) Selection.activeObject;
			GlyphStore store = GlyphStoreRegistry.Acquire(def);
			try {
				var log = new System.Text.StringBuilder();
				log.AppendLine($"backend={store.Fonts.GetType().Name} atlasSize={store.AtlasSize} distanceRange={store.DistanceRange} " +
					$"primMetrics.sampling={store.Fonts.PrimaryMetrics.SamplingPointSize} valid={store.Fonts.PrimaryMetrics.IsValid}");
				store.PrewarmAscii();
				store.ProcessQueue(9999);
				foreach (char ch in "abcdefghijklmnopqrstuvwxyz") {
					GlyphData g = store.GetOrRequest(ch);
					log.AppendLine($"'{ch}' face={g.FaceIndex} resolved={g.IsResolved} ws={g.IsWhitespace} " +
						$"rect=({g.AtlasRect.x},{g.AtlasRect.y},{g.AtlasRect.z},{g.AtlasRect.w}) " +
						$"bearing=({g.Bearing.x:F1},{g.Bearing.y:F1}) size=({g.Size.x:F1},{g.Size.y:F1}) pad={g.Padding} adv={g.Advance:F1}");
				}
				string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MtsdfDebug");
				Directory.CreateDirectory(outDir);
				File.WriteAllText(Path.Combine(outDir, "resolved.txt"), log.ToString());
				Debug.Log("[MsdfDebugDump] resolved GlyphData:\n\n" + log);
			} finally {
				GlyphStoreRegistry.Release(def);
			}
		}

		private static string Safe(char c) => char.IsLetterOrDigit(c) ? c.ToString() : "x";

		private static void WritePng(FloatBitmap bmp, int w, int h, bool alphaAsGrey, string path) {
			Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
			Color32[] px = new Color32[w * h];
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					int b = bmp.PixelBase(x, y);
					// FloatBitmap row 0 = bottom; PNG/Texture2D row 0 = bottom too -> no flip needed here.
					int outIdx = y * w + x;
					if (alphaAsGrey) {
						byte a = To8(bmp.Data[b + 3]);
						px[outIdx] = new Color32(a, a, a, 255);
					} else {
						px[outIdx] = new Color32(To8(bmp.Data[b + 0]), To8(bmp.Data[b + 1]), To8(bmp.Data[b + 2]), 255);
					}
				}
			}
			tex.SetPixels32(px);
			tex.Apply();
			File.WriteAllBytes(path, tex.EncodeToPNG());
			Object.DestroyImmediate(tex);
		}

		private static byte To8(float v) => (byte) Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
	}
}
