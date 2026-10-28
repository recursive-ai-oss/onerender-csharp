using OneRender.Forms;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.TestSupport;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// The track and thumb of an <c>input type=range</c> are boxes laid out and painted from the cascade (<c>::slider-track</c>,
/// <c>::slider-thumb</c>, CSS Forms 1 §4.2). The expected numbers were read off Chrome 156's rendering of the same markup.
/// </summary>
public class RangePartTests
{
    private const string Custom =
        "input.c{-webkit-appearance:none;appearance:none;background:transparent;margin:0;width:129px}" +
        "input.c::-webkit-slider-runnable-track{height:8px;background:#ccc;border-radius:4px}" +
        "input.c::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:16px;height:16px;border-radius:50%;background:red;margin-top:-4px}";

    private static InteractivePage Page(string body, string css = Custom, int w = 400, int h = 300) => Ix.Page(body, w, h, css);

    private static (StyledNode Track, StyledNode Thumb) Parts(InteractivePage p, string id)
    {
        StyledNode input = p.Page.Styled.FindBySource(Ix.Id(p, id).Index);
        StyledNode container = input.FirstChild;
        Assert.Equal(StyledNodeKind.SliderContainer, container.Kind);
        StyledNode track = container.FirstChild;
        Assert.Equal(StyledNodeKind.SliderTrack, track.Kind);
        StyledNode thumb = track.FirstChild;
        Assert.Equal(StyledNodeKind.SliderThumb, thumb.Kind);
        return (track, thumb);
    }

    private static Layout.Rect Of(InteractivePage p, StyledNode n) => p.Layout.GetBoundingClientRect(n);

    [Fact]
    public void AStyledRangeIsAsHighAsItsTrackAndTheThumbSitsOnTheTrackTop()
    {
        // Chrome 156: the input is 129 x 8; the track fills it; a 16 px thumb with margin-top -4 is centered on the track and, at 50%,
        // sits half the room (129 - 16) in.
        InteractivePage p = Page("<input id=r class=c type=range value=50>");
        Layout.Rect input = p.Layout.GetBoundingClientRect(Ix.Id(p, "r"));
        (StyledNode track, StyledNode thumb) = Parts(p, "r");
        Layout.Rect t = Of(p, track), th = Of(p, thumb);
        Assert.Equal(129, input.Width);
        Assert.Equal(8, input.Height);
        Assert.Equal(input.X, t.X);
        Assert.Equal(129, t.Width);
        Assert.Equal(8, t.Height);
        Assert.Equal(input.X + 56.5, th.X, 3);
        Assert.Equal(input.Y - 4, th.Y, 3);
        Assert.Equal(16, th.Width);
        Assert.Equal(16, th.Height);
    }

    [Fact]
    public void ThumbPositionFollowsTheValueAndTheTrackContentBox()
    {
        // Chrome 156: a track with a 4 px border and 3 px padding gives the thumb 129 - 14 = 115 px; a 20 px thumb at 0% and 100%.
        InteractivePage p = Page(
            "<input id=a class=c type=range value=0><br><input id=b class=c type=range value=100>",
            Custom + "input.c::-webkit-slider-runnable-track{border:4px solid blue;padding:3px;height:8px}input.c::-webkit-slider-thumb{width:20px;margin-top:0}");
        Layout.Rect ia = p.Layout.GetBoundingClientRect(Ix.Id(p, "a")), ib = p.Layout.GetBoundingClientRect(Ix.Id(p, "b"));
        Assert.Equal(ia.X + 7, Of(p, Parts(p, "a").Thumb).X, 3);
        Assert.Equal(ib.X + 7 + 95, Of(p, Parts(p, "b").Thumb).X, 3);
        // The track is a border box: 8 px of height cannot hold 14 px of border and padding, so it is 14 px.
        Assert.Equal(14, Of(p, Parts(p, "a").Track).Height);
    }

