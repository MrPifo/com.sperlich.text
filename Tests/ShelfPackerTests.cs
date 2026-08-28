using NUnit.Framework;
using Sperlich.Text;

namespace Sperlich.Text.Tests {

	public class ShelfPackerTests {

		[Test]
		public void RejectsOversizedRect() {
			ShelfPacker p = new ShelfPacker(64, 64, 1);
			Assert.IsFalse(p.TryInsert(128, 10, out _, out _));
			Assert.IsFalse(p.TryInsert(10, 128, out _, out _));
		}

		[Test]
		public void FirstInsertGoesToOrigin() {
			ShelfPacker p = new ShelfPacker(128, 128, 0);
			Assert.IsTrue(p.TryInsert(20, 20, out int x, out int y));
			Assert.AreEqual(0, x);
			Assert.AreEqual(0, y);
		}

		[Test]
		public void SecondRectSharesShelfWhenItFits() {
			ShelfPacker p = new ShelfPacker(128, 128, 0);
			p.TryInsert(20, 20, out _, out _);
			Assert.IsTrue(p.TryInsert(20, 18, out int x, out int y));
			Assert.AreEqual(20, x);
			Assert.AreEqual(0, y);
		}

		[Test]
		public void NewShelfStartsBelowThePrevious() {
			ShelfPacker p = new ShelfPacker(40, 128, 0);
			p.TryInsert(30, 20, out _, out _);      // shelf 0, height 20
			Assert.IsTrue(p.TryInsert(30, 25, out int x, out int y)); // doesn't fit width -> new shelf
			Assert.AreEqual(0, x);
			Assert.AreEqual(20, y);
		}

		[Test]
		public void ReportsFullWhenHeightExhausted() {
			ShelfPacker p = new ShelfPacker(32, 32, 0);
			Assert.IsTrue(p.TryInsert(32, 20, out _, out _));
			Assert.IsFalse(p.TryInsert(32, 20, out _, out _));
		}

		[Test]
		public void OccupancyGrowsWithInserts() {
			ShelfPacker p = new ShelfPacker(100, 100, 0);
			Assert.AreEqual(0f, p.Occupancy, 1e-4f);
			p.TryInsert(50, 50, out _, out _);
			Assert.Greater(p.Occupancy, 0.2f);
		}
	}
}
