using OneRender.Image;
using OneRender.Painting;
using OneRender.TestSupport;

namespace OneRender.Tests;

/// <summary>The second paint area keeps the frame path at zero bytes: repainting an unchanged page and patching fixed backgrounds.</summary>
public class PaintAllocationTests
{
    private sealed class Table : IImageSource
    {
        public bool TryGetImage(string address, out OneRender.Painting.ImageInfo image)
        {
            image = new OneRender.Painting.ImageInfo(new ImageHandle(address.EndsWith("a.png", StringComparison.Ordinal) ? 1 : 2), 16, 8);
            return true;
        }
    }

    private const string Scene =
        "<!DOCTYPE html><html><head><style>body{margin:0;font:14px Liberation Sans}</style></head><body>" +
        "<div style='width:100px;height:60px;border:4px solid;border-image:url(b.png) 1/4px round;border-radius:0;background:url(a.png) 0 0/16px 8px space,radial-gradient(circle,#fff,#06f),repeating-linear-gradient(45deg,#fc0 0 4px,#c60 4px 8px);box-shadow:3px 3px 5px #888,inset 0 0 6px #000;outline:2px solid #f0f;outline-offset:2px'></div>" +
        "<div style='margin:6px;width:120px;height:30px;border-radius:10px;background:conic-gradient(#f00,#00f,#f00);text-shadow:1px 1px 2px #000;text-decoration:underline wavy #f00'>Painted text</div>" +
        "<ul><li>one</li><li>two</li></ul><ol><li>three</li></ol>" +
        "<img src='a.png' style='display:block;width:40px;height:40px;object-fit:cover;object-position:20% 30%'>" +
        "<div style='height:1500px;background:url(a.png) 10px 10px/32px 16px no-repeat fixed,linear-gradient(red,blue) 0 0/20px 20px fixed'></div></body></html>";

    [Fact]
    public void RepaintingAnUnchangedPageWithBackgroundsAndImagesAllocatesNothing()
    {
        HtmlPage page = HtmlPage.Create(Scene, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 300, Images = new Table() });
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(300, 300) { Scroll = page.Scroll, Images = new Table() };
        void Frame() => painter.Paint(page.Fragments, retained, options);
        Frame();
        Assert.True(retained.Current.Count > 40);
        Allocation.AssertZero(Frame, "repaint of a page with backgrounds, images, shadows, decorations and markers", warmup: 4, iterations: 20);
    }

    [Fact]
    public void ScrollingAFixedBackgroundPatchesInPlaceAtZeroBytes()
    {
        HtmlPage page = HtmlPage.Create(Scene, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 300, Images = new Table() });
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(300, 300) { Scroll = page.Scroll, Images = new Table() };
        painter.Paint(page.Fragments, retained, options);
        double y = 0;
        void Scroll()
        {
            y = y == 0 ? 80 : 0;
            page.Scroll.SetViewportOffset(0, y);
            painter.PatchScroll(retained);
        }

        page.Scroll.SetViewportOffset(0, 80);
        Assert.True(painter.PatchScroll(retained) >= 2, "the fixed layers and the content move");
        Allocation.AssertZero(Scroll, "scroll patching with fixed backgrounds", warmup: 4, iterations: 20);
    }
}
