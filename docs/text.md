# Text service (`OneRender.Text`)

The service inline layout calls to turn computed font properties and text into shaped glyphs. It is independent of the box and
fragment trees; layout reaches it through `IFontProvider` (`OneRender.Layout`), and `TextFontProvider` is the engine's
implementation. OneFont does the shaping, bidi, line-break opportunities and grapheme segmentation; this layer adds CSS font
matching, `@font-face`, per-character fallback, a shaped-run cache and metrics.

```
ComputedValues ─▶ TextFontProvider ─▶ FontRequest ─▶ FontCatalog.Resolve ─▶ ResolvedFontList (per-char fallback)
                        │                                                        │
                        └──────── Shape(text) ─▶ TextShaper (bounded cache) ◀────┘ ─▶ ShapedText
```

## What layout calls

| Call | Does | Frame cost |
|---|---|---|
| `IFontProvider.Resolve(in style)` | Font matching (CSS Fonts 4 §5.2) for the style's family list, weight, style and stretch. | 0 B after first use of the style |
| `IFontProvider.Shape(text, in style, rtl)` | Shapes one bidi-level run with fallback; returns a cached `ShapedText`. | 0 B on a cache hit |
| `IFontProvider.GetMetrics(in style)` | `FontMetricsInfo` of the primary face at `font-size`. | 0 B |
| `TextFontProvider.Shaper.ShapeInto(run, text, request, rtl)` | Reshapes into a retained `ShapedText` buffer (no cache). | 0 B once the buffer has grown |
| `TextAnalysis` | `GetBidiRuns` (UAX #9), `GetVisualOrder`, `GetLineBreakOpportunities` (UAX #14), `GetGraphemeStarts`. | caller-provided buffers |
| `IFontProvider.Catalog` | Add fonts (`AddFont`), `@font-face` rules (`AddFontFace`), generic mapping, resolver, system-font provider, diagnostics. | document path |

Providers, catalogs and shapers are single-thread objects. The happy path is `OneRender.Image.FontProviders`:
`CreateDefault(resolver, systemFonts)` builds a `TextFontProvider` with the packaged faces and their aliases
(`DefaultFonts.TryResolveFamily`) registered, and `ForCurrentThread()` returns one cached per thread. `HtmlRender.ToImage`,
`HtmlPage` callers, `GeometryProbe` and the WPT and geometry tools use it. Page `@font-face` rules are added to
`provider.Catalog` via `FontFaceRule.FromNode` or `FontFaceRule.Create`. Adding a face invalidates resolved lists
(`FontCatalog.Version` increments), and cached runs for stale lists are no longer returned.

## Matching

- Family list in order. A generic keyword resolves through `SetGenericFamily` if the host set one, otherwise through the aliases
  (with `DefaultFonts`: `sans-serif`, `system-ui` and `ui-sans-serif` → Liberation Sans, `serif` → Liberation Serif,
  `monospace` → Liberation Mono). Names are compared ASCII case-insensitively and go through the aliases first (Arial,
  Helvetica, Times New Roman, Courier New, Calibri, Cambria).
- Within a family, CSS Fonts 4 §5.2: stretch first, then style, then weight, using the spec's search directions
  (weight 400/500 special cases, lighter below / heavier above the target).
- After the request's list come the default faces: sans-serif, serif, monospace, then other registered families.
- Synthetic bold applies when the request weight is at least 600 and the chosen face's heaviest weight is under 600; synthetic
  italic slants by 14° (or the requested oblique angle) when the chosen face is upright. Both obey `font-synthesis` and are
  reported as flags on `ResolvedFace` for paint; the text service never alters glyph outlines or advances.
- `local()` consults only the host's `ISystemFontProvider` (off by default); it never sees the registered faces.
- Computed-value conversion (`TextFontProvider.GetRequest`): a `font-size` of 0 or less becomes 0.001 px, weight is clamped to
  1–1000. The request cache holds 4096 distinct font property sets and is cleared when full.

## First available font, `ex`, `ch`, `ic`

The first available font of a style (CSS Fonts 4 §5.1) is the first listed family with a usable face whose `unicode-range`
includes U+0020; a family limited to other characters is skipped even when listed first. The default faces after the author's list
do not count in this pass. If no listed family qualifies, the old rule applies: the first family with a space glyph, else with any
usable face. `PrimaryFace` and the strut metrics use it.

The style computer asks the font provider for that face's metrics once per computed style, right after `font-family`, `font-size`,
`font-weight`, `font-style` and `font-stretch` are known, and resolves `ex` (x-height), `ch` (advance of "0") and `ic` (advance of
U+6C34) from them (CSS Values 4 §6.1.1). Where the face lacks the measure, the spec's fallbacks apply: 0.5 em for `ex` and `ch`, 1 em
for `ic`. `font-size` itself is computed in the parent's font, so its `ex` and `ch` use the parent's face. The font set must be
complete before styles are computed (the page registers `@font-face` rules first); cached styles keep the numbers they were
computed with. A page built without a font provider uses the fallbacks.

`rex`, `rcap`, `rch` and `ric` (CSS Values 4 §6.1.2) use the root element's font, not the element's: after the root's style is computed
its face gives the four ratios, which every later style receives (`LengthContext.RootExRatio`, `RootCapRatio`, `RootChRatio`,
`RootIcRatio`), so `10rch` is the same length in a monospace span as in the root. The root's own `rex` uses its own font. A style cached
under a different root x-height ratio is recomputed.

`white-space` is a shorthand of `white-space-collapse` and `text-wrap-mode` (CSS Text 4 §3.1, with `white-space-trim` accepted and
ignored); OneHtml expands it, and both longhands inherit, so a child of `white-space: pre` keeps preserved newlines and no wrapping.

## `@font-face`

`FontFaceRule` carries `font-family`, `src`, `font-weight`/`-style`/`-stretch` (ranges, with `auto` = 400 / normal / 100%;
reversed ranges are swapped), `unicode-range` and `font-display` (recorded only: there is no clock-driven swap).
`src` entries are tried in order: `url()` through the host's `IFontResourceResolver` (the engine never fetches; `data:` URLs
are decoded in place), `local()` through the system provider, `format()`/`tech()` filtering (unknown formats, colour, AAT and
palette tech are skipped; `variations` is accepted and the default instance is used). Faces load lazily the first time a
character needs them. A face outside its `unicode-range` is skipped for that character. Failures become diagnostics, never
exceptions.

