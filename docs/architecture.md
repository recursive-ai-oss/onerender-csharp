# Architecture: trees, handles and the layout entry

This page describes the front half of the pipeline as built: the styled-node tree, computed values, the box tree and
the fragment tree. Layout algorithms, paint and the display list build on it.

```
OneHtml Document + DocumentStyles ──► HtmlFrontEnd ─┐
                                                    ├─► StyledTree ─► BoxTree ─► ILayoutEngine ─► FragmentTree
C# builder (StyleDeclaration) ──────────────────────┘      (+ StyleTable)
```

## Shared rules

- **Handles.** `StyledNode`, `Box` and `Fragment` are `readonly struct { Tree, Index }`. They hold no data; every
  accessor reads a column of the owning tree. The default value is the null handle (`IsNull`). A handle is valid until
  its tree is cleared or rebuilt; indexes are dense, assigned in document order (parents before children).
- **Storage.** Each tree is struct-of-arrays: parallel `int`/`byte`/`double` columns and first-child / next-sibling
  links, grown by doubling and **retained** across `Clear()` / `Build()`. Rebuilding a tree of the same size allocates
  nothing (asserted by tests for a 1,000-node document built from HTML and from the builder).
- **No recursion on depth.** Every traversal (building, dumping, layout helpers) uses an explicit stack. Trees of
  100,000 levels or 100,000 siblings build and dump.
- **Limits.** `TreeLimits` bounds node count (2M), depth (1,048,576) and text length (32M UTF-16 units); the box tree
  allows twice the node limit. A limit is checked before anything is stored; the over-limit node is dropped and a
  `LimitExceeded` entry goes to `StyledTree.Diagnostics` (capped at 256 entries, the rest counted in `Dropped`).
  Declarations that survive the cascade but do not parse raise `InvalidValue` diagnostics.
- **Determinism.** Ordinal comparisons only, no culture-sensitive APIs, no clock, no file or network access. The host
  supplies natural sizes of replaced content through `IResourceResolver` and fonts through `IFontProvider`.

## Computed values (`OneRender.Style`)

- `PropertyId` lists the longhands the engine models (display, box model, position, sizing, borders, overflow, font,
  text, color and a few more). Shorthands are expanded before they reach the pipeline: by OneHtml's cascade for the
  HTML front end and by `StyleDeclaration.TrySet` for the builder, with `ShorthandFallbacks` covering `white-space`,
  `border-radius` and `overflow`, which the cascade keeps whole.
- `ComputedValues` is one struct of typed fields (enums, `Length`, `ColorValue`, doubles). **Styles are interned**: a
  `StyleTable` stores one row per distinct computed style, and a node holds a `StyleId`. A document with thousands of
  nodes typically has tens of rows. Rows are found by value equality, so `StyleId` equality means identical style.
  The table holds at most 65,536 rows; beyond that a diagnostic is raised and the parent's style is reused.
- A computed style is cached by (declaration source, version, parent `StyleId`, root font size); a warm rebuild
  computes nothing and allocates nothing.
- `Length` keeps what layout needs: pixels for every absolute, font-relative and viewport unit, and an unresolved form for
  percentages and `calc()` containing percentages (`CalcValue` evaluates against a containing-block size at layout
  time). Keywords (`auto`, `min-content`, `max-content`, `fit-content`, `normal`, `none`) are `LengthKind` values.
- `inherit`, `initial` and `unset` are resolved when the style is computed. `revert` and `revert-layer` act as `unset` in
  builder trees, which have no earlier origin.
