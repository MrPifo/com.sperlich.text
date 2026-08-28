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
		NoWrap,
		/// <summary>UAX #14 style word wrap, breaking between words and at soft hyphens.</summary>
		Word,
		/// <summary>Word wrap, but overly long words are broken mid-word as a last resort.</summary>
		WordThenChar
	}

	/// <summary>Synthetic weight / slant applied on top of the loaded face (no real font file swap).</summary>
	public enum FontSynthesis {
		None = 0,
		Bold = 1 << 0,
		Italic = 1 << 1,
		Light = 1 << 2
	}

	/// <summary>Runtime letter case transform, applied before layout.</summary>
	public enum TextCase {
		None,
		Upper,
		Lower,
		SmallCaps
	}

	/// <summary>Distance-field kind stored in the atlas. Only <see cref="SDF"/> is generated in v1.</summary>
	public enum GlyphFieldKind {
		/// <summary>Single channel signed distance field (FontEngine raster).</summary>
		SDF,
		/// <summary>Multi channel + alpha SDF (reserved for the future msdfgen plugin path).</summary>
		MTSDF,
		/// <summary>Plain alpha coverage bitmap, no distance field.</summary>
		Bitmap
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
		Pulse,
		Rainbow,
		Glow,
		Glitch
	}
}
