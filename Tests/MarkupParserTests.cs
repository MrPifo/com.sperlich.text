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
