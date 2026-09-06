using NUnit.Framework;
using Sperlich.Text;

namespace Sperlich.Text.Tests {

	public class MarkupParserTests {

		private readonly MarkupParser parser = new();

		[Test]
		public void PlainTextIsOneSpan() {
			MarkupResult r = parser.Parse("Hello world");
			Assert.AreEqual("Hello world", r.Text);
			Assert.AreEqual(1, r.Spans.Count);
			Assert.AreEqual(11, r.Spans[0].Length);
		}

		[Test]
		public void ColorTagStripsAndStyles() {
			MarkupResult r = parser.Parse("a<color=#ff0000>b</color>c");
			Assert.AreEqual("abc", r.Text);
			int bIndex = 1;
			StyleSpan span = FindSpanFor(r, bIndex);
			Assert.AreEqual(1f, span.Style.Color.x, 1e-3f);
			Assert.AreEqual(0f, span.Style.Color.y, 1e-3f);
		}

		[Test]
		public void NestedTagsPopCorrectly() {
			MarkupResult r = parser.Parse("<b>x<i>y</i>z</b>");
			Assert.AreEqual("xyz", r.Text);
			Assert.IsTrue((FindSpanFor(r, 0).Style.Synthesis & FontSynthesis.Bold) != 0);
			Assert.IsTrue((FindSpanFor(r, 1).Style.Synthesis & FontSynthesis.Italic) != 0);
			Assert.IsTrue((FindSpanFor(r, 1).Style.Synthesis & FontSynthesis.Bold) != 0);
			Assert.IsTrue((FindSpanFor(r, 2).Style.Synthesis & FontSynthesis.Italic) == 0);
		}

		[Test]
		public void LinkRegionRecordsRange() {
			MarkupResult r = parser.Parse("see <link=\"rules\">the rules</link> now");
			Assert.AreEqual("see the rules now", r.Text);
			Assert.AreEqual(1, r.Links.Count);
			Assert.AreEqual("rules", r.Links[0].Id);
			Assert.AreEqual(4, r.Links[0].Start);
			Assert.AreEqual(9, r.Links[0].Length);
		}

		[Test]
		public void SizePercentBecomesMultiplier() {
			MarkupResult r = parser.Parse("<size=150%>big</size>");
			Assert.AreEqual("big", r.Text);
			Assert.AreEqual(1.5f, FindSpanFor(r, 0).Style.SizeMultiplier, 1e-3f);
		}

		[Test]
		public void AbsoluteSizeSetsPixels() {
			MarkupResult r = parser.Parse("<size=48>x</size>");
			Assert.AreEqual(48f, FindSpanFor(r, 0).Style.AbsoluteSizePx, 1e-3f);
		}

		[Test]
		public void SpriteInsertsPlaceholderChar() {
			MarkupResult r = parser.Parse("hp <sprite=\"heart\"> full");
			Assert.AreEqual(1, r.Inserts.Count);
			Assert.AreEqual("heart", r.Inserts[0].Name);
			Assert.IsFalse(r.Inserts[0].IsActionGlyph);
			Assert.AreEqual('￼', r.Text[r.Inserts[0].CharIndex]);
		}

		[Test]
		public void ActionGlyphTagIsMarkedDynamic() {
			MarkupResult r = parser.Parse("press <glyph:Jump>");
			Assert.AreEqual(1, r.Inserts.Count);
			Assert.IsTrue(r.Inserts[0].IsActionGlyph);
			Assert.AreEqual("Jump", r.Inserts[0].Name);
			Assert.IsTrue(string.IsNullOrEmpty(r.Inserts[0].DeviceOverride));
		}

		[Test]
		public void AtActionAtIsShorthandForGlyphTag() {
			MarkupResult r = parser.Parse("press @Jump@ now");
			Assert.AreEqual(1, r.Inserts.Count);
			Assert.IsTrue(r.Inserts[0].IsActionGlyph);
			Assert.AreEqual("Jump", r.Inserts[0].Name);
			Assert.AreEqual('￼', r.Text[r.Inserts[0].CharIndex]);
			Assert.AreEqual("press ￼ now", r.Text);
		}

		[Test]
		public void UnmatchedAtSignIsLiteralText() {
			MarkupResult r = parser.Parse("contact me@example.com please");
			Assert.AreEqual(0, r.Inserts.Count);
			Assert.AreEqual("contact me@example.com please", r.Text);
		}

