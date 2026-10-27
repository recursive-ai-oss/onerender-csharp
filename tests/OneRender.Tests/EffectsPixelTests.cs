using OneImage;
using OneRender.Image;
using OneRender.Painting;
using OneRender.TestSupport;

namespace OneRender.Tests;

/// <summary>
/// Transforms, filters, masking, compositing and inline SVG through the whole pipeline. Expected colors follow CSS Transforms 1 and 2,
/// Filter Effects 1 and 2, CSS Masking 1 and Compositing 1, and are written down analytically.
/// </summary>
public class EffectsPixelTests
{
    private const string Head = "<!DOCTYPE html><html><body style='margin:0'>";

    private static RgbaImage Render(string body, int w = 64, int h = 64, float dpr = 1f) =>
        HtmlRender.ToImage(Head + body, new HtmlRenderOptions { Width = w, Height = h, DevicePixelRatio = dpr });

    private static (int R, int G, int B) Px(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    private static void Near((int R, int G, int B) expected, RgbaImage img, int x, int y, int tol = 3)
    {
        (int r, int g, int b) = Px(img, x, y);
        Assert.True(
            Math.Abs(r - expected.R) <= tol && Math.Abs(g - expected.G) <= tol && Math.Abs(b - expected.B) <= tol,
            "pixel (" + x + "," + y + ") is " + r + "," + g + "," + b + ", expected " + expected.R + "," + expected.G + "," + expected.B);
    }

    private static readonly (int, int, int) Red = (255, 0, 0);
    private static readonly (int, int, int) White = (255, 255, 255);

    // ------------------------------------------------------------------ transforms

    [Fact]
    public void TranslateMovesThePaintedBox()
    {
        RgbaImage img = Render("<div style='width:10px;height:10px;background:#f00;transform:translate(20px,30px)'></div>");
        Near(Red, img, 25, 35);
        Near(White, img, 5, 5);
    }

    [Fact]
    public void RotateTurnsAroundTheCenter()
    {
        // CSS Transforms 1 §13: a 20 x 10 box rotated 90 degrees about (10, 5) covers x 5..15 and y -5..15.
        RgbaImage img = Render("<div style='width:20px;height:10px;background:#f00;transform:rotate(90deg)'></div>");
        Near(Red, img, 10, 12);
        Near(White, img, 2, 5);
        Near(White, img, 18, 5);
    }

    [Fact]
    public void ScaleWithAnOriginAndTheIndividualProperties()
    {
        RgbaImage img = Render("<div style='width:10px;height:10px;background:#f00;transform:scale(2);transform-origin:0 0'></div>");
        Near(Red, img, 15, 15);
        Near(White, img, 25, 25);

        // Transforms 2 §2: translate, rotate and scale apply before transform; here scale 2 about the center (5, 5), then +10px.
        RgbaImage ind = Render("<div style='width:10px;height:10px;background:#f00;translate:10px 10px;scale:2'></div>");
        Near(Red, ind, 20, 20);
        Near(White, ind, 2, 2);
    }

    [Fact]
    public void ATransformedBoxIsTheContainingBlockOfAbsoluteDescendants()
    {
        // Transforms 1 §2: the transformed box is the containing block for fixed and absolute descendants.
        RgbaImage img = Render("<div style='margin-left:20px;margin-top:20px;width:30px;height:30px;transform:translate(0,0)'><div style='position:absolute;left:0;top:0;width:10px;height:10px;background:#f00'></div></div>");
        Near(Red, img, 25, 25);
        Near(White, img, 5, 5);
    }

    [Fact]
    public void PerspectiveScalesAboveThePlane()
    {
        // Transforms 2 §10: with perspective 100px, a box lifted by 50px scales by 100 / (100 - 50) = 2 about the perspective origin.
        RgbaImage img = Render("<div style='perspective:100px;width:64px;height:20px'><div style='width:20px;height:20px;background:#f00;transform:translateZ(50px)'></div></div>");
        // The parent's origin is (32, 10): the box covers x -32..8, y -10..30.
        Near(Red, img, 4, 15);
        Near(White, img, 12, 15);
    }

    [Fact]
    public void RotateYWithPerspectiveNarrowsTheBox()
    {
        RgbaImage img = Render("<div style='width:40px;height:40px;margin:10px;background:#f00;transform:perspective(200px) rotateY(60deg)'></div>");
        Near(Red, img, 30, 30);
        Near(White, img, 12, 30);
    }

    [Fact]
    public void BackfaceVisibilityHiddenHidesAFlippedBox()
    {
        RgbaImage img = Render("<div style='width:20px;height:20px;background:#f00;transform:rotateY(180deg);backface-visibility:hidden'></div>");
        Near(White, img, 10, 10);
        RgbaImage shown = Render("<div style='width:20px;height:20px;background:#f00;transform:rotateY(180deg)'></div>");
        Near(Red, shown, 10, 10);
    }

    [Fact]
    public void ATransformExtendsTheScrollableOverflow()
    {
        HtmlPage page = HtmlPage.Create(Head + "<div id='s' style='width:50px;height:50px;overflow:auto'><div style='width:10px;height:10px;transform:translate(100px,0)'></div></div>", new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 200 });
        int found = -1;
        for (int i = 0; i < page.Fragments.Count; i++)
        {
            var bb = page.Fragments.BorderBoxOf(i);
            if (bb.Width == 50 && bb.Height == 50)
            {
                found = i;
            }
        }

        Assert.True(found >= 0);
        Assert.True(page.Fragments.OverflowOf(found).Right >= 110, "a box moved to x 100..110 is scrollable overflow of its container");
    }

