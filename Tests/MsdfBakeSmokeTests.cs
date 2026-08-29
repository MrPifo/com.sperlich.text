#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using Sperlich.Text;
using Sperlich.Text.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Sperlich.Text.Tests {

	/// <summary>End-to-end smoke test for the in-memory MTSDF bake (no assets written).</summary>
	public class MsdfBakeSmokeTests {

		private static FontDefinition MakeComfortaaDef() {
			string[] hits = AssetDatabase.FindAssets("Comfortaa t:Font");
			Assert.Greater(hits.Length, 0, "Comfortaa font not found in project");
			Font font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(hits[0]));
			Assert.IsNotNull(font);

			FontDefinition def = ScriptableObject.CreateInstance<FontDefinition>();
			def.primary = font;
			def.msdfEmSize = 40;
			def.msdfPixelRange = 4f;
			def.msdfEdgeAngle = 3f;
			return def;
		}

		[Test]
		public void BakesAsciiInMemory() {
			FontDefinition def = MakeComfortaaDef();
			try {
				MsdfBakeParams p = MsdfBakeParams.From(def);
				p.charset = MsdfCharset.Ascii;
				p.extraChars = "";

				MsdfBakeResult res = MsdfBaker.BakeToMemory(def, p);
				Assert.IsTrue(res.ok, res.error);
				Assert.Greater(res.atlasSize, 0);
				Assert.AreEqual(res.atlasSize * res.atlasSize, res.pixels.Length);
				Assert.AreEqual(40f, res.emSize);
				Assert.AreEqual(40f, res.faces[0].metrics.SamplingPointSize, "emSize must match face SamplingPointSize");
				Assert.Greater(res.faces[0].metrics.LineHeight, 0f);

				// every printable ASCII code point resolved (Comfortaa covers Basic Latin)
				var byCp = new System.Collections.Generic.Dictionary<uint, MsdfFontData.GlyphRecord>();
				foreach (var g in res.glyphs) byCp[g.codepoint] = g;
				for (uint u = 0x20; u <= 0x7E; u++)
					Assert.IsTrue(byCp.ContainsKey(u), $"missing glyph U+{u:X4}");

				// space is blank + has advance; 'M' has ink and a non-degenerate rect
				Assert.AreEqual(0f, byCp[' '].width);
				Assert.Greater(byCp[' '].advance, 0f);
				Assert.Greater(byCp['M'].rectW, 0f);
				Assert.Greater(byCp['M'].rectH, 0f);
				Assert.Greater(byCp['M'].advance, 0f);

				// rects in bounds and mutually non-overlapping; channels valid bytes (implicit via Color32)
				var rects = new System.Collections.Generic.List<RectInt>();
				foreach (var g in res.glyphs) {
					if (g.rectW <= 0) continue;
					Assert.GreaterOrEqual(g.rectX, 0);
					Assert.GreaterOrEqual(g.rectY, 0);
					Assert.LessOrEqual(g.rectX + g.rectW, res.atlasSize);
					Assert.LessOrEqual(g.rectY + g.rectH, res.atlasSize);
					rects.Add(new RectInt((int) g.rectX, (int) g.rectY, (int) g.rectW, (int) g.rectH));
				}
				for (int i = 0; i < rects.Count; i++)
					for (int j = i + 1; j < rects.Count; j++)
						Assert.IsFalse(rects[i].Overlaps(rects[j]), $"atlas rects {i} and {j} overlap");
			} finally {
				Object.DestroyImmediate(def);
			}
		}
	}
}
#endif
