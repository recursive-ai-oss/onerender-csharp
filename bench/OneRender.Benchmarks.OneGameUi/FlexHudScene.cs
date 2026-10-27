using System;
using System.Collections.Generic;
using System.Globalization;
using OneRender.Interaction;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Benchmarks;

/// <summary>
/// The flex-only game-UI page of <see cref="OneGameUiBenchmarks"/> (a heads-up display of about 1,000 nodes: top bar, quest list, party,
/// a 16 x 12 wrapping minimap, an inventory of 48 wrapping slots, an action bar and a chat log), written for the engine's builder front end:
/// <see cref="StyledTree"/> nodes with style declarations and no HTML. Every box mirrors the flex defaults of the other library's elements
/// (<c>display:flex</c>, row direction, <c>box-sizing:border-box</c>, <c>flex-shrink:1</c>), and every <c>Row</c>, <c>Column</c>, <c>Box</c> or
/// <c>Label</c> there is one element here, in the same order, so the two trees can be compared node for node.
/// </summary>
internal sealed class FlexHudScene
{
    public const int Width = 1280;
    public const int Height = 720;

    private const string Flex = "display:flex;box-sizing:border-box;";
    private const string Col = "display:flex;box-sizing:border-box;flex-direction:column;";

    /// <summary>The declaration of the named quest row before and after a resize (the swap the benchmark makes).</summary>
    public const string QuestRowNormal = Flex + "height:24px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;";

    /// <summary>The quest row at double height.</summary>
    public const string QuestRowTall = Flex + "height:48px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;";

    private readonly StyledTree _tree;
    private readonly Dictionary<string, StyleDeclaration> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StyledNode> _named = new(StringComparer.Ordinal);

    private FlexHudScene(StyledTree tree) => _tree = tree;

    /// <summary>The named elements: <c>gold</c> (a label), <c>quest0</c>..<c>quest29</c>, <c>slot0</c>..<c>slot47</c>.</summary>
    public StyledNode this[string id] => _named[id];

    /// <summary>The text node of a named label.</summary>
    public StyledNode TextOf(string id) => _named[id].FirstChild;

    /// <summary>Builds the page into an empty tree.</summary>
    public static FlexHudScene Populate(StyledTree tree)
    {
        var s = new FlexHudScene(tree);
        s.Build();
        return s;
    }

    /// <summary>A declaration for CSS text, shared between nodes that use the same text.</summary>
    public static StyleDeclaration Declaration(string css)
    {
        var d = new StyleDeclaration();
        foreach (string decl in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = decl.IndexOf(':', StringComparison.Ordinal);
            d.Set(decl[..colon].Trim(), decl[(colon + 1)..].Trim());
        }

        return d;
    }

    private StyleDeclaration Decl(string css)
    {
        if (!_cache.TryGetValue(css, out StyleDeclaration? d))
        {
            _cache[css] = d = Declaration(css);
        }

        return d;
    }

    private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    private StyledNode Root(string css) => _tree.AddRoot("div", Decl(css));

    private StyledNode Box(StyledNode parent, string css, string? id = null, string? hover = null)
    {
        StyledNode n = _tree.AddElement(parent, "div", Decl(css));
        if (id is not null)
        {
            _named[id] = n;
        }

        if (hover is not null)
        {
            _tree.SetStateStyle(n, ElementStates.Hover, Decl(hover));
        }

        return n;
    }

    private StyledNode Label(StyledNode parent, string text, float size, string? color = null, bool bold = false, string extra = "", string? id = null)
    {
        string css = "display:block;box-sizing:border-box;line-height:1.1499;font-size:" + N(size) + "px;" + (color is null ? "" : "color:" + color + ";") + (bold ? "font-weight:700;" : "") + extra;
        StyledNode n = _tree.AddElement(parent, "span", Decl(css));
        _tree.AddText(n, text);
        if (id is not null)
        {
            _named[id] = n;
        }

        return n;
    }

    private StyledNode Fill(StyledNode parent, float w, float h, string color, string extra = "") =>
        Box(parent, Flex + "width:" + N(w) + "px;height:" + N(h) + "px;background:" + color + ";" + extra);

    private static string Hsl(int hue, int sat, int light) => "hsl(" + N(hue % 360) + "," + N(sat) + "%," + N(light) + "%)";