**In a document.** `HtmlPage.Create` removes the `@font-face` faces left by an earlier document from the provider's catalog, then walks the document's style sheets (top level, `@media`, `@supports` and `@layer` blocks that apply) and adds every rule. Font files are fetched through the page's `StylesheetBytes` lookup, the same host callback as style sheets, so a relative `src` resolves against the sheet's address. `HtmlRender`, `HtmlRenderSeam` and the WPT runner get this through `HtmlPage`. Faces load lazily during layout, so a font that first appears in a later incremental change is not loaded by it.

`FontLoader.Load(bytes, limits)` reads TrueType/OpenType, WOFF (zlib) and WOFF2 (Brotli, with the glyf/loca and hmtx
transforms). `FontLimits` bounds input size (32 MiB), decoded size (64 MiB) and table count (256) before anything is
allocated. Runtimes without a native Brotli (browser WebAssembly) report WOFF2 as `UnsupportedFormat` with a message.

## Fallback and diagnostics

`ResolvedFontList.Select(codePoint)` walks the list (cluster-aware: a base character and its combining marks stay in one face
where one face covers the cluster). Private-use characters skip the generic and default faces. A character no face covers
renders the primary face's `.notdef` and records a `TextDiagnostic` of kind missing glyph whose `Package` names the opt-in
package that would cover it (`RecursiveAI.OneRender.Fonts.Noto.<Script>`, `.Fonts.Cjk`, `.Fonts.Emoji`; the table is
`FontPackages`). `TextDiagnostics` keeps 512 entries and counts the rest in `Dropped`. Canonical-equivalence matching
(Fonts 4 §5.4's normalization step) is not done.

## `ShapedText`

Glyphs are in **visual (display) order**, in CSS px, **y down** from the baseline origin, advances unrounded (rounding policy is
decided against the Chrome differential).

- `GlyphIds`, `X`, `Y`, `Advances`, `GlyphClusters` (per glyph: UTF-16 offset of its cluster in `Text`), `Width`, `Size`,
  `RightToLeft`.
- `Clusters`: `GlyphCluster` records (text range, glyph range, advance, missing flag) that tile the text; line breaking cuts runs
  at cluster boundaries.
- `Segments`: maximal glyph ranges in one face (`ResolvedFace` with its synthetic bold/italic flags). The display list's glyph run
  item takes one segment: the OneFont `FontFace`, glyph ids, positions, source text and cluster map.
- `MissingGlyphCount`, `FirstMissingGlyph`.

Kerning and common ligatures are on by default; `FontFeatureSettings` carries `font-kerning`, `font-variant-*` subsets and
`font-feature-settings` tags. Text over `TextShaper.MaxRunLength` (1 Mi code units) throws `ArgumentException`.

**Cache.** `TextShaper` keeps a 4-way set-associative table (default 4096 entries, LRU within a set) keyed on text, matched
list, size and direction; texts up to 2048 code units are cached. A hit returns the same `ShapedText` object (do not modify it).
`ClearCache` empties it.

## Metrics

`FontMetricsInfo` (px at the requested size): ascent, descent, line gap (the horizontal header; the OS/2 typographic metrics when the font sets
USE_TYPO_METRICS or has no header values; the Windows metrics as the last resort), x-height, cap-height, underline and strikethrough position and thickness, zero advance, and the
`size-adjust`/`ascent-override`/`descent-override`/`line-gap-override` descriptors of an `@font-face` face applied. Values are
not rounded. `font-size-adjust` is computed by layout from `XHeight`, `CapHeight` and `ZeroAdvance`.

## Limits and determinism

Hostile font data fails with a status and detail, at the `FontLimits` above. A fuzz target (`fuzz/`, target `fontdata`) drives
the loader, descriptor parser, matching, fallback and shaping. Nothing in the service reads a clock, the file system or the
network; the same fonts and inputs give the same runs on every platform.
