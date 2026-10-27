using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;

namespace OneRender.Benchmarks;

/// <summary>
/// CSS animations on the 1,000-node game-UI page, one frame per call: the host's clock moves 16 ms, the animations are evaluated and the
/// display list is brought up to date. Transform and opacity animations must patch the list without laying anything out again.
/// </summary>
[MemoryDiagnoser]
public class MotionBenchmarks
{
    private const string Keyframes =
        "@keyframes spin{to{transform:rotate(360deg)}}@keyframes pulse{from{opacity:.2}to{opacity:1}}" +
        "@keyframes grow{from{width:20px}to{width:70px}}@keyframes tint{from{color:#ff0000}to{color:#0000ff}}" +
        "@keyframes bg{from{background-color:#102030}to{background-color:#a0b0c0}}";

    // One row's width runs a transition for the whole benchmark: hovering it starts a linear transition of 400,000 px over 100,000 s, so
    // every frame moves the width by about 0.06 px and nothing ever completes. This is the frame of a game UI that animates a layout
    // property (a progress bar, an expanding row): restyle of one element, relayout of its subtree, display-list update.
    private const string WidthCss = "#quest3{width:100px;transition:width 100000s linear}#quest3:hover{width:400000px}";

    private InteractivePage _compositor = null!, _mixed = null!, _idle = null!, _width = null!;
    private Painter _compositorPainter = null!, _mixedPainter = null!, _idlePainter = null!, _widthPainter = null!;
    private RetainedDisplayList _compositorList = null!, _mixedList = null!, _idleList = null!, _widthList = null!;
    private PaintOptions _mixedOptions, _widthOptions;
    private double _tCompositor, _tMixed, _tIdle, _tWidth;

    [GlobalSetup]
    public void Setup()
    {
        string html = GameUiScene.Html();
        string[] ids = IdsOf(html);
        _compositor = Create(Hud(html, ids, 100, false), out _compositorPainter, out _compositorList, out _);
        _mixed = Create(Hud(html, ids, 100, true), out _mixedPainter, out _mixedList, out _mixedOptions);
        _idle = Create(html, out _idlePainter, out _idleList, out _);
        _width = Create(html.Replace("</style>", WidthCss + "</style>", StringComparison.Ordinal), out _widthPainter, out _widthList, out _widthOptions);
        var row = _width.Layout.GetBoundingClientRect(_width.FindElement("quest3"));
        _width.HandlePointer(new PointerInput { Action = PointerAction.Move, X = row.X + 5, Y = row.Y + (row.Height / 2), Time = 0 });
        _widthPainter.Paint(_width.Page.Fragments, _widthList, _widthOptions);
        for (int i = 0; i < 400; i++)
        {
            CompositorTick100();
            MixedTick100Compositor4Layout();
            IdleAdvance();
            WidthTransitionTick();
        }
    }

    private static InteractivePage Create(string html, out Painter painter, out RetainedDisplayList list, out PaintOptions options)
    {
        var o = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage page = InteractivePage.Create(html, o, new InteractionOptions { AnimationStartTime = 0 });
        painter = new Painter();
        list = new RetainedDisplayList();
        options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = page.Page.Scroll };
        painter.Paint(page.Page.Fragments, list, options);
        page.Advance(0);
        return page;
    }

    private static string[] IdsOf(string html)
    {
        var ids = new List<string>();
        int at = 0;
        while ((at = html.IndexOf("id=\"", at, StringComparison.Ordinal)) >= 0)
        {
            at += 4;
            int end = html.IndexOf('"', at);
            ids.Add(html.Substring(at, end - at));
            at = end;
        }

        return ids.ToArray();
    }

    private static string Hud(string html, string[] ids, int compositor, bool layout)
    {
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

    /// <summary>100 transform and opacity animations running: evaluate them and patch the display list. No layout, expected 0 B.</summary>
    [Benchmark]
    public int CompositorTick100()
    {
        _tCompositor += 16;
        _compositor.Advance(_tCompositor);
        return _compositor.PatchAnimations(_compositorPainter, _compositorList);
    }

    /// <summary>The same 100 plus a width animation on two slots, a color and a background-color animation: layout for the width ones, then a repaint.</summary>
    [Benchmark]
    public int MixedTick100Compositor4Layout()
    {
        _tMixed += 16;
        InputOutcome outcome = _mixed.Advance(_tMixed);
        if ((outcome & InputOutcome.Relaid) != 0)
        {
            return _mixed.Repaint(_mixedPainter, _mixedList, _mixedOptions);
        }

        return _mixed.PatchAnimations(_mixedPainter, _mixedList);
    }

    /// <summary>A frame on a page with no animation running: advancing the clock does nothing. Expected: next to nothing and 0 B.</summary>
    [Benchmark]
    public int IdleAdvance()
    {
        _tIdle += 16;
        _idle.Advance(_tIdle);
        return _idle.PatchAnimations(_idlePainter, _idleList);
    }

    /// <summary>A CSS transition on one row's width ticks one frame: the clock moves 16 ms, the width is interpolated, the row's subtree is laid out again and the display list is updated. Expected 0 B.</summary>
    [Benchmark]
    public int WidthTransitionTick()
    {
        _tWidth += 16;
        InputOutcome outcome = _width.Advance(_tWidth);
        if ((outcome & InputOutcome.Relaid) != 0)
        {
            return _width.Repaint(_widthPainter, _widthList, _widthOptions);
        }

        return _width.PatchAnimations(_widthPainter, _widthList);
    }
}