    private void Build()
    {
        StyledNode root = Root(Col + "width:1280px;height:720px;background:#10141c;color:#dde3ee;font:14px Liberation Sans;");

        // Top bar.
        StyledNode top = Box(root, Flex + "height:64px;padding:8px 12px;gap:12px;background:#1a2230;align-items:center;");
        StyledNode card = Box(top, Flex + "gap:10px;width:300px;");
        Fill(card, 48, 48, "#5b7bd6", "border-radius:8px;");
        StyledNode info = Box(card, Col + "gap:2px;width:220px;");
        Label(info, "Aria Stormwind", 14, bold: true);
        Label(info, "Level 42 Ranger", 12, "#9fb0cc", id: "level");
        StyledNode bars = Box(info, Col + "gap:3px;");
        foreach ((string color, int pct) in new[] { ("#d04a4a", 80), ("#4a7bd0", 55), ("#c9a43a", 30) })
        {
            StyledNode bar = Box(bars, Flex + "width:200px;height:6px;background:#0b0e14;overflow:hidden;");
            Fill(bar, 200 * pct / 100f, 6, color);
        }

        StyledNode buffs = Box(info, Flex + "gap:2px;");
        for (int i = 0; i < 16; i++)
        {
            Fill(buffs, 10, 10, Hsl(i * 23, 60, 50));
        }

        string[] chips = ["Gold", "Gems", "Wood", "Iron", "Food", "Mana", "Fame", "Keys", "Ore", "Herbs", "Cloth", "Ink"];
        for (int i = 0; i < chips.Length; i++)
        {
            StyledNode chip = Box(top, Flex + "width:92px;height:28px;gap:6px;padding:0 6px;align-items:center;background:#232d40;border-radius:4px;", hover: "background:#33425f;");
            Fill(chip, 14, 14, "#e0b84a", "border-radius:7px;");
            Label(chip, chips[i] + " " + N(100 + (i * 37)), 12, id: i == 0 ? "gold" : null);
        }

        foreach (string label in new[] { "Menu", "Map", "Quests", "Options" })
        {
            StyledNode button = Box(top, Flex + "width:64px;height:30px;justify-content:center;align-items:center;background:#2c3a55;border-radius:4px;", hover: "background:#4562a0;");
            Label(button, label, 12);
        }

        // Middle: quests, party, minimap, inventory.
        StyledNode middle = Box(root, Flex + "flex-grow:1;gap:12px;padding:12px;");
        StyledNode quests = Box(middle, Col + "width:280px;background:#161c28;padding:8px;gap:2px;");
        Label(quests, "Quests", 14, bold: true, extra: "height:24px;");
        for (int i = 0; i < 30; i++)
        {
            StyledNode row = Box(quests, QuestRowNormal, "quest" + N(i), "background:#2a3750;");
            Label(row, "Quest number " + N(i + 1), 13);
            Label(row, N(i % 5) + "/5", 12, "#9fb0cc");
        }

        StyledNode party = Box(middle, Col + "width:200px;background:#161c28;padding:8px;gap:6px;");
        for (int i = 0; i < 8; i++)
        {
            StyledNode member = Box(party, Col + "gap:2px;", hover: "background:#232d40;");
            Label(member, "Member " + N(i + 1), 12);
            foreach (string color in new[] { "#d04a4a", "#4a7bd0" })
            {
                StyledNode bar = Box(member, Flex + "width:180px;height:5px;background:#0b0e14;");
                Fill(bar, 180 * ((i * 13) + 20) % 100 / 100f, 5, color);
            }
        }

        StyledNode map = Box(middle, Flex + "flex-wrap:wrap;flex-grow:1;gap:2px;padding:4px;background:#0d1118;");
        for (int i = 0; i < 16 * 12; i++)
        {
            Box(map, Flex + "width:10px;height:10px;background:" + (i % 7 == 0 ? "#2f5a3a" : "#1e2b22") + ";", hover: i % 11 == 0 ? "background:#58a86a;" : null);
        }

        StyledNode inv = Box(middle, Col + "width:520px;background:#161c28;padding:8px;gap:8px;");
        StyledNode head = Box(inv, Flex + "justify-content:space-between;height:24px;");
        Label(head, "Inventory", 14, bold: true);
        Label(head, "Weight 31/80", 14, "#e0b84a", id: "weight");
        StyledNode tabs = Box(inv, Flex + "gap:4px;height:26px;");
        foreach (string tab in new[] { "All", "Gear", "Food", "Tools", "Misc" })
        {
            StyledNode t = Box(tabs, Flex + "width:60px;justify-content:center;align-items:center;background:#232d40;", hover: "background:#3a4d73;");
            Label(t, tab, 12);
        }

        StyledNode grid = Box(inv, Flex + "flex-wrap:wrap;gap:6px;");
        for (int i = 0; i < 48; i++)
        {
            StyledNode slot = Box(grid, Col + "width:56px;height:56px;padding:4px;justify-content:space-between;background:#232d40;border:1px solid #3a4560;", "slot" + N(i), "background:#33425f;border-color:#8fb0ff;");
            Fill(slot, 24, 24, Hsl(i * 29 % 360, 50, 50), "border-radius:4px;");
            Label(slot, N((i % 9) + 1), 11, extra: "align-self:flex-end;");
        }

        // Bottom: action bar and chat.
        StyledNode bottom = Box(root, Flex + "height:150px;gap:12px;padding:0 12px;");
        StyledNode actions = Box(bottom, Flex + "gap:6px;width:560px;align-items:flex-end;");
        for (int i = 0; i < 10; i++)
        {
            StyledNode a = Box(actions, Col + "width:52px;height:64px;background:#232d40;padding:3px;justify-content:space-between;", "act" + N(i), "background:#3a4d73;");
            Label(a, N((i + 1) % 10), 10, "#9fb0cc");
            Label(a, "Skill " + N(i + 1), 11);
            Fill(a, 52 * ((i * 10) % 100) / 100f, 4, "#d04a4a");
        }

        StyledNode chat = Box(bottom, Col + "flex-grow:1;background:#161c28;padding:6px;gap:1px;overflow:hidden;");
        for (int i = 0; i < 50; i++)
        {
            StyledNode line = Box(chat, Flex + "height:16px;gap:6px;", hover: "background:#232d40;");
            Label(line, "Player" + N(i), 12, "#7fd09a");
            Label(line, "message number " + N(i) + " in the chat log", 12);
        }
    }
}
