using OneRender.Interaction;
using RecursiveAI.OneHtml;
using Xunit;
using System.Linq;

namespace OneRender.Tests;

/// <summary>The select control: type-ahead, modifier multi-selection in list boxes and the drop-down in the top layer.</summary>
public class SelectEditingTests
{
    private const string Fruit = "<option>apple</option><option>banana</option><option>blueberry</option><option>cherry</option><option>cranberry</option>";

    private static KeyInput KeyAt(string key, double time, KeyModifiers m = KeyModifiers.None) => new() { Key = key, Down = true, Time = time, Modifiers = m };

    private static bool IsSelected(InteractivePage p, Node option) =>
        p.Page.Form.TryGetSelected(option, out bool on) ? on : option.HasAttribute("selected");

    private static string Chosen(InteractivePage p, string id = "s")
    {
        var sb = new System.Text.StringBuilder();
        for (Node o = Ix.Id(p, id).FirstChild; !o.IsNull; o = o.NextSibling)
        {
            if (o.IsElement && IsSelected(p, o))
            {
                sb.Append(o.TextContent).Append(',');
            }
        }

        return sb.ToString();
    }

    private static InputOutcome ClickOption(InteractivePage p, int index, KeyModifiers m = KeyModifiers.None, double t = 0)
    {
        Node option = Ix.Id(p, "s").FirstChild;
        for (int i = 0; i < index; i++)
        {
            option = option.NextSibling;
        }

        Layout.Rect r = p.Layout.GetBoundingClientRect(option);
        double x = r.X + 5, y = r.Y + (r.Height / 2);
        p.HandlePointer(new PointerInput { Action = PointerAction.Move, X = x, Y = y, Time = t, Modifiers = m });
        p.HandlePointer(new PointerInput { Action = PointerAction.Down, X = x, Y = y, Time = t, Button = 0, Buttons = MouseButtons.Primary, Modifiers = m });
        return p.HandlePointer(new PointerInput { Action = PointerAction.Up, X = x, Y = y, Time = t + 1, Button = 0, Modifiers = m });
    }

    [Fact]
    public void TypingChoosesTheOptionThatStartsWithThePrefixAndTheBufferTimesOut()
    {
        InteractivePage p = Ix.Page("<select id=s>" + Fruit + "</select>");
        p.Focus(Ix.Id(p, "s"), FocusReason.Keyboard);
        var changes = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "s")), "change", _ => changes.Add(Chosen(p)));
        p.HandleKey(KeyAt("b", 0));
        Assert.Equal("banana,", Chosen(p));
        p.HandleKey(KeyAt("l", 300));
        Assert.Equal("blueberry,", Chosen(p));
        p.HandleKey(KeyAt("c", 3000));
        Assert.Equal("cherry,", Chosen(p));
        p.HandleKey(KeyAt("c", 3200));
        Assert.Equal("cranberry,", Chosen(p));
        p.HandleKey(KeyAt("c", 3400));
        Assert.Equal("cherry,", Chosen(p));
        Assert.Equal(5, changes.Count);
    }

    [Fact]
    public void TypingInAnOpenDropDownMovesTheHighlightOnly()
    {
        InteractivePage p = Ix.Page("<select id=s>" + Fruit + "</select>");
        p.Focus(Ix.Id(p, "s"), FocusReason.Keyboard);
        p.HandleKey(KeyAt(" ", 0));
        Assert.True(p.IsDropdownOpen(Ix.Id(p, "s")));
        p.HandleKey(KeyAt("c", 2000));
        Assert.Equal(0, p.GetSelectedIndex(Ix.Id(p, "s")));
        p.HandleKey(KeyAt("Enter", 2100));
        Assert.Equal("cherry,", Chosen(p));
        Assert.False(p.IsDropdownOpen(Ix.Id(p, "s")));
    }

    [Fact]
    public void CtrlClickTogglesAndShiftClickSelectsARangeInAMultipleList()
    {
        InteractivePage p = Ix.Page("<select id=s multiple size=5>" + Fruit + "</select>");
        ClickOption(p, 1);
        Assert.Equal("banana,", Chosen(p));
        ClickOption(p, 3, KeyModifiers.Control, 1000);
        Assert.Equal("banana,cherry,", Chosen(p));
        ClickOption(p, 1, KeyModifiers.Control, 2000);
        Assert.Equal("cherry,", Chosen(p));
        ClickOption(p, 1, KeyModifiers.None, 3000);
        ClickOption(p, 3, KeyModifiers.Shift, 4000);
        Assert.Equal("banana,blueberry,cherry,", Chosen(p));
        ClickOption(p, 4, KeyModifiers.Shift | KeyModifiers.Control, 5000);
        Assert.Equal("banana,blueberry,cherry,cranberry,", Chosen(p));
        ClickOption(p, 0, KeyModifiers.None, 6000);
        Assert.Equal("apple,", Chosen(p));
    }

    [Fact]
    public void ShiftArrowsExtendAndCtrlASelectsAll()
    {
        InteractivePage p = Ix.Page("<select id=s multiple size=5>" + Fruit + "</select>");
        ClickOption(p, 1);
        p.HandleKey(KeyAt("ArrowDown", 10, KeyModifiers.Shift));
        p.HandleKey(KeyAt("ArrowDown", 11, KeyModifiers.Shift));
        Assert.Equal("banana,blueberry,cherry,", Chosen(p));
        p.HandleKey(KeyAt("ArrowUp", 12, KeyModifiers.Shift));
        Assert.Equal("banana,blueberry,", Chosen(p));
        p.HandleKey(KeyAt("a", 13, KeyModifiers.Control));
        Assert.Equal("apple,banana,blueberry,cherry,cranberry,", Chosen(p));
    }

    [Fact]
    public void TheOpenListIsInTheTopLayerAboveAndOutsideAnOverflowClip()
    {
        InteractivePage p = Ix.Page(
            "<div style='overflow:hidden;height:30px;width:200px;position:relative;z-index:1'><select id=s>" + Fruit + "</select></div><div id=below style='position:relative;z-index:5;height:300px;background:#fee'>x</div>");
        Node select = Ix.Id(p, "s");
        Layout.Rect box = p.Layout.GetBoundingClientRect(select);
        p.HandlePointer(Ix.Down(box.X + 5, box.Y + 5, 0));
        p.HandlePointer(Ix.Up(box.X + 5, box.Y + 5, 1));
        Assert.True(p.IsDropdownOpen(select));
        Layout.Rect third = p.Layout.GetBoundingClientRect(select.FirstChild.NextSibling.NextSibling);
        Assert.True(third.Y > 40, third.Y.ToString());
        Node hit = p.ElementFromPoint(third.X + 6, third.Y + (third.Height / 2));
        Assert.Equal("option", hit.LocalName);
        var list = new OneRender.Painting.DisplayList();
        p.Page.Paint(list);
        int lastFill = -1, lastGlyph = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == OneRender.Painting.ItemKind.GlyphRun)
            {
                lastGlyph = i;
            }

            if (list[i].Kind == OneRender.Painting.ItemKind.FillRect && list[i].RootBounds.Height > 100)
            {
                lastFill = i;
            }
        }

        Assert.True(lastFill >= 0 && lastGlyph > lastFill);
        p.HandlePointer(Ix.Down(third.X + 6, third.Y + (third.Height / 2), 100));
        p.HandlePointer(Ix.Up(third.X + 6, third.Y + (third.Height / 2), 101));
        Assert.Equal("blueberry,", Chosen(p));
    }
}