- **Invalid declarations are dropped in the cascade.** `StyleValidation.Validator` is the engine's grammar offered to
  OneHtml as `StyleSourceOptions.DeclarationValidator` (`HtmlPage` sets it; a host that builds its own `DocumentStyles`
  should too). OneHtml asks it once per declaration, when a rule's declarations are first collected, for author and user
  declarations of properties OneHtml does not type itself. A rejected declaration neither wins nor counts as
  `!important`, so an earlier valid one stands (CSS Syntax 3 section 5.4.6, CSS Cascade 4 section 4.3). For a
  longhand the validator runs the same parser that computes the value, against initial values, so the two cannot
  disagree; for `white-space`, `border-radius`, `overflow`, `flex`, `grid` and the other shorthands OneHtml passes
  through whole, it expands them with `ShorthandFallbacks` and checks every longhand. The check parses the value text
  again (OneHtml's token range cannot be turned back into a value list), which costs one small allocation per
  declaration per stylesheet and nothing per restyle. It holds one lock around a shared scratch computer.
- What still reaches the style computer as invalid behaves as `unset` and raises `InvalidValue` (CSS Variables 1
  section 3.2 for `var()`): values with `var()`, which are only known at computed-value time; properties OneHtml
  types itself (`display`, `position`, the offsets, `overflow-x/y`, `visibility`, `font-size`, `font-weight`,
  `font-style`, `font-family`), where OneHtml's own grammar decides; and builder trees, which have no cascade.
- What is deliberately lossy is listed, with the reason, in `tests/OneRender.Tests/testdata/wpt-style/expected-failures.tsv`
  (colors are stored as sRGB, `content` with counters is kept as raw text, and so on).

## Styled-node tree (`OneRender.Tree.StyledTree`)

Elements and text nodes in document order, each with a tag name (builder trees and diagnostics), a `StyleId` and, for
the HTML front end, the `Node.Index` of the OneHtml node it came from (`StyledTree.FindBySource`). Two front ends feed it:

- `HtmlFrontEnd.Build(DocumentStyles)` walks the OneHtml document with an explicit stack, visits elements and text
  (not comments), computes styles from the cascade, and adds `::before`, `::after` and `::marker` nodes from OneHtml's
  pseudo-element styles when they generate content. Elements the user-agent sheet hides (`head`, `script`, `style`)
  are `display: none` and get no box.
- The builder: `AddRoot` / `AddElement` / `AddText` with a `StyleDeclaration` of CSS text per node
  (`new StyleDeclaration().Set("width", "50%")`). It goes through the same value parsers as a style sheet.

Replaced elements (`img`, `video`, `canvas`, `iframe`, `embed`, `object`, `svg`) have no children in the styled tree.
Their natural size comes from `IResourceResolver` (or `StyledTree.SetNaturalSize` in a builder tree); with none, 300x150
for non-images and NaN for images.

## Box tree (`OneRender.Tree.BoxTree`)

`BoxTree.Build()` generates boxes from the styled tree following CSS Display 3 and CSS2 §9.2.

- A `Box` has a `Kind` (Container, Inline, Text, Replaced, LineBreak, Marker), an outer `Level` (Block or Inline) and,
  for containers, a `FormattingContext` (Block, FlowRoot, Flex, Grid, Table and its parts). Flex, grid and table boxes
  are marked only; table internals are not fixed up.
- `display: none` makes no box; `display: contents` makes the element's children boxes of its parent.
- Anonymous block boxes wrap inline runs when a container also has block-level in-flow children (§9.2.1.1), and anonymous
  inline (text) boxes wrap text (§9.2.2.1). Out-of-flow boxes and outside markers do not split or start runs.
- A block inside an inline splits the inline into pieces linked by `Box.NextPiece` (`Continuation` marks the later ones);
  nesting deeper than 64 turns the block into an inline-block and raises a diagnostic.
- Whitespace-only text that collapses is dropped at run edges, in flex and grid containers and inside table structure
  (CSS Text 3 §4.1.1). Children of flex and grid containers are blockified; their text runs get anonymous items.
- `list-item` boxes get a `Marker` box (a placeholder; none when `list-style-type` is `none`).
- `BoxTree.PrincipalBox(StyledNode)` maps a styled node to its (first) box.
- `BoxTree.Dump()` prints an indented text form, used by tests to compare the two front ends.

## Fragment tree (`OneRender.Layout`)

Layout writes a `FragmentTree` and paint, hit testing and the harnesses read it.

- Kinds: `Box` (a box fragment, one per box or per split piece), `Line` (a line box inside an inline formatting
  context), `Text` (a run of text in one line, referencing a glyph run) and `Column` (a fragmentainer, see
  "Fragmentation"; it has no box).
- **Coordinates.** Every fragment's `BorderBox` is in **CSS px, relative to the border-box origin of its parent
  fragment**; the root's is relative to the initial containing block. `AbsoluteBorderBox` and `AbsoluteOrigin` walk up the
  chain. `Border` and `Padding` are the resolved edge widths; the content box follows from them. `ScrollableOverflow`
  is in the fragment's own coordinates and covers the box's descendants' overflow; `Baseline` is measured from the
  border-box top.
- **Glyph runs.** `AddGlyphRun` stores a OneFont `FontFace`, the font size, glyph ids, x/y positions and cluster
  indexes (into the box's text) in shared columns; a `Text` fragment holds a `GlyphRunId`.
- **Fragmentation.** A box that splits (inline pieces, later fragmentainers) has several fragments chained by
  `Fragment.NextOfBox`; `FragmentOf(box)` returns the first. `Fragment.FragmentainerIndex` is the index of the column a
  fragment landed in (0 outside any fragmentation context).
- **Keyed by element.** `TryGetBorderBox(StyledNode, out Rect)` and `TryGetBorderBox(Node, out Rect)` (a OneHtml node)
  return the union of the element's fragments in document coordinates; this is what the geometry differential compares.
- `FragmentTree.Dump()` prints the tree as text.

## The layout entry

```csharp
public interface ILayoutEngine
{
    void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default);
}
```

`LayoutInput` carries the initial containing block (`AvailableWidth`, and `AvailableHeight` or NaN when the content
decides) and an `IFontProvider`, the text service layout calls: `Resolve(in ComputedValues)` matches a style to a font list with
per-character fallback, `Shape(text, in ComputedValues, rtl)` returns a cached `ShapedText`, and `GetMetrics(in ComputedValues)`
returns the primary face's metrics (see `text.md`; `TextFontProvider` is the engine's implementation). An implementation clears `output`, writes a fragment for the root and for every box that generates one in tree
order, reads nothing but its arguments, and checks the token every few hundred boxes. The result is a pure function of
the box tree, the fonts and the available size. Block and inline flow, floats, positioning, flex, grid and tables are separate implementations behind this one entry; fragmentation is a
stage the block engine runs at the end (see "Fragmentation").

## Block layout (`BlockLayoutEngine`)

`BlockLayoutEngine` implements `ILayoutEngine` for normal-flow block-level boxes: the block formatting context of CSS2
§9.4.1, widths (§10.3.3, replaced §10.3.2/§10.3.4 with the constraint table of §10.4), heights (§10.5 percentages that
behave as `auto`, §10.6.3, min/max §10.7), `box-sizing`, `aspect-ratio` (Sizing 4 §5), margin collapsing (§8.3.1) and
intrinsic sizes (Sizing 3 §5). Grid and table containers are laid out as plain block containers until their own
layout exists (flex containers: see "Flex layout"); floats and `clear` are described under "Floats" below; absolutely positioned boxes are described under "Positioning".

- **No recursion.** One explicit stack of frames (a struct array kept between runs) walks the box tree; a frame is a box
  whose children are being placed. Depth is capped at 262,144 levels (a diagnostic, and the deeper boxes get no fragment).
  Laying out an unchanged tree again allocates nothing.
- **Coordinates.** Every fragment is added when its box is opened, with its border-box rectangle fixed when it closes. Child
  rectangles are relative to the parent's border box, as the fragment tree requires. `Baseline` of a block is its first
  in-flow line or child baseline from the border-box top, NaN when there is none.
- **Margin collapsing.** One margin run (largest positive plus most negative) is kept for the block formatting context.
  A box whose top margin adjoins its first child's (no top border or padding, not a BFC root) is *pending*: the run keeps
  growing through the chain of pending boxes and, when the first thing with content (or a box with its own top border or
  padding, or a BFC root) is reached, it is resolved once and moves the outermost pending box; nested pending boxes sit at
  offset 0 in their parent. A pending box that closes without content collapses through: it sits where a box with a
  non-zero bottom border would (§8.3.1), and its bottom margin joins the run. At the end of a box the run passes through to
  the parent only if the height is auto, nothing is below the last child, the box is not a BFC root, and min/max/aspect-ratio
  do not change the height; otherwise it is resolved into the content height. Boxes that establish a BFC are the root
  element, anything but `display: block`/`list-item`/anonymous containers, floats, absolutely positioned boxes and
  scroll containers (`overflow` other than `visible`/`clip`).
- **Intrinsic sizes** (`GetIntrinsicSizes`) are content-box min-content and max-content widths, cached per box until the next
  `Layout`, computed with an explicit stack. A block's sizes are the largest child contribution (the child's definite width
  or intrinsic size, clamped by min/max width, plus fixed margins, border and padding). They back `min-content`,
  `max-content`, `fit-content` and shrink-to-fit widths. Percentages count as auto.
- **Re-entry.** `LayoutIndependent` lays out one box as its own BFC root, nested inside a running layout; inline layout
  uses it for inline-block and other atomic boxes (`shrinkToFit` selects CSS2 §10.3.5 widths).
- **List items.** An outside `::marker` box gets a fragment one `font-size` wide and one line high just outside the item's
  start edge, at the content top. This is a placeholder until real markers.
- **Not yet:** the block-axis `fit-content`.

### The inline seam (`IInlineLayout`)

A block container with `ChildLayout.Inline` hands its children to an `IInlineLayout`:

```csharp
InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken ct);
void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent);
```

- The request carries the container, the content-box width to fill, the content box's offset inside the container's border
  box (`ContentLeft`, `ContentTop`, where lines are added as children of `parent`), the content box's origin in the BFC
  root's content box (`BfcLeft`, `BfcTop`, where float exclusions are looked up), the font provider and the exclusion space
  (`Exclusions`, see "Floats"). A request built without one has the empty `NoExclusions` space.
- The result is the total line height, the line count and the first and last baselines from the content top. A container
  with lines counts as having content for margin collapsing; the block layout settles the margin run before the call.
- `Measure` returns the content-box min-content and max-content widths.
- The result also carries the extent of everything the lines occupy (`HasOverflow`, `Overflow*`, in the container's border-box
  coordinates), which the block layout adds to the scrollable overflow, and `QueuedAbsolutes`, true when the inline layout has
  already handed the absolute boxes of its content to the block layout (`IAtomicInlineHost.QueueAbsolute`).
