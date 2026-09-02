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
		public WaveStyle WaveStyle;
		public RotateStyle RotateStyle;
		public ScaleStyle ScaleStyle;
		public TextEasing Easing;
		public GlowStyle GlowStyle;
		public GlitchStyle GlitchStyle;

		public float Amplitude;   // px for spatial effects, unit-less for others
		public float Frequency;   // Wave: spatial wavelength · Glow: A<->B crossfade sharpness · Glitch/Rainbow: spread
		public float Speed;       // temporal speed (Glow: fade animation speed · Glitch: colour cycle)
		/// <summary>Anteil betroffener Zeichen (0..1 bzw. 0..100%, z. B. für Glitch-Wahrscheinlichkeit).</summary>
		[UnityEngine.Range(0f, 1f)]
		public float Amount;

		/// <summary>Richtungsumkehr (z. B. Welle schwingt in die andere Richtung).</summary>
		public bool Inverse;

		/// <summary>Effekt wird nur 1x ausgeführt statt endlos (z. B. Popup-Welle).</summary>
		public bool Once;

		/// <summary>Manueller Animationsfortschritt (0..1) für Once-Effekte.</summary>
		[UnityEngine.Range(0f, 1f)]
		public float Progress;

		/// <summary>Glanz-, Dreh- oder Bewegungswinkel in Grad (z. B. für Shimmer Lichtstrahl-Neigung oder PopIn-Start-Rotation).</summary>
		public float Angle;

		/// <summary>Optionaler Zeichensatz für den Matrix-Effekt. Leer = Standard (a-z, A-Z, 0-9 und Symbole).</summary>
		public string ScrambleCharacters;

		public UnityEngine.Color ColorA;
		public UnityEngine.Color ColorB;

		/// <summary>Colour ramp for Rainbow / Glitch / Shimmer. <c>null</c> (or empty) = built-in HSV rainbow.</summary>
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
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.1f, 0.4f, 1f), 4f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.55f, 0.15f, 0.95f), 5f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.9f, 0.1f, 0.6f), 6f / 7f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 0.1f, 0.1f), 1f),
				},
				new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
			return g;
		}

		/// <summary>Erstellt einen warmen Gold-Shimmer-Farbverlauf.</summary>
		public static UnityEngine.Gradient CreateGoldShimmerGradient() {
			var g = new UnityEngine.Gradient();
			g.SetKeys(
				new[] {
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 1f, 1f), 0f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1.15f, 1.05f, 0.7f), 0.25f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1.4f, 1.25f, 0.9f), 0.5f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1.15f, 1.05f, 0.7f), 0.75f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(1f, 1f, 1f), 1f),
				},
				new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
			return g;
		}

		/// <summary>Erstellt einen digitalen Matrix-Grün-Farbverlauf.</summary>
		public static UnityEngine.Gradient CreateMatrixGradient() {
			var g = new UnityEngine.Gradient();
			g.SetKeys(
				new[] {
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.1f, 0.5f, 0.15f), 0f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.2f, 1f, 0.3f), 0.5f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.8f, 1f, 0.85f), 0.85f),
					new UnityEngine.GradientColorKey(new UnityEngine.Color(0.2f, 1f, 0.3f), 1f),
				},
				new[] { new UnityEngine.GradientAlphaKey(1f, 0f), new UnityEngine.GradientAlphaKey(1f, 1f) });
			return g;
		}

		public static BuiltinEffectParams Wave => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Wave, WaveStyle = WaveStyle.Sine, Amplitude = 6f, Frequency = 0.35f, Speed = 6f };
		public static BuiltinEffectParams Wobble => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Pulse, ScaleStyle = ScaleStyle.SquashAndStretch, Amplitude = 0.25f, Frequency = 0.35f, Speed = 5f, Easing = TextEasing.EaseOutBack };
		public static BuiltinEffectParams Rotate => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Rotate, RotateStyle = RotateStyle.Wobble, Amplitude = 20f, Frequency = 0.35f, Speed = 4f };
		public static BuiltinEffectParams Shake => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Shake, Amplitude = 2.5f, Frequency = 30f, Speed = 1f };
		public static BuiltinEffectParams Pulse => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Pulse, ScaleStyle = ScaleStyle.Pulse, Amplitude = 0.12f, Frequency = 2f, Speed = 4f };
		public static BuiltinEffectParams Rainbow => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Rainbow, Amplitude = 1f, Frequency = 0.04f, Speed = 1.5f, Ramp = CreateRainbowGradient() };
		public static BuiltinEffectParams Glow => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glow, GlowStyle = GlowStyle.Fade, Amplitude = 0.4f, Frequency = 3f, Speed = 1.5f, ColorA = new UnityEngine.Color(0.55f, 0.55f, 0.55f, 1f), ColorB = UnityEngine.Color.white, Ramp = CreateGoldShimmerGradient() };
		public static BuiltinEffectParams Shimmer => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glow, GlowStyle = GlowStyle.Shimmer, Amplitude = 0.25f, Frequency = 1f, Speed = 2f, Angle = 25f, Ramp = CreateGoldShimmerGradient() };
		public static BuiltinEffectParams Glitch => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glitch, GlitchStyle = GlitchStyle.Glitch, Amplitude = 3f, Frequency = 12f, Speed = 1f, Amount = 0.25f, Ramp = CreateRainbowGradient() };
		public static BuiltinEffectParams Matrix => new BuiltinEffectParams { Enabled = true, Effect = BuiltinEffect.Glitch, GlitchStyle = GlitchStyle.Matrix, Amplitude = 1.5f, Frequency = 0.15f, Speed = 2f, Amount = 0.4f, Inverse = false, Ramp = CreateMatrixGradient() };
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
		public WaveStyle WaveStyle;
		public RotateStyle RotateStyle;
		public ScaleStyle ScaleStyle;
		public TextEasing Easing;
		public GlowStyle GlowStyle;
		public GlitchStyle GlitchStyle;

		public float Time;
		public float Amplitude;
		public float Frequency;
		public float Speed;
		public float Amount;
		public float Angle;
		public bool Inverse;
		public bool Once;
		public float Progress;
		public float4 ColorA;
		public float4 ColorB;
		public int TotalChars;
		public float ProjectedMin;
		public float ProjectedMax;

		/// <summary>Resolved atlas rects (u0, v0, u1, v1) of characters in the scramble pool for Matrix.</summary>
		[ReadOnly] public NativeArray<float4> ScrambleAtlas;
		public int ScrambleAtlasLen;

		/// <summary>Colour ramp LUT (evenly spaced samples) for Rainbow / Glitch / Shimmer. Always populated.</summary>
		[ReadOnly] public NativeArray<float4> Ramp;
		public int RampLen;

		public void Execute(int quad) {
			if (EffectFilter >= 0 && QuadEffect[quad] != EffectFilter) return;

			int s = QuadStart[quad];
			int src = QuadSource[quad];
			float phase = src < 0 ? quad : src;

			switch (Effect) {
				case BuiltinEffect.Wave: {
					// Frequency = spatial wavelength / phase step, Amplitude = height.
					float p = Inverse ? ((TotalChars > 0 ? (TotalChars - 1 - phase) : -phase)) : phase;
					float y = 0f;
					if (Once) {
						float maxChars = math.max(0, TotalChars - 1);
						float totalSpan = maxChars * Frequency + 2f * math.PI;
						float t = Progress * totalSpan;
						float angle = t - p * Frequency;
						if (angle >= 0f && angle <= 2f * math.PI) {
							switch (WaveStyle) {
								case WaveStyle.Sine:
									y = math.sin(angle) * Amplitude;
									break;
								case WaveStyle.Bounce: {
									// Parabolic hop: 0..0.7 is the primary hop, 0.7..1.0 is the small secondary settle bounce
									float norm = angle / (2f * math.PI);
									if (norm < 0.7f) {
										float hopT = norm / 0.7f;
										y = math.sin(hopT * math.PI) * Amplitude;
									} else {
										float bounceT = (norm - 0.7f) / 0.3f;
										y = math.sin(bounceT * math.PI) * (Amplitude * 0.25f);
									}
									break;
								}
								case WaveStyle.Triangle: {
									float cycle = angle / (2f * math.PI);
									y = (cycle < 0.5f ? (cycle * 4f - 1f) : (3f - cycle * 4f)) * Amplitude;
									break;
								}
							}
						}
					} else {
						float angle = p * Frequency + Time * Speed;
						switch (WaveStyle) {
							case WaveStyle.Sine:
								y = math.sin(angle) * Amplitude;
								break;
							case WaveStyle.Bounce:
								y = math.abs(math.sin(angle)) * Amplitude;
								break;
							case WaveStyle.Triangle: {
								float frac = math.frac(angle / (2f * math.PI));
								y = (frac < 0.5f ? (frac * 4f - 1f) : (3f - frac * 4f)) * Amplitude;
								break;
							}
						}
					}
					Offset(s, new float2(0f, y));
					break;
				}
				case BuiltinEffect.Shake: {
					// 2D discrete pseudo-random jitter. Frequency = shake rate.
					float t = math.floor(Time * Frequency) + phase * 7.13f;
					float2 o = new float2(Hash(t) - 0.5f, Hash(t + 19.7f) - 0.5f) * (2f * Amplitude);
					Offset(s, o);
					break;
				}
				case BuiltinEffect.Pulse: {
					float p = Inverse ? ((TotalChars > 0 ? (TotalChars - 1 - phase) : -phase)) : phase;
					float scaleX = 1f;
					float scaleY = 1f;

					switch (ScaleStyle) {
						case ScaleStyle.Pulse: {
							if (Once) {
								float maxChars = math.max(0, TotalChars - 1);
								float totalSpan = maxChars * Frequency + 2f * math.PI;
								float t = Progress * totalSpan;
								float angle = t - p * Frequency;
								float m = (angle >= 0f && angle <= 2f * math.PI) ? (1f + math.sin(angle) * Amplitude) : 1f;
								scaleX = scaleY = m;
							} else {
								float m = 1f + math.sin(Time * Speed + p * Frequency) * Amplitude;
								scaleX = scaleY = m;
							}
							break;
						}
						case ScaleStyle.SquashAndStretch: {
							// Wobble: Y stretches by (1 + sin*amp), X compresses inversely to conserve area
							float wave;
							if (Once) {
								float maxChars = math.max(0, TotalChars - 1);
								float totalSpan = maxChars * Frequency + 2f * math.PI;
								float t = Progress * totalSpan;
								float angle = t - p * Frequency;
								wave = (angle >= 0f && angle <= 2f * math.PI) ? math.sin(angle) * Amplitude : 0f;
							} else {
								wave = math.sin(Time * Speed + p * Frequency) * Amplitude;
							}
							scaleY = 1f + wave;
							scaleX = 1f / math.max(0.01f, scaleY);
							break;
						}
						case ScaleStyle.PopIn: {
							// Appear: scaling from 0 to 1 with Easing and optional initial rotation
							float maxChars = math.max(0, TotalChars - 1);
							float totalSpan = maxChars * Frequency + 1f;
							float t = Once ? (Progress * totalSpan) : math.fmod(Time * Speed, totalSpan + 0.5f);
							float rawT = t - p * Frequency;
							float eased = rawT <= 0f ? 0f : (rawT >= 1f ? 1f : EvaluateEasing(rawT, Easing));
							scaleX = scaleY = math.max(0f, eased);
							if (Angle != 0f) {
								float rotRad = math.radians(Angle) * (1f - eased);
								RotateAboutCenter(s, rotRad);
							}
							break;
						}
						case ScaleStyle.PopOut: {
							// Disappear: scaling from 1 to 0 with Easing and optional exit rotation
							float maxChars = math.max(0, TotalChars - 1);
							float totalSpan = maxChars * Frequency + 1f;
							float t = Once ? (Progress * totalSpan) : math.fmod(Time * Speed, totalSpan + 0.5f);
							float rawT = t - p * Frequency;
							float eased = rawT <= 0f ? 1f : (rawT >= 1f ? 0f : 1f - EvaluateEasing(rawT, Easing));
							scaleX = scaleY = math.max(0f, eased);
							if (Angle != 0f) {
								float rotRad = math.radians(Angle) * (1f - eased);
								RotateAboutCenter(s, rotRad);
							}
							break;
						}
					}

					ScaleAboutCenter(s, new float2(scaleX, scaleY));
					break;
				}
				case BuiltinEffect.Rotate: {
					float p = Inverse ? ((TotalChars > 0 ? (TotalChars - 1 - phase) : -phase)) : phase;
					float rotRad = 0f;
					switch (RotateStyle) {
						case RotateStyle.Wobble: {
							// Pendulum swing / wobble: Amplitude is peak angle in degrees
							float maxRad = math.radians(Amplitude);
							if (Once) {
								float maxChars = math.max(0, TotalChars - 1);
								float totalSpan = maxChars * Frequency + 2f * math.PI;
								float t = Progress * totalSpan;
								float angle = t - p * Frequency;
								rotRad = (angle >= 0f && angle <= 2f * math.PI) ? math.sin(angle) * maxRad : 0f;
							} else {
								rotRad = math.sin(Time * Speed + p * Frequency) * maxRad;
							}
							break;
						}
						case RotateStyle.Spin: {
							// Continuous 360-degree rotation
							if (Once) {
								float maxChars = math.max(0, TotalChars - 1);
								float totalSpan = maxChars * Frequency + 1f;
								float t = Progress * totalSpan;
								float rawT = math.clamp(t - p * Frequency, 0f, 1f);
								rotRad = rawT * (2f * math.PI) * (Amplitude != 0f ? (Amplitude / 360f) : 1f);
							} else {
								rotRad = (Time * Speed + p * Frequency) * (2f * math.PI);
							}
							break;
						}
					}
					RotateAboutCenter(s, rotRad);
					break;
				}
				case BuiltinEffect.Rainbow: {
					// colour comes from the ramp (default = HSV rainbow); Frequency spreads it across glyphs.
					float spread = math.clamp(Frequency, 0f, 0.1f);
					float dir = Inverse ? -1f : 1f;
					float t = math.frac(phase * spread - Time * Speed * 0.1f * dir);
					if (t < 0f) t += 1f;
					MulColor(s, SampleRamp(t));
					break;
				}
				case BuiltinEffect.Glow: {
					float p = Inverse ? ((TotalChars > 0 ? (TotalChars - 1 - phase) : -phase)) : phase;
					switch (GlowStyle) {
						case GlowStyle.Fade: {
							float s01 = 0.5f + 0.5f * math.sin(Time * Speed + p * 0.2f);
							float k = math.max(0.05f, Frequency);
							float blend = math.saturate((s01 - 0.5f) * k + 0.5f);
							float4 c = math.lerp(ColorA, ColorB, blend);
							c.w = 1f;
							MulColor(s, c);
							break;
						}
						case GlowStyle.Shimmer: {
							// Smooth continuous light beam reflection across projected text geometry
							float rad = math.radians(Angle);
							float cosA = math.cos(rad);
							float sinA = math.sin(rad);
							float span = math.max(1f, ProjectedMax - ProjectedMin);
							float beamWidth = math.max(0.01f, Amplitude > 0f ? Amplitude : 0.25f);
							float beamCount = math.max(1f, Frequency);
							float dir = Inverse ? -1f : 1f;

							for (int i = 0; i < 4; i++) {
								TextVertex v = Vertices[s + i];
								float projected = v.position.x * cosA + v.position.y * sinA;
								float normPos = (projected - ProjectedMin) / span;

								if (Once) {
									float t = math.clamp(Progress, 0f, 1f);
									if (Inverse) t = 1f - t;
									int count = (int)beamCount;
									float spacing = math.max(beamWidth * 1.5f, 0.25f);
									float leadCenter = math.lerp(-beamWidth, 1f + beamWidth + (count - 1) * spacing, t);

									for (int b = 0; b < count; b++) {
										float center = leadCenter - b * spacing;
										float dist = (normPos - center) / beamWidth;
										if (math.abs(dist) <= 1f) {
											float rampT = dist * 0.5f + 0.5f;
											v.color *= SampleRamp(rampT);
											Vertices[s + i] = v;
											break;
										}
									}
								} else {
									// Multi-beam continuous wave across text geometry
									float cycle = math.frac((normPos * beamCount) * dir - Time * (Speed * 0.2f) * dir);
									float dist = (cycle - 0.5f) / (beamWidth * 0.5f);
									if (math.abs(dist) <= 1f) {
										float rampT = dist * 0.5f + 0.5f;
										v.color *= SampleRamp(rampT);
										Vertices[s + i] = v;
									}
								}
							}
							break;
						}
						case GlowStyle.NeonFlicker: {
							// Irregular neon tube flicker:
							// Speed controls temporal speed, Frequency controls flicker rate/jitter frequency, Amount controls dropout frequency
							float effectiveSpeed = math.max(0.1f, Speed);
							float effectiveFreq = math.max(0.1f, Frequency);
							float cell = math.floor(Time * effectiveSpeed * effectiveFreq * 10f);
							float noise = Hash(cell * 1.73f + p * 3.19f);
							float threshold = 1f - math.saturate(Amount > 0f ? Amount : 0.35f);
							float flicker;
							if (noise >= threshold) {
								float subHash = Hash(Time * effectiveSpeed * 30f + p * 7.13f);
								flicker = subHash < 0.35f ? 0.08f : (subHash < 0.7f ? 0.45f : 0.85f);
							} else {
								flicker = 1f;
							}
							float4 c = math.lerp(ColorA, ColorB, flicker);
							MulColor(s, c);
							break;
						}
					}
					break;
				}
				case BuiltinEffect.Glitch: {
					float p = Inverse ? ((TotalChars > 0 ? (TotalChars - 1 - phase) : -phase)) : phase;
					float effAmp = math.max(0f, Amplitude);
					float effSpeed = math.max(0f, Speed);

					switch (GlitchStyle) {
						case GlitchStyle.Glitch: {
							bool active;
							if (Once) {
								float progress01 = math.clamp(Progress, 0f, 1f);
								float normChar = TotalChars > 1 ? (phase / (float)(TotalChars - 1)) : 0f;
								if (progress01 <= 0.0001f) {
									active = false;
								} else if (Inverse) {
									active = normChar >= (1f - progress01 - 0.001f);
								} else {
									active = normChar <= (progress01 + 0.001f);
								}
							} else {
								active = true;
							}

							if (active) {
								float cell = math.floor(Time * math.max(0.01f, Frequency));
								float roll = Hash(cell * 1.37f + p * 2.11f);
								float threshold = 1f - math.saturate(Amount > 0f ? Amount : (Once ? 1f : 0f));
								if (roll >= threshold && (Amount > 0f || Once)) {
									float jx = (Hash(Time * 3.1f + p) - 0.5f) * 2f;
									float jy = (Hash(Time * 2.3f + p + 5f) - 0.5f) * 2f;
									Offset(s, new float2(jx * effAmp, jy * effAmp * 0.5f));
									float rampT = math.frac(roll * 3.37f + Time * effSpeed * 0.2f);
									MulColor(s, SampleRamp(rampT));
								}
							}
							break;
						}
						case GlitchStyle.Matrix: {
							// Digital matrix scramble decode:
							// Characters swap UVs to glyphs from the ScrambleAtlas pool and glow in matrix green
							bool isScrambling;

							if (Once) {
								float progress01 = math.clamp(Progress, 0f, 1f);
								float normChar = TotalChars > 1 ? (phase / (float)(TotalChars - 1)) : 0f;
								if (progress01 <= 0.0001f) {
									isScrambling = false;
								} else if (Inverse) {
									isScrambling = normChar >= (1f - progress01 - 0.001f);
								} else {
									isScrambling = normChar <= (progress01 + 0.001f);
								}
							} else {
								// Loop mode: Amount determines fraction of scrambling characters
								if (Amount <= 0f) {
									isScrambling = false;
								} else {
									float roll = Hash(math.floor(Time * (effSpeed * 2f)) * 1.73f + p * 3.19f);
									float threshold = 1f - math.saturate(Amount);
									isScrambling = roll >= threshold;
								}
							}

							if (isScrambling) {
								if (ScrambleAtlasLen > 0) {
									float frameSeed = math.floor(Time * math.max(1f, effSpeed * 10f) + p * 7.31f);
									int poolIdx = (int)(Hash(frameSeed) * 1000f) % ScrambleAtlasLen;
									float4 rect = ScrambleAtlas[poolIdx];

									TextVertex v0 = Vertices[s + 0]; v0.uv0.x = rect.x; v0.uv0.y = rect.w; v0.tangent = rect; v0.uv2 = rect; Vertices[s + 0] = v0;
									TextVertex v1 = Vertices[s + 1]; v1.uv0.x = rect.z; v1.uv0.y = rect.w; v1.tangent = rect; v1.uv2 = rect; Vertices[s + 1] = v1;
									TextVertex v2 = Vertices[s + 2]; v2.uv0.x = rect.z; v2.uv0.y = rect.y; v2.tangent = rect; v2.uv2 = rect; Vertices[s + 2] = v2;
									TextVertex v3 = Vertices[s + 3]; v3.uv0.x = rect.x; v3.uv0.y = rect.y; v3.tangent = rect; v3.uv2 = rect; Vertices[s + 3] = v3;
								} else {
									float frameSeed = math.floor(Time * math.max(1f, effSpeed * 10f) + p * 7.31f);
									int targetQ = (int)(Hash(frameSeed) * 1000f) % math.max(1, QuadStart.Length);
									int targetStart = QuadStart[targetQ];
									for (int i = 0; i < 4; i++) {
										TextVertex v = Vertices[s + i];
										TextVertex srcV = Vertices[targetStart + i];
										v.uv0 = srcV.uv0;
										v.tangent = srcV.tangent;
										v.uv2 = srcV.uv2;
										Vertices[s + i] = v;
									}
								}

								// Matrix color ramp + small digital jitter
								float jx = (Hash(Time * 20f + p) - 0.5f) * effAmp;
								float jy = (Hash(Time * 15f + p + 7f) - 0.5f) * effAmp * 0.5f;
								Offset(s, new float2(jx, jy));

								float rampT = math.frac(Time * effSpeed * 0.5f + p * 0.3f);
								MulColor(s, SampleRamp(rampT));
							}
							break;
						}
					}
					break;
				}
			}
		}

		private static float EvaluateEasing(float t, TextEasing easing) {
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

		private void Offset(int s, float2 d) {
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				v.position.x += d.x;
				v.position.y += d.y;
				Vertices[s + i] = v;
			}
		}

		private void ScaleAboutCenter(int s, float2 m) {
			float3 c = (Vertices[s].position + Vertices[s + 1].position + Vertices[s + 2].position + Vertices[s + 3].position) * 0.25f;
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				v.position.x = c.x + (v.position.x - c.x) * m.x;
				v.position.y = c.y + (v.position.y - c.y) * m.y;
				Vertices[s + i] = v;
			}
		}

		private void RotateAboutCenter(int s, float radians) {
			float3 c = (Vertices[s].position + Vertices[s + 1].position + Vertices[s + 2].position + Vertices[s + 3].position) * 0.25f;
			float sn = math.sin(radians);
			float cs = math.cos(radians);
			for (int i = 0; i < 4; i++) {
				TextVertex v = Vertices[s + i];
				float dx = v.position.x - c.x;
				float dy = v.position.y - c.y;
				v.position.x = c.x + dx * cs - dy * sn;
				v.position.y = c.y + dx * sn + dy * cs;
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
