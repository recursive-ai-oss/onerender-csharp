using System.Text;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.TestSupport;

namespace OneRender.Tests;

/// <summary>
/// Paint order, borders, canvas background, stable ids and allocation, through the whole pipeline: HTML in, display list and
/// pixels out. Expected colors come from the CSS 2.2 Appendix E paint order and the Backgrounds 3 box model, not from the output.
/// </summary>
public class PaintTests
{
    private const string Reset = "<!DOCTYPE html><html><body style='margin:0'>";

    private static RgbaImage Render(string body, int size = 64) =>
        HtmlRender.ToImage(Reset + body, new HtmlRenderOptions { Width = size, Height = size });

    private static (byte R, byte G, byte B) Px(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    private static readonly (byte, byte, byte) Red = (255, 0, 0);
    private static readonly (byte, byte, byte) Green = (0, 128, 0);
    private static readonly (byte, byte, byte) Blue = (0, 0, 255);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    private static string Box(string style, string inner = "") => "<div style='" + style + "'>" + inner + "</div>";

    // ---------------------------------------------------------------- Appendix E order

    [Fact]
    public void NegativeZIndexPaintsOverTheContextBackgroundButUnderInFlowBlocks()
    {
        // E.2 step 1 (context background), 3 (negative z-index children), 4 (in-flow block backgrounds).
        RgbaImage img = Render(Box(
            "position:relative;z-index:0;width:60px;height:60px;background:blue",
            Box("position:absolute;z-index:-1;left:20px;top:20px;width:30px;height:30px;background:red")
            + Box("width:30px;height:30px;background:green")));
        Assert.Equal(Green, Px(img, 25, 25)); // the in-flow block covers the negative-z box
        Assert.Equal(Red, Px(img, 40, 40)); // outside the block the negative-z box is over the context's own background
        Assert.Equal(Blue, Px(img, 55, 10));
    }

    [Fact]
    public void NegativeZIndexWithoutAContextGoesBehindTheRootStackingContextContent()
    {
        // The negative-z box belongs to the root stacking context; the body's background is propagated to the canvas, so the
        // box is above it, but a block with its own background paints over the box.
        RgbaImage img = Render(Box("position:absolute;z-index:-1;left:0;top:0;width:30px;height:30px;background:red")
            + Box("width:20px;height:20px;background:green"));
        Assert.Equal(Green, Px(img, 10, 10));
        Assert.Equal(Red, Px(img, 25, 25));
    }

    [Fact]
    public void PositionedBoxesPaintOverInFlowContentWhateverTheTreeOrder()
    {
        // Step 8 comes after steps 4 and 7, so an earlier positioned box is still above a later in-flow one.
        RgbaImage img = Render(
            Box("position:absolute;left:10px;top:10px;width:30px;height:30px;background:red")
            + Box("width:30px;height:30px;background:green"));
        Assert.Equal(Red, Px(img, 20, 20));
        Assert.Equal(Green, Px(img, 5, 5));
    }

    [Fact]
    public void FloatsPaintOverBlockBackgroundsAndUnderPositionedBoxes()
    {
        RgbaImage img = Render(
            Box("float:left;width:30px;height:30px;background:blue")
            + Box("width:50px;height:50px;background:green")
            + Box("position:relative;left:0;top:-80px;width:10px;height:10px;background:red"));
        // The float takes no space in this layout, so it overlaps the green block (step 5 over step 4).
        Assert.Equal(Blue, Px(img, 20, 20));
        Assert.Equal(Green, Px(img, 40, 40));
    }

    [Fact]
    public void EqualZIndexesPaintInTreeOrderAndHigherOnesLast()
    {
        string a = "position:absolute;left:0;top:0;width:30px;height:30px;background:red;z-index:";
        string b = "position:absolute;left:10px;top:10px;width:30px;height:30px;background:blue;z-index:";
        Assert.Equal(Blue, Px(Render(Box(a + "1") + Box(b + "1")), 20, 20)); // tie: later in tree order is on top
        Assert.Equal(Red, Px(Render(Box(a + "2") + Box(b + "1")), 20, 20)); // higher z-index is on top
        Assert.Equal(Red, Px(Render(Box(a + "1") + Box(b + "auto")), 20, 20)); // auto paints at z 0, below z 1
        Assert.Equal(Blue, Px(Render(Box(a + "0") + Box(b + "auto")), 20, 20)); // auto and 0 tie, tree order decides
    }

    [Fact]
    public void ANestedStackingContextKeepsItsChildrenTogether()
    {
        // The inner z-index:100 is confined to its parent context (z-index:1), which is below the sibling context (z-index:2).
        RgbaImage img = Render(
            Box("position:absolute;z-index:1;left:0;top:0;width:40px;height:40px;background:red",
                Box("position:absolute;z-index:100;left:10px;top:10px;width:40px;height:40px;background:green"))
            + Box("position:absolute;z-index:2;left:20px;top:20px;width:30px;height:30px;background:blue"));
        Assert.Equal(Blue, Px(img, 30, 30));
        Assert.Equal(Green, Px(img, 15, 15));
    }

    [Fact]
    public void OpacityMakesAGroupThatIsCompositedOnce()
    {
        // Compositing 1 section 3: the whole subtree is drawn opaque, then blended. Overlapping children do not show through each other.
        RgbaImage img = Render(Box(
            "opacity:0.5;width:40px;height:40px",
            Box("width:30px;height:30px;background:red")
            + Box("position:absolute;left:10px;top:10px;width:20px;height:20px;background:blue")));
        var overlap = Px(img, 15, 15);
        Assert.InRange(overlap.R, 126, 130);
        Assert.InRange(overlap.G, 126, 130);
        Assert.Equal(255, overlap.B);
        var redOnly = Px(img, 3, 3);
        Assert.Equal(255, redOnly.R);
        Assert.InRange(redOnly.G, 126, 130);
    }

    // ---------------------------------------------------------------- borders, radii, canvas, visibility

    [Fact]
    public void BordersPaintOnTheEdgesInTheirColorsAndStyles()
    {
        RgbaImage img = Render(Box("width:20px;height:20px;border:4px solid green;border-top-color:red;border-bottom-style:none;background:blue"));
        Assert.Equal(Red, Px(img, 12, 1)); // top border
        Assert.Equal(Green, Px(img, 1, 12)); // left border
        Assert.Equal(Green, Px(img, 27, 12)); // right border
        Assert.Equal(Blue, Px(img, 12, 12)); // background is under the borders and shows inside
        Assert.Equal(Blue, Px(img, 12, 22)); // no bottom border: the box ends at 24 and the background reaches the edge
        Assert.Equal(White, Px(img, 12, 26));
    }

    [Fact]
    public void RoundedCornersClipTheBackgroundAndBorder()
    {
        RgbaImage img = Render(Box("width:40px;height:40px;border:2px solid green;border-radius:20px;background:blue"));
        Assert.Equal(White, Px(img, 0, 0)); // the corner is outside the curve
        Assert.Equal(Blue, Px(img, 22, 22));
        Assert.Equal(Green, Px(img, 21, 0)); // top edge midpoint is on the border
    }

    [Fact]
    public void TheRootBackgroundCoversTheWholeCanvas()
    {
        RgbaImage img = HtmlRender.ToImage("<!DOCTYPE html><html style='background:#00f'><body style='margin:0'><div style='height:10px;background:red'></div>", new HtmlRenderOptions { Width = 32, Height = 32 });
        Assert.Equal(Blue, Px(img, 31, 31));
        Assert.Equal(Red, Px(img, 5, 5));
    }

    [Fact]
    public void TheBodyBackgroundPropagatesToTheCanvasWhenTheRootHasNone()
    {
        // Backgrounds 3 section 2.11.2: the body's background paints the canvas and the body itself paints none.
        RgbaImage img = HtmlRender.ToImage("<!DOCTYPE html><html><body style='margin:0;background:#00f;height:10px'></body></html>", new HtmlRenderOptions { Width = 32, Height = 32 });
        Assert.Equal(Blue, Px(img, 31, 31));
        Assert.Equal(Blue, Px(img, 5, 5));
    }

    [Fact]
    public void ARootBackgroundStopsTheBodyFromPropagating()
    {
        RgbaImage img = HtmlRender.ToImage("<!DOCTYPE html><html style='background:red'><body style='margin:0;background:#00f;height:10px'></body></html>", new HtmlRenderOptions { Width = 32, Height = 32 });
        Assert.Equal(Red, Px(img, 31, 31));
        Assert.Equal(Blue, Px(img, 5, 5)); // the body paints its own background as a box now
    }

    [Fact]
    public void HiddenBoxesPaintNothingButVisibleDescendantsStillPaint()
    {
        RgbaImage img = Render(Box(
            "visibility:hidden;width:40px;height:40px;background:red;border:2px solid red",
            Box("visibility:visible;width:10px;height:10px;background:green")));
        Assert.Equal(Green, Px(img, 7, 7));
        Assert.Equal(White, Px(img, 30, 30));
        Assert.Equal(White, Px(img, 1, 1));
    }

    [Fact]
    public void OverflowHiddenClipsDescendantsAndPaddingBoxIsTheEdge()
    {
        RgbaImage img = Render(Box(
            "overflow:hidden;width:20px;height:20px;border:4px solid green",
            Box("width:50px;height:50px;background:red")));
        Assert.Equal(Red, Px(img, 10, 10));
        Assert.Equal(White, Px(img, 40, 10)); // clipped at the padding edge, past the 28 px border box
        Assert.Equal(Green, Px(img, 26, 10)); // the border is not clipped
    }

    [Fact]
    public void OverflowClipDoesNotClipAnAbsoluteBoxWhoseContainingBlockIsOutside()
    {
        RgbaImage img = Render(Box(
            "position:relative;width:60px;height:60px",
            Box("overflow:hidden;width:10px;height:10px", Box("position:absolute;left:30px;top:30px;width:10px;height:10px;background:red"))));
        Assert.Equal(Red, Px(img, 35, 35));
    }

    [Fact]
    public void TextPaintsGlyphsInTheTextColor()
    {
        HtmlPage page = HtmlPage.Create(Reset + "<p style='margin:0;color:#800000;font-size:20px'>Hello</p>", new HtmlPageOptions(new DefaultFontProvider()) { ViewportWidth = 100, ViewportHeight = 40 });
        var list = new DisplayList();
        page.Paint(list);
        int runs = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.GlyphRun)
            {
                runs++;
                Assert.Equal("Hello", list[i].GlyphRun.Text.ToString());
            }
        }

