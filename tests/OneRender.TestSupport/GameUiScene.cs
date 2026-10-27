using System;
using System.Collections.Generic;
using System.Text;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.TestSupport;

/// <summary>
/// A generic game-UI page of about a thousand nodes (a heads-up display with a quest list, an inventory grid, an action bar and a chat
/// log), described once and produced as HTML or as a builder tree. Every box sets its own <c>display</c> and sizes, so both front ends
/// lay it out the same way without a user-agent sheet.
/// </summary>
public static class GameUiScene
{
    /// <summary>The viewport the scene is designed for, in CSS px.</summary>
    public const int Width = 1280;

    /// <summary>The viewport the scene is designed for, in CSS px.</summary>
    public const int Height = 720;

    private sealed class Spec(string tag, string css, string? hover = null, string? text = null, string? id = null)
    {
        public string Tag = tag;
        public string Css = css;
        public string? Hover = hover;
        public string? Text = text;
        public string? Id = id;
        public List<Spec> Kids = [];

        public Spec Add(Spec kid)
        {
            Kids.Add(kid);
            return this;
        }
    }

    private const string Flex = "display:flex;";
    private const string Col = "display:flex;flex-direction:column;";

    private static Spec D(string css, string? hover = null, string? id = null) => new("div", css, hover, null, id);

    private static Spec T(string css, string text, string? hover = null, string? id = null) => new("span", "display:block;" + css, hover, text, id);

