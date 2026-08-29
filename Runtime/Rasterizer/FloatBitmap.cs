// Minimal stand-in for msdfgen's BitmapRef<float, N> — a contiguous N-channel float image.
// Row 0 is the bottom row (Y_UPWARD, msdfgen's default orientation).

namespace Sperlich.Text.Rasterizer {

	public sealed class FloatBitmap {

		public readonly int Width;
		public readonly int Height;
		public readonly int Channels;
		public readonly float[] Data;

		public FloatBitmap(int width, int height, int channels) {
			Width = width;
			Height = height;
			Channels = channels;
			Data = new float[width * height * channels];
		}

		private int Index(int x, int y, int c) => Channels * (Width * y + x) + c;

		public float this[int x, int y, int c] {
			get => Data[Index(x, y, c)];
			set => Data[Index(x, y, c)] = value;
		}

		/// <summary>Base index of pixel (x, y); channels are the next <see cref="Channels"/> entries.</summary>
		public int PixelBase(int x, int y) => Channels * (Width * y + x);
	}
}
