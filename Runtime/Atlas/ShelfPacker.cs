using System.Collections.Generic;

namespace Sperlich.Text {

	/// <summary>
	/// Deterministic shelf (row) rectangle packer. Pure math, no Unity types, unit-tested.
	/// Used for the future MTSDF path where this package owns the atlas bitmap; the v1 SDF path
	/// currently lets FontEngine pack instead.
	/// </summary>
	public sealed class ShelfPacker {

		private struct Shelf {
			public int Y;
			public int Height;
			public int UsedWidth;
		}

		private readonly int width;
		private readonly int height;
		private readonly int padding;
		private readonly List<Shelf> shelves = new();

		public int Width => width;
		public int Height => height;

		public ShelfPacker(int width, int height, int padding = 1) {
			this.width = width;
			this.height = height;
			this.padding = padding < 0 ? 0 : padding;
		}

		public void Reset() => shelves.Clear();

		/// <summary>Tries to place a <paramref name="w"/> x <paramref name="h"/> rect. Returns its top-left on success.</summary>
		public bool TryInsert(int w, int h, out int x, out int y) {
			x = 0;
			y = 0;
			if (w <= 0 || h <= 0 || w > width || h > height) return false;

			int padW = w + padding;
			int padH = h + padding;

			int bestShelf = -1;
			int bestWaste = int.MaxValue;
			for (int i = 0; i < shelves.Count; i++) {
				Shelf s = shelves[i];
				if (s.Height < padH) continue;
				if (s.UsedWidth + padW > width) continue;
				int waste = s.Height - padH;
				if (waste < bestWaste) {
					bestWaste = waste;
					bestShelf = i;
				}
			}

			if (bestShelf >= 0) {
				Shelf s = shelves[bestShelf];
				x = s.UsedWidth;
				y = s.Y;
				s.UsedWidth += padW;
				shelves[bestShelf] = s;
				return true;
			}

			int nextY = shelves.Count == 0 ? 0 : shelves[^1].Y + shelves[^1].Height;
			if (nextY + padH > height) return false;

			shelves.Add(new Shelf { Y = nextY, Height = padH, UsedWidth = padW });
			x = 0;
			y = nextY;
			return true;
		}

		/// <summary>Fraction of the atlas area currently consumed by shelves (0..1).</summary>
		public float Occupancy {
			get {
				long used = 0;
				for (int i = 0; i < shelves.Count; i++) used += (long)shelves[i].UsedWidth * shelves[i].Height;
				return (float)((double)used / ((double)width * height));
			}
		}
	}
}
