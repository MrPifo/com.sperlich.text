using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Sperlich.Text {

	/// <summary>
	/// Glyph cache and generation queue on top of an <see cref="IFontFaceSource"/>. Layout asks for a
	/// code point; it gets back the real atlas entry or a tofu placeholder while the entry is generated
	/// over the next frames (dynamic source) or immediately (baked source).
	///
	/// Dynamic sources rasterise on the main thread, so "background generation" is an amortised
	/// per-frame budget. A static source (baked MTSDF atlas) resolves on the first ask and never
	/// queues or rebuilds.
	/// </summary>
	public sealed class GlyphStore {

		private readonly IFontFaceSource fonts;
		private readonly Dictionary<uint, GlyphData> resolved = new();
		private readonly Queue<uint> pendingQueue = new();
		private readonly HashSet<uint> pendingSet = new();
		private readonly List<uint> batchScratch = new();

		// Code points the font chain has no glyph for at all (as opposed to "atlas is full right now").
		// Kept apart so a genuinely absent glyph is served a tofu box once and never again drives an
		// atlas clear/refill — that loop is what blanked the whole label on exotic characters.
		private readonly HashSet<uint> permanentMissing = new();
		private readonly Dictionary<uint, int> addAttempts = new();
		private const int MaxAddAttempts = 2;

		private GlyphData tofu;
		private bool tofuReady;
		private bool rebuiltThisPass;
		private int version;

		public IFontFaceSource Fonts => fonts;
		public Texture AtlasTexture => fonts.AtlasTexture;
		public int AtlasSize => fonts.AtlasSize;
		public float DistanceRange => fonts.DistanceRange;
		public int PendingCount => pendingQueue.Count;

		/// <summary>Bumped whenever atlas contents change; renderers compare it to know they must re-mesh.</summary>
		public int Version => version;

		public GlyphStore(IFontFaceSource fonts) {
			this.fonts = fonts;
		}

		public GlyphData GetOrRequest(uint unicode) {
			if (IsWhitespace(unicode)) return GlyphData.Whitespace(unicode, WhitespaceAdvance(unicode));
			if (resolved.TryGetValue(unicode, out GlyphData data)) return data;
			if (permanentMissing.Contains(unicode)) return Placeholder(unicode);

			if (fonts.TryGetGlyph(unicode, out GlyphEntry ge)) {
				GlyphData built = Build(unicode, ge);
				resolved[unicode] = built;
				return built;
			}

			if (!fonts.SupportsDynamicGeneration) {
				// Baked source: the code point was not baked and cannot be added. Serve tofu once and
				// never ask again — no queue, no rebuild.
				permanentMissing.Add(unicode);
				return Placeholder(unicode);
			}

			if (pendingSet.Add(unicode)) pendingQueue.Enqueue(unicode);
			return Placeholder(unicode);
		}

		/// <summary>Generates up to <paramref name="budget"/> queued glyphs. Call once per frame.</summary>
		public bool ProcessQueue(int budget) {
			if (pendingQueue.Count == 0) return false;

			if (!fonts.SupportsDynamicGeneration) {
				// Static source: nothing to rasterise. Drain whatever prewarm queued — hits resolve
				// straight away, misses settle to a permanent tofu. Never touches the atlas.
				bool anyResolved = false;
				while (pendingQueue.Count > 0) {
					uint u = pendingQueue.Dequeue();
					pendingSet.Remove(u);
					if (resolved.ContainsKey(u) || permanentMissing.Contains(u)) continue;
					if (fonts.TryGetGlyph(u, out GlyphEntry ge)) { resolved[u] = Build(u, ge); anyResolved = true; }
					else { permanentMissing.Add(u); resolved[u] = Placeholder(u); }
				}
				if (anyResolved) version++;
				return anyResolved;
			}

			rebuiltThisPass = false;

			batchScratch.Clear();
			while (batchScratch.Count < budget && pendingQueue.Count > 0) {
				uint u = pendingQueue.Dequeue();
				pendingSet.Remove(u);
				if (!resolved.ContainsKey(u)) batchScratch.Add(u);
			}
			if (batchScratch.Count == 0) return false;

			uint[] arr = batchScratch.ToArray();
			fonts.TryAddGlyphs(arr);

			bool changed = false;
			bool atlasPressure = false;
			for (int i = 0; i < arr.Length; i++) {
				uint u = arr[i];
				if (fonts.TryGetGlyph(u, out GlyphEntry ge)) {
					resolved[u] = Build(u, ge);
					addAttempts.Remove(u);
					changed = true;
				} else {
					int attempts = addAttempts.TryGetValue(u, out int a) ? a + 1 : 1;
					addAttempts[u] = attempts;
					if (attempts >= MaxAddAttempts) {
						// Survived a dedicated add attempt (and an atlas rebuild, if one happened) and
						// still is not in any face: the font chain has no glyph for it. Cache the tofu
						// box so it never re-queues and never again looks like atlas pressure.
						permanentMissing.Add(u);
						resolved[u] = Placeholder(u);
					} else {
						atlasPressure = true;
					}
				}
			}

			// Only a code point that might still fit after a clear counts as pressure. Genuinely
			// absent glyphs must not drive an endless clear/refill loop (that blanked the label).
			if (atlasPressure && !rebuiltThisPass && resolved.Count > 8) {
				RebuildAtlas();
				for (int i = 0; i < arr.Length; i++) {
					uint u = arr[i];
					if (!resolved.ContainsKey(u) && !permanentMissing.Contains(u) && pendingSet.Add(u)) {
						pendingQueue.Enqueue(u);
					}
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

		private static GlyphData Build(uint unicode, in GlyphEntry e) {
			return new GlyphData {
				FaceIndex = e.FaceIndex,
				GlyphIndex = e.GlyphIndex,
				Unicode = unicode,
				Advance = e.Advance,
				Size = new float2(e.Width, e.Height),
				Bearing = new float2(e.BearingX, e.BearingY),
				AtlasRect = new float4(e.RectX, e.RectY, e.RectW, e.RectH),
				Padding = e.Padding,
				IsResolved = true,
				IsWhitespace = e.Width <= 0f || e.Height <= 0f
			};
		}

		private GlyphData GetTofu() {
			if (tofuReady) return tofu;
			tofuReady = true;

			uint replacement = TypographyDefaults.Tofu;
			if (fonts.TryGetGlyph(replacement, out GlyphEntry ge)) {
				tofu = Build(replacement, ge);
				return tofu;
			}
			fonts.TryAddGlyphs(new[] { replacement });
			if (fonts.TryGetGlyph(replacement, out ge)) {
				tofu = Build(replacement, ge);
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
				Padding = 0f,
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
			if (fonts.TryGetGlyph(' ', out GlyphEntry ge) && ge.Advance > 0f) return ge.Advance;
			return em * 0.28f;
		}
	}
}