    // ------------------------------------------------------------------ compositing

    [Fact]
    public void MixBlendModeMultipliesWithTheBackdrop()
    {
        RgbaImage img = Render("<div style='width:20px;height:20px;background:#ff0'><div style='width:20px;height:20px;background:#0ff;mix-blend-mode:multiply'></div></div>");
        Near((0, 255, 0), img, 10, 10);
    }

    [Fact]
    public void IsolationKeepsABlendInsideTheGroup()
    {
        RgbaImage img = Render("<div style='width:20px;height:20px;background:#ff0;isolation:isolate'><div style='width:20px;height:20px;background:#0ff;mix-blend-mode:multiply'></div></div>");
        // The child blends with the parent's own background, which is inside the isolated group: yellow x cyan = lime still.
        Near((0, 255, 0), img, 10, 10);
        RgbaImage outside = Render("<div style='width:20px;height:20px;isolation:isolate'><div style='width:20px;height:20px;background:#0ff;mix-blend-mode:multiply'></div></div>");
        // Nothing but a transparent group behind the child, so it keeps its own color.
        Near((0, 255, 255), outside, 10, 10);
    }

    [Fact]
    public void BackgroundBlendModeBlendsTheLayersOfOneBackground()
    {
        RgbaImage img = Render("<div style='width:20px;height:20px;background:linear-gradient(#0ff,#0ff),#ff0;background-blend-mode:multiply'></div>");
        Near((0, 255, 0), img, 10, 10);
    }

    // ------------------------------------------------------------------ filters

    [Fact]
    public void ColorFilterFunctionsFollowFilterEffects()
    {
        const string Box = "<div style='width:20px;height:20px;background:#f00;filter:";
        Near((128, 0, 0), Render(Box + "brightness(0.5)'></div>"), 10, 10, 2);
        Near((0, 255, 255), Render(Box + "invert(1)'></div>"), 10, 10, 2);
        Near((54, 54, 54), Render(Box + "grayscale(1)'></div>"), 10, 10, 2);
        Near((0, 108, 108), Render(Box + "hue-rotate(180deg)'></div>"), 10, 10, 2);
        // contrast(0) maps every channel to one half.
        Near((128, 128, 128), Render(Box + "contrast(0)'></div>"), 10, 10, 2);
        // opacity(0.5) over white.
        Near((255, 128, 128), Render(Box + "opacity(0.5)'></div>"), 10, 10, 2);
    }

