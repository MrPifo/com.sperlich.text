using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Sperlich.Text {

	/// <summary>Tunables for one built-in effect instance.</summary>
	[System.Serializable]
	public struct BuiltinEffectParams {
		/// <summary>Gibt an, ob dieser Effekt aktiv ist.</summary>
		public bool Enabled;

		public BuiltinEffect Effect;
		public float Amplitude;   // px for spatial effects, unit-less for others
		public float Frequency;   // Wave: spatial wavelength · Glow: A<->B crossfade sharpness · Glitch/Rainbow: spread
		public float Speed;       // temporal speed (Glow: fade animation speed · Glitch: colour cycle)
		/// <summary>Anteil betroffener Zeichen (0..1 bzw. 0..100%, z. B. für Glitch-Wahrscheinlichkeit).</summary>
		[UnityEngine.Range(0f, 1f)]
		public float Amount;

		public UnityEngine.Color ColorA;
		public UnityEngine.Color ColorB;

		/// <summary>Colour ramp for Rainbow / Glitch. <c>null</c> (or empty) = built-in HSV rainbow.</summary>
		public UnityEngine.Gradient Ramp;

		/// <summary>Erstellt einen 8-Stop-Regenbogen-Gradienten mit nahtlosem Farbkreis (Rot, Orange, Gelb, Grün, Cyan, Blau, Violett, Rot).</summary>
		public static UnityEngine.Gradient CreateRainbowGradient() {
			var g = new UnityEngine.Gradient();
			g.SetKeys(
				new[] {
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 0.1f, 0.1f), 0f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 0.55f, 0f), 1f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 0.92f, 0.05f), 2f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.1f, 0.85f, 0.2f), 3f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0f, 0.8f, 1f), 4f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.15f, 0.35f, 1f), 5f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.7f, 0.15f, 0.95f), 6f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 0.1f, 0.1f), 1f),
				},
				new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
			return g;
		}

		public static BuiltinEffectParams Wave => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Wave, Amplitude = 6f, Frequency = 0.35f, Speed = 6f };
		public static BuiltinEffectParams Shake => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Shake, Amplitude = 2.5f, Frequency = 30f, Speed = 1f };
		public static BuiltinEffectParams Pulse => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Pulse, Amplitude = 0.12f, Frequency = 2f, Speed = 4f };
		public static BuiltinEffectParams Rainbow => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Rainbow, Amplitude = 1f, Frequency = 0.04f, Speed = 1.5f, Ramp = CreateRainbowGradient() };
		public static BuiltinEffectParams Glow => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glow, Amplitude = 0.4f, Frequency = 3f, Speed = 1.5f, ColorA = new UnityEngine.Color(0.55f, 0.55f, 0.55f, 1f), ColorB = UnityEngine.Color.white };
		public static BuiltinEffectParams Glitch => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glitch, Amplitude = 3f, Frequency = 12f, Speed = 1f, Amount = 0.25f, Ramp = CreateRainbowGradient() };
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
		public float Amount;
		public float4 ColorA;
		public float4 ColorB;
		public int TotalChars;

		/// <summary>Colour ramp LUT (evenly spaced samples) for Rainbow / Glitch. Always populated.</summary>
		[ReadOnly] public NativeArray<float4> Ramp;
		public int RampLen;

		public void Execute(int quad) {
			if (EffectFilter >= 0 && QuadEffect[quad] != EffectFilter) return;

			int s = QuadStart[quad];
			int src = QuadSource[quad];
			float phase = src < 0 ? quad : src;

			switch (Effect) {
				case BuiltinEffect.Wave: {
					// Frequency = spatial wavelength (how many waves run across the text), Amplitude = height.
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
					// colour comes from the ramp (default = HSV rainbow); Frequency spreads it across glyphs.
					float t = math.frac(phase * Frequency + Time * Speed * 0.1f);
					MulColor(s, SampleRamp(t));
					break;
				}
				case BuiltinEffect.Glow: {
					// Speed = fade animation speed, Frequency = how sharp the A<->B crossfade is.
					float s01 = 0.5f + 0.5f * math.sin(Time * Speed + phase * 0.2f);
					float k = math.max(0.05f, Frequency);
					float blend = math.saturate((s01 - 0.5f) * k + 0.5f);
					float4 c = math.lerp(ColorA, ColorB, blend);
					c.w = 1f;
					MulColor(s, c);
					break;
				}
				case BuiltinEffect.Glitch: {
					// each glyph decides for itself (phase in the hash) -> per-letter shake + colour.
					float cell = math.floor(Time * math.max(0.01f, Frequency));
					float roll = Hash(cell * 1.37f + phase * 2.11f);
					float threshold = 1f - math.saturate(Amount);
					if (roll >= threshold && Amount > 0f) {
						float jx = (Hash(Time * 3.1f + phase) - 0.5f) * 2f;
						float jy = (Hash(Time * 2.3f + phase + 5f) - 0.5f) * 2f;
						Offset(s, new float2(jx * Amplitude, jy * Amplitude * 0.5f));
						float rampT = math.frac(roll * 3.37f + Time * Speed * 0.2f);
						MulColor(s, SampleRamp(rampT));
					}
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

		/// <summary>Linear sample of the colour-ramp LUT at <paramref name="t"/> in 0..1.</summary>
		private float4 SampleRamp(float t) {
			if (RampLen <= 1) return new float4(1f, 1f, 1f, 1f);
			float f = math.saturate(t) * (RampLen - 1);
			int i0 = (int)f;
			int i1 = math.min(i0 + 1, RampLen - 1);
			return math.lerp(Ramp[i0], Ramp[i1], f - i0);
		}

		private static float Hash(float n) {
			return math.frac(math.sin(n * 12.9898f) * 43758.5453f);
		}
	}
}
