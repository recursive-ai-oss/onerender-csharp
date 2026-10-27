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
layout exists; floats and `clear` are not handled yet (floats get no fragment). Positioned boxes are handled, see below.

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
- **Not yet:** `clear`, floats, and the block-axis `fit-content`.

### The inline seam (`IInlineLayout`)

A block container with `ChildLayout.Inline` hands its children to an `IInlineLayout`:

```csharp
InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken ct);
void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent);
```

- The request carries the container, the content-box width to fill, the content box's offset inside the container's border
  box (`ContentLeft`, `ContentTop`, where lines are added as children of `parent`), the content box's origin in the BFC
  root's content box (`BfcLeft`, `BfcTop`, where float exclusions will be looked up) and the font provider. New fields can be
  added for exclusions without changing implementations.
- The result is the total line height, the line count and the first and last baselines from the content top. A container
  with lines counts as having content for margin collapsing; the block layout settles the margin run before the call.
- `Measure` returns the content-box min-content and max-content widths.
- `PlaceholderInlineLayout` is the stand-in used until inline layout lands: a container with any non-blank text, replaced or
  atomic inline child gets one empty line box as tall as `line-height` (`normal` is 1.2 × font size) and the intrinsic
  inline sizes are 0.

## Positioning (`BlockLayoutEngine.Positioned.cs`)

- **Containing blocks** (CSS2 §10.1, Position 3 §2). Each block frame carries `CbFrag`, the fragment of its nearest positioned
  ancestor (`position` other than `static`), or -1 for the initial containing block. Nested layouts started through
  `LayoutIndependent` inherit it from the frame that started them. An inline box is never a containing block yet: a positioned
  `span` does not contain absolute boxes, the nearest block-level positioned ancestor does. That is the one approximation, and it
  sits behind `CbFrag`, so an inline layout that produces fragments for inline boxes only has to set it for them.
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
- **Absolute boxes inside inline content** have the start of the container's content box as their static position until the inline
  layout reports where the hypothetical box would be. `QueueInlineAbsolutes` is the seam: an inline layout calls the same queue
  with its own position.
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
  scrollable overflow of inline content (line and text fragments add none until inline layout lands).