    [Fact]
    public void MarginsMoveTheThumbWithoutChangingItsTravel()
    {
        // Chrome 156: margin-left 5 shifts the thumb by 5; the room is the track width minus the thumb's border box only.
        InteractivePage p = Page("<input id=r class=c type=range value=50>", Custom + "input.c::-webkit-slider-thumb{width:20px;margin-left:5px;margin-right:9px;margin-top:0}");
        Layout.Rect input = p.Layout.GetBoundingClientRect(Ix.Id(p, "r"));
        Assert.Equal(input.X + 5 + ((129 - 20) / 2.0), Of(p, Parts(p, "r").Thumb).X, 3);
    }

    [Fact]
    public void ARightToLeftRangeRunsTheOtherWay()
    {
        InteractivePage p = Page("<input id=r class=c type=range value=20 style='direction:rtl'>");
        Layout.Rect input = p.Layout.GetBoundingClientRect(Ix.Id(p, "r"));
        // Chrome 156: min is at the right, so 20% from the start edge sits 80% of the room from the left.
        Assert.Equal(input.X + (0.8 * (129 - 16)), Of(p, Parts(p, "r").Thumb).X, 3);
    }

    [Fact]
    public void ThePartsOfANativeRangeAreSixteenPixelsWhateverTheAuthorSays()
    {
        // Chrome 156: with the native look the thumb ignores its own size, and the input is 16 px high.
        InteractivePage p = Page(
            "<input id=r type=range value=50>",
            "input::-webkit-slider-thumb{width:30px;height:30px;background:red}input{margin:0}");
        Layout.Rect input = p.Layout.GetBoundingClientRect(Ix.Id(p, "r"));
        Layout.Rect th = Of(p, Parts(p, "r").Thumb);
        Assert.Equal(16, input.Height);
        Assert.Equal(16, th.Width);
        Assert.Equal(16, th.Height);
        Assert.Equal(ControlKind.RangeThumb, Parts(p, "r").Thumb.Values.Control);
    }

    [Fact]
    public void ATrackStyledOnANativeRangeSetsItsHeight()
    {
        // Chrome 156: height 20 on the track makes the input 20 high with the native thumb on top of the track.
        InteractivePage p = Page("<input id=r type=range value=50>", "input{margin:0}input::-webkit-slider-runnable-track{height:20px;background:#ccc}");
        Assert.Equal(20, p.Layout.GetBoundingClientRect(Ix.Id(p, "r")).Height);
    }

    [Fact]
    public void AnAuthorThumbOnAnAppearanceNoneRangeKeepsItsSize()
    {
        InteractivePage p = Page(
            "<input id=r type=range value=50>",
            "input{-webkit-appearance:none;appearance:none;margin:0}input::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:30px;height:12px;background:red}");
        (_, StyledNode thumb) = Parts(p, "r");
        Assert.Equal(30, Of(p, thumb).Width);
        Assert.Equal(12, Of(p, thumb).Height);
        Assert.NotEqual(ControlKind.RangeThumb, thumb.Values.Control);
    }

    [Fact]
    public void AnUnstyledRangeKeepsItsNativeSize()
    {
        InteractivePage p = Page("<input id=r type=range>", "");
        Layout.Rect r = p.Layout.GetBoundingClientRect(Ix.Id(p, "r"));
        Assert.Equal(129, r.Width);
        Assert.Equal(16, r.Height);
    }

