# Sperlich.Text — working notes for Claude

Isolated context for this package only. Not a project-wide status file — see the repo-root
`PROGRESS.md` for that. Read this first when a session touches `Assets/Sperlich/Text/`.

## What this is

A TextMeshPro-style label for **uGUI** (`SperlichText : MaskableGraphic`). Own SDF atlas at
runtime, no font-asset bake step. Latin / German focus. Reusable `Sperlich.*` package — **must not
take hard dependencies on BattleTanks gameplay code** (so `SperlichText` does NOT derive from
`MonoBase`; it derives straight from `MaskableGraphic`).

Namespaces: `Sperlich.Text` (runtime), `Sperlich.Text.EditorTools` (editor).

## Hard architecture decisions (do not undo without asking)

1. **SDF atlas comes from `TMP_FontAsset.CreateFontAsset(...)` in `AtlasPopulationMode.Dynamic`.**
   Unity 6's public `FontEngine` API cannot rasterise a glyph to a texture — every render method
   (`RenderGlyphToTexture`, `TryAddGlyphToTexture`, …) is `internal`. TMP dynamic mode is the only
   public path. All TMP contact is isolated to `Fonts/FontAccess.cs` + `Pipeline/GlyphStore.cs`.
   msdfgen / MTSDF would replace only `FontAccess`. This adds an asmdef ref on `Unity.TextMeshPro`
   (already in the project via `com.unity.ugui`).
2. **Mesh hand-off is the standard uGUI path: `OnPopulateMesh(VertexHelper)` +
   `TextMeshBuilder.FillVertexHelper(vh)`.** A custom vertex format + `canvasRenderer.SetMesh` was
   tried and does NOT work in edit mode / CanvasRenderer rejects the format. Do not reintroduce a
   custom `UpdateGeometry`/`UpdateMaterial`. `TextMeshBuilder.Apply(Mesh)` (MeshData path) is kept
   but unused — reserved for a future world-space renderer.
3. **Single time source** via `Common/SperlichTextClock.cs`. The package has **no** pause-system
   dependency. The clock follows `Time.deltaTime` by default; the host makes it pause-aware by
   setting `SperlichTextClock.IsPausedProvider` (or replacing `DeltaTimeProvider` / `TimeProvider`).

## File map (runtime, under `Runtime/`)

- `Common/` — `TextEnums.cs`, `SperlichTextClock.cs`, `TextVertex.cs` (88-byte blittable struct,
  `uv0`: xy=atlasUV, z=sdfScale (negative = solid-fill flag), w=weightBias; `uv1`: x=fxMode
  0 face / 1 outline / 2 glow, y=width/softness, z=glow intensity), `TypographyDefaults.cs`
- `Fonts/` — `FontAccess.cs` (TMP wrapper, primary + fallback list), `FaceMetrics.cs`,
  `GlyphData.cs`, `FontDefinition.cs` (SO)
- `Pipeline/GlyphStore.cs` — glyph queue, tofu placeholder, amortised `ProcessQueue(budget)`,
  atlas-full rebuild via `ClearFontAssetData(false)`
- `Atlas/ShelfPacker.cs` — unit-tested, only used on the future MTSDF path (TMP owns packing now)
- `Markup/` — `MarkupParser.cs` (stack-based), `StyleSpan.cs` (`StyleState` carries every span
  property incl. per-tag outline/shadow/glow + `GradientScope`)
