using System.IO;
using OneImage;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

/// <summary>
/// Pixels of the second paint area through the whole pipeline: layered backgrounds, gradients, border-image, shadows, outlines,
/// text decoration and shadow, markers, <c>object-fit</c> and pixel snapping. Expected colors follow CSS Backgrounds 3, CSS Images 3,
/// CSS UI 4 and Text Decoration 4, and are written down analytically; a few scenes are also kept as golden images.
/// </summary>
public class PaintPixelTests
{
    private const string Head = "<!DOCTYPE html><html><body style='margin:0'>";

    private static readonly (byte, byte, byte) Red = (255, 0, 0);
    private static readonly (byte, byte, byte) Lime = (0, 255, 0);
    private static readonly (byte, byte, byte) Blue = (0, 0, 255);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    private static RgbaImage Render(string body, int w = 64, int h = 64, float dpr = 1f, Func<string, byte[]?>? resources = null) =>
        HtmlRender.ToImage(Head + body, new HtmlRenderOptions { Width = w, Height = h, DevicePixelRatio = dpr, Resources = resources });

    private static (byte R, byte G, byte B) Px(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    private static void Near((byte R, byte G, byte B) expected, RgbaImage img, int x, int y, int tol = 3)
    {
        (byte r, byte g, byte b) = Px(img, x, y);
        Assert.True(
            Math.Abs(r - expected.R) <= tol && Math.Abs(g - expected.G) <= tol && Math.Abs(b - expected.B) <= tol,
            "pixel (" + x + "," + y + ") is " + r + "," + g + "," + b + ", expected " + expected.R + "," + expected.G + "," + expected.B);
    }

    private static void Solid((byte, byte, byte) expected, RgbaImage img, int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
        {
            for (int i = x; i < x + w; i++)
            {
                Near(expected, img, i, j);
            }
        }
    }

    private static string SolidGradient(string color) => "linear-gradient(" + color + "," + color + ")";

    /// <summary>A PNG of <paramref name="pixels"/> (row-major, w by h), written through oneimage.</summary>
    private static byte[] Png(int w, int h, params Rgba32[] pixels)
    {
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "onerender-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            OneImage.Image.FromPixels<Rgba32>(pixels, w, h).Save(file);
            return File.ReadAllBytes(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static Func<string, byte[]?> Resources(params (string Name, byte[] Bytes)[] files) =>
        address =>
        {
            foreach ((string name, byte[] bytes) in files)
            {
                if (address.EndsWith(name, StringComparison.Ordinal))
                {
                    return bytes;
                }
            }

            return null;
        };

    private static readonly Rgba32 R = new(255, 0, 0, 255);
    private static readonly Rgba32 B = new(0, 0, 255, 255);
    private static readonly Rgba32 G = new(0, 255, 0, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    // ---------------------------------------------------------------- backgrounds

    [Fact]
    public void LayersPaintFirstOnTop()
    {
        RgbaImage img = Render("<div style='width:40px;height:40px;background:" + SolidGradient("#f00") + " 0 0/10px 10px no-repeat," + SolidGradient("#00f") + "'></div>");
        Near(Red, img, 5, 5);
        Near(Blue, img, 15, 5);
        Near(Blue, img, 5, 15);
        Near(White, img, 45, 5);
    }

    [Fact]
    public void RepeatXRepeatsOnlyAcrossAndStopsAtTheClip()
    {
        RgbaImage img = Render("<div style='width:40px;height:30px;background:" + SolidGradient("#f00") + " 0 0/8px 8px repeat-x'></div>");
        Near(Red, img, 4, 4);
        Near(Red, img, 36, 4);
        Near(White, img, 4, 12);
    }

    [Fact]
    public void SpaceDistributesTheGapAndRoundStretchesTheTile()
    {
        // Backgrounds 3 §3.5: 40px area, 12px tile. space: floor(40/12)=3 tiles, the 4px left over split in two gaps of 2px.
        RgbaImage s = Render("<div style='width:40px;height:12px;background:" + SolidGradient("#f00") + " 0 0/12px 12px space space'></div>");
        Near(Red, s, 6, 6);
        Near(White, s, 13, 6);
        Near(Red, s, 20, 6);
        Near(White, s, 27, 6);
        Near(Red, s, 34, 6);
        // round: 40/12 rounds to 3 tiles of 13.33px, so nothing is left uncovered and the last tile reaches the edge.
        RgbaImage r = Render("<div style='width:40px;height:12px;background:" + SolidGradient("#f00") + " 0 0/12px 12px round round'></div>");
        Solid(Red, r, 0, 0, 40, 12);
    }

    [Fact]
    public void OriginAndClipSelectTheBoxes()
    {
        string common = "width:20px;height:20px;padding:10px;border:10px solid rgba(0,0,255,0.0);";
        // origin: content-box with no-repeat positions the 6px tile at the content corner (20,20); clip: border-box shows it
        // even over the border area only if the tile is there, here it is not, so just the tile.
        RgbaImage o = Render("<div style='" + common + "background:" + SolidGradient("#f00") + " 0 0/6px 6px no-repeat content-box'></div>");
        Near(Red, o, 22, 22);
        Near(White, o, 12, 12);
        // clip: content-box confines a border-box-sized fill to the content box.
        RgbaImage c = Render("<div style='" + common + "background:#f00 border-box content-box'></div>");
        Near(Red, c, 25, 25);
        Near(White, c, 15, 15);
        Near(White, c, 45, 45);
        RgbaImage p = Render("<div style='" + common + "background:#f00;background-clip:padding-box'></div>");
        Near(Red, p, 15, 15);
        Near(White, p, 5, 5);
    }

    [Fact]
    public void ImageSizesCoverContainAndAutoKeepTheRatio()
    {
        // A 2x1 image: left red, right blue.
        Func<string, byte[]?> res = Resources(("two.png", Png(2, 1, R, B)));
        const string Box = "width:40px;height:40px;image-rendering:pixelated;background:url(two.png) center / ";
        RgbaImage contain = Render("<div style='" + Box + "contain no-repeat'></div>", resources: res);
        Near(White, contain, 20, 5);
        Near(Red, contain, 10, 20);
        Near(Blue, contain, 30, 20);
        RgbaImage cover = Render("<div style='" + Box + "cover no-repeat'></div>", resources: res);
        Near(Red, cover, 5, 5);
        Near(Blue, cover, 35, 35);
        RgbaImage auto = Render("<div style='" + Box + "auto 10px no-repeat'></div>", resources: res);
        Near(Red, auto, 15, 20);
        Near(Blue, auto, 25, 20);
        Near(White, auto, 5, 20);
        Near(White, auto, 15, 5);
    }

    [Fact]
    public void FixedAttachmentIsRelativeToTheViewport()
    {
        RgbaImage img = Render("<div style='width:40px;height:40px;margin:30px 0 0 30px;background:" + SolidGradient("#f00") + " 0 0/10px 10px no-repeat fixed'></div>");
        Near(White, img, 35, 35);
        RgbaImage visible = Render("<div style='width:40px;height:40px;background:" + SolidGradient("#f00") + " 0 0/10px 10px no-repeat fixed'></div>");
        Near(Red, visible, 5, 5);
    }

    // ---------------------------------------------------------------- gradients

    [Fact]
    public void HardStopsAndStopFixup()
    {
        RgbaImage hard = Render("<div style='width:40px;height:10px;background:linear-gradient(to right,#f00 50%,#00f 50%)'></div>");
        Near(Red, hard, 5, 5);
        Near(Red, hard, 18, 5);
        Near(Blue, hard, 22, 5);
        // Images 3 §3.4.1: a stop earlier than the one before it is raised to it, so 60% red then blue "at 20%" is a hard edge at 60%.
        RgbaImage fix = Render("<div style='width:100px;height:10px;background:linear-gradient(to right,#f00 60%,#00f 20%)'></div>", w: 100);
        Near(Red, fix, 58, 5);
        Near(Blue, fix, 62, 5);
        // Before the first stop and after the last the end colors extend.
        RgbaImage ext = Render("<div style='width:100px;height:10px;background:linear-gradient(to right,#f00 30%,#00f 70%)'></div>", w: 100);
        Near(Red, ext, 10, 5);
        Near(Blue, ext, 90, 5);
    }

    [Fact]
    public void LinearGradientMidpointIsTheMix()
    {
        RgbaImage img = Render("<div style='width:64px;height:10px;background:linear-gradient(to right,#f00,#00f)'></div>");
        Near((128, 0, 127), img, 32, 5, 6);
        RgbaImage hint = Render("<div style='width:100px;height:10px;background:linear-gradient(to right,#f00,10%,#00f)'></div>", w: 100);
        (byte r, _, _) = Px(hint, 50, 5);
        Assert.True(r < 128, "a hint at 10% moves the midpoint toward the first color's start, so at 50% the mix is mostly blue");
    }

    [Fact]
    public void RepeatingAndRadialAndConicGradientsPaint()
    {
        RgbaImage rep = Render("<div style='width:40px;height:10px;background:repeating-linear-gradient(to right,#f00 0,#f00 10px,#00f 10px,#00f 20px)'></div>");
        Near(Red, rep, 5, 5);
        Near(Blue, rep, 15, 5);
        Near(Red, rep, 25, 5);
        Near(Blue, rep, 35, 5);
        RgbaImage rad = Render("<div style='width:40px;height:40px;background:radial-gradient(circle closest-side,#f00,#00f)'></div>");
        Near(Red, rad, 20, 20, 12);
        (byte r1, _, byte b1) = Px(rad, 36, 20);
        Assert.True(b1 > r1);
        RgbaImage con = Render("<div style='width:40px;height:40px;background:conic-gradient(#f00 0 25%,#00f 25% 50%,#0f0 50% 100%)'></div>");
        Near(Red, con, 30, 10);
        Near(Blue, con, 30, 30);
        Near(Lime, con, 10, 30);
    }

    // ---------------------------------------------------------------- borders and shadows

    [Fact]
    public void BorderImageSlicesCornersAndStretchesEdges()
    {
        // A 3x3 source: red corners, blue edges, green middle (middle is dropped unless "fill").
        Rgba32[] src = [R, B, R, B, G, B, R, B, R];
        RgbaImage img = Render(
            "<div style='width:20px;height:20px;border:6px solid;border-image:url(b.png) 1 / 6px stretch'></div>",
            resources: Resources(("b.png", Png(3, 3, src))));
        Near(Red, img, 2, 2);
        Near(Red, img, 29, 2);
        Near(Blue, img, 16, 2);
        Near(Blue, img, 2, 16);
        Near(White, img, 16, 16);
    }

    [Fact]
    public void OuterAndInsetShadowsAndSpread()
    {
        RgbaImage outer = Render("<div style='margin:20px;width:20px;height:20px;background:#fff;box-shadow:10px 0 0 #00f'></div>");
        Near(Blue, outer, 45, 30);
        Near(White, outer, 55, 30);
        Near(White, outer, 30, 30);
        RgbaImage spread = Render("<div style='margin:20px;width:20px;height:20px;background:#fff;box-shadow:0 0 0 4px #f00'></div>");
        Near(Red, spread, 17, 30);
        Near(White, spread, 15, 30);
        RgbaImage inset = Render("<div style='margin:10px;width:40px;height:40px;background:#fff;box-shadow:inset 0 0 0 5px #00f'></div>");
        Near(Blue, inset, 12, 30);
        Near(White, inset, 20, 30);
        Near(Blue, inset, 30, 12);
        RgbaImage blur = Render("<div style='margin:20px;width:20px;height:20px;background:#fff;box-shadow:0 0 10px #000'></div>");
        (byte edge, _, _) = Px(blur, 17, 30);
        Assert.InRange((int)edge, 1, 254);
    }

    [Fact]
    public void ABlurMuchLargerThanTheBoxCompletes()
    {
        RgbaImage img = Render("<div style='margin:30px;width:2px;height:2px;background:#fff;box-shadow:0 0 400px #000'></div>");
        Assert.Equal(64, img.Width);
    }

    [Fact]
    public void RoundedCornersClipTheBackground()
    {
        RgbaImage img = Render("<div style='width:40px;height:40px;border-radius:20px;background:#f00'></div>");
        Near(White, img, 1, 1);
        Near(Red, img, 20, 20);
        Near(Red, img, 20, 1);
    }

    [Fact]
    public void OutlineDrawsOutsideTheBoxWithOffset()
    {
        RgbaImage img = Render("<div style='margin:20px;width:20px;height:20px;background:#fff;outline:4px solid #f00;outline-offset:2px'></div>");
        Near(White, img, 19, 30);
        Near(White, img, 18, 30);
        Near(Red, img, 16, 30);
        Near(Red, img, 14, 30);
        Near(White, img, 13, 30);
        // Outlines take no space.
        Near(White, img, 30, 30);
    }

    [Fact]
    public void AutoOutlineIsAVisibleRing()
    {
        RgbaImage img = Render("<div style='margin:20px;width:20px;height:20px;background:#fff;outline:auto'></div>");
        bool any = false;
        for (int x = 14; x < 20; x++)
        {
            (byte r, byte g, byte b) = Px(img, x, 30);
            any |= r != 255 || g != 255 || b != 255;
        }

        Assert.True(any);
    }

    // ---------------------------------------------------------------- text

    private static int LongestRun(RgbaImage img, Func<byte, byte, byte, bool> match)
    {
        int best = 0;
        for (int y = 0; y < img.Height; y++)
        {
            int run = 0;
            for (int x = 0; x < img.Width; x++)
            {
                (byte r, byte g, byte b) = Px(img, x, y);
                run = match(r, g, b) ? run + 1 : 0;
                best = Math.Max(best, run);
            }
        }

        return best;
    }

    private static bool IsRed(byte r, byte g, byte b) => r > 200 && g < 60 && b < 60;

    [Theory]
    [InlineData("underline")]
    [InlineData("overline")]
    [InlineData("line-through")]
    public void DecorationLinesRunAcrossTheText(string kind)
    {
        const string Text = "<span style='font:16px Liberation Sans;color:#000;";
        RgbaImage with = Render("<p style='margin:10px 0'>" + Text + "text-decoration:" + kind + " #f00'>Hello world</span></p>", 120, 40);
        RgbaImage without = Render("<p style='margin:10px 0'>" + Text + "'>Hello world</span></p>", 120, 40);
        Assert.True(LongestRun(with, IsRed) >= 60, kind + " did not paint a line across the text");
        Assert.True(LongestRun(without, IsRed) < 3);
    }

    [Theory]
    [InlineData(16, new[] { 25 })]
    [InlineData(20, new[] { 29, 30 })]
    [InlineData(32, new[] { 41, 42, 43 })]
    [InlineData(48, new[] { 57, 58, 59, 60 })]
    public void UnderlineRowsMatchChrome(int size, int[] rows)
    {
        // Rows measured in headless Chrome 156 with the same font: tools/snap/probe.py in UNDERLINE mode.
        RgbaImage img = Render("<p style='margin:10px 0;font:" + size + "px Liberation Sans;color:#000;text-decoration:underline #f00'>Hello world</p>", 300, 140);
        var found = new List<int>();
        for (int y = 0; y < img.Height; y++)
        {
            int n = 0;
            for (int x = 0; x < img.Width; x++)
            {
                (byte r, byte g, byte b) = Px(img, x, y);
                n += IsRed(r, g, b) ? 1 : 0;
            }

            if (n >= 20)
            {
                found.Add(y);
            }
        }

        Assert.Equal(rows, found.ToArray());
    }

    [Fact]
    public void DecorationStylesAndThickness()
    {
        RgbaImage thick = Render("<p style='margin:10px 0;font:16px Liberation Sans;color:#000;text-decoration:underline #f00 4px'>Hello world</p>", 120, 40);
        int rows = 0;
        for (int y = 0; y < thick.Height; y++)
        {
            int n = 0;
            for (int x = 0; x < thick.Width; x++)
            {
                (byte r, byte g, byte b) = Px(thick, x, y);
                n += IsRed(r, g, b) ? 1 : 0;
            }

            rows += n >= 20 ? 1 : 0;
        }

        Assert.Equal(4, rows);
        RgbaImage dotted = Render("<p style='margin:10px 0;font:16px Liberation Sans;color:#000;text-decoration:underline dotted #f00'>Hello world</p>", 120, 40);
        Assert.True(LongestRun(dotted, IsRed) <= 3);
        RgbaImage dbl = Render("<p style='margin:10px 0;font:16px Liberation Sans;color:#000;text-decoration:underline double #f00 2px'>Hello world</p>", 120, 40);
        int lines = 0;
        for (int y = 0; y < dbl.Height; y++)
        {
            int n = 0;
            for (int x = 0; x < dbl.Width; x++)
            {
                (byte r, byte g, byte b) = Px(dbl, x, y);
                n += IsRed(r, g, b) ? 1 : 0;
            }

            lines += n >= 20 ? 1 : 0;
        }

        Assert.Equal(4, lines);
    }

    [Fact]
    public void TextShadowOffsetsACopyOfTheGlyphs()
    {
        const string Style = "margin:4px;font:32px Liberation Sans;color:#f00;";
        RgbaImage plain = Render("<p style='" + Style + "'>HH</p>", 80, 50);
        RgbaImage shadow = Render("<p style='" + Style + "text-shadow:6px 0 #00f'>HH</p>", 80, 50);
        Assert.Equal(0, CountBlue(plain));
        Assert.True(CountBlue(shadow) > 20);
        RgbaImage blurred = Render("<p style='" + Style + "text-shadow:0 0 4px #00f'>HH</p>", 80, 50);
        Assert.True(CountBlue(blurred) > 20);
    }

    private static int CountBlue(RgbaImage img)
    {
        int n = 0;
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            n += img.Pixels[i + 2] - img.Pixels[i] > 40 ? 1 : 0;
        }

        return n;
    }

    [Theory]
    [InlineData("disc")]
    [InlineData("circle")]
    [InlineData("square")]
    [InlineData("decimal")]
    [InlineData("lower-roman")]
    [InlineData("upper-alpha")]
    public void MarkersPaintLeftOfTheItem(string type)
    {
        RgbaImage img = Render("<ul style='margin:0;padding-left:40px;list-style:" + type + "'><li style='font:16px Liberation Sans'>item</li></ul>", 100, 30);
        bool marker = false;
        for (int y = 0; y < 30; y++)
        {
            for (int x = 10; x < 38; x++)
            {
                (byte r, _, _) = Px(img, x, y);
                marker |= r < 128;
            }
        }

        Assert.True(marker, type + " marker missing");
        bool left = false;
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                (byte r, _, _) = Px(img, x, y);
                left |= r < 250;
            }
        }

        Assert.False(left);
    }

    // ---------------------------------------------------------------- replaced content

    [Fact]
    public void ObjectFitModes()
    {
        Func<string, byte[]?> res = Resources(("two.png", Png(2, 1, R, B)));
        string Img(string fit) => "<img src='two.png' style='display:block;width:40px;height:40px;image-rendering:pixelated;object-fit:" + fit + "'>";
        RgbaImage fill = Render(Img("fill"), resources: res);
        Near(Red, fill, 5, 5);
        Near(Blue, fill, 35, 35);
        RgbaImage contain = Render(Img("contain"), resources: res);
        Near(White, contain, 20, 3);
        Near(Red, contain, 10, 20);
        Near(Blue, contain, 30, 20);
        RgbaImage cover = Render(Img("cover"), resources: res);
        Near(Red, cover, 5, 5);
        Near(Blue, cover, 35, 5);
        RgbaImage none = Render(Img("none"), resources: res);
        Near(White, none, 5, 5);
        RgbaImage scale = Render(Img("scale-down"), resources: res);
        Near(White, scale, 20, 3);
        RgbaImage pos = Render("<img src='two.png' style='display:block;width:40px;height:40px;image-rendering:pixelated;object-fit:none;object-position:0 0'>", resources: res);
        Near(Red, pos, 0, 0, 3);
        Near(Blue, pos, 1, 0, 3);
    }

    // ---------------------------------------------------------------- pixel snapping

    [Fact]
    public void AdjacentFractionalBoxesLeaveNoSeam()
    {
        // Two floats at x=0 and x=10.5: the shared edge snaps to one device pixel, so no column is a blend of red and white.
        RgbaImage img = Render("<div style='float:left;width:10.5px;height:10px;background:#f00'></div><div style='float:left;width:10.5px;height:10px;background:#f00'></div>", 40, 12);
        for (int x = 0; x < 21; x++)
        {
            Near(Red, img, x, 5, 0);
        }

        Near(White, img, 21, 5, 0);
        RgbaImage vertical = Render("<div style='height:10.4px;background:#f00'></div><div style='height:10.4px;background:#f00'></div>", 8, 24);
        for (int y = 0; y < 21; y++)
        {
            Near(Red, vertical, 3, y, 0);
        }
    }

    [Fact]
    public void BorderWidthsSnapToWholeDevicePixels()
    {
        RgbaImage img = Render("<div style='width:20px;height:20px;border:1.6px solid #f00'></div>", 40, 40);
        // Chrome floors a border width to whole device pixels, at least one: 1.6px paints as 1px.
        Near(Red, img, 0, 10, 0);
        Near(White, img, 1, 10, 0);
        RgbaImage thin = Render("<div style='width:20px;height:20px;border:0.4px solid #f00'></div>", 40, 40);
        Near(Red, thin, 0, 10, 0);
    }

    [Fact]
    public void Snapping2x()
    {
        RgbaImage img = Render("<div style='width:10.25px;height:10px;background:#f00'></div>", 20, 12, dpr: 2f);
        Near(Red, img, 20, 5, 0);
        Near(White, img, 21, 5, 0);
    }

    // ---------------------------------------------------------------- golden scenes

    [Fact]
    public void GoldenBackgroundsAndBorders()
    {
        Func<string, byte[]?> res = Resources(("b.png", Png(3, 3, [R, B, R, B, Clear, B, R, B, R])), ("two.png", Png(2, 1, R, B)));
        RgbaImage img = Render(
            "<div style='margin:4px;width:40px;height:30px;background:radial-gradient(circle at 30% 30%,#fff,#06f 60%,#003),repeating-linear-gradient(45deg,#fc0 0 4px,#c60 4px 8px);border:4px solid;border-image:url(b.png) 1/4px round;box-shadow:4px 4px 6px #888,inset 0 0 8px #0006'></div>" +
            "<div style='margin:4px;width:60px;height:24px;border-radius:12px;background:url(two.png) 0 0/12px 6px space,#cfc;outline:2px dashed #909;outline-offset:2px'></div>",
            72, 96, resources: res);
        Golden.Check("paint2-backgrounds", img);
    }

    [Fact]
    public void GoldenTextPaint()
    {
        RgbaImage img = Render(
            "<p style='margin:2px 4px;font:16px Liberation Sans;text-decoration:underline wavy #f00;text-shadow:2px 2px 2px #00f'>Wavy shadow</p>" +
            "<p style='margin:2px 4px;font:16px Liberation Sans;text-decoration:line-through double #080 2px'>Struck out</p>" +
            "<ol style='margin:2px;padding-left:30px;font:14px Liberation Sans'><li>one</li><li>two</li></ol>" +
            "<ul style='margin:2px;padding-left:30px;font:14px Liberation Sans;list-style:square'><li>sq</li></ul>",
            140, 130);
        Golden.Check("paint2-text", img);
    }
}
