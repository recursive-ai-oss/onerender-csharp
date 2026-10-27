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
decides) and an `IFontProvider`, the text service layout calls: `Resolve(in ComputedValues)` matches a style to a font list with
per-character fallback, `Shape(text, in ComputedValues, rtl)` returns a cached `ShapedText`, and `GetMetrics(in ComputedValues)`
returns the primary face's metrics (see `text.md`; `TextFontProvider` is the engine's implementation). An implementation clears `output`, writes a fragment for the root and for every box that generates one in tree
order, reads nothing but its arguments, and checks the token every few hundred boxes. The result is a pure function of
the box tree, the fonts and the available size. Block and inline flow, floats, positioning, flex, grid, tables and
fragmentation are separate implementations behind this one entry.
