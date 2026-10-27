using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>Animations on the 1,000-node game-UI page: bytes per frame, relayout, time, and agreement with a fresh render at the same time.</summary>
public partial class MotionGameUiTests
{
    private const string Keyframes =
        "@keyframes spin{to{transform:rotate(360deg)}}@keyframes pulse{from{opacity:.2}to{opacity:1}}" +
        "@keyframes grow{from{width:20px}to{width:70px}}@keyframes tint{from{color:#ff0000}to{color:#0000ff}}" +
        "@keyframes bg{from{background-color:#102030}to{background-color:#a0b0c0}}";

    [GeneratedRegex("id=\"([A-Za-z0-9]+)\"")]
    private static partial Regex IdPattern();

    private static string[] Ids(string html)
    {
        var ids = new List<string>();
        foreach (Match m in IdPattern().Matches(html))
        {
            ids.Add(m.Groups[1].Value);
        }

        return ids.ToArray();
    }

    // Transform and opacity on `compositor` elements; width, color and background on a handful more.
    private static string Hud(int compositor, bool layout)
    {
        string html = GameUiScene.Html();
        string[] ids = Ids(html);
        var css = new StringBuilder(Keyframes);
        for (int i = 0; i < compositor; i++)
        {
            css.Append('#').Append(ids[i]).Append(i % 2 == 0 ? "{animation:spin 2s linear infinite}" : "{animation:pulse 1s ease-in-out infinite alternate}");
        }

        if (layout)
        {
            css.Append("#slot3{animation:grow 1s linear infinite alternate}#slot20{animation:grow 700ms ease-in-out infinite alternate}");
            css.Append("#level{animation:tint 1s linear infinite alternate}#quest3{animation:bg 900ms linear infinite alternate}");
        }

        return html.Replace("</style>", css + "</style>", StringComparison.Ordinal);
    }

    private static HtmlPageOptions Options() => new(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };

    private static InteractivePage Create(string html) => InteractivePage.Create(html, Options(), new InteractionOptions { AnimationStartTime = 0 });

    [Fact]
    public void AHundredTransformAndOpacityAnimationsTickWithoutBytesOrRelayout()
    {
        InteractivePage p = Create(Hud(100, false));
        Assert.InRange(p.Page.Styled.Count, 1000, 2000);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(p.Page.Fragments, retained, new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll });
        p.Advance(0);
        int generation = p.Generation;
        int epoch = p.Page.Boxes.Epoch;
        double t = 0;
        InputOutcome last = InputOutcome.None;
        int patched = 0;

        void Frame()
        {
            t += 16;
            last = p.Advance(t);
            patched = p.PatchAnimations(painter, retained);
        }

