using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Sperlich.Text {

	/// <summary>
	/// Applies the effect layers to the mesh vertex buffer each frame, in place, before upload:
	/// 1) the typewriter reveal mask, 2) the built-in Burst catalog (Ebene 2), 3) user <see cref="ITextEffect"/>
	/// scripts (Ebene 1). Order is deterministic; jobs are completed before Ebene 1 runs so scripts see the result.
	/// </summary>
	public sealed class TextEffectStack {

		private readonly List<BuiltinEffectParams> builtins = new();
		private readonly List<ITextEffect> scripts = new();

		public IReadOnlyList<BuiltinEffectParams> Builtins => builtins;
		public IReadOnlyList<ITextEffect> Scripts => scripts;

		public int RevealVisibleChars = int.MaxValue;
		public float RevealFadeChars = 0f;

		public void AddBuiltin(BuiltinEffectParams p) => builtins.Add(p);
		public void ClearBuiltins() => builtins.Clear();
		public void AddScript(ITextEffect e) { if (e != null && !scripts.Contains(e)) scripts.Add(e); }
		public void RemoveScript(ITextEffect e) => scripts.Remove(e);
		public void ClearScripts() => scripts.Clear();

		public bool HasWork {
			get {
				for (int i = 0; i < builtins.Count; i++) {
					if (builtins[i].Enabled && builtins[i].Effect != BuiltinEffect.None) return true;
				}
				return scripts.Count > 0 || RevealVisibleChars != int.MaxValue;
			}
		}

		public void Apply(TextMeshBuilder builder, GlyphStore store, float time, float deltaTime, int totalSourceChars) {
			if (builder == null || builder.GlyphQuadCount == 0) return;
			if (!HasWork && !builder.HasSpanEffects) return;

			NativeArray<TextVertex> verts = builder.Vertices.AsArray();
			NativeArray<int> quadStart = builder.GlyphQuadStart.AsArray();
			NativeArray<int> quadSource = builder.GlyphQuadSource.AsArray();
			NativeArray<int> quadEffect = builder.GlyphQuadEffect.AsArray();

			ApplyRevealMask(verts, quadStart, quadSource);

			// component-level effects (whole text): a single config applies to every quad, so a trivial
			// one-entry table + all-zero index array stands in for the per-span dedup table below.
			if (builtins.Count > 0) {
				NativeArray<int> uniformIndex = new NativeArray<int>(quadStart.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
				for (int i = 0; i < builtins.Count; i++) {
					if (!builtins[i].Enabled || builtins[i].Effect == BuiltinEffect.None) continue;
					NativeArray<BuiltinEffectParamsBurst> singleEntry = new NativeArray<BuiltinEffectParamsBurst>(1, Allocator.TempJob);
					singleEntry[0] = builtins[i].ToBurst();
					RunEffect(builtins[i], -1, store, verts, quadStart, quadSource, quadEffect, singleEntry, uniformIndex, time, totalSourceChars);
					singleEntry.Dispose();
				}
				uniformIndex.Dispose();
			}

			// per-span effects from <wave> / <shake> / <blink> / ... tags -- tunables come from the builder's
			// deduplicated per-span table, so two tags of the same type can carry different attribute values
			// without needing an extra scheduled job per occurrence (job count still only depends on how many
			// distinct BuiltinEffect *types* are present, exactly as before per-tag attributes existed).
			if (builder.HasSpanEffects) {
				NativeArray<int> quadParamIndex = builder.GlyphQuadParamIndex.AsArray();
				IReadOnlyList<BuiltinEffectParamsBurst> table = builder.EffectParamsTable;
				NativeArray<BuiltinEffectParamsBurst> paramsTable = new NativeArray<BuiltinEffectParamsBurst>(table.Count, Allocator.TempJob);
				for (int i = 0; i < table.Count; i++) paramsTable[i] = table[i];

				int mask = 0;
				for (int q = 0; q < quadEffect.Length; q++) mask |= 1 << quadEffect[q];
				for (int e = 1; e <= (int)BuiltinEffect.Blink; e++) {
					if ((mask & (1 << e)) == 0) continue;
					RunEffect(DefaultParams((BuiltinEffect)e), e, store, verts, quadStart, quadSource, quadEffect, paramsTable, quadParamIndex, time, totalSourceChars);
				}
				paramsTable.Dispose();
			}

			if (scripts.Count > 0) {
				TextEffectContext ctx = new TextEffectContext(builder.Vertices, quadStart, quadSource, time, deltaTime, totalSourceChars);
				for (int i = 0; i < scripts.Count; i++) {
					try { scripts[i].Apply(ctx); }
					catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
				}
			}
		}

		private const int RampSamples = 64;

		/// <summary>Schedules one Burst job for every quad whose <see cref="BuiltinEffectJob.QuadEffect"/> matches
		/// <paramref name="filter"/> (-1 = every quad). <paramref name="p"/> is only used for the resources that
		/// stay shared per effect *type* rather than per tag occurrence -- the colour ramp, the Matrix scramble
		/// pool, and the Shimmer beam-angle projection bounds (see the plan's scope note: per-tag Ramp/scramble
		/// overrides are a possible later extension, not part of this pass). The actual per-quad tunables come
		/// from <paramref name="paramsTable"/>/<paramref name="quadParamIndex"/>.</summary>
		private static void RunEffect(BuiltinEffectParams p, int filter, GlyphStore store,
			NativeArray<TextVertex> verts, NativeArray<int> quadStart, NativeArray<int> quadSource,
			NativeArray<int> quadEffect, NativeArray<BuiltinEffectParamsBurst> paramsTable, NativeArray<int> quadParamIndex,
			float time, int totalChars) {

			NativeArray<float4> ramp = new NativeArray<float4>(RampSamples, Allocator.TempJob);
			BuildRamp(p, ramp);

			NativeArray<float4> scrambleAtlas;
			int scrambleAtlasLen = 0;
			if (p.Effect == BuiltinEffect.Glitch && p.GlitchStyle == GlitchStyle.Matrix) {
				const string defaultPool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()<>{}[]?/\\|~+=";
				string pool = string.IsNullOrEmpty(p.ScrambleCharacters) ? defaultPool : p.ScrambleCharacters;
				scrambleAtlas = new NativeArray<float4>(math.max(1, pool.Length), Allocator.TempJob);
				if (store != null) {
					float atlasSize = math.max(1f, store.AtlasSize);
					for (int c = 0; c < pool.Length; c++) {
						uint cp = pool[c];
						GlyphData gd = store.GetOrRequest(cp);
						if (gd.IsWhitespace) continue;
						scrambleAtlas[scrambleAtlasLen++] = new float4(
							gd.AtlasRect.x / atlasSize,
							gd.AtlasRect.y / atlasSize,
							(gd.AtlasRect.x + gd.AtlasRect.z) / atlasSize,
							(gd.AtlasRect.y + gd.AtlasRect.w) / atlasSize
						);
					}
				}
			} else {
				scrambleAtlas = new NativeArray<float4>(1, Allocator.TempJob);
			}

			float rad = math.radians(p.Angle);
			float cosA = math.cos(rad);
			float sinA = math.sin(rad);
			float pMin = float.MaxValue;
			float pMax = float.MinValue;
			for (int v = 0; v < verts.Length; v++) {
				float proj = verts[v].position.x * cosA + verts[v].position.y * sinA;
				if (proj < pMin) pMin = proj;
				if (proj > pMax) pMax = proj;
			}
			if (pMin >= pMax) { pMin = 0f; pMax = 100f; }

			BuiltinEffectJob job = new BuiltinEffectJob {
				Vertices = verts,
				QuadStart = quadStart,
				QuadSource = quadSource,
				QuadEffect = quadEffect,
				EffectFilter = filter,
				Effect = p.Effect,
				ParamsTable = paramsTable,
				QuadParamIndex = quadParamIndex,
				Time = time,
				TotalChars = totalChars,
				ProjectedMin = pMin,
				ProjectedMax = pMax,
				ScrambleAtlas = scrambleAtlas,
				ScrambleAtlasLen = scrambleAtlasLen,
				Ramp = ramp,
				RampLen = RampSamples
			};
			job.Schedule(quadStart.Length, 32).Complete();
			ramp.Dispose();
			if (scrambleAtlas.IsCreated) scrambleAtlas.Dispose();
		}

		/// <summary>Bakes the effect's <see cref="BuiltinEffectParams.Ramp"/> gradient into an evenly spaced
		/// LUT on the main thread. Null / empty gradient -> a full HSV rainbow (the historical Rainbow look).</summary>
		private static void BuildRamp(in BuiltinEffectParams p, NativeArray<float4> lut) {
			UnityEngine.Gradient grad = p.Ramp;
			bool has = grad != null && grad.colorKeys != null && grad.colorKeys.Length > 0;
			for (int i = 0; i < lut.Length; i++) {
				float t = i / (float)(lut.Length - 1);
				UnityEngine.Color c = has ? grad.Evaluate(t) : UnityEngine.Color.HSVToRGB(math.frac(t), 0.85f, 1f);
				lut[i] = new float4(c.r, c.g, c.b, c.a);
			}
		}

		private static BuiltinEffectParams DefaultParams(BuiltinEffect e) => BuiltinEffectParams.DefaultFor(e);

		private void ApplyRevealMask(NativeArray<TextVertex> verts, NativeArray<int> quadStart, NativeArray<int> quadSource) {
			if (RevealVisibleChars == int.MaxValue) return;
			for (int q = 0; q < quadStart.Length; q++) {
				int src = quadSource[q];
				if (src < 0) continue;
				float a;
				if (src < RevealVisibleChars - RevealFadeChars) a = 1f;
				else if (src >= RevealVisibleChars) a = 0f;
				else a = math.saturate(1f - (src - (RevealVisibleChars - RevealFadeChars)) / math.max(0.001f, RevealFadeChars));

				int s = quadStart[q];
				for (int i = 0; i < 4; i++) {
					TextVertex v = verts[s + i];
					v.color.w *= a;
					verts[s + i] = v;
				}
			}
		}
	}
}
