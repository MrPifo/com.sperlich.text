namespace Sperlich.Text {

	/// <summary>
	/// Small subset of UAX #14 line breaking: mandatory breaks, break opportunities after spaces and
	/// hyphens, author-placed soft hyphens, and a short "do not break before" punctuation set.
	/// Enough for Latin / German UI text; no dictionary hyphenation, no CJK rules.
	/// </summary>
	public static class LineBreaker {

		private const char SoftHyphen = '\u00AD';
		private const char NoBreakSpace = '\u00A0';
		private const char NarrowNoBreakSpace = '\u202F';
		private const char ZeroWidthSpace = '\u200B';
		private const char IdeographicSpace = '\u3000';
		private const char LineSeparator = '\u2028';
		private const char ParagraphSeparator = '\u2029';
		private const char NextLine = '\u0085';
		private const char VerticalTab = '\u000B';
		private const char FormFeed = '\u000C';
		private const char EnDash = '\u2013';
		private const char EmDash = '\u2014';
		private const char PunctuationSpaceStart = '\u2000';
		private const char PunctuationSpaceEnd = '\u200A';

		/// <summary>True when <paramref name="c"/> forces a new line.</summary>
		public static bool IsMandatoryBreak(char c) {
			return c == '\n' || c == LineSeparator || c == ParagraphSeparator
				|| c == NextLine || c == VerticalTab || c == FormFeed;
		}

		/// <summary>True when <paramref name="c"/> is a space a line may break after (excludes no-break spaces).</summary>
		public static bool IsBreakingSpace(char c) {
			if (c == ' ' || c == '\t' || c == IdeographicSpace || c == ZeroWidthSpace) return true;
			if (c == NoBreakSpace || c == NarrowNoBreakSpace) return false;
			return c >= PunctuationSpaceStart && c <= PunctuationSpaceEnd;
		}

		/// <summary>The soft hyphen: invisible unless a break is taken at it, then a hyphen is drawn.</summary>
		public static bool IsSoftHyphen(char c) => c == SoftHyphen;

		/// <summary>True if a break opportunity exists between <paramref name="prev"/> and <paramref name="next"/>.</summary>
		public static bool CanBreakBetween(char prev, char next) {
			if (prev == '\0') return false;
			if (IsBreakingSpace(prev)) return true;
			if (IsSoftHyphen(prev)) return true;
			if ((prev == '-' || prev == EnDash || prev == EmDash || prev == '/') && !char.IsWhiteSpace(next)) {
				return !IsNoBreakBefore(next);
			}
			return false;
		}

		/// <summary>Characters that must not start a new line (trailing punctuation, closing brackets).</summary>
		public static bool IsNoBreakBefore(char c) {
			switch (c) {
				case '.': case ',': case ';': case ':': case '!': case '?':
				case ')': case ']': case '}': case '%':
				case '»': case '›': case '’': case '”': case '…':
					return true;
				default:
					return false;
			}
		}

		/// <summary>Characters that must not end a line (opening brackets / quotes).</summary>
		public static bool IsNoBreakAfter(char c) {
			switch (c) {
				case '(': case '[': case '{':
				case '«': case '‹': case '‘': case '“':
				case '¿': case '¡':
					return true;
				default:
					return false;
			}
		}
	}
}
