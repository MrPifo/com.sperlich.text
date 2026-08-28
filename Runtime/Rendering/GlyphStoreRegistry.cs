using System.Collections.Generic;

namespace Sperlich.Text {

	/// <summary>
	/// Shares one <see cref="GlyphStore"/> (and therefore one atlas + one draw-call batch) between all
	/// labels that use the same <see cref="FontDefinition"/>. Ref-counted; the store is disposed when the
	/// last label using it goes away.
	/// </summary>
	public static class GlyphStoreRegistry {

		private struct Entry {
			public GlyphStore Store;
			public int RefCount;
		}

		private static readonly Dictionary<FontDefinition, Entry> stores = new();

		public static GlyphStore Acquire(FontDefinition font) {
			if (font == null) return null;
			if (stores.TryGetValue(font, out Entry e)) {
				e.RefCount++;
				stores[font] = e;
				return e.Store;
			}

			FontAccess access = new FontAccess(font);
			GlyphStore store = new GlyphStore(access);

			stores[font] = new Entry { Store = store, RefCount = 1 };
			return store;
		}

		public static void Release(FontDefinition font) {
			if (font == null || !stores.TryGetValue(font, out Entry e)) return;
			e.RefCount--;
			if (e.RefCount <= 0) {
				e.Store.Fonts.Dispose();
				stores.Remove(font);
			} else {
				stores[font] = e;
			}
		}
	}
}
