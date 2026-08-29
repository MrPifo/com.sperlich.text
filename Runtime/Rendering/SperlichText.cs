using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Sperlich.Text {

	/// <summary>
	/// TextMeshPro-style label for uGUI, built on the Sperlich text pipeline: no font-asset bake step,
	/// runtime SDF atlas, Burst effect catalog, curved baselines, rich-text markup, link hit-testing.
	/// The component itself is display-only (plan module 14); interaction and editing live in sibling
	/// components (<see cref="TextInteraction"/>, <see cref="SperlichTextInputField"/>).
	/// </summary>
	[AddComponentMenu("Sperlich/Text/Sperlich Text")]
	public class SperlichText : MaskableGraphic {

		[SerializeField, TextArea(2, 6)] private string m_text = "New Text";
		[SerializeField] private FontDefinition m_font;
		[SerializeField] private float m_fontSize = 32f;
		[SerializeField] private bool m_richText = true;

		[SerializeField] private TextAlign m_align = TextAlign.Left;
		[SerializeField] private TextVerticalAlign m_verticalAlign = TextVerticalAlign.Top;
		[SerializeField] private TextWrap m_wrap = TextWrap.Word;
		[SerializeField] private TextOverflow m_overflow = TextOverflow.Overflow;

		[SerializeField] private bool m_autoSize;
		[SerializeField] private float m_autoSizeMin = 8f;
		[SerializeField] private float m_autoSizeMax = 72f;

		[SerializeField] private float m_lineSpacing = 1f;
		[SerializeField] private float m_paragraphSpacing = 1f;
		[SerializeField] private float m_extraTrackingEm = 0f;

		[SerializeField] private List<BuiltinEffectParams> m_builtinEffects = new();

		[SerializeField] private bool m_typewriter;
		[SerializeField] private RevealController m_reveal = new();

		[Header("Face / SDF")]
		[SerializeField, Range(-0.5f, 0.5f)] private float m_faceDilate = 0f;
		[SerializeField, Range(0f, 2f)] private float m_sharpness = 1f;

		[Header("Outline (whole label)")]
		[SerializeField] private Color m_outlineColor = Color.black;
		[SerializeField, Range(0f, 0.5f)] private float m_outlineWidth = 0f;

		[Header("Drop Shadow (whole label)")]
		[SerializeField] private Color m_shadowColor = new Color(0f, 0f, 0f, 0.5f);
		[SerializeField] private Vector2 m_shadowOffset = new Vector2(0.05f, -0.05f);
		[SerializeField, Range(0f, 0.5f)] private float m_shadowSoftness = 0.05f;
		[SerializeField, Range(-0.5f, 0.5f)] private float m_shadowDilate = 0f;

		[Header("Glow (whole label)")]
		[SerializeField] private Color m_glowColor = new Color(0.3f, 0.6f, 1f, 1f);
		[SerializeField, Range(0f, 1f)] private float m_glowPower = 0f;
		[SerializeField, Range(0f, 0.5f)] private float m_glowOuter = 0.25f;

		private GlyphStore store;
		private FontDefinition boundFont;
		private readonly MarkupParser parser = new();
		private readonly TextLayoutEngine layoutEngine = new();
		private TextMeshBuilder meshBuilder;
		private readonly TextEffectStack effects = new();
		private Material runtimeMaterial;

		private MarkupResult markup;
		private LayoutResult layout;
		private CurvedBaseline curve;

		private bool textDirty = true;
		private bool layoutDirty = true;
		private int lastStoreVersion = -1;
		private float2 originOffset;
		private IReadOnlyList<Rect> editingRects;

		/// <summary>Raised after every successful (re)layout, so sibling components can refresh.</summary>
		public event Action LayoutChanged;

		// -- public API -------------------------------------------------------------------------

		public string Text {
			get => m_text;
			set { if (m_text != value) { m_text = value ?? string.Empty; textDirty = layoutDirty = true; RestartReveal(); SetVerticesDirty(); } }
		}

		public FontDefinition Font {
			get => m_font;
			set { if (m_font != value) { m_font = value; RebindFont(); textDirty = layoutDirty = true; SetVerticesDirty(); SetMaterialDirty(); } }
		}

		public float FontSize {
			get => m_fontSize;
			set { value = Mathf.Max(1f, value); if (!Mathf.Approximately(m_fontSize, value)) { m_fontSize = value; layoutDirty = true; SetVerticesDirty(); } }
		}

		public TextAlign Align { get => m_align; set { m_align = value; layoutDirty = true; SetVerticesDirty(); } }
		public TextOverflow Overflow { get => m_overflow; set { m_overflow = value; layoutDirty = true; SetVerticesDirty(); } }
		public TextWrap Wrap { get => m_wrap; set { m_wrap = value; layoutDirty = true; SetVerticesDirty(); } }

		public TextEffectStack Effects => effects;
		public RevealController Reveal => m_reveal;
		public bool TypewriterEnabled { get => m_typewriter; set { m_typewriter = value; RestartReveal(); } }
		public LayoutResult CurrentLayout => layout;
		public MarkupResult CurrentMarkup => markup;
		public Vector2 MeasuredSize => layout != null ? new Vector2(layout.Size.x, layout.Size.y) : Vector2.zero;

		public override Texture mainTexture => store != null && store.AtlasTexture != null ? store.AtlasTexture : base.mainTexture;

		/// <summary>Alloc-free text update from a reused <see cref="StringBuilder"/>.</summary>
		public void SetText(StringBuilder builder) {
			if (builder == null) return;
			if (!SameAsCurrent(builder)) {
				m_text = builder.ToString();
				textDirty = layoutDirty = true;
				RestartReveal();
				SetVerticesDirty();
			}
		}

		public void SetText(string value) => Text = value;

		/// <summary>
		/// Caret / selection rectangles to draw, in text-local space (origin top-left, +y up).
		/// Used by <see cref="SperlichTextInputField"/>. Pass null to clear.
		/// </summary>
		public void SetEditingRects(IReadOnlyList<Rect> rects) {
			editingRects = rects;
			SetVerticesDirty();
		}

		/// <summary>Sets a polyline the baseline should follow. Pass null to return to a straight baseline.</summary>
		public void SetBaselinePath(IReadOnlyList<Vector2> waypoints) {
			if (waypoints == null || waypoints.Count < 2) { curve = null; }
			else { curve ??= new CurvedBaseline(); curve.SetWaypoints(waypoints); }
			layoutDirty = true;
			SetVerticesDirty();
		}

		/// <summary>Pure measurement: size the current text would take at <paramref name="size"/> without meshing.</summary>
		public Vector2 Measure(float size, Vector2 rect) {
			EnsureStore();
			if (store == null) return Vector2.zero;
			if (markup.Spans == null) EnsureMarkup();
			TextLayoutInput input = BuildLayoutInput(size, rect);
			LayoutResult r = layoutEngine.Layout(input);
			return new Vector2(r.Size.x, r.Size.y);
		}

		public Vector2 ScreenToTextLocal(Vector2 screenPoint, Camera cam) {
			RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, cam, out Vector2 local);
			return local; // hitboxes are stored in the same rect-local space (origin offset already baked in)
		}

		/// <summary>Fills <paramref name="output"/> with one hitbox per <c>&lt;link&gt;</c> span in the current layout.</summary>
		public void CollectLinkHitboxes(List<LinkHitbox> output) {
			output.Clear();
			if (layout == null || markup.Links == null) return;

			for (int li = 0; li < markup.Links.Count; li++) {
				LinkRegion link = markup.Links[li];
				Dictionary<int, Rect> perLine = new();

				for (int g = 0; g < layout.Glyphs.Count; g++) {
					PositionedGlyph pg = layout.Glyphs[g];
					if (pg.SourceIndex < link.Start || pg.SourceIndex >= link.End) continue;

					float x0 = pg.Pen.x + originOffset.x;
					float x1 = x0 + pg.Glyph.Advance * pg.UnitScale;
					float lineTop = pg.Pen.y + layout.Lines[pg.LineIndex].Ascent + originOffset.y;
					float lineBot = pg.Pen.y - layout.Lines[pg.LineIndex].Descent + originOffset.y;
					Rect r = Rect.MinMaxRect(x0, lineBot, x1, lineTop);

					perLine[pg.LineIndex] = perLine.TryGetValue(pg.LineIndex, out Rect ex)
						? Rect.MinMaxRect(Mathf.Min(ex.xMin, r.xMin), Mathf.Min(ex.yMin, r.yMin), Mathf.Max(ex.xMax, r.xMax), Mathf.Max(ex.yMax, r.yMax))
						: r;
				}

				if (perLine.Count == 0) continue;
				List<Rect> rects = new(perLine.Values);
				Rect bounds = rects[0];
				for (int i = 1; i < rects.Count; i++) {
					bounds = Rect.MinMaxRect(
						Mathf.Min(bounds.xMin, rects[i].xMin), Mathf.Min(bounds.yMin, rects[i].yMin),
						Mathf.Max(bounds.xMax, rects[i].xMax), Mathf.Max(bounds.yMax, rects[i].yMax));
				}

				output.Add(new LinkHitbox {
					Id = link.Id,
					Start = link.Start,
					Length = link.Length,
					Bounds = bounds,
					LineRects = rects
				});
			}
		}

		// -- lifecycle -------------------------------------------------------------------------

		protected override void Awake() {
			base.Awake();
			meshBuilder ??= new TextMeshBuilder();
		}

		protected override void OnEnable() {
			base.OnEnable();
			meshBuilder ??= new TextMeshBuilder();
			RebindFont();
			textDirty = layoutDirty = true;
			SyncBuiltinEffects();
			RestartReveal();
			SetAllDirty();
		}

		protected override void OnDisable() {
			base.OnDisable();
			ReleaseFont();
		}

		protected override void OnDestroy() {
			base.OnDestroy();
			meshBuilder?.Dispose();
			meshBuilder = null;
			if (runtimeMaterial != null) { DestroySafe(runtimeMaterial); runtimeMaterial = null; }
		}

		protected override void OnRectTransformDimensionsChange() {
			base.OnRectTransformDimensionsChange();
			layoutDirty = true;
		}

		private static void DestroySafe(UnityEngine.Object o) {
			if (o == null) return;
			if (Application.isPlaying) Destroy(o);
			else DestroyImmediate(o);
		}

#if UNITY_EDITOR
		protected override void OnValidate() {
			base.OnValidate();
			m_fontSize = Mathf.Max(1f, m_fontSize);
			m_autoSizeMax = Mathf.Max(m_autoSizeMin, m_autoSizeMax);
			textDirty = layoutDirty = true;
			SyncBuiltinEffects();
			if (m_font != boundFont) RebindFont();
			PushMaterialProps();
			SetAllDirty();
		}

		/// <summary>
		/// Editor-only: drop the current font binding and rebuild it from a clean state, then flag a
		/// full text + layout rebuild. Called after the "TMP Essential Resources" get imported so an
		/// existing label picks up the now-working SDF atlas without a scene reload.
		/// </summary>
		public void EditorRebindFont() {
			// Drop the stale reference without going through the ref-counted registry (the caller has
			// usually already purged it), then rebuild from scratch.
			boundFont = null;
			store = null;
			RebindFont();
			textDirty = layoutDirty = true;
			SetAllDirty();
		}
#endif

		private void LateUpdate() {
			if (store == null) return;

			int budget = SperlichTextSettings.GetOrDefault()?.glyphsPerFrame ?? 8;
			bool generated = store.ProcessQueue(budget);
			if (generated || store.Version != lastStoreVersion) {
				layoutDirty = true;
				SetVerticesDirty();
			}

			if (m_typewriter && Application.isPlaying) {
				m_reveal.Tick();
				effects.RevealVisibleChars = m_reveal.VisibleChars;
				effects.RevealFadeChars = m_reveal.FadeChars;
				SetVerticesDirty();
			}

			if (Application.isPlaying && (effects.HasWork || (meshBuilder != null && meshBuilder.HasSpanEffects))) {
				SetVerticesDirty();
			}
		}

		private void RestartReveal() {
			if (m_reveal == null) return;
			if (m_typewriter && isActiveAndEnabled && Application.isPlaying) {
				m_reveal.Begin(parser.Parse(m_text ?? string.Empty, m_richText).Text);
				effects.RevealVisibleChars = 0;
			} else {
				effects.RevealVisibleChars = int.MaxValue;
			}
		}

		/// <summary>Logs the internal pipeline state. Right-click the component header &gt; "Log Diagnostics".</summary>
		[ContextMenu("Log Diagnostics")]
		public void LogDiagnostics() {
			EnsureStore();
			EnsureMarkup();
			if (store != null) RunLayout();

			string perGlyph = "";
			if (store != null && layout != null && meshBuilder != null) {
				originOffset = new float2(rectTransform.rect.xMin, rectTransform.rect.yMax);
				meshBuilder.Build(layout, store, markup.Spans, new Vector2(originOffset.x, originOffset.y), color, editingRects);
				int shown = 0;
				for (int i = 0; i < layout.Glyphs.Count && shown < 4; i++) {
					PositionedGlyph g = layout.Glyphs[i];
					GlyphData gd = g.Glyph;
					perGlyph += $"\n  glyph[{i}] u+{gd.Unicode:X4} vis={g.Visible} ws={gd.IsWhitespace} " +
						$"atlasRect=({gd.AtlasRect.x},{gd.AtlasRect.y},{gd.AtlasRect.z},{gd.AtlasRect.w}) " +
						$"unitScale={g.UnitScale:0.###} pen=({g.Pen.x:0.#},{g.Pen.y:0.#})";
					shown++;
				}
			}

			string s = $"[SperlichText] '{name}'\n" +
				$"  font def        : {(m_font != null ? m_font.name : "<null>")}\n" +
				$"  store           : {(store != null ? "acquired" : "NULL")}\n" +
				$"  face ready      : {(store != null && store.Fonts.IsReady)}\n" +
				$"  atlas texture   : {(store != null && store.AtlasTexture != null ? $"{store.AtlasTexture.width}x{store.AtlasTexture.height}" : "<null>")}\n" +
				$"  atlas size/pad  : {(store != null ? $"{store.AtlasSize} / {store.Padding}" : "-")}\n" +
				$"  pending glyphs  : {(store != null ? store.PendingCount : 0)}\n" +
				$"  markup text len : {(markup.Text != null ? markup.Text.Length : 0)}\n" +
				$"  layout glyphs   : {(layout != null ? layout.Glyphs.Count : 0)}, unresolved={(layout != null && layout.HasUnresolvedGlyphs)}\n" +
				$"  layout size     : {(layout != null ? layout.Size.ToString() : "-")}\n" +
				$"  mesh verts/idx  : {(meshBuilder != null ? $"{meshBuilder.VertexCount}/{meshBuilder.IndexCount}" : "-")}\n" +
				$"  runtime shader  : {(SperlichTextSettings.GetOrDefault()?.ResolveShader() ?? Shader.Find("Sperlich/Text SDF"))?.name ?? "NOT FOUND"}\n" +
				$"  runtime material: {(runtimeMaterial != null ? runtimeMaterial.shader.name : "<null> (using fallback UI material)")}\n" +
				$"  canvasRenderer  : cull={canvasRenderer.cull} materialCount={canvasRenderer.materialCount}\n" +
				$"  after forced Build: verts/idx = {(meshBuilder != null ? $"{meshBuilder.VertexCount}/{meshBuilder.IndexCount}" : "-")}" +
				perGlyph;
			Debug.Log(s, this);
			SetAllDirty();
		}

		/// <summary>True when something on this label animates every frame (built-in / span effects, typewriter).</summary>
		public bool HasAnimatedEffects {
			get {
				for (int i = 0; i < m_builtinEffects.Count; i++)
					if (m_builtinEffects[i].Effect != BuiltinEffect.None) return true;
				if (m_typewriter) return true;
				return meshBuilder != null && meshBuilder.HasSpanEffects;
			}
		}

		/// <summary>Editor-only: pushed by the edit-mode ticker so effects animate without Play mode.</summary>
		public void EditorAnimateTick() => SetVerticesDirty();

		/// <summary>Reveals the whole string immediately (typewriter skip).</summary>
		public void SkipTypewriter() {
			m_reveal?.SkipToEnd();
			effects.RevealVisibleChars = int.MaxValue;
			SetVerticesDirty();
		}

		// -- rendering -------------------------------------------------------------------------

		/// <summary>Standard uGUI vertex path: run the pipeline, then copy into the VertexHelper.</summary>
		protected override void OnPopulateMesh(VertexHelper vh) {
			vh.Clear();
			EnsureStore();
			if (store == null || meshBuilder == null) return;

			if (textDirty) { EnsureMarkup(); textDirty = false; layoutDirty = true; }
			if (layoutDirty || layout == null) { RunLayout(); layoutDirty = false; }
			if (layout == null || layout.Glyphs.Count == 0) return;

			originOffset = new float2(rectTransform.rect.xMin, rectTransform.rect.yMax);
			meshBuilder.Build(layout, store, markup.Spans, new Vector2(originOffset.x, originOffset.y), color, editingRects);

			if (effects.HasWork || meshBuilder.HasSpanEffects) {
				effects.Apply(meshBuilder,
					Application.isPlaying ? SperlichTextClock.Time : Time.realtimeSinceStartup,
					SperlichTextClock.DeltaTime,
					markup.Text?.Length ?? 0);
			}

			meshBuilder.FillVertexHelper(vh);
			lastStoreVersion = store.Version;
		}

		/// <summary>The runtime SDF material (built from the shader) unless one was assigned in the inspector.</summary>
		public override Material material {
			get {
				if (m_Material != null && m_Material != defaultMaterial) return m_Material;
				EnsureRuntimeMaterial();
				return runtimeMaterial != null ? runtimeMaterial : base.material;
			}
			set => base.material = value;
		}

		// -- internals -------------------------------------------------------------------------

		private void EnsureRuntimeMaterial() {
			if (runtimeMaterial == null) {
				Shader shader = SperlichTextSettings.GetOrDefault()?.ResolveShader() ?? Shader.Find("Sperlich/Text SDF");
				if (shader == null) return;
				runtimeMaterial = new Material(shader) { name = "SperlichText (runtime)", hideFlags = HideFlags.DontSave };
			}
			PushMaterialProps();
		}

		private void PushMaterialProps() {
			if (runtimeMaterial == null) return;
			runtimeMaterial.SetFloat("_FaceDilate", m_faceDilate);
			runtimeMaterial.SetFloat("_Sharpness", m_sharpness);
			runtimeMaterial.SetColor("_OutlineColor", m_outlineColor);
			runtimeMaterial.SetFloat("_OutlineWidth", m_outlineWidth);
			runtimeMaterial.SetColor("_UnderlayColor", m_shadowColor);
			runtimeMaterial.SetVector("_UnderlayOffset",
				new Vector4(m_shadowOffset.x, m_shadowOffset.y, Mathf.Max(0.0001f, m_shadowSoftness), 0f));
			runtimeMaterial.SetFloat("_UnderlayDilate", m_shadowDilate);
			runtimeMaterial.SetColor("_GlowColor", m_glowColor);
			runtimeMaterial.SetFloat("_GlowPower", m_glowPower);
			runtimeMaterial.SetFloat("_GlowOuter", m_glowOuter);
		}

		private void EnsureStore() {
			if (store != null) return;
			RebindFont();
		}

		private void RebindFont() {
			FontDefinition target = m_font != null ? m_font : SperlichTextSettings.GetOrDefault()?.defaultFont;
			if (target == boundFont && store != null) return;

			ReleaseFont();
			boundFont = target;
			if (boundFont == null) return;

			store = GlyphStoreRegistry.Acquire(boundFont);
			if (store != null && (SperlichTextSettings.GetOrDefault()?.prewarmLatin1 ?? true)) {
				store.PrewarmAscii();
			}
			if (store != null && store.Fonts.IsReady == false) {
				Debug.LogWarning($"[SperlichText] Font definition '{boundFont.name}' produced no usable face. " +
					"Check that 'primary' is an imported dynamic Font and that TMP Essential Resources are imported " +
					"(Window > TextMeshPro > Import TMP Essential Resources).", this);
			}
			lastStoreVersion = -1;
			SetMaterialDirty();
		}

		private void ReleaseFont() {
			if (boundFont != null) GlyphStoreRegistry.Release(boundFont);
			boundFont = null;
			store = null;
		}

		private void EnsureMarkup() {
			markup = parser.Parse(m_text ?? string.Empty, m_richText);
		}

		private void RunLayout() {
			EnsureStore();
			if (store == null) { layout = null; return; }
			if (markup.Spans == null) EnsureMarkup();

			Vector2 rect = rectTransform.rect.size;
			float size = m_fontSize;

			if (m_autoSize || m_overflow == TextOverflow.ScaleToFit) {
				float upper = Mathf.Max(m_autoSizeMin, m_autoSize ? m_autoSizeMax : m_fontSize);
				bool constrainHeight = m_autoSize || m_overflow == TextOverflow.ScaleToFit;
				size = AutoSizeSolver.Solve(s => Measure(s, rect), m_autoSizeMin, upper,
					rect.x, constrainHeight ? rect.y : 0f);
			}

			layout = layoutEngine.Layout(BuildLayoutInput(size, rect));

			// Pump the glyph queue synchronously so the very first frame (and the edit-mode preview,
			// where LateUpdate never runs) shows real glyphs instead of empty placeholders.
			int guard = 0;
			while (layout.HasUnresolvedGlyphs && store.PendingCount > 0 && guard++ < 4) {
				store.ProcessQueue(512);
				layout = layoutEngine.Layout(BuildLayoutInput(size, rect));
			}

			LayoutChanged?.Invoke();
		}

		private TextLayoutInput BuildLayoutInput(float size, Vector2 rect) {
			return new TextLayoutInput {
				Text = markup.Text,
				Spans = markup.Spans,
				Glyphs = store,
				BaseFontSize = size,
				RectSize = rect,
				Align = m_align,
				VerticalAlign = m_verticalAlign,
				Wrap = m_wrap,
				Overflow = m_overflow,
				LineSpacingMul = m_lineSpacing,
				ParagraphSpacingMul = m_paragraphSpacing,
				ExtraTrackingEm = m_extraTrackingEm,
				AutoUppercaseTracking = true,
				Curve = curve
			};
		}

		private void SyncBuiltinEffects() {
			effects.ClearBuiltins();
			for (int i = 0; i < m_builtinEffects.Count; i++) {
				if (m_builtinEffects[i].Effect != BuiltinEffect.None) effects.AddBuiltin(m_builtinEffects[i]);
			}
		}

		private bool SameAsCurrent(StringBuilder sb) {
			if (m_text == null || m_text.Length != sb.Length) return false;
			for (int i = 0; i < sb.Length; i++) if (m_text[i] != sb[i]) return false;
			return true;
		}
	}
}
