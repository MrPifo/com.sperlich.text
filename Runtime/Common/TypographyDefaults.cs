namespace Sperlich.Text {

	/// <summary>
	/// Central place for the micro-typography constants derived from the research doc
	/// (WCAG 1.4.8 / 1.4.12, Xbox Accessibility Guidelines, Emil Ruder line length).
	/// Values are multipliers of the font size unless noted otherwise.
	/// </summary>
	public static class TypographyDefaults {

		/// <summary>Line advance as a multiple of font size (WCAG "space and a half").</summary>
		public const float LineSpacing = 1.5f;

		/// <summary>Extra gap between paragraphs as a multiple of font size (WCAG AA: 2x).</summary>
		public const float ParagraphSpacing = 2.0f;

		/// <summary>Minimum letter tracking the layout must survive without breaking (em).</summary>
		public const float MinTrackingEm = 0.12f;

		/// <summary>Minimum word spacing the layout must survive without breaking (em).</summary>
		public const float MinWordSpacingEm = 0.16f;

		/// <summary>Additional tracking added automatically for all-caps headings (em).</summary>
		public const float UppercaseHeadingTrackingEm = 0.22f;

		/// <summary>Additional tracking added automatically for short all-caps words / acronyms (em).</summary>
		public const float UppercaseWordTrackingEm = 0.08f;

		/// <summary>Recommended lower bound for line length in characters (Ruder).</summary>
		public const int RecommendedMinLineChars = 50;

		/// <summary>Recommended upper bound for line length in characters.</summary>
		public const int RecommendedMaxLineChars = 75;

		/// <summary>Hard upper bound for line length in characters (W3C).</summary>
		public const int HardMaxLineChars = 80;

		/// <summary>Default modular scale ratio (Perfect Fourth) for size presets.</summary>
		public const float ModularScaleRatio = 1.333f;

		/// <summary>Soft hyphen code point. Author-placed break opportunity.</summary>
		public const char SoftHyphen = '­';

		/// <summary>Replacement / "tofu" code point used when no fallback font has the glyph.</summary>
		public const char Tofu = '�';

		/// <summary>
		/// Minimum readable font size in pixels for a given platform context, from XAG 101.
		/// Used by the editor linter only, never enforced at runtime.
		/// </summary>
		public static float MinReadablePx(PlatformContext context) {
			return context switch {
				PlatformContext.ConsoleFullHd => 26f,
				PlatformContext.ConsoleUhd => 52f,
				PlatformContext.PcFullHd => 18f,
				PlatformContext.PcUhd => 36f,
				PlatformContext.MobileStreaming => 18f,
				_ => 18f
			};
		}
	}

	/// <summary>Display context for the readability linter (editor only).</summary>
	public enum PlatformContext {
		PcFullHd,
		PcUhd,
		ConsoleFullHd,
		ConsoleUhd,
		MobileStreaming
	}
}