- `InlineLayoutEngine` (see "Inline layout") is the default. `PlaceholderInlineLayout` is the stand-in for tests of the block
  layout that need no fonts: a container with any non-blank text, replaced or atomic inline child gets one empty line box as
  tall as `line-height` (`normal` is 1.2 × font size) and the intrinsic inline sizes are 0.

### Floats

Floats, `clear` and the boxes that avoid floats live in `Layout/Block/BlockLayoutEngine.Floats.cs` and `.Open.cs`.

- **Placement (CSS2 §9.5.1).** Each block formatting context keeps its floats as margin boxes in the coordinates of its root's
  content box, sorted by top (rule 5 makes the tops non-decreasing). A float is laid out first, as the root of a context of
  its own, so its size is known; its position is then the highest one (rule 8) that is as far left, or right, as the earlier
  floats allow (rules 2, 3, 7 and 9), not above the flow position it was met at, the top of its containing block or the line
  it sits on (rules 4 to 6). Rule 3 is measured against the other side's floats wherever they are; rule 7 applies whenever
  another float on the same side is in the way, even outside the containing block, and a float with none to its side may stick
  out of its containing block. `float: inline-start | inline-end` and the logical `clear` values are resolved to left and
  right by the computed style.
- **Width and height.** A float's `auto` width is shrink-to-fit (§10.3.5, from the intrinsic-size cache) and its auto margins
  are 0; a floated replaced box uses §10.3.6. The auto height of a block formatting context root reaches the bottom margin
  edge of its lowest float (§10.6.7); floats are also added to the root's scrollable overflow. Intrinsic sizes count floats
  (Sizing 3 §5.2): the widest float for min-content, and the sum of consecutive floats (broken by `clear`) for max-content.
- **Floats and margins.** A float is placed where a block would be, after the margins collected so far. While the top margin
  of the box that holds it is still pending (§8.3.1) the float is held until that position is settled, so that a larger
  margin collapsing through the container moves the float with it. A box that starts a formatting context is separated from
  adjoining floats: a margin that would pull them down does not.
- **Clearance (§9.5.2).** A box with `clear` gets clearance when its hypothetical position, with its own top margin and the
  margins of the first children that adjoin it collapsed into the run, is above the bottom edge of the floats it clears, or
  when a float adjoining it would otherwise be pulled down with it. The margins before the box end there; its border edge goes
  to the bottom edge of the floats whatever its own margin is. A cleared box that is still pending keeps the margin
  collapsing with its first child, and its position is raised to the floats' edge when the pending chain is settled.
- **Boxes beside floats (§9.5).** An in-flow block that is a block formatting context root, and block-level replaced boxes,
  are put beside the floats at their top edge. An `auto` width shrinks to the room between the floats (not below its
  min-content width); a definite width fits when its border box and start margin do (an over-constrained end margin is
  dropped). If the box does not fit it moves down to the next float edge, and with no float edge left it stays.
- **The exclusion space.** `IExclusionSpace`, reached through `InlineLayoutRequest.Exclusions`, is what inline layout asks:
  - `Query(bfcTop, height, bfcLeft, bfcRight, out start, out end, out nextEdge)` returns the usable inline range of a band
    (floats whose margin box overlaps the band; with height 0 the single line at `bfcTop`) and the smallest bottom edge
    among them, the next y where more room can appear (`+Infinity` when no float intersects). `start == end` means no room.
  - `PlaceFloat(request, container, floatBox, minBfcTop, out marginBox)` lays out and places a float met in inline content.
    `minBfcTop` is the top of the current line when the float fits on that line and the line's bottom when it does not; the
    inline layout decides, which is the hook for rule 6 and 7 of §9.5.1. The float's fragment is added to the container's
    fragment and the margin box comes back in block-formatting-context coordinates.
  - `PlaceholderInlineLayout` places the floats of its container at the top of the content box, then puts its one line in the
    room beside them, below the next float edge when there is none.
- **Fragments.** A float's fragment is a child of its containing block's fragment, in tree order; painting orders it
  (Appendix E step 5).
- **Bounds.** No recursion on tree depth: floats in the block flow are frames of the same explicit stack (nested floats
  need no call depth), and floats in inline content nest one engine call each, at most 32 deep (then a diagnostic and no
  fragment). A context stops searching after 50 million float visits or 262,144 floats: later floats are stacked below
  everything placed so far, lines ignore floats, and a diagnostic says so. Placement only scans the floats that can still
  matter (those ending below the lowest float top), so rows of thousands of floats cost about linear time. The margin-collapse
  look-ahead for clearance follows at most 64 first children. All storage is kept between layouts.
- **Chosen where the specs leave room.** Floats in a settled position use the margins collected so far (matching what
  browsers do); a cleared box's border edge ignores its own margin when clearance applies; the 'separated from adjoining
  floats' rule for formatting-context boxes uses the smaller of the two outcomes (pulled down or not).
- **Checked by.** `FloatLayoutTests` (spec examples by fragment border boxes, allocation and limits) and `WptFloatReftests`:
  58 CSS2 `floats` and `floats-clear` reftests whose page and reference render to the same pixels through `HtmlRender`,
  with the whole-pixel line height injected. The test with `ONERENDER_FLOAT_SWEEP` set sweeps the whole
  directories of a web-platform-tests checkout and writes one result per test.

## Inline layout (`Layout/Inline/`)

`InlineLayoutEngine` is the `IInlineLayout` behind `BlockLayoutEngine()`. It lays out one inline formatting context (CSS2 §9.4.2,
CSS Text 3, CSS Inline 3) per call, as one paragraph, and keeps one worker per nesting depth because atomic inlines
re-enter it through `IAtomicInlineHost` (at most 40 deep; deeper content is dropped). Everything is stored in arrays that grow and
are reused, so a relayout of unchanged content allocates nothing.

- **Collect.** An explicit-stack walk of the container's subtree produces the paragraph text and raw elements: text runs, inline
  box open and close, atomic inlines (U+FFFC in the text), tabs, forced breaks, floats and absolute boxes. White space is
  processed here (CSS Text 3 §4.1, every `white-space-collapse` and `text-wrap-mode` value): collapsing crosses inline box
  boundaries, segment breaks and `tab-size` follow the mode, and each character keeps a flag for "collapsible", "word separator"
  and the source offset it came from. `unicode-bidi` embeddings, isolates and overrides become the UAX #9 controls.
- **Prepare.** UAX #14 opportunities and grapheme starts come from OneFont; `AdjustOpportunities` turns them into CSS's:
  `white-space`, `word-break` (normal, break-all, keep-all, break-word), `line-break: anywhere`, soft hyphens (`hyphens: manual`
  only; `none` removes them), the `break-spaces` rule, and breaks around atomic inlines. Bidi levels come from UAX #9; a
  paragraph with a left-to-right base and no character at U+0590 or above skips the algorithm (all levels are 0). Text is split by
  level and into pieces of at most 1,024 characters (at a break opportunity or grapheme boundary) and shaped through
  `IFontProvider.Shape`, whose cache is keyed by text and style, so an unchanged paragraph hits it without allocating (a page with
  more than about 4,096 distinct runs thrashes that cache and allocates on a relayout).