    [Fact]
    public void BlurSpreadsInkOutsideTheBox()
    {
        RgbaImage img = Render("<div style='margin:20px;width:20px;height:20px;background:#000;filter:blur(4px)'></div>");
        Near((0, 0, 0), img, 30, 30, 12);
        (int r, _, _) = Px(img, 18, 30);
        Assert.InRange(r, 20, 240); // the edge has moved outward: neither white nor black
        (int far, _, _) = Px(img, 3, 3);
        Assert.Equal(255, far);
    }

    [Fact]
    public void DropShadowPaintsAnOffsetCopy()
    {
        RgbaImage img = Render("<div style='margin:5px;width:10px;height:10px;background:#f00;filter:drop-shadow(10px 10px 0 #00f)'></div>");
        Near(Red, img, 8, 8);
        Near((0, 0, 255), img, 20, 20);
        Near(White, img, 40, 40);
    }

    [Fact]
    public void BackdropFilterFiltersWhatIsBehind()
    {
        RgbaImage img = Render("<div style='position:relative;width:40px;height:20px;background:linear-gradient(to right,#000 50%,#fff 50%)'><div style='position:absolute;left:0;top:0;width:40px;height:20px;backdrop-filter:invert(1)'></div></div>");
        Near(White, img, 5, 5);
        Near((0, 0, 0), img, 35, 5);
    }

    // ------------------------------------------------------------------ clip-path and masks

    [Fact]
    public void ClipPathShapes()
    {
        Near(White, Render("<div style='width:40px;height:40px;background:#f00;clip-path:inset(10px)'></div>"), 5, 5);
        Near(Red, Render("<div style='width:40px;height:40px;background:#f00;clip-path:inset(10px)'></div>"), 20, 20);
        RgbaImage circle = Render("<div style='width:40px;height:40px;background:#f00;clip-path:circle(10px at 20px 20px)'></div>");
        Near(Red, circle, 20, 20);
        Near(White, circle, 35, 5);
        RgbaImage poly = Render("<div style='width:40px;height:40px;background:#f00;clip-path:polygon(0 0,100% 0,0 100%)'></div>");
        Near(Red, poly, 5, 5);
        Near(White, poly, 35, 35);
        RgbaImage path = Render("<div style='width:40px;height:40px;background:#f00;clip-path:path(\"M0 0 H20 V20 H0 Z\")'></div>");
        Near(Red, path, 10, 10);
        Near(White, path, 30, 30);
        RgbaImage box = Render("<div style='width:20px;height:20px;padding:10px;background:#f00;clip-path:content-box'></div>");
        Near(White, box, 5, 5);
        Near(Red, box, 20, 20);
    }

    [Fact]
    public void MaskImageGradientFadesTheBox()
    {
        RgbaImage img = Render("<div style='width:40px;height:10px;background:#f00;mask-image:linear-gradient(to right,transparent,#000)'></div>");
        Near(White, img, 0, 5, 6);
        Near((255, 127, 127), img, 20, 5, 8);
        Near(Red, img, 39, 5, 8);
    }

    [Fact]
    public void MaskShorthandPositionsAndStopsRepeating()
    {
        RgbaImage img = Render("<div style='width:40px;height:10px;background:#f00;mask:linear-gradient(#000,#000) 0 0/20px 100% no-repeat'></div>");
        Near(Red, img, 10, 5);
        Near(White, img, 30, 5);
    }

    [Fact]
    public void MaskCompositeCombinesLayers()
    {
        // Two opaque halves; exclude of overlapping layers leaves only the non-overlapping part visible.
        RgbaImage img = Render("<div style='width:40px;height:10px;background:#f00;mask-image:linear-gradient(#000,#000),linear-gradient(#000,#000);mask-size:30px 100%,30px 100%;mask-position:0 0,10px 0;mask-repeat:no-repeat;mask-composite:exclude'></div>");
        Near(Red, img, 5, 5);
        Near(White, img, 20, 5);
        Near(Red, img, 35, 5);
    }

    // ------------------------------------------------------------------ inline svg

