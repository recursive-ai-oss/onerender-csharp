using System;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using OneGame.Text;
using OneGame.UI;
using OneRender.Fonts;
using OneRender.TestSupport;

namespace OneRender.Benchmarks;

/// <summary>
/// The same changes as <see cref="IncrementalBenchmarks"/>, on the same game-UI page written as a OneGame.UI element tree through that library's
/// public builder API: hover, text change, one-row resize and a frame with nothing changed. A change is measured the way a game does it:
/// the input or edit, then <c>UiTree.Frame()</c> (bindings, style resolution, layout, draw-list update). Shapes follow the page's visible
/// structure; the page's CSS grids become wrapping rows of fixed-size cells because OneGame.UI lays out with flexbox only.
/// </summary>
[MemoryDiagnoser]
public class OneGameUiBenchmarks
{
    private UiTree _ui = null!;
    private Element _slotA = null!, _slotB = null!, _label = null!, _row = null!;
    private readonly string[] _texts = ["Gold 100", "Gold 1000", "Gold 7"];
    private readonly Action<StyleBuilder> _tall = s => s.Height(48), _normal = s => s.Height(24);
    private bool _flip;
    private int _t;
    private float _ax, _ay, _bx, _by;

    [GlobalSetup]
    public void Setup()
    {
        _ui = CreateTree();
        _ui.Frame();
        _slotA = _ui.Find("slot3")!;
        _slotB = _ui.Find("slot20")!;
        _label = _ui.Find("gold")!;
        _row = _ui.Find("quest3")!;
        (_ax, _ay) = Center(_slotA);
        (_bx, _by) = Center(_slotB);
        for (int i = 0; i < 200; i++)
        {
            HoverRestyle();
            TextChange();
            SubtreeRelayout();
            IdleFrame();
        }
    }

    /// <summary>The page as a laid-out OneGame.UI tree, with the style sheet, shaped with the Liberation Sans files.</summary>
    internal static UiTree CreateTree()
    {
        var fonts = new FontLibrary();
        foreach (FontFaceInfo face in DefaultFonts.Faces)
        {
            if (face.Family == "Liberation Sans" && face.Slant == FontSlant.Normal)
            {
                fonts.Add(face.ReadAllBytes(), "Liberation Sans");
            }
        }

        var ui = new UiTree(new FontTextMeasurer(fonts));
        ui.SetViewport(GameUiScene.Width, GameUiScene.Height);
        ui.AddStyleSheet(Hovers());
        ui.Root.Add(Scene());
        return ui;
    }

    private static (float X, float Y) Center(Element e)
    {
        Rect r = e.AbsoluteBounds;
        return (r.X + (r.Width / 2), r.Y + (r.Height / 2));
    }

    /// <summary>A frame with nothing changed.</summary>
    [Benchmark]
    public int IdleFrame() => _ui.Frame().Version;

    /// <summary>Moves the pointer from one slot to the other, then builds the frame.</summary>
    [Benchmark]
    public int HoverRestyle()
    {
        _flip = !_flip;
        _ui.Dispatch(_flip ? UiEvent.PointerMove(_ax, _ay) : UiEvent.PointerMove(_bx, _by));
        return _ui.Frame().Version;
    }

    /// <summary>Changes a label's text, then builds the frame.</summary>
    [Benchmark]
    public int TextChange()
    {
        _label.Text = _texts[_t++ % 3];
        return _ui.Frame().Version;
    }

    /// <summary>Resizes one row, then builds the frame.</summary>
    [Benchmark]
    public int SubtreeRelayout()
    {
        _flip = !_flip;
        _row.Style(_flip ? _tall : _normal);
        return _ui.Frame().Version;
    }

    private static StyleSheet Hovers() => new StyleSheet()
        .Rule(".chip:hover", s => s.Background(Rgb("#33425f")))
        .Rule(".button:hover", s => s.Background(Rgb("#4562a0")))
        .Rule(".quest:hover", s => s.Background(Rgb("#2a3750")))
        .Rule(".member:hover", s => s.Background(Rgb("#232d40")))
        .Rule(".tile:hover", s => s.Background(Rgb("#58a86a")))
        .Rule(".tab:hover", s => s.Background(Rgb("#3a4d73")))
        .Rule(".slot:hover", s => s.Background(Rgb("#33425f")).BorderColor(Rgb("#8fb0ff")))
        .Rule(".act:hover", s => s.Background(Rgb("#3a4d73")))
        .Rule(".line:hover", s => s.Background(Rgb("#232d40")));

