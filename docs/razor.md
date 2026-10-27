# Razor components to PDF and images

`RecursiveAI.OneRender.Razor` renders a Razor component to a tagged PDF or to pixels in one call. It runs the component on .NET's
`HtmlRenderer`, serves the document's style sheets, images and fonts from the app's web root and static web assets, and passes the
markup to `HtmlPdf.ToPdf` or `HtmlRender.ToImage`. Everything it does can be done by hand with those public APIs; it adds no private hooks.
The package references the ASP.NET Core shared framework and nothing else.

```csharp
builder.Services.AddOneRender();                      // singleton RazorRenderer over IWebHostEnvironment.WebRootFileProvider

app.MapGet("/invoice.pdf", async (RazorRenderer r, CancellationToken ct) =>
{
    RazorPdfResult pdf = await r.RenderPdfAsync<Invoice>(new Dictionary<string, object?> { ["Number"] = "INV-1" }, cancellationToken: ct);
    return Results.File(pdf.Bytes!, "application/pdf");
});

RazorImageResult img = await renderer.RenderImageAsync<Invoice>(parameters, new HtmlRenderOptions { Width = 800, Height = 600 });
byte[] png = img.ToPng();
```

## Behavior

- **Parameters** are a name-to-value dictionary (null for none). The component renders in a fresh service scope per call, so scoped
  services behave as in a request.
- **Documents and fragments.** A component whose output has an `<html>` element is used as is (a `<!DOCTYPE html>` is added if missing).
  Otherwise the markup is wrapped in a document with `RazorRenderOptions.Language` (default `en`), `Title` and one `<link>` per entry of
  `Stylesheets`. For a tagged PDF with a title and language, render a whole document or set those options.
- **Options.** `HtmlPdfOptions` and `HtmlRenderOptions` pass through (page setup, output profile such as PDF/UA-1, viewport, device-pixel
  ratio, fonts). Their `BaseUrl` and `Resources` are replaced: the web root is asked first, then the options' own `Resources`.
- **Assets.** `StaticAssetResolver` maps an address to a path below the web root (`css/app.css`, `/img/a.png`, `http://localhost/x?v=1`,
  `_content/<Library>/site.css`), decodes `data:` addresses, and refuses `..` segments. `RazorRenderOptions.AdditionalAssets` adds
  providers searched after the web root. In a web host `IWebHostEnvironment.WebRootFileProvider` already includes the static web assets of
  referenced libraries (in development through the manifest, when published as files under `wwwroot/_content`).
- **Missing assets never throw.** The engine records a diagnostic and lays out without the file; the result's `MissingAssets` lists each
  path no provider had, and `RazorPdfResult.Diagnostics` carries the engine's diagnostics.
- **Async and cancellation.** Rendering the component is asynchronous (components may await); laying out and painting then run on the
  calling continuation. Every method takes a `CancellationToken`: a token cancelled before the call, or while a component is still
  rendering, throws `OperationCanceledException`; layout and paint check it as `HtmlPdf` and `HtmlRender` do.
- **Streams.** `RenderPdfAsync<T>(Stream, ...)` writes to the caller's stream and returns the page count with `Bytes` null.

## Sample

`samples/InvoiceApp` is an ASP.NET app with an invoice component styled by Tailwind CSS 4.3.3 (`wwwroot/css/app.css`, compiled by
`samples/InvoiceApp/build-css.sh` from `Styles/app.css`; the compiled file keeps Tailwind's MIT notice, see THIRD-PARTY-NOTICES). It
serves `/invoice.pdf` (PDF/UA-1) and `/invoice.png`. `tests/OneRender.Razor.Tests` renders it through the renderer and through
`HtmlPdf`/`HtmlRender` directly and requires byte-identical PDFs and pixel-identical images; `tools/pdf/run-verapdf-razor.sh` validates the
sample's PDF against PDF/UA-1 with veraPDF.

## Limits

- `<img>` of an SVG file is not drawn by `.Image` or `.Pdf` (only inline `<svg>` is); use PNG, JPEG, WebP or inline SVG.
- The package is not part of the WebAssembly smoke (it needs ASP.NET Core). Generic entry points require the component type to be
  preserved by the trimmer; type arguments written in code are.
