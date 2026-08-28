using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;

namespace Sperlich.Text {

	/// <summary>
	/// Glyph cache and generation queue on top of <see cref="FontAccess"/> (TMP dynamic SDF). Layout asks
	/// for a code point; it gets back the real atlas entry or a tofu placeholder while the entry is
	/// generated over the next frames.
	///
	/// v1 note: TMP rasterises on the main thread, so "background generation" is an amortised per-frame
	/// budget rather than the Task/Thread pipeline the plan reserves for the msdfgen plugin. The queue /
	/// placeholder / swap-in shape is already what that path plugs into.
	/// </summary>
	public sealed class GlyphStore {

		private readonly FontAccess fonts;
		private readonly Dictionary<uint, GlyphData> resolved = new();
		private readonly Queue<uint> pendingQueue = new();
		private readonly HashSet<uint> pendingSet = new();
		private readonly List<uint> batchScratch = new();

		private GlyphData tofu;
		private bool tofuReady;
		private bool rebuiltThisPass;
		private int version;

		public FontAccess Fonts => fonts;
		public Texture AtlasTexture => fonts.AtlasTexture;
		public int AtlasSize => fonts.AtlasSize;
		public float Padding => fonts.Padding;
		public int PendingCount => pendingQueue.Count;

		/// <summary>Bumped whenever atlas contents change; renderers compare it to know they must re-mesh.</summary>
		public int Version => version;

		public GlyphStore(FontAccess fonts) {
			this.fonts = fonts;
		}

		public GlyphData GetOrRequest(uint unicode) {
			if (IsWhitespace(unicode)) return GlyphData.Whitespace(unicode, WhitespaceAdvance(unicode));
			if (resolved.TryGetValue(unicode, out GlyphData data)) return data;

			if (fonts.TryGetCharacter(unicode, out int faceIndex, out TMP_Character ch)) {
				GlyphData built = Build(unicode, faceIndex, ch);
				resolved[unicode] = built;
				return built;
			}

			if (pendingSet.Add(unicode)) pendingQueue.Enqueue(unicode);
			return Placeholder(unicode);
		}

		/// <summary>Generates up to <paramref name="budget"/> queued glyphs. Call once per frame.</summary>
		public bool ProcessQueue(int budget) {
			if (pendingQueue.Count == 0) return false;
			rebuiltThisPass = false;

			batchScratch.Clear();
			while (batchScratch.Count < budget && pendingQueue.Count > 0) {
				uint u = pendingQueue.Dequeue();
				pendingSet.Remove(u);
				if (!resolved.ContainsKey(u)) batchScratch.Add(u);
			}
			if (batchScratch.Count == 0) return false;

			uint[] arr = batchScratch.ToArray();
			fonts.TryAddCharacters(arr);

			bool changed = false;
			int stillMissing = 0;
			for (int i = 0; i < arr.Length; i++) {
				uint u = arr[i];
				if (fonts.TryGetCharacter(u, out int fi, out TMP_Character ch)) {
					resolved[u] = Build(u, fi, ch);
					changed = true;
				} else {
					stillMissing++;
				}
			}

			if (stillMissing > 0 && !rebuiltThisPass && resolved.Count > 8) {
				RebuildAtlas();
				for (int i = 0; i < arr.Length; i++) {
					if (!resolved.ContainsKey(arr[i]) && pendingSet.Add(arr[i])) pendingQueue.Enqueue(arr[i]);
				}
			}

			if (changed) version++;
			return changed;
		}

		public void Prewarm(IEnumerable<uint> codePoints) {
			foreach (uint u in codePoints) {
				if (IsWhitespace(u) || resolved.ContainsKey(u)) continue;
				if (pendingSet.Add(u)) pendingQueue.Enqueue(u);
			}
		}

		public void PrewarmAscii() {
			for (uint u = 0x20; u < 0x7F; u++) if (pendingSet.Add(u)) pendingQueue.Enqueue(u);
			for (uint u = 0xA1; u <= 0xFF; u++) if (pendingSet.Add(u)) pendingQueue.Enqueue(u);
		}

		/// <summary>Clears the dynamic atlas and every cache so glyphs are regenerated.</summary>
		public void RebuildAtlas() {
			fonts.ClearDynamicData();
			resolved.Clear();
			tofuReady = false;
			rebuiltThisPass = true;
			version++;
		}

		private GlyphData Build(uint unicode, int faceIndex, TMP_Character ch) {
			Glyph g = ch.glyph;
			GlyphMetrics m = g.metrics;
			GlyphRect r = g.glyphRect;
			float pad = fonts.Padding;

			return new GlyphData {
				FaceIndex = faceIndex,
				GlyphIndex = g.index,
				Unicode = unicode,
				Advance = m.horizontalAdvance,
				Size = new float2(m.width, m.height),
				Bearing = new float2(m.horizontalBearingX, m.horizontalBearingY),
				AtlasRect = new float4(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f),
				Padding = pad,
				IsResolved = true,
				IsWhitespace = m.width <= 0f || m.height <= 0f
			};
		}

		private GlyphData GetTofu() {
			if (tofuReady) return tofu;
			tofuReady = true;

			uint replacement = TypographyDefaults.Tofu;
			if (fonts.TryGetCharacter(replacement, out int fi, out TMP_Character ch)) {
				tofu = Build(replacement, fi, ch);
				return tofu;
			}
			fonts.TryAddCharacters(new[] { replacement });
			if (fonts.TryGetCharacter(replacement, out fi, out ch)) {
				tofu = Build(replacement, fi, ch);
				version++;
				return tofu;
			}

			FaceMetrics fm = fonts.PrimaryMetrics;
			float em = fm.IsValid ? fm.SamplingPointSize : 1f;
			tofu = new GlyphData {
				Unicode = replacement,
				Advance = em * 0.55f,
				Size = new float2(em * 0.5f, em * 0.7f),
				Bearing = new float2(em * 0.05f, em * 0.7f),
				AtlasRect = float4.zero,
				Padding = fonts.Padding,
				IsResolved = true
			};
			return tofu;
		}

		private GlyphData Placeholder(uint unicode) {
			GlyphData box = GetTofu();
			box.Unicode = unicode;
			box.IsResolved = false;
			return box;
		}

		private static bool IsWhitespace(uint u) =>
			u == ' ' || u == '\t' || u == '\n' || u == '\r' || u == 0x00A0 || u == 0x200B;

		private float WhitespaceAdvance(uint u) {
			FaceMetrics fm = fonts.PrimaryMetrics;
			float em = fm.IsValid ? fm.SamplingPointSize : 1f;
			return u switch {
				(uint)'\t' => fm.IsValid && fm.TabWidth > 0 ? fm.TabWidth : em * 2f,
				(uint)'\n' => 0f,
				(uint)'\r' => 0f,
				0x200B => 0f,
				_ => TrySpaceAdvance(em)
			};
		}

		private float TrySpaceAdvance(float em) {
			if (fonts.TryGetCharacter(' ', out _, out TMP_Character ch) && ch.glyph.metrics.horizontalAdvance > 0f) {
				return ch.glyph.metrics.horizontalAdvance;
			}
			return em * 0.28f;
		}
	}
}
