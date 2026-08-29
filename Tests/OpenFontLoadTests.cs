#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using Sperlich.Text.Rasterizer;
using UnityEditor;

namespace Sperlich.Text.Tests {

	/// <summary>
	/// Smoke test for the vendored <c>Typography.OpenFont</c> bridge (<see cref="FontOutlineSource"/>).
	/// Editor-only: it reads the raw <c>.ttf</c> bytes off disk, which is exactly what the bake does.
	/// </summary>
	public class OpenFontLoadTests {

		private static byte[] LoadComfortaaBytes() {
			string path = "Assets/com.sperlich.text/Fonts/Comfortaa.ttf";
			if (!File.Exists(path)) {
				string[] hits = AssetDatabase.FindAssets("Comfortaa t:Font");
				if (hits.Length > 0) path = AssetDatabase.GUIDToAssetPath(hits[0]);
			}
			Assert.IsTrue(File.Exists(path), $"Comfortaa.ttf not found (looked at '{path}')");
			return File.ReadAllBytes(path);
		}

		private static FontOutlineSource NewSource() => new FontOutlineSource(LoadComfortaaBytes());

		[Test]
		public void ParsesHeadMetrics() {
			using FontOutlineSource src = NewSource();
			Assert.Greater(src.UnitsPerEm, 0);
			Assert.Greater(src.Ascender, 0);
			Assert.Less(src.Descender, 0);
			Assert.IsFalse(src.IsCff, "Comfortaa is a TrueType (glyf) font");
		}

		[TestCase('G')]
		[TestCase('r')]
		[TestCase('M')]
		[TestCase('A')]
		[TestCase('g')]
		public void InkGlyphsHaveContours(char c) {
			using FontOutlineSource src = NewSource();
			Assert.IsTrue(src.TryGetOutline(c, out RawGlyphOutline o), $"no glyph for '{c}'");
			Assert.IsFalse(o.IsBlank, $"'{c}' produced no contours");
			Assert.Greater(o.AdvanceWidth, 0f);
			int segs = 0;
			foreach (RawContour ct in o.Contours) segs += ct.Segments.Count;
			Assert.Greater(segs, 2, $"'{c}' outline is degenerate");
		}

		[Test]
		public void SpaceIsBlankButValid() {
			using FontOutlineSource src = NewSource();
			Assert.IsTrue(src.TryGetOutline(' ', out RawGlyphOutline o));
			Assert.IsTrue(o.IsBlank);
			Assert.Greater(o.AdvanceWidth, 0f);
		}

		[Test]
		public void CompositeUmlautDecomposesToBasePlusDiaeresis() {
			using FontOutlineSource src = NewSource();
			Assert.IsTrue(src.TryGetOutline('u', out RawGlyphOutline baseU));
			Assert.IsTrue(src.TryGetOutline('ü', out RawGlyphOutline umlautU));
			Assert.IsFalse(umlautU.IsBlank);
			// 'ü' = 'u' contours + two dots -> strictly more closed contours than plain 'u'.
			Assert.Greater(umlautU.Contours.Count, baseU.Contours.Count,
				"composite 'ü' should decompose to more contours than 'u'");
		}

		[Test]
		public void MissingCodepointReportsFalse() {
			using FontOutlineSource src = NewSource();
			// A CJK ideograph Comfortaa does not cover.
			Assert.IsFalse(src.TryGetOutline(0x65E5, out _));
		}
	}
}
#endif