- **Scan.** `ScanLine` is the greedy breaker: it accumulates clusters, remembers the last opportunity that fits, rolls back to
  it, hangs trailing collapsible and preserved spaces (CSS Text 3 §4.1.3), and breaks inside a word only when the line has no
  opportunity (`overflow-wrap: break-word | anywhere`). Min-content and max-content (`Measure`) run the same scan with an
  available width of 0 and infinity; a forced break splits max-content, atomic inlines count whole, `anywhere` counts in
  min-content and `break-word` does not.
- **Lines.** For each line the exclusion space is asked for the usable range at the line's top and height; a line whose first
  unbreakable item does not fit moves down to the next float edge. Floats met in the content are placed with
  `PlaceFloat`: at the line's top when the content before the float plus its shrink-to-fit width fits on the line, below the
  line otherwise (CSS2 §9.5.1 rule 6); the line is scanned again when a float joined it. The line's height needs the vertical
  metrics of its pieces, and the strut is always part of it (§10.8.1).
- **Vertical metrics.** Each inline box has ascent, descent and half-leading from its font and `line-height`; `vertical-align`
  shifts it (baseline, sub, super, text-top, text-bottom, middle, lengths and percentages of the line height; top and bottom
  align to the line box after the others are placed). An inline-block's baseline is the baseline of its last in-flow line, or
  its bottom margin edge when `overflow` is not `visible` or it has no line. The first baseline is stored on the container's
  fragment (`SetBaseline`); an inline-block's last baseline is found by walking its fragments.
- **Emit.** Alignment (`text-align`, `text-align-last`, `text-indent` with `hanging` and `each-line`, justification between
  words, `letter-spacing`, `word-spacing`), then bidi reordering of the line's pieces (UAX #9 L2), then fragments: a `Line`
  fragment per line; inside it one `Box` fragment per run of consecutive pieces of an inline box (so a box split across lines, or
  across a nested bidi run, has several pieces chained with `NextOfBox`, with `ContinuesBefore`/`ContinuesAfter`, and its
  border and padding only on the first and last piece, `box-decoration-break: slice`); inside those one `Text` fragment per
  font face of each piece with a glyph run (glyph ids, positions, source offsets). Atomic inlines are laid out through the host
  while preparing and moved into the right fragment with `FragmentTree.Reparent`. `TryGetBorderBox` of an inline element is the
  union of its pieces.
- **Bounds.** No recursion on tree depth (a 10,000-deep span nest is walked with the explicit stack). A paragraph holds at most
  4,194,304 characters (the rest is dropped); line scanning checks the cancellation token every 1,024 steps; a line that cannot
  place anything still consumes one cluster, so there is no endless loop. Costs on the development machine: a 1,000,000-character
  paragraph (12,171 lines) lays out in about 0.3 s including the document build, a 100,000-character unbreakable word in about
  25 ms (one line) or 30 ms with `overflow-wrap: anywhere` (4,000 lines), a 10,000-deep inline nest in about 30 ms. All are
  linear: shaping is per piece of at most 1,024 characters and a line is scanned once, plus once more for each float that
  joins it.
- **Choices where the specs leave room** (each checked against Chrome by the `inline-lab` pages, where 98.7% to 99.4% of the
  elements are within the geometry tolerance):
  - Ascent, descent and line gap are rounded to whole pixels (half away from zero) before half-leading is computed; the
    half-leading's top is the floor of half the leading and the bottom takes the rest. This put the lab from 9% to 90% within
    tolerance.
  - `sub` lowers by a fifth of the parent's font size plus 1 px and `super` raises by a third plus 1 px.
  - An inline box's open and close edges take the bidi level of the next/previous content, so the box's pieces stay contiguous.
    Known deviation: an RTL-direction box around left-to-right text puts its start edge on the wrong side.
  - `text-justify: inter-character` is treated as `inter-word`; `line-break: strict | loose | normal` are alike; letter-spacing
    does not disable ligatures; a percentage `word-spacing` is a fraction of the separator's advance.
  - An empty inline box alone on a line produces no fragment (Chrome gives a zero-size box).
  - Percentage `line-height`, and `em` lengths of `letter-spacing`, `word-spacing`, `text-indent` and `tab-size`, inherit as the
    ancestor's computed length (CSS Values 4 §7.1), not as text resolved against the descendant's font.
- **Not done:** `::first-line` and `::first-letter`, `text-overflow`, line clamping, `hyphens: auto`, `text-decoration` and text
  shadows, vertical writing modes, ruby, `box-decoration-break: clone`, inline-flex, inline-grid and inline-table as anything
  but blocks, markers inside the line, and `initial-letter`.
- **Checked by.** `InlineLayoutTests` (a section per topic, the 0 B relayout of a 1,000-box text page and the limits above);
  the `inline` fuzz target; the `inline-lab` differential pages in `testdata/inline-lab/`.

## Positioning (`BlockLayoutEngine.Positioned.cs`)

- **Containing blocks** (CSS2 §10.1, Position 3 §2). Each block frame carries `CbFrag`, the fragment of its nearest positioned
  ancestor (`position` other than `static`), or -1 for the initial containing block. Nested layouts started through
  `LayoutIndependent` inherit it from the frame that started them. A positioned inline box is a containing block too (§10.1 item 4): inline layout
  passes the fragment of the nearest positioned inline box around an absolute box as that box's containing block. When the
  inline box is split across lines this is the piece that holds the absolute box, not the union of the first and last pieces.
- **Relative** (§9.4.3). The shift is applied when a box closes, to the fragment rectangle only: the flow position that siblings
  and the parent's height see is unchanged. A shifted box adds its shifted rectangle to the parent's scrollable overflow.
  `left` wins over `right` in a left-to-right containing block, `top` over `bottom` always; percentage `top`/`bottom` against a
  height that depends on content are `auto`.
- **Absolute and fixed** (§10.3.7, §10.6.4; replaced §10.3.8, §10.6.5). While placing the in-flow children of a block, an
  absolute child is not laid out. It gets a placeholder fragment, appended as the next child of its *parent's* fragment so
  fragment order is document order (paint order needs that), and a queue entry holding its static position in the parent's
  border-box coordinates. The static top is the cursor plus the pending margin run (0 when the parent's own top margin is still
  pending). After the whole tree is done the queue is resolved in discovery order, so a box is resolved after the box that contains
  it:
  1. document-space origins of all fragments are computed in one pass (parents precede children), so a containing block's padding
     box and a parent's offset cost O(1) however deep they are;
  2. `SolveAbsoluteAxis` solves one axis (`start + margins + size + end + padding/border = containing size`) with `auto` as NaN:
     every case of §10.3.7, and §10.6.4 with the vertical differences (static top, equal margins even when negative, bottom
     ignored when over-constrained); the containing block's direction picks the ignored inset. Min/max apply as §10.4 says, by
     solving again with the violated size. An `auto` width is shrink-to-fit from the intrinsic sizes; an `auto` height is the
     content height (laid out once with the solved width forced into the box) or follows from `top`/`bottom` or `aspect-ratio`;
  3. the box is laid out through the same entry as every independent block (`LayoutIndependent`) with the solved sizes forced, into
     the placeholder, then moved to its place. Percentages of the box itself resolve against the containing block's padding box.
  Fixed boxes use the viewport (`LayoutInput`, at scroll position 0) as their containing block; painting counters the viewport
  scroll, so layout needs no scroll input. The root element can itself be `absolute`/`fixed`; it is then placed in the initial
  containing block.
