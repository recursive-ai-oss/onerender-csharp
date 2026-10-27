# Rendered page facts

`RecursiveAI.OneRender.Accessibility` lays a document out with the engine and returns OneHtml's `RenderedFacts` (format version 1) for it: the same facts, with the same meaning, that a `DOMSnapshot.captureSnapshot` capture from a browser yields, so a consumer can switch sources without changing its rules.

## Route

The package reads the laid-out page and hands each box straight to OneHtml.Accessibility's `RenderedFactsBuilder` (OneHtml 1.2): no snapshot JSON is written or parsed, and there is no cap on the size of the page as bytes. `RenderedFactsLimits` (`FactsOptions.Limits`) bound boxes, text boxes and strings and throw `RenderedFactsLimitException` before storing past a bound; `FactsOptions.BackgroundScanLimit` (default 16384 display items) bounds the backward scan for colors behind one text box. All walks are iterative; cost is linear in nodes, text boxes and display items. Nodes are numbered as a snapshot would number them (document order, pseudo-elements included, the doctype counted but not a node of the facts), so `BackendNodeId` is that number plus one and `SnapshotNodeIndex` the number. Generated content (`::marker`, `::before`, `::after`) is added as generated boxes of its host: the pseudo-element's own box, then the box of its generated text.

The facts are the same as the earlier snapshot route gave on the text-block, templates and tailwind pages (every box, text box, style value, paint order and color), except that `StackingContextNodeIndex` is set for every node, as the builder derives it, including the doctype that the adapter left at -1.

## API

- Low level: `AccessibilityFacts.FromLayout(page, queries, displayList, paintTrace, options, styleContext, ct)` for a page the host already laid out and painted, with a `PaintTrace` (an `IPaintObserver`) attached to the host's `Painter`.
- Mid level: `FromPage(HtmlPage, …)` paints and queries itself; `FromInteractive(InteractivePage, …)` reads the page's own layout and style context, so hover and focus state show in the facts.
- Happy path: `AccessibilityFacts.Render(html or Document, HtmlPageOptions, FactsOptions?, ct)` returns a `RenderedPage` (document, page, facts).
- `FactsOptions.ComputedStyles` lists the properties to serialize; `StandardStyleList` is the 96-property list used by the browser captures.

## Fact semantics

- **Boxes.** One layout item per element that has a principal-box fragment: the union of its fragments' border boxes in document coordinates, CSS px, ignoring transforms. `display: none` and `display: contents` have no box; `visibility: hidden` and `opacity: 0` keep theirs. Generated `::before`, `::after` and `::marker` boxes are child nodes with a box item.
- **Text.** A text item exists only where a text fragment exists. Text boxes are in layout order (not sorted), as UTF-16 start and length into the layout text; a preserved line feed gets an extra zero-width box. The layout text is the source text with `text-transform` applied when that keeps its length.
- **Rects.** Offset, client and scroll rectangles come from the CSSOM View queries; `br` and `wbr` report zeros.
- **Paint order.** A "layer" is any positioned box, stacking context, or box that clips (`overflow` other than `visible`). Layers are numbered in the order the painter enters them (the root element is 1, the document 0); boxes painted into the same layer share a value. Stacking-context flags come from the painter (CSS 2.2 Appendix E, plus the CSS Transforms, Compositing and Filter Effects triggers).
- **Color behind text.** Given only for an element with no element children and non-blank painted direct text. The display list is scanned backward from the text for fills (solid; a gradient counts as its first stop) covering the text box origin, composited over white with alpha quantized to 8 bits and each channel rounded when two colors mix. Opacity groups that enclose the text are not part of the background and count in the text opacity; groups that closed earlier flatten their fills with their opacity. The color is absent when an image, inline SVG or backdrop filter covers the point, or when a fill sits inside a closed filter, mask or non-normal blend scope (those change pixels in ways one color can't express); fills inside mask layers are skipped.
- **Text opacity.** The product of the opacities of the groups enclosing the text, as a 32-bit float.
- **Computed styles.** Resolved values in the browser's serialization: used `width`/`height`, used padding and margin when specified as percent or `auto`, `currentcolor` replaced by the color, unitless `line-height` as px, `min-*: auto` as `0px` outside flex and grid, `word-spacing: normal` as `0px`, `outline-width: none` as `3px`, numbers with six significant digits.
- **Form state.** `inputValue` for every input (empty by default; `on` for checkbox and radio), `checked`, textarea text, option selectedness by the HTML selectedness algorithm. No click-ability facts.

## Known differences from the browser captures

Markers have no text and their boxes are shapes, not text-width boxes; authored `oklch()` and similar colors serialize as `rgb()`; transforms are not applied to bounds; scroll offsets are ignored at the blending point; `option` elements get boxes inside `select` and `progress` gets none; initial `text-align`, `font-family` and `top`/`left` resolved values differ in places; sub-pixel layout rounding differs in offset rects (agreement within 1 px is 100% on the text-block set).

## Agreement with the browser

`tools/geomdiff` commands `facts-capture` and `facts-compare` (`scripts/run-facts.sh`) capture the browser's facts on the same page with the same font files and compare fact by fact. Per-set summaries for the committed sets are in `results/facts/<set>/`.
