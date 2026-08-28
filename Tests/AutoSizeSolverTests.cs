using NUnit.Framework;
using UnityEngine;
using Sperlich.Text;

namespace Sperlich.Text.Tests {

	public class AutoSizeSolverTests {

		// synthetic: block size scales linearly with font size
		private static Vector2 Linear(float s) => new Vector2(s * 10f, s * 2f);

		[Test]
		public void ReturnsMaxWhenEverythingFits() {
			float r = AutoSizeSolver.Solve(Linear, 8f, 40f, 10000f, 10000f);
			Assert.AreEqual(40f, r, 0.01f);
		}

		[Test]
		public void ReturnsMinWhenNothingFits() {
			float r = AutoSizeSolver.Solve(Linear, 8f, 40f, 1f, 1f);
			Assert.AreEqual(8f, r, 0.01f);
		}

		[Test]
		public void FindsLargestFittingSizeOnWidth() {
			// width = s * 10 must be <= 200  ->  s <= 20
			float r = AutoSizeSolver.Solve(Linear, 8f, 40f, 200f, 0f, iterations: 24, tolerance: 0.05f);
			Assert.That(r, Is.InRange(19.5f, 20.01f));
		}

		[Test]
		public void HeightConstraintAlsoApplies() {
			// height = s * 2 must be <= 30  ->  s <= 15
			float r = AutoSizeSolver.Solve(Linear, 8f, 40f, 0f, 30f, iterations: 24, tolerance: 0.05f);
			Assert.That(r, Is.InRange(14.5f, 15.01f));
		}

		[Test]
		public void NullMeasureReturnsMax() {
			Assert.AreEqual(40f, AutoSizeSolver.Solve(null, 8f, 40f, 100f, 100f));
		}
	}
}
