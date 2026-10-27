# The display list

The display list is what the painter produces and every backend consumes: a flat, ordered, retained list of paint items for one frame or page. This document is the format. A backend (the image rasterizer, the PDF writer, a game engine's GPU renderer) can be built from it alone; it never needs the layout trees, the style system or the source of the engine.

Types live in `RecursiveAI.OneRender`, namespace `OneRender.Painting`: `DisplayList`, `DisplayItem`, `RetainedDisplayList`, `DamageRegion`. The reference rasterizer is `OneRender.Image.DisplayListRasterizer`.

## Contents

1. [Version](#version)
2. [Space, units and conventions](#space-units-and-conventions)
3. [Structure](#structure)
4. [Ids](#ids)
5. [Items](#items)
6. [Paints and gradients](#paints-and-gradients)
7. [Borders](#borders)
8. [Box shadows](#box-shadows)
9. [Images](#images)
10. [Glyph runs](#glyph-runs)
11. [Clips, transforms, groups, tags](#clips-transforms-groups-tags)
    - [Effects (1.2)](#effects-12)
12. [Bounds](#bounds)
13. [Damage](#damage)
14. [Patching](#patching)
15. [Retained lists and allocation](#retained-lists-and-allocation)
16. [Limits and failure](#limits-and-failure)
17. [Writing a backend](#writing-a-backend)
18. [The image backend](#the-image-backend)
19. [Choices the specs leave open](#choices-the-specs-leave-open)

## Version

The format has a major and a minor number (`DisplayList.FormatMajor`, `FormatMinor`; now 1.2).

- A reader of major *N* must refuse lists of any other major.
- A minor release only **adds**: new item kinds (new numbers), new properties on existing kinds with defaults that mean "as before", new enum members. It never renumbers, repurposes or removes.
- `DisplayList.RequiredMinor` is the lowest minor a reader needs to draw this particular list. A reader of minor *M* must refuse a list whose `RequiredMinor` is above *M*, because an item it does not know cannot be ignored: masks, filters and clip-path shapes change the pixels of everything inside them. A list that uses only items of version 1.0 has `RequiredMinor` 0 whatever library version made it. Format 1.1 (below) adds two properties; a list that uses either has `RequiredMinor` 1. Format 1.2 adds the effect items listed under [Effects](#effects-12); a list that uses any has `RequiredMinor` 2.
- Kind numbers 32 to 63 are reserved for effects that need a minor bump. 1.2 uses 32 to 38 and 40 (see [Effects](#effects-12)); 39 and 41 to 63 stay reserved. They follow the same push/pop discipline as the existing pairs, and a reader that does not know them refuses the list rather than skipping them. `clip-path` needs no kind of its own: its shapes are `PushClipRect`, `PushClipRoundedRect` and `PushClipPath`.
- Everything that is not a public member (storage layout, pool indices, hash values) is not part of the format. Reading goes through `DisplayList[i]` and `DisplayItem`.

## Space, units and conventions

- **Units:** CSS pixels, as `float`. The list knows nothing about device pixels. A backend multiplies every coordinate and length by its device-pixel ratio when it draws (`RasterOptions.DevicePixelRatio` in the image backend).
- **Axes:** x grows to the right, y grows downward. The origin is the top-left of the root (the viewport or the page).
- **Root space:** the space of the list before any transform item. Transform items change the space for their content only until the matching pop.
- **Rectangles** are `RectF(X, Y, Width, Height)`; a rectangle with zero, negative or NaN width or height is empty and paints nothing. The list rejects NaN, infinities and any coordinate beyond ±1e9 with `ArgumentOutOfRangeException` before storing anything.
- **Matrices** are `System.Numerics.Matrix3x2` in row-vector form: `x' = x*M11 + y*M21 + M31`, `y' = x*M12 + y*M22 + M32`. `m1 * m2` applies `m1` first. A transform item's matrix maps its content's space to its parent's space; `transform-origin` is already folded into it by the painter.
- **Colors** are `PaintColor(R, G, B, A)`, sRGB, straight (not premultiplied) alpha, floats in 0 to 1 (values outside are clamped when stored). Other color spaces are resolved to sRGB by the painter.
- **Pixel snapping:** none in the list or in the reference rasterizer. A painter that wants crisp edges (CSS pixel-snapped backgrounds and borders) snaps the rectangles it emits.
- **Anti-aliasing:** edges of all shapes are anti-aliased; the exact filter is the backend's.

## Structure

A list is a sequence of **items** in paint order, back to front. Items of two kinds:

- **Leaf items** paint something: `FillRect`, `FillRoundedRect`, `FillPath`, `Border`, `BoxShadow`, `Image`, `GlyphRun`.
- **Push items** open a scope and a matching **pop item** closes it: clips (`PushClipRect`/`PushClipRoundedRect`/`PushClipPath` … `PopClip`), `PushTransform` … `PopTransform`, `PushGroup` … `PopGroup`, `BeginTag` … `EndTag`.

Scopes nest strictly (a stack): a pop must close the innermost open push, and its family must match (any clip push closes with `PopClip`). Nesting is limited to 256 open scopes of all families together. A finished list has none open (`Validate()` throws `DisplayListException` otherwise; `RetainedDisplayList.Commit` calls it).

For every item a reader can ask (`DisplayItem`):

| Property | Meaning |
|---|---|
| `Kind`, `Id` | what it is; the painter's id (`ItemId.None` for pops) |
| `LocalBounds` | the area the item can touch in the space it is painted in; empty for items without pixels |
| `RootBounds` | `LocalBounds` through all enclosing transforms, intersected with all enclosing clips' bounds; empty if clipped away entirely |
| `ParentIndex` | index of the innermost enclosing push, or -1 |
| `MatchIndex` | for a push the index of its pop, for a pop the index of its push |
| `ContentHash`, `ContextHash` | hashes for change detection, see [Damage](#damage) |

A backend walks the items once in order, keeping its own stacks (see [Writing a backend](#writing-a-backend)). It never needs recursion, and the list never nests deeper than 256.

## Ids

Every item except a pop has an `ItemId` (a `ulong`, zero reserved) chosen by **whoever paints the item**. The contract:

- Ids are unique within one list; adding a duplicate or zero id throws `DisplayListException`.
- Repainting an unchanged box emits the **same id**. That is what lets damage tell "unchanged" from "removed and added", and lets a backend keep a resource (a GPU texture, a PDF XObject) per id.
- Ids carry no order and no meaning to the list. `ItemId.Of(owner, part, sub)` packs `owner << 32 | part << 16 | sub` as a convention: one owner number per box or text run, one part per thing it paints (background layers, border, shadow, text fragments), one sub-index per repeat.
- A push item has an id of its own and so does a tag; patching addresses a transform or group by its id.

## Items

All fields are in CSS pixels unless stated. "Rect" is the item's primary rectangle (`DisplayItem.Rect`).

| Kind | Number | Fields | Notes |
|---|---|---|---|
| `FillRect` | 1 | rect, paint | |
| `FillRoundedRect` | 2 | rect, radii, paint | radii constrained on entry |
| `FillPath` | 3 | verbs, coordinates, fill rule, paint | |
| `Border` | 4 | rect (border box), radii (outer), four sides | see [Borders](#borders) |
| `BoxShadow` | 5 | rect, radii, color, offset x/y, blur, spread, inset | see [Box shadows](#box-shadows) |
| `Image` | 6 | image handle, source rect, destination rect, sampling | see [Images](#images) |
| `SvgImage` | 8 | rect, `currentColor`, svg markup | 1.2, see [Effects](#effects-12) |
| `GlyphRun` | 7 | face, size, origin, color, glyph ids, x, y, clusters, text, direction | see [Glyph runs](#glyph-runs) |
| `PushClipRect` | 16 | rect | |
| `PushClipRoundedRect` | 17 | rect, radii | |
| `PushClipPath` | 18 | verbs, coordinates, fill rule | |
| `PopClip` | 19 | | |
| `PushTransform` | 20 | matrix | |
| `PopTransform` | 21 | | |
| `PushGroup` | 22 | opacity, blend mode | |
| `PopGroup` | 23 | | |
| `BeginTag` | 24 | role, alt text, link target, language | |
| `EndTag` | 25 | | |
| `PushFilter` / `PopFilter` | 32 / 33 | filter chain | 1.2 |
| `PushMask` / `PopMask` | 34 / 35 | | 1.2 |
| `PushMaskLayer` / `PopMaskLayer` | 36 / 37 | mode, composite operator | 1.2, only directly inside a mask |
| `BackdropFilter` | 38 | rect, radii, filter chain | 1.2 |
| `PushTransform3D` | 40 | 4×4 matrix | 1.2; closed by `PopTransform` |

**Radii** (`CornerRadii`) are eight numbers: a horizontal and a vertical radius for each corner (top-left, top-right, bottom-right, bottom-left), an elliptical quarter at each corner. The list stores radii after CSS Backgrounds 3 §5.5: negative radii become zero, and if the radii on any side add up to more than that side, all eight are multiplied by the one factor that makes the worst side fit. A consumer therefore draws the stored radii as they are. A corner whose horizontal or vertical radius is zero is square.

**Paths** are arrays of verbs (`Move` 2 coordinates, `Line` 2, `Quad` 4 (control, end), `Cubic` 6 (control 1, control 2, end), `Close` 0) and a flat coordinate array whose length must match the verbs exactly. A figure starts with `Move`; `Close` ends it. After a `Close` the next figure starts with a new `Move`. Fill rules are `NonZero` and `EvenOdd` (SVG `fill-rule`).

## Paints and gradients

Fill items carry a `Paint`: a solid color, or a gradient created on **the same list** before the item (`LinearGradient`, `RadialGradient`, `ConicGradient`). A gradient paint is valid only for its list and only until the list is cleared. A gradient is described in the coordinates of the item that uses it, not in the unit square of a box: the painter has already resolved the gradient box, the ending shape, angles and `calc()` lengths. To tile a background (`background-repeat`), the painter emits one fill per tile or clips and offsets; the list has no tiling.

**Stops** are `(offset, color)`. The painter has applied color-stop fixup (CSS Images 3 §3.4.3: missing positions distributed, positions clamped to be non-decreasing) and expanded transition hints into stops. The list only raises a smaller offset to the previous one. Offsets may lie outside 0 to 1. Between stops colors interpolate **linearly in premultiplied sRGB** (CSS Images 3 §3.5.2). Two stops at one offset make a hard edge: a position equal to that offset takes the later stop's color. Before the first stop the first color holds and after the last the last color, unless the gradient repeats.

| Kind | Geometry | Position *t* of a point |
|---|---|---|
| Linear | start (x0, y0), end (x1, y1) | the point's projection on the line, 0 at start and 1 at end: `((p − p0)·d) / |d|²` (CSS Images 3 §3.1) |
| Radial | center (cx, cy), radii (rx, ry) | `sqrt(((x − cx)/rx)² + ((y − cy)/ry)²)`: 0 at the center, 1 on the ellipse (§3.2); a circle when the radii are equal |
| Conic | center (cx, cy), from-angle (radians) | the angle of the point clockwise from "up" (negative y), minus the from-angle, in turns and wrapped to [0, 1) (CSS Images 4 §3.3) |

**Repeating** gradients map *t* into the interval from the first to the last stop: `t' = first + frac((t − first) / (last − first)) · (last − first)`.

**Degenerate cases:** a linear gradient whose line has zero length, and a radial gradient with a zero radius, paint the last stop's color everywhere. A repeating gradient whose first and last offsets coincide paints the average of the stop colors (CSS Images 3 §3.5).

## Borders

`Border` draws the ring between the rounded **border edge** (the item's rect and radii, the outer edge) and the **padding edge**, following CSS Backgrounds 3 §4 to §5.

- The padding edge is the rect pulled in by the four side widths (top, right, bottom, left). Each inner radius is the outer radius minus the width of the adjacent side (horizontal radius minus the left or right width, vertical radius minus the top or bottom width), floored at zero (§5.2). If the widths leave no inner rectangle, the ring is the whole outer shape.
- Each side owns the part of the ring on its side of the **corner diagonals**: the line from each outer corner of the rect to the matching inner corner of the padding rectangle (§5.3). Where the widths of opposite sides exceed the box, the inner corners are brought together in proportion to the widths. Where a side's width is zero the neighbours' diagonals meet it square.
- A side is not drawn if its width is zero, its style is `None` or `Hidden`, or its color is fully transparent. The painter passes width zero for `None` and `Hidden`.
- When all four sides are `Solid` with one color, the ring is drawn as one shape (no seams along the diagonals).

Styles, per side (`BorderStyle`):

| Style | Drawing |
|---|---|
| `Solid` | the side's part of the ring in the side color |
| `Dashed` | along the middle of the ring: dashes of length 2w, gaps of w, w the side width, cut to the side's part of the ring |
| `Dotted` | round dots of diameter w with a gap of w, cut the same way |
| `DoubleLine` | two solid rings, each a third of the width, with a third between them. Widths under 3 px draw solid |
| `Groove`, `Ridge` | two half-width rings, an outer one and an inner one, one dark and one in the side color. Groove: the outer ring is dark on the top and left sides and light on the bottom and right sides, the inner one the opposite; ridge swaps them |
| `Inset` | dark on the top and left sides, the side color on the bottom and right |
| `Outset` | the side color on the top and left sides, dark on the bottom and right |

"Dark" is the side color with each RGB channel multiplied by 2/3 (alpha unchanged). The dash and dot patterns start at the top-left of the middle line and are not fitted to side lengths. These pattern, dash and darkening choices are the reference rasterizer's (see [Choices the specs leave open](#choices-the-specs-leave-open)); the geometry above is normative.

## Box shadows

`BoxShadow` follows CSS Backgrounds 3 §7.

- **Outer** (`inset` false): `rect` and `radii` are the border box. The shadow shape is the rect moved by (offset x, offset y) and grown by `spread` on every side (shrunk if negative); each non-zero radius grows by `spread` and is floored at zero, and a square corner stays square (§7.1). The shape is blurred and painted in `color`, but **not inside the border-box edge** (the border box, with its radii, is cut out). If the grown shape is empty, nothing is drawn.
- **Inset** (`inset` true): `rect` and `radii` are the **padding box**. The shadow is the area of the padding box that lies *outside* the "hole": the rect moved by the offset and **shrunk** by `spread`, with each non-zero radius reduced by `spread`. The area is blurred and painted in `color`, clipped to the padding box.
- **Blur:** the shadow is blurred with a Gaussian of standard deviation `blur / 2` (§7.2); `blur` 0 is a hard edge. A blur reaches about 3 standard deviations (1.5 × `blur`) beyond the shape.

## Images

`Image` draws part of a **host-resolved** image. The list holds an `ImageHandle` (any `int` the host chose) and never the pixels; the host maps handles to pixels or textures when it draws. `source` is in the image's own pixels; an empty source means the whole image. `destination` is the rectangle the source is scaled to fit exactly (the painter has already applied `object-fit`, `object-position`, `background-size` and repeat). `sampling` is `Smooth` (bilinear or better) or `Pixelated` (nearest neighbour; `image-rendering: pixelated` and `crisp-edges`).

A backend must not sample outside the source rectangle: edges repeat the border texels (clamp), so scaled images neither fade at their edges nor bleed their neighbours.

**Tiled images (1.1).** An `Image` item with the tiled flag (`IsTiledImage`, made by `DrawImageTiled`) repeats the destination rectangle (the *tile*) across `ImageTileArea`: tile *(i, j)* is the destination moved by `(i · (width + ImageTileGapX), j · (height + ImageTileGapY))` for every integer *i*, *j*, including negative ones, and only the part inside the area is drawn. A gap is what `background-repeat: space` leaves between tiles; `round` and `repeat` use a gap of 0. The painter emits one tiled item per `url()` background layer that repeats; gradient layers repeat as one filled rectangle per tile (at most 4,096 per layer, with a diagnostic beyond), each on the pixel-snapped edges of its tile. `Sampling` is bit 0 of the flags (bit 1 is the tiled flag). A reader of 1.0 refuses the list (`RequiredMinor` 1).

## Glyph runs

A `GlyphRun` is one run of glyphs of one face, size and color.

- `Face`: a `OneFont.FontFace`. The list keeps the reference until it is cleared; `FaceIndex` numbers the distinct faces of the list.
- `FontSize` (CSS px) and `Origin` (x, y of the run's baseline origin).
- Glyph *i* has id `GlyphIds[i]` in the face, and its **pen position** is `(OriginX + X[i], OriginY + Y[i])`; `Y` is the offset from the baseline, positive downward. Glyphs are in **visual order**, left to right on screen, whatever the text direction.
- **Outline:** the face's outline for the glyph id, in font units with y up, scaled by `FontSize / face.UnitsPerEm` and flipped to y down: a font point `(fx, fy)` maps to `(pen.x + fx·s, pen.y − fy·s)` with `s = FontSize / UnitsPerEm`. Fill with the non-zero rule in `Color`.
- `Text`: the source text of the run in **logical order** (UTF-16), and `Clusters[i]`: the index into `Text` of the first code unit of the cluster that produced glyph *i*. Glyphs of one cluster share a value; for a left-to-right run the values do not decrease along the glyphs, for a right-to-left run (`RightToLeft`) they do not increase. A cluster runs from its index to the next larger distinct index (or to the end). A PDF writer builds `/ToUnicode` from (glyph id → text of its cluster) and emits `/ActualText` when one cluster has several glyphs or a glyph stands for several characters (ligatures); a text extractor reads the clusters back in logical order.
- `Blur` (1.1, default 0, CSS px): the standard deviation of a Gaussian blur is `Blur / 2`, as for box shadows. A run with a blur is a `text-shadow`: the glyphs are drawn into a mask, blurred, and filled with `Color`. Bounds grow by `1.5 × Blur`. A list with a blurred run has `RequiredMinor` 1; a run without blur is the same item as in 1.0.
- Glyph-level details such as hinting are the backend's business; the list never contains hinted outlines.

## Clips, transforms, groups, tags

- **Clips.** Content between a clip push and its pop is clipped to the rectangle, rounded rectangle or path (in the space current at the push) intersected with every enclosing clip. Edges are anti-aliased. `PushClipPath` takes a fill rule. A clip with empty shape clips everything.
- **Transforms.** `PushTransform` multiplies the current transform by the matrix for everything up to the matching pop; clips pushed inside move with it. `PushTransform3D` on the builder takes a 4×4 matrix (row vectors, as `System.Numerics.Matrix4x4`). Without perspective (`M14` and `M24` zero) the plane maps affinely and the builder stores an ordinary `PushTransform` of the 2D part `(M11, M12, M21, M22, M41, M42)` divided by `M44`, which must then be positive. With perspective the whole matrix is stored (kind 40) and the content, drawn flat in its own plane, is projected: `w = x·M14 + y·M24 + M44`, `x' = (x·M11 + y·M21 + M41) / w`, `y' = (x·M12 + y·M22 + M42) / w`. `M44` is the `w` of the page origin and can be negative; what matters is `w` at the points of the content, and a point with `w ≤ 0` is behind the viewer and is not drawn. Inside a projection a further perspective is not nested: it is flattened to its affine part. A matrix that cannot be inverted flattens its content away; nothing inside it is drawn.
- **Groups.** `PushGroup(opacity, blend)` is an isolated group (Compositing and Blending 1 §3 to §4): its content is painted into a transparent layer, and when the group ends the layer is composited onto what is behind it using `blend` (§10, plus `PlusLighter`) with the group's opacity multiplied into the layer's alpha. Opacity is 0 to 1. Blend modes inside the group blend with the group's own content, not the backdrop. Use it for `opacity` below 1, `mix-blend-mode` and `isolation: isolate` (opacity 1, normal). Clips enclosing the group apply to the composited layer once. Groups nest, and opacities multiply.
- **Tags.** `BeginTag(role, alt, link, language)` / `EndTag` carry structure and no pixels. Everything painted inside belongs to the element. `TagRole` names follow PDF structure types (`Document`, `Part`, `Section`, `Div`, `Paragraph`, `Span`, `Heading1`–`6`, `List`, `ListItem`, `ListLabel`, `ListBody`, `Table`, `TableRow`, `TableHeaderCell`, `TableDataCell`, `TableHead`, `TableBody`, `TableFoot`, `Figure`, `Caption`, `Link`, `BlockQuote`, `Code`, `Form`) plus `Artifact` for decoration that is not part of the structure. `alt` is the alternative text (for `Figure` and for anything whose accessible name differs from its text), `link` the destination of a `Link` (an absolute URL or a `#fragment`), `language` a BCP 47 tag when it differs from the parent. A backend that has no use for structure ignores tags; they never change drawing. The painter emits tags only when `PaintOptions.Tags` is set. A tag item's id is `ItemId.Of(box, 0x7F00 + virtual part, occurrence)`, and `Id >> 16` identifies the element for backends (two scopes of one element, such as a box split across pages, share it). Popping any other scope first closes the tags opened inside it, so tags always nest with the other scopes. Empty table cells carry a transparent `FillRect` (part `0x7F10`) so that the cell exists in document order. `docs/pdf.md` has the HTML mapping.

### Effects (1.2)

Every effect scope is a push and a pop on the shared stack; the 256-level nesting limit applies. A backend that draws a list refuses `RequiredMinor` above what it knows.

- **Filters** (`PushFilter`, Filter Effects 1 §5). Content up to `PopFilter` is painted into a layer, the chain is applied in order, and the result is composited like any content (enclosing clips apply to the result). One to 32 operations, each `FilterOp(Kind, A, B, C, Color)`: `Blur` (A = standard deviation in CSS px, at most 4096), `Brightness`, `Contrast`, `Grayscale`, `Invert`, `Opacity`, `Saturate`, `Sepia` (A = amount), `HueRotate` (A = radians), `DropShadow` (A, B = offset, C = standard deviation, color). The item's bounds grow by the chain's outset (three standard deviations of each blur and shadow plus offsets), so damage covers blurred ink.
- **Backdrop filter** (`BackdropFilter`, Filter Effects 2 §2). A leaf: the pixels of the current layer inside the rounded rectangle are replaced by the filtered pixels. The painter emits it before the element's own background.
- **Masks** (`PushMask`, Masking 1 §6). Directly inside, zero to 16 `PushMaskLayer` scopes describe the mask bottom layer first, each painted into a transparent layer whose alpha (`Alpha`) or luminance times alpha (`Luminance`) is its coverage, combined with the layers before it by `Add` (source over), `Subtract` (source out), `Intersect` (source in) or `Exclude` (xor). The items after the layers, up to `PopMask`, are the masked content, multiplied by the combined coverage. A mask with no layers hides its content.
- **Inline SVG** (`SvgImage`). The serialized `<svg>` element (at most 1,048,576 characters) is drawn as an image exactly the size of the rectangle: the backend replaces the root's `width` and `height` with the rectangle's size and keeps `viewBox` and `preserveAspectRatio`; `currentColor` is the item's color. The painter draws the element's own box; nothing in the markup refers to the page.
- **Projection** (`PushTransform3D`) is described under [Clips, transforms, groups, tags](#clips-transforms-groups-tags). `PatchTransform3D(id, matrix)` replaces the matrix in place (0 B) like `PatchTransform`.
- **Scope order** the painter uses for one element: transform, then the opacity/blend group, then the mask, then the clip-path, then the filter, then the element (Filter Effects 1 §3, Masking 1 §5: filter, clip, mask, opacity).
- **PDF.** `DisplayItem.PdfNative` says whether a PDF writer can express an item: opacity and blend groups, masks (soft masks) and an opacity-only filter chain are native; other filters, backdrop filters, projections and SVG images are drawn as rasterized islands with a diagnostic.
- **Limits.** 32 filter operations, blur 4096 px, 16 mask layers, 1,048,576 SVG characters, 256 scopes; each is checked before the item is added. The painter stops opening scopes before the nesting limit and records `LimitExceeded`.

## Bounds

- `LocalBounds` is computed when an item is added: for rects, the rect; for paths, the bounding box of all control points; for glyph runs, the glyph pen positions and advances widened by the face's ascent and descent (at least 1 em above and ½ em below the baseline, since accents and descenders can exceed the nominal metrics) and by ¼ em on both sides; for an outer shadow, the shadow rect plus `max(spread, 0) + 1.5 × blur` on every side; for an inset shadow, the padding box. Bounds are **conservative**: they may be larger than the ink, never smaller.
- `RootBounds` maps `LocalBounds` through the enclosing transforms (the bounding box of the four mapped corners) and intersects it with the bounds of the enclosing clips (the bounding box of each clip's shape, mapped the same way). It is empty when nothing of the item can show.
- A backend may use `RootBounds` to cull items against its viewport or a damaged region.

## Damage

`DamageRegion` holds the changed area in root space (CSS px). It keeps up to 16 rectangles; a rectangle that overlaps or touches an existing one is merged with it into the bounding box of both, and when the 17th disjoint rectangle arrives all collapse into one. `Bounds` is the bounding rectangle of everything; `ToDevicePixels(dpr)` rounds that outward to whole device pixels (`RectI`).

`DisplayList.ComputeDamage(previous, next, damage)` adds to the region the area where `next` differs from `previous`:

- Items are matched by **id**.
- A matched item is **unchanged** if its kind, `ContentHash`, `ContextHash` and `RootBounds` are all equal. Otherwise both its old and its new `RootBounds` are added.
- An item only in `next` adds its `RootBounds`; an item only in `previous` adds its old `RootBounds`.
- Reordering: if kept items change their relative paint order, every kept item that now comes earlier than one that used to come before it counts as changed, since what it overlaps changed.
- `ContextHash` mixes the kinds and parameters of all enclosing clips, transforms and groups, so moving, clipping or fading a group changes every item inside it. Tags are not part of the context: changing a tag, or adding one around unchanged content, causes no damage.
- Push and pop items have empty bounds and add nothing themselves; their effect arrives through the items inside.

The result is **conservative and exact in the sense that matters**: every pixel that differs between a rasterization of `previous` and of `next` lies inside the region (the tests rasterize random edit sequences and check this), and an edit of one item changes only that item's bounds.

`RetainedDisplayList` keeps two lists and the accumulated region. `Begin()` returns the cleared back list; the painter fills it and calls `Commit()`, which validates, diffs it against the current list, adds the difference to `Damage`, and swaps. The first commit damages everything. The viewer redraws `Damage` and calls `ClearDamage()`.

## Patching

Transform and opacity changes of a subtree that already exists do not need a repaint:

- `PatchTransform(id, matrix, damage)` replaces the matrix of the `PushTransform` with that id.
- `PatchOpacity(id, opacity, damage)` replaces the opacity of the `PushGroup` with that id.

Both update the list **in place**: the item's payload, the context and the `RootBounds` of everything inside it. The damage added is the old and new bounds of every item inside (for opacity the bounds do not move, so just those). The patched list is identical, in damage terms and bounds, to a list built from scratch with the new value (tested on random scenes). They throw `ArgumentException` if the id is missing, names another kind, or the item is not yet closed. `RetainedDisplayList.PatchTransform/PatchOpacity` patch the current list and add to `Damage`. Patching does not allocate.

## Retained lists and allocation

A `DisplayList` pools all of its storage: items, numbers, paths, gradient stops, glyph arrays, source text, strings, the id table and the push stack. `Clear()` resets the counts and keeps the arrays. Refilling a list of the same shape, committing it into a `RetainedDisplayList` (diff included) and patching all allocate **0 B** once the lists have grown to the frame's size; the tests assert this on a 1,000-item list. What allocates: growth to a larger size than ever before, and anything the painter itself allocates (paths in `PathBuffer`s should be reused; strings passed to `BeginTag` should be existing strings).

## Limits and failure

| Limit | Value | Failure |
|---|---|---|
| items per list | 1,048,576 | `DisplayListLimitException` |
| open scopes (nesting) | 256 | `DisplayListLimitException` |
| verbs per path | 1,048,576; 8,388,608 per list | `DisplayListLimitException` |
| glyphs per list | 4,194,304 | `DisplayListLimitException` |
| source text per list | 8,388,608 UTF-16 units | `DisplayListLimitException` |
| stops per gradient | 1,024 | `DisplayListLimitException` |
| gradients per list | 262,144 | `DisplayListLimitException` |
| distinct faces per list | 4,096 | `DisplayListLimitException` |
| strings per list | 1,048,576 | `DisplayListLimitException` |
| damage rectangles | 16 | merged into one |
| coordinates | finite, within ±1e9 | `ArgumentOutOfRangeException` |

All checks run **before** the add changes any state, so a refused add leaves the list as it was. Unbalanced pops, a pop of the wrong family, duplicate or zero ids and unclosed scopes throw `DisplayListException`. Nothing in the list recurses on nesting depth; every traversal uses the item indices and the explicit stacks.

## Writing a backend

Read the items in order. Keep a stack with one entry per open push. A sketch:

```
state = { transform = scale(dpr), clip = none, target = surface }
stack = []
for i in 0 .. list.Count - 1:
    item = list[i]
    switch item.Kind:
        leaf kinds:
            if item.RootBounds is empty or outside your viewport: continue    # optional culling
            draw item with state.transform, state.clip, state.target
        PushClip*:   stack.push(state); state.clip = state.clip ∩ shape (in state.transform)
        PushTransform: stack.push(state); state.transform = item.Matrix * state.transform
        PushGroup:   stack.push(state); allocate a transparent layer; state.target = layer; state.clip = none
        BeginTag:    stack.push(state)           # structure backends open an element here
        Pop*:        group: composite layer into the saved target with opacity, blend and the saved clip;
                     then state = stack.pop()
        EndTag:      state = stack.pop()
```

Notes for a GPU adapter:

- **Caching.** Key resources by `item.Id`; they stay valid while `item.ContentHash` is unchanged (and, for anything baked with position or enclosing effects, `item.ContextHash` and `item.RootBounds`). Glyph runs: cache glyph outlines or atlas entries by (`FaceIndex`, glyph id, size).
- **Redraw.** Redraw only the `DamageRegion` of a `RetainedDisplayList`, with scissor and the culling above.
- **Changing only a transform or opacity** (an animation) arrives as a patch with damage and needs no rebuilt resources: the `ContentHash` of the items inside does not change.
- **Groups** may be realized as render targets, or as per-draw alpha when the group holds a single leaf item and the blend is `Normal`.
- A backend that cannot draw something (a blend mode, an effect of a later minor version) should say so and refuse the list rather than approximate silently.

## The image backend

`RecursiveAI.OneRender.Image` rasterizes a list through oneimage:

```csharp
RgbaImage img = DisplayListRasterizer.Render(list, widthPx, heightPx, new RasterOptions { DevicePixelRatio = 2f, Images = resolver });
DisplayListRasterizer.RenderRegion(list, surface, damage.ToDevicePixels(2f), options);   // redraw only a region
```

- The result is RGBA, straight alpha, rows top to bottom, on a surface cleared to `RasterOptions.Background` (default opaque white). Width and height are in device pixels, 1 to 16,384 each and at most 64 Mpixel.
- `RenderRegion` clears the region to the background and redraws every item that touches it, clipped to the region; its result equals the same region of a full render (tested).
- **Blending** is on encoded sRGB values. **Determinism:** the same list, options and resolver give the same bytes on every run (tested); the golden images tolerate 2 levels per channel for floating point differences between CPUs.
- **Groups** are layers of the full surface size: at most 16 open at once and 128 Mpixel of live layers, else `RasterizationException`. **Blur** deviations are limited to 256 device pixels.
- Items that need path booleans (non-rectangular or rotated clips, borders other than four equal solid sides, shadows) accept device coordinates within ±33,554,432 pixels (a oneimage limit); beyond that `Render` throws `RasterizationException`. Fills, glyph runs and images are not limited that way.
- `Render` and `RenderRegion` throw only `ArgumentNullException`, `ArgumentOutOfRangeException`, `DisplayListException` (unclosed list), `RasterizationException` and `OperationCanceledException`.

What the backend builds around oneimage, because oneimage has no direct equivalent:

| Need | oneimage has | Built here |
|---|---|---|
| conic gradients | linear, radial, elliptic | a `Brush` computing all three CSS gradients in double precision with premultiplied interpolation |
| elliptical corner radii | circular `RoundedRectangle` | cubic Bézier quarter ellipses |
| border sides, styles | strokes, fills, path booleans | ring paths clipped by corner diagonals; dashes and dots as stroked middle lines |
| box shadows | `GaussianBlur` on images | an 8-bit coverage mask, blurred and applied through a mask brush so colors never fringe |
| image sampling with clamped edges | `ImageBrush` fades the edges when not tiling | an own brush with bilinear and nearest sampling |
| nested non-rectangular clips | one clip path per fill | path intersection on push; rectangles are intersected as rectangles |
| groups with opacity and blend | `Fill` with blend and opacity | a layer canvas composited through an image brush |
| `plus-lighter` | `AlphaComposition.Plus` | used for the composite |

## Choices the specs leave open

- **Pixel snapping (painter, checked against Chrome 156 output):** each edge of a background, border, outline, clip and decoration rectangle is placed at `round(x · dpr) / dpr`, half rounding up, and a rectangle's far edge is snapped on its own, so a 10.4 px box at 10.4 px covers device pixels 10 to 20 inclusive and two boxes that meet on a fractional edge share one pixel edge with no blended column. Border widths are floored to whole device pixels with a minimum of one (1.6 px paints 1 px at scale 1, 3 device pixels at scale 2 for 1.6 px; 0.4 px paints 1). Glyph positions are not snapped. Evidence: `tools/snap/probe.py`, results in the Paint section of `architecture.md`.

These are choices of the reference rasterizer where CSS does not decide. None has been compared with Chrome's output yet; each is a proposal to revisit with differential evidence.

- Dash length 2w and gap w for `dashed`; round dots with a gap of w for `dotted`; the pattern starts at the top-left of the middle line.
- `double` borders thinner than 3 px draw solid.
- "Dark" for groove, ridge, inset and outset is 2/3 of each channel; black is not treated specially.
- A square corner stays square when a shadow spread grows or shrinks it.
- `transform-style: preserve-3d` is flattened: each element is projected on its own plane and nested 3D contexts do not intersect.
- Backdrop filters are painted before the element's own group, so the element's `opacity` does not fade them.
- Filter blur and color operations work on premultiplied sRGB floats; luminance masks use the sRGB coefficients 0.2126, 0.7152 and 0.0722.
- A clip shape beyond ±33,554,432 device pixels leaves the previous clip in place.
- A bilinear image sample clamps at the edges of the source rectangle; scaling down by more than 2× is not filtered further (no mip-mapping), so a large reduction aliases.
- Group, filter and mask layers cover only the device region their content can reach (bounds plus filter outset, clipped to the surface and the enclosing clip); the sum over open layers is capped at 134,217,728 (2^27) pixels and the nesting at 32 layers, beyond which `Render` throws `RasterizationException`.
