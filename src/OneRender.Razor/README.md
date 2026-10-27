# RecursiveAI.OneRender.Razor

Renders a Razor component to a PDF or an image in one call, on .NET's `HtmlRenderer`. Stylesheets, images and fonts are served from the app's `wwwroot` and static web assets; nothing is fetched over the network.

```csharp
builder.Services.AddOneRender();

RazorPdfResult pdf = await renderer.RenderPdfAsync<Invoice>(parameters);
RazorImageResult img = await renderer.RenderImageAsync<Invoice>(parameters);
byte[] png = img.ToPng();
```

It is a thin layer over `HtmlPdf` and `HtmlRender`, and the output is identical to calling those with the same markup. The package references the ASP.NET Core shared framework.

Part of OneRender, an HTML and CSS layout and rendering engine for .NET: <https://github.com/recursive-ai-oss/onerender-csharp>. Licence `MIT OR Apache-2.0`; see `THIRD-PARTY-NOTICES.md` in the package for bundled third-party material.
