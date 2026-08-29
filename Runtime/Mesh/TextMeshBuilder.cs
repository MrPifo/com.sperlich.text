using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sperlich.Text {

	/// <summary>
	/// Builds the renderable mesh from a <see cref="LayoutResult"/>. Vertices live in a reused
	/// <see cref="NativeList{T}"/> of <see cref="TextVertex"/> so the Burst effect jobs can mutate them
	/// in place; the final hand-off to a <see cref="Mesh"/> uses <see cref="Mesh.AllocateWritableMeshData"/>.
	/// Emits: highlight (mark) quads, glyph quads, underline / strikethrough quads, optional selection quads.
	/// </summary>
	public sealed class TextMeshBuilder : System.IDisposable {

		/// <summary>Whole-label bloom settings (per-glyph ring blur, same shader path as the <c>&lt;bloom&gt;</c> tag).</summary>
		public struct ComponentBloom {
			public bool Enabled;
			public float4 Color;
			public float Radius;     // 0..1 fraction of the baked glow padding
			public float Intensity;  // 0..4
		}

		private NativeList<TextVertex> vertices;
		private NativeList<uint> indices;
		private NativeList<int> glyphQuadStart;   // first vertex of each glyph quad (effect target)
		private NativeList<int> glyphQuadSource;  // stripped-text index of each glyph quad
		private NativeList<int> glyphQuadEffect;  // per-quad BuiltinEffect from the style span (0 = none)
		private float2 origin;
		private float4 tint = new float4(1f, 1f, 1f, 1f);
		private bool allocated;
		private bool hasSpanEffects;

		// (spanIndex, lineIndex) -> (xMin, yMin, xMax, yMax) covering a whole gradient run on one line
		private readonly Dictionary<long, float4> gradientBounds = new();

		public NativeList<TextVertex> Vertices => vertices;
		public NativeList<int> GlyphQuadStart => glyphQuadStart;
		public NativeList<int> GlyphQuadSource => glyphQuadSource;
		public NativeList<int> GlyphQuadEffect => glyphQuadEffect;
		public bool HasSpanEffects => hasSpanEffects;
		public int GlyphQuadCount => allocated ? glyphQuadStart.Length : 0;
		public int VertexCount => allocated ? vertices.Length : 0;
		public int IndexCount => allocated ? indices.Length : 0;

		public TextMeshBuilder() {
			vertices = new NativeList<TextVertex>(256, Allocator.Persistent);
			indices = new NativeList<uint>(384, Allocator.Persistent);
			glyphQuadStart = new NativeList<int>(128, Allocator.Persistent);
			glyphQuadSource = new NativeList<int>(128, Allocator.Persistent);
			glyphQuadEffect = new NativeList<int>(128, Allocator.Persistent);
			allocated = true;
		}

		public void Dispose() {
			if (!allocated) return;
			if (vertices.IsCreated) vertices.Dispose();
			if (indices.IsCreated) indices.Dispose();
			if (glyphQuadStart.IsCreated) glyphQuadStart.Dispose();
			if (glyphQuadSource.IsCreated) glyphQuadSource.Dispose();
			if (glyphQuadEffect.IsCreated) glyphQuadEffect.Dispose();
			allocated = false;
		}

		/// <summary>Rebuilds the vertex / index buffers. Call after every layout change.</summary>
		public void Build(LayoutResult layout, GlyphStore store, IReadOnlyList<StyleSpan> spans,
			Vector2 originOffset, Color baseTint, IReadOnlyList<Rect> selectionRects = null,
			ComponentBloom bloom = default) {

			origin = originOffset;
			tint = new float4(baseTint.r, baseTint.g, baseTint.b, baseTint.a);
			vertices.Clear();
			indices.Clear();
			glyphQuadStart.Clear();
			glyphQuadSource.Clear();
			glyphQuadEffect.Clear();
			hasSpanEffects = false;
			if (layout == null || store == null || layout.Glyphs.Count == 0) return;

			float atlasSize = math.max(1, store.AtlasSize);
			FaceMetrics fm = store.Fonts.PrimaryMetrics;
			float samplePx = fm.IsValid ? fm.SamplingPointSize : store.Fonts.Definition.samplingPointSize;
			// The atlas backend defines its own field normalisation (TMP: atlasPadding + 1; baked MTSDF:
			// the bake pixel range). Getting it wrong widens/narrows the screen-space AA band.
			float distanceRange = math.max(1f, store.DistanceRange);

			// MTSDF fields are normalised over a much narrower distance range than TMP's SDF, so the same
			// weight bias moves the edge far less in pixels. Scale the synthetic bold/light offsets up on
			// the MTSDF backend so <b> reads about as heavy as it does on the SDF path.
			bool mtsdfField = store.Fonts.FieldKind == GlyphFieldKind.MTSDF;
			float boldBias = mtsdfField ? 0.32f : 0.20f;
			float lightBias = mtsdfField ? 0.22f : 0.14f;

			EmitMarks(layout, spans);
			if (selectionRects != null) EmitRects(selectionRects, new float4(0.25f, 0.5f, 1f, 0.4f));

			ComputeGradientBounds(layout, spans);

			// decoration layers behind the glyphs, far to near
			EmitSpanFx(layout, spans, atlasSize, samplePx, distanceRange, SpanFxKind.Shadow);
			if (bloom.Enabled) EmitComponentBloom(layout, atlasSize, samplePx, distanceRange, bloom);
			EmitSpanFx(layout, spans, atlasSize, samplePx, distanceRange, SpanFxKind.Glow);
			EmitSpanFx(layout, spans, atlasSize, samplePx, distanceRange, SpanFxKind.Outline);

			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (!g.Visible) continue;
				GlyphData gd = g.Glyph;
				if (gd.IsWhitespace) continue;
				if (gd.AtlasRect.z <= 0f || gd.AtlasRect.w <= 0f) {
					// No atlas rect and not resolved -> the font chain has no glyph and even the tofu
					// replacement char is absent. Draw a hollow "notdef" box so the gap stays visible.
					if (!gd.IsResolved) EmitNotdefBox(g);
					continue;
				}

				StyleState style = SpanStyle(spans, g.SpanIndex);
				float unit = g.UnitScale;
				float pad = gd.Padding;

				float left = g.Pen.x + (gd.Bearing.x - pad) * unit;
				float top = g.Pen.y + (gd.Bearing.y + pad) * unit;
				float w = gd.AtlasRect.z * unit;
				float h = gd.AtlasRect.w * unit;

				float u0 = gd.AtlasRect.x / atlasSize;
				float v0 = gd.AtlasRect.y / atlasSize;
				float u1 = (gd.AtlasRect.x + gd.AtlasRect.z) / atlasSize;
				float v1 = (gd.AtlasRect.y + gd.AtlasRect.w) / atlasSize;

				float weightBias = 0f;
				if ((style.Synthesis & FontSynthesis.Bold) != 0) weightBias += boldBias;
				if ((style.Synthesis & FontSynthesis.Light) != 0) weightBias -= lightBias;

				float shear = (style.Synthesis & FontSynthesis.Italic) != 0 ? 0.22f : 0f;
				float sdfScale = distanceRange / math.max(1f, samplePx) * math.max(0.0001f, g.FontSize);

				float4 cTL, cTR, cBL, cBR;
				if (style.HasGradient && style.GradientScope != GradientScope.PerChar &&
					gradientBounds.TryGetValue(GradientKey(g.SpanIndex, g.LineIndex), out float4 gb)) {
					// gb = (xMin, yMin, xMax, yMax) of the whole gradient run on this line
					float gw = math.max(1e-4f, gb.z - gb.x);
					float gh = math.max(1e-4f, gb.w - gb.y);
					if (style.GradientScope == GradientScope.Stepped) {
						float uc = math.saturate((left + w * 0.5f - gb.x) / gw);
						float vc = math.saturate((top - h * 0.5f - gb.y) / gh);
						cTL = cTR = cBL = cBR = GradientAt(style, uc, vc);
					} else {
						float uL = math.saturate((left - gb.x) / gw);
						float uR = math.saturate((left + w - gb.x) / gw);
						float vT = math.saturate((top - gb.y) / gh);
						float vB = math.saturate((top - h - gb.y) / gh);
						cTL = GradientAt(style, uL, vT);
						cTR = GradientAt(style, uR, vT);
						cBL = GradientAt(style, uL, vB);
						cBR = GradientAt(style, uR, vB);
					}
				} else if (style.HasGradient) {
					cTL = style.GradientTopLeft; cTR = style.GradientTopRight;
					cBL = style.GradientBottomLeft; cBR = style.GradientBottomRight;
				} else {
					cTL = cTR = cBL = cBR = g.Color;
				}
				cTL *= tint; cTR *= tint; cBL *= tint; cBR *= tint;

				if (style.HasGlow && style.GlowBloom) {
					// bloom pulls the face toward a hot mix of white + the glow colour so the letters
					// themselves read as "lit", not just haloed
					float4 hot = math.lerp(style.GlowColor, new float4(1f, 1f, 1f, 1f), 0.6f);
					cTL = BloomFace(cTL, hot); cTR = BloomFace(cTR, hot);
					cBL = BloomFace(cBL, hot); cBR = BloomFace(cBR, hot);
				} else if (bloom.Enabled) {
					float4 hot = math.lerp(bloom.Color, new float4(1f, 1f, 1f, 1f), 0.6f);
					cTL = BloomFace(cTL, hot); cTR = BloomFace(cTR, hot);
					cBL = BloomFace(cBL, hot); cBR = BloomFace(cBR, hot);
				}

				float rot = g.Rotation;
				float2 pivot = new float2(left + w * 0.5f, top - h * 0.5f);

				glyphQuadStart.Add(vertices.Length);
				glyphQuadSource.Add(g.SourceIndex);
				glyphQuadEffect.Add((int)style.SpanEffect);
				if (style.SpanEffect != BuiltinEffect.None) hasSpanEffects = true;

				AddQuad(
					Corner(new float2(left, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top - h), pivot, rot, shear, h, top),
					Corner(new float2(left, top - h), pivot, rot, shear, h, top),
					new float2(u0, v1), new float2(u1, v1), new float2(u1, v0), new float2(u0, v0),
					cTL, cTR, cBR, cBL, sdfScale, weightBias, float4.zero);
			}

			EmitLineDecorations(layout, spans, fm, samplePx);
		}

		/// <summary>Whole-label bloom: one ring-blur quad per glyph (fxMode 2, bloom flag set), same shader
		/// path as a <c>&lt;bloom&gt;</c> span but driven by the component's bloom settings.</summary>
		private void EmitComponentBloom(LayoutResult layout, float atlasSize, float samplePx, float distanceRange, in ComponentBloom b) {
			float rFrac = math.min(math.saturate(b.Radius) * 1.7f, 1f);
			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (!g.Visible) continue;
				GlyphData gd = g.Glyph;
				if (gd.IsWhitespace || gd.AtlasRect.z <= 0f || gd.AtlasRect.w <= 0f) continue;

				float unit = g.UnitScale;
				float pad = gd.Padding;
				float left = g.Pen.x + (gd.Bearing.x - pad) * unit;
				float top = g.Pen.y + (gd.Bearing.y + pad) * unit;
				float w = gd.AtlasRect.z * unit;
				float h = gd.AtlasRect.w * unit;

				float u0 = gd.AtlasRect.x / atlasSize;
				float v0 = gd.AtlasRect.y / atlasSize;
				float u1 = (gd.AtlasRect.x + gd.AtlasRect.z) / atlasSize;
				float v1 = (gd.AtlasRect.y + gd.AtlasRect.w) / atlasSize;

				float shear = 0f;
				float sdfScale = distanceRange / math.max(1f, samplePx) * math.max(0.0001f, g.FontSize);
				float rot = g.Rotation;
				float radiusUv = rFrac * math.max(1f, pad) / atlasSize;
				float4 mode = new float4(2f, radiusUv, math.max(0f, b.Intensity), 1f); // 1 = bloom
				float4 fxTangent = new float4(u0, v0, u1, v1);
				float2 pivot = new float2(left + w * 0.5f, top - h * 0.5f);

				// tie this halo quad to its glyph's source so the Burst effect jobs move / pulse / fade it
				// in lockstep with the letter (component effects run over every tracked quad).
				glyphQuadStart.Add(vertices.Length);
				glyphQuadSource.Add(g.SourceIndex);
				glyphQuadEffect.Add(0); // BuiltinEffect.None

				AddQuad(
					Corner(new float2(left, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top - h), pivot, rot, shear, h, top),
					Corner(new float2(left, top - h), pivot, rot, shear, h, top),
					new float2(u0, v1), new float2(u1, v1), new float2(u1, v0), new float2(u0, v0),
					b.Color, b.Color, b.Color, b.Color, sdfScale, 0f, mode, fxTangent);
			}
		}

		private enum SpanFxKind { Shadow, Glow, Outline }

		/// <summary>Emits one decoration quad per glyph for the shadow / glow / outline span tags.</summary>
		private void EmitSpanFx(LayoutResult layout, IReadOnlyList<StyleSpan> spans,
			float atlasSize, float samplePx, float distanceRange, SpanFxKind kind) {

			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (!g.Visible) continue;
				GlyphData gd = g.Glyph;
				if (gd.IsWhitespace || gd.AtlasRect.z <= 0f || gd.AtlasRect.w <= 0f) continue;

				StyleState style = SpanStyle(spans, g.SpanIndex);
				bool on = kind switch {
					SpanFxKind.Shadow => style.HasShadow,
					SpanFxKind.Glow => style.HasGlow,
					_ => style.HasOutline
				};
				if (!on) continue;

				float unit = g.UnitScale;
				float pad = gd.Padding;
				float left = g.Pen.x + (gd.Bearing.x - pad) * unit;
				float top = g.Pen.y + (gd.Bearing.y + pad) * unit;
				float w = gd.AtlasRect.z * unit;
				float h = gd.AtlasRect.w * unit;

				float u0 = gd.AtlasRect.x / atlasSize;
				float v0 = gd.AtlasRect.y / atlasSize;
				float u1 = (gd.AtlasRect.x + gd.AtlasRect.z) / atlasSize;
				float v1 = (gd.AtlasRect.y + gd.AtlasRect.w) / atlasSize;

				float shear = (style.Synthesis & FontSynthesis.Italic) != 0 ? 0.22f : 0f;
				float sdfScale = distanceRange / math.max(1f, samplePx) * math.max(0.0001f, g.FontSize);
				float rot = g.Rotation;

				float4 color;
				float4 mode; // x: 0 face / 1 outline / 2 glow ; y,z: params
				float4 fxTangent = default; // glow: this glyph's cell uv-rect, to clamp the blur taps

				if (kind == SpanFxKind.Shadow) {
					float dx = style.ShadowOffsetEm.x * g.FontSize;
					float dy = style.ShadowOffsetEm.y * g.FontSize;
					left += dx; top += dy;
					color = style.ShadowColor;
					mode = new float4(0f, math.max(0.001f, style.ShadowSoftness), 0f, 0f);
				} else if (kind == SpanFxKind.Glow) {
					// No quad grow — the baked cell already carries 'pad' transparent px (Msdf Glow
					// Padding) for the halo to blur into. The shader ring-blurs the face coverage within
					// this cell; radius is a 0..1 fraction of that padding, sent in UV units.
					color = style.GlowColor;
					float rFrac = math.saturate(style.GlowRadius) * (style.GlowBloom ? 1.7f : 1f);
					float radiusUv = math.min(rFrac, 1f) * math.max(1f, pad) / atlasSize;
					mode = new float4(2f, radiusUv, math.max(0f, style.GlowIntensity), style.GlowBloom ? 1f : 0f);
					fxTangent = new float4(u0, v0, u1, v1);
				} else {
					color = style.OutlineColor;
					mode = new float4(1f, math.max(0.01f, style.OutlineWidth), 0f, 0f);
				}

				float2 pivot = new float2(left + w * 0.5f, top - h * 0.5f);
				AddQuad(
					Corner(new float2(left, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top), pivot, rot, shear, h, top),
					Corner(new float2(left + w, top - h), pivot, rot, shear, h, top),
					Corner(new float2(left, top - h), pivot, rot, shear, h, top),
					new float2(u0, v1), new float2(u1, v1), new float2(u1, v0), new float2(u0, v0),
					color, color, color, color, sdfScale, 0f, mode, fxTangent);
			}
		}

		private static long GradientKey(int span, int line) => ((long)span << 20) ^ (uint)line;

		/// <summary>Pre-pass: the pixel extent of each gradient run, per line, so colours span the whole run.</summary>
		private void ComputeGradientBounds(LayoutResult layout, IReadOnlyList<StyleSpan> spans) {
			gradientBounds.Clear();
			for (int i = 0; i < layout.Glyphs.Count; i++) {
				PositionedGlyph g = layout.Glyphs[i];
				if (!g.Visible) continue;
				GlyphData gd = g.Glyph;
				if (gd.IsWhitespace || gd.AtlasRect.z <= 0f || gd.AtlasRect.w <= 0f) continue;

				StyleState style = SpanStyle(spans, g.SpanIndex);
				if (!style.HasGradient || style.GradientScope == GradientScope.PerChar) continue;

				float unit = g.UnitScale;
				float pad = gd.Padding;
				float left = g.Pen.x + (gd.Bearing.x - pad) * unit;
				float top = g.Pen.y + (gd.Bearing.y + pad) * unit;
				float w = gd.AtlasRect.z * unit;
				float h = gd.AtlasRect.w * unit;

				long key = GradientKey(g.SpanIndex, g.LineIndex);
				if (gradientBounds.TryGetValue(key, out float4 r)) {
					gradientBounds[key] = new float4(
						math.min(r.x, left), math.min(r.y, top - h),
						math.max(r.z, left + w), math.max(r.w, top));
				} else {
					gradientBounds[key] = new float4(left, top - h, left + w, top);
				}
			}
		}

		/// <summary>Blends a face colour partway toward the hot bloom colour, keeping its own alpha.</summary>
		private static float4 BloomFace(float4 c, float4 hot) {
			float4 r = math.lerp(c, hot, 0.45f);
			r.w = c.w;
			return r;
		}

		private static float4 GradientAt(in StyleState s, float u, float v) {
			// u: 0 = left, 1 = right ; v: 0 = bottom, 1 = top
			float4 bottom = math.lerp(s.GradientBottomLeft, s.GradientBottomRight, u);
			float4 topRow = math.lerp(s.GradientTopLeft, s.GradientTopRight, u);
			return math.lerp(bottom, topRow, v);
		}

		private static float3 Corner(float2 p, float2 pivot, float rot, float shear, float height, float top) {
			float2 v = p;
			if (shear != 0f) v.x += (v.y - (top - height)) * shear;
			if (rot != 0f) {
				float2 d = v - pivot;
				float s = math.sin(rot);
				float c = math.cos(rot);
				v = pivot + new float2(d.x * c - d.y * s, d.x * s + d.y * c);
			}
			return new float3(v.x, v.y, 0f);
		}

		private void EmitMarks(LayoutResult layout, IReadOnlyList<StyleSpan> spans) {
			int i = 0;
			while (i < layout.Glyphs.Count) {
				PositionedGlyph g = layout.Glyphs[i];
				StyleState s = SpanStyle(spans, g.SpanIndex);
				if (!s.HasMark || !g.Visible) { i++; continue; }

				int line = g.LineIndex;
				float minX = g.Pen.x;
				float maxX = g.Pen.x + g.Glyph.Advance * g.UnitScale;
				float topY = g.Pen.y + layout.Lines[line].Ascent * 0.9f;
				float botY = g.Pen.y - layout.Lines[line].Descent * 0.9f;
				float4 col = s.MarkColor;

				int j = i + 1;
				while (j < layout.Glyphs.Count) {
					PositionedGlyph n = layout.Glyphs[j];
					StyleState ns = SpanStyle(spans, n.SpanIndex);
					if (!ns.HasMark || n.LineIndex != line || !ns.MarkColor.Equals(col)) break;
					maxX = n.Pen.x + n.Glyph.Advance * n.UnitScale;
					j++;
				}

				AddSolidQuad(new float2(minX, topY), new float2(maxX, topY), new float2(maxX, botY), new float2(minX, botY), col);
				i = j;
			}
		}

		private void EmitLineDecorations(LayoutResult layout, IReadOnlyList<StyleSpan> spans, FaceMetrics fm, float samplePx) {
			EmitDecoration(layout, spans, fm, samplePx, underline: true);
			EmitDecoration(layout, spans, fm, samplePx, underline: false);
		}

		private void EmitDecoration(LayoutResult layout, IReadOnlyList<StyleSpan> spans, FaceMetrics fm, float samplePx, bool underline) {
			int i = 0;
			while (i < layout.Glyphs.Count) {
				PositionedGlyph g = layout.Glyphs[i];
				StyleState s = SpanStyle(spans, g.SpanIndex);
				bool on = underline ? s.Underline : s.Strikethrough;
				if (!on || !g.Visible) { i++; continue; }

				int line = g.LineIndex;
				float unit = g.FontSize / math.max(1f, samplePx);
				float offset = underline
					? (fm.IsValid ? fm.UnderlineOffset * unit : -g.FontSize * 0.12f)
					: (fm.IsValid ? fm.StrikethroughOffset * unit : g.FontSize * 0.28f);
				float thick = math.max(1f, (underline ? fm.UnderlineThickness : fm.StrikethroughThickness) * unit);

				float minX = g.Pen.x;
				float maxX = g.Pen.x + g.Glyph.Advance * g.UnitScale;
				float y = g.Pen.y + offset;
				float4 col = g.Color * tint;

				int j = i + 1;
				while (j < layout.Glyphs.Count) {
					PositionedGlyph n = layout.Glyphs[j];
					StyleState ns = SpanStyle(spans, n.SpanIndex);
					bool nOn = underline ? ns.Underline : ns.Strikethrough;
					if (!nOn || n.LineIndex != line) break;
					maxX = n.Pen.x + n.Glyph.Advance * n.UnitScale;
					j++;
				}

				AddSolidQuad(new float2(minX, y + thick * 0.5f), new float2(maxX, y + thick * 0.5f),
					new float2(maxX, y - thick * 0.5f), new float2(minX, y - thick * 0.5f), col);
				i = j;
			}
		}

		private void EmitRects(IReadOnlyList<Rect> rects, float4 color) {
			for (int i = 0; i < rects.Count; i++) {
				Rect r = rects[i];
				AddSolidQuad(new float2(r.xMin, r.yMax), new float2(r.xMax, r.yMax),
					new float2(r.xMax, r.yMin), new float2(r.xMin, r.yMin), color);
			}
		}

		private static StyleState SpanStyle(IReadOnlyList<StyleSpan> spans, int spanIndex) {
			if (spans == null || spans.Count == 0) return StyleState.Default;
			if (spanIndex >= 0 && spanIndex < spans.Count) return spans[spanIndex].Style;
			return spans[spans.Count - 1].Style;
		}

		private void AddQuad(float3 p0, float3 p1, float3 p2, float3 p3,
			float2 uv0, float2 uv1, float2 uv2, float2 uv3,
			float4 c0, float4 c1, float4 c2, float4 c3, float sdfScale, float weightBias, float4 mode,
			float4 tan = default) {

			uint b = (uint)vertices.Length;
			if (tan.x == 0f && tan.y == 0f && tan.z == 0f && tan.w == 0f) tan = new float4(1f, 0f, 0f, -1f);
			float3 n = new float3(0f, 0f, -1f);

			vertices.Add(Vtx(p0, n, tan, c0, uv0, sdfScale, weightBias, mode));
			vertices.Add(Vtx(p1, n, tan, c1, uv1, sdfScale, weightBias, mode));
			vertices.Add(Vtx(p2, n, tan, c2, uv2, sdfScale, weightBias, mode));
			vertices.Add(Vtx(p3, n, tan, c3, uv3, sdfScale, weightBias, mode));

			indices.Add(b); indices.Add(b + 1); indices.Add(b + 2);
			indices.Add(b); indices.Add(b + 2); indices.Add(b + 3);
		}

		/// <summary>
		/// Draws a hollow rectangle (four solid bars) where a glyph is missing from the whole font
		/// chain, so the gap is visible while debugging instead of an invisible blank. Uses the
		/// glyph's resolved colour so a missing char inside a coloured span shows a coloured box.
		/// </summary>
		private void EmitNotdefBox(PositionedGlyph g) {
			float fs = g.FontSize;
			float adv = g.Glyph.Advance * g.UnitScale;
			float w = adv > 1f ? adv * 0.78f : fs * 0.42f;
			float h = fs * 0.62f;
			float t = math.max(1f, fs * 0.055f);           // bar thickness
			float x0 = g.Pen.x + math.max(0f, (adv - w) * 0.5f);
			float x1 = x0 + w;
			float y0 = g.Pen.y;                             // baseline
			float y1 = y0 + h;

			float4 c = g.Color * tint;
			c.w *= 0.9f;

			Bar(x0, y0, x1, y0 + t, c);        // bottom
			Bar(x0, y1 - t, x1, y1, c);        // top
			Bar(x0, y0, x0 + t, y1, c);        // left
			Bar(x1 - t, y0, x1, y1, c);        // right
		}

		private void Bar(float x0, float y0, float x1, float y1, float4 c) {
			AddSolidQuad(new float2(x0, y1), new float2(x1, y1), new float2(x1, y0), new float2(x0, y0), c);
		}

		private void AddSolidQuad(float2 p0, float2 p1, float2 p2, float2 p3, float4 color) {
			uint b = (uint)vertices.Length;
			float4 tan = new float4(1f, 0f, 0f, -1f);
			float3 n = new float3(0f, 0f, -1f);
			// uv0.z flagged negative so the shader treats it as a solid fill, not an SDF sample
			float2 flag = new float2(0.5f, 0.5f);
			vertices.Add(Vtx(new float3(p0.x, p0.y, 0f), n, tan, color, flag, -1f, 0f, float4.zero));
			vertices.Add(Vtx(new float3(p1.x, p1.y, 0f), n, tan, color, flag, -1f, 0f, float4.zero));
			vertices.Add(Vtx(new float3(p2.x, p2.y, 0f), n, tan, color, flag, -1f, 0f, float4.zero));
			vertices.Add(Vtx(new float3(p3.x, p3.y, 0f), n, tan, color, flag, -1f, 0f, float4.zero));
			indices.Add(b); indices.Add(b + 1); indices.Add(b + 2);
			indices.Add(b); indices.Add(b + 2); indices.Add(b + 3);
		}

		private TextVertex Vtx(float3 pos, float3 n, float4 tan, float4 col, float2 uv, float sdfScale, float weightBias, float4 mode) {
			return new TextVertex {
				position = new float3(pos.x + origin.x, pos.y + origin.y, pos.z),
				normal = n,
				tangent = tan,
				color = col,
				uv0 = new float4(uv.x, uv.y, sdfScale, weightBias),
				uv1 = mode
			};
		}

		/// <summary>
		/// Copies the current buffers into a uGUI <see cref="VertexHelper"/>. This is the standard, robust
		/// hand-off for the CanvasRenderer target; the NativeArray buffers still carry the parallel
		/// vertex/effect work that happened before this point.
		/// </summary>
		public void FillVertexHelper(UnityEngine.UI.VertexHelper vh) {
			vh.Clear();
			if (vertices.Length == 0 || indices.Length == 0) return;

			for (int i = 0; i < vertices.Length; i++) {
				TextVertex t = vertices[i];
				UIVertex v = new UIVertex {
					position = new Vector3(t.position.x, t.position.y, t.position.z),
					normal = new Vector3(t.normal.x, t.normal.y, t.normal.z),
					tangent = new Vector4(t.tangent.x, t.tangent.y, t.tangent.z, t.tangent.w),
					color = new Color(t.color.x, t.color.y, t.color.z, t.color.w),
					uv0 = new Vector4(t.uv0.x, t.uv0.y, t.uv0.z, t.uv0.w),
					uv1 = new Vector4(t.uv1.x, t.uv1.y, t.uv1.z, t.uv1.w)
				};
				vh.AddVert(v);
			}

			for (int i = 0; i + 2 < indices.Length; i += 3) {
				vh.AddTriangle((int)indices[i], (int)indices[i + 1], (int)indices[i + 2]);
			}
		}

		/// <summary>Copies the current buffers into <paramref name="mesh"/> via the writable mesh-data API.</summary>
		public void Apply(Mesh mesh) {
			if (mesh == null) return;
			int vcount = vertices.Length;
			int icount = indices.Length;

			if (vcount == 0 || icount == 0) {
				mesh.Clear();
				return;
			}

			Mesh.MeshDataArray mda = Mesh.AllocateWritableMeshData(1);
			Mesh.MeshData md = mda[0];

			md.SetVertexBufferParams(vcount, TextVertex.Layout);
			NativeArray<TextVertex> vdst = md.GetVertexData<TextVertex>();
			NativeArray<TextVertex>.Copy(vertices.AsArray(), vdst, vcount);

			md.SetIndexBufferParams(icount, IndexFormat.UInt32);
			NativeArray<uint> idst = md.GetIndexData<uint>();
			NativeArray<uint>.Copy(indices.AsArray(), idst, icount);

			md.subMeshCount = 1;
			md.SetSubMesh(0, new SubMeshDescriptor(0, icount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

			Mesh.ApplyAndDisposeWritableMeshData(mda, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
			mesh.RecalculateBounds();
		}
	}
}