        Frame();
        Assert.Equal(InputOutcome.Animated, last);
        Assert.True(patched >= 100, "every animated element is patched: " + patched);
        Allocation.AssertZero(Frame, "advance and patch of 100 running animations on the HUD", warmup: 20, iterations: 100);
        Assert.Equal(InputOutcome.Animated, last);
        Assert.Equal(generation, p.Generation);
        Assert.Equal(epoch, p.Page.Boxes.Epoch);
    }

    [Fact]
    public void AnimatedWidthAndColorTickWithoutBytesOnceWarm()
    {
        InteractivePage p = Create(Hud(0, true));
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, retained, options);
        double t = 0;
        InputOutcome last = InputOutcome.None;

        void Frame()
        {
            t += 16;
            last = p.Advance(t);
            painter.Paint(p.Page.Fragments, retained, options);
        }

        Frame();
        Assert.Equal(InputOutcome.Animated | InputOutcome.Relaid, last);
        Allocation.AssertZero(Frame, "advance and repaint with animated width and color on the HUD", warmup: 300, iterations: 100);
    }

    [Fact]
    public void TimingPerTick()
    {
        // 1 ms is the bar for a tick plus repaint on a release build on the benchmark host; elsewhere the test only guards against an
        // order-of-magnitude regression. ONERENDER_STRICT_TIMING=1 holds it to the bar.
        bool strict = Environment.GetEnvironmentVariable("ONERENDER_STRICT_TIMING") == "1";
        double limit = strict ? 1.0 : 25.0;
        InteractivePage p = Create(Hud(0, true));
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, retained, options);
        double t = 0;
        for (int i = 0; i < 300; i++)
        {
            t += 16;
            p.Advance(t);
            painter.Paint(p.Page.Fragments, retained, options);
        }

        double best = double.MaxValue;
        const int N = 200;
        for (int round = 0; round < 5; round++)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < N; i++)
            {
                t += 16;
                p.Advance(t);
                painter.Paint(p.Page.Fragments, retained, options);
            }

            best = Math.Min(best, Stopwatch.GetElapsedTime(t0).TotalMilliseconds / N);
        }

        Console.WriteLine("TIMING animated width and color tick plus repaint: " + best.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5 rounds)");
        Assert.True(best <= limit, "a tick took " + best + " ms; limit " + limit + " ms");
    }

    [Fact]
    public void IncrementalTicksEqualAFreshRenderAtTheSameTime()
    {
        string html = Hud(40, true);
        InteractivePage p = Create(html);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, retained, options);
        double[] sampled = [100, 450, 701, 1234, 2000, 3333];
        double t = 0;
        foreach (double target in sampled)
        {
            while (t + 16 < target)
            {
                t += 16;
                p.Advance(t);
            }

            p.Advance(target);
            t = target;

            HtmlPage fresh = HtmlPage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height, AnimationTime = target });
            IReadOnlyList<ElementBox> a = p.Page.ElementBoxes(), b = fresh.ElementBoxes();
            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(b[i], a[i]);
            }

            var mine = new DisplayList();
            var theirs = new DisplayList();
            new Painter().Paint(p.Page.Fragments, mine, options);
            new Painter().Paint(fresh.Fragments, theirs, options);
            Assert.Equal(theirs.Count, mine.Count);
            for (int i = 0; i < mine.Count; i++)
            {
                Assert.Equal(theirs[i].Id, mine[i].Id);
                Assert.Equal(theirs[i].ContentHash, mine[i].ContentHash);
            }
        }
    }

    [Fact]
    public void HoverTransitionsInPlaceEqualARebuild()
    {
        // Hovering starts width and color transitions on some elements; the in-place restyle and the page rebuilt from scratch at each
        // input must show the same boxes and the same display list at every sampled time.
        string html = GameUiScene.Html().Replace("</style>",
            "#slot3,#slot20,#quest3{transition:width 400ms linear,background-color 400ms linear,color 400ms linear}" +
            "#slot3:hover,#slot20:hover{width:90px;background-color:#808000}#quest3:hover{color:#ff0000;background-color:#335577}</style>", StringComparison.Ordinal);
        InteractivePage fast = Create(html);
        InteractivePage slow = Create(html);
        slow.DisableInPlaceRestyle = true;
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height);
        var rng = new Random(7);
        string[] ids = ["slot3", "slot20", "quest3"];
        double t = 0;
        for (int step = 0; step < 30; step++)
        {
            string id = ids[rng.Next(ids.Length)];
            OneRender.Layout.Rect r = fast.Page.Fragments.TryGetBorderBox(fast.Page.Document.GetElementById(id), out OneRender.Layout.Rect box) ? box : default;
            double x = rng.Next(3) == 0 ? 2000 : r.X + 1, y = rng.Next(3) == 0 ? 2000 : r.Y + 1;
            t += rng.Next(1, 300);
            fast.HandlePointer(Ix.Move(x, y, t));
            slow.HandlePointer(Ix.Move(x, y, t));
            for (int k = 0; k < 3; k++)
            {
                t += rng.Next(5, 200);
                fast.Advance(t);
                slow.Advance(t);
            }

            IReadOnlyList<ElementBox> a = fast.Page.ElementBoxes(), b = slow.Page.ElementBoxes();
            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.True(b[i] == a[i], "step " + step + " t=" + t + " id=" + id + " at " + x + "," + y + ": " + a[i] + " vs " + b[i]);
            }

            var mine = new DisplayList();
            var theirs = new DisplayList();
            new Painter().Paint(fast.Page.Fragments, mine, options);
            new Painter().Paint(slow.Page.Fragments, theirs, options);
            Assert.Equal(theirs.Count, mine.Count);
            for (int i = 0; i < mine.Count; i++)
            {
                Assert.Equal(theirs[i].ContentHash, mine[i].ContentHash);
            }
        }
    }
}
