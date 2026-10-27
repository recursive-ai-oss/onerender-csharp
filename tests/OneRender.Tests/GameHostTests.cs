using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>The surface a game host needs: image damage, content keys, host scopes, names and synthetic clicks.</summary>
public class GameHostTests
{
    private sealed class Table : IImageSource
    {
        public bool TryGetImage(string address, out ImageInfo image)
        {
            image = new ImageInfo(new ImageHandle(address.EndsWith("a.png", StringComparison.Ordinal) ? 1 : 2), 16, 8);
            return true;
        }
    }

    private static HtmlPage Page(string body, int w = 300, int h = 300) =>
        HtmlPage.Create(
            "<!DOCTYPE html><html><head><style>body{margin:0;font:14px Liberation Sans}</style></head><body>" + body + "</body></html>",
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = w, ViewportHeight = h, Images = new Table() });

    private static RetainedDisplayList Paint(HtmlPage page, Painter? painter = null)
    {
        var retained = new RetainedDisplayList();
        (painter ?? new Painter()).Paint(page.Fragments, retained, new PaintOptions(300, 300) { Images = new Table(), Scroll = page.Scroll });
        return retained;
    }

    private static string Sig(DisplayList l)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < l.Count; i++)
        {
            DisplayItem it = l[i];
            RectF r = it.RootBounds;
            sb.Append(it.Kind).Append(' ').Append(it.Id.Value.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(r.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Width.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.Height.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(it.ContentHash.ToString("x", CultureInfo.InvariantCulture)).Append(' ').Append(it.ContextHash.ToString("x", CultureInfo.InvariantCulture)).Append(' ')
                .Append(it.ParentIndex.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    private static List<ItemKind> Kinds(DisplayList l)
    {
        var k = new List<ItemKind>();
        for (int i = 0; i < l.Count; i++)
        {
            k.Add(l[i].Kind);
        }

        return k;
    }

    // ------------------------------------------------------------------ image invalidation

    private const string ImageScene =
        "<img src='a.png' style='display:block;width:32px;height:16px'>" +
        "<img src='b.png' style='display:block;width:32px;height:16px;margin-top:40px'>" +
        "<div style='width:100px;height:60px;margin-top:20px;border:4px solid;border-image:url(b.png) 1/4px round;border-radius:0;background:url(a.png) 0 0/16px 8px repeat'></div>" +
        "<img src='a.png' style='display:block;width:32px;height:16px;margin-top:30px'>";

    [Fact]
    public void InvalidatingAnImageHandleDamagesExactlyTheItemsThatDrawIt()
    {
        HtmlPage page = Page(ImageScene);
        RetainedDisplayList retained = Paint(page);
        DisplayList list = retained.Current;
        foreach (int handle in new[] { 1, 2 })
        {
            var expected = new DamageRegion();
            int items = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Kind == ItemKind.Image && list[i].Image.Value == handle)
                {
                    expected.Add(list[i].RootBounds);
                    items++;
                }
            }

            Assert.True(items >= 2, "handle " + handle + " draws " + items + " items");
            retained.ClearDamage();
            Assert.Equal(items, retained.InvalidateImage(new ImageHandle(handle)));
            Assert.Equal(expected.Count, retained.Damage.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i], retained.Damage[i]);
            }
        }

        retained.ClearDamage();
        Assert.Equal(0, retained.InvalidateImage(new ImageHandle(99)));
        Assert.True(retained.Damage.IsEmpty);
    }

    [Fact]
    public void ImageInvalidationChangesNoItemAndAllocatesNothing()
    {
        HtmlPage page = Page(ImageScene);
        RetainedDisplayList retained = Paint(page);
        string before = Sig(retained.Current);
        var handle = new ImageHandle(1);
        void Invalidate()
        {
            retained.ClearDamage();
            retained.InvalidateImage(handle);
        }

        Allocation.AssertZero(Invalidate, "image invalidation", warmup: 4, iterations: 50);
        Assert.Equal(before, Sig(retained.Current));
        Assert.False(retained.Damage.IsEmpty);
    }

    [Fact]
    public void ALivePageForwardsImageInvalidation()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("width", "200px").Set("height", "100px"));
        StyledNode img = tree.AddElement(root, "img", new StyleDeclaration().Set("display", "block").Set("width", "40px").Set("height", "20px"));
        tree.SetReplacedSource(img, "a.png");
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { Images = new Table() });
        page.Display.ClearDamage();
        Assert.Equal(1, page.InvalidateImage(new ImageHandle(1)));
        Assert.Equal(0, page.InvalidateImage(new ImageHandle(2)));
        Assert.Equal(1, page.Display.Damage.Count);
        Assert.Equal(40f, page.Display.Damage[0].Width);
    }

    // ------------------------------------------------------------------ recycled node slots

    private static LivePage SlotPage(StyledTree tree) => new(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 300, Images = new Table() });

    [Fact]
    public void ARecycledSlotNeverKeepsTheOldName()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        StyledNode a = tree.AddElement(root, "div", new StyleDeclaration().Set("display", "block"));
        tree.SetName(a, "panel");
        LivePage page = SlotPage(tree);
        tree.Remove(a);
        page.Flush();
        Assert.False(a.IsValid);

        StyledNode reused = tree.AddElement(root, "div", new StyleDeclaration().Set("display", "block"));
        Assert.Equal(a.Index, reused.Index);
        Assert.True(tree.FindByName("panel").IsNull);
        Assert.Null(tree.NameOf(reused));

        // the name is free for the new node, and a stale handle cannot rename or read what reuses the slot
        tree.SetName(reused, "panel");
        Assert.Equal(reused, tree.FindByName("panel"));
        Assert.Throws<InvalidOperationException>(() => tree.SetName(a, "other"));
        Assert.Throws<InvalidOperationException>(() => tree.NameOf(a));
        Assert.Equal("panel", tree.NameOf(reused));
    }

    [Fact]
    public void AHostScopeDoesNotSurviveItsNodesSlotAndIncrementalEqualsFull()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("display", "block").Set("width", "200px"));
        StyledNode a = tree.AddElement(root, "div", new StyleDeclaration().Set("display", "block").Set("height", "20px").Set("background-color", "red").SetHostScope(7));
        LivePage page = SlotPage(tree);
        Assert.Equal(3, page.Display.Current.RequiredMinor);

        tree.Remove(a);
        page.Flush();
        StyledNode reused = tree.AddElement(root, "div", new StyleDeclaration().Set("display", "block").Set("height", "20px").Set("background-color", "red"));
        Assert.Equal(a.Index, reused.Index);
        page.Flush();
        Assert.True(page.Display.Current.RequiredMinor < 3);
        string incremental = Sig(page.Display.Current);

        var fresh = new StyledTree();
        StyledNode froot = fresh.AddRoot("div", new StyleDeclaration().Set("display", "block").Set("width", "200px"));
        fresh.AddElement(froot, "div", new StyleDeclaration().Set("display", "block").Set("height", "20px").Set("background-color", "red"));
        Assert.Equal(Sig(SlotPage(fresh).Display.Current), incremental);

        // and a scope given to the node in the reused slot is painted
        tree.SetDeclaration(reused, new StyleDeclaration().Set("display", "block").Set("height", "20px").Set("background-color", "red").SetHostScope(9));
        page.Flush();
        Assert.Equal(3, page.Display.Current.RequiredMinor);
    }

    [Fact]
    public void ImageInvalidationFollowsTheNodeNotTheSlot()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("width", "200px").Set("height", "100px"));
        StyledNode img = tree.AddElement(root, "img", new StyleDeclaration().Set("display", "block").Set("width", "40px").Set("height", "20px"));
        tree.SetReplacedSource(img, "a.png");
        LivePage page = SlotPage(tree);
        Assert.Equal(1, page.InvalidateImage(new ImageHandle(1)));

        tree.Remove(img);
        page.Flush();
        Assert.Equal(0, page.InvalidateImage(new ImageHandle(1)));

        StyledNode again = tree.AddElement(root, "img", new StyleDeclaration().Set("display", "block").Set("width", "40px").Set("height", "20px"));
        Assert.Equal(img.Index, again.Index);
        tree.SetReplacedSource(again, "b.png");
        page.Flush();
        Assert.Equal(0, page.InvalidateImage(new ImageHandle(1)));
        Assert.Equal(1, page.InvalidateImage(new ImageHandle(2)));
    }

    // ------------------------------------------------------------------ SVG keys

    [Fact]
    public void SvgKeyDependsOnMarkupAndColorOnly()
    {
        var a = new DisplayList();
        var b = new DisplayList();
        var red = new PaintColor(1, 0, 0, 1);
        a.SvgImage(new ItemId(1), new RectF(0, 0, 10, 10), "<svg><rect/></svg>", red);
        a.SvgImage(new ItemId(2), new RectF(0, 0, 10, 10), "<svg><rect/></svg>", new PaintColor(0, 1, 0, 1));
        b.FillRect(new ItemId(7), new RectF(0, 0, 1, 1), new Paint(PaintKind.Solid, red, 0));
        b.SvgImage(new ItemId(9), new RectF(50, 60, 99, 77), "<svg><rect/></svg>", red);
        b.SvgImage(new ItemId(10), new RectF(0, 0, 10, 10), "<svg><circle/></svg>", red);
        Assert.NotEqual(0UL, a[0].SvgKey);
        Assert.Equal(a[0].SvgKey, b[1].SvgKey);
        Assert.NotEqual(a[0].SvgKey, a[1].SvgKey);
        Assert.NotEqual(a[0].SvgKey, b[2].SvgKey);
        Assert.Equal("<svg><rect/></svg>", a[0].SvgMarkup);
        Assert.Equal(0UL, b[0].SvgKey);
    }

    [Fact]
    public void SvgKeyHasADocumentedValue()
    {
        // FNV-1a 64 over UTF-16 code units (low byte first), then R, G, B, A as little-endian single-precision bits.
        var list = new DisplayList();
        list.SvgImage(new ItemId(1), new RectF(0, 0, 4, 4), "<svg/>", new PaintColor(0, 0, 0, 1));
        ulong h = 0xCBF29CE484222325UL;
        void Byte(byte b) => h = (h ^ b) * 0x100000001B3UL;
        foreach (char c in "<svg/>")
        {
            Byte((byte)c);
            Byte((byte)(c >> 8));
        }

        foreach (float f in new[] { 0f, 0f, 0f, 1f })
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            for (int i = 0; i < 4; i++)
            {
                Byte((byte)(bits >> (8 * i)));
            }
        }

        Assert.Equal(h, list[0].SvgKey);
    }

    private const string SvgScene = "<svg width='20' height='20' viewBox='0 0 20 20' style='color:#c00'><rect width='20' height='20' fill='currentColor'/></svg>";

    [Fact]
    public void SvgKeyIsStableAcrossPaintsAndFollowsTheMarkup()
    {
        static ulong KeyOf(HtmlPage page)
        {
            DisplayList l = Paint(page).Current;
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i].Kind == ItemKind.SvgImage)
                {
                    Assert.False(string.IsNullOrEmpty(l[i].SvgMarkup));
                    return l[i].SvgKey;
                }
            }

            throw new InvalidOperationException("no svg item");
        }

        ulong first = KeyOf(Page("<p>" + SvgScene + "</p>"));
        ulong again = KeyOf(Page("<div><div>" + SvgScene + "</div></div>"));
        ulong other = KeyOf(Page("<p>" + SvgScene.Replace("<rect ", "<rect x='1' ", StringComparison.Ordinal) + "</p>"));
        ulong recolored = KeyOf(Page("<p>" + SvgScene.Replace("#c00", "#0c0", StringComparison.Ordinal) + "</p>"));
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.NotEqual(first, recolored);
    }

    // ------------------------------------------------------------------ face keys

    [Fact]
    public void FaceKeysAreTheSameInEveryListAndIndexesAreNot()
    {
        static (Dictionary<string, ulong> KeyByText, Dictionary<string, int> IndexByText) Keys(string body)
        {
            DisplayList l = Paint(Page(body)).Current;
            var keys = new Dictionary<string, ulong>();
            var index = new Dictionary<string, int>();
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i].Kind == ItemKind.GlyphRun)
                {
                    GlyphRunView r = l[i].GlyphRun;
                    string text = r.Text.ToString().Trim();
                    keys[text] = r.FaceKey;
                    index[text] = r.FaceIndex;
                    Assert.Equal(r.FaceKey, l.GetFaceKey(r.FaceIndex));
                }
            }

            return (keys, index);
        }

        (Dictionary<string, ulong> k1, Dictionary<string, int> i1) = Keys("<p><b>heavy</b> <i>slanted</i> plain</p>");
        (Dictionary<string, ulong> k2, Dictionary<string, int> i2) = Keys("<p style='font-style:italic'>slanted</p><p>plain</p><p style='font-weight:bold'>heavy</p>");
        Assert.Equal(3, k1.Values.Distinct().Count());
        Assert.Equal(k1["heavy"], k2["heavy"]);
        Assert.Equal(k1["slanted"], k2["slanted"]);
        Assert.Equal(k1["plain"], k2["plain"]);
        Assert.NotEqual(i1["heavy"], i2["heavy"]);
    }

    [Fact]
    public void FaceKeysAreStableWhenTheListIsRefilled()
    {
        HtmlPage page = Page("<p>one <b>two</b></p>");
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(300, 300) { Images = new Table() };
        var seen = new List<ulong>();
        for (int round = 0; round < 3; round++)
        {
            painter.Paint(page.Fragments, retained, options);
            DisplayList l = retained.Current;
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i].Kind == ItemKind.GlyphRun)
                {
                    seen.Add(l[i].GlyphRun.FaceKey);
                }
            }
        }

        Assert.True(seen.Count > 0 && seen.Count % 3 == 0);
        Assert.Equal(seen.Take(seen.Count / 3), seen.Skip(seen.Count / 3).Take(seen.Count / 3));
        Assert.Equal(seen.Take(seen.Count / 3), seen.Skip(2 * (seen.Count / 3)));
    }

    // ------------------------------------------------------------------ host scopes

    [Fact]
    public void AHostScopeRaisesTheRequiredMinorOnlyWhenUsed()
    {
        var plain = new DisplayList();
        plain.FillRect(new ItemId(1), new RectF(0, 0, 5, 5), new Paint(PaintKind.Solid, new PaintColor(1, 0, 0, 1), 0));
        plain.SvgImage(new ItemId(2), new RectF(0, 0, 5, 5), "<svg/>", default);
        Assert.Equal(2, plain.RequiredMinor);
        Assert.Equal(3, DisplayList.FormatMinor);

        var scoped = new DisplayList();
        scoped.PushHostScope(new ItemId(1), 42);
        scoped.FillRect(new ItemId(2), new RectF(0, 0, 5, 5), new Paint(PaintKind.Solid, new PaintColor(1, 0, 0, 1), 0));
        scoped.PopHostScope();
        scoped.Validate();
        Assert.Equal(3, scoped.RequiredMinor);
        Assert.Equal(ItemKind.PushHostScope, scoped[0].Kind);
        Assert.Equal(42, scoped[0].HostScope);
        Assert.Equal(2, scoped[0].MatchIndex);
        Assert.Equal(0, scoped[1].ParentIndex);
        Assert.Equal(0, scoped[1].HostScope);
        scoped.Clear();
        Assert.Equal(0, scoped.RequiredMinor);
    }

    [Fact]
    public void HostScopesNestWithEveryOtherScopeAndMustMatch()
    {
        var l = new DisplayList();
        var red = new Paint(PaintKind.Solid, new PaintColor(1, 0, 0, 1), 0);
        l.PushTransform(new ItemId(1), Matrix3x2.CreateTranslation(10, 0));
        l.PushHostScope(new ItemId(2), -5);
        l.PushClipRect(new ItemId(3), new RectF(0, 0, 20, 20));
        l.PushGroup(new ItemId(4), 0.5f);
        l.BeginTag(new ItemId(5), TagRole.Div);
        l.FillRect(new ItemId(6), new RectF(0, 0, 50, 50), red);
        l.PopGroup();
        l.PopClip();
        Assert.Throws<DisplayListException>(() => l.PopTransform());
        l.PopHostScope();
        l.PopTransform();
        l.Validate();
        Assert.Equal(new RectF(10, 0, 20, 20), l[5].RootBounds);
        Assert.Throws<DisplayListException>(() => l.PopHostScope());
    }

    [Fact]
    public void ScopesWithDifferentValuesChangeTheContextOfTheirContent()
    {
        static (ulong Content, ulong Context) Build(int scope)
        {
            var l = new DisplayList();
            l.PushHostScope(new ItemId(1), scope);
            l.FillRect(new ItemId(2), new RectF(0, 0, 5, 5), new Paint(PaintKind.Solid, new PaintColor(1, 0, 0, 1), 0));
            l.PopHostScope();
            return (l[1].ContentHash, l[1].ContextHash);
        }

        (ulong c1, ulong x1) = Build(1);
        (ulong c2, ulong x2) = Build(2);
        Assert.Equal(c1, c2);
        Assert.NotEqual(x1, x2);

        // So changing only the scope of a retained list damages what is inside it and nothing else.
        var retained = new RetainedDisplayList();
        foreach (int scope in new[] { 1, 2 })
        {
            DisplayList l = retained.Begin();
            l.FillRect(new ItemId(9), new RectF(100, 100, 5, 5), new Paint(PaintKind.Solid, new PaintColor(0, 1, 0, 1), 0));
            l.PushHostScope(new ItemId(1), scope);
            l.FillRect(new ItemId(2), new RectF(0, 0, 5, 5), new Paint(PaintKind.Solid, new PaintColor(1, 0, 0, 1), 0));
            l.PopHostScope();
            retained.Commit();
            if (scope == 1)
            {
                retained.ClearDamage();
            }
        }

        Assert.Equal(1, retained.Damage.Count);
        Assert.Equal(new RectF(0, 0, 5, 5), retained.Damage[0]);
    }

    [Fact]
    public void AnElementWithTheCustomPropertyPaintsInsideAScopeBetweenItsEffectsAndItsBox()
    {
        HtmlPage page = Page(
            "<div id=a style='--host-scope:7;opacity:.5;transform:translate(3px,4px);overflow:hidden;width:50px;height:30px;background:#09f'>" +
            "<div id=inner style='width:10px;height:10px;background:#f90'>text</div>" +
            "<div id=nested style='--host-scope:9;width:10px;height:10px;background:#0f9'></div></div>");
        DisplayList l = Paint(page).Current;
        Assert.Equal(3, l.RequiredMinor);
        List<ItemKind> kinds = Kinds(l);
        int t = kinds.IndexOf(ItemKind.PushTransform);
        Assert.True(t >= 0);
        Assert.Equal(ItemKind.PushGroup, kinds[t + 1]);
        Assert.Equal(ItemKind.PushHostScope, kinds[t + 2]);
        Assert.Equal(7, l[t + 2].HostScope);
        Assert.Equal(ItemKind.FillRect, kinds[t + 3]);
        Assert.Equal(ItemKind.PushClipRect, kinds[t + 4]);
        int close = l[t + 2].MatchIndex;
        Assert.Equal(ItemKind.PopHostScope, kinds[close]);
        Assert.Equal(ItemKind.PopGroup, kinds[close + 1]);
        Assert.Equal(ItemKind.PopTransform, kinds[close + 2]);
        Assert.Equal(ItemKind.PopClip, kinds[close - 1]);

        // The child that merely inherits the custom property adds no scope; the one that sets another value adds a nested one.
        var values = new List<int>();
        for (int i = 0; i < l.Count; i++)
        {
            if (l[i].Kind == ItemKind.PushHostScope)
            {
                values.Add(l[i].HostScope);
            }
        }

        Assert.Equal([7, 9], values);
    }

    [Fact]
    public void AStateRuleCanSwitchTheScopeOnAndOff()
    {
        InteractivePage p = Ix.Page("<div id=a class=x style='width:50px;height:50px;background:#09f'></div>", extraStyle: ".x:hover{--host-scope:5}");
        var painter = new Painter();
        var list = new RetainedDisplayList();
        var paint = new PaintOptions(400, 300) { Scroll = p.Page.Scroll };
        painter.Paint(p.Page.Fragments, list, paint);
        Assert.Equal(0, list.Current.RequiredMinor);
        p.HandlePointer(Ix.Move(10, 10));
        p.Repaint(painter, list, paint);
        Assert.Equal(3, list.Current.RequiredMinor);
        p.HandlePointer(Ix.Move(300, 200));
        p.Repaint(painter, list, paint);
        Assert.Equal(0, list.Current.RequiredMinor);
    }

    [Fact]
    public void NoneAndInvalidValuesMakeNoScope()
    {
        HtmlPage page = Page("<div style='--host-scope:none;width:5px;height:5px;background:red'></div><div style='--host-scope:abc;width:5px;height:5px;background:red'></div><div style='--host-scope:1.5;width:5px;height:5px;background:red'></div>");
        Assert.Equal(0, Paint(page).Current.RequiredMinor);
    }

    [Fact]
    public void TheBuilderStyleSetsAScopeAndNegativeAndZeroValuesWork()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("width", "100px").Set("height", "100px"));
        foreach (int v in new[] { 0, -2147483648, 2147483647, 17 })
        {
            tree.AddElement(root, "div", new StyleDeclaration().SetHostScope(v).Set("display", "block").Set("width", "10px").Set("height", "10px").Set("background-color", "red"));
        }

        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()));
        DisplayList l = page.Display.Current;
        var values = new List<int>();
        for (int i = 0; i < l.Count; i++)
        {
            if (l[i].Kind == ItemKind.PushHostScope)
            {
                values.Add(l[i].HostScope);
            }
        }

        Assert.Equal([0, -2147483648, 2147483647, 17], values);
    }

    [Fact]
    public void ScopedContentPatchesWithTheScroll()
    {
        const string Body =
            "<div style='height:1000px'><div id=s style='--host-scope:3;position:relative;width:60px;height:60px;background:#369'>" +
            "<div style='position:absolute;left:5px;top:5px;width:10px;height:10px;background:#c60'></div></div></div>";
        HtmlPage page = Page(Body);
        var painter = new Painter();
        RetainedDisplayList retained = Paint(page, painter);
        page.Scroll.SetViewportOffset(0, 120);
        painter.PatchScroll(retained);
        HtmlPage fresh = Page(Body);
        fresh.Scroll.SetViewportOffset(0, 120);
        RetainedDisplayList full = Paint(fresh);
        Assert.Equal(Sig(full.Current), Sig(retained.Current));
        Assert.Equal(3, retained.Current.RequiredMinor);
    }

    [Fact]
    public void IncrementalEditsOfScopedElementsEqualAFreshBuild()
    {
        static LivePage Build(string[] cssByNode, out StyledNode[] nodes)
        {
            var tree = new StyledTree();
            StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("width", "300px").Set("height", "200px"));
            nodes = new StyledNode[cssByNode.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = tree.AddElement(root, "div", Decl(cssByNode[i]));
                tree.AddText(nodes[i], "node " + i.ToString(CultureInfo.InvariantCulture));
            }

            return new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()));
        }

        static StyleDeclaration Decl(string css)
        {
            var d = new StyleDeclaration();
            foreach (string part in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int c = part.IndexOf(':', StringComparison.Ordinal);
                d.Set(part[..c].Trim(), part[(c + 1)..].Trim());
            }

            return d;
        }

        string[] start = ["height:30px;background-color:red", "height:30px;opacity:.5", "height:30px;--host-scope:2", "height:30px"];
        LivePage live = Build(start, out StyledNode[] nodes);
        string[] state = (string[])start.Clone();
        var rng = new Random(11);
        string[] choices = ["--host-scope:1", "--host-scope:5", "--host-scope:none", "opacity:.4", "transform:rotate(5deg)", "overflow:hidden", "background-color:blue", "position:relative", "display:inline-block"];
        for (int step = 0; step < 60; step++)
        {
            int n = rng.Next(state.Length);
            string css = "height:30px;" + string.Join(';', Enumerable.Range(0, rng.Next(0, 4)).Select(_ => choices[rng.Next(choices.Length)]).Distinct());
            state[n] = css;
            live.Styled.SetDeclaration(nodes[n], Decl(css));
            live.Flush();
            LivePage fresh = Build(state, out _);
            Assert.Equal(EditSession.Snapshot(fresh), EditSession.Snapshot(live));
        }
    }

    [Fact]
    public void TheImageBackendDrawsScopedContentAsIfTheScopeWerenotThere()
    {
        const string Inner = "width:40px;height:30px;background:#36c;border:3px solid #c30;";
        byte[] Render(string style)
        {
            HtmlPage page = Page("<div style='" + style + "'>hello</div>", 120, 80);
            var list = new DisplayList();
            page.Paint(list);
            return DisplayListRasterizer.Render(list, 120, 80, new RasterOptions()).Pixels;
        }

        byte[] without = Render(Inner + "isolation:isolate");
        byte[] with = Render(Inner + "--host-scope:5");
        Assert.Equal(without, with);
    }

    // ------------------------------------------------------------------ names and synthetic clicks

    private const string Form =
        "<div id=outer><button id=go name=gobtn>Go</button><input id=chk type=checkbox name=agree>" +
        "<button id=off disabled name=nope>x</button><input name=lonely><input name=dup><input id=dup name=other></div>";

    [Fact]
    public void ElementsAreFoundByIdThenByFormControlName()
    {
        InteractivePage p = Ix.Page(Form);
        Assert.Equal("go", Ix.IdOf(p.FindElement("go")));
        Assert.Equal("chk", Ix.IdOf(p.FindElement("agree")));
        Assert.Equal("lonely", p.FindElement("lonely").GetAttribute("name"));
        Assert.True(p.FindElement("missing").IsNull);
        Assert.True(p.FindElement(string.Empty).IsNull);
        Assert.True(p.FindElement("outer").IsElement);
        // The id wins over a name, even when the named control comes first in the document.
        Assert.Equal("dup", Ix.IdOf(p.FindElement("dup")));
        Assert.Equal("other", p.FindElement("dup").GetAttribute("name"));
        // A name only counts on form controls.
        InteractivePage q = Ix.Page("<div name=x></div><span name=x></span>");
        Assert.True(q.FindElement("x").IsNull);
        Assert.Throws<ArgumentNullException>(() => p.FindElement(null!));
    }

    [Fact]
    public void NameLookupAllocatesNothing()
    {
        InteractivePage p = Ix.Page(Form);
        Node n = default;
        void Find() => n = p.FindElement("lonely");
        Allocation.AssertZero(Find, "find by name", warmup: 4, iterations: 20);
        Assert.False(n.IsNull);
    }

    [Fact]
    public void ClickDispatchesTheClickEventAlongTheNormalPathAndNothingElse()
    {
        InteractivePage p = Ix.Page(Form);
        Node go = p.FindElement("gobtn");
        Node outer = p.FindElement("outer");
        Node html = p.Page.Document.DocumentElement;
        var log = new List<string>();
        foreach ((EventTarget t, string name) in new[] { (EventTarget.Window, "win"), (p.DocumentTarget, "doc"), (EventTarget.Of(html), "html"), (EventTarget.Of(outer), "outer"), (EventTarget.Of(go), "go") })
        {
            Ix.Record(p, log, t, name + "/cap", capture: true, "click", "pointerdown", "mousedown", "pointerup", "mouseup", "focus", "focusin");
            Ix.Record(p, log, t, name + "/bub", capture: false, "click", "pointerdown", "mousedown", "pointerup", "mouseup", "focus", "focusin");
        }

        double seen = -1;
        p.Events.AddEventListener(EventTarget.Of(go), "click", e => seen = e.TimeStamp);
        p.Click(go, 1234);
        Assert.Equal(
            [
                "click@win/cap", "click@doc/cap", "click@html/cap", "click@outer/cap",
                "click@go/cap*", "click@go/bub*",
                "click@outer/bub", "click@html/bub", "click@doc/bub", "click@win/bub",
            ],
            log);
        Assert.Equal(1234, seen);
        Assert.True(p.FocusedElement.IsNull);
    }

    [Fact]
    public void ClickTogglesACheckboxBeforeListenersSeeItAndBackWhenCanceled()
    {
        InteractivePage p = Ix.Page(Form);
        Node chk = p.FindElement("agree");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(chk), "click", e => log.Add("click:" + p.IsChecked(chk)));
        p.Events.AddEventListener(EventTarget.Of(chk), "input", e => log.Add("input:" + p.IsChecked(chk)));
        p.Events.AddEventListener(EventTarget.Of(chk), "change", e => log.Add("change:" + p.IsChecked(chk)));
        p.Click(chk, 1);
        Assert.Equal(["click:True", "input:True", "change:True"], log);
        Assert.True(p.IsChecked(chk));
        p.Click(chk, 2);
        Assert.False(p.IsChecked(chk));

        log.Clear();
        p.Events.AddEventListener(EventTarget.Of(chk), "click", e => e.PreventDefault());
        p.Click(chk, 3);
        Assert.False(p.IsChecked(chk));
        Assert.Equal(["click:True"], log);
    }

    [Fact]
    public void ClickOnADisabledControlDoesNothingAndForeignNodesAreRefused()
    {
        InteractivePage p = Ix.Page(Form);
        var log = new List<string>();
        Ix.Record(p, log, p.DocumentTarget, "doc", capture: true, "click");
        p.Click(p.FindElement("off"), 1);
        Assert.Empty(log);
        InteractivePage other = Ix.Page(Form);
        Assert.Throws<ArgumentException>(() => p.Click(other.FindElement("go"), 1));
        Assert.Throws<ArgumentException>(() => p.Click(default, 1));
    }

    [Fact]
    public void ClickingALabelActivatesItsControl()
    {
        InteractivePage p = Ix.Page("<label id=l for=c>text</label><input id=c type=checkbox>");
        p.Click(p.FindElement("l"), 5);
        Assert.True(p.IsChecked(p.FindElement("c")));
    }

    // ------------------------------------------------------------------ builder names

    [Fact]
    public void BuilderNodesAreFoundByTheNameTheyWereGiven()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div");
        StyledNode a = tree.AddElement(root, "div");
        StyledNode b = tree.AddElement(a, "span");
        tree.SetName(a, "panel");
        tree.SetName(b, "label");
        Assert.Equal(a, tree.FindByName("panel"));
        Assert.Equal("label", tree.NameOf(b));
        Assert.True(tree.FindByName("nothing").IsNull);

        // A name moves when it is given to another node; a node has one name.
        tree.SetName(b, "panel");
        Assert.Equal(b, tree.FindByName("panel"));
        Assert.Null(tree.NameOf(a));
        tree.SetName(b, "renamed");
        Assert.True(tree.FindByName("panel").IsNull);
        Assert.Equal(b, tree.FindByName("renamed"));
        tree.SetName(b, null);
        Assert.True(tree.FindByName("renamed").IsNull);

        // A removed node is not found; clearing the tree forgets every name.
        tree.SetName(a, "panel");
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()));
        page.Styled.Remove(a);
        Assert.True(page.Styled.FindByName("panel").IsNull);
        tree.Clear();
        Assert.True(tree.FindByName("panel").IsNull);
        Assert.Throws<ArgumentException>(() => tree.SetName(default, "x"));
    }

    [Fact]
    public void AnHtmlStyledTreeFindsElementsByIdAndFormName()
    {
        HtmlPage page = Page("<div id=box><input name=field></div>");
        StyledNode box = page.Styled.FindByName("box");
        Assert.False(box.IsNull);
        Assert.Equal("div", box.TagName);
        StyledNode field = page.Styled.FindByName("field");
        Assert.Equal("input", field.TagName);
    }

    // ------------------------------------------------------------------ an animated layout property

    [Fact]
    public void ATransitionOnOneRowsWidthTicksEveryFrameAtZeroBytes()
    {
        string html = GameUiScene.Html().Replace("</style>", "#quest3{width:100px;transition:width 100000s linear}#quest3:hover{width:400000px}</style>", StringComparison.Ordinal);
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage page = InteractivePage.Create(html, options, new InteractionOptions { AnimationStartTime = 0 });
        var painter = new Painter();
        var list = new RetainedDisplayList();
        var paint = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = page.Page.Scroll };
        painter.Paint(page.Page.Fragments, list, paint);
        page.Advance(0);
        Node row = page.FindElement("quest3");
        Rect r = page.Layout.GetBoundingClientRect(row);
        page.HandlePointer(new PointerInput { Action = PointerAction.Move, X = r.X + 5, Y = r.Y + (r.Height / 2), Time = 0 });
        painter.Paint(page.Page.Fragments, list, paint);
        double t = 0;
        InputOutcome last = InputOutcome.None;
        void Frame()
        {
            t += 16;
            last = page.Advance(t);
            if ((last & InputOutcome.Relaid) != 0)
            {
                page.Repaint(painter, list, paint);
            }
            else
            {
                page.PatchAnimations(painter, list);
            }
        }

        for (int i = 0; i < 50; i++)
        {
            Frame();
        }

        double before = page.Layout.GetBoundingClientRect(row).Width;
        Assert.True((last & InputOutcome.Relaid) != 0, "each frame lays the row out again");
        Allocation.AssertZero(Frame, "a width transition ticked through Advance and Repaint", warmup: 100, iterations: 100);
        double after = page.Layout.GetBoundingClientRect(row).Width;
        Assert.True(after > before + 1, "the row grows: " + before + " to " + after);
    }
}
