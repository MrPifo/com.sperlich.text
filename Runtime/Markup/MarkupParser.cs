using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>Result of a markup pass: stripped display text plus resolved style / link / insert tables.</summary>
	public struct MarkupResult {
		public string Text;
		public List<StyleSpan> Spans;
		public List<LinkRegion> Links;
		public List<InlineInsert> Inserts;
	}

	/// <summary>
	/// Stack-based rich-text tag parser. Produces a flat list of <see cref="StyleSpan"/> over the
	/// stripped text. Deliberately small: no BiDi, no nested-list logic, unknown tags are dropped.
	/// Reused across text changes; call <see cref="Parse"/> with a pooled result to stay allocation-light.
	/// </summary>
	public sealed class MarkupParser {

		private readonly Stack<StyleState> stack = new();
		private readonly StringBuilder sb = new(256);
		private readonly List<StyleSpan> spans = new(16);
		private readonly List<LinkRegion> links = new(4);
		private readonly List<InlineInsert> inserts = new(4);

		/// <param name="baseStyle">Style the whole text starts from (component-level "Font Style" etc.).
		/// <c>null</c> uses <see cref="StyleState.Default"/>.</param>
		public MarkupResult Parse(string source, bool richText = true, StyleState? baseStyle = null) {
			stack.Clear();
			sb.Clear();
			spans.Clear();
			links.Clear();
			inserts.Clear();
			stack.Push(baseStyle ?? StyleState.Default);

			int spanStart = 0;
			StyleState spanStyle = stack.Peek();

			if (string.IsNullOrEmpty(source)) {
				return Build(spanStart, spanStyle);
			}

			int i = 0;
			int n = source.Length;
			while (i < n) {
				char c = source[i];

				if (richText && c == '<') {
					int close = source.IndexOf('>', i + 1);
					if (close > i) {
						string raw = source.Substring(i + 1, close - i - 1);
						if (IsTagLike(raw)) {
							string peekName = TagName(raw);
							if (peekName == "sprite" || peekName == "glyph") {
								// <sprite>/<glyph> are self-closing and insert exactly one placeholder char
								// that visually belongs to whatever run is currently open (e.g. inside a
								// <wave>...</wave>) -- do NOT flush/reset spanStart around it like every
								// other tag: that would push the placeholder's char index into the gap
								// between spans, where SpanAt() falls back to the *last* span's style
								// instead of the wave/color/etc. span it's actually inside.
								ApplyTag(raw);
								i = close + 1;
								continue;
							}
							FlushSpan(ref spanStart, ref spanStyle);
							ApplyTag(raw);
							spanStyle = stack.Peek();
							spanStart = sb.Length;
							i = close + 1;
							continue;
						}
					}
				}

				// @ActionName@ -- shorthand for <glyph:ActionName>, always resolved via the active device
				// (GlyphDeviceContext.ActiveDeviceId), no attributes. Only matches a single "word" (no
				// whitespace, no nested '@') so a stray '@' (an email address, "@" used as punctuation)
				// passes through as a literal character instead of being swallowed.
				if (richText && c == '@') {
					int close = source.IndexOf('@', i + 1);
					if (close > i + 1) {
						string actionName = source.Substring(i + 1, close - i - 1);
						if (IsValidActionToken(actionName)) {
							ApplyInlineObjectTag("glyph:" + actionName, isGlyph: true);
							i = close + 1;
							continue;
						}
					}
				}

				if (c == '\r') { i++; continue; }
				sb.Append(c);
				i++;
			}

			FlushSpan(ref spanStart, ref spanStyle);
			return Build(spanStart, spanStyle);
		}

		private MarkupResult Build(int spanStart, StyleState spanStyle) {
			if (spans.Count == 0) {
				spans.Add(new StyleSpan { Start = 0, Length = sb.Length, Style = spanStyle });
			}
			return new MarkupResult {
				Text = sb.ToString(),
				Spans = new List<StyleSpan>(spans),
				Links = new List<LinkRegion>(links),
				Inserts = new List<InlineInsert>(inserts)
			};
		}

		private void FlushSpan(ref int spanStart, ref StyleState spanStyle) {
			int len = sb.Length - spanStart;
			if (len <= 0) return;
			spans.Add(new StyleSpan { Start = spanStart, Length = len, Style = spanStyle });
			spanStart = sb.Length;
		}

		private static bool IsTagLike(string raw) {
			if (raw.Length == 0) return false;
			char first = raw[0];
			if (first == '/') return raw.Length > 1 && char.IsLetter(raw[1]);
			return char.IsLetter(first);
		}

		/// <summary>Tag name only, stopping at the first <c>=</c>/<c>:</c>/whitespace -- unlike a plain
		/// eq/colon split, this also isolates the name correctly when the tag carries space-separated
		/// attributes after it (e.g. <c>wave amp="1.5"</c>, <c>sprite="x" size="1.1"</c>). Safe to use for
		/// every existing tag too: none of their names contain internal spaces, so this agrees with the old
		/// "first =/: only" split for all of them, it just additionally stops at whitespace.</summary>
		private static string TagName(string raw) {
			if (raw.Length == 0) return string.Empty;
			int end = raw.Length;
			for (int k = 0; k < raw.Length; k++) {
				char ch = raw[k];
				if (ch == '=' || ch == ':' || char.IsWhiteSpace(ch)) { end = k; break; }
			}
			return raw.Substring(0, end).ToLowerInvariant();
		}

		/// <summary>The 7 animated <see cref="BuiltinEffect"/> tags that accept per-tag attributes
		/// (<c>amp</c>/<c>freq</c>/<c>speed</c>/<c>once</c>/<c>progress</c>/<c>ease</c>/<c>style</c>/...).</summary>
		private static bool TryGetParameterizedEffect(string tagName, out BuiltinEffect effect) {
			switch (tagName) {
				case "wave": effect = BuiltinEffect.Wave; return true;
				case "shake": effect = BuiltinEffect.Shake; return true;
				case "pulse": effect = BuiltinEffect.Pulse; return true;
				case "rainbow": effect = BuiltinEffect.Rainbow; return true;
				case "glowpulse": effect = BuiltinEffect.Glow; return true;
				case "glitch": effect = BuiltinEffect.Glitch; return true;
				case "blink": effect = BuiltinEffect.Blink; return true;
				default: effect = BuiltinEffect.None; return false;
			}
		}

		private void ApplyTag(string raw) {
			bool closing = raw[0] == '/';
			string body = closing ? raw.Substring(1) : raw;
			string name = TagName(body);

			if (closing) { PopStyle(name); return; }

			// "sprite"/"glyph" are self-closing and never change the style state, so -- unlike every other
			// tag here -- this must NOT push a stack frame: doing so leaves an extra, unmatched frame that
			// the *next* unrelated closing tag (e.g. </color>) would pop instead of its own, letting that
			// color/style leak into everything after it.
			if (name == "sprite" || name == "glyph") {
				ApplyInlineObjectTag(body, isGlyph: name == "glyph");
				return;
			}

			// <wave>/<shake>/<pulse>/<rainbow>/<glowpulse>/<glitch>/<blink> optionally carry space-separated
			// attributes (amp=, freq=, speed=, once=, progress=, ease=, style=, min=/max=/color=/color2= for
			// blink) that override the shared component-wide default preset for just this run -- these DO
			// push a stack frame (they have closing tags), unlike sprite/glyph above.
			if (TryGetParameterizedEffect(name, out BuiltinEffect parameterizedEffect)) {
				StyleState es = stack.Peek();
				ApplyEffectTag(ref es, body, parameterizedEffect);
				stack.Push(es);
				return;
			}

			string value = null;
			int eq = body.IndexOf('=');
			int colon = body.IndexOf(':');
			if (eq >= 0) value = Unquote(body.Substring(eq + 1));
			else if (colon >= 0) value = Unquote(body.Substring(colon + 1));

			StyleState s = stack.Peek();
			switch (name) {
				case "color": if (TryColor(value, out float4 col)) s.Color = col; break;
				case "alpha": if (TryAlpha(value, out float a)) s.Color.w = a; break;
				case "gradient": ApplyGradient(ref s, value); break;
				case "size": ApplySize(ref s, value); break;
				case "weight":
					if (value == "bold" || value == "700") s.Synthesis |= FontSynthesis.Bold;
					else if (value == "light" || value == "300") s.Synthesis |= FontSynthesis.Light;
					break;
				case "b": s.Synthesis |= FontSynthesis.Bold; break;
				case "i": s.Synthesis |= FontSynthesis.Italic; break;
				case "u": s.Underline = true; break;
				case "s": case "strike": s.Strikethrough = true; break;
				case "mark": s.HasMark = true; s.MarkColor = TryColor(value, out float4 mc) ? mc : new float4(1f, 1f, 0f, 0.35f); break;
				case "cspace": s.LetterSpacingEm = ParseEm(value); break;
				case "sub": s.BaselineShift = -0.25f; s.ScaleMultiplier = 0.65f; break;
				case "sup": s.BaselineShift = 0.45f; s.ScaleMultiplier = 0.65f; break;
				case "uppercase": case "allcaps": s.Case = TextCase.Upper; break;
				case "lowercase": s.Case = TextCase.Lower; break;
				case "smallcaps": s.Case = TextCase.SmallCaps; break;
				case "outline": ApplyOutline(ref s, value); break;
				case "shadow": ApplyShadow(ref s, value); break;
				case "glow": ApplyGlow(ref s, value, false); break;
				case "bloom": ApplyGlow(ref s, value, true); break;
				case "link": {
					int id = links.Count;
					links.Add(new LinkRegion { Id = value ?? id.ToString(), Start = sb.Length, Length = 0 });
					s.LinkId = id;
					break;
				}
				default: return; // unknown tag: ignore, do not push
			}

			stack.Push(s);
		}

		private void PopStyle(string name) {
			if (stack.Count <= 1) return;
			if (name == "link") {
				for (int k = links.Count - 1; k >= 0; k--) {
					if (links[k].Length == 0) {
						LinkRegion lr = links[k];
						lr.Length = sb.Length - lr.Start;
						links[k] = lr;
						break;
					}
				}
			}
			stack.Pop();
		}

		/// <summary>Parses attributes for one of the 7 parameterized effect tags (see
		/// <see cref="TryGetParameterizedEffect"/>) and, if any were recognised, resolves a full
		/// <see cref="BuiltinEffectParams"/> starting from <see cref="BuiltinEffectParams.DefaultFor"/> for
		/// <paramref name="effect"/> with just those attributes overridden -- stored on the span as
		/// <see cref="StyleState.EffectParamsOverride"/>. A bare tag (no attributes, e.g. plain <c>&lt;wave&gt;</c>)
		/// leaves <see cref="StyleState.HasEffectParamsOverride"/> false, falling back to the shared default
		/// preset exactly as before per-tag attributes existed.</summary>
		private static void ApplyEffectTag(ref StyleState s, string body, BuiltinEffect effect) {
			s.SpanEffect = effect;
			List<string> tokens = Tokenize(body);
			if (tokens.Count <= 1) return; // just the tag name itself, e.g. "wave" -- no attributes

			BuiltinEffectParams p = BuiltinEffectParams.DefaultFor(effect);
			bool changed = false;

			// two passes: "min"/"max" (Blink's alpha-only shortcut) always apply LAST regardless of where
			// they sit in the tag, so `color="#f00" min="0.2"` and `min="0.2" color="#f00"` behave the same
			// -- color/color2 set the full RGBA, min/max then override just the alpha component.
			for (int t = 1; t < tokens.Count; t++) {
				(string key, string val) = SplitAttribute(tokens[t]);
				if (key == "min" || key == "max") continue;
				if (ApplyEffectAttribute(ref p, effect, key, val)) changed = true;
			}
			for (int t = 1; t < tokens.Count; t++) {
				(string key, string val) = SplitAttribute(tokens[t]);
				if (key != "min" && key != "max") continue;
				if (ApplyEffectAttribute(ref p, effect, key, val)) changed = true;
			}

			if (changed) { s.HasEffectParamsOverride = true; s.EffectParamsOverride = p; }
		}

		private static (string key, string val) SplitAttribute(string tok) {
			int eq = tok.IndexOf('=');
			if (eq >= 0) return (tok.Substring(0, eq).Trim().ToLowerInvariant(), Unquote(tok.Substring(eq + 1)));
			return (tok.Trim().ToLowerInvariant(), null);
		}

		private static bool ApplyEffectAttribute(ref BuiltinEffectParams p, BuiltinEffect effect, string key, string val) {
			switch (key) {
				case "amp": case "amplitude":
					if (TryFloat(val, out float amp)) { p.Amplitude = amp; return true; }
					return false;
				case "freq": case "frequency":
					if (TryFloat(val, out float freq)) { p.Frequency = freq; return true; }
					return false;
				case "speed":
					if (TryFloat(val, out float speed)) { p.Speed = speed; return true; }
					return false;
				case "amount":
					if (TryFloat(val, out float amount)) { p.Amount = Mathf.Clamp01(amount); return true; }
					return false;
				case "angle":
					if (TryFloat(val, out float angle)) { p.Angle = angle; return true; }
					return false;
				case "inverse":
					if (TryBool(val, out bool inv)) { p.Inverse = inv; return true; }
					return false;
				case "once":
					if (TryBool(val, out bool once)) { p.Once = once; return true; }
					return false;
				case "progress":
					if (TryFloat(val, out float prog)) { p.Progress = Mathf.Clamp01(prog); return true; }
					return false;
				case "ease": case "easing":
					if (!string.IsNullOrEmpty(val) && System.Enum.TryParse(val, true, out TextEasing ease)) { p.Easing = ease; return true; }
					return false;
				case "style":
					return ApplyStyleAttribute(ref p, effect, val);
				// Blink's "just alpha" shortcut: leaves ColorA/ColorB RGB at whatever they already are
				// (default preset = neutral white) and only sets the alpha component.
				case "min":
					if (effect == BuiltinEffect.Blink && TryFloat(val, out float minA)) {
						Color c = p.ColorA; c.a = Mathf.Clamp01(minA); p.ColorA = c; return true;
					}
					return false;
				case "max":
					if (effect == BuiltinEffect.Blink && TryFloat(val, out float maxA)) {
						Color c = p.ColorB; c.a = Mathf.Clamp01(maxA); p.ColorB = c; return true;
					}
					return false;
				case "color":
					if (TryColor(val, out float4 colA)) { p.ColorA = new Color(colA.x, colA.y, colA.z, colA.w); return true; }
					return false;
				case "color2":
					if (TryColor(val, out float4 colB)) { p.ColorB = new Color(colB.x, colB.y, colB.z, colB.w); return true; }
					return false;
				default:
					return false;
			}
		}

		private static bool ApplyStyleAttribute(ref BuiltinEffectParams p, BuiltinEffect effect, string val) {
			if (string.IsNullOrEmpty(val)) return false;
			switch (effect) {
				case BuiltinEffect.Wave:
					if (System.Enum.TryParse(val, true, out WaveStyle ws)) { p.WaveStyle = ws; return true; }
					return false;
				case BuiltinEffect.Pulse:
					if (System.Enum.TryParse(val, true, out ScaleStyle ss)) { p.ScaleStyle = ss; return true; }
					return false;
				case BuiltinEffect.Glow:
					if (System.Enum.TryParse(val, true, out GlowStyle gs)) { p.GlowStyle = gs; return true; }
					return false;
				case BuiltinEffect.Glitch:
					if (System.Enum.TryParse(val, true, out GlitchStyle gts)) { p.GlitchStyle = gts; return true; }
					return false;
				default:
					return false;
			}
		}

		private static bool TryFloat(string v, out float f) =>
			float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

		private static bool TryBool(string v, out bool b) {
			b = false;
			if (string.IsNullOrEmpty(v)) return false;
			switch (v.Trim().ToLowerInvariant()) {
				case "true": case "1": case "yes": case "on": b = true; return true;
				case "false": case "0": case "no": case "off": b = false; return true;
				default: return false;
			}
		}

		/// <summary>Parses <c>&lt;sprite="Name" size="1.1"&gt;</c> / <c>&lt;glyph:ActionName sizeabs="24"&gt;</c>.</summary>
		private void ApplyInlineObjectTag(string body, bool isGlyph) {
			List<string> tokens = Tokenize(body);
			string objName = string.Empty;
			float sizeMul = 1f;
			float sizeAbs = 0f;
			string deviceOverride = null;

			for (int t = 0; t < tokens.Count; t++) {
				string tok = tokens[t];
				int eq = tok.IndexOf('=');
				int colon = tok.IndexOf(':');
				string key, val;
				if (eq >= 0 && (colon < 0 || eq < colon)) { key = tok.Substring(0, eq).Trim().ToLowerInvariant(); val = Unquote(tok.Substring(eq + 1)); }
				else if (colon >= 0) { key = tok.Substring(0, colon).Trim().ToLowerInvariant(); val = Unquote(tok.Substring(colon + 1)); }
				else { key = tok.Trim().ToLowerInvariant(); val = null; }

				if (t == 0) {
					// first token is "sprite=Name" / "glyph:ActionName" itself -- val is the object name
					objName = val ?? string.Empty;
					continue;
				}

				switch (key) {
					case "size":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) sizeMul = Mathf.Max(0.01f, m);
						break;
					case "sizeabs":
						if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ab)) sizeAbs = Mathf.Max(0.01f, ab);
						break;
					// device= only makes sense on <glyph:...> (isGlyph); harmlessly ignored on <sprite=...>
					case "device":
						if (isGlyph && !string.IsNullOrEmpty(val)) deviceOverride = val;
						break;
				}
			}

			inserts.Add(new InlineInsert {
				CharIndex = sb.Length,
				IsActionGlyph = isGlyph,
				Name = objName,
				SizeMultiplier = sizeMul,
				AbsoluteSizePx = sizeAbs,
				DeviceOverride = deviceOverride
			});
			sb.Append('￼');
		}

		/// <summary>True for a valid <c>@ActionName@</c> action token: non-empty, no whitespace, no nested
		/// <c>@</c> (both already guaranteed by the caller's <c>IndexOf</c>, checked again for clarity).</summary>
		private static bool IsValidActionToken(string s) {
			if (string.IsNullOrEmpty(s)) return false;
			for (int k = 0; k < s.Length; k++) {
				if (char.IsWhiteSpace(s[k]) || s[k] == '@') return false;
			}
			return true;
		}

		/// <summary>Splits on whitespace outside of single/double quotes, e.g. <c>sprite="Enter Key" size="1.1"</c>
		/// -&gt; [<c>sprite="Enter Key"</c>, <c>size="1.1"</c>].</summary>
		private static List<string> Tokenize(string s) {
			var tokens = new List<string>();
			int i = 0, n = s.Length;
			while (i < n) {
				while (i < n && char.IsWhiteSpace(s[i])) i++;
				if (i >= n) break;
				int start = i;
				bool inQuote = false;
				char quoteChar = '\0';
				while (i < n) {
					char c = s[i];
					if (inQuote) {
						if (c == quoteChar) inQuote = false;
					} else {
						if (c == '"' || c == '\'') { inQuote = true; quoteChar = c; }
						else if (char.IsWhiteSpace(c)) break;
					}
					i++;
				}
				tokens.Add(s.Substring(start, i - start));
			}
			return tokens;
		}

		private static string Unquote(string v) {
			if (v == null) return null;
			v = v.Trim();
			if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0]) v = v.Substring(1, v.Length - 2);
			return v;
		}

		private static bool TryColor(string v, out float4 col) {
			col = new float4(1, 1, 1, 1);
			if (string.IsNullOrEmpty(v)) return false;
			string html = v[0] == '#' ? v : "#" + v;
			if (ColorUtility.TryParseHtmlString(html, out Color c) || ColorUtility.TryParseHtmlString(v, out c)) {
				col = new float4(c.r, c.g, c.b, c.a);
				return true;
			}
			return false;
		}

		private static bool TryAlpha(string v, out float a) {
			a = 1f;
			if (string.IsNullOrEmpty(v)) return false;
			v = v.Trim();
			if (v.StartsWith("#")) {
				if (int.TryParse(v.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)) {
					a = Mathf.Clamp01(hex / 255f);
					return true;
				}
			}
			if (float.TryParse(v.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) {
				a = v.EndsWith("%") ? Mathf.Clamp01(f / 100f) : Mathf.Clamp01(f);
				return true;
			}
			return false;
		}

		private static void ApplyGradient(ref StyleState s, string v) {
			// <gradient=#top,#bottom>                       vertical (default)
			// <gradient=vertical,#a,#b> / <gradient=horizontal,#a,#b>
			// <gradient=#tl,#tr,#bl,#br>                    explicit corners
			if (string.IsNullOrEmpty(v)) return;
			string[] parts = v.Split(',');

			// consume any leading keyword tokens: direction (h/v) and scope (perchar/perword), in any order
			int start = 0;
			bool horizontal = false;
			GradientScope scope = GradientScope.Run;
			while (start < parts.Length) {
				string kw = parts[start].Trim().ToLowerInvariant();
				if (kw == "h" || kw == "horizontal") { horizontal = true; start++; }
				else if (kw == "v" || kw == "vertical") { horizontal = false; start++; }
				else if (kw == "perchar" || kw == "char" || kw == "letter") { scope = GradientScope.PerChar; start++; }
				else if (kw == "perword" || kw == "word" || kw == "run" || kw == "smooth") { scope = GradientScope.Run; start++; }
				else if (kw == "stepped" || kw == "step" || kw == "blocky" || kw == "quantized") { scope = GradientScope.Stepped; start++; }
				else break;
			}
			s.GradientScope = scope;

			int n = parts.Length - start;
			float4 C(int k) => TryColor(parts[start + k].Trim(), out float4 c) ? c : new float4(1, 1, 1, 1);

			if (n == 2) {
				float4 a = C(0);
				float4 b = C(1);
				if (horizontal) {
					s.GradientTopLeft = s.GradientBottomLeft = a;
					s.GradientTopRight = s.GradientBottomRight = b;
				} else {
					s.GradientTopLeft = s.GradientTopRight = a;
					s.GradientBottomLeft = s.GradientBottomRight = b;
				}
				s.HasGradient = true;
			} else if (n >= 4) {
				s.GradientTopLeft = C(0);
				s.GradientTopRight = C(1);
				s.GradientBottomLeft = C(2);
				s.GradientBottomRight = C(3);
				s.HasGradient = true;
			}
		}

		private static void ApplySize(ref StyleState s, string v) {
			if (string.IsNullOrEmpty(v)) return;
			v = v.Trim();
			if (v.EndsWith("%")) {
				if (float.TryParse(v.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct)) {
					s.SizeMultiplier = Mathf.Max(0.01f, pct / 100f);
					s.AbsoluteSizePx = 0f;
				}
				return;
			}
			if (v.EndsWith("x")) {
				if (float.TryParse(v.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out float mul)) {
					s.SizeMultiplier = Mathf.Max(0.01f, mul);
					s.AbsoluteSizePx = 0f;
				}
				return;
			}
			if (v.StartsWith("+") || v.StartsWith("-")) {
				// relative delta is resolved by layout against the base size; store as multiplier hint via absolute later
			}
			if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)) {
				s.AbsoluteSizePx = Mathf.Max(1f, px);
			}
		}

		private static void ApplyOutline(ref StyleState s, string v) {
			s.HasOutline = true;
			s.OutlineColor = new float4(0f, 0f, 0f, 1f);
			s.OutlineWidth = 0.2f;
			if (string.IsNullOrEmpty(v)) return;
			string[] p = v.Split(',');
			if (TryColor(p[0].Trim(), out float4 c)) s.OutlineColor = c;
			if (p.Length > 1 && float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
				s.OutlineWidth = Mathf.Clamp(w, 0.01f, 0.5f);
		}

		private static void ApplyShadow(ref StyleState s, string v) {
			s.HasShadow = true;
			s.ShadowColor = new float4(0f, 0f, 0f, 0.6f);
			s.ShadowOffsetEm = new float2(0.06f, -0.06f);
			s.ShadowSoftness = 0.08f;
			if (string.IsNullOrEmpty(v)) return;
			string[] p = v.Split(',');
			if (TryColor(p[0].Trim(), out float4 c)) s.ShadowColor = c;
			if (p.Length > 2
				&& float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dx)
				&& float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dy)) {
				s.ShadowOffsetEm = new float2(dx, dy);
			}
			if (p.Length > 3 && float.TryParse(p[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float soft))
				s.ShadowSoftness = Mathf.Clamp(soft, 0f, 0.5f);
		}

		private static void ApplyGlow(ref StyleState s, string v, bool bloom) {
			s.HasGlow = true;
			s.GlowBloom = bloom;
			s.GlowColor = new float4(1f, 0.92f, 0.65f, 1f);
			s.GlowRadius = bloom ? 1f : 0.6f;
			s.GlowIntensity = bloom ? 2f : 1f;
			if (string.IsNullOrEmpty(v)) return;
			string[] p = v.Split(',');
			int numIdx = 0;
			for (int i = 0; i < p.Length; i++) {
				string tok = p[i].Trim();
				if (tok.Length == 0) continue;
				// "bloom" keyword may sit anywhere in the arg list of a plain <glow> tag
				if (string.Equals(tok, "bloom", System.StringComparison.OrdinalIgnoreCase)) { s.GlowBloom = true; continue; }
				if (i == 0 && TryColor(tok, out float4 c)) { s.GlowColor = c; continue; }
				if (numIdx == 0 && float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)) {
					s.GlowRadius = Mathf.Clamp(r, 0.05f, 1f); numIdx++; continue;
				}
				if (numIdx == 1 && float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float it)) {
					s.GlowIntensity = Mathf.Clamp(it, 0f, 4f); numIdx++; continue;
				}
			}
		}

		private static float ParseEm(string v) {
			if (string.IsNullOrEmpty(v)) return 0f;
			v = v.Trim().Replace("em", "");
			return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
		}
	}
}
