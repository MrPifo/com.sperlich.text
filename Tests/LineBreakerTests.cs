using NUnit.Framework;
using Sperlich.Text;

namespace Sperlich.Text.Tests {

	public class LineBreakerTests {

		[Test]
		public void NewlineIsMandatory() {
			Assert.IsTrue(LineBreaker.IsMandatoryBreak('\n'));
			Assert.IsFalse(LineBreaker.IsMandatoryBreak('a'));
		}

		[Test]
		public void SpaceIsABreakOpportunity() {
			Assert.IsTrue(LineBreaker.CanBreakBetween(' ', 'w'));
			Assert.IsFalse(LineBreaker.CanBreakBetween('o', 'w'));
		}

		[Test]
		public void NoBreakSpaceDoesNotBreak() {
			Assert.IsFalse(LineBreaker.IsBreakingSpace(' '));
			Assert.IsFalse(LineBreaker.CanBreakBetween(' ', 'x'));
		}

		[Test]
		public void SoftHyphenIsABreakOpportunity() {
			Assert.IsTrue(LineBreaker.IsSoftHyphen('­'));
			Assert.IsTrue(LineBreaker.CanBreakBetween('­', 'e'));
		}

		[Test]
		public void HyphenBreaksButNotBeforeClosingPunctuation() {
			Assert.IsTrue(LineBreaker.CanBreakBetween('-', 'w'));
			Assert.IsFalse(LineBreaker.CanBreakBetween('-', ')'));
		}

		[Test]
		public void ClosingPunctuationMayNotStartALine() {
			Assert.IsTrue(LineBreaker.IsNoBreakBefore('.'));
			Assert.IsTrue(LineBreaker.IsNoBreakBefore(')'));
			Assert.IsFalse(LineBreaker.IsNoBreakBefore('a'));
		}

		[Test]
		public void OpeningBracketMayNotEndALine() {
			Assert.IsTrue(LineBreaker.IsNoBreakAfter('('));
			Assert.IsFalse(LineBreaker.IsNoBreakAfter(')'));
		}
	}
}