    private static Spec Build()
    {
        var root = D(Col + "width:1280px;height:720px;background:#10141c;color:#dde3ee;font:14px Liberation Sans;");

        // Top bar: player card, resource chips, menu buttons.
        Spec top = D(Flex + "height:64px;padding:8px 12px;gap:12px;background:#1a2230;align-items:center;");
        Spec card = D(Flex + "gap:10px;width:300px;");
        card.Add(D("width:48px;height:48px;border-radius:8px;background:#5b7bd6;"));
        Spec info = D(Col + "gap:2px;width:220px;");
        info.Add(T("font-weight:bold;", "Aria Stormwind"));
        info.Add(T("font-size:12px;color:#9fb0cc;", "Level 42 Ranger", id: "level"));
        Spec bars = D(Col + "gap:3px;");
        foreach ((string color, int pct) in new[] { ("#d04a4a", 80), ("#4a7bd0", 55), ("#c9a43a", 30) })
        {
            Spec bar = D("width:200px;height:6px;background:#0b0e14;overflow:hidden;");
            bar.Add(D($"width:{pct}%;height:6px;background:{color};"));
            bars.Add(bar);
        }

        info.Add(bars);
        Spec buffs = D(Flex + "gap:2px;");
        for (int i = 0; i < 16; i++)
        {
            buffs.Add(D($"width:10px;height:10px;background:hsl({i * 23},60%,50%);"));
        }

        info.Add(buffs);
        card.Add(info);
        top.Add(card);
        string[] chips = ["Gold", "Gems", "Wood", "Iron", "Food", "Mana", "Fame", "Keys", "Ore", "Herbs", "Cloth", "Ink"];
        for (int i = 0; i < chips.Length; i++)
        {
            Spec chip = D(Flex + "height:28px;width:92px;gap:6px;padding:0 6px;align-items:center;background:#232d40;border-radius:4px;", "background:#33425f;");
            chip.Add(D("width:14px;height:14px;border-radius:7px;background:#e0b84a;"));
            chip.Add(T("font-size:12px;", chips[i] + " " + (100 + (i * 37)), id: i == 0 ? "gold" : null));
            top.Add(chip);
        }

        foreach (string label in new[] { "Menu", "Map", "Quests", "Options" })
        {
            Spec button = D(Flex + "width:64px;height:30px;justify-content:center;align-items:center;background:#2c3a55;border-radius:4px;", "background:#4562a0;");
            button.Add(T("font-size:12px;", label));
            top.Add(button);
        }

        root.Add(top);

        // Middle: quest list, minimap in the middle, inventory.
        Spec middle = D(Flex + "flex:1;gap:12px;padding:12px;");
        Spec quests = D(Col + "width:280px;background:#161c28;padding:8px;gap:2px;");
        quests.Add(T("font-weight:bold;height:24px;", "Quests"));
        for (int i = 0; i < 30; i++)
        {
            Spec row = D(Flex + "height:24px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;", "background:#2a3750;", "quest" + i);
            row.Add(T("font-size:13px;", "Quest number " + (i + 1)));
            row.Add(T("font-size:12px;color:#9fb0cc;", (i % 5) + "/5"));
            quests.Add(row);
        }

        middle.Add(quests);
        Spec party = D(Col + "width:200px;background:#161c28;padding:8px;gap:6px;");
        for (int i = 0; i < 8; i++)
        {
            Spec member = D(Col + "gap:2px;", "background:#232d40;", "member" + i);
            member.Add(T("font-size:12px;", "Member " + (i + 1)));
            foreach (string color in new[] { "#d04a4a", "#4a7bd0" })
            {
                Spec bar = D("width:180px;height:5px;background:#0b0e14;");
                bar.Add(D($"width:{(i * 13 + 20) % 100}%;height:5px;background:{color};"));
                member.Add(bar);
            }

            party.Add(member);
        }

        middle.Add(party);
        Spec map = D("flex:1;display:grid;grid-template-columns:repeat(16,1fr);grid-template-rows:repeat(12,1fr);gap:2px;background:#0d1118;padding:4px;");
        for (int i = 0; i < 16 * 12; i++)
        {
            map.Add(D(i % 7 == 0 ? "background:#2f5a3a;" : "background:#1e2b22;", i % 11 == 0 ? "background:#58a86a;" : null));
        }

        middle.Add(map);
        Spec inv = D(Col + "width:520px;background:#161c28;padding:8px;gap:8px;");
        Spec head = D(Flex + "justify-content:space-between;height:24px;");
        head.Add(T("font-weight:bold;", "Inventory"));
        head.Add(T("color:#e0b84a;", "Weight 31/80", id: "weight"));
        inv.Add(head);
        Spec tabs = D(Flex + "gap:4px;height:26px;");
        foreach (string tab in new[] { "All", "Gear", "Food", "Tools", "Misc" })
        {
            Spec t = D(Flex + "width:60px;justify-content:center;align-items:center;background:#232d40;", "background:#3a4d73;");
            t.Add(T("font-size:12px;", tab));
            tabs.Add(t);
        }

        inv.Add(tabs);
        Spec grid = D("display:grid;grid-template-columns:repeat(8,56px);grid-auto-rows:56px;gap:6px;");
        for (int i = 0; i < 48; i++)
        {
            Spec slot = D(Flex + "flex-direction:column;justify-content:space-between;padding:4px;background:#232d40;border:1px solid #3a4560;", "background:#33425f;border-color:#8fb0ff;", "slot" + i);
            slot.Add(D($"width:24px;height:24px;border-radius:4px;background:hsl({i * 29 % 360},50%,50%);"));
            slot.Add(T("font-size:11px;align-self:flex-end;", (i % 9 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            grid.Add(slot);
        }

        inv.Add(grid);
        middle.Add(inv);
        root.Add(middle);

        // Bottom: action bar and chat.
        Spec bottom = D(Flex + "height:150px;gap:12px;padding:0 12px 12px;");
        Spec actions = D(Flex + "gap:6px;width:560px;align-items:flex-end;");
        for (int i = 0; i < 10; i++)
        {
            Spec a = D(Col + "width:52px;height:64px;background:#232d40;padding:3px;justify-content:space-between;", "background:#3a4d73;", "act" + i);
            a.Add(T("font-size:10px;color:#9fb0cc;", ((i + 1) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            a.Add(T("font-size:11px;", "Skill " + (i + 1)));
            a.Add(D($"height:4px;background:#d04a4a;width:{(i * 10) % 100}%;"));
            actions.Add(a);
        }

        bottom.Add(actions);
        Spec chat = D(Col + "flex:1;background:#161c28;padding:6px;gap:1px;overflow:hidden;");
        for (int i = 0; i < 50; i++)
        {
            Spec line = D(Flex + "gap:6px;height:16px;font-size:12px;", "background:#232d40;");
            line.Add(T("color:#7fd09a;white-space:nowrap;", "Player" + i));
            line.Add(T("white-space:nowrap;", "message number " + i + " in the chat log"));
            chat.Add(line);
        }

        bottom.Add(chat);
        root.Add(bottom);
        return root;
    }

    /// <summary>The page as a complete HTML document (hover effects are <c>:hover</c> rules).</summary>
    public static string Html()
    {
        Spec root = Build();
        var css = new StringBuilder();
        var body = new StringBuilder();
        int n = 0;
        Emit(root, body, css, ref n);
        return "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>HUD</title><style>\nhtml,body{margin:0;padding:0}\n" + css + "</style></head>\n<body>\n" + body + "</body></html>\n";
    }

    private static void Emit(Spec s, StringBuilder body, StringBuilder css, ref int n)
    {
        // An explicit id on the spec is kept; others get a number only when a hover rule needs one.
        string? id = s.Id;
        if (s.Hover is not null && id is null)
        {
            id = "h" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        n++;
        body.Append('<').Append(s.Tag);
        if (id is not null)
        {
            body.Append(" id=\"").Append(id).Append('"');
        }

        body.Append(" style=\"").Append(s.Css).Append("\">");
        if (s.Hover is not null)
        {
            css.Append('#').Append(id).Append(":hover{");
            foreach (string decl in s.Hover.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                css.Append(decl).Append(" !important;");
            }

            css.Append("}\n");
        }

        if (s.Text is not null)
        {
            body.Append(System.Net.WebUtility.HtmlEncode(s.Text));
        }

        foreach (Spec k in s.Kids)
        {
            Emit(k, body, css, ref n);
        }

        body.Append("</").Append(s.Tag).Append(">\n");
    }

    /// <summary>The elements the scene names (<c>gold</c>, <c>slot0</c> ... <c>quest17</c> ...), and their handles.</summary>
    public sealed class SceneNodes
    {
        private readonly Dictionary<string, StyledNode> _byId = new(StringComparer.Ordinal);

        internal void Add(string id, StyledNode node) => _byId[id] = node;

        /// <summary>The element with the id.</summary>
        /// <param name="id">An id given in the scene.</param>
        public StyledNode this[string id] => _byId[id];

        /// <summary>The first text node under the element with the id.</summary>
        /// <param name="id">An id given in the scene.</param>
        public StyledNode TextOf(string id) => _byId[id].FirstChild;
    }

    /// <summary>Fills <paramref name="tree"/> with the page through the builder front end.</summary>
    /// <param name="tree">An empty tree.</param>
    public static SceneNodes Populate(StyledTree tree)
    {
        Spec root = Build();
        var handles = new SceneNodes();
        var cache = new Dictionary<string, StyleDeclaration>(StringComparer.Ordinal);
        Add(tree, default, root, handles, cache);
        return handles;
    }

    /// <summary>A declaration for CSS text, shared between nodes that use the same text.</summary>
    /// <param name="css">Declarations separated by semicolons.</param>
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

    private static void Add(StyledTree tree, StyledNode parent, Spec spec, SceneNodes handles, Dictionary<string, StyleDeclaration> cache)
    {
        // Explicit stack: the scene is shallow, but the code does not rely on that.
        var stack = new Stack<(Spec Spec, StyledNode Parent)>();
        stack.Push((spec, parent));
        while (stack.Count > 0)
        {
            (Spec s, StyledNode p) = stack.Pop();
            if (!cache.TryGetValue(s.Css, out StyleDeclaration? decl))
            {
                cache[s.Css] = decl = Declaration(s.Css);
            }

            StyledNode node = p.IsNull ? tree.AddRoot(s.Tag, decl) : tree.AddElement(p, s.Tag, decl);
            if (s.Id is not null)
            {
                handles.Add(s.Id, node);
            }

            if (s.Hover is not null)
            {
                string key = "hover:" + s.Hover;
                if (!cache.TryGetValue(key, out StyleDeclaration? hd))
                {
                    cache[key] = hd = Declaration(s.Hover);
                }

                tree.SetStateStyle(node, Interaction.ElementStates.Hover, hd);
            }

            if (s.Text is not null)
            {
                tree.AddText(node, s.Text);
            }

            for (int i = s.Kids.Count - 1; i >= 0; i--)
            {
                stack.Push((s.Kids[i], node));
            }
        }
    }
}
