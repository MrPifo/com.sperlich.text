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

		public void Scale(int glyph, float mul) {
			int s = quadStart[glyph];
			Vector2 c = GetCenter(glyph);
			for (int i = 0; i < 4; i++) {
				TextVertex v = vertices[s + i];
				v.position.x = c.x + (v.position.x - c.x) * mul;
				v.position.y = c.y + (v.position.y - c.y) * mul;
				vertices[s + i] = v;
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
