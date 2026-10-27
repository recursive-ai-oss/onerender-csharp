using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Text;

namespace OneRender.Pdf.Tests;

public class PixelReferenceTests
{
    /// <summary>
    /// Writes the raster backend's picture of every template page (96 dpi, the content area of the page) as binary PPM files, for the
    /// comparison against a PDF viewer's rendering of the same page (<c>tools/pdf/compare-pixels.sh</c>). Runs only when
    /// ONERENDER_PDF_OUT is set; it asserts nothing about the pixels.
    /// </summary>
    [Theory]
    [MemberData(nameof(TemplateTests.Names), MemberType = typeof(TemplateTests))]
    public void WriteRasterReference(string name)
    {
        if (Support.OutDir is not { Length: > 0 } dir)
        {
            return;
        }

        PdfPageSetup setup = name == "certificate" ? new PdfPageSetup(1122.5196850, 793.7007874, 24, 24, 24, 24) : PdfPageSetup.A4;
        string baseUrl = "https://templates.invalid/" + name + "/index.html";
        var images = new ImageStore(Support.TemplateResources(name), baseUrl);
        var options = new HtmlPageOptions(FontProviders.ForCurrentThread())
        {
            Images = images,
            Replaced = images,
            DevicePixelRatio = 1,
            ViewportWidth = setup.ContentWidth,
            ViewportHeight = setup.ContentHeight,
            BaseUrl = baseUrl,
            StylesheetBytes = Support.TemplateResources(name),
        };
        HtmlPage page = HtmlPage.Create(Support.Template(name), options, default);
        int pages = PageFragmentation.Paginate(page.Fragments, setup.ContentWidth, setup.ContentHeight, default);
        var list = new DisplayList();
        new Painter().Paint(
            page.Fragments,
            list,
            new PaintOptions(setup.ContentWidth, setup.ContentHeight) { Scroll = page.Scroll, Images = images, Fonts = options.Fonts, DevicePixelRatio = 1, BaseUrl = baseUrl },
            default);
        double stride = PageFragmentation.PageStride(setup.ContentWidth);
        int w = (int)Math.Ceiling(setup.ContentWidth), h = (int)Math.Ceiling(setup.ContentHeight);
        Directory.CreateDirectory(dir);
        for (int i = 0; i < pages; i++)
        {
            var shifted = new DisplayList();
            shifted.PushTransform(ItemId.Of(uint.MaxValue, ushort.MaxValue, ushort.MaxValue), System.Numerics.Matrix3x2.CreateTranslation((float)(-i * stride), 0));
            DisplayListCopier.Copy(list, 0, list.Count - 1, shifted);
            shifted.PopTransform();
            RgbaImage img = DisplayListRasterizer.Render(shifted, w, h, new RasterOptions { Images = images }, default);
            using FileStream fs = File.Create(Path.Combine(dir, name + "-" + (i + 1) + ".ref.ppm"));
            fs.Write(System.Text.Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n"));
            byte[] rgb = new byte[w * h * 3];
            for (int p = 0; p < w * h; p++)
            {
                rgb[p * 3] = img.Pixels[p * 4];
                rgb[(p * 3) + 1] = img.Pixels[(p * 4) + 1];
                rgb[(p * 3) + 2] = img.Pixels[(p * 4) + 2];
            }

            fs.Write(rgb);
        }
    }
}
