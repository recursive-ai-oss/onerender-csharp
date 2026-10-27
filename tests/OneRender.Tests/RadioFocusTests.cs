using System.Linq;
using OneRender.Interaction;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

// HTML "radio button group" with sequential focus navigation: one tab stop per group, arrows move and check.
public class RadioFocusTests
{
    private static List<string> Tabs(InteractivePage p, bool forward, int steps)
    {
        var seen = new List<string>();
        for (int i = 0; i < steps; i++)
        {
            p.HandleKey(Ix.Key("Tab", m: forward ? KeyModifiers.None : KeyModifiers.Shift));
            seen.Add(Ix.IdOf(p.FocusedElement));
        }

        return seen;
    }

    private static List<string> Order(InteractivePage p) => p.GetTabOrder().Select(Ix.IdOf).ToList();

    private const string Group = "<input type=radio name=g id=r1><input type=radio name=g id=r2><input type=radio name=g id=r3>";

    [Fact]
    public void CheckedAttributeMakesThatRadioTheOnlyStop()
    {
        InteractivePage p = Ix.Page("<button id=a></button><input type=radio name=g id=r1><input type=radio name=g id=r2 checked><input type=radio name=g id=r3><button id=z></button>");
        Assert.Equal(["a", "r2", "z", "a"], Tabs(p, true, 4));
        Assert.Equal(["z", "r2", "a"], Tabs(p, false, 3));
    }

    [Fact]
    public void SetCheckedAfterCreationMovesTheStop()
    {
        InteractivePage p = Ix.Page("<button id=a></button>" + Group + "<button id=z></button>");
        p.SetChecked(Ix.Id(p, "r3"), true);
        Assert.Equal(["a", "r3", "z"], Order(p));
        p.SetChecked(Ix.Id(p, "r1"), true);
        Assert.Equal(["a", "r1", "z"], Order(p));
        Assert.Equal(["a", "r1", "z"], Tabs(p, true, 3));
    }

    [Fact]
    public void SetCheckedOverridesTheAttribute()
    {
        InteractivePage p = Ix.Page("<input type=radio name=g id=r1 checked><input type=radio name=g id=r2>");
        p.SetChecked(Ix.Id(p, "r2"), true);
        Assert.Equal(["r2"], Order(p));
    }

    [Fact]
    public void NoneCheckedStopsAtFirstForwardAndLastBackward()
    {
        InteractivePage p = Ix.Page("<button id=a></button>" + Group + "<button id=z></button>");
        Assert.Equal(["a", "r1", "z"], Tabs(p, true, 3));
        InteractivePage q = Ix.Page("<button id=a></button>" + Group + "<button id=z></button>");
        Assert.Equal(["z", "r3", "a"], Tabs(q, false, 3));
    }

    [Fact]
    public void ClickingARadioMakesItTheStop()
    {
        InteractivePage p = Ix.Page("<button id=a></button>" + Group + "<button id=z></button>");
        p.Click(Ix.Id(p, "r2"), 1);
        Assert.True(p.IsChecked(Ix.Id(p, "r2")));
        p.Blur();
        Assert.Equal(["a", "r2", "z"], Tabs(p, true, 3));
    }

    [Fact]
    public void ArrowsMoveWithinTheGroupCheckAndWrap()
    {
        InteractivePage p = Ix.Page("<button id=a></button>" + Group + "<button id=z></button>");
        p.HandleKey(Ix.Key("Tab"));
        p.HandleKey(Ix.Key("Tab"));
        Assert.Equal("r1", Ix.IdOf(p.FocusedElement));
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal("r2", Ix.IdOf(p.FocusedElement));
        Assert.True(p.IsChecked(Ix.Id(p, "r2")));
        p.HandleKey(Ix.Key("ArrowRight"));
        p.HandleKey(Ix.Key("ArrowRight"));
        Assert.Equal("r1", Ix.IdOf(p.FocusedElement));
        Assert.True(p.IsChecked(Ix.Id(p, "r1")));
        Assert.False(p.IsChecked(Ix.Id(p, "r3")));
        p.HandleKey(Ix.Key("ArrowUp"));
        Assert.Equal("r3", Ix.IdOf(p.FocusedElement));
        Assert.True(p.IsChecked(Ix.Id(p, "r3")));
        Assert.False(p.IsChecked(Ix.Id(p, "r1")));
        p.HandleKey(Ix.Key("Tab"));
        Assert.Equal("z", Ix.IdOf(p.FocusedElement));
        p.HandleKey(Ix.Key("Tab", m: KeyModifiers.Shift));
        Assert.Equal("r3", Ix.IdOf(p.FocusedElement));
    }

    [Fact]
    public void ArrowsSkipDisabledRadios()
    {
        InteractivePage p = Ix.Page("<input type=radio name=g id=r1><input type=radio name=g id=r2 disabled><input type=radio name=g id=r3>");
        p.HandleKey(Ix.Key("Tab"));
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal("r3", Ix.IdOf(p.FocusedElement));
    }

    [Fact]
    public void SeparateFormsWithTheSameNameAreSeparateGroups()
    {
        InteractivePage p = Ix.Page(
            "<form id=f1><input type=radio name=g id=a1><input type=radio name=g id=a2 checked></form>" +
            "<form id=f2><input type=radio name=g id=b1><input type=radio name=g id=b2></form>");
        Assert.Equal(["a2", "b1"], Order(p));
        p.HandleKey(Ix.Key("Tab"));
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal("a1", Ix.IdOf(p.FocusedElement));
        Assert.False(p.IsChecked(Ix.Id(p, "b1")));
    }

    [Fact]
    public void DifferentNamesAndNamelessRadiosAreSeparateStops()
    {
        InteractivePage p = Ix.Page("<input type=radio name=g id=g1><input type=radio name=h id=h1><input type=radio id=n1><input type=radio id=n2>");
        Assert.Equal(["g1", "h1", "n1", "n2"], Order(p));
    }

    [Fact]
    public void MixedWithCheckboxesAndTextInputs()
    {
        InteractivePage p = Ix.Page("<input id=t><input type=checkbox id=c>" + Group + "<input type=checkbox id=c2><textarea id=ta></textarea>");
        p.SetChecked(Ix.Id(p, "r2"), true);
        Assert.Equal(["t", "c", "r2", "c2", "ta"], Order(p));
    }
}