    [Fact]
    public void InlineSvgIsAReplacedElementDrawnByTheSvgRenderer()
    {
        RgbaImage img = Render("<svg width='20' height='20' viewBox='0 0 10 10'><rect width='10' height='10' fill='#f00'/></svg>");
        Near(Red, img, 10, 10);
        Near(White, img, 30, 10);
    }

    [Fact]
    public void InlineSvgFillsCurrentColor()
    {
        RgbaImage img = Render("<svg width='10' height='10' style='color:#00f'><rect width='10' height='10' fill='currentColor'/></svg>");
        Near((0, 0, 255), img, 5, 5);
    }

    [Fact]
    public void InlineSvgWithOnlyAViewBoxTakesItsRatio()
    {
        RgbaImage img = Render("<div style='width:40px'><svg viewBox='0 0 2 1'><rect width='2' height='1' fill='#f00'/></svg></div>");
        Near(Red, img, 20, 10);
        Near(White, img, 20, 30);
    }

    [Fact]
    public void InlineSvgPreserveAspectRatioFitsTheViewBox()
    {
        // xMidYMid meet: a square viewBox in a 40 x 20 viewport is drawn 20 x 20 in the middle.
        RgbaImage img = Render("<svg width='40' height='20' viewBox='0 0 10 10'><rect width='10' height='10' fill='#f00'/></svg>");
        Near(Red, img, 20, 10);
        Near(White, img, 5, 10);
    }

    // ------------------------------------------------------------------ the frame path

    private const string Scene =
        "<!DOCTYPE html><html><head><style>body{margin:0}</style></head><body>" +
        "<div style='width:30px;height:30px;background:#f00;transform:rotate(10deg) translateZ(0);opacity:.8'></div>" +
        "<div style='perspective:200px'><div style='width:30px;height:30px;background:#0f0;transform:rotateY(40deg)'></div></div>" +
        "<div style='width:30px;height:30px;background:#00f;filter:blur(2px) brightness(1.2) drop-shadow(2px 2px 1px #000)'></div>" +
        "<div style='width:30px;height:30px;background:#ff0;clip-path:circle(40%);mix-blend-mode:multiply'></div>" +
        "<div style='width:30px;height:30px;background:#0ff;mask:linear-gradient(#000,transparent)'></div>" +
        "<div style='width:30px;height:30px;backdrop-filter:blur(1px)'></div>" +
        "<svg width='30' height='30'><circle cx='15' cy='15' r='10' fill='red'/></svg></body></html>";

    [Fact]
    public void RepaintingAnUnchangedEffectsPageAllocatesNothing()
    {
        HtmlPage page = HtmlPage.Create(Scene, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 300 });
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(300, 300) { Scroll = page.Scroll };
        void Frame() => painter.Paint(page.Fragments, retained, options);
        Frame();
        Assert.True(retained.Current.RequiredMinor >= 2);
        Allocation.AssertZero(Frame, "repaint of a page with transforms, filters, masks, blending and svg", warmup: 4, iterations: 20);
    }

    [Fact]
    public void PatchingATransformOrOpacityAllocatesNothingAndNeedsNoLayout()
    {
        HtmlPage page = HtmlPage.Create(
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:30px;height:30px;background:#f00;transform:translate(5px,5px);opacity:.5'></div></body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, new PaintOptions(100, 100));
        DisplayList list = retained.Current;
        ItemId transform = default, group = default;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.PushTransform)
            {
                transform = list[i].Id;
            }
            else if (list[i].Kind == ItemKind.PushGroup)
            {
                group = list[i].Id;
            }
        }

        Assert.True(transform.IsValid && group.IsValid);
        float x = 5f;
        void Patch()
        {
            x = x == 5f ? 40f : 5f;
            retained.PatchTransform(transform, System.Numerics.Matrix3x2.CreateTranslation(x, 5f));
            retained.PatchOpacity(group, x == 5f ? 0.5f : 0.9f);
        }

        Patch();
        Allocation.AssertZero(Patch, "patching transform and opacity", warmup: 4, iterations: 20);
        RgbaImage img = DisplayListRasterizer.Render(retained.Current, 100, 100);
        Assert.NotNull(img);
    }
}
