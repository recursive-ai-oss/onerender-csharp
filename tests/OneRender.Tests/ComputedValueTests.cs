using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Parses and computes each property against its specification, through the builder front end.</summary>
public class ComputedValueTests
{
    private static StyleDeclaration D(string css)
    {
        var d = new StyleDeclaration();
        foreach (string part in css.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            Assert.True(d.TrySet(part[..colon].Trim(), part[(colon + 1)..].Trim()), "could not set " + part);
        }

        return d;
    }

    /// <summary>Computes <paramref name="css"/> on a child of a parent with <paramref name="parentCss"/> under a root with <paramref name="rootCss"/>.</summary>
    private static ComputedValues Compute(string css, string parentCss = "", string rootCss = "", double vw = 800, double vh = 600)
    {
        var tree = new StyledTree();
        tree.Styles.SetViewport(vw, vh);
        StyledNode root = tree.AddRoot("html", D(rootCss));
        StyledNode parent = tree.AddElement(root, "div", D(parentCss));
        StyledNode child = tree.AddElement(parent, "span", D(css));
        return child.Values;
    }

    private static ComputedValues ComputeRoot(string css)
    {
        var tree = new StyledTree();
        return tree.AddRoot("html", D(css)).Values;
    }

    [Fact]
    public void InitialValuesMatchTheSpecifications()
    {
        ComputedValues v = Compute(string.Empty);
        Assert.Equal(DisplayType.Inline, v.Display);
        Assert.Equal(PositionKind.Static, v.Position);
        Assert.Equal(FloatKind.None, v.Floating);
        Assert.Equal(ClearKind.None, v.Clear);
        Assert.Equal(BoxSizingKind.ContentBox, v.BoxSizing);
        Assert.True(v.Width.IsAuto && v.Height.IsAuto && v.Top.IsAuto && v.MinWidth.IsAuto);
        Assert.Equal(LengthKind.None, v.MaxWidth.Kind);
        Assert.Equal(0, v.MarginLeft.Value);
        Assert.Equal(0, v.BorderTopWidth);
        Assert.Equal(BorderStyleKind.None, v.BorderTopStyle);
        Assert.True(v.BorderTopColor.IsCurrentColor);
        Assert.True(v.ZIndexAuto);
        Assert.Equal(16, v.FontSize);
        Assert.Equal(400, v.FontWeight);
        Assert.Equal(LengthKind.Normal, v.LineHeight.Kind);
        Assert.Equal(1, v.Opacity);
        Assert.Equal(ColorValue.Black, v.Color);
        Assert.Equal(ColorValue.Transparent, v.BackgroundColor);
        Assert.Equal(ContentKind.Normal, v.Content);
        Assert.Equal(WhiteSpaceCollapseKind.Collapse, v.WhiteSpaceCollapse);
    }

    [Theory]
    [InlineData("block", "block")]
    [InlineData("inline", "inline")]
    [InlineData("inline-block", "inline-block")]
    [InlineData("block flow", "block")]
    [InlineData("inline flow", "inline")]
    [InlineData("block flow-root", "flow-root")]
    [InlineData("inline flow-root", "inline-block")]
    [InlineData("flow-root", "flow-root")]
    [InlineData("list-item", "list-item")]
    [InlineData("block flow list-item", "list-item")]
    [InlineData("list-item block flow", "list-item")]
    [InlineData("inline list-item", "inline list-item")]
    [InlineData("flex", "flex")]
    [InlineData("block flex", "flex")]
    [InlineData("inline flex", "inline-flex")]
    [InlineData("inline-flex", "inline-flex")]
    [InlineData("grid", "grid")]
    [InlineData("inline-grid", "inline-grid")]
    [InlineData("table", "table")]
    [InlineData("inline-table", "inline-table")]
    [InlineData("table-row", "table-row")]
    [InlineData("table-cell", "table-cell")]
    [InlineData("table-caption", "table-caption")]
    [InlineData("none", "none")]
    [InlineData("contents", "contents")]
    public void DisplayParsesTheTwoValueForms(string specified, string computed)
    {
        // CSS Display 3 §2: the multi-keyword syntax and the legacy single keywords compute to the same pair.
        Assert.Equal(computed, Compute("display:" + specified).Display.ToCss());
    }

