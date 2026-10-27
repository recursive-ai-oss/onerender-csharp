# RecursiveAI.OneRender

The OneRender engine: an HTML and CSS layout engine for .NET, written from the specifications.

HTML (through OneHtml's parser and cascade) or a styled tree built in C# goes in; the engine builds a box tree, lays it out incrementally into a fragment tree, and paints a retained display list with stable item ids and damage regions. It also does hit testing, DOM-standard events, focus, scrolling, form controls with text editing and IME composition, CSS Transitions and Animations on the host's clock, and paged media (`@page`, margin boxes, counters).

No networking, no clock and no file access: the host passes time and supplies stylesheets, images and fonts. The package depends only on OneHtml and OneFont. It does not draw: use `RecursiveAI.OneRender.Image` for pixels, `RecursiveAI.OneRender.Pdf` for a tagged PDF, or read the display list yourself.

```csharp
var options = new HtmlPageOptions(fonts) { ViewportWidth = 800, ViewportHeight = 600 };   // fonts: an IFontProvider, e.g. FontProviders.CreateDefault() from .Image
HtmlPage page = HtmlPage.Create(html, options);
var list = new DisplayList();
page.Paint(list);
```

For an interactive host, `InteractivePage` (events, input, `Advance(nowMs)`) and `LivePage` (edit a `StyledTree`, `Flush()`) update the page incrementally and allocate nothing in steady state. The display list format is specified in `docs/display-list.md` in the repository.

Part of OneRender, an HTML and CSS layout and rendering engine for .NET: <https://github.com/recursive-ai-oss/onerender-csharp>. Licence `MIT OR Apache-2.0`; see `THIRD-PARTY-NOTICES.md` in the package for bundled third-party material.
