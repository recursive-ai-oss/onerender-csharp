# Driving the engine from a game host

A game engine that draws the display list with its own GPU renderer owns three things: the loop, the drawing, and the caches behind the drawing (glyph atlases, image textures, rasterized SVG). The engine owns layout, style, input handling and the list. This page is the whole surface between them. The list format itself is in [display-list.md](display-list.md); interaction in [architecture.md](architecture.md); the cost of each frame in [benchmarks.md](benchmarks.md).

There is no GPU code in the engine, and nothing here needs a clock, a thread or a file: the host passes the time of every frame and every input, and supplies images through its own tables. The same inputs on the same fonts give the same list on every machine.

## Contents

1. [Two front ends, one list](#two-front-ends-one-list)
2. [The loop](#the-loop)
3. [Walking the list](#walking-the-list)
4. [Atlas keys](#atlas-keys)
5. [Damage](#damage)
6. [Host scopes](#host-scopes)
7. [Harness support](#harness-support)
8. [Changing text and classes after creation](#changing-text-and-classes-after-creation)
9. [Determinism and limits](#determinism-and-limits)
10. [What the list already carries](#what-the-list-already-carries)
11. [The cost of an animated layout property](#the-cost-of-an-animated-layout-property)

## Two front ends, one list

- **A C# builder** (`StyledTree`, `StyleDeclaration`) with no HTML anywhere, run by `LivePage`: edit the tree (`SetDeclaration`, `SetText`, `SetStates`, insert, remove), call `Flush()`, read `LivePage.Display`. Style values are CSS text through the same parsers as a stylesheet, so a builder tree and an HTML page compute alike.
- **HTML and CSS** (`InteractivePage`, over OneHtml's document and cascade): the same pipeline plus hit testing, DOM events, focus, forms, scrolling input and CSS transitions and animations on the host's clock.

Both end in a `RetainedDisplayList`, which the host reads.

## The loop

The builder path:

```csharp
var tree = new StyledTree();
StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("width", "1280px").Set("height", "720px"));
// ... AddElement, AddText ...
var page = new LivePage(tree, new LivePageOptions(fonts) { ViewportWidth = 1280, ViewportHeight = 720, Images = images, DevicePixelRatio = 2 });

while (running)
{
    // edits from game state: tree.SetText(node, "Gold 120"); tree.SetStates(node, ElementStates.Hover); ...
    FlushResult r = page.Flush();            // restyle, patch boxes, lay out what changed, update the list; 0 B for the usual edits
    if (r.Repainted)
    {
        Draw(page.Display.Current, page.Display.Damage);   // your renderer
        page.Display.ClearDamage();
    }
}
```

The HTML path adds input and the animation clock:

```csharp
InteractivePage page = InteractivePage.Create(html, new HtmlPageOptions(fonts) { ViewportWidth = w, ViewportHeight = h, Images = images }, new InteractionOptions { AnimationStartTime = nowMs });
var painter = new Painter();
var list = new RetainedDisplayList();
var paint = new PaintOptions(w, h) { Scroll = page.Page.Scroll, Images = images, DevicePixelRatio = dpr };
painter.Paint(page.Page.Fragments, list, paint);

each frame:
    InputOutcome o = InputOutcome.None;
    foreach (input in frameInputs) o |= page.HandlePointer(input);        // also HandleKey, HandleWheel, InsertText
    o |= page.Advance(nowMs);                                             // transitions and animations, on your clock
    if ((o & (InputOutcome.Relaid | InputOutcome.EditChanged)) != 0) page.Repaint(painter, list, paint);   // rewrites only what changed
    else if ((o & InputOutcome.Animated) != 0) page.PatchAnimations(painter, list);                        // transform and opacity only: no layout
    if ((o & InputOutcome.Scrolled) != 0) painter.PatchScroll(list);
    Draw(list.Current, list.Damage);
    list.ClearDamage();
```

Steady-state frames and the usual changes (hover restyle, a text change, one subtree relayout, a transform or opacity animation, a layout-property transition) allocate 0 B; the tests that hold this are listed in [benchmarks.md](benchmarks.md). Hover and text through the HTML front end still allocate in OneHtml's cascade; the builder path does not.

## Walking the list

A frame is the items of `RetainedDisplayList.Current` in order, back to front. A GPU backend keeps one stack and never recurses; the full sketch is in [display-list.md](display-list.md#writing-a-backend). The short version for a host with passes:

```csharp
DisplayList list = retained.Current;
if (list.RequiredMinor > DisplayList.FormatMinor) throw new NotSupportedException();   // newer than this reader
stack.Clear();
for (int i = 0; i < list.Count; i++)
{
    DisplayItem it = list[i];
    switch (it.Kind)
    {
        case ItemKind.PushHostScope:  stack.Push(currentMaterial); currentMaterial = it.HostScope; break;   // your number
        case ItemKind.PopHostScope:   currentMaterial = stack.Pop(); break;
        case ItemKind.PushClipRect: case ItemKind.PushClipRoundedRect: case ItemKind.PushClipPath: /* push clip */ break;
        case ItemKind.PopClip: /* pop clip */ break;
        case ItemKind.PushTransform: /* current = it.Matrix * current */ break;
        case ItemKind.PushGroup: /* opacity it.Opacity, blend it.Blend (including PlusLighter) */ break;
        case ItemKind.FillRect: case ItemKind.FillRoundedRect: case ItemKind.Border: case ItemKind.BoxShadow: /* ... */ break;
        case ItemKind.GlyphRun: DrawGlyphs(it.GlyphRun, currentMaterial); break;
        case ItemKind.Image: DrawImage(it, currentMaterial); break;
        case ItemKind.SvgImage: DrawSvg(it, currentMaterial); break;
        case ItemKind.BeginTag: case ItemKind.EndTag: break;      // structure only
        // PushFilter, PushMask, PushMaskLayer, BackdropFilter, PushTransform3D: effects, drawn as scopes (see below)
    }
}
```

`it.RootBounds` is the item's box in root space after transforms and clips, and is empty when nothing can show: skip those and cull against the viewport or the damage rectangle with it. Coordinates are CSS px; multiply by your device-pixel ratio. Tags are only emitted when `PaintOptions.Tags` is set, which an interactive frame does not set.

## Atlas keys

| Cache | Key | Where it comes from |
|---|---|---|
| glyph atlas | (`FaceKey`, size × DPR, glyph id) | `GlyphRunView.FaceKey`, `FontSize`, `GlyphIds`. The face key is the same in every list, frame and process for the same font file. `FaceIndex` only numbers the faces of one list; do not key on it. |
| rasterized SVG | (`SvgKey`, size × DPR) | `DisplayItem.SvgKey`: equal for equal markup and `currentColor`, whatever the rectangle, list or frame. Read `SvgMarkup` and rasterize only on a miss. |
| image texture | the handle | `DisplayItem.Image` is the `ImageHandle` the host's `IImageSource` gave the engine; the engine never sees pixels. |
| per-item GPU data | (`Id`, `ContentHash`; and `ContextHash` and `RootBounds` for anything baked with position or enclosing effects) | stable ids; hashes change when the item changes. |

Glyph outlines come from `GlyphRunView.Face` (a `OneFont.FontFace`, shared: every list refers to the same object for the same font) and glyph positions from `OriginX`, `X[]`, `Y[]`; the outline is `face.GetOutline(glyphId)` scaled by `FontSize / UnitsPerEm`, y flipped. The face key is defined in [display-list.md](display-list.md#glyph-runs), the SVG key in [display-list.md](display-list.md#effects-12), so a port can reproduce them.

## Damage

`RetainedDisplayList.Damage` collects what changed since the host last called `ClearDamage()`: at most 16 rectangles in root CSS px (`ToDevicePixels(dpr)` for the bounding box in whole device pixels). It covers every pixel that differs between the frames, and an edit of one item adds only that item's bounds. Every update path (flush, repaint, patch of a transform, opacity, scroll or animation) adds to it.

**New pixels behind an image handle.** When the host replaces the pixels of a handle and keeps their size, it says so:

```csharp
int items = page.Display.InvalidateImage(new ImageHandle(7));   // LivePage.InvalidateImage(handle) does the same
```

Every `Image` item that draws handle 7 (plain images, tiled backgrounds, the pieces of a nine-slice border image) adds its bounds to the damage; nothing is laid out or painted again, no item or hash changes, and the call allocates nothing (one pass over the items). A host that caches the texture by handle re-uploads it itself. With the list alone: `DisplayList.InvalidateImage(handle, damageRegion)`. A new size is a layout change, which goes through the styled tree.

## Host scopes

A host scope names a pass: a pair of items (`PushHostScope`, `PopHostScope`, [display list 1.3](display-list.md#host-scopes-13)) carrying one `int` the host chose. The engine gives it no meaning, and a backend that ignores it draws the content unchanged.

**Setting it.** On an element, with the custom property `--host-scope`:

```css
.glow { --host-scope: 3; }        /* any integer in the int range: material 3 */
.plain { --host-scope: none; }    /* no scope (the initial state) */
```

or from the builder, `new StyleDeclaration().SetHostScope(3)` (the same as `.Set("--host-scope", "3")`). It also works in an inline `style` attribute and under `:hover` and the other states, so a hover can switch a material. `none`, a non-integer or anything else invalid means no scope. The property is not animatable: a transition or animation never interpolates it.

**Which element.** Custom properties inherit, but a scope belongs to the element that sets it: in HTML an element gets a scope only where its `--host-scope` differs from its parent's, so setting it on a container scopes the container once and not each descendant, and a child that sets another value opens a nested scope inside it. (In the builder, a declaration applies to its own node only.) An element with a scope paints as one unit, like one with `isolation: isolate`: it is a stacking context, so nothing from outside paints between its parts, and a scope on an inline element makes it paint atomically as an opacity would.

**Where it sits.** For one element the engine emits, outermost first: the scroll or sticky transform, the element's `transform`, its opacity/blend group, its mask, `clip-path` and filter, **the host scope**, then its background and border, its overflow clip and its descendants. A host that renders each scope as a pass therefore gets the element's whole subtree, and the pass is drawn under the element's own transform, opacity and filter. Scopes nest inside each other and inside every other scope kind; the innermost applies. The reader keeps its own stack (above) or finds the enclosing scope through `ParentIndex`.

**Cost and compatibility.** A scope's number joins the context hash of everything inside it, so changing the number damages exactly what is inside. A list with no scope is still readable by a 1.2 reader (`RequiredMinor` is 3 only for lists that contain one).

## Harness support

For tests and automation that drive a UI by name.

**Finding.** `InteractivePage.FindElement(name)` returns the first element in document order whose `id` is `name`; failing that, the first form control (`button`, `fieldset`, `input`, `object`, `output`, `select`, `textarea`) whose `name` attribute is `name`; else the null node. Comparison is exact and the scan allocates nothing. For a builder tree, give nodes names with `StyledTree.SetName(node, "gold")` and find them with `StyledTree.FindByName("gold")`, which falls back, in a tree built from HTML, to the same id and form-name rules. A name belongs to one node (naming another node moves it), a removed node is not found, and `Clear()` forgets all names. Names are harness state: layout never reads them.

**Activating.** `InteractivePage.Click(node, timeMs)` is HTML's `click()`: a `click` event (a `PointerEvent` with `detail` 0 and no position) goes to the element along the normal capture, target, bubble path with the time stamp you pass, and unless a listener cancels it the element's activation runs (a checkbox or radio toggles before the event so listeners see the new state and is put back if a listener cancels; a label activates its control; a select opens). No `pointerdown`, `mousedown`, `pointerup` or `mouseup` fire, nothing is hit-tested and focus does not move, so the result does not depend on layout, scrolling or what covers the element, and a disabled control does nothing, as `click()` says. It returns an `InputOutcome` like every input. For a test that must go through hit testing, compute the center from `Layout.GetBoundingClientRect(node)` and send `HandlePointer` down and up.

```csharp
Node go = page.FindElement("start");
page.Events.AddEventListener(EventTarget.Of(go), "click", e => started = true);
page.Click(go, nowMs);
```

A builder tree has no DOM, so it has no events to dispatch: the host that drives a builder UI dispatches its own input to the nodes it finds by name.

## Changing text and classes after creation

An `InteractivePage` is made from a OneHtml document, which is a read-only snapshot. These calls change what the page shows without making a new page; the result equals a page created from the equivalent HTML, and focus, hover, the active element, scroll positions, form state, editing sessions and listeners stay as they were.

| Call | Does |
|---|---|
| `SetTextContent(element, text)` | Replaces the text of an element that holds exactly one text node (a label, a value readout). |
| `SetClassName(element, value)`, `AddClass`, `RemoveClass`, `ToggleClass(element, token, force)` | Change the `class` attribute (`className`, `classList`); tokens are ordered, de-duplicated and single-space separated after a `classList` call. |
| `GetTextContent(element)`, `GetClassName(element)` | Read the current values. A `Node` handle you hold still reads what the page was created with, because the document it points into never changes. |

Each returns an `InputOutcome` (`Relaid` when something changed, `None` for a no-op) and takes a `CancellationToken`. Handles stay valid: nothing is re-parented or renumbered.

**What it costs.**

- *Text* does not touch the cascade. The characters are kept beside the document (a rebuild reads them from there), the one styled text node is rewritten, and only the affected text is laid out and painted again. Once warm it allocates **0 B** (measured on the 1,000-node HUD page) when the new text fits the room the old text had. A stylesheet with a `:empty` selector is the one case where the cascade reads text; then the change goes through the class path below.
- *Classes* need the cascade for the edited document. OneHtml's document editor makes a new snapshot of the same shape (it re-parses the document source), and the cascade restyles from what the edit reaches. On the HUD page (69 KB of source, 1,800 nodes) one change allocates about 0.8 to 1 MB, nearly all of it the new snapshot (about 14 KB is the restyle); the page itself is updated in place with no further allocation, unless the change makes boxes come or go (`display`), or the stylesheet has `::before`, `::after`, `::marker`, `counter` or `content` rules, in which case the trees are built again from the edited document. A host that toggles a class every frame should not: toggle it on a state change, or drive per-frame changes with the builder path, `transform` and `opacity` (which patch the list at 0 B), or text.

**Limits.** `SetTextContent` refuses elements without exactly one text child, `style`, `script`, `textarea`, `title`, `option`, `template` and non-HTML elements (`InvalidOperationException`), and empty text (`ArgumentException`; a single space shows nothing). Attributes other than `class` are not editable this way, because the engine itself reads attributes such as `disabled`, `type` and `src` from the original document; they wait for a mutable document in OneHtml. After a relayout under a stationary pointer the hover target is re-evaluated, as after a scroll.

```csharp
Node label = page.FindElement("volumeValue");
page.SetTextContent(label, "7");
page.ToggleClass(page.FindElement("mute"), "active", force: muted);
```

## Determinism and limits

- No clock: time enters through `Advance(nowMs)`, input timestamps and `Click(node, timeMs)`. No threads, no file system, no network; images and fonts come from the host's tables.
- The same document, fonts, viewport, inputs and times give the same list, ids, hashes and keys on every platform. Keys (`FaceKey`, `SvgKey`) are FNV-1a over defined bytes, not runtime hash codes.
- Iteration orders are defined; `FindElement` and `FindByName` return the first match in document order.
- Bounds are checked before work: 256 open scopes, 1,048,576 items, and the other limits of [display-list.md](display-list.md#limits-and-failure).

## What the list already carries

- **Images** are `ImageHandle` values the host resolved through `IImageSource`; source and destination rectangles are in image pixels and CSS px; `Smooth` and `Pixelated` sampling. Tiled backgrounds are one item with a tile and an area.
- **Nine-slice** (`border-image`) is decomposed into ordinary `Image` items, one per slice, all on one handle with different source rectangles.
- **Additive blending** is `BlendMode.PlusLighter` on a `PushGroup` (`mix-blend-mode: plus-lighter`).
- **Glyph runs** share the `OneFont.FontFace` objects, so shaping and outlines are the host's to use directly.
- **Ids** are stable 64-bit values: repainting an unchanged box gives the same ids, which is what damage and your per-item caches use.
- **Effects** are scopes: filters, masks (with layers), backdrop filters, `clip-path`, 3D transforms with perspective.

## The cost of an animated layout property

A CSS transition on one row's `width` in the 1,000-node HUD, ticked every frame through `InteractivePage.Advance` and `Repaint` (`MotionBenchmarks.WidthTransitionTick`): each frame interpolates the width, restyles the row, lays out its subtree and updates the list in place. Measured on an AMD Ryzen 9 7950X3D (benchmarks pinned to 8 cores, BenchmarkDotNet, 3 launches of 40 iterations): **65 µs per frame and 0 B per frame**. For comparison on the same page: a hover restyle is 3 µs, a text change 46 µs, a subtree resize 405 µs, inserting a 50-node subtree 551 µs and removing it 401 µs, all 0 B. See [benchmarks.md](benchmarks.md) for the method and `results/bench/interactive/gate-e7f53db/` for the run.
