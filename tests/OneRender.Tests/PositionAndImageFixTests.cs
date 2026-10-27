using System.IO;
using OneImage;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Style;

namespace OneRender.Tests;

/// <summary>
/// The static position of an inline-level absolutely positioned box, the lh unit and image-orientation, each pinned by the
/// reftests that found them (css-position static-position, css-images line-height units and orientation of masks).
/// </summary>
public class PositionAndImageFixTests
{
    private const string Head = "<!DOCTYPE html><html><body style='margin:0'>";

    private static RgbaImage Render(string body, int w = 120, int h = 120) =>
        HtmlRender.ToImage(Head + body, new HtmlRenderOptions { Width = w, Height = h });

    private static (int R, int G, int B) Px(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    private static void AllGreen(RgbaImage img, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y += 3)
        {
            for (int x = x0; x < x1; x += 3)
            {
                (int r, int g, int b) = Px(img, x, y);
                Assert.True(r < 20 && g > 100 && b < 20, "pixel (" + x + "," + y + ") is " + r + "," + g + "," + b);
            }
        }
    }

    private static string Container(string dir, string align, string floatSide, string transform) =>
        "<style>#container{position:relative;background:red;width:100px;height:100px}#container>div{background:green}#inflow{height:50px}"
        + "#float{float:left;width:50px;height:50px}#abs{display:inline;position:absolute;width:50px;height:50px}</style>"
        + "<div id=container style='direction:" + dir + ";text-align:" + align + "'><div id=inflow></div><div id=float style='float:" + floatSide
        + "'></div><div id=abs style='transform:" + transform + "'></div></div>";

    [Theory]
    [InlineData("ltr", "center", "right", "translateX(-50%)")]
    [InlineData("ltr", "right", "right", "translateX(-100%)")]
    [InlineData("rtl", "center", "left", "translateX(50%)")]
    [InlineData("rtl", "left", "left", "translateX(100%)")]
    public void AnInlineLevelAbsoluteBoxFollowsTextAlignForItsStaticPosition(string dir, string align, string floatSide, string transform)
    {
        // The box sits where it would on a line of the container, aligned in the whole content box (floats do not narrow it),
        // so after the opposite translation it lands beside the float and the container's red never shows.
        RgbaImage img = Render(Container(dir, align, floatSide, transform));
        AllGreen(img, 1, 1, 99, 99);
    }

    [Fact]
    public void ABlockLevelAbsoluteBoxKeepsTheStartEdge()
    {
        RgbaImage img = Render("<div style='position:relative;width:100px;height:20px;text-align:center'><div style='position:absolute;width:20px;height:20px;background:#0a0'></div></div>");
        (int r, int g, _) = Px(img, 5, 5);
        Assert.True(r < 20 && g > 120, "a box that was block-level starts at the container's start edge");
        (r, g, _) = Px(img, 50, 5);
        Assert.True(g > 200 && r > 200, "and is not centered");
    }

    [Fact]
    public void TheLhUnitIsTheElementsLineHeight()
    {
        RgbaImage img = Render("<div style='line-height:30px;width:1lh;height:10px;background:#0a0'></div><div style='line-height:2;font-size:20px;width:1lh;height:10px;background:#0a0'></div>");
        Assert.True(Px(img, 25, 5).G > 120 && Px(img, 35, 5).G > 200, "1lh is 30px");
        Assert.True(Px(img, 35, 15).G > 120 && Px(img, 45, 15).G > 200, "1lh is 40px for line-height 2 at 20px");
    }

    [Fact]
    public void AMaskPositionInLhUsesTheElementsLineHeight()
    {
        // The wedge of a conic mask is anchored at 1lh: the same picture comes out whether the position is written in lh or px.
        string a = "<div style='width:60px;height:60px;line-height:30px;background:#00f;mask-image:conic-gradient(from 45deg at 1lh 20px,#000 25%,transparent 0)'></div>";
        string b = "<div style='width:60px;height:60px;line-height:30px;background:#00f;mask-image:conic-gradient(from 45deg at 30px 20px,#000 25%,transparent 0)'></div>";
        Assert.Equal(Render(a).Pixels, Render(b).Pixels);
    }

    private static byte[] Jpeg()
    {
        var px = new Rgba32[16 * 16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                px[(y * 16) + x] = x < 8 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 0, 255, 255);
            }
        }

        OneImage.Image img = OneImage.Image.FromPixels<Rgba32>(px, 16, 16);
        using var ms = new MemoryStream();
        img.Save(ms, OneImage.JpegFormat.Instance);
        byte[] jpeg = ms.ToArray();

        // An EXIF APP1 segment right after SOI with one entry, Orientation (0x0112) = 2: mirrored horizontally.
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0, 0x2A, 0, 0, 0, 8,
            0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 2, 0, 0,
            0, 0, 0, 0,
        ];
        byte[] result = new byte[jpeg.Length + exif.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        exif.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + exif.Length));
        return result;
    }

    [Fact]
    public void ImageOrientationNoneKeepsTheStoredPixels()
    {
        byte[] bytes = Jpeg();
        var store = new ImageStore(_ => bytes);
        Assert.True(store.TryGetImage("a.jpg", false, out OneRender.Painting.ImageInfo upright));
        Assert.True(store.TryGetImage("a.jpg", true, out OneRender.Painting.ImageInfo stored));
        RgbaImage u = store.Resolve(upright.Handle)!, s = store.Resolve(stored.Handle)!;
        Assert.True(u.Pixels[(4 * 4)] < 100 && u.Pixels[(4 * 4) + 2] > 150, "from-image: the mirrored file shows its blue half on the left");
        Assert.True(s.Pixels[(4 * 4)] > 150 && s.Pixels[(4 * 4) + 2] < 100, "none: the stored left half is red");
    }

    [Theory]
    [InlineData("none", "none")]
    [InlineData("from-image", "from-image")]
    public void ImageOrientationParsesAndInherits(string css, string expected)
    {
        var tree = new OneRender.Tree.StyledTree();
        tree.Styles.SetViewport(800, 600);
        var d = new OneRender.Style.StyleDeclaration();
        Assert.True(d.TrySet("image-orientation", css));
        OneRender.Tree.StyledNode root = tree.AddRoot("html", d);
        OneRender.Tree.StyledNode child = tree.AddElement(root, "div");
        ComputedValues v = child.Values;
        Assert.Equal(expected, OneRender.Style.ComputedValueText.Get(OneRender.Style.PropertyId.ImageOrientation, in v));
    }
}