- **With floats.** An absolute box inside a float (or in any nested formatting context) is queued like any other: the float lays
  its content out in its own context, and the box's static position is taken from the float's frame. Resolution runs after
  every float of the document is placed, and lays the box out in a formatting context of its own, so it is neither pushed by nor
  does it push floats. A relatively positioned float is placed as a float first and drawn shifted afterwards; the
  exclusion it leaves for lines and later floats stays where the float was placed, and its shifted rectangle adds to the scrollable
  overflow.
- **Absolute boxes inside inline content.** The inline layout reports each one with `IAtomicInlineHost.QueueAbsolute`: the static
  position is the inline position reached on the line when the box is met, at the top of the line box. A box with nothing
  but out-of-flow boxes before it on its line is at the start edge of the container's content box, however floats narrow the
  line. An inline layout that does not report (the placeholder) leaves the box at the start of the content box
  (`QueueInlineAbsolutes`).
- **Relatively positioned inline boxes** are drawn shifted by their offset; the line and the box's content move with the
  fragment and the line is unchanged.
- **Sticky** (Position 3 §3.4) takes no part in layout (it is in flow like `relative` with no offset). The shift is computed at
  paint time, see below.
- **Bounds.** The queue is capped at 4M boxes, nothing recurses, the resolution pass is linear in fragments plus queue entries, and
  the token is checked every 64 boxes. A 3,000-deep nest of positioned boxes with an absolute child at each level lays out in
  milliseconds (test).

## Overflow and scrolling

- **Overflow values** (Overflow 3 §3.1) are already computed with the visible/clip → auto/hidden rule when the other axis scrolls.
  A *scroll container* is a box with `overflow` other than `visible`/`clip` on an axis; `overflow: clip` clips like `hidden` but
  is not scrollable. `overflow-clip-margin` (Overflow 4 §3.2: `<visual-box> || <length>`, padding-box and 0 by default) outsets
  the clip edge on `clip` axes only.
- **Viewport propagation** (Overflow 3 §3.3). `ViewportOverflow.Resolve` picks the root's `overflow` or, when the root is `html`
  with `visible` and has a `body` child with something else, the body's; `visible` becomes `auto` and `clip` becomes `hidden` for the
  viewport. The element it came from neither clips, nor is a scroll container, nor establishes a block formatting context for that
  reason. Layout and the painter both ask the same function.
- **Scrollable overflow** (Overflow 3 §2.2) is recorded on every box fragment (`Fragment.ScrollableOverflow`, in the fragment's
  border-box coordinates). It starts from the border box and takes in-flow children's overflow (shifted by relative offsets). A
  box that clips an axis contributes only its border box on that axis. A scroll container's own area starts at its padding box,
  and content that reaches past the end of the content box also brings the end padding in. Absolute boxes add their overflow to
  the fragment of their containing block, not their parent, and from there to every ancestor that does not clip; fixed boxes add
  nothing. This is one pass over the fragments in reverse order after the queue is resolved.
