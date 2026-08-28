# Sperlich.Text

A TextMeshPro-style label for uGUI, built from the architecture plan on the user's desktop
(`unitytextrendererplan.md` + the two companion docs). Own atlas at runtime, **no font-asset bake step**,
Latin / German focus. Deliberately out of scope: emoji, BiDi / RTL, complex-script shaping, CJK IME.

Namespace: `Sperlich.Text`. Assemblies: `Sperlich.Text` (runtime), `Sperlich.Text.Editor`, `Sperlich.Text.Tests`.

---

## What is implemented (v1)

| Plan module | Status | File(s) |
|---|---|---|
| 2 Font access + fallback chain | done | `Fonts/FontAccess.cs`, `Fonts/FontDefinition.cs` |
| 3.1 Distance field | **SDF (SDFAA) via `TMP_FontAsset` dynamic mode** (MTSDF path reserved) | `Fonts/FontAccess.cs` |
| 3.2 Generation pipeline | queue + tofu placeholder + amortised per-frame budget | `Pipeline/GlyphStore.cs` |
| 3.3 Atlas / packing | TMP owns the dynamic atlas + packing in v1; `ShelfPacker` ready for the MTSDF path | `Atlas/ShelfPacker.cs` |
| 4 Weight / style synthesis | faux bold/light (SDF threshold), faux italic (vertex shear) | shader + `TextMeshBuilder` |
| 5.1 Standard layout | advance, wrap (UAX #14 subset), multi-line, alignment | `Layout/TextLayoutEngine.cs`, `Layout/LineBreaker.cs` |
| 5.2 Curved baseline | polyline path, arc-length distribution, tangent rotation | `Layout/CurvedBaseline.cs` |
| 5.3 Auto-size + overflow | binary-search auto-size; Clip / Ellipsis / ScaleToFit / Scroll | `Layout/AutoSizeSolver.cs`, `TextLayoutEngine` |
| 5.4 Micro-typography defaults | line 1.5x / paragraph 2x, uppercase tracking, soft hyphen | `Common/TypographyDefaults.cs` |
| 6 Markup parser | color, gradient, size, weight/b/i, u, s, mark, cspace, sub/sup, case, link, sprite, glyph | `Markup/MarkupParser.cs` |
| 7 Mesh generation | `NativeArray` / `MeshData` from phase 1, glyph + underline + mark + selection quads | `Mesh/TextMeshBuilder.cs` |
| 8 Shader | SDF sampling, screen-space AA, outline, drop shadow, glow, gradient, gamma-correct | `Shaders/SperlichTextSDF.shader` |
| 9 Effects | Ebene 1 `ITextEffect` (plain C#) + Ebene 2 Burst `IJobParallelFor` catalog | `Effects/*` |
| 9.3 Reveal / typewriter | per-char callbacks, punctuation pauses, skip, pause-aware | `Effects/RevealController.cs` |
| 10 Input glyphs | `ITextGlyphSource` abstraction + opt-in Rewired adapter | `Glyphs/*`, `Adapters/Rewired/*` |
| 11 Interaction layer | link hit-testing (bounds, no raycast), hover / click events | `Interaction/TextInteraction.cs` |
| 12 Input / editing layer | caret + selection + keyboard + clipboard (single/multi-line, no IME) | `Editing/SperlichTextInputField.cs` |
| 13 Editor tooling | custom inspector, tag-insert toolbar, readability linter, live preview | `Editor/SperlichTextEditor.cs` |
| 14 Public API component | `SperlichText : MaskableGraphic`, measure API, alloc-light `SetText(StringBuilder)` | `Rendering/SperlichText.cs` |
| 15 Draw-call / alloc principles | shared atlas per font (`GlyphStoreRegistry`), reused native buffers | `Rendering/GlyphStoreRegistry.cs` |

## Deliberate v1 simplifications (documented, not bugs)

- **SDF via TMP, not raw FontEngine, not MTSDF.** Unity 6's public `FontEngine` API cannot rasterise a
  glyph to a texture — every rendering method (`RenderGlyphToTexture`, `TryAddGlyphToTexture`, …) is
  `internal`. The only public path to a runtime SDF atlas is `TMP_FontAsset.CreateFontAsset(font, …)` in
  `AtlasPopulationMode.Dynamic`, so `FontAccess` wraps one `TMP_FontAsset` per face (fallbacks go in its
  `fallbackFontAssetTable`). This adds a dependency on `Unity.TextMeshPro` (already in the project via
  `com.unity.ugui`). msdfgen (native plugin) or a CPU EDT pass would replace only `FontAccess`; edge
  sharpness at small sizes is the current trade-off.
- **TMP owns the dynamic atlas + packing.** `ShelfPacker` is written and unit-tested for the future MTSDF
  path where this package owns the bitmap. `enableMultiAtlasSupport:false` — when the atlas fills up the
  store clears and repopulates it rather than spawning a second atlas texture.
- **Kerning returns 0.** `FontAccess.GetKerning` is plumbed through the whole layout path; wiring it via
  `fontAsset.fontFeatureTable.glyphPairAdjustmentRecords` is a self-contained follow-up.
- **"Background" glyph generation = amortised per-frame budget**, not `Task`/`Thread`. That step becomes a
  real background thread only with the msdfgen plugin (per the performance-architecture doc); the
  queue / placeholder / swap-in shape is already correct.
- **Justified alignment falls back to Left** (the research doc argues against justified as a default anyway).
- **Compute / Jump-Flood tier not built.** Target is PC + WebGL; WebGL has no compute shaders, so the
  Burst/Jobs CPU path is the plan. Revisit only if glyph throughput ever demands it.
- **Markup parse allocates** new lists per call. `SetText(StringBuilder)` avoids the string alloc; a fully
  pooled markup pass is a later polish item.

## TMP API surface used (all public, verified against 6000.3.9f1)

Isolated to `Fonts/FontAccess.cs` + `Pipeline/GlyphStore.cs`:

- `TMP_FontAsset.CreateFontAsset(Font, int samplingPointSize, int atlasPadding, GlyphRenderMode.SDFAA, int w, int h, AtlasPopulationMode.Dynamic, bool enableMultiAtlasSupport)`
- `fontAsset.faceInfo` (TextCore `FaceInfo`), `.atlasTexture`, `.atlasWidth`, `.atlasPadding`, `.fallbackFontAssetTable`
- `fontAsset.characterLookupTable` (`Dictionary<uint, TMP_Character>`), `fontAsset.TryAddCharacters(uint[], out uint[], bool)`
- `fontAsset.ClearFontAssetData(false)` for the atlas-full rebuild
- `TMP_Character.glyph` → `Glyph.metrics` / `.glyphRect` / `.index`

If TMP is somehow absent, `Sperlich.Text` will not compile — add `com.unity.ugui` (it is already in `Packages/manifest.json`).

## Setup in the editor (needs scene / prefab work — do this yourself)

1. Create a **Font Definition**: `Assets > Create > Sperlich > Text > Font Definition`. Assign a primary
   `Font` (imported `.ttf`/`.otf`) and optional fallback fonts.
2. Optional: `Assets > Create > Sperlich > Text > Settings`, name it `SperlichTextSettings`, put it under a
   `Resources/` folder, assign the default font and (recommended) the `Sperlich/Text SDF` shader asset so
   it is not stripped from player builds. Otherwise add that shader to **Project Settings > Graphics >
   Always Included Shaders**.
3. Add a **Sperlich Text** component to a UI GameObject under a Canvas (Add Component > Sperlich > Text).
   Assign the Font Definition, type text, done. It previews live without Play mode.
4. For links: add **Text Interaction**. For an input field: add **Sperlich Text Input Field**.
   Link hover needs an input module that dispatches pointer-move events.
5. Rewired glyphs: add `SPERLICH_TEXT_REWIRED` to Scripting Define Symbols, then put `RewiredGlyphSource`
   on a GameObject and register it as the active `ITextGlyphSource` (wire-up hook is game-side).

## Tests

EditMode NUnit tests (pure logic, no FontEngine): `LineBreaker`, `MarkupParser`, `AutoSizeSolver`,
`ShelfPacker`, `CurvedBaseline`. Run from **Window > General > Test Runner**.

## Known follow-ups (next small steps)

1. Build a scratch scene, confirm a glyph renders end-to-end, tune the shader defaults (SDF spread ↔ `sdfPadding`).
2. Wire real kerning — now easy via `fontAsset.fontFeatureTable.glyphPairAdjustmentRecords`.
3. `<sprite>` / `<glyph>` inline objects currently reserve a blank box — hook a sprite atlas + `ITextGlyphSource` UV resolve.
4. Move built-in effect jobs to a single combined pass (currently one `Schedule().Complete()` per effect).
5. Multi-atlas support (currently `enableMultiAtlasSupport:false` + full rebuild on overflow).
