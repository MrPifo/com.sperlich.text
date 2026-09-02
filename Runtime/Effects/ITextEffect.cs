using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Ebene 1 effect contract (plan module 9.1): a plain, main-thread script that mutates the glyph
	/// quads each frame. Simple and debuggable, no Burst / NativeArray knowledge required. The built-in
	/// catalog additionally has a Burst fast path (<see cref="BuiltinEffectJob"/>) selected by enum.
	/// </summary>
	public interface ITextEffect {
		/// <summary>Called once per frame after the mesh is built, before it is uploaded.</summary>
		void Apply(TextEffectContext ctx);
	}

	/// <summary>
	/// Per-glyph mutation surface handed to <see cref="ITextEffect"/>. Wraps the native vertex buffer
	/// of <see cref="TextMeshBuilder"/>; all edits are applied in place.
	/// </summary>
	public struct TextEffectContext {

		private NativeList<TextVertex> vertices;
		private NativeArray<int> quadStart;
		private NativeArray<int> quadSource;

		public float Time { get; private set; }
		public float DeltaTime { get; private set; }
		public int TotalSourceChars { get; private set; }

		/// <summary>Number of glyph quads currently in the mesh (whitespace excluded).</summary>
		public int GlyphCount => quadStart.Length;

		internal TextEffectContext(NativeList<TextVertex> vertices, NativeArray<int> quadStart, NativeArray<int> quadSource,
			float time, float deltaTime, int totalSourceChars) {
			this.vertices = vertices;
			this.quadStart = quadStart;
			this.quadSource = quadSource;
			Time = time;
			DeltaTime = deltaTime;
			TotalSourceChars = totalSourceChars;
		}

		/// <summary>Stripped-text index of glyph quad <paramref name="glyph"/> (-1 for injected glyphs).</summary>
		public int SourceIndex(int glyph) => (uint)glyph < (uint)quadSource.Length ? quadSource[glyph] : -1;

		public Vector2 GetCenter(int glyph) {
			int s = quadStart[glyph];
			float3 sum = vertices[s].position + vertices[s + 1].position + vertices[s + 2].position + vertices[s + 3].position;
			return new Vector2(sum.x * 0.25f, sum.y * 0.25f);
		}

		public void Translate(int glyph, Vector2 delta) {
			int s = quadStart[glyph];
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.position.x += delta.x;
				v.position.y += delta.y;
				vertices[s + i] = v;
			}
		}

		public void Rotate(int glyph, float radians) {
			int s = quadStart[glyph];
			Vector2 c = GetCenter(glyph);
			float sn = math.sin(radians);
			float cs = math.cos(radians);
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				float dx = v.position.x - c.x;
				float dy = v.position.y - c.y;
				v.position.x = c.x + dx * cs - dy * sn;
				v.position.y = c.y + dx * sn + dy * cs;
				vertices[s + i] = v;
			}
		}

		/// <summary>Scales glyph quad <paramref name="glyph"/> uniformly about its center by <paramref name="mul"/>.</summary>
		public void Scale(int glyph, float mul) {
			Scale(glyph, new Vector2(mul, mul));
		}

		/// <summary>Scales glyph quad <paramref name="glyph"/> non-uniformly about its center by <paramref name="scale"/> (X, Y).</summary>
		public void Scale(int glyph, Vector2 scale) {
			int s = quadStart[glyph];
			Vector2 c = GetCenter(glyph);
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.position.x = c.x + (v.position.x - c.x) * scale.x;
				v.position.y = c.y + (v.position.y - c.y) * scale.y;
				vertices[s + i] = v;
			}
		}

		/// <summary>Applies a squash & stretch wobble deformation to glyph <paramref name="glyph"/> (conserves visual area).</summary>
		public void SquashAndStretch(int glyph, float stretchY) {
			float y = math.max(0.01f, 1f + stretchY);
			float x = 1f / y;
			Scale(glyph, new Vector2(x, y));
		}

		/// <summary>Evaluates an easing function at normalized time <paramref name="t"/> in 0..1.</summary>
		public static float EvaluateEasing(float t, TextEasing easing) {
			t = math.saturate(t);
			switch (easing) {
				case TextEasing.Linear:
					return t;
				case TextEasing.Sine:
					return math.sin(t * (math.PI * 0.5f));
				case TextEasing.EaseOutBack: {
					const float c1 = 1.70158f;
					const float c3 = c1 + 1f;
					float tMinus1 = t - 1f;
					return 1f + c3 * tMinus1 * tMinus1 * tMinus1 + c1 * tMinus1 * tMinus1;
				}
				case TextEasing.EaseOutBounce: {
					const float n1 = 7.5625f;
					const float d1 = 2.75f;
					if (t < 1f / d1) {
						return n1 * t * t;
					} else if (t < 2f / d1) {
						t -= 1.5f / d1;
						return n1 * t * t + 0.75f;
					} else if (t < 2.5f / d1) {
						t -= 2.25f / d1;
						return n1 * t * t + 0.9375f;
					} else {
						t -= 2.625f / d1;
						return n1 * t * t + 0.984375f;
					}
				}
				case TextEasing.EaseOutElastic: {
					const float c4 = (2f * math.PI) / 3f;
					if (t <= 0f) return 0f;
					if (t >= 1f) return 1f;
					return math.pow(2f, -10f * t) * math.sin((t * 10f - 0.75f) * c4) + 1f;
				}
				case TextEasing.EaseInOutSine:
					return -(math.cos(math.PI * t) - 1f) * 0.5f;
				default:
					return t;
			}
		}

		public void SetColor(int glyph, Color color) {
			int s = quadStart[glyph];
			float4 c = new float4(color.r, color.g, color.b, color.a);
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.color = c;
				vertices[s + i] = v;
			}
		}

		public void MultiplyColor(int glyph, Color color) {
			int s = quadStart[glyph];
			float4 m = new float4(color.r, color.g, color.b, color.a);
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.color *= m;
				vertices[s + i] = v;
			}
		}

		public void SetAlpha(int glyph, float alpha) {
			int s = quadStart[glyph];
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.color.w = alpha;
				vertices[s + i] = v;
			}
		}
	}
}