    private static Color Rgb(string hex) => Color.Hex(hex);

    private static Color Hsl(int hue, int sat, int light)
    {
        float h = (hue % 360) / 60f, s = sat / 100f, l = light / 100f;
        float c = (1 - MathF.Abs((2 * l) - 1)) * s;
        float x = c * (1 - MathF.Abs((h % 2) - 1));
        float m = l - (c / 2);
        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Color((byte)MathF.Round((r + m) * 255), (byte)MathF.Round((g + m) * 255), (byte)MathF.Round((b + m) * 255));
    }

    private static Element Text(string text, float size, string? color = null, bool bold = false)
    {
        Element e = Ui.Label(text).FontSize(size);
        if (color is not null)
        {
            e.TextColor(Rgb(color));
        }

        if (bold)
        {
            e.Style(s => s.FontWeight(700));
        }

        return e;
    }

    private static Element Fill(float w, float h, Color color) => Ui.Box().Size(w, h).Background(color);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Element Scene()
    {
        Element root = Ui.Column().Style(s => s.Width(GameUiScene.Width).Height(GameUiScene.Height))
            .Background(Rgb("#10141c")).TextColor(Rgb("#dde3ee")).FontSize(14);

        // Top bar.
        Element top = Ui.Row().Height(64).Padding(8, 12).Gap(12).Background(Rgb("#1a2230")).Align(Align.Center);
        Element info = Ui.Column().Gap(2).Width(220);
        info.Add(Text("Aria Stormwind", 14, bold: true));
        info.Add(Text("Level 42 Ranger", 12, "#9fb0cc").Named("level"));
        Element bars = Ui.Column().Gap(3);
        foreach ((string color, int pct) in new[] { ("#d04a4a", 80), ("#4a7bd0", 55), ("#c9a43a", 30) })
        {
            Element bar = Ui.Box().Size(200, 6).Background(Rgb("#0b0e14")).Overflow(OverflowMode.Hidden);
            bar.Add(Fill(200 * pct / 100f, 6, Rgb(color)));
            bars.Add(bar);
        }

        info.Add(bars);
        Element buffs = Ui.Row().Gap(2);
        for (int i = 0; i < 16; i++)
        {
            buffs.Add(Fill(10, 10, Hsl(i * 23, 60, 50)));
        }

        info.Add(buffs);
        top.Add(Ui.Row(Fill(48, 48, Rgb("#5b7bd6")).Radius(8), info).Gap(10).Width(300));
        string[] chips = ["Gold", "Gems", "Wood", "Iron", "Food", "Mana", "Fame", "Keys", "Ore", "Herbs", "Cloth", "Ink"];
        for (int i = 0; i < chips.Length; i++)
        {
            Element label = Text(chips[i] + " " + N(100 + (i * 37)), 12);
            if (i == 0)
            {
                label.Named("gold");
            }

            top.Add(Ui.Row(Fill(14, 14, Rgb("#e0b84a")).Radius(7), label).Class("chip").Size(92, 28).Gap(6).Padding(0, 6)
                .Align(Align.Center).Background(Rgb("#232d40")).Radius(4));
        }

        foreach (string label in new[] { "Menu", "Map", "Quests", "Options" })
        {
            top.Add(Ui.Row(Text(label, 12)).Class("button").Size(64, 30).Justify(Align.Center).Align(Align.Center)
                .Background(Rgb("#2c3a55")).Radius(4));
        }

        root.Add(top);

        // Middle: quests, party, minimap, inventory.
        Element middle = Ui.Row().Grow().Gap(12).Padding(12);
        Element quests = Ui.Column().Width(280).Background(Rgb("#161c28")).Padding(8).Gap(2);
        quests.Add(Text("Quests", 14, bold: true).Height(24));
        for (int i = 0; i < 30; i++)
        {
            quests.Add(Ui.Row(Text("Quest number " + N(i + 1), 13), Text(N(i % 5) + "/5", 12, "#9fb0cc")).Named("quest" + N(i)).Class("quest")
                .Height(24).Overflow(OverflowMode.Hidden).Justify(Align.SpaceBetween).Align(Align.Center).Padding(0, 6).Background(Rgb("#1d2535")));
        }

        middle.Add(quests);
        Element party = Ui.Column().Width(200).Background(Rgb("#161c28")).Padding(8).Gap(6);
        for (int i = 0; i < 8; i++)
        {
            Element member = Ui.Column(Text("Member " + N(i + 1), 12)).Class("member").Gap(2);
            foreach (string color in new[] { "#d04a4a", "#4a7bd0" })
            {
                member.Add(Ui.Box(Fill(180 * ((i * 13) + 20) % 100 / 100f, 5, Rgb(color))).Size(180, 5).Background(Rgb("#0b0e14")));
            }

            party.Add(member);
        }

        middle.Add(party);
        Element map = Ui.Row().Wrap().Grow().Gap(2).Padding(4).Background(Rgb("#0d1118"));
        for (int i = 0; i < 16 * 12; i++)
        {
            Element tile = Ui.Box().Size(10, 10).Background(Rgb(i % 7 == 0 ? "#2f5a3a" : "#1e2b22"));
            if (i % 11 == 0)
            {
                tile.Class("tile");
            }

            map.Add(tile);
        }

        middle.Add(map);
        Element inv = Ui.Column().Width(520).Background(Rgb("#161c28")).Padding(8).Gap(8);
        inv.Add(Ui.Row(Text("Inventory", 14, bold: true), Text("Weight 31/80", 14, "#e0b84a").Named("weight")).Justify(Align.SpaceBetween).Height(24));
        Element tabs = Ui.Row().Gap(4).Height(26);
        foreach (string tab in new[] { "All", "Gear", "Food", "Tools", "Misc" })
        {
            tabs.Add(Ui.Row(Text(tab, 12)).Class("tab").Width(60).Justify(Align.Center).Align(Align.Center).Background(Rgb("#232d40")));
        }

        inv.Add(tabs);
        Element grid = Ui.Row().Wrap().Gap(6);
        for (int i = 0; i < 48; i++)
        {
            grid.Add(Ui.Column(Fill(24, 24, Hsl(i * 29 % 360, 50, 50)).Radius(4), Text(N((i % 9) + 1), 11).AlignSelf(Align.End))
                .Named("slot" + N(i)).Class("slot").Size(56, 56).Padding(4).Justify(Align.SpaceBetween)
                .Background(Rgb("#232d40")).Border(1, Rgb("#3a4560")));
        }

        inv.Add(grid);
        middle.Add(inv);
        root.Add(middle);

        // Bottom: action bar and chat.
        Element bottom = Ui.Row().Height(150).Gap(12).Padding(0, 12);
        Element actions = Ui.Row().Gap(6).Width(560).Align(Align.End);
        for (int i = 0; i < 10; i++)
        {
            actions.Add(Ui.Column(
                    Text(N((i + 1) % 10), 10, "#9fb0cc"),
                    Text("Skill " + N(i + 1), 11),
                    Fill(52 * ((i * 10) % 100) / 100f, 4, Rgb("#d04a4a")))
                .Named("act" + N(i)).Class("act").Size(52, 64).Background(Rgb("#232d40")).Padding(3).Justify(Align.SpaceBetween));
        }

        bottom.Add(actions);
        Element chat = Ui.Column().Grow().Background(Rgb("#161c28")).Padding(6).Gap(1).Overflow(OverflowMode.Hidden);
        for (int i = 0; i < 50; i++)
        {
            chat.Add(Ui.Row(
                    Text("Player" + N(i), 12, "#7fd09a"),
                    Text("message number " + N(i) + " in the chat log", 12)).Class("line").Height(16).Gap(6));
        }

        bottom.Add(chat);
        root.Add(bottom);
        return root;
    }
}
