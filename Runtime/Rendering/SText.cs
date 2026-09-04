using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Sperlich.Text {
	/// <summary>
	/// Quality levels for blur effects such as Drop Shadow and Glow.
	/// Low uses a fast single-tap SDF evaluation; Medium (24) and High (48) use detailed Gaussian blur.
	/// </summary>
	public enum BlurQuality { Low = 1, Medium = 24, High = 48 }

	/// <summary>
	/// TextMeshPro-style label for uGUI, built on the Sperlich text pipeline: no font-asset bake step,
	/// runtime SDF atlas, Burst effect catalog, curved baselines, rich-text markup, link hit-testing.
	/// The component itself is display-only (plan module 14); interaction and editing live in sibling
	/// components (<see cref="TextInteraction"/>, <see cref="SperlichTextInputField"/>).
	/// </summary>
	[AddComponentMenu("Sperlich UI/Text/SText")]
	[RequireComponent(typeof(CanvasRenderer))]
	public class SText : MaskableGraphic {

		[SerializeField, TextArea(2, 6)] private string m_text = "New Text";
		[SerializeField] private FontDefinition m_font;
		[SerializeField] private float m_fontSize = 32f;
		[SerializeField] private bool m_richText = true;
		[SerializeField] private TextFontStyle m_fontStyle = TextFontStyle.None;

		[SerializeField] private TextAlign m_align = TextAlign.Left;
		[SerializeField] private TextVerticalAlign m_verticalAlign = TextVerticalAlign.Middle;
		[SerializeField] private TextWrap m_wrap = TextWrap.WordThenChar;
		[SerializeField] private TextOverflow m_overflow = TextOverflow.Overflow;

		// Inner padding between the RectTransform and the text box, in local units (like TMP's Margins).
		[SerializeField] private float m_marginLeft = 0f;
		[SerializeField] private float m_marginRight = 0f;
		[SerializeField] private float m_marginTop = 0f;
		[SerializeField] private float m_marginBottom = 0f;

		[SerializeField] private bool m_autoSize;
		[SerializeField] private float m_autoSizeMin = 8f;
		[SerializeField] private float m_autoSizeMax = 72f;

		[SerializeField] private float m_lineSpacing = 0f;      // "Line Height": extra leading as a fraction of the natural line box (0 = single)
		[SerializeField] private float m_paragraphSpacing = 0f; // "Paragraph Spacing": extra gap after a hard line break, in font-size units
		[SerializeField] private float m_extraTrackingEm = 0f;  // "Character Spacing": em added between every glyph
		[SerializeField] private float m_wordSpacingEm = 0f;    // "Word Spacing": em added on top of every space

		[SerializeField] private List<BuiltinEffectParams> m_builtinEffects = new();

		[SerializeField] private bool m_typewriter;
		[SerializeField] private RevealController m_reveal = new();

		// Face / Outline / Drop Shadow / Glow — grouped by the custom inspector's collapsible sections.
		[SerializeField, Range(-0.5f, 0.5f)] private float m_faceDilate = 0f;
		[SerializeField, Range(0f, 2f)] private float m_sharpness = 1f;

		[SerializeField] private bool m_outline = false;
		[SerializeField] private Color m_outlineColor = Color.black;
		[Tooltip("Outline width in UI pixels.")]
		[SerializeField, Range(0f, 32f)] private float m_outlineWidth = 2f;
		[SerializeField] private TextOutlinePlacement m_outlineMode = TextOutlinePlacement.Outer; // enum index 2

		[SerializeField] private bool m_shadow = false;
		[SerializeField] private Color m_shadowColor = new Color(0f, 0f, 0f, 0.5f);
		[Tooltip("Offset in UI pixels.")]
		[SerializeField] private Vector2 m_shadowOffset = new Vector2(2f, -2f);
		[Tooltip("Blur softness radius in UI pixels.")]
		[SerializeField, Range(0f, 10f)] private float m_shadowSoftness = 4f;
		[Tooltip("Thickens or thins the shadow shape before blurring.")]
		[SerializeField, Range(0f, 1f)] private float m_shadowDilate = 0f;
		[SerializeField] private BlurQuality m_shadowQuality = BlurQuality.Medium;

		[SerializeField] private bool m_glow = false;
		[SerializeField] private Color m_glowColor = new Color(0.3f, 0.6f, 1f, 1f);
		[SerializeField, Range(0f, 1f)] private float m_glowPower = 0.5f;
		[SerializeField, Range(0f, 0.5f)] private float m_glowOuter = 0.25f;
		[SerializeField] private BlurQuality m_glowQuality = BlurQuality.Medium;

		// Component-wide bloom: the per-glyph ring-blur (same path as the <bloom> tag) for the whole label.
		[SerializeField] private bool m_bloom = false;
		[SerializeField] private Color m_bloomColor = new Color(1f, 0.55f, 0.2f, 1f);
		[SerializeField, Range(0f, 1f)] private float m_bloomRadius = 1f;
		[SerializeField, Range(0f, 4f)] private float m_bloomIntensity = 2f;
		[SerializeField, Range(0.1f, 5f)] private float m_bloomFallOff = 1.7f;
		[SerializeField, Range(1, 128)] private int m_bloomSamples = 24;

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

		/// <summary>Convenience text getter/setter matching TMP / UnityEngine.UI.Text API.</summary>
		public string text {
			get => Text;
			set => Text = value;
		}

		/// <summary>Sets the text content (TMP compatibility).</summary>
		public void SetText(string newText) => Text = newText;

		public FontDefinition Font {
			get => m_font;
			set { if (m_font != value) { m_font = value; RebindFont(); textDirty = layoutDirty = true; SetVerticesDirty(); SetMaterialDirty(); } }
		}

		public float FontSize {
			get => m_fontSize;
			set { value = Mathf.Max(1f, value); if (!Mathf.Approximately(m_fontSize, value)) { m_fontSize = value; layoutDirty = true; SetVerticesDirty(); } }
		}

		public TextAlign Align { get => m_align; set { m_align = value; layoutDirty = true; SetVerticesDirty(); } }
		public TextVerticalAlign VerticalAlign { get => m_verticalAlign; set { m_verticalAlign = value; layoutDirty = true; SetVerticesDirty(); } }
		public TextOverflow Overflow { get => m_overflow; set { m_overflow = value; layoutDirty = true; SetVerticesDirty(); } }
		public TextWrap Wrap { get => m_wrap; set { m_wrap = value; layoutDirty = true; SetVerticesDirty(); } }

		/// <summary>Convenience alpha getter/setter matching TMP API.</summary>
		public float alpha {
			get => color.a;
			set {
				var c = color;
				c.a = value;
				color = c;
			}
		}

		/// <summary>Whole-label style flags (bold / italic / underline / strikethrough / case), like TMP's "Font Style".</summary>
		public TextFontStyle FontStyle {
			get => m_fontStyle;
			set { if (m_fontStyle != value) { m_fontStyle = value; textDirty = layoutDirty = true; SetVerticesDirty(); } }
		}

		/// <summary>Whole-label bloom (per-glyph ring blur, like the <c>&lt;bloom&gt;</c> tag applied to everything).</summary>
		public bool Bloom {
			get => m_bloom;
			set { if (m_bloom != value) { m_bloom = value; SetVerticesDirty(); } }
		}

		/// <summary>Color of the component-level bloom halo.</summary>
		public Color BloomColor {
			get => m_bloomColor;
			set { if (m_bloomColor != value) { m_bloomColor = value; SetVerticesDirty(); } }
		}

		/// <summary>Radius of the component-level bloom halo.</summary>
		public float BloomRadius {
			get => m_bloomRadius;
			set {
				float clamped = Mathf.Clamp01(value);
				if (!Mathf.Approximately(m_bloomRadius, clamped)) { m_bloomRadius = clamped; SetVerticesDirty(); }
			}
		}

		/// <summary>Intensity of the component-level bloom halo.</summary>
		public float BloomIntensity {
			get => m_bloomIntensity;
			set {
				float clamped = Mathf.Max(0f, value);
				if (!Mathf.Approximately(m_bloomIntensity, clamped)) { m_bloomIntensity = clamped; SetVerticesDirty(); }
			}
		}

		/// <summary>Falloff exponent for the component-level bloom halo.</summary>
		public float BloomFallOff {
			get => m_bloomFallOff;
			set {
				float clamped = Mathf.Clamp(value, 0.1f, 5f);
				if (!Mathf.Approximately(m_bloomFallOff, clamped)) { m_bloomFallOff = clamped; SetMaterialDirty(); }
			}
		}

		/// <summary>Sample count for the bloom blur noise.</summary>
		public int BloomSamples {
			get => m_bloomSamples;
			set {
				int clamped = Mathf.Clamp(value, 1, 128);
				if (m_bloomSamples != clamped) { m_bloomSamples = clamped; SetMaterialDirty(); }
			}
		}

		/// <summary>Enables the component-level outline.</summary>
		public bool Outline {
			get => m_outline;
			set { if (m_outline != value) { m_outline = value; SetMaterialDirty(); SetVerticesDirty(); } }
		}

		/// <summary>Color of the component-level outline.</summary>
		public Color OutlineColor {
			get => m_outlineColor;
			set { if (m_outlineColor != value) { m_outlineColor = value; SetMaterialDirty(); } }
		}

		/// <summary>Outline width in UI pixels.</summary>
		public float OutlineWidth {
			get => m_outlineWidth;
			set {
				float clamped = Mathf.Max(0f, value);
				if (!Mathf.Approximately(m_outlineWidth, clamped)) { m_outlineWidth = clamped; SetMaterialDirty(); SetVerticesDirty(); }
			}
		}

		/// <summary>Placement mode of the component-level outline (Inner, Middle, Outer).</summary>
		public TextOutlinePlacement OutlineMode {
			get => m_outlineMode;
			set { if (m_outlineMode != value) { m_outlineMode = value; SetMaterialDirty(); } }
		}

		/// <summary>Enables the component-level drop shadow.</summary>
		public bool Shadow {
			get => m_shadow;
			set { if (m_shadow != value) { m_shadow = value; SetVerticesDirty(); } }
		}

		/// <summary>Enables the component-level glow.</summary>
		public bool Glow {
			get => m_glow;
			set { if (m_glow != value) { m_glow = value; SetMaterialDirty(); SetVerticesDirty(); } }
		}

		/// <summary>Color of the component-level glow.</summary>
		public Color GlowColor {
			get => m_glowColor;
			set { if (m_glowColor != value) { m_glowColor = value; SetMaterialDirty(); } }
		}

		/// <summary>Power/intensity of the component-level glow (0..1).</summary>
		public float GlowPower {
			get => m_glowPower;
			set {
				float clamped = Mathf.Clamp01(value);
				if (!Mathf.Approximately(m_glowPower, clamped)) { m_glowPower = clamped; SetMaterialDirty(); }
			}
		}

		/// <summary>Outer radius/spread of the component-level glow.</summary>
		public float GlowOuter {
			get => m_glowOuter;
			set {
				float clamped = Mathf.Clamp(value, 0f, 0.5f);
				if (!Mathf.Approximately(m_glowOuter, clamped)) { m_glowOuter = clamped; SetMaterialDirty(); SetVerticesDirty(); }
			}
		}

		/// <summary>Color and alpha of the component-level drop shadow.</summary>
		public Color ShadowColor {
			get => m_shadowColor;
			set { if (m_shadowColor != value) { m_shadowColor = value; SetVerticesDirty(); } }
		}

		/// <summary>Offset of the component-level drop shadow in UI pixels.</summary>
		public Vector2 ShadowOffset {
			get => m_shadowOffset;
			set { if (m_shadowOffset != value) { m_shadowOffset = value; SetVerticesDirty(); } }
		}

		/// <summary>Softness blur radius of the component-level drop shadow in UI pixels.</summary>
		public float ShadowSoftness {
			get => m_shadowSoftness;
			set {
				float clamped = Mathf.Clamp(value, 0f, 10f);
				if (!Mathf.Approximately(m_shadowSoftness, clamped)) { m_shadowSoftness = clamped; SetVerticesDirty(); }
			}
		}

		/// <summary>Dilation of the component-level drop shadow.</summary>
		public float ShadowDilate {
			get => m_shadowDilate;
			set {
				float clamped = Mathf.Clamp01(value);
				if (!Mathf.Approximately(m_shadowDilate, clamped)) { m_shadowDilate = clamped; SetVerticesDirty(); }
			}
		}

		/// <summary>Sample count for the shadow blur.</summary>
		public BlurQuality ShadowQuality {
			get => m_shadowQuality;
			set { if (m_shadowQuality != value) { m_shadowQuality = value; SetMaterialDirty(); } }
		}

		/// <summary>Sample count for the glow blur.</summary>
		public BlurQuality GlowQuality {
			get => m_glowQuality;
			set { if (m_glowQuality != value) { m_glowQuality = value; SetMaterialDirty(); } }
		}

		/// <summary>Inner padding (left, right, top, bottom) between the RectTransform and the text box, in local units.</summary>
		public Vector4 Margins {
			get => new Vector4(m_marginLeft, m_marginRight, m_marginTop, m_marginBottom);
			set {
				m_marginLeft = value.x; m_marginRight = value.y; m_marginTop = value.z; m_marginBottom = value.w;
				layoutDirty = true;
				SetVerticesDirty();
			}
		}

		public TextEffectStack Effects => effects;
		public RevealController Reveal => m_reveal;
		public bool TypewriterEnabled { get => m_typewriter; set { m_typewriter = value; RestartReveal(); } }
		public LayoutResult CurrentLayout => layout;
		public MarkupResult CurrentMarkup => markup;
		public Vector2 MeasuredSize => layout != null ? new Vector2(layout.Size.x, layout.Size.y) : Vector2.zero;

		/// <summary>Gibt eine schreibgeschützte Liste aller konfigurierten Built-in-Effekte zurück.</summary>
		public IReadOnlyList<BuiltinEffectParams> BuiltinEffects => m_builtinEffects;

		/// <summary>Fügt einen konfigurierten Built-in-Effekt hinzu.</summary>
		public void AddBuiltinEffect(BuiltinEffectParams effectParams) {
			m_builtinEffects.Add(effectParams);
			SyncBuiltinEffects();
			SetVerticesDirty();
		}

		/// <summary>Fügt einen Built-in-Effekt mit Standardwerten hinzu.</summary>
		public void AddBuiltinEffect(BuiltinEffect effect) {
			BuiltinEffectParams p = effect switch {
				BuiltinEffect.Wave => BuiltinEffectParams.Wave,
				BuiltinEffect.Shake => BuiltinEffectParams.Shake,
				BuiltinEffect.Pulse => BuiltinEffectParams.Pulse,
				BuiltinEffect.Rainbow => BuiltinEffectParams.Rainbow,
				BuiltinEffect.Glow => BuiltinEffectParams.Glow,
				BuiltinEffect.Glitch => BuiltinEffectParams.Glitch,
				_ => new BuiltinEffectParams { Enabled = true, Effect = effect }
			};
			AddBuiltinEffect(p);
		}

		/// <summary>Entfernt den Built-in-Effekt am angegebenen Index.</summary>
		public bool RemoveBuiltinEffect(int index) {
			if (index < 0 || index >= m_builtinEffects.Count) return false;
			m_builtinEffects.RemoveAt(index);
			SyncBuiltinEffects();
			SetVerticesDirty();
			return true;
		}

		/// <summary>Entfernt den ersten Built-in-Effekt des angegebenen Typs.</summary>
		public bool RemoveBuiltinEffect(BuiltinEffect effect) {
			for (int i = 0; i < m_builtinEffects.Count; i++) {
				if (m_builtinEffects[i].Effect == effect) {
					m_builtinEffects.RemoveAt(i);
					SyncBuiltinEffects();
					SetVerticesDirty();
					return true;
				}
			}
			return false;
		}

		/// <summary>Entfernt alle konfigurierten Built-in-Effekte.</summary>
		public void ClearBuiltinEffects() {
			m_builtinEffects.Clear();
			SyncBuiltinEffects();
			SetVerticesDirty();
		}

		/// <summary>Aktiviert oder deaktiviert den Built-in-Effekt am angegebenen Index.</summary>
		public bool SetBuiltinEffectEnabled(int index, bool enabled) {
			if (index < 0 || index >= m_builtinEffects.Count) return false;
			BuiltinEffectParams p = m_builtinEffects[index];
			p.Enabled = enabled;
			m_builtinEffects[index] = p;
			SyncBuiltinEffects();
			SetVerticesDirty();
			return true;
		}

		/// <summary>Aktiviert oder deaktiviert alle Built-in-Effekte des angegebenen Typs.</summary>
		public bool SetBuiltinEffectEnabled(BuiltinEffect effect, bool enabled) {
			bool changed = false;
			for (int i = 0; i < m_builtinEffects.Count; i++) {
				if (m_builtinEffects[i].Effect == effect) {
					BuiltinEffectParams p = m_builtinEffects[i];
					p.Enabled = enabled;
					m_builtinEffects[i] = p;
					changed = true;
				}
			}
			if (changed) {
				SyncBuiltinEffects();
				SetVerticesDirty();
			}
			return changed;
		}

		/// <summary>Aktualisiert die Parameter des Built-in-Effekts am angegebenen Index.</summary>
		public bool SetBuiltinEffect(int index, BuiltinEffectParams effectParams) {
			if (index < 0 || index >= m_builtinEffects.Count) return false;
			m_builtinEffects[index] = effectParams;
			SyncBuiltinEffects();
			SetVerticesDirty();
			return true;
		}

		/// <summary>Sucht nach dem ersten Built-in-Effekt des angegebenen Typs.</summary>
		public bool TryGetBuiltinEffect(BuiltinEffect effect, out BuiltinEffectParams effectParams) {
			for (int i = 0; i < m_builtinEffects.Count; i++) {
				if (m_builtinEffects[i].Effect == effect) {
					effectParams = m_builtinEffects[i];
					return true;
				}
			}
			effectParams = default;
			return false;
		}

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
			EnsureCanvasShaderChannels();
			RebindFont();
			textDirty = layoutDirty = true;
			SyncBuiltinEffects();
			ITextEffect[] customEffects = GetComponents<ITextEffect>();
			for (int i = 0; i < customEffects.Length; i++) {
				effects.AddScript(customEffects[i]);
			}
			RestartReveal();
			SetAllDirty();
		}

		protected override void OnCanvasHierarchyChanged() {
			base.OnCanvasHierarchyChanged();
			EnsureCanvasShaderChannels();
		}

		/// <summary>
		/// uGUI only feeds <c>TEXCOORD1</c> (and normals/tangents) to the shader when the owning
		/// <see cref="Canvas"/> asks for it. Our SDF shader packs the per-tag FX mode + widths into
		/// <c>uv1</c>; without this the fragment stage reads garbage there, which trips the outline/glow
		/// branch (solid-colour blocks) and the shadow-copy softness path (blurred glyphs).
		/// </summary>
		private void EnsureCanvasShaderChannels() {
			Canvas c = canvas;
			if (c == null) return;
			const AdditionalCanvasShaderChannels need =
				AdditionalCanvasShaderChannels.TexCoord1 |
				AdditionalCanvasShaderChannels.TexCoord2 |
				AdditionalCanvasShaderChannels.Normal |
				AdditionalCanvasShaderChannels.Tangent;
			if ((c.additionalShaderChannels & need) != need)
				c.additionalShaderChannels |= need;
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
				originOffset = ContentOrigin();
				meshBuilder.Build(layout, store, markup.Spans, new Vector2(originOffset.x, originOffset.y), color, editingRects, BuildBloom(), BuildComponentShadow(), CalculateExtraPadding());
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
				$"  atlas size/range: {(store != null ? $"{store.AtlasSize} / {store.DistanceRange}" : "-")}\n" +
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
					if (m_builtinEffects[i].Enabled && m_builtinEffects[i].Effect != BuiltinEffect.None) return true;
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
			EnsureCanvasShaderChannels();
			EnsureStore();
			if (store == null || meshBuilder == null) return;

			if (textDirty) { EnsureMarkup(); textDirty = false; layoutDirty = true; }
			if (layoutDirty || layout == null) { RunLayout(); layoutDirty = false; }
			if (layout == null || layout.Glyphs.Count == 0) return;

			originOffset = ContentOrigin();
			meshBuilder.Build(layout, store, markup.Spans, new Vector2(originOffset.x, originOffset.y), color, editingRects, BuildBloom(), BuildComponentShadow(), CalculateExtraPadding());

			SyncBuiltinEffects();
			if (effects.HasWork || meshBuilder.HasSpanEffects) {
				effects.Apply(meshBuilder,
					store,
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
			runtimeMaterial.SetFloat("_OutlineWidth", m_outline ? m_outlineWidth : 0f);
			runtimeMaterial.SetFloat("_OutlineMode", (float)(int)m_outlineMode);
			runtimeMaterial.SetColor("_UnderlayColor", m_shadow ? m_shadowColor : Color.clear);
			runtimeMaterial.SetVector("_UnderlayOffset",
				new Vector4(m_shadowOffset.x, m_shadowOffset.y, Mathf.Max(0.0001f, m_shadowSoftness), 0f));
			runtimeMaterial.SetFloat("_UnderlayDilate", m_shadowDilate);
			runtimeMaterial.SetFloat("_ShadowTaps", (float)m_shadowQuality);
			runtimeMaterial.SetColor("_GlowColor", m_glowColor);
			runtimeMaterial.SetFloat("_GlowPower", m_glow ? m_glowPower : 0f);
			runtimeMaterial.SetFloat("_GlowOuter", m_glowOuter);
			runtimeMaterial.SetFloat("_GlowTaps", (float)m_glowQuality);
			runtimeMaterial.SetFloat("_BloomFalloff", m_bloomFallOff);
			runtimeMaterial.SetFloat("_BloomTaps", (float)m_bloomSamples);

			// MTSDF sampling is a shader keyword: only when the bound font asks for it AND the store's
			// backend actually serves an MTSDF atlas (a fell-back FontAccess still reports SDF).
			bool mtsdf = boundFont != null && boundFont.fieldKind == GlyphFieldKind.MTSDF
				&& store != null && store.Fonts.FieldKind == GlyphFieldKind.MTSDF;
			if (mtsdf) runtimeMaterial.EnableKeyword("SPERLICH_MTSDF");
			else runtimeMaterial.DisableKeyword("SPERLICH_MTSDF");
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
				if (boundFont.fieldKind == GlyphFieldKind.MTSDF)
					Debug.LogWarning($"[SperlichText] Font definition '{boundFont.name}' is set to MTSDF but has no usable " +
						"baked atlas. Select the FontDefinition and press 'Bake MTSDF Atlas'.", this);
				else
					Debug.LogWarning($"[SperlichText] Font definition '{boundFont.name}' produced no usable face. " +
						"Check that 'primary' is an imported dynamic Font and that TMP Essential Resources are imported " +
						"(Window > TextMeshPro > Import TMP Essential Resources).", this);
			}
			lastStoreVersion = -1;
			PushMaterialProps();
			SetMaterialDirty();
		}

		private void ReleaseFont() {
			if (boundFont != null) GlyphStoreRegistry.Release(boundFont);
			boundFont = null;
			store = null;
		}

		private void EnsureMarkup() {
			markup = parser.Parse(m_text ?? string.Empty, m_richText, BuildBaseStyle());
		}

		/// <summary>Seeds the parser with the component-level "Font Style" flags so they cover the whole label.</summary>
		private StyleState BuildBaseStyle() {
			StyleState s = StyleState.Default;
			if ((m_fontStyle & TextFontStyle.Bold) != 0) s.Synthesis |= FontSynthesis.Bold;
			if ((m_fontStyle & TextFontStyle.Italic) != 0) s.Synthesis |= FontSynthesis.Italic;
			if ((m_fontStyle & TextFontStyle.Underline) != 0) s.Underline = true;
			if ((m_fontStyle & TextFontStyle.Strikethrough) != 0) s.Strikethrough = true;
			if ((m_fontStyle & TextFontStyle.SmallCaps) != 0) s.Case = TextCase.SmallCaps;
			else if ((m_fontStyle & TextFontStyle.Uppercase) != 0) s.Case = TextCase.Upper;
			else if ((m_fontStyle & TextFontStyle.Lowercase) != 0) s.Case = TextCase.Lower;
			return s;
		}

		private TextMeshBuilder.ComponentBloom BuildBloom() {
			if (m_bloom == false) return default;
			return new TextMeshBuilder.ComponentBloom {
				Enabled = true,
				Color = new float4(m_bloomColor.r, m_bloomColor.g, m_bloomColor.b, m_bloomColor.a),
				Radius = Mathf.Clamp01(m_bloomRadius),
				Intensity = Mathf.Max(0f, m_bloomIntensity)
			};
		}

		private TextMeshBuilder.ComponentShadow BuildComponentShadow() {
			return new TextMeshBuilder.ComponentShadow {
				Enabled = m_shadow && m_shadowColor.a > 0f,
				Color = new float4(m_shadowColor.r, m_shadowColor.g, m_shadowColor.b, m_shadowColor.a),
				Offset = new float2(m_shadowOffset.x, m_shadowOffset.y),
				Softness = m_shadowSoftness,
				Dilate = m_shadowDilate
			};
		}

		/// <summary>Layout-box size after subtracting the margins from the RectTransform rect (never negative).</summary>
		private Vector2 ContentRectSize(Vector2 full) => new Vector2(
			Mathf.Max(0f, full.x - m_marginLeft - m_marginRight),
			Mathf.Max(0f, full.y - m_marginTop - m_marginBottom));

		/// <summary>Top-left origin of the text box (RectTransform top-left shifted in by the left/top margin).</summary>
		private float2 ContentOrigin() {
			Rect r = rectTransform.rect;
			return new float2(r.xMin + m_marginLeft, r.yMax - m_marginTop);
		}

		/// <summary>Calculates required padding in atlas pixels for effects so quads do not clip.</summary>
		private float CalculateExtraPadding() {
			float pad = 0f;
			float samplePx = store != null && store.Fonts != null && store.Fonts.PrimaryMetrics.SamplingPointSize > 0
				? store.Fonts.PrimaryMetrics.SamplingPointSize
				: (boundFont != null ? boundFont.samplingPointSize : 90f);
			float fontSize = m_fontSize > 0 ? m_fontSize : 36f;
			float pxToAtlas = samplePx / fontSize;

			if (m_outline && m_outlineWidth > 0f) {
				pad = Mathf.Max(pad, m_outlineWidth * pxToAtlas * 1.5f);
			}
			if (m_shadow && m_shadowColor.a > 0f) {
				float shadowDist = Mathf.Max(Mathf.Abs(m_shadowOffset.x), Mathf.Abs(m_shadowOffset.y)) * pxToAtlas;
				shadowDist += m_shadowSoftness * pxToAtlas * 1.5f;
				pad = Mathf.Max(pad, shadowDist);
			}
			if (m_glow && m_glowPower > 0f) {
				pad = Mathf.Max(pad, m_glowOuter * samplePx);
			}
			return pad;
		}

		private void RunLayout() {
			EnsureStore();
			if (store == null) { layout = null; return; }
			if (markup.Spans == null) EnsureMarkup();

			Vector2 rect = ContentRectSize(rectTransform.rect.size);
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
				WordSpacingEm = m_wordSpacingEm,
				AutoUppercaseTracking = true,
				Curve = curve
			};
		}

		private void SyncBuiltinEffects() {
			effects.ClearBuiltins();
			for (int i = 0; i < m_builtinEffects.Count; i++) {
				if (m_builtinEffects[i].Enabled && m_builtinEffects[i].Effect != BuiltinEffect.None) {
					effects.AddBuiltin(m_builtinEffects[i]);
				}
			}
		}

		private bool SameAsCurrent(StringBuilder sb) {
			if (m_text == null || m_text.Length != sb.Length) return false;
			for (int i = 0; i < sb.Length; i++) if (m_text[i] != sb[i]) return false;
			return true;
		}
	}
}
