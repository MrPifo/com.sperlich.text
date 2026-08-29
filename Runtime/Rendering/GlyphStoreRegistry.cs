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

#if UNITY_EDITOR
		/// <summary>
		/// Editor-only: dispose and forget every cached store. Used after the "TMP Essential Resources"
		/// get imported, so labels rebuild their atlas from a clean state. Callers must rebind their
		/// labels right after (see <c>SperlichText.EditorRebindFont</c>).
		/// </summary>
		public static void EditorPurgeAll() {
			foreach (Entry e in stores.Values) {
				e.Store.Fonts.Dispose();
			}
			stores.Clear();
		}
#endif
	}
}
