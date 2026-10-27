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
  Invalid declarations raise `InvalidValue` diagnostics.
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
- A value that does not parse for its property behaves as `unset` and raises `InvalidValue`; OneHtml's cascade does
  not validate longhands, so it cannot fall back to an earlier declaration.
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
  context) and `Text` (a run of text in one line, referencing a glyph run).
- **Coordinates.** Every fragment's `BorderBox` is in **CSS px, relative to the border-box origin of its parent
  fragment**; the root's is relative to the initial containing block. `AbsoluteBorderBox` and `AbsoluteOrigin` walk up the
  chain. `Border` and `Padding` are the resolved edge widths; the content box follows from them. `ScrollableOverflow`
  is in the fragment's own coordinates and covers the box's descendants' overflow; `Baseline` is measured from the
  border-box top.
- **Glyph runs.** `AddGlyphRun` stores a OneFont `FontFace`, the font size, glyph ids, x/y positions and cluster
  indexes (into the box's text) in shared columns; a `Text` fragment holds a `GlyphRunId`.
- **Fragmentation.** A box that splits (inline pieces, later fragmentainers) has several fragments chained by
  `Fragment.NextOfBox`; `FragmentOf(box)` returns the first.
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
decides) and an `IFontProvider`, whose `GetFallbackChain(in ComputedValues)` returns a OneFont `FontFallbackChain` for a
style. An implementation clears `output`, writes a fragment for the root and for every box that generates one in tree
order, reads nothing but its arguments, and checks the token every few hundred boxes. The result is a pure function of
the box tree, the fonts and the available size. Block and inline flow, floats, positioning, flex, grid, tables and
fragmentation are separate implementations behind this one entry.

## Block layout (`BlockLayoutEngine`)

`BlockLayoutEngine` implements `ILayoutEngine` for normal-flow block-level boxes: the block formatting context of CSS2
§9.4.1, widths (§10.3.3, replaced §10.3.2/§10.3.4 with the constraint table of §10.4), heights (§10.5 percentages that
behave as `auto`, §10.6.3, min/max §10.7), `box-sizing`, `aspect-ratio` (Sizing 4 §5), margin collapsing (§8.3.1) and
intrinsic sizes (Sizing 3 §5). Flex, grid and table containers are laid out as plain block containers until their own
layout exists; floats and `clear` are described under "Floats" below; absolutely positioned boxes are not handled yet (they
get no fragment).

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
- **Not yet:** the `overflow` propagation from `body` to the viewport and the block-axis `fit-content`.

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
- `PlaceholderInlineLayout` is the stand-in used until inline layout lands: a container with any non-blank text, replaced or
  atomic inline child gets one empty line box as tall as `line-height` (`normal` is 1.2 × font size) and the intrinsic
  inline sizes are 0.

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
  58 CSS2 `floats` and `floats-clear` reftests whose page and reference paint the same pixels, a stand-in for painting that
  draws backgrounds and borders in Appendix E order. The test with `ONERENDER_FLOAT_SWEEP` set sweeps the whole
  directories of a web-platform-tests checkout and writes one result per test.