    [Theory]
    [InlineData("display:foo")]
    [InlineData("display:block block")]
    [InlineData("display:run-in")]
    public void InvalidDisplayIsIgnoredAndReported(string css)
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("html");
        StyledNode child = tree.AddElement(root, "span", D(css));
        Assert.Equal(DisplayType.Inline, child.Values.Display);
        Assert.Single(tree.Diagnostics.Items);
        Assert.Equal(DiagnosticKind.InvalidValue, tree.Diagnostics.Items[0].Kind);
    }

    [Fact]
    public void FloatAndAbsolutePositionBlockifyDisplay()
    {
        // CSS2 §9.7: float and absolute positioning force a block-level display and absolute positioning forces float: none.
        Assert.Equal("block", Compute("display:inline; float:left").Display.ToCss());
        Assert.Equal("flow-root", Compute("display:inline-block; float:right").Display.ToCss());
        Assert.Equal("flex", Compute("display:inline-flex; position:absolute").Display.ToCss());
        Assert.Equal("block", Compute("display:table-cell; float:left").Display.ToCss());
        Assert.Equal(FloatKind.None, Compute("float:left; position:fixed").Floating);
        Assert.Equal("none", Compute("display:none; float:left").Display.ToCss());
        Assert.Equal("block", ComputeRoot("display:inline").Display.ToCss()); // the root is blockified
    }

    [Fact]
    public void LogicalFloatAndClearFollowDirection()
    {
        Assert.Equal(FloatKind.Left, Compute("float:inline-start").Floating);
        Assert.Equal(FloatKind.Right, Compute("float:inline-start", "direction:rtl").Floating);
        Assert.Equal(FloatKind.Right, Compute("float:inline-end").Floating);
        Assert.Equal(ClearKind.Left, Compute("clear:inline-start").Clear);
        Assert.Equal(ClearKind.Left, Compute("clear:inline-end", "direction:rtl").Clear);
    }

    [Fact]
    public void LengthsResolveToPixelsAndPercentagesStayUnresolved()
    {
        ComputedValues v = Compute("width:10em; height:50%; min-width:2rem; max-width:50vw; margin-top:5vh; padding-left:1in", "font-size:20px", "font-size:10px");
        Assert.Equal(LengthKind.Px, v.Width.Kind);
        Assert.Equal(200, v.Width.Value); // em: the element's own font size, inherited 20px
        Assert.Equal(LengthKind.Percent, v.Height.Kind);
        Assert.Equal(50, v.Height.Value);
        Assert.Equal(20, v.MinWidth.Value); // rem: the root's 10px
        Assert.Equal(400, v.MaxWidth.Value); // 50vw of 800
        Assert.Equal(30, v.MarginTop.Value); // 5vh of 600
        Assert.Equal(96, v.PaddingLeft.Value); // 1in
        Assert.Equal(25, v.Height.Resolve(50));
    }

    [Fact]
    public void EmInFontSizeRefersToTheParent()
    {
        // CSS Fonts 4 §2.5: em and % in font-size use the parent's font size; other properties use the element's own.
        ComputedValues v = Compute("font-size:2em; width:2em", "font-size:10px");
        Assert.Equal(20, v.FontSize);
        Assert.Equal(40, v.Width.Value);
        Assert.Equal(15, Compute("font-size:150%", "font-size:10px").FontSize);
        Assert.Equal(16 * 1.2, Compute("font-size:larger").FontSize, 6);
        Assert.Equal(13, Compute("font-size:small").FontSize);
        Assert.Equal(20, Compute("font-size:2rem", "", "font-size:10px").FontSize);
        Assert.Equal(32, ComputeRoot("font-size:2rem").FontSize); // rem in the root's own font-size uses the initial 16px
    }

    [Fact]
    public void RemOnTheRootUsesItsOwnFontSizeOutsideFontSize()
    {
        Assert.Equal(40, ComputeRoot("font-size:20px; width:2rem").Width.Value);
    }

    [Fact]
    public void CalcStaysUnresolvedOnlyWithPercentages()
    {
        // CSS Values 4 §10.8: a calculation without a percentage computes to a length; with one, layout resolves it.
        ComputedValues v = Compute("width:calc(10px + 2em); height:calc(100% - 10px); min-height:min(50px, 10%); max-height:max(1px, 2px)", "font-size:10px");
        Assert.Equal(LengthKind.Px, v.Width.Kind);
        Assert.Equal(30, v.Width.Value);
        Assert.Equal(LengthKind.Calc, v.Height.Kind);
        Assert.Equal(190, v.Height.Resolve(200));
        Assert.Equal(LengthKind.Calc, v.MinHeight.Kind);
        Assert.Equal(20, v.MinHeight.Resolve(200));
        Assert.Equal(5, v.MinHeight.Resolve(50));
    }

    [Fact]
    public void SizingKeywordsAndFitContentFunction()
    {
        Assert.Equal(LengthKind.MinContent, Compute("width:min-content").Width.Kind);
        Assert.Equal(LengthKind.MaxContent, Compute("height:max-content").Height.Kind);
        Assert.Equal(LengthKind.FitContent, Compute("width:fit-content").Width.Kind);
        Length fc = Compute("width:fit-content(10em)", "font-size:10px").Width;
        Assert.Equal(LengthKind.FitContentPx, fc.Kind);
        Assert.Equal(100, fc.Value);
        Assert.Equal(LengthKind.FitContentPercent, Compute("width:fit-content(30%)").Width.Kind);
        Assert.Equal(LengthKind.Stretch, Compute("width:stretch").Width.Kind);
        Assert.Equal(LengthKind.None, Compute("max-width:none").MaxWidth.Kind);
        Assert.True(Compute("min-width:auto").MinWidth.IsAuto);
    }

    [Theory]
    [InlineData("width:-1px")]
    [InlineData("width:none")]
    [InlineData("max-width:auto")]
    [InlineData("padding-left:-3px")]
    [InlineData("padding-left:auto")]
    [InlineData("border-top-width:-1px")]
    [InlineData("width:10")]
    [InlineData("width:fit-content(-5px)")]
    public void InvalidLengthsAreRejected(string css)
    {
        var tree = new StyledTree();
        StyledNode child = tree.AddElement(tree.AddRoot("html"), "div", D(css));
        Assert.NotEmpty(tree.Diagnostics.Items);
        Assert.Equal(DiagnosticKind.InvalidValue, tree.Diagnostics.Items[0].Kind);
        Assert.NotNull(child.Tree);
    }

    [Fact]
    public void BorderWidthIsZeroUnlessTheStyleIsVisible()
    {
        // CSS Backgrounds 3 §4.1.
        ComputedValues v = Compute("border-top-width:5px; border-right-style:solid; border-bottom-style:dashed; border-bottom-width:thick; border-left-style:solid; border-left-width:2em", "font-size:5px");
        Assert.Equal(0, v.BorderTopWidth);
        Assert.Equal(3, v.BorderRightWidth); // medium
        Assert.Equal(5, v.BorderBottomWidth);
        Assert.Equal(10, v.BorderLeftWidth);
        Assert.Equal(1, Compute("border-top-style:solid; border-top-width:thin").BorderTopWidth);
    }

    [Fact]
    public void BorderShorthandAndRadius()
    {
        ComputedValues v = Compute("border:2px solid red; border-radius: 3px 4px / 5px 6px");
        Assert.Equal(2, v.BorderLeftWidth);
        Assert.Equal(BorderStyleKind.Solid, v.BorderBottomStyle);
        Assert.Equal("rgb(255, 0, 0)", v.BorderTopColor.ToCss());
        Assert.Equal(3, v.BorderTopLeftRadius.Horizontal.Value);
        Assert.Equal(5, v.BorderTopLeftRadius.Vertical.Value);
        Assert.Equal(4, v.BorderTopRightRadius.Horizontal.Value);
        Assert.Equal(6, v.BorderTopRightRadius.Vertical.Value);
        Assert.Equal(3, v.BorderBottomRightRadius.Horizontal.Value);
        Assert.Equal(4, v.BorderBottomLeftRadius.Horizontal.Value);
        ComputedValues pct = Compute("border-top-left-radius: 50% 10px");
        Assert.Equal(LengthKind.Percent, pct.BorderTopLeftRadius.Horizontal.Kind);
        Assert.Equal(10, pct.BorderTopLeftRadius.Vertical.Value);
    }

    [Fact]
    public void MarginAndPaddingShorthands()
    {
        ComputedValues v = Compute("margin: 1px 2px 3px; padding: 4px 5px");
        Assert.Equal(1, v.MarginTop.Value);
        Assert.Equal(2, v.MarginRight.Value);
        Assert.Equal(3, v.MarginBottom.Value);
        Assert.Equal(2, v.MarginLeft.Value);
        Assert.Equal(4, v.PaddingTop.Value);
        Assert.Equal(5, v.PaddingLeft.Value);
        Assert.True(Compute("margin: 0 auto").MarginLeft.IsAuto);
        Assert.Equal(LengthKind.Percent, Compute("padding: 10%").PaddingTop.Kind);
    }

    [Fact]
    public void OverflowAdjustsVisibleAndClipNextToScrollingValues()
    {
        // CSS Overflow 3 §3.1.
        ComputedValues v = Compute("overflow: hidden visible");
        Assert.Equal(OverflowKind.Hidden, v.OverflowX);
        Assert.Equal(OverflowKind.Auto, v.OverflowY);
        v = Compute("overflow-x: clip; overflow-y: scroll");
        Assert.Equal(OverflowKind.Hidden, v.OverflowX);
        Assert.Equal(OverflowKind.Scroll, v.OverflowY);
        v = Compute("overflow: clip");
        Assert.Equal(OverflowKind.Clip, v.OverflowX);
        Assert.False(v.IsScrollContainer);
        Assert.True(Compute("overflow: auto").IsScrollContainer);
    }

    [Fact]
    public void ZIndexVisibilityAndOpacity()
    {
        Assert.True(Compute("z-index:auto").ZIndexAuto);
        ComputedValues z = Compute("z-index:-3");
        Assert.False(z.ZIndexAuto);
        Assert.Equal(-3, z.ZIndex);
        Assert.Equal(3, Compute("z-index:calc(1 + 2)").ZIndex);
        Assert.Equal(VisibilityKind.Hidden, Compute(string.Empty, "visibility:hidden").Visibility); // inherited
        Assert.Equal(VisibilityKind.Visible, Compute("visibility:visible", "visibility:hidden").Visibility);
        Assert.Equal(1, Compute("opacity:3").Opacity);
        Assert.Equal(0, Compute("opacity:-2").Opacity);
        Assert.Equal(0.5, Compute("opacity:50%").Opacity);
    }

    [Fact]
    public void AspectRatio()
    {
        ComputedValues v = Compute("aspect-ratio: 16 / 9");
        Assert.False(v.AspectRatioAuto);
        Assert.Equal(16.0 / 9, v.AspectRatio, 9);
        v = Compute("aspect-ratio: auto 4/3");
        Assert.True(v.AspectRatioAuto);
        Assert.Equal(4.0 / 3, v.AspectRatio, 9);
        v = Compute("aspect-ratio: 2");
        Assert.Equal(2, v.AspectRatio);
        v = Compute("aspect-ratio: auto");
        Assert.True(v.AspectRatioAuto);
        Assert.Equal(0, v.AspectRatio);
        Assert.NotEmpty(DiagnosticsFor("aspect-ratio: 1 / -2"));
    }

    private static IReadOnlyList<Diagnostic> DiagnosticsFor(string css)
    {
        var tree = new StyledTree();
        tree.AddElement(tree.AddRoot("html"), "div", D(css));
        return tree.Diagnostics.Items;
    }

    [Fact]
    public void FontProperties()
    {
        ComputedValues v = Compute("font-weight:bolder", "font-weight:400");
        Assert.Equal(700, v.FontWeight);
        Assert.Equal(100, Compute("font-weight:lighter", "font-weight:400").FontWeight);
        Assert.Equal(900, Compute("font-weight:bolder", "font-weight:700").FontWeight);
        Assert.Equal(700, Compute("font-weight:bold").FontWeight);
        Assert.Equal(FontStyleKind.Italic, Compute("font-style:italic").FontStyle);
        v = Compute("font-style:oblique 10deg");
        Assert.Equal(FontStyleKind.Oblique, v.FontStyle);
        Assert.Equal(10, v.ObliqueAngle);
        Assert.Equal(FontStyleKind.Normal, Compute("font-style:oblique 0deg").FontStyle);
        Assert.Equal(75, Compute("font-stretch:condensed").FontStretch);
        Assert.Equal(110, Compute("font-stretch:110%").FontStretch);
        FontFamilyList f = Compute("font-family: 'Helvetica Neue', Arial, sans-serif").FontFamily;
        Assert.Equal(3, f.Count);
        Assert.Equal("Helvetica Neue", f[0]);
        Assert.False(f.IsGeneric(1));
        Assert.True(f.IsGeneric(2));
        Assert.Equal("Helvetica Neue, Arial, sans-serif", f.ToCss());
        Assert.Equal("Arial", Compute(string.Empty, "font-family:Arial").FontFamily[0]); // inherited
        v = Compute("font: italic bold 20px/30px Georgia, serif");
        Assert.Equal(FontStyleKind.Italic, v.FontStyle);
        Assert.Equal(700, v.FontWeight);
        Assert.Equal(20, v.FontSize);
        Assert.Equal(30, v.LineHeight.Value);
    }

    [Fact]
    public void LineHeightComputesNumbersLengthsAndPercentages()
    {
        // CSS Inline 3 §4.2: a percentage computes to a length from the element's own font size; a number is inherited as a number.
        Assert.Equal(LengthKind.Number, Compute("line-height:1.5").LineHeight.Kind);
        Assert.Equal(1.5, Compute("line-height:1.5").LineHeight.Value);
        ComputedValues pct = Compute("line-height:150%; font-size:20px");
        Assert.Equal(LengthKind.Px, pct.LineHeight.Kind);
        Assert.Equal(30, pct.LineHeight.Value);
        Assert.Equal(48, Compute("line-height:2em", "font-size:24px").LineHeight.Value);
        // inheritance: a length inherits as the parent's computed length, a number as the number
        Assert.Equal(30, Compute("font-size:100px", "line-height:150%; font-size:20px").LineHeight.Value);
        ComputedValues inherited = Compute("font-size:100px", "line-height:1.5");
        Assert.Equal(LengthKind.Number, inherited.LineHeight.Kind);
        Assert.Equal(LengthKind.Normal, Compute("line-height:normal").LineHeight.Kind);
        Assert.NotEmpty(DiagnosticsFor("line-height:-1"));
    }

    [Fact]
    public void VerticalAlignKeywordsAndLengths()
    {
        Assert.Equal(VerticalAlignKind.Middle, Compute("vertical-align:middle").VerticalAlign);
        Assert.Equal(VerticalAlignKind.TextBottom, Compute("vertical-align:text-bottom").VerticalAlign);
        ComputedValues v = Compute("vertical-align:-3px");
        Assert.Equal(VerticalAlignKind.Length, v.VerticalAlign);
        Assert.Equal(-3, v.VerticalAlignOffset.Value);
        Assert.Equal(LengthKind.Percent, Compute("vertical-align:20%").VerticalAlignOffset.Kind);
        Assert.Equal(VerticalAlignKind.Baseline, Compute(string.Empty, "vertical-align:top").VerticalAlign); // not inherited
    }

    [Fact]
    public void WhiteSpaceLonghandsAndShorthand()
    {
        // CSS Text 4 §4.3: white-space is a shorthand of white-space-collapse, text-wrap-mode and white-space-trim.
        (string css, WhiteSpaceCollapseKind collapse, TextWrapModeKind wrap)[] cases =
        [
            ("white-space:normal", WhiteSpaceCollapseKind.Collapse, TextWrapModeKind.Wrap),
            ("white-space:pre", WhiteSpaceCollapseKind.Preserve, TextWrapModeKind.NoWrap),
            ("white-space:pre-wrap", WhiteSpaceCollapseKind.Preserve, TextWrapModeKind.Wrap),
            ("white-space:pre-line", WhiteSpaceCollapseKind.PreserveBreaks, TextWrapModeKind.Wrap),
            ("white-space:nowrap", WhiteSpaceCollapseKind.Collapse, TextWrapModeKind.NoWrap),
            ("white-space:break-spaces", WhiteSpaceCollapseKind.BreakSpaces, TextWrapModeKind.Wrap),
            ("white-space-collapse:preserve-breaks", WhiteSpaceCollapseKind.PreserveBreaks, TextWrapModeKind.Wrap),
            ("text-wrap-mode:nowrap", WhiteSpaceCollapseKind.Collapse, TextWrapModeKind.NoWrap),
        ];
        foreach ((string css, WhiteSpaceCollapseKind collapse, TextWrapModeKind wrap) in cases)
        {
            ComputedValues v = Compute(css);
            Assert.Equal(collapse, v.WhiteSpaceCollapse);
            Assert.Equal(wrap, v.TextWrapMode);
        }

        Assert.Equal(WhiteSpaceCollapseKind.Preserve, Compute(string.Empty, "white-space:pre").WhiteSpaceCollapse); // inherited
    }

    [Fact]
    public void TextProperties()
    {
        Assert.Equal(TextAlignKind.Center, Compute("text-align:center").TextAlign);
        Assert.Equal(TextAlignKind.Center, Compute(string.Empty, "text-align:center").TextAlign);
        // match-parent computes against the parent's direction
        Assert.Equal(TextAlignKind.Right, Compute("text-align:match-parent", "text-align:start; direction:rtl").TextAlign);
        Assert.Equal(TextAlignKind.Left, Compute("text-align:match-parent", "text-align:start").TextAlign);
        Assert.Equal(TextAlignKind.Right, Compute("text-align:match-parent", "text-align:end").TextAlign);
        ComputedValues v = Compute("text-indent: 2em hanging each-line; letter-spacing: 1px; word-spacing: 10%", "font-size:10px");
        Assert.Equal(20, v.TextIndent.Value);
        Assert.True(v.TextIndentHanging && v.TextIndentEachLine);
        Assert.Equal(1, v.LetterSpacing.Value);
        Assert.Equal(LengthKind.Percent, v.WordSpacing.Kind);
        Assert.Equal(LengthKind.Normal, Compute("letter-spacing:normal").LetterSpacing.Kind);
        Assert.Equal(32, Compute(string.Empty, "text-indent:2em").TextIndent.Value);
        Assert.Equal(DirectionKind.Rtl, Compute(string.Empty, "direction:rtl").Direction);
        Assert.Equal(UnicodeBidiKind.IsolateOverride, Compute("unicode-bidi:isolate-override").UnicodeBidi);
        Assert.Equal(UnicodeBidiKind.Normal, Compute(string.Empty, "unicode-bidi:embed").UnicodeBidi); // not inherited
    }

    [Fact]
    public void TextIndentInheritsAsAComputedLength()
    {
        // The em in the parent's text-indent is resolved with the parent's font size before it is inherited.
        Assert.Equal(20, Compute("font-size:100px", "text-indent:2em; font-size:10px").TextIndent.Value);
    }

    [Fact]
    public void ColorsAndCurrentColor()
    {
        ComputedValues v = Compute("color: #ff8000; background-color: rgb(0 0 255 / 50%); border-top-color: currentcolor");
        Assert.Equal("rgb(255, 128, 0)", v.Color.ToCss());
        Assert.Equal("rgba(0, 0, 255, 0.5)", v.BackgroundColor.ToCss());
        Assert.True(v.BorderTopColor.IsCurrentColor);
        Assert.Equal("rgb(255, 0, 0)", Compute("color:currentcolor", "color:red").Color.ToCss()); // currentcolor for color means inherit
        Assert.Equal("rgb(255, 0, 0)", Compute(string.Empty, "color:red").Color.ToCss());
        Assert.Equal("rgb(0, 128, 0)", Compute("color:green").Color.ToCss());
        Assert.Equal(ColorValue.Transparent, Compute("background-color:transparent").BackgroundColor);
        Assert.Equal(ColorValue.Transparent, Compute(string.Empty, "background-color:red").BackgroundColor); // not inherited
    }

    [Fact]
    public void CssWideKeywords()
    {
        Assert.Equal(5, Compute("margin-left:inherit", "margin-left:5px").MarginLeft.Value);
        Assert.Equal(0, Compute("margin-left:initial", "margin-left:5px").MarginLeft.Value);
        Assert.Equal(0, Compute("margin-left:unset", "margin-left:5px").MarginLeft.Value); // not inherited: initial
        Assert.Equal("rgb(255, 0, 0)", Compute("color:unset", "color:red").Color.ToCss()); // inherited: inherit
        Assert.Equal(ColorValue.Black, Compute("color:initial", "color:red").Color);
        Assert.Equal(20, Compute("font-size:inherit", "font-size:20px").FontSize);
        Assert.Equal(16, Compute("font-size:initial", "font-size:20px").FontSize);
        Assert.Equal("block", Compute("display:inherit", "display:block").Display.ToCss());
        Assert.Equal(10, Compute("border-top-width:inherit; border-top-style:solid", "border-top-width:10px; border-top-style:solid").BorderTopWidth);
    }

    [Fact]
    public void ContentAndListStyle()
    {
        ComputedValues v = Compute("content:'ab' 'cd'");
        Assert.Equal(ContentKind.Text, v.Content);
        Assert.Equal("abcd", v.ContentText);
        Assert.Equal(ContentKind.None, Compute("content:none").Content);
        Assert.Equal(ContentKind.Other, Compute("content:counter(x)").Content);
        Assert.Equal("none", Compute("list-style: none").ListStyleType);
        Assert.Equal(ListStylePositionKind.Inside, Compute("list-style-position:inside").ListStylePosition);
        Assert.Equal("decimal", Compute(string.Empty, "list-style-type:decimal").ListStyleType);
    }

    [Fact]
    public void PositionAndOffsets()
    {
        ComputedValues v = Compute("position:sticky; top:10px; left:5%; right:auto; bottom:calc(50% + 2px); inset: 1px");
        Assert.Equal(PositionKind.Sticky, v.Position);
        Assert.Equal(1, v.Top.Value);
        Assert.Equal(1, v.Left.Value);
        v = Compute("position:absolute; top:10px; left:5%; bottom:calc(50% + 2px)");
        Assert.Equal(10, v.Top.Value);
        Assert.Equal(LengthKind.Percent, v.Left.Kind);
        Assert.Equal(LengthKind.Calc, v.Bottom.Kind);
        Assert.Equal(PositionKind.Fixed, Compute("position:fixed").Position);
        Assert.Equal(BoxSizingKind.BorderBox, Compute("box-sizing:border-box").BoxSizing);
    }

    [Fact]
    public void ViewportUnitsFollowTheViewport()
    {
        ComputedValues v = Compute("width:10vw; height:10vh; margin-left:5vmin; margin-right:5vmax", "", "", 1000, 400);
        Assert.Equal(100, v.Width.Value);
        Assert.Equal(40, v.Height.Value);
        Assert.Equal(20, v.MarginLeft.Value);
        Assert.Equal(50, v.MarginRight.Value);
    }

    [Fact]
    public void AnonymousStyleInheritsOnlyInheritedProperties()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("html", D("color:red; width:100px; font-size:30px"));
        StyleId anon = tree.Styles.GetAnonymous(root.Style, DisplayType.Block);
        ref readonly ComputedValues v = ref tree.Styles[anon];
        Assert.Equal("rgb(255, 0, 0)", v.Color.ToCss());
        Assert.Equal(30, v.FontSize);
        Assert.True(v.Width.IsAuto);
        Assert.Equal("block", v.Display.ToCss());
        Assert.Equal(anon, tree.Styles.GetAnonymous(root.Style, DisplayType.Block));
    }

    [Fact]
    public void PropertyNamesRoundTrip()
    {
        for (int i = 0; i < Properties.Count; i++)
        {
            var id = (PropertyId)i;
            Assert.True(Properties.TryGet(Properties.NameOf(id), out PropertyId back));
            Assert.Equal(id, back);
            // our idea of inheritance agrees with the property table OneHtml generates from the specifications
            RecursiveAI.OneHtml.Css.PropertyInfo? info = RecursiveAI.OneHtml.Css.PropertyTable.Find(Properties.NameOf(id));
            Assert.NotNull(info);
            if (info.Inherited == RecursiveAI.OneHtml.Css.PropertyInheritance.Unspecified)
            {
                continue; // font-stretch: a legacy alias in the table, inherited by its definition
            }

            if (id == PropertyId.Page)
            {
                continue; // not inherited by the table, but an auto value takes the parent's name (CSS Paged Media 3 section 7.1)
            }

            Assert.True(info.Inherited == RecursiveAI.OneHtml.Css.PropertyInheritance.Yes == Properties.IsInherited(id), Properties.NameOf(id) + " inheritance differs from the property table (" + info.Inherited + ")");
        }
    }
}