- `Layout/` — `LineBreaker.cs` (UAX #14 subset), `TextLayoutEngine.cs`, `LayoutResult.cs`,
  `CurvedBaseline.cs`, `AutoSizeSolver.cs`
- `Mesh/TextMeshBuilder.cs` — quads for glyph / underline / mark / selection + `EmitSpanFx` extra
  geometry for per-tag shadow/glow/outline; `ComputeGradientBounds` run-wide gradient pre-pass
- `Effects/` — `ITextEffect` (plain C#), `BuiltinEffectJob` (`[BurstCompile] IJobParallelFor`,
  `EffectFilter` selects per-span vs component-level), `TextEffectStack`, `RevealController`
- `Rendering/` — `SperlichText.cs`, `GlyphStoreRegistry.cs` (ref-counted store per FontDefinition),
  `SperlichTextSettings.cs` (SO, from `Resources/`)
- `Shaders/SperlichTextSDF.shader` — ShaderLab, uGUI/CanvasRenderer compatible, `fxMode` branch in
  frag for outline/glow/shadow copies + component-level outline/glow/underlay blocks
- `Interaction/` `Editing/` `Glyphs/` `Adapters/Rewired/` — link hit-test, basic input field,
  `ITextGlyphSource` abstraction, opt-in Rewired adapter (`#if SPERLICH_TEXT_REWIRED`, off)

Editor: `Editor/SperlichTextEditor.cs` (grouped inspector, tag toolbar, readability lint),
`Editor/SperlichTextEditorTicker.cs` (`[InitializeOnLoad]` + `EditorApplication.update`, ~60 fps,
drives animated effects in edit mode via `SperlichText.EditorAnimateTick()`).

Tests: `Tests/` — `ShelfPacker`, `LineBreaker`, `AutoSizeSolver`, `MarkupParser`, `CurvedBaseline`
(pure logic, no FontEngine/TMP).

## Current state (2026-08-29)

Everything compiles. **Text renders correctly in edit mode** (user confirmed). On top of the v1
raw build these feature batches landed and are **awaiting the user's test + a list of
corrections** (user said corrections are coming next):

- **Gradient scope**: `GradientScope { Run, PerChar, Stepped }`. Tag keywords in any order:
  `h`/`horizontal` · `v`/`vertical` · `perchar` · `perword`/`run`/`smooth` ·
  `stepped`/`step`/`blocky`/`quantized`. `Run` = one smooth gradient across the whole tagged run
  (via `ComputeGradientBounds`). `Stepped` = each letter one flat colour, stepping toward the end.
  2 or 4 colour stops.
- **Per-tag decoration**: `<outline=#c,width>`, `<shadow=#c,dx,dy,soft>`, `<glow=#c,radius,intensity>`
  — extra geometry quads from `EmitSpanFx`, `uv1`-packed mode read by the shader `fxMode` branch.
  `<glowpulse>` = the old animated glow (`BuiltinEffect.Glow`).
- **Edit-mode animated effects**: `wave/shake/pulse/rainbow/glitch` + glowpulse now preview
  outside Play mode, always time-driven (the editor ticker).
- **Component-level Face & Material FX** fields on `SperlichText` (whole label):
  `m_faceDilate/m_sharpness/m_outlineColor/m_outlineWidth/m_shadowColor/m_shadowOffset/`
  `m_shadowSoftness/m_shadowDilate/m_glowColor/m_glowPower/m_glowOuter` → `PushMaterialProps()`
  sets shader `_FaceDilate/_Sharpness/_OutlineColor/_OutlineWidth/_UnderlayColor/_UnderlayOffset/`
  `_UnderlayDilate/_GlowColor/_GlowPower/_GlowOuter`.
- **Base Color** now actually tints (`tint` in `TextMeshBuilder.Build`, `SperlichText` passes
  `color`).
- **Align**: `TextAlign { Left, Center, Right, Justified, Flush, GeometryCenter }`. Real
  `JustifyLine` (distributes slack over breaking spaces, skips trailing spaces). `Justified` skips
  the last line of a paragraph; `Flush` justifies every line incl. last (interpretation not yet
  confirmed by the user). `GeometryCenter` centres on the ink box, not the advance width.
- **Readability lint** always shows an Info/Warning HelpBox (was silent). Advisory only — changes
  nothing at runtime.

## Master test string (all implemented tags)

```
Normal <b>Fett</b> <i>Kursiv</i> <b><i>Beides</i></b> <weight=light>Light</weight>
<color=#ff5555>Rot</color> <alpha=#80>halbtransparent</alpha> <mark=#ffee0055>markiert</mark>
<size=160%>gross</size> normal <size=55%>klein</size> H2O<sub>tief</sub> x<sup>hoch</sup> <cspace=0.35>g e s p e r r t</cspace>
<u>unterstrichen</u> <s>durchgestrichen</s> <uppercase>uppercase</uppercase> <smallcaps>smallcaps</smallcaps>
<gradient=#ffee00,#ff2200>vertikal smooth</gradient>
<gradient=horizontal,#ffffff,#3399ff>horizontal smooth ueber das ganze Wort</gradient>
<gradient=horizontal,stepped,#ffffff,#3399ff>horizontal stepped</gradient>
<gradient=perchar,#ff00ff,#00ffff>perchar</gradient>
<gradient=#ff0000,#00ff00,#0000ff,#ffffff>vier Ecken</gradient>
<outline=#000000,0.25>Outline</outline> <shadow=#000000cc,0.08,-0.08,0.08>Schatten</shadow> <glow=#ffcc66,0.7,1.6>Glow</glow>
<outline=#3399ff,0.2><glow=#3399ff,0.6,1.4>Outline plus Glow</glow></outline>
<wave>Wave</wave> <shake>Shake</shake> <pulse>Pulse</pulse> <rainbow>Rainbow</rainbow> <glitch>Glitch</glitch> <glowpulse>GlowPuls</glowpulse>
<link="id_a">Klickbarer Link A</link> und <glyph:Jump> <sprite="heart">
Sonderzeichen: aeoeue AEOEUE ss - "quote" 'single' >>guillemet<< -- dash ... ende
```

## Known deferred limitations (not bugs)

- `<glyph:…>` / `<sprite=…>` reserve a blank box — no `ITextGlyphSource` / sprite-atlas UV resolve
  wired yet.
- `<link>` needs the `TextInteraction` component + an input module that sends pointer-move events.
- Kerning returns 0 (plumbed through layout; wire via `fontAsset.fontFeatureTable`).
- Glow is alpha-blended, not additive bloom.
- Typewriter reveal is play-mode only.
- Multi-line run-gradient restarts per line.
- `<wave=amp,freq,speed>` custom params not parsed (presets only).
- One `Schedule().Complete()` per built-in effect (not a single combined pass).
- TMP owns the dynamic atlas + packing; `enableMultiAtlasSupport:false` → full rebuild on overflow.

## Rules for this package

- Only edit `.cs` and the `.shader`. Never touch `.unity` / `.prefab` (project rule). Scene/prefab
  setup steps go to the user as instructions — see the README "Setup in the editor" section.
- Keep all TMP API contact inside `FontAccess` + `GlyphStore`.
- Anything time-based must go through `SperlichTextClock` (never `Time.deltaTime` directly).
- `///` XML doc for types/methods, `//` for inline only, prefer self-documenting names.
- When a batch is confirmed good, fold its summary into the README table + "Known follow-ups" and
  trim this "Current state" section down to the confirmed baseline.
