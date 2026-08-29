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
							FlushSpan(ref spanStart, ref spanStyle);
							ApplyTag(raw);
							spanStyle = stack.Peek();
							spanStart = sb.Length;
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

		private void ApplyTag(string raw) {
			bool closing = raw[0] == '/';
			string body = closing ? raw.Substring(1) : raw;

			string name;
			string value = null;
			int eq = body.IndexOf('=');
			int colon = body.IndexOf(':');
			if (eq >= 0) { name = body.Substring(0, eq).Trim().ToLowerInvariant(); value = Unquote(body.Substring(eq + 1)); }
			else if (colon >= 0) { name = body.Substring(0, colon).Trim().ToLowerInvariant(); value = Unquote(body.Substring(colon + 1)); }
			else name = body.Trim().ToLowerInvariant();

			if (closing) { PopStyle(name); return; }

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
				case "wave": s.SpanEffect = BuiltinEffect.Wave; break;
				case "shake": s.SpanEffect = BuiltinEffect.Shake; break;
				case "pulse": s.SpanEffect = BuiltinEffect.Pulse; break;
				case "rainbow": s.SpanEffect = BuiltinEffect.Rainbow; break;
				case "glowpulse": s.SpanEffect = BuiltinEffect.Glow; break;
				case "glitch": s.SpanEffect = BuiltinEffect.Glitch; break;
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
				case "sprite":
					inserts.Add(new InlineInsert { CharIndex = sb.Length, IsActionGlyph = false, Name = value ?? string.Empty });
					sb.Append('￼');
					break;
				case "glyph":
					inserts.Add(new InlineInsert { CharIndex = sb.Length, IsActionGlyph = true, Name = value ?? string.Empty });
					sb.Append('￼');
					break;
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