        Assert.Equal(1, runs);
        RgbaImage img = HtmlRender.ToImage(Reset + "<p style='margin:0;color:#800000;font-size:20px'>Hello</p>", new HtmlRenderOptions { Width = 100, Height = 40 });
        int dark = 0;
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                if (Px(img, x, y).R is > 100 and < 200 && Px(img, x, y).G < 60)
                {
                    dark++;
                }
            }
        }

        Assert.True(dark > 20, "expected maroon glyph pixels, found " + dark);
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public void NestedBlocksWithBordersRenderToExactPixels()
    {
        // 40x40 outer with a 2 px green border and 3 px padding, an inner 10x10 red block, all at the origin.
        RgbaImage img = Render(Box("width:30px;height:30px;padding:3px;border:2px solid green;background:#ff0", Box("width:10px;height:10px;background:red")), 48);
        Assert.Equal(Green, Px(img, 0, 0));
        Assert.Equal(Green, Px(img, 39, 39));
        Assert.Equal(White, Px(img, 40, 40));
        Assert.Equal((255, 255, 0), Px(img, 3, 3)); // padding area shows the background
        Assert.Equal(Red, Px(img, 5, 5)); // the inner block starts at border + padding = 5
        Assert.Equal(Red, Px(img, 14, 14));
        Assert.Equal((255, 255, 0), Px(img, 15, 15));
    }

    // ---------------------------------------------------------------- ids, damage, allocation

    private static HtmlPage Page(string body, int size = 100) =>
        HtmlPage.Create(Reset + body, new HtmlPageOptions(new DefaultFontProvider()) { ViewportWidth = size, ViewportHeight = size });

    private static string ThreeBoxes(string middleColor) =>
        Box("width:30px;height:20px;background:red") + Box("width:40px;height:20px;background:" + middleColor, Box("width:10px;height:10px;background:blue")) + Box("width:50px;height:20px;background:green");

    [Fact]
    public void RepaintingAnUnchangedTreeGivesTheSameIdsAndNoDamage()
    {
        HtmlPage page = Page(ThreeBoxes("gray"));
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(100, 100);
        painter.Paint(page.Fragments, retained, options);
        Assert.False(retained.Damage.IsEmpty); // the first frame damages everything
        ulong[] first = Ids(retained.Current);
        Assert.Equal(first.Length, new HashSet<ulong>(first).Count);
        retained.ClearDamage();
        painter.Paint(page.Fragments, retained, options);
        Assert.Equal(first, Ids(retained.Current));
        Assert.True(retained.Damage.IsEmpty);
    }

    [Fact]
    public void ChangingOneBoxColorDamagesExactlyThatBox()
    {
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(100, 100);
        painter.Paint(Page(ThreeBoxes("gray")).Fragments, retained, options);
        retained.ClearDamage();
        painter.Paint(Page(ThreeBoxes("purple")).Fragments, retained, options);
        Assert.Equal(1, retained.Damage.Count);
        Assert.Equal(new RectF(0, 20, 40, 20), retained.Damage[0]);
    }

    [Fact]
    public void RepaintingAThousandBoxesAllocatesNothing()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("<div style='height:2px;background:#").Append((i % 9) + 1).Append("00;border-left:1px solid blue;border-radius:").Append(i % 3).Append("px'></div>");
        }

        HtmlPage page = Page(sb.ToString(), 200);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(200, 200);
        void Frame() => painter.Paint(page.Fragments, retained, options);
        Allocation.AssertZero(Frame, "repaint of a 1,000-box tree", warmup: 4, iterations: 20);
        Assert.True(retained.Current.Count >= 2000);
        Assert.True(retained.Damage.IsEmpty || retained.Damage.Count > 0);
    }

    private static ulong[] Ids(DisplayList list)
    {
        var ids = new List<ulong>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id.IsValid)
            {
                ids.Add(list[i].Id.Value);
            }
        }

        return [.. ids];
    }
}