    [Fact]
    public void ThePointerMovesTheThumbAsLaidOut()
    {
        // A 40 px thumb on a 200 px track: the value 0..100 maps the pointer from 20 px to 180 px in.
        InteractivePage p = Page(
            "<input id=r class=c type=range value=50 style='width:200px'>",
            Custom + "input.c::-webkit-slider-thumb{width:40px;margin-top:0}");
        Node r = Ix.Id(p, "r");
        Layout.Rect box = p.Layout.GetBoundingClientRect(r);
        p.HandlePointer(Ix.Down(box.X + 20, box.Y + 4, 1));
        Assert.Equal(0, p.GetRangeValue(r));
        p.HandlePointer(Ix.Move(box.X + 100, box.Y + 4, 2, MouseButtons.Primary));
        Assert.Equal(50, p.GetRangeValue(r));
        p.HandlePointer(Ix.Move(box.X + 180, box.Y + 4, 3, MouseButtons.Primary));
        Assert.Equal(100, p.GetRangeValue(r));
        p.HandlePointer(Ix.Up(box.X + 180, box.Y + 4, 4));
        Layout.Rect th = Of(p, Parts(p, "r").Thumb);
        Assert.Equal(box.X + 160, th.X, 3);
    }

    [Fact]
    public void APointerPressOnTheThumbOfAStyledRangeTargetsTheInput()
    {
        InteractivePage p = Page("<input id=r class=c type=range value=50>");
        (_, StyledNode thumb) = Parts(p, "r");
        Layout.Rect th = Of(p, thumb);
        Assert.Equal("r", Ix.At(p, th.X + 8, th.Y + 8));
    }

    // ---- states reach the parts, in place equal to built again

    private const string StateCss =
        Custom + "input.c:hover::-webkit-slider-thumb{background:blue;width:24px;height:24px}" +
        "input.c:focus::-webkit-slider-runnable-track{background:#fc0}input.c:disabled::-webkit-slider-thumb{background:#999}" +
        "input.c:disabled{opacity:.5}input.c:active::-webkit-slider-thumb{margin-top:-8px}";

    private const string StateBody =
        "<div style='margin:6px'><input id=a class=c type=range value=30></div><div style='margin:6px'><input id=b class=c type=range value=70 disabled></div>" +
        "<div style='margin:6px'><input id=c type=range value=10 style='width:200px'></div><button id=btn>x</button>";

    private static string Describe(InteractivePage p) => p.Page.Styled.Dump() + "\n--\n" + p.Page.Boxes.Dump() + "\n--\n" + p.Page.Fragments.Dump();

