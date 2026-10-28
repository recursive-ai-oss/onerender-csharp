# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [1.3.0] - 2026-10-28

### Added

- An `input type=range` is styled through its parts. `::-webkit-slider-runnable-track`, `::-webkit-slider-thumb` and the standard and `-moz-` names for them (OneHtml 1.5.0) set the track's and thumb's size, background, border, radius, shadow and margin (the `margin-top: -4px` centring idiom too). The range is as high as its track, the track is centered in the input, and the thumb moves along the track's content box with the value. `appearance: none` on the input or the thumb switches from the native look to the author's boxes. The native look is unchanged: a native thumb is 16 px whatever its rule says, and the input's own border and background stay unpainted.
- A rule such as `input:hover::-webkit-slider-thumb` restyles the range in place. A page whose only rules that combine a state with a pseudo-element name range parts no longer builds the page again on every hover, focus or press.
- Dragging a range follows the thumb as laid out (a styled thumb's width and the track's border and padding), and mirrors in a right-to-left range.
- New styled-node kinds `SliderContainer`, `SliderTrack` and `SliderThumb`, and `ControlKind.RangeThumb`.
- A `range-parts` geometry and pixel set of six pages (custom sliders, native with styled parts, the parts' box model, states, ranges in flow, dark scheme).

### Changed

- OneHtml and OneHtml.Accessibility 1.5.0.
- A range with `appearance: none` is 129 px wide like a native one (it had no size), and its baseline is the bottom border edge under both appearances.

## [1.2.0] - 2026-10-27

### Added

- Form-state selectors follow live form state: `:checked`, `:indeterminate`, `:disabled`, `:enabled`, `:read-only` and `:read-write` (with `:checked + label`, `:has(:checked)`, disabled `fieldset` and radio groups) restyle when the user clicks or uses the keyboard, and after `SetChecked`, `SetIndeterminate`, a selected option, a form reset or a `disabled` attribute edit, and equal a page parsed with that state. A host can style `appearance: none` checkboxes and radios with these selectors.
- `InteractivePage.SetIndeterminate` and `IsIndeterminate`.
- A text control's selection highlight takes its `color` and `background-color` from `::selection` rules.
- `color-scheme` (`light`, `dark`, `light dark`, `only`) and the system colors under it (`Canvas`, `CanvasText`, `Field`, `ButtonFace`, `GrayText`, `Highlight`, `AccentColor` and the rest, with Chrome's light and dark values), `light-dark()` by the element's used scheme, and the dark canvas of a dark root. `ColorSchemes`, `ComputedValues.ColorScheme` and `DarkScheme`.
- `HtmlPageOptions.PrefersDarkColorScheme`, `HtmlRenderOptions.PrefersDarkColorScheme` and `StyleTable.PrefersDarkScheme`: the host's preferred scheme, used by `color-scheme: light dark` and `prefers-color-scheme`.
- Native form controls in Chrome's dark palette: fields, buttons, selects and list boxes, checkboxes and radios, range, progress and meter, disabled states, the focus ring and the select popup.
- `accent-color` follows Chrome's contrast rules: the check mark is white or dark by contrast, and a range or progress track turns dark under a light accent (and light under a dark accent in the dark scheme); the track border leaves a ring over the filled part.
- `::placeholder` rules set the `color` of a text control's placeholder; the default is `#757575` under both schemes.
- A `forms-themed` geometry set and pixel-similarity runs under a dark preferred scheme (`GEOMDIFF_PREFERS_DARK`).

### Changed

- OneHtml and OneHtml.Accessibility 1.4.0.
- Hover, active and focus on an HTML page, and form state changes, go through OneHtml's live cascade: restyling allocates nothing once warm (a hover on the 1,000-node HUD page was about 24 KB).
- `@keyframes` come from OneHtml's resolved keyframes: `@layer` and origin precedence, `var()` per element, merged offsets. Animations that are already running keep the keyframes they started with.
- `OneRender.Interaction.ElementStateChange` now has the same name as a type OneHtml 1.4.0 added to `RecursiveAI.OneHtml.Css`. Code that imports both namespaces needs a using alias or a qualified name.
- Light-scheme paint, measured from Chrome again: the track border of a range and progress is drawn over the filled part, the focus ring has a 1 px white outer ring, the selected option of an unfocused list box is `#cecece` (was `#cbcbcb`), a disabled list box is white with a `#e2e2e2` border, and the textarea resizer has a light second stroke.
- The range thumb is item part 6 and its ring part 8 of the control's ids (the parts of the track and the thumb are painted separately).

## [1.1.0] - 2026-10-27

### Added

- `InteractivePage.SetTextContent`, `SetClassName`, `AddClass`, `RemoveClass`, `ToggleClass`, `SetAttribute`, `RemoveAttribute`, `GetTextContent`, `GetClassName` and `GetAttribute`: change the text of an element with one text node, and classes and attributes (`disabled`, `hidden`, `style`, `checked`, `value`, `href`, `tabindex`, `name` ...) of HTML elements, after the page was created. The page restyles, lays out and paints incrementally where it can (otherwise it is built again from the edited document), keeps focus, hover, scroll and form state, moves focus off an element that stops being focusable, and equals a page created from the equivalent HTML. Text changes allocate nothing once warm; class and attribute changes make a new OneHtml document snapshot (about 0.8 MB on a 1,800-node page; attributes a control reads when its boxes are made, such as `value`, `checked` and `type`, rebuild the page, about 7 MB). Use text, `transform`, `opacity` or a hover rule for per-frame changes. See `docs/game-hosts.md`.

### Fixed

- A radio group whose checked radio is the page's first tab stop kept the wrong stop (the first or last radio instead of the checked one).
- `InteractivePage.SetValue` threw a `NullReferenceException` on a text control that had never been focused.

## [1.0.1] - 2026-10-27

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

[1.3.0]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.3.0
[1.2.0]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.2.0
[1.1.0]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.1.0
[1.0.1]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.0.1
[1.0.0]: https://github.com/recursive-ai-oss/onerender-csharp/releases/tag/v1.0.0
