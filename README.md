# OneRender

An HTML and CSS layout and rendering engine for .NET, written from the specifications. HTML in (or a styled tree built in C#), pixels, a tagged PDF, or a retained display list out. The same engine lays out a 200-page report and a game UI that updates every frame. It has no JavaScript, no networking and no clock: the host passes the time and supplies stylesheets, images and fonts.

The name is provisional (it lives in one build property, `LibName`/`PackagePrefix` in `Directory.Build.props`), and the version is a pre-1.0 placeholder.

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `RecursiveAI.OneRender` | The engine: styled-node tree, computed values, box tree, layout, fragmentation and paged media, paint to a display list, hit testing, DOM-standard events, focus, scrolling, form controls and text editing, CSS Transitions and Animations. | OneHtml, OneFont |
| `RecursiveAI.OneRender.Image` | Display list to pixels (`HtmlRender.ToImage`, `DisplayListRasterizer`). The reference rasterizer. | OneRender, OneImage, `.Fonts` |
| `RecursiveAI.OneRender.Pdf` | Display list to tagged PDF (`HtmlPdf.ToPdf`, `DisplayListPdfWriter`). | OneRender, OnePdf (core), `.Image` |
| `RecursiveAI.OneRender.Razor` | A Razor component to a PDF or an image in one call. | OneRender, `.Image`, `.Pdf`, the ASP.NET Core shared framework |
| `RecursiveAI.OneRender.Accessibility` | Rendered page facts (boxes, paint order, text boxes, colors behind text) in OneHtml's `RenderedFacts` shape. | OneRender, OneHtml.Accessibility |
| `RecursiveAI.OneRender.Fonts` | Default fonts: Liberation Sans/Serif/Mono, Carlito, Caladea. | fonts only |
| `RecursiveAI.OneRender.Fonts.Noto.<Script>` (29), `.Fonts.Cjk`, `.Fonts.Emoji` | Opt-in script coverage. | `.Fonts` |

Every library targets `net10.0` and is NativeAOT-, trim- and WebAssembly-clean (the Razor package needs ASP.NET Core and is not part of the WebAssembly check). Family libraries come from NuGet at the versions pinned in `Directory.Packages.props`.

## Quick start

HTML to PNG (`RecursiveAI.OneRender.Image`):

```csharp
using OneRender.Image;

RgbaImage image = HtmlRender.ToImage(
    "<h1 style='font-family:serif'>Hello</h1><p>Rendered without a browser.</p>",
    new HtmlRenderOptions { Width = 600, Height = 200, DevicePixelRatio = 2 });
File.WriteAllBytes("hello.png", image.ToPng());
```

HTML to PDF (`RecursiveAI.OneRender.Pdf`). The file is tagged by default:

```csharp
using OneRender.Pdf;

byte[] pdf = HtmlPdf.ToPdf(html, new HtmlPdfOptions { Page = PdfPageSetup.A4 });
// or HtmlPdf.ToPdf(html, stream, options) to write page by page
```

`@page` rules, margin boxes, page counters, running strings and `target-counter` work in the PDF path ([docs/architecture.md](docs/architecture.md), [docs/pdf.md](docs/pdf.md)).

A Razor component to PDF or PNG (`RecursiveAI.OneRender.Razor`, details in [docs/razor.md](docs/razor.md)):

```csharp
builder.Services.AddOneRender();

app.MapGet("/invoice.pdf", async (RazorRenderer r, CancellationToken ct) =>
{
    RazorPdfResult pdf = await r.RenderPdfAsync<Invoice>(new Dictionary<string, object?> { ["Number"] = "INV-1" }, cancellationToken: ct);
    return Results.File(pdf.Bytes!, "application/pdf");
});
```

Stylesheets, images and fonts the document references are never fetched by the engine. Pass them in with `HtmlRenderOptions.Resources` / `HtmlPdfOptions.Resources` (a function from absolute address to bytes); the Razor package serves them from `wwwroot`.

## The low level

The happy paths above are built on public types you can use directly:

```csharp
using OneRender;
using OneRender.Image;
using OneRender.Painting;

var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 };
HtmlPage page = HtmlPage.Create(html, options);   // OneHtml parse + cascade -> styled tree -> boxes -> fragments
// page.Styled, page.Boxes, page.Fragments are struct-of-arrays trees with readonly struct handles.

var list = new DisplayList();
page.Paint(list);                                 // stacking contexts and paint order -> display list
RgbaImage pixels = DisplayListRasterizer.Render(list, 800, 600);
```

Without HTML, build a `StyledTree` (`AddRoot`, `AddElement`, `AddText`, `StyleDeclaration`) and give it to a `LivePage`. The display list is a documented, versioned format ([docs/display-list.md](docs/display-list.md)): items have stable ids, glyph runs carry the face, glyph ids, positions and source text, and semantic tags are bracketed begin/end items. Backends read only the list.

Layout queries (`LayoutQueries`: offsets, bounding rects, `elementFromPoint`) answer synchronously after flushing pending changes. Layout explanation and box-to-source mapping are off by default and cost nothing when off.

## Interactive pages and game UIs

`InteractivePage` wraps a page with hit testing, DOM-standard events (capture, target, bubble, `preventDefault`, one listener model), focus and `:focus-visible`, `:hover`/`:active`, scrolling with scroll snap, form controls with text editing and IME composition, and the animation clock. The host owns time:

```csharp
InteractivePage ui = InteractivePage.Create(html, options, new InteractionOptions { AnimationStartTime = 0 });
var painter = new Painter();
var retained = new RetainedDisplayList();
var paint = new PaintOptions(800, 600) { Scroll = ui.Page.Scroll };
painter.Paint(ui.Page.Fragments, retained, paint);

// every frame, with the host's clock in milliseconds:
InputOutcome outcome = ui.Advance(nowMs);                       // transitions and animations
outcome |= ui.HandlePointer(new PointerInput { Action = PointerAction.Move, X = x, Y = y, Time = nowMs });
if ((outcome & InputOutcome.Relaid) != 0)
{
    painter.Paint(ui.Page.Fragments, retained, paint);          // new layout: paint again
}
else
{
    ui.PatchAnimations(painter, retained);                      // transform and opacity patched in place, no relayout
}

for (int i = 0; i < retained.Damage.Count; i++)
{
    RectF area = retained.Damage[i];                            // redraw only this area from retained.Current
}
retained.ClearDamage();
```

`LivePage` is the same machinery over a `StyledTree` you edit: edits are logged as change sets, `Flush()` restyles and relays out only what changed and repaints into a `RetainedDisplayList`, and the result equals a full rebuild (checked by randomized edit-sequence tests and a fuzz target). Steady-state frames and the common incremental changes (hover restyle, text change, transform/opacity animation) allocate nothing; the allocation tests are in `tests/OneRender.Tests`. See [docs/animations.md](docs/animations.md), [docs/forms.md](docs/forms.md) and [docs/benchmarks.md](docs/benchmarks.md).

## Fonts

`RecursiveAI.OneRender.Fonts` embeds Liberation Sans, Serif and Mono (also answering to Arial, Helvetica, Times New Roman and Courier New), Carlito (Calibri) and Caladea (Cambria), unmodified, so output is the same on every machine. `.Image` references it, so the happy paths need no setup. Script coverage is opt-in: reference `RecursiveAI.OneRender.Fonts.Noto.<Script>` (Arabic, Hebrew, Devanagari, Thai and 25 more), `.Fonts.Cjk` (Noto Sans CJK, about 32 MB) or `.Fonts.Emoji` (Noto Color Emoji) and the package registers itself at start-up. When a glyph is missing, the engine records a diagnostic that names the package that covers it. System fonts are used only through a provider you pass in. WOFF and WOFF2 `@font-face` fonts work; on browser WebAssembly WOFF2 needs decoded bytes from the host because the platform has no Brotli. Details: [docs/text.md](docs/text.md).

## Design

- **Deterministic.** The same input, fonts and host time give the same fragments, display list, pixels and PDF bytes on every platform. No culture-sensitive APIs, ordinal comparisons, defined iteration orders.
- **No network, no clock, no processes.** Resources come from the host's resolver; time comes from the host (`Advance(nowMs)`; renders default to time 0).
- **Safe code.** No `unsafe`, no reflection, no runtime codegen. Hostile input is bounded: depth, node, page, counter, grid-track and iteration limits are checked before the work they guard, and recursion never follows tree depth. Fuzz targets for the parsers, layout, paint, incremental edits, PDF output and font data are in `fuzz/`.
- **Two layers.** Every package exposes the low level; the happy-path APIs use only it.

## Support matrix

| | |
|---|---|
| Runtime | `net10.0` |
| NativeAOT | `tests/OneRender.AotSmoke` published with warnings as errors and run on win-x64, linux-x64, linux-arm64, osx-arm64 (CI) |
| Trimming / WebAssembly | `tests/OneRender.WasmSmoke` published for `browser-wasm` with warnings as errors; the IL trim analysis is the check |
| Build and tests | win-x64, linux-x64, linux-arm64, osx-arm64 (CI) |

## How it is checked

Layout is compared with Chrome for Testing 156 on the same font files, element by element ([docs/geometry-differential.md](docs/geometry-differential.md)); for example the six document templates are 99.967% within tolerance at both 1280×800 and 390×844. Web Platform Tests reftests gate each CSS module with a floor that only rises ([docs/wpt-results.md](docs/wpt-results.md)). Tagged PDFs are validated against PDF/UA-1 with veraPDF. Measured numbers are under `results/`.

## Known limitations

- **PDF/UA-1 with complex scripts.** Tagged PDF/UA-1 output with complex-script text (full Arabic, Devanagari or Thai sentences) can fail veraPDF rule 7.21.7, because glyphs inside multi-glyph clusters have no `ToUnicode` mapping. Extracted text is still correct. Pages that mix scripts with short complex-script words pass.
- `<img>` of an SVG file is not drawn; inline `<svg>` is.
- Vertical writing modes, date/time/color pickers, file input, `datalist`, CSS footnotes, scroll-driven animations and view transitions are not implemented. The full lists are in [docs/index.md](docs/index.md) and the per-topic pages.

## Build and test

You need the .NET SDK named in `global.json` (a newer feature band also works).

```
dotnet restore OneRender.slnx
dotnet build --no-restore -c Release OneRender.slnx
dotnet test --no-build -c Release --solution OneRender.slnx
```

Build output goes to `artifacts/` at the repository root, or to the directory in the `ONERENDER_ARTIFACTS` environment variable. Smoke apps check ahead-of-time compilation and trimming:

```
dotnet publish tests/OneRender.AotSmoke -c Release -r linux-x64 -warnaserror
dotnet publish tests/OneRender.WasmSmoke -c Release -warnaserror
```

Use your own runtime identifier for the first (`win-x64`, `linux-arm64`, `osx-arm64`); NativeAOT on Linux needs `clang` and `zlib1g-dev`. Benchmarks: `bench/` ([docs/benchmarks.md](docs/benchmarks.md)). Fuzzing: `fuzz/README.md`. Packages: `dotnet pack OneRender.slnx -c Release -o <dir>`, then `python3 tools/ci/check-packages.py <dir>`.

The display list backends draw from is specified in `docs/display-list.md`. A game engine that draws the list itself (atlas keys, damage, host scopes, test-harness helpers) starts at `docs/game-hosts.md`.

## Licence

`MIT OR Apache-2.0`, copyright Recursive AI, LLC (`LICENSE-MIT`, `LICENSE-APACHE`). Font packages are `(MIT OR Apache-2.0) AND OFL-1.1`. Bundled third-party material is listed in `THIRD-PARTY-NOTICES.md`.