		[Test]
		public void GlyphTagDeviceAttributeOverridesActiveDevice() {
			MarkupResult r = parser.Parse("<glyph:Jump device=\"Xbox\">");
			Assert.AreEqual("Xbox", r.Inserts[0].DeviceOverride);
		}

		[Test]
		public void SpriteWithoutSizeAttributeDefaultsToFactorOne() {
			MarkupResult r = parser.Parse("hp <sprite=\"heart\"> full");
			Assert.AreEqual(1f, r.Inserts[0].SizeMultiplier, 1e-4f);
			Assert.AreEqual(0f, r.Inserts[0].AbsoluteSizePx, 1e-4f);
		}

		[Test]
		public void SpriteSizeAttributeIsRelativeFactor() {
			MarkupResult r = parser.Parse("hp <sprite=\"heart\" size=\"1.1\"> full");
			Assert.AreEqual(1, r.Inserts.Count);
			Assert.AreEqual("heart", r.Inserts[0].Name);
			Assert.AreEqual(1.1f, r.Inserts[0].SizeMultiplier, 1e-4f);
			Assert.AreEqual(0f, r.Inserts[0].AbsoluteSizePx, 1e-4f);
		}

		[Test]
		public void SpriteSizeAbsAttributeSetsPixels() {
			MarkupResult r = parser.Parse("<sprite=\"heart\" sizeabs=\"24\">");
			Assert.AreEqual(24f, r.Inserts[0].AbsoluteSizePx, 1e-4f);
		}

		[Test]
		public void SpriteAttributeOrderDoesNotMatter() {
			MarkupResult r = parser.Parse("<sprite=\"heart\" sizeabs=\"24\" size=\"2\">");
			Assert.AreEqual("heart", r.Inserts[0].Name);
			Assert.AreEqual(24f, r.Inserts[0].AbsoluteSizePx, 1e-4f);
			Assert.AreEqual(2f, r.Inserts[0].SizeMultiplier, 1e-4f);
		}

		[Test]
		public void GlyphTagAcceptsSizeAttributeToo() {
			MarkupResult r = parser.Parse("<glyph:Jump size=\"1.5\">");
			Assert.IsTrue(r.Inserts[0].IsActionGlyph);
			Assert.AreEqual("Jump", r.Inserts[0].Name);
			Assert.AreEqual(1.5f, r.Inserts[0].SizeMultiplier, 1e-4f);
		}

		/// <summary>Regression: <c>&lt;sprite&gt;</c>/<c>&lt;glyph&gt;</c> are self-closing and never used to
		/// push a style-stack frame; if they ever did again, the *next* unrelated closing tag would pop that
		/// frame instead of its own and leak the style past its closing tag.</summary>
		[Test]
		public void ColorAfterSpriteDoesNotLeakPastItsClosingTag() {
			MarkupResult r = parser.Parse("<color=red><sprite=\"dice\"></color>after");
			int afterIndex = r.Text.IndexOf("after", System.StringComparison.Ordinal);
			StyleSpan span = FindSpanFor(r, afterIndex);
			Assert.AreEqual(1f, span.Style.Color.x, 1e-3f);
			Assert.AreEqual(1f, span.Style.Color.y, 1e-3f);
			Assert.AreEqual(1f, span.Style.Color.z, 1e-3f);
		}

		/// <summary>Regression: a sprite alone inside a span tag (no surrounding letters) must still land
		/// inside that span's char range, not in the gap between spans that <c>SpanAt</c> falls back to the
		/// *last* span for -- which silently dropped the effect on a sprite-only run like
		/// <c>&lt;wave&gt;&lt;sprite=...&gt;&lt;/wave&gt;</c>.</summary>
		[Test]
		public void SpriteAloneInsideWaveTagGetsTheWaveEffect() {
			MarkupResult r = parser.Parse("Press<wave><sprite=\"enter\"></wave>down");
			int spriteIndex = r.Inserts[0].CharIndex;
			StyleSpan span = FindSpanFor(r, spriteIndex);
			Assert.AreEqual(BuiltinEffect.Wave, span.Style.SpanEffect);
		}

		[Test]
		public void BareWaveTagUsesSharedDefaultPreset() {
			MarkupResult r = parser.Parse("<wave>x</wave>");
			StyleSpan span = FindSpanFor(r, 0);
			Assert.AreEqual(BuiltinEffect.Wave, span.Style.SpanEffect);
			Assert.IsFalse(span.Style.HasEffectParamsOverride);
		}

