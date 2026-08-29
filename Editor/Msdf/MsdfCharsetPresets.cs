using System.Collections.Generic;

namespace Sperlich.Text.EditorTools {

	/// <summary>
	/// Expands a <see cref="MsdfCharset"/> preset (+ an extra-characters string) to code points.
	/// The presets are strictly nested and each maps to a recognised standard:
	/// <list type="bullet">
	/// <item><see cref="MsdfCharset.Ascii"/> — Unicode "Basic Latin", U+0020..U+007E.</item>
	/// <item><see cref="MsdfCharset.Latin1"/> — + Latin-1 Supplement (ISO-8859-1).</item>
	/// <item><see cref="MsdfCharset.LatinExtended"/> — + Latin Extended-A + the standard typographic
	/// punctuation / currency / spaces (covers every European Latin-script language).</item>
	/// <item><see cref="MsdfCharset.Wgl4"/> — the full Windows Glyph List 4 (adds Greek, Cyrillic,
	/// arrows, math operators, fractions, box drawing, a few symbols).</item>
	/// </list>
	/// </summary>
	public static class MsdfCharsetPresets {

		/// <summary>
		/// Typographic punctuation added at the <see cref="MsdfCharset.LatinExtended"/> tier. This is the
		/// subset of Unicode "General Punctuation" / "Currency Symbols" that real body text uses, plus
		/// U+1E9E (capital ẞ) so German all-caps works.
		/// </summary>
		private static readonly uint[] TypographicPunctuation = {
			0x2018, 0x2019, 0x201A, 0x201B, // ' ' ‚ ‛
			0x201C, 0x201D, 0x201E, 0x201F, // " " „ ‟
			0x2013, 0x2014, 0x2015,         // – — ―
			0x2026,                         // …
			0x00AB, 0x00BB, 0x2039, 0x203A, // « » ‹ ›
			0x2022, 0x00B7,                 // • ·
			0x2020, 0x2021,                 // † ‡
			0x2030,                         // ‰
			0x20AC,                         // €
			0x1E9E,                         // ẞ  capital sharp s
			0x00A0, 0x2009, 0x2007, 0x202F, // NBSP, thin space, figure space, narrow NBSP
			0x200B                          // zero-width space
		};

		/// <summary>
		/// Windows Glyph List 4, expressed as inclusive code-point ranges. Everything in
		/// <see cref="MsdfCharset.LatinExtended"/> is already covered by the earlier tiers + the punctuation
		/// table; this list adds the rest of the WGL4 repertoire on top.
		/// </summary>
		private static readonly (uint lo, uint hi)[] Wgl4Ranges = {
			(0x0100, 0x017F),               // Latin Extended-A (all)
			(0x0192, 0x0192),               // ƒ
			(0x01FA, 0x01FF),               // Latin Extended-B (WGL4 subset)
			(0x02C6, 0x02C7), (0x02C9, 0x02C9), (0x02D8, 0x02DD), // spacing modifier letters
			(0x0384, 0x038A), (0x038C, 0x038C), (0x038E, 0x03A1), (0x03A3, 0x03CE), // Greek
			(0x0400, 0x045F), (0x0490, 0x0491),                   // Cyrillic
			(0x1E80, 0x1E85), (0x1EF2, 0x1EF3),                   // Welsh W / Y grave
			(0x2013, 0x2015), (0x2017, 0x201E), (0x2020, 0x2022), // General Punctuation
			(0x2026, 0x2026), (0x2030, 0x2030), (0x2032, 0x2033),
			(0x2039, 0x203A), (0x203C, 0x203C), (0x203E, 0x203E), (0x2044, 0x2044),
			(0x20A3, 0x20A4), (0x20A7, 0x20A7), (0x20AC, 0x20AC), // currency
			(0x2105, 0x2105), (0x2113, 0x2113), (0x2116, 0x2116), // letterlike
			(0x2122, 0x2122), (0x2126, 0x2126), (0x212E, 0x212E),
			(0x2153, 0x2154), (0x215B, 0x215E),                   // fractions
			(0x2190, 0x2195), (0x21A8, 0x21A8),                   // arrows
			(0x2202, 0x2202), (0x2206, 0x2206), (0x220F, 0x220F), // math operators
			(0x2211, 0x2212), (0x2215, 0x2215), (0x2219, 0x221A),
			(0x221E, 0x221F), (0x2229, 0x2229), (0x222B, 0x222B),
			(0x2248, 0x2248), (0x2260, 0x2261), (0x2264, 0x2265),
			(0x2302, 0x2302), (0x2310, 0x2310), (0x2320, 0x2321),
			(0x2500, 0x2500), (0x2502, 0x2502), (0x250C, 0x250C), // box drawing
			(0x2510, 0x2510), (0x2514, 0x2514), (0x2518, 0x2518),
			(0x251C, 0x251C), (0x2524, 0x2524), (0x252C, 0x252C),
			(0x2534, 0x2534), (0x253C, 0x253C), (0x2550, 0x256C),
			(0x2580, 0x2580), (0x2584, 0x2584), (0x2588, 0x2588), // block elements
			(0x258C, 0x258C), (0x2590, 0x2593),
			(0x25A0, 0x25A1), (0x25AA, 0x25AC), (0x25B2, 0x25B2), // geometric shapes
			(0x25BA, 0x25BA), (0x25BC, 0x25BC), (0x25C4, 0x25C4),
			(0x25CA, 0x25CB), (0x25CF, 0x25CF), (0x25D8, 0x25D9), (0x25E6, 0x25E6),
			(0x263A, 0x263C), (0x2640, 0x2640), (0x2642, 0x2642), // misc symbols
			(0x2660, 0x2660), (0x2663, 0x2663), (0x2665, 0x2666), (0x266A, 0x266B),
			(0xFB01, 0xFB02)                // fi / fl ligatures
		};

		/// <summary>Enumerates every code point a bake with this preset + extra string should cover.</summary>
		public static IEnumerable<uint> CodePoints(MsdfCharset preset, string extra) {
			SortedSet<uint> set = new SortedSet<uint>();

			// Ascii — Basic Latin.
			for (uint u = 0x20; u <= 0x7E; u++) set.Add(u);

			// Latin1 — + Latin-1 Supplement.
			if (preset >= MsdfCharset.Latin1)
				for (uint u = 0x00A0; u <= 0x00FF; u++) set.Add(u);

			// LatinExtended — + Latin Extended-A + standard typographic punctuation.
			if (preset >= MsdfCharset.LatinExtended) {
				for (uint u = 0x0100; u <= 0x017F; u++) set.Add(u);
				foreach (uint u in TypographicPunctuation) set.Add(u);
			}

			// Wgl4 — the full Windows Glyph List 4.
			if (preset >= MsdfCharset.Wgl4)
				foreach ((uint lo, uint hi) in Wgl4Ranges)
					for (uint u = lo; u <= hi; u++) set.Add(u);

			if (!string.IsNullOrEmpty(extra))
				foreach (char ch in extra)
					if (!char.IsControl(ch)) set.Add(ch);

			return set;
		}
	}
}
