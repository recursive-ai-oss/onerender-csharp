# RecursiveAI.OneRender.Accessibility

Produces rendered page facts (layout boxes, paint order, visibility, text boxes, colors behind text) from OneRender's own layout and paint, in the `RenderedFacts` shape that OneHtml.Accessibility reads, so accessibility checks need no browser capture.

```csharp
RenderedPage rendered = AccessibilityFacts.Render(html, new HtmlPageOptions(fonts) { ViewportWidth = 1280, ViewportHeight = 800 });
```

`AccessibilityFacts.FromLayout`, `FromPage` and `FromInteractive` build facts from pages you already have. See `docs/accessibility-facts.md` in the repository.

Part of OneRender, an HTML and CSS layout and rendering engine for .NET: <https://github.com/recursive-ai-oss/onerender-csharp>. Licence `MIT OR Apache-2.0`; see `THIRD-PARTY-NOTICES.md` in the package for bundled third-party material.
