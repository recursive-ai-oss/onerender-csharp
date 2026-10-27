# RecursiveAI.OneRender.Pdf

Writes a OneRender display list as a PDF through OnePdf's writer. Output is tagged by default, with the structure tree taken from the HTML semantics (headings, paragraphs, tables, figures with alt text, links), `/ToUnicode` and `/ActualText` for text, font subsetting, and the PDF/UA-1 and PDF/A-2a profiles.

```csharp
using OneRender.Pdf;

byte[] pdf = HtmlPdf.ToPdf(html, new HtmlPdfOptions { Page = PdfPageSetup.A4 });
```

`HtmlPdf.ToPdf` paginates with `@page` rules, margin boxes and counters; `DisplayListPdfWriter` is the low level, one display list per page. Effects the PDF model cannot express (some filters, blend modes) are rasterized into images with a diagnostic.

Known limitation: tagged PDF/UA-1 output with complex-script text (full Arabic, Devanagari or Thai sentences) can fail veraPDF rule 7.21.7, because glyphs inside multi-glyph clusters have no `ToUnicode` mapping. Extracted text is still correct. Pages that mix scripts with short complex-script words pass.

Part of OneRender, an HTML and CSS layout and rendering engine for .NET: <https://github.com/recursive-ai-oss/onerender-csharp>. Licence `MIT OR Apache-2.0`; see `THIRD-PARTY-NOTICES.md` in the package for bundled third-party material.