		[Test]
		public void WaveTagWithAttributesGetsItsOwnOverride() {
			MarkupResult r = parser.Parse("<wave amp=\"1.5\" speed=\"3\" once=\"true\">x</wave>");
			StyleSpan span = FindSpanFor(r, 0);
			Assert.AreEqual(BuiltinEffect.Wave, span.Style.SpanEffect);
			Assert.IsTrue(span.Style.HasEffectParamsOverride);
			Assert.AreEqual(1.5f, span.Style.EffectParamsOverride.Amplitude, 1e-4f);
			Assert.AreEqual(3f, span.Style.EffectParamsOverride.Speed, 1e-4f);
			Assert.IsTrue(span.Style.EffectParamsOverride.Once);
		}

		[Test]
		public void TwoWaveTagsInSameTextCanHaveDifferentAttributes() {
			MarkupResult r = parser.Parse("<wave amp=\"1\">a</wave> <wave amp=\"5\">b</wave>");
			int aIndex = r.Text.IndexOf('a');
			int bIndex = r.Text.IndexOf('b');
			StyleSpan spanA = FindSpanFor(r, aIndex);
			StyleSpan spanB = FindSpanFor(r, bIndex);
			Assert.AreEqual(1f, spanA.Style.EffectParamsOverride.Amplitude, 1e-4f);
			Assert.AreEqual(5f, spanB.Style.EffectParamsOverride.Amplitude, 1e-4f);
		}

		[Test]
		public void BlinkTagDefaultsToNoOverride() {
			MarkupResult r = parser.Parse("<blink>x</blink>");
			StyleSpan span = FindSpanFor(r, 0);
			Assert.AreEqual(BuiltinEffect.Blink, span.Style.SpanEffect);
			Assert.IsFalse(span.Style.HasEffectParamsOverride);
		}

		[Test]
		public void BlinkMinMaxSetsOnlyAlphaShortcut() {
			MarkupResult r = parser.Parse("<blink min=\"0.2\" max=\"0.9\">x</blink>");
			StyleSpan span = FindSpanFor(r, 0);
			Assert.IsTrue(span.Style.HasEffectParamsOverride);
			UnityEngine.Color a = span.Style.EffectParamsOverride.ColorA;
			UnityEngine.Color b = span.Style.EffectParamsOverride.ColorB;
			Assert.AreEqual(0.2f, a.a, 1e-4f);
			Assert.AreEqual(0.9f, b.a, 1e-4f);
			// RGB stays neutral white -- the "just alpha" shortcut, no unintended tint
			Assert.AreEqual(1f, a.r, 1e-4f);
			Assert.AreEqual(1f, a.g, 1e-4f);
			Assert.AreEqual(1f, a.b, 1e-4f);
		}

		[Test]
		public void BlinkColorThenMinMaxAppliesMinMaxLast() {
			// min/max must win over color's default alpha regardless of attribute order in the tag
			MarkupResult r = parser.Parse("<blink color=\"#ff0000\" min=\"0.3\">x</blink>");
			StyleSpan span = FindSpanFor(r, 0);
			UnityEngine.Color a = span.Style.EffectParamsOverride.ColorA;
			Assert.AreEqual(1f, a.r, 1e-4f);
			Assert.AreEqual(0.3f, a.a, 1e-4f);
		}

		[Test]
		public void BlinkEaseAttributeParsesEnumName() {
			MarkupResult r = parser.Parse("<blink ease=\"linear\">x</blink>");
			StyleSpan span = FindSpanFor(r, 0);
			Assert.AreEqual(TextEasing.Linear, span.Style.EffectParamsOverride.Easing);
		}

		[Test]
		public void UnknownTagIsDropped() {
			MarkupResult r = parser.Parse("a<wobble>b</wobble>c");
			Assert.AreEqual("abc", r.Text);
		}

		[Test]
		public void RichTextDisabledKeepsTagsLiteral() {
			MarkupResult r = parser.Parse("a<b>c", richText: false);
			Assert.AreEqual("a<b>c", r.Text);
		}

		[Test]
		public void UppercaseTagSetsCase() {
			MarkupResult r = parser.Parse("<uppercase>hi</uppercase>");
			Assert.AreEqual(TextCase.Upper, FindSpanFor(r, 0).Style.Case);
		}

		private static StyleSpan FindSpanFor(MarkupResult r, int charIndex) {
			foreach (StyleSpan s in r.Spans) {
				if (charIndex >= s.Start && charIndex < s.End) return s;
			}
			return r.Spans[r.Spans.Count - 1];
		}
	}
}
