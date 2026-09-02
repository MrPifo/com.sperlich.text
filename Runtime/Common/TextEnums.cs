namespace Sperlich.Text {

	/// <summary>Horizontal alignment of a text block inside its layout rect.</summary>
	public enum TextAlign {
		Left,
		Center,
		Right,
		/// <summary>Fill the line width by widening spaces; the last line of a paragraph stays left.</summary>
		Justified,
		/// <summary>Like <see cref="Justified"/> but every line is stretched, including the last.</summary>
		Flush,
		/// <summary>Centre on the glyph ink bounds instead of the pen advance (ignores side bearings).</summary>
		GeometryCenter
	}

	/// <summary>Vertical alignment of the whole text block inside its layout rect.</summary>
	public enum TextVerticalAlign {
		Top,
		Middle,
		Bottom,
		Baseline
	}

	/// <summary>How the layout engine reacts when the text does not fit the layout rect.</summary>
	public enum TextOverflow {
		/// <summary>Draw everything, ignore the rect height.</summary>
		Overflow,
		/// <summary>Cut lines that leave the rect; the fragment shader / mesh clip removes the rest.</summary>
		Clip,
		/// <summary>Truncate at a word boundary and append an ellipsis glyph.</summary>
		Ellipsis,
		/// <summary>Shrink the font size (see <see cref="AutoSizeSolver"/>) until it fits.</summary>
		ScaleToFit,
		/// <summary>Keep full layout, expose a scroll offset for an external viewport/mask.</summary>
		Scroll
	}

	/// <summary>Line wrapping behaviour.</summary>
	public enum TextWrap {
		/// <summary>No wrapping, a single visual line (still honours explicit line feeds).</summary>
		[UnityEngine.InspectorName("Off (single line)")]
		NoWrap,
		/// <summary>UAX #14 style word wrap, breaking between words and at soft hyphens.</summary>
		[UnityEngine.InspectorName("Wrap at words")]
		Word,
		/// <summary>Word wrap, but overly long words are broken mid-word as a last resort.</summary>
		[UnityEngine.InspectorName("Wrap, break long words")]
		WordThenChar
	}

	/// <summary>Synthetic weight / slant applied on top of the loaded face (no real font file swap).</summary>
	public enum FontSynthesis {
		None = 0,
		Bold = 1 << 0,
		Italic = 1 << 1,
		Light = 1 << 2
	}

	/// <summary>
	/// Whole-label style flags, like TextMeshPro's "Font Style". Applied as the base style for the
	/// entire text (rich-text tags still layer on top). Uppercase / Lowercase / SmallCaps are mutually
	/// exclusive — SmallCaps wins, then Uppercase, then Lowercase.
	/// </summary>
	[System.Flags]
	public enum TextFontStyle {
		None = 0,
		Bold = 1 << 0,
		Italic = 1 << 1,
		Underline = 1 << 2,
		Strikethrough = 1 << 3,
		Uppercase = 1 << 4,
		Lowercase = 1 << 5,
		SmallCaps = 1 << 6
	}

	/// <summary>Where a component-level outline sits relative to the glyph edge (like Photoshop's stroke position).</summary>
	public enum TextOutlinePlacement {
		/// <summary>Outline grows inward, eating into the glyph face.</summary>
		Inner,
		/// <summary>Outline straddles the edge, half in / half out.</summary>
		Middle,
		/// <summary>Outline grows outward, the glyph face keeps its size.</summary>
		Outer
	}

	/// <summary>Runtime letter case transform, applied before layout.</summary>
	public enum TextCase {
		None,
		Upper,
		Lower,
		SmallCaps
	}

	/// <summary>Distance-field kind stored in the atlas.</summary>
	public enum GlyphFieldKind {
		/// <summary>Single channel signed distance field via TMP dynamic mode (any code point at runtime).</summary>
		SDF,
		/// <summary>Multi channel + true-SDF alpha, pre-baked by the editor (sharper corners, fixed charset).</summary>
		MTSDF,
		/// <summary>Plain alpha coverage bitmap, no distance field. Not implemented.</summary>
		Bitmap
	}

	/// <summary>
	/// Which characters an MTSDF bake covers. Presets are strictly nested (each one is a superset of the
	/// one above) and follow well-known character-set standards. Extra characters are added on top.
	/// </summary>
	public enum MsdfCharset {
		/// <summary>Printable ASCII only, U+0020..U+007E (Unicode "Basic Latin").</summary>
		Ascii,
		/// <summary>ASCII + Latin-1 Supplement, U+00A0..U+00FF (the ISO-8859-1 / "Latin-1" repertoire).</summary>
		Latin1,
		/// <summary>Latin-1 + Latin Extended-A (U+0100..U+017F, all European Latin-script languages) plus the
		/// standard "smart" punctuation: curly quotes, en/em dash, ellipsis, bullet, guillemets, €, ẞ and the
		/// typographic spaces. Good default for Latin / German UI text.</summary>
		LatinExtended,
		/// <summary>Windows Glyph List 4 — the Microsoft pan-European standard (~650 glyphs): everything in
		/// LatinExtended plus Greek, Cyrillic, arrows, common math operators, fractions, box drawing and a few
		/// symbols. Covers Ω π ∑ → out of the box. Needs a larger atlas — only pick this if you actually use
		/// those scripts.</summary>
		Wgl4
	}

	/// <summary>How a <c>&lt;gradient&gt;</c> run distributes its colours.</summary>
	public enum GradientScope {
		/// <summary>One smooth gradient stretched across the whole run (per line).</summary>
		Run,
		/// <summary>Every glyph shows the full gradient independently.</summary>
		PerChar,
		/// <summary>Every glyph is one flat colour, stepping toward the end colour across the run.</summary>
		Stepped
	}

	/// <summary>Built-in effect catalog. These have a Burst <see cref="BuiltinEffectJob"/> fast path.</summary>
	public enum BuiltinEffect {
		None = 0,
		Wave,
		Shake,
		[UnityEngine.InspectorName("Scale / Wobble")]
		Pulse,
		[UnityEngine.InspectorName("Rotate / Wobble")]
		Rotate,
		Rainbow,
		Glow,
		Glitch
	}

	/// <summary>Sub-style for the Wave effect.</summary>
	public enum WaveStyle {
		/// <summary>Smooth harmonic sine wave.</summary>
		Sine = 0,
		/// <summary>Arc/parabolic jump where characters hop upward and bounce.</summary>
		Bounce = 1,
		/// <summary>Linear triangle waveform with sharp crests.</summary>
		Triangle = 2
	}

	/// <summary>Sub-style for the Rotate effect.</summary>
	public enum RotateStyle {
		/// <summary>Harmonic pendulum swing / wobble back and forth.</summary>
		Wobble = 0,
		/// <summary>Continuous 360-degree rotation.</summary>
		Spin = 1
	}

	/// <summary>Sub-style for the Scale / Wobble effect.</summary>
	public enum ScaleStyle {
		/// <summary>Squash & stretch wobble (Y expands while X compresses).</summary>
		SquashAndStretch = 0,
		/// <summary>Appear transition scaling from 0 to 1 with easing.</summary>
		PopIn = 1,
		/// <summary>Disappear transition scaling from 1 to 0 with easing.</summary>
		PopOut = 2,
		/// <summary>Uniform breathing/pulsing scale around glyph center.</summary>
		Pulse = 3
	}

	/// <summary>Easing function for transition effects (e.g. PopIn/PopOut).</summary>
	public enum TextEasing {
		/// <summary>Linear interpolation (constant rate).</summary>
		Linear = 0,
		/// <summary>Smooth sine ease out.</summary>
		Sine = 1,
		/// <summary>Overshooting ease out for punchy pop animations.</summary>
		EaseOutBack = 2,
		/// <summary>Bouncing ease out for falling/dropping effects.</summary>
		EaseOutBounce = 3,
		/// <summary>Elastic spring ease out.</summary>
		EaseOutElastic = 4,
		/// <summary>Smooth S-curve ease in and out.</summary>
		EaseInOutSine = 5
	}

	/// <summary>Sub-style for the Glow / Lighting effect.</summary>
	public enum GlowStyle {
		/// <summary>Smooth crossfade between Color A and Color B.</summary>
		Fade = 0,
		/// <summary>Traveling light beam / shine sweep using a color ramp.</summary>
		Shimmer = 1,
		/// <summary>Irregular flickering like a faulty neon tube.</summary>
		NeonFlicker = 2
	}

	/// <summary>Sub-style for the Glitch effect.</summary>
	public enum GlitchStyle {
		/// <summary>Position jitter combined with RGB color shifts.</summary>
		Glitch = 0,
		/// <summary>Digital matrix scramble decode transition.</summary>
		Matrix = 1
	}
}
