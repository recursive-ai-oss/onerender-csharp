# PDF output (`RecursiveAI.OneRender.Pdf`)

The backend turns the retained display list (`display-list.md`, format 1.2) into a PDF with OnePdf core's writer. It never reaches back into layout: everything it knows is in the list. Fonts, images and the structure tree come from the list items and their ids.

## API

| Layer | Entry point | Use |
|---|---|---|
| Low level | `DisplayListPdfWriter(Stream, PdfOutputOptions?, IImageResolver?)`, `AddPage(list, PdfPageSetup[, RectF region])`, `Finish()`, `Diagnostics`, `PageCount` | one list, any number of pages, each page a region of the list in CSS px |
| Happy path | `HtmlPdf.ToPdf(html, Stream, HtmlPdfOptions?, ct)` returns `HtmlPdfResult(PageCount, Diagnostics)`; `HtmlPdf.ToPdf(html, options)` returns the bytes | HTML to PDF in one call; the happy path uses only the public low-level pieces (`HtmlPage`, `PageFragmentation`, `Painter`, `DisplayListPdfWriter`) |

`PdfPageSetup` is in CSS px (96 per inch; `A4` and `Letter` have 48 px margins); each edge is at most 14,400 px. `PdfOutputOptions` carries title, author, language, creation date (none by default, so output is deterministic), `Tagged` (default true), `Profile`, `Compress`, `DocumentId`, `RasterDpi` (default 192), `MaxPages` (default 100,000).

With `Profile = None` the file streams: bytes are written as each page is committed and memory does not grow with the page count (the display list and layout of the whole document are of course in memory). With `PdfUA1`, `PdfA2a` or both, OnePdf can only write the XMP metadata when it re-saves a finished document, so the file is built in memory first, then re-saved with the identification; the write fails with `PdfRenderException` listing the rules it breaks if the file is not conforming.

## Pages

`HtmlPdf.ToPdf` builds a `PagedDocument` (see the paged media section of `architecture.md`), so print style sheets apply: `@page` size and margins (the page setup in the options is only the default), margin boxes, named pages, forced breaks with blank pages, running strings, `counter(page)`/`counter(pages)`, tables of contents with `target-counter()` and `leader()`, and repeated `thead`/`tfoot`. Break opportunities are those of CSS Fragmentation 3 §4.1: between block siblings, between line boxes, and between the rows of a table; a row's cells are never split, so a row taller than a page overflows it. Page *i* of the tree occupies the region at x = i · stride, so the pages sit side by side in one display list and each page is written from its own region, together with its page layer.

The page layer (page background and margin boxes) is drawn first, as artifacts, so the running headers and page numbers are not in the structure tree and do not split the structure elements of the content. A repeated table header is also an artifact inside its table. In-document links (`href="#id"`) are link annotations to named destinations the writer adds at the position of each target; `target-counter()` and `target-text()` make no link by themselves. A document that would need more than `MaxPages` pages makes `ToPdf` throw `PdfRenderException`.

For a long document, `ToPdf` collects the heap between building, painting and writing (at 40 pages or more) and does not keep the cascade alive after the trees are built; the 10,000-row table (213 pages) peaks at about 340 MB instead of 440 MB. Layout and the display list of the whole document are still in memory; paging them out would need a layout that can be resumed page by page.

## Vector or island

Everything PDF can express is written as vector content:

| Display list | PDF |
|---|---|
| rectangles, rounded rectangles, paths | fills (even-odd and non-zero), curves |
| borders: every style, radii, per-side colors | side quads and rings clipped to the corner diagonals, dashes as stroked lines |
| box shadows without blur | filled shapes (inset via clip); blurred shadows are islands |
| linear and radial gradients (and repeating ones) | axial and radial shadings; stops with alpha add a luminosity soft mask |
| conic gradients | 180 wedge fills (a documented approximation; PDF has no conic shading) |
| images | Flate-compressed RGB with a separate soft-mask for alpha; tiled and cropped placements |
| glyph runs | text with the subsetted font, advances from the run's positions, vertical offsets from the run's y |
| clips, transforms | `W n`, `cm`; 3D transforms are flattened to their affine part, with a diagnostic |
| opacity, blend groups | `ca`/`CA`, `/BM`, or a transparency-group form when the group has more than one leaf |
| opacity-only filters | as opacity |
| masks whose layers are all `add` and of one mode | soft masks |

Islands: anything with `PdfNative == false` or that PDF cannot express (filters other than opacity, backdrop filters, inline SVG, blurred shadows and text shadows, masks with other composites) is rasterized by the reference rasterizer (`.Image`) at `RasterDpi`, at most 16 million pixels per island and 400 million per document, and placed as an image. Each island adds a diagnostic that names the item and the resolution. An island over the cap is drawn at reduced resolution (per island) or skipped with a diagnostic (per document).

Known gaps, each with a diagnostic where it changes the picture: blend modes `hue`, `saturation`, `color` and `luminosity` have no operator in the writer and draw as normal; graphics-state nesting beyond 28 levels is flattened; pixelated image sampling is not requested (PDF viewers interpolate).

