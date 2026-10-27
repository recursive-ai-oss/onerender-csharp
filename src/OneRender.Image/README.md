# RecursiveAI.OneRender.Image

Turns a OneRender display list into pixels through OneImage. It is the reference rasterizer for the engine's tests.

```csharp
using OneRender.Image;

RgbaImage image = HtmlRender.ToImage(html, new HtmlRenderOptions { Width = 800, Height = 600, DevicePixelRatio = 2 });
byte[] png = image.ToPng();
```

`HtmlRender.ToImage` is the happy path; `DisplayListRasterizer` renders any display list, or a region of it, which is what a viewer with damage regions wants. The package references `RecursiveAI.OneRender.Fonts`, so the default fonts need no setup. `ColorGlyphBitmaps` decodes the PNG glyph images of color bitmap fonts for backends that draw them.

Part of OneRender, an HTML and CSS layout and rendering engine for .NET: <https://github.com/recursive-ai-oss/onerender-csharp>. Licence `MIT OR Apache-2.0`; see `THIRD-PARTY-NOTICES.md` in the package for bundled third-party material.