- **Scroll positions** are host input in a `ScrollState`: one viewport offset and one offset per scroll container (keyed by box
  index, so call `Clear()` after rebuilding the box tree). They are stored as requested and clamped when used to what
  `ScrollState.GetRange` (a container) or `GetViewportRange` (the root's scrollable overflow beyond the viewport) allows:
  `0..scrollable overflow − padding box`, and for `direction: rtl` containers `overflow start − padding start .. 0`.
  `HtmlPage.Scroll` is the page's state. Layout never reads it.
- **Painting scrolls.** `PaintOptions.Scroll` turns it on. A scroll container that can scroll emits `PushClipRect` (fixed) and, inside
  it, a `PushTransform` that translates its content by the negative offset; the viewport gets one around everything but the
  canvas; fixed boxes get a counter-translation by the viewport offset; sticky boxes get a translation of their own. A box painted
  outside its containing block's scope (an absolute or relative box deferred to its stacking context) gets a copy of the scroll
  transform of each scroll container on its containing-block chain, so it scrolls with the container but not with ancestors it
  escapes. Items are only emitted when the container can scroll, which depends on layout, never on the offset. Their ids use parts 6 (scroll container), 7 (sticky), 8 (fixed counter-scroll) and 9 (copy of a scroll transform pushed for a deferred box), and `ItemId.Of(0, 6, 0)` for the viewport.
- **Changing a position never repaints.** `Painter.PatchScroll(retained)` recomputes the translation of every scroll, viewport,
  fixed and sticky item the last paint emitted and calls `PatchTransform` on those that changed; the damage is the old and new
  bounds of what moved, clipped by the container, and nothing allocates. The patched list equals a fresh paint (tested by comparing
  ids, bounds and both hashes). The sequence for a host is: `page.Scroll.SetOffset(box, x, y)`, `painter.PatchScroll(retained)`,
  draw the damage.
- **Sticky shifts** are `StickyPositioning.Offset(borderBox, margins, containingBlock, scrollport, scroll, insets)`, a pure
  function: on each axis the box is moved inward of the scrollport edge by its inset (the scrollport shifted by the scroll offset,
  in unscrolled coordinates) and then limited so its margin box stays in its containing block (the parent's content box); with
  both insets on an axis, the start inset wins. The painter records the geometry in document space when it paints, so a scroll
  change is arithmetic on a table. A sticky box inside another sticky box is shifted relative to the outer one's final position.
  The scrollport of a sticky box is its nearest scroll container's padding box, or the viewport.
- **Not done:** scrollbars (no gutter, nothing is drawn), scroll snapping, scroll events, `scroll-behavior`, `overscroll`, `contain`, and
  the overflow of the content of an atomic inline (the border box is used).
- **Overflow of inline content.** Line boxes, inline box edges, text that runs past the end of its line (an unbreakable word, a
  `nowrap` line) and atomic inlines add to the scrollable overflow of the container (`InlineLayoutResult.Overflow*`).

## Flex layout (`Layout/Flex/`, `BlockLayoutEngine.Flex.cs`)

Flexbox Level 1 §9 on the block engine's frame stack, with Box Alignment 3 for `justify-content`, `align-content`, `align-items`,
`align-self` (including `safe`/`unsafe`) and `gap`. Block-level and inline-level flex containers are both handled; the container
is a formatting-context root like a BFC root, so floats stay outside it and margins do not collapse through it.

- **Style.** `flex-direction`, `flex-wrap`, `flex-grow`, `flex-shrink`, `flex-basis`, `order`, the alignment properties and
  `row-gap`/`column-gap` are typed in `ComputedValues`. The cascade layer does not expand `flex`, `flex-flow` and `place-*`, so
  `ShorthandFallbacks` does, with the Flexbox §7 rules (`flex: 1` is `1 1 0`, `flex: auto` is `1 1 auto`, `none` is `0 0 auto`).
- **Phases per container.** Init (collect in-flow items in `order`, skipping `display: none`; absolutely positioned children are
  queued for the static position), Base (flex base size and hypothetical main size per item, §9.2), Lines (§9.3), Resolve flexible
  lengths (§9.7, with min/max violation freezing; capped at 256 rounds), Cross (§9.4 hypothetical cross sizes, line cross size,
  `align-content`, stretch), Final (§9.5 main-axis placement, §9.6 cross-axis alignment, §8.3 baseline alignment) and Close.
- **Trial layouts.** Measuring an item means laying it out at a forced border-box width and/or height (`ForceW`/`ForceH`), then
  truncating the fragments it produced (`FragmentTree.Mark`/`Truncate`). The measurement is memoized per box by the forced sizes,
  the containing block size, and the containing height only when the subtree has percentage block sizes. A trial-mode container
  skips its own final phase, so a chain of nested flex containers is measured in linear time. The exact item layout happens once.
  A budget of 1M + 64 per box trials guards hostile input; past it a diagnostic is recorded and sizes come from style only.
- **`min-width/height: auto`** (§4.5) uses the smaller of the content-size suggestion and the specified size suggestion, clamped by
  `max`; for aspect-ratio items the transferred suggestion applies. Column containers compute the content-size suggestion with a
  trial at the resolved width.
- **Intrinsic sizes** (§9.9) are in `FlexIntrinsic.cs` and feed the shared intrinsic cache: single-line containers sum item
  contributions along the main axis, multi-line take the largest for min-content, and gaps are added.
- **Absolutely positioned children** (§4.1) use the flex container's content box as the containing block and are aligned with
  `justify-content`/`align-self` when both insets on the axis are `auto`.
- **Choices where the specs leave room (to be checked against the Chrome differential once text lands):** overflow alignment
  defaults to `unsafe`; `space-around`/`space-evenly` with negative free space fall back to `safe center` and `space-between` to
  `flex-start`; `align-content: normal` behaves as `stretch`; `baseline` in a column container falls back to `flex-start`;
  `place-items`/`place-self` ignore the justify half (it has no effect on flex items); `float` and `clear` on items are ignored.
- **Bounds.** No recursion (a 50,000-deep nest of flex containers lays out), 100,000 items lay out in well under a second, the
  measurement count for a 2,000-level alternating nest is at most twice the depth, and relayout of a retained tree allocates
  nothing (the zero-byte assertion covers a page of more than a thousand flex items).
- **Not done:** `visibility: collapse` on items, `list-item` flex containers' outside marker, percentage and `min-content`
  `flex-basis` in column containers (treated as `content`), multi-line column containers size trial layouts from the container
  width rather than the line width, vertical writing modes, fragmentation of flex containers across pages. Text items take their min-content and
  max-content from the inline layout's intrinsic sizes and their baselines from its lines.

## Fragmentation (`Layout/Fragmentation/`)

CSS Fragmentation 3 over the fragment tree. The design is **lay out as one strip, then cut**: a fragmented flow is first laid out
as one tall continuous block at the fragmentainers' inline size, which the block and inline engines already do; the
`Fragmenter` then finds the break points in that strip, chooses breaks, and rewrites the fragment tree into one fragment per
fragmentainer. Breaking never feeds back into inline layout, because every fragmentainer of a flow has the same inline size.

- **Fragmentainers.** `FragmentationKind` says what is being filled. `Column` is used by multi-column layout. `Page` selects the page
  values of the break properties (`page`, `left`, `right`, `recto`, `verso` force; `avoid-page` discourages) and is what paged media
  uses with `Slice` over fragmentainer sizes that differ page by page (`Slice` takes the block size of the first fragmentainer, the second
  and so on, the last one repeating). A paged unit supplies the sizes, calls `Collect`, `Finish`, `Slice` and `Apply` (or `Plan`), and
  lays its page boxes and margin boxes around the fragmentainers; the core does not change. `left`/`right`/`recto`/`verso` are carried
  in `BreakCandidate.Bits`; inserting blank pages is the page layer's job.
- **Strip coordinates** are block offsets from the top of the unfragmented flow. A break point is a pair: where the content above ends
  (`Ye`) and where the content below resumes in the next fragmentainer (`Ys`). The distance between them is the truncated margin.
- **Break points** (§4.1), found by one explicit-stack walk of the strip (`Collect`): class A between in-flow block siblings and class B
  between the lines of a block container. Class C (between a container's content edge and its content) is used only for the empty
  room below the last child of a box, which behaves like an empty box. Floats are not break points and are never cut. Boxes that
  cannot fragment are *monolithic* (§4.1): replaced boxes, markers, scroll containers (`overflow` other than `visible`/`clip`),
  flex, grid and table containers (these three are kept whole for now), and nested multi-column containers. Their insides contribute
  no break points and a forced break inside them has no effect. An empty box with a height has no break points but may be cut
  anywhere when it is what does not fit (the "break anywhere" last resort of §4.4, applied to boxes that have nothing in them).
- **Break values** (§3). `break-before` and `break-after` on the first and last in-flow child propagate to the parent chain (§3.1.1),
  and the values at a break point combine. A forced value (`column` in columns, the page values in pages) forces the break and
  overrides avoid values; `break-inside` of every ancestor applies (rules 2 and 4); `always` and the `page-break-*` aliases are
  accepted (§3.4).
- **Choosing breaks** (§4.3, §4.4, `Slice`). For each fragmentainer: the first forced break that fits wins; if the rest of the flow fits, it is the
  last fragmentainer; otherwise the break is the latest candidate that fits (counting the border and padding that
  `box-decoration-break: clone` repeats at the break) among those no rule forbids. Candidates carry a *level*: 0 breaks no rule,
  1 breaks only orphans/widows (rule 3), 2 breaks an avoid rule (rules 1, 2, 4). The search takes the latest level-0 candidate,
  else the latest level-1 one, else the latest candidate of any level, so conflicting avoids end in the latest possible break. If
  nothing fits, the first break after the point where content stops fitting is used and the content overflows the fragmentainer
  (monolithic overflow). A break at the very top of a fragmentainer is never chosen unless forced (§4.4 progress), fragmentainers
  are at least 1px high (§4), and a break that would cut through a float is level 2. Orphans and widows: a break between lines `j` and
  `n - j` is level 1 unless `j >= orphans` and `n - j >= widows`; a block with fewer lines than either value is kept whole.
- **Margins at breaks** (§5.2). At an unforced break the margin between the two boxes is dropped (the next fragmentainer starts at the
  top of the later box); at a forced break the margin above is dropped and the later box keeps its own top margin (clamped to the
  collapsed gap). Cloned margins are always zero.
- **Splitting boxes** (§5.3, §5.4, `Apply`). A box that crosses breaks gets one fragment per fragmentainer it occupies. The first keeps
  the original fragment, so handles stay valid; the others are copies without children, chained after it with `NextOfBox`. A piece
  that continues extends to the end of its fragmentainer (the box's content box "fills the remaining extent"). With `slice` the pieces
  take the original border and padding only at the real start and end (the top and bottom edges of the other pieces are 0, so
  painting needs no special case); with `clone` every piece keeps all of them and the break leaves less room. Lines, text, floats,
  absolutely positioned boxes and monolithic boxes are not split; each goes to the fragmentainer where its top is.
  Every fragment records its `FragmentainerIndex`; pieces of a box are in index order.
- **Bounds.** No recursion (explicit stacks, 3,000-deep and 100,000-line flows are tested). `Slice` is O(fragmentainers × log candidates)
  with prefix tables, so a 100,000-line paragraph cuts in milliseconds. A flow makes at most 100,000 fragmentainers and 4,000,000
  extra box pieces; past either the rest stays whole in the last or current fragmentainer and a diagnostic is recorded. All storage is
  pooled: re-cutting an unchanged flow allocates nothing.
- **Choices where the specs leave room.** Class C points other than the end gap are not used. Floats and absolutely positioned
  boxes are never fragmented (Fragmentation 3 leaves their fragmentation to the layout mode). A box whose containing block is
  inside the strip is placed by the strip's coordinates, so an absolutely positioned box is assigned to the column its top lands
  in. Percentage heights inside a fragmented flow resolve against the container, not the column.

## Multi-column layout (`Layout/Multicol/`)

CSS Multi-column Layout 1. A container with `column-count` or `column-width` other than `auto` (`ComputedValues.IsMulticol`) is a
block container that establishes a block formatting context (§2).

- **Used count and width** (§3.4, `BlockLayoutEngine.ResolveColumns`): with only `column-width`, `N = max(1, floor((W + gap) / (width + gap)))`; with both,
  `N` is capped by `column-count`; with only `column-count`, `N = count`. The used width is `(W - (N - 1) * gap) / N`. `column-gap: normal` is
  `1em`; percentages resolve against the content width. At most 10,000 columns per row.
- **Layout.** The container's children are laid out as a normal block flow at the column width (the frame's content width is the column
  width). When the container closes, `Fragmenter.Plan` picks each row's column height and cuts the strip; after the whole layout (and the
  resolution of absolutely positioned boxes) `Apply` rewrites the container: its children become `Column` fragments, each positioned at
  `contentLeft + k * (columnWidth + gap)` (mirrored for `direction: rtl`) with its content as children relative to the column.
  A trial layout that is discarded (flex measurement) drops its plans.
- **Rows and spanners** (§6). A direct in-flow block child with `column-span: all` is laid out at the container's full content width and
  splits the flow into rows: the content before it is balanced on its own, then the spanner, then the content after. The gaps in the
  strip around a spanner (margins) are kept. A row with no content creates no columns. Spanners deeper than a direct child have no
  effect yet.
- **Height.** Every row's columns share one height. Non-final rows always balance. The final row balances (`column-fill: balance`, the
  default) or fills (`auto`): balancing finds the smallest height, in steps of 1/64 px, at which the row fits in `N` columns without
  overflow and without breaking an avoid or orphans/widows rule (`Fragmenter.Balance`: a binary search of at most 40 trial slices, repeated
  once with the rules relaxed if that fails; each trial stops as soon as it needs more than `N` columns). With a definite `height` (or a
  `max-height`) the final row is limited to what is left; if the content still does not fit, the extra columns are added in the inline
  direction after the last one (§8) and become scrollable overflow. With `column-fill: auto` and an `auto` height all the content stays in
  one column unless a forced break splits it. The container's height is its `height`, or the rows and spanners, clamped by `min-height`/`max-height`.
- **Column rules** (§4): `Painter` draws one border item (part 10 of the container's box id, sub id the column's index) in the middle of the gap
  between two adjacent columns of the same row, as high as the columns, after the container's background and border and before its
  content. Rules separate columns that exist; a spanner ends the run. `column-rule` and `columns` expand to their longhands
  (`ShorthandFallbacks`).
- **Intrinsic sizes** (no section in level 1): min-content is `N * max(content min, column-width) + (N - 1) * gap` with `N = max(1, column-count)`;
  max-content is the same with the max-content width.
- **Scrollable overflow** of the container is its border box plus the columns, so overflow columns extend it sideways; overflow of the
  content inside columns is not propagated past the container.
- **Not done:** `column-span: all` on non-children, nested multi-column containers that break across the outer columns (a nested
  container stays whole in one outer column), fragmentation of flex, grid and table containers, `column-height`, `column-wrap`,
  and vertical writing modes.

## Grid layout (`Layout/Grid/`)

CSS Grid 2 as a formatting context of the block engine. `OpenChild` calls `GridOpen` for a container whose context is grid (flex has the same hook one line above); `GridIntrinsicSizes` answers min-content and max-content widths for shrink-to-fit and for items that are grids themselves (`display: inline-grid` is an atomic inline through `LayoutIndependent`). A grid nested more than 24 grid items deep is laid out as a block with a diagnostic.

- **Style.** `GridTypes.cs` holds `GridTrackList` (head, `repeat()` body, tail, line names, `subgrid`), `GridTrackSize`, `GridLine`, `GridAreas`; `StyleComputer.Grid.cs` parses the longhands and `ShorthandFallbacks.Grid.cs` the shorthands (`grid`, `grid-template` with the ASCII-art form, `grid-area`, `grid-row`, `grid-column`, `place-*`). Property ids are appended after `ColumnGap`.
- **Contexts.** A grid and every subgrid below it form one flat list of contexts built parents first; an axis of a context either owns its tracks or points to the owner context with an offset and a span. Everything is held in per-depth pooled arrays (`GridLevel`), so a relayout of an unchanged grid allocates nothing; named lines are the exception (the name index allocates per layout).
- **Placement** (`GridPlacement.cs`, §8): line resolution with names, spans, negative lines and implicit names from areas; sparse and dense auto-placement for row and column flow over an occupancy bitmap; `order` sorts the items first. Subgrid placement is clamped to the parent span.
- **Track sizing** (`GridTrackSizer.cs`, §12): initialize, resolve intrinsic sizes in span order (items spanning flexible tracks last, and only they raise flexible tracks that have an intrinsic minimum), maximize, expand flexible tracks (find the size of an fr; indefinite space uses the items' max-content contributions), stretch auto tracks. `auto-fit` collapses unused tracks and their gutters. Columns are sized parents first; rows children first, because a subgrid's height is an input of its parent's rows. Row percentages resolve in a second pass once the container height is known.
- **Items.** Each item is measured by laying it out at its used width with `_absForce` and truncating the fragments (`FragmentTree.Mark/Truncate`), then laid out again at the final size in its area. Replaced items get their used size through `_gridReplacedForce`. Baseline-aligned items in the same first row get a shim so their baselines meet; a box without a baseline uses its border-box bottom.
- **Subgrid** (§9): subgridded items contribute their own children to the parent's tracks; the subgrid's margin, border and padding at an edge, and half the gap difference elsewhere, become extra margin on the items there. Line names from the parent are visible inside, and a subgrid with an automatic span covers as many tracks as its name list.
- **Absolutely positioned children** (§10): with grid lines the containing block is the grid area (only when the grid container is the containing block; a missing line is the padding edge); without lines the static position is the box's own alignment inside the container's content box.
- **Limits.** 10,000 tracks per axis (a 1,000,000-track `repeat()` is clamped, line numbers clamp to the same bound as §5.4 allows), 1,048,576 items per container, 50 million cell visits in placement (then the remaining items stack one per row), 100 million units of sizing work, 32 levels of shared subgrid tracks (deeper ones become independent grids). All checks run before the work they guard; nothing recurses on tree depth.
- **Known deviations.** Inline-axis `baseline` falls back to start; last-baseline container baselines are not produced; `justify-items: legacy` computes to `normal`; masonry (`grid-lanes`), fragmentation across pages and non-horizontal writing modes are not implemented.
- **Checked by.** `GridLayoutTests` (placement, auto-repeat, sizing, alignment, baselines, subgrid, abspos, 0 B relayout of a 1,000-item grid with subgrids, hostile limits), the `grid` fuzz target, `testdata/grid-lab/` against Chrome (`results/grid/summary.md`) and WPT `css-grid`.

## Paint (`Painting/Painter*.cs`)

The painter walks the fragment tree in CSS 2 Appendix E order and writes the display list (`display-list.md`). The second paint area added the pieces below; each is computed from typed values in `Style/PaintTypes.cs` and painted by one method.

- **Backgrounds** (Backgrounds 3 §3). One painted layer per `background-image` entry; the other lists cycle (§3.1). Layers paint last to first under `background-color`, which fills the clip box of the final layer. Origin, clip (`border-box`, `padding-box`, `content-box`; rounded by the box's radii) and size (`auto` with the natural ratio, `cover`, `contain`, lengths) follow §3.4 to §3.9. Repeat follows §3.5: `repeat`, `no-repeat`, `space` (the whole number of tiles that fit, spread with equal gaps; a single tile is placed at the start) and `round` (the tile is rescaled to fit a whole number, at least one). `background-position-x/-y` are the longhands (OneHtml 1.0 expands the shorthand into them). A `url()` layer that repeats is one tiled image item. A gradient layer is one rectangle per tile on snapped edges, so no seam shows. `fixed` attachment is relative to the viewport: the layer sits inside a translation that `PatchScroll` updates in place, the same entry the fixed-position patch uses. The canvas takes the root's (or the body's) background (§2.11.2). `background-clip: text` and `border-area` are not painted and record a diagnostic.
- **Gradients** (Images 3 §3). Linear, radial and conic, plain and repeating. Stops get the fixup of §3.4.1 (missing positions spread evenly, positions raised to the largest one before, transition hints expanded to a few intermediate stops). Interpolation is premultiplied sRGB, which is what the display list defines and what Chrome does for legacy colors; the `in <colorspace>` syntax is not parsed yet.
- **Borders.** Widths floor to whole device pixels (minimum one). `border-image` (§6) slices the source into nine parts, paints the corners, repeats, rounds, stretches or spaces the edges, and draws a gradient source through a transform and clip; `fill` paints the middle. Radius clipping of backgrounds is by clip box (§5.3).
- **Shadows** (§7). Box shadows paint last to first, outer before the box, inset after the background and before the border, with spread and blur.
- **Outlines** (UI 4 §4). Drawn after the content of the paint unit that owns them, outside the border edge, offset by `outline-offset`, with the border-style look. `outline-style: auto` is a 2 px ring in the outline color with a 1 px white ring inside it, a fixed deterministic look; it follows the border radius.
- **Text paint.** `text-shadow` paints each shadow as a blurred copy of the glyph run before the text, the first shadow on top. Decorations (Text Decoration 4) are drawn by the box that declares them across its inline text: underline and overline under the text, line-through over it. Thickness is the font's `post` underline thickness (minimum one device pixel) and the line's edges snap like any rectangle. The underline's center sits half the font's descent below the baseline, which is what Chrome 156 does with the packaged fonts rather than what the `post` table's underline position says: probing 10 to 48 px Liberation Sans, the rule puts the underline on the same rows as Chrome at 10 of 13 sizes and one row off at 18, 24 and 40 px (`UNDERLINE=1` mode of `tools/snap/probe.py`); other families are not probed. The strike-through comes from the `OS/2` strikeout metrics; `text-underline-offset` and `-position: under` override; `solid`, `double`, `dotted`, `dashed` and a zigzag `wavy`. Skip-ink is off. Markers: `disc`, `circle` and `square` are shapes beside the first line; `decimal`, alpha, roman and greek markers are text built from the item's index among its list siblings; `list-style-image` draws the image. `start`, `reversed` and `value` are not read, and `list-style-position: inside` markers are not painted.
- **Replaced content.** `img` paints through `object-fit`, `object-position` and `image-rendering` (Images 3 §5); the natural size comes from the host's `IImageSource`. A missing image paints nothing and records a diagnostic; alt text and the broken-image icon are not drawn.
- **Pixel snapping.** Every edge of a background, border, outline, clip and decoration rectangle is placed at `round(x · dpr) / dpr` (half up), and the far edge is snapped on its own. Evidence (Chrome for Testing 156.0.8078.47, headless, `tools/snap/probe.py`; columns shown as `.` white, `R` red, `B` blue):

  | Case | Chrome result |
  |---|---|
  | left 10.4, width 10.4 | red on columns 10 to 20 (edges 10 and 21) |
  | left 10.5, width 10 | columns 11 to 20 (10.5 rounds up to 11, 20.5 to 21) |
  | left 10.6, width 10 | columns 11 to 20 |
  | boxes at 0 and 10.5, both 10.5 wide | red 0 to 10, blue 11 to 20, no blended column |
  | boxes at 0 and 10.3, both 10.3 wide | red 0 to 9, blue 10 to 20 |
  | scale 2, left 10.25, width 10 | 20 device pixels from device column 21 (20.5 rounds up) |
  | scale 2, left 10.2, width 10 | 20 device pixels from column 20 |
  | scale 1.5, left 10.2, width 10 | 15 device pixels from column 15 |
  | top 10.5, height 10 | rows 11 to 20 |
  | border 0.4, 0.9, 1.0, 1.6 px at scale 1 | 1 px |
  | border 2.5, 2.99 px at scale 1 | 2 px |
  | border 0.4, 0.6, 1.3, 1.6 px at scale 2 | 1, 1, 2, 3 device pixels |

  Known difference: a box narrower than half a device pixel whose edges snap to the same pixel is painted one pixel wide by Chrome and not at all by us.
- **Resources.** The painter never loads anything: images come from `PaintOptions.Images`, fonts from `PaintOptions.Fonts`. `.Image` supplies `ImageStore`, which decodes through oneimage from the host's `Resources` lookup and answers all of layout (`IResourceResolver`), painting (`IImageSource`) and rasterizing (`IImageResolver`).
- **Ids.** Items of this area take ids from a second id space (`Id2`, keyed by the owner, a part number and an index) with a free-id search on collision, so ids stay stable across unchanged frames.
- **Allocation.** Repainting an unchanged page with layered backgrounds, border-image, shadows, outlines, decorations, text-shadow, markers and images, and patching fixed backgrounds on scroll, allocate 0 bytes once warm (`PaintAllocationTests`).
- **Checked by.** `PaintStyleTests` (parsing and computed values), `PaintPixelTests` (analytic pixels plus two golden scenes), `PaintAllocationTests`.
