using System;
using System.Diagnostics;
using System.Text;
using OneRender.Layout;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Flex layout under load: retained storage, huge and deep trees, the exponential trap of nested measurements, and consistency of the fragment tree.</summary>
public class FlexStressTests
{
    private static readonly LayoutInput NoFontInput = new(800, double.NaN, new NoFonts());

    private sealed class NoFonts : OneRender.Layout.IFontProvider
    {
        public OneRender.Text.FontCatalog Catalog => throw new NotSupportedException();

        public OneRender.Text.ResolvedFontList Resolve(in ComputedValues style) => throw new NotSupportedException();

        public OneRender.Text.ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

        public OneRender.Text.FontMetricsInfo GetMetrics(in ComputedValues style) => throw new NotSupportedException();
    }

    private static int CountTree(FragmentTree f)
    {
        int n = 0;
        Fragment x = f.Root;
        var stack = new System.Collections.Generic.Stack<Fragment>();
        stack.Push(x);
        while (stack.Count > 0)
        {
            Fragment cur = stack.Pop();
            n++;
            for (Fragment c = cur.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
        }

        return n;
    }

    [Fact]
    public void RelayoutOfAnUnchangedThousandItemFlexPageAllocatesNothing()
    {
        var sb = new StringBuilder("<div id=c style='display:flex;flex-wrap:wrap;width:800px;gap:3px;align-items:stretch'>");
        for (int i = 0; i < 500; i++)
        {
            sb.Append("<div style='flex:").Append(i % 3).Append(" 1 ").Append(20 + i % 40).Append("px;min-height:").Append(i % 17).Append("px'><div style='height:").Append(i % 9).Append("px'></div></div>");
        }

        sb.Append("<div style='display:flex;flex-direction:column;width:300px;height:400px'>");
        for (int i = 0; i < 200; i++)
        {
            sb.Append("<div style='flex:1;margin:").Append(i % 4).Append("px'><div style='display:flex;justify-content:space-between'><div style='width:10px;height:5px'></div><div style='width:20px;height:").Append(i % 7).Append("px;align-self:center'></div></div></div>");
        }

        sb.Append("</div></div>");
        FlexLayoutTests.Lay l = FlexLayoutTests.Run(sb.ToString());
        Assert.True(l.P.Boxes.Count >= 1000, "boxes " + l.P.Boxes.Count);
        string dump = l.F.Dump();
        Allocation.AssertZero(() => l.Engine.Layout(l.P.Boxes, NoFontInput, l.F), "relayout of an unchanged flex page with 1,000+ items");
        Assert.Equal(dump, l.F.Dump());
    }

    [Fact]
    public void TrialLayoutsLeaveNoFragmentsBehind()
    {
        FlexLayoutTests.Lay l = FlexLayoutTests.Run(
            "<div id=c style='display:flex;flex-wrap:wrap;width:300px'>"
            + "<div style='width:100px'><div style='height:10px'></div><div style='display:flex'><div style='width:5px;height:5px'></div></div></div>"
            + "<div style='width:100px'><div style='display:flex;flex-direction:column'><div style='height:7px'></div></div></div>"
            + "<div style='width:150px'><div style='height:3px'></div></div></div>");
        int boxesWithFragments = 0;
        for (int i = 0; i < l.P.Boxes.Count; i++)
        {
            Box b = l.P.Boxes.Make(i);
            if (b.Kind == BoxKind.Container && !l.F.FragmentOf(b).IsNull)
            {
                boxesWithFragments++;
                Assert.True(l.F.FragmentOf(b).NextOfBox.IsNull, "one fragment per box");
            }
        }

        Assert.Equal(boxesWithFragments, l.F.Count);
        Assert.Equal(l.F.Count, CountTree(l.F));
    }

    [Fact]
    public void AHundredThousandItemsLayOutInBoundedTime()
    {
        foreach (string wrap in new[] { "nowrap", "wrap" })
        {
            var styled = new StyledTree();
            var boxes = new BoxTree(styled);
            StyledNode root = styled.AddRoot("div", new StyleDeclaration().Set("display", "flex").Set("flex-wrap", wrap));
            StyleDeclaration item = new StyleDeclaration().Set("width", "10px").Set("height", "5px").Set("flex-grow", "1");
            for (int i = 0; i < 100_000; i++)
            {
                styled.AddElement(root, "div", item);
            }

            boxes.Build();
            var f = new FragmentTree(boxes);
            var sw = Stopwatch.StartNew();
            new BlockLayoutEngine().Layout(boxes, new LayoutInput(1000, double.NaN, new NoFonts()), f);
            sw.Stop();
            Assert.True(sw.Elapsed.TotalSeconds < 20, wrap + " took " + sw.Elapsed);
            Assert.Equal(100_001, f.Count);
            if (wrap == "nowrap")
            {
                Assert.Equal(0.01, f.Root.LastChild.BorderBox.Width, 6);
            }
            else
            {
                Assert.Equal(5.0 * 1000, f.Root.BorderBox.Height, 6);
            }
        }
    }

    [Fact]
    public void ADeepFlexNestLaysOutWithoutRecursionAndInLinearTime()
    {
        foreach (int depth in new[] { 1_000, 50_000 })
        {
            var styled = new StyledTree();
            var boxes = new BoxTree(styled);
            StyleDeclaration flex = new StyleDeclaration().Set("display", "flex").Set("padding-left", "1px");
            StyledNode n = styled.AddRoot("div", flex);
            for (int i = 1; i < depth; i++)
            {
                n = styled.AddElement(n, "div", flex);
            }

            styled.AddElement(n, "div", new StyleDeclaration().Set("width", "10px").Set("height", "10px"));
            boxes.Build();
            var f = new FragmentTree(boxes);
            var sw = Stopwatch.StartNew();
            var eng = new BlockLayoutEngine();
            eng.Layout(boxes, new LayoutInput(1_000_000, double.NaN, new NoFonts()), f);
            sw.Stop();
            Assert.True(eng.FlexMeasurements <= 2L * depth, "measurements " + eng.FlexMeasurements);
            Assert.True(sw.Elapsed.TotalSeconds < 20, depth + " levels took " + sw.Elapsed);
            Assert.Equal(depth + 1, f.Count);
            Assert.Equal(10, f.Root.BorderBox.Height, 6);
            Assert.Equal(1_000_000, f.Root.BorderBox.Width, 6);
        }

    }

    [Fact]
    public void NestedMeasurementsDoNotMultiplyAtEveryLevel()
    {
        // Each level has the deep container and a shorter sibling that is stretched to its height, which asks for a second layout of the
        // sibling's subtree; without memoized measurements this doubles at every level.
        foreach (int depth in new[] { 40, 2_000 })
        {
            var styled = new StyledTree();
            var boxes = new BoxTree(styled);
            StyleDeclaration row = new StyleDeclaration().Set("display", "flex");
            StyleDeclaration col = new StyleDeclaration().Set("display", "flex").Set("flex-direction", "column");
            StyleDeclaration leaf = new StyleDeclaration().Set("height", "5px").Set("width", "5px");
            StyledNode n = styled.AddRoot("div", row);
            for (int i = 1; i < depth; i++)
            {
                styled.AddElement(n, "div", leaf);
                n = styled.AddElement(n, "div", i % 2 == 0 ? row : col);
            }

            styled.AddElement(n, "div", leaf);
            boxes.Build();
            var f = new FragmentTree(boxes);
            var sw = Stopwatch.StartNew();
            new BlockLayoutEngine().Layout(boxes, new LayoutInput(1000, double.NaN, new NoFonts()), f);
            sw.Stop();
            Assert.True(sw.Elapsed.TotalSeconds < 10, depth + " levels took " + sw.Elapsed);
            Assert.Equal(CountTree(f), f.Count);
        }
    }
}
