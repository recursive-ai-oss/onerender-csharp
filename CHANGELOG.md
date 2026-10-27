# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [1.0.1] - TBD

### Fixed

- **WebAssembly:** `HtmlPage`, `InteractivePage` and `LivePage` crashed the Mono runtime (`Assertion: should not be reached at class-accessors.c`, or `memory access out of bounds`) on the first garbage collection after the first page, in Debug and Release builds alike. `ComputedValues.Initial` was a large `static readonly` struct, and Mono on 32-bit WebAssembly does not keep the references held by such a static alive across a collection. `ComputedValues.Initial` is now a `ref readonly` property over a heap object (source compatible; code compiled against 1.0.0 must be recompiled).
- A `LivePage` over a `StyledTree` with no nodes no longer loops until memory runs out.
- Sequential focus navigation treats a radio button group as one tab stop: Tab lands on the checked radio (first or last in the direction of travel when none is checked), including checkedness set after creation with `SetChecked` or by the user. Arrow keys move within the group and check the new radio.

### Changed

- The browser-wasm smoke app now runs in headless Chrome in CI, in Debug (interpreter, no native relink) and Release, instead of only being published.

## [1.0.0] - 2026-10-27

First release. All packages share one version.

### Added

- **RecursiveAI.OneRender** (the engine; depends on OneHtml and OneFont only)
  - Styled-node tree with typed computed values, fed by OneHtml's DOM and cascade or by a C# builder (`StyledTree`); box tree; incremental layout into a fragment tree.
  - Layout: block and inline formatting, margin collapsing, floats, positioning (relative, absolute, fixed, sticky), overflow and scrolling, flexbox, grid with subgrid, the CSS table model, multi-column, intrinsic sizing, aspect-ratio, bidi text with OneFont shaping and line breaking, `@font-face` (WOFF and WOFF2).
  - Paged media: fragmentation, `@page`, margin boxes, page counters, named pages, `string-set`, `target-counter`, leaders, repeated table headers and footers.
  - Paint into a retained, versioned display list with stable item IDs and damage regions: backgrounds, gradients, borders and radii, shadows, transforms, opacity, blend modes, clip paths, masks, filters, images, inline SVG, generated content and lists. Transform and opacity changes patch the list without relayout.
  - Interaction: hit testing, DOM-standard events (capture, target, bubble, `preventDefault`), focus, scrolling with scroll snap, text selection, CSS Transitions and Animations on the host's clock, synchronous layout queries.
  - Form controls with Chrome's default look, text editing with caret, selection, undo, clipboard hooks and IME composition, and the `select` dropdown as an overlay in the same display list.
  - Incremental restyle, relayout and repaint equal a full recompute; steady-state frames and the common incremental changes (hover, text change, one subtree relayout, transform and opacity animation) allocate nothing.
  - Layout explanation and box-to-source mapping, off by default.
  - NativeAOT-, trim- and WebAssembly-clean; no network, clock or process access.
- **RecursiveAI.OneRender.Image**: display list to pixels (`HtmlRender.ToImage`, `DisplayListRasterizer`) through OneImage; inline SVG; colour bitmap glyphs.
- **RecursiveAI.OneRender.Pdf**: display list to PDF (`HtmlPdf.ToPdf`, `DisplayListPdfWriter`) through OnePdf core: tagged by default from HTML semantics, `/ToUnicode` and `/ActualText`, font subsetting, PDF/UA-1 and PDF/A-2a profiles, rasterized islands where PDF cannot express an effect.
- **RecursiveAI.OneRender.Razor**: `RazorRenderer` renders a Razor component to HTML, PDF or an image in one call, with `wwwroot` and static web asset resolution and `AddOneRender()`.
- **RecursiveAI.OneRender.Accessibility**: rendered page facts (boxes, paint order, visibility, text boxes, background colours) in OneHtml's `RenderedFacts` shape.
- **RecursiveAI.OneRender.Fonts**: Liberation Sans, Serif and Mono, Carlito and Caladea, unmodified, with their OFL texts.
- **RecursiveAI.OneRender.Fonts.Noto.\*** (29 script packages), **.Fonts.Cjk** and **.Fonts.Emoji**: opt-in script coverage; the engine names the package that covers any missing glyph.

### Known limitations

- Tagged PDF/UA-1 output with full complex-script sentences (Arabic, Devanagari, Thai) can fail veraPDF rule 7.21.7, because glyphs inside multi-glyph clusters have no `ToUnicode` mapping. Extracted text is still correct.
- Vertical writing modes, date/time/color pickers, file input, `datalist`, CSS footnotes, scroll-driven animations and view transitions are not implemented.
- `<img>` of an SVG file is not drawn; inline `<svg>` is.
- Container queries and `@scope` depend on OneHtml support that is not yet available.
- WOFF2 fonts on browser WebAssembly need decoded bytes from the host (the platform has no Brotli).

[1.0.0]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.0.0
