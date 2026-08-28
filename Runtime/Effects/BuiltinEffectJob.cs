using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Sperlich.Text {

	/// <summary>Tunables for one built-in effect instance.</summary>
	[System.Serializable]
	public struct BuiltinEffectParams {
		public BuiltinEffect Effect;
		public float Amplitude;   // px for spatial effects, unit-less for others
		public float Frequency;   // waves per glyph (spatial) / cycles per second (temporal)
		public float Speed;       // scroll speed along the string
		public UnityEngine.Color ColorA;
		public UnityEngine.Color ColorB;

		public static BuiltinEffectParams Wave => new BuiltinEffectParams { Effect = BuiltinEffect.Wave, Amplitude = 6f, Frequency = 0.35f, Speed = 6f };
		public static BuiltinEffectParams Shake => new BuiltinEffectParams { Effect = BuiltinEffect.Shake, Amplitude = 2.5f, Frequency = 30f, Speed = 1f };
		public static BuiltinEffectParams Pulse => new BuiltinEffectParams { Effect = BuiltinEffect.Pulse, Amplitude = 0.12f, Frequency = 2f, Speed = 4f };
		public static BuiltinEffectParams Rainbow => new BuiltinEffectParams { Effect = BuiltinEffect.Rainbow, Amplitude = 1f, Frequency = 0.08f, Speed = 1.5f };
		public static BuiltinEffectParams Glow => new BuiltinEffectParams { Effect = BuiltinEffect.Glow, Amplitude = 0.4f, Frequency = 1.5f, Speed = 1f, ColorA = new UnityEngine.Color(0.55f, 0.55f, 0.55f, 1f), ColorB = UnityEngine.Color.white };
		public static BuiltinEffectParams Glitch => new BuiltinEffectParams { Effect = BuiltinEffect.Glitch, Amplitude = 3f, Frequency = 12f, Speed = 1f };
	}

	/// <summary>
	/// Ebene 2 fast path (plan module 9.1 / 16): the fixed built-in catalog as a Burst
	/// <see cref="IJobParallelFor"/> over glyph quads. Enum switch, no virtual dispatch.
	/// Each quad owns a disjoint 4-vertex slice, so parallel writes are race-free.
	/// </summary>
	[BurstCompile]
	public struct BuiltinEffectJob : IJobParallelFor {

		[NativeDisableParallelForRestriction] public NativeArray<TextVertex> Vertices;
		[ReadOnly] public NativeArray<int> QuadStart;
		[ReadOnly] public NativeArray<int> QuadSource;
		[ReadOnly] public NativeArray<int> QuadEffect;

		/// <summary>-1 = apply to every quad (component-level). Otherwise only quads whose span effect matches.</summary>
		public int EffectFilter;

		public BuiltinEffect Effect;
		public float Time;
		public float Amplitude;
		public float Frequency;
		public float Speed;
		public float4 ColorA;
		public float4 ColorB;
		public int TotalChars;

		public void Execute(int quad) {
			if (EffectFilter >= 0 && QuadEffect[quad] != EffectFilter) return;

			int s = QuadStart[quad];
			int src = QuadSource[quad];
			float phase = src < 0 ? quad : src;

			switch (Effect) {
				case BuiltinEffect.Wave: {
					float y = math.sin((phase * Frequency) + Time * Speed) * Amplitude;
					Offset(s, new float2(0f, y));
					break;
				}
				case BuiltinEffect.Shake: {
					float t = Time * Frequency + phase * 7.13f;
					float2 o = new float2(Hash(t) - 0.5f, Hash(t + 19.7f) - 0.5f) * (2f * Amplitude);
					Offset(s, o);
					break;
				}
				case BuiltinEffect.Pulse: {
					float m = 1f + math.sin(Time * Speed + phase * Frequency) * Amplitude;
					ScaleAboutCenter(s, m);
					break;
				}
				case BuiltinEffect.Rainbow: {
					float h = math.frac(phase * Frequency + Time * Speed * 0.1f);
					MulColor(s, HsvToRgb(h, 0.85f, 1f));
					break;
				}
				case BuiltinEffect.Glow: {
					float g = 0.5f + 0.5f * math.sin(Time * Frequency + phase * 0.2f);
					float4 c = math.lerp(ColorA, ColorB, g);
					c.w = 1f;
					MulColor(s, c);
					break;
				}
				case BuiltinEffect.Glitch: {
					float slice = math.step(0.82f, Hash(math.floor(Time * Frequency) + phase));
					Offset(s, new float2(slice * (Hash(Time + phase) - 0.5f) * 2f * Amplitude, 0f));
					if (slice > 0.5f) MulColor(s, new float4(1f, 0.4f, 0.6f, 1f));
					break;
				}
			}
		}

		private void Offset(int s, float2 d) {
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				v.position.x += d.x;
				v.position.y += d.y;
				Vertices[s + i] = v;
			}
		}

		private void ScaleAboutCenter(int s, float m) {
			float3 c = (Vertices[s].position + Vertices[s + 1].position + Vertices[s + 2].position + Vertices[s + 3].position) * 0.25f;
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				v.position.x = c.x + (v.position.x - c.x) * m;
				v.position.y = c.y + (v.position.y - c.y) * m;
				Vertices[s + i] = v;
			}
		}

		private void MulColor(int s, float4 m) {
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				v.color *= m;
				Vertices[s + i] = v;
			}
		}

		private static float Hash(float n) {
			return math.frac(math.sin(n * 12.9898f) * 43758.5453f);
		}

		private static float4 HsvToRgb(float h, float s, float v) {
			float3 k = new float3(1f, 2f / 3f, 1f / 3f);
			float3 p = math.abs(math.frac(new float3(h) + k) * 6f - 3f);
			float3 rgb = v * math.lerp(new float3(1f), math.clamp(p - 1f, 0f, 1f), s);
			return new float4(rgb, 1f);
		}
	}
}