## Text

A glyph run becomes real text. The writer embeds the face as a subset (OnePdf does the subsetting at output; the engine rebuilds an sfnt from the face's tables because it keeps no file bytes, and the shipped font files are never altered). `/ToUnicode` maps each used glyph to the source text of its cluster, and the marked-content `/ActualText` carries the run's source text, so extraction returns the source, including ligatures and bidi runs. When a profile is requested, a character the face lacks is not shown as `.notdef` (ISO 14289-1 7.21.8, ISO 19005-2 6.2.11.8): the glyph slot keeps its advance and draws the font's space, and the text stays in the structure. Without a profile the `.notdef` box is drawn like on screen.

## Tags

Tagging is on by default (`Tagged`). The painter adds `BeginTag`/`EndTag` items when `PaintOptions.Tags` is set; the happy path sets it from `Tagged`. The mapping follows HTML-AAM / the PDF 2.0 HTML mapping:

| HTML | Structure type |
|---|---|
| `h1`–`h6` | `H1`–`H6` |
| `p`, `pre`, `dt`, `dd` | `P` |
| `blockquote` | `BlockQuote` |
| `ul`, `ol`, `menu` | `L`; `li` → `LI` with the marker as `Lbl` and the content in a virtual `LBody` |
| `table` | `Table` only if it has a `th` (so layout tables stay untagged); `thead`/`tbody`/`tfoot`, `tr`, `th` (with `Scope` Column or Row), `td` |
| `caption`, `figcaption` | `Caption` |
| `a[href]` | `Link` with a link annotation (absolute URLs; the `aria-label` is the alternative description) |
| `code`, `kbd`, `samp` | `Code` |
| `img`, inline `svg` | `Figure` with `Alt`; an image with no or empty `alt`, or `role=presentation`, is an artifact |
| `section`, `article` | `Sect` |
| `div`, `main`, `nav`, `aside`, `header`, `footer`, `figure`, `form`, `fieldset`, `address`, `dl` | `Div` |

Everything painted outside a tagged element (page backgrounds, borders of untagged boxes, decoration) is marked as an artifact. The document root is `Document`; the document language and the title come from the options, or the `lang` attribute and `<title>` in the happy path; `DisplayDocumentTitle` is set when there is a title. Empty `td`/`th` get an invisible anchor fill so the cell exists in the structure (otherwise the table is irregular, Matterhorn 15-003). Nesting is capped: the painter emits at most 14 tags in a chain, and the structure writer folds anything deeper than 96 into its parent. Elements inside a transparency-group form XObject cannot be tagged separately; their content belongs to the enclosing element.

Identity: a tag item's element is `Id >> 16` (the box, the part, the occurrence in the low 16 bits). A pop of any other scope closes the tags open above it, so scopes always nest.

## Limits

`PdfPageSetup` edges are 1 to 14,400 px and the margins must leave content (`ArgumentOutOfRangeException`); `MaxPages` fails with `PdfRenderException`; island pixel caps as above; tag nesting as above; clip and transform nesting 21 `q` levels per page and 6 transparency-group forms deep, beyond which the inner ones are flattened with a diagnostic. Only `PdfRenderException`, `ArgumentException` (and its subclasses), `DisplayListException` and `OperationCanceledException` leave the public methods. The `pdf` fuzz target (`fuzz/`) feeds hostile display lists, page sizes and profiles through the writer and checks the file reopens.

## Checks

- `tools/pdf/fetch-verapdf.sh` fetches veraPDF 1.30.3 (from software.verapdf.org, SHA-256 pinned) and a user-local Eclipse Temurin 21 JRE; veraPDF is GPLv3+/MPLv2+ and is only run as a separate program, never linked, committed or shipped. `tools/pdf/run-verapdf.sh` writes the six templates and the paged sample (`testdata/templates/paged`: contents with leaders and links, running header, page X of Y, a 90-row table with a repeated header) and validates each against PDF/UA-1 (`--flavour ua1`): all seven pass with zero failed rules. `results/paged/weasyprint.md` compares page counts and break positions with WeasyPrint, run as a separate program (information only).
- `tests/OneRender.Pdf.Tests` run OnePdf's own PDF/UA-1 check on the same six, compare extracted text with the document text, pin table pagination and the limits.
- `tools/pdf/compare-pixels.py` compares the raster backend's picture of each page with poppler's rendering of the PDF (mean SSIM over 7×7 windows on luma). Information only: the two rasterizers anti-alias text differently, so the templates score 0.89 to 0.98 per page (the long text-only report pages are lowest, about 0.92 on average).
- `tools/pdf/OneRender.PdfRun` writes a generated 50-page report or a 10,000-row table and prints time and memory.

## Choices the specs leave open

- The page is laid out at the content width of the first page and each page region is clipped to the content area; the margins hold the page layer.
- A profile other than `None` buffers the file; `None` streams.
- Links are annotations only for non-fragment targets; in-document anchors need positions the list does not carry.
- Conic gradients are 180 wedges; at 192 dpi islands rasterize at twice the screen resolution.
