# Third-party notices

Code in `Assets/com.sperlich.text/ThirdParty/` and portions of `Runtime/Rasterizer/`
derive from the projects below. All are under permissive licenses. Per-file license
headers are preserved verbatim; consult them for the exact terms.

## Typography.OpenFont

- Source: https://github.com/LayoutFarm/Typography (`Typography.OpenFont` module)
- License: MIT overall, with per-file Apache-2.0 / BSD-3-Clause / MIT headers
  (WinterDev, Samuel Carlsson, Apache/PDFBox, Adobe AFDKO, FreeType project, and others).
  Full text: `Typography.OpenFont/LICENSE-Typography.md`.
- Vendored as source. Excluded from the copy: `TrueTypeInterperter/` (bytecode hinting —
  unreferenced by the outline path, and the only `unsafe`/`Span` user) and the MSBuild
  shared-project scaffolding (`*.shproj`, `*.projitems`).
- Used for: reading `.ttf` / `.otf` glyph outlines, metrics, `cmap`, and (later) kerning.

## msdfgen (algorithm reference for `Runtime/Rasterizer/`)

- Source: https://github.com/Chlumsky/msdfgen — Viktor Chlumsky
- License: MIT
- The `Runtime/Rasterizer/` distance-field generator is an independent C# port of the
  msdfgen core algorithm, not a wrapper. Cross-checked against the C# ports
  https://github.com/DWVoid/Msdfgen.Net and https://github.com/vazgriz/CSharpGameLibrary
  (both MIT-family). Ported files carry a header crediting msdfgen.