    [Fact]
    public void HoverFocusAndDragOnStyledRangesEqualAPageBuiltAgain()
    {
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300 };
        string html = Ix.Head.Replace("</style>", StateCss + "</style>", StringComparison.Ordinal) + StateBody + "</body></html>";
        for (int seed = 1; seed <= 6; seed++)
        {
            InteractivePage fast = InteractivePage.Create(html, options);
            InteractivePage slow = InteractivePage.Create(html, options);
            slow.DisableInPlaceRestyle = true;
            var rng = new Random(seed);
            double t = 0;
            for (int i = 0; i < 80; i++)
            {
                t += 16;
                string what;
                int kind = rng.Next(10);
                double x = rng.Next(260), y = rng.Next(120);
                if (kind < 5)
                {
                    what = $"move {x},{y}";
                    fast.HandlePointer(Ix.Move(x, y, t));
                    slow.HandlePointer(Ix.Move(x, y, t));
                }
                else if (kind < 8)
                {
                    what = $"press {x},{y}";
                    fast.HandlePointer(Ix.Down(x, y, t));
                    slow.HandlePointer(Ix.Down(x, y, t));
                    double x2 = rng.Next(260);
                    fast.HandlePointer(Ix.Move(x2, y, t + 1, MouseButtons.Primary));
                    slow.HandlePointer(Ix.Move(x2, y, t + 1, MouseButtons.Primary));
                    fast.HandlePointer(Ix.Up(x2, y, t + 2));
                    slow.HandlePointer(Ix.Up(x2, y, t + 2));
                }
                else if (kind < 9)
                {
                    what = "tab";
                    fast.HandleKey(Ix.Key("Tab"));
                    slow.HandleKey(Ix.Key("Tab"));
                }
                else
                {
                    what = "arrow";
                    fast.HandleKey(Ix.Key("ArrowRight"));
                    slow.HandleKey(Ix.Key("ArrowRight"));
                }

                string a = Describe(fast), b = Describe(slow);
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    string[] x1 = a.Split('\n'), y1 = b.Split('\n');
                    int n = 0;
                    while (n < x1.Length && n < y1.Length && x1[n] == y1[n])
                    {
                        n++;
                    }

                    throw new Xunit.Sdk.XunitException($"seed {seed} step {i} ({what}): line {n}\n in place: {(n < x1.Length ? x1[n] : "(end)")}\n rebuilt:  {(n < y1.Length ? y1[n] : "(end)")}");
                }
            }
        }
    }

    [Fact]
    public void HoverReachesTheThumbAndLeavesIt()
    {
        InteractivePage p = Page("<input id=a class=c type=range value=30>", StateCss);
        Layout.Rect box = p.Layout.GetBoundingClientRect(Ix.Id(p, "a"));
        (_, StyledNode thumb) = Parts(p, "a");
        Assert.Equal(16, Of(p, thumb).Width);
        p.HandlePointer(Ix.Move(box.X + 40, box.Y + 2, 1));
        (_, thumb) = Parts(p, "a");
        Assert.Equal(24, Of(p, thumb).Width);
        p.HandlePointer(Ix.Move(300, 250, 2));
        (_, thumb) = Parts(p, "a");
        Assert.Equal(16, Of(p, thumb).Width);
    }

    // Bytes allocated by `count` rounds of moving the pointer over two ranges and then off them, after a warm-up.
    private static long HoverBytes(string css, out string where)
    {
        InteractivePage p = Page(StateBody + "<div id=d class=hv style='height:20px;width:50px'>x</div>", css);
        Layout.Rect a = p.Layout.GetBoundingClientRect(Ix.Id(p, "a")), c = p.Layout.GetBoundingClientRect(Ix.Id(p, "c"));
        double ax = a.X + 20, ay = a.Y + 2, cx = c.X + 20, cy = c.Y + 2;
        long Round(int count, ref double t)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                p.HandlePointer(Ix.Move(ax, ay, t += 1));
                p.HandlePointer(Ix.Move(cx, cy, t += 1));
                p.HandlePointer(Ix.Move(350, 280, t += 1));
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        double time = 0;
        Round(300, ref time);
        long best = long.MaxValue;
        for (int pass = 0; pass < 3 && best != 0; pass++)
        {
            best = Math.Min(best, Round(100, ref time));
        }

        where = "bytes per 300 hover changes: " + best;
        return best;
    }

    [Fact]
    public void AHoverChangeOverAStyledRangeAllocatesNothingInSteadyState()
    {
        // Parts styled by rules that read no state: the cascade keeps their styles, so moving the pointer over them costs nothing.
        long bytes = HoverBytes(Custom + ".hv:hover{background:#eee}", out string where);
        Assert.True(bytes == 0, where);
    }

    [Fact]
    public void AHoverRuleOnAPartAllocatesNothingInSteadyState()
    {
        // `input:hover::-webkit-slider-thumb` reads the input's state, so each pointer move restyles the parts in place: the cascade reuses
        // its pseudo-element boxes across state changes, and the engine's restyle, layout and paint of the parts allocate nothing either.
        long bytes = HoverBytes(StateCss, out string where);
        Assert.True(bytes == 0, where);
    }

    [Fact]
    public void ThePartsOfAnUnstyledPageNeverRebuildItOnHover()
    {
        // A rule that styles only range parts leaves the page restyling in place; a ::before rule behind a state still builds it again.
        InteractivePage p = Page("<input id=a class=c type=range value=30>", StateCss);
        StyledTree before = p.Page.Styled;
        Layout.Rect box = p.Layout.GetBoundingClientRect(Ix.Id(p, "a"));
        p.HandlePointer(Ix.Move(box.X + 40, box.Y + 2, 1));
        Assert.Same(before, p.Page.Styled);
    }
}
