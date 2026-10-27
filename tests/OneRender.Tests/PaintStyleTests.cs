using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Parsing and computing of the paint properties against CSS Backgrounds 3, CSS Images 3, CSS UI 4 and Text Decoration 4.</summary>
public class PaintStyleTests
{
    private static ComputedValues Compute(string property, string value)
    {
        var tree = new StyledTree();
        tree.Styles.SetViewport(800, 600);
        var d = new StyleDeclaration();
        Assert.True(d.TrySet(property, value), "could not set " + property);
        return tree.AddRoot("html", d).Values;
    }

    [Theory]
    [InlineData("50% 6px", "50% 6px")]
    [InlineData("left top", "0% 0%")]
    [InlineData("right", "100% 50%")]
    [InlineData("center", "50% 50%")]
    [InlineData("top", "50% 0%")]
    [InlineData("10px", "10px 50%")]
    [InlineData("bottom 10% right 20%", "80% 90%")]
    public void BackgroundPositionComputesPerLayer(string css, string expected)
    {
        ComputedValues v = Compute("background-position", css);
        Assert.Equal(expected, ComputedValueText.Get(PropertyId.BackgroundPositionX, in v) + " " + ComputedValueText.Get(PropertyId.BackgroundPositionY, in v));
    }

    [Theory]
    [InlineData("left right")]
    [InlineData("top bottom")]
    [InlineData("10px left")]
    [InlineData("left 10px 20px")]
    [InlineData("center 10px center")]
    public void InvalidPositionsAreRejected(string css) =>
        Assert.False(new StyleDeclaration().TrySet("background-position", css));

    [Fact]
    public void LayersAreCountedByImageAndOtherListsCycle()
    {
        ComputedValues v = Compute("background", "url(a.png) no-repeat, linear-gradient(red, blue) repeat-x, none");
        Assert.Equal(3, v.Background.LayerCount);
        Assert.Equal(ImageSourceKind.Url, v.Background.Images[0].Kind);
        Assert.Equal("a.png", v.Background.Images[0].Url);
        Assert.Equal(ImageSourceKind.Gradient, v.Background.Images[1].Kind);
        Assert.Equal(3, v.Background.Repeats.Length);
        Assert.Equal((BackgroundRepeatKind.NoRepeat, BackgroundRepeatKind.NoRepeat), v.Background.Repeats[0]);
        Assert.Equal((BackgroundRepeatKind.Repeat, BackgroundRepeatKind.NoRepeat), v.Background.Repeats[1]);
    }

    [Fact]
    public void BackgroundShorthandTakesPositionSlashSizeAndBoxes()
    {
        ComputedValues v = Compute("background", "url(x.png) 10px 20px / cover content-box border-box fixed");
        Assert.Equal(BackgroundSizeKind.Cover, v.Background.Sizes[0].Kind);
        Assert.Equal(BackgroundBoxKind.ContentBox, v.Background.Origins[0]);
        Assert.Equal(BackgroundBoxKind.BorderBox, v.Background.Clips[0]);
        Assert.Equal(BackgroundAttachmentKind.Fixed, v.Background.Attachments[0]);
        Assert.Equal(10, v.Background.PositionsX[0].Offset.Value);
    }

    [Fact]
    public void ASingleBoxKeywordSetsOriginAndClip()
    {
        ComputedValues v = Compute("background", "url(x.png) padding-box");
        Assert.Equal(BackgroundBoxKind.PaddingBox, v.Background.Origins[0]);
        Assert.Equal(BackgroundBoxKind.PaddingBox, v.Background.Clips[0]);
    }

    [Fact]
    public void GradientsParseWithAnglesSidesCornersAndHints()
    {
        ComputedValues v = Compute("background-image", "linear-gradient(to top right, red 10%, 40%, blue 90%, lime)");
        GradientSpec g = v.Background.Images[0].Gradient!;
        Assert.True(g.CornerKeyword);
        Assert.Equal(1, g.CornerX);
        Assert.Equal(-1, g.CornerY);
        Assert.Equal(4, g.Stops.Length);
        Assert.True(g.Stops[1].IsHint);

        v = Compute("background-image", "repeating-linear-gradient(45deg, red, blue 20px)");
        g = v.Background.Images[0].Gradient!;
        Assert.True(g.Repeating);
        Assert.Equal(Math.PI / 4, g.Angle, 9);

        v = Compute("background-image", "radial-gradient(circle farthest-side at 25% 75%, red, blue)");
        g = v.Background.Images[0].Gradient!;
        Assert.Equal(GradientKind.Radial, g.Kind);
        Assert.True(g.Circle);
        Assert.Equal(RadialExtent.FarthestSide, g.Extent);
        Assert.Equal(25, g.CenterX.Offset.Value);

        v = Compute("background-image", "conic-gradient(from 90deg at 10px 20px, red 0deg, blue 0.5turn, red 360deg)");
        g = v.Background.Images[0].Gradient!;
        Assert.Equal(GradientKind.Conic, g.Kind);
        Assert.Equal(Math.PI / 2, g.Angle, 9);
        Assert.Equal(3, g.Stops.Length);
    }

    [Fact]
    public void TwoPositionsMakeTwoStops()
    {
        ComputedValues v = Compute("background-image", "linear-gradient(red 10% 30%, blue)");
        Assert.Equal(3, v.Background.Images[0].Gradient!.Stops.Length);
    }

    [Theory]
    [InlineData("linear-gradient(red)")]
    [InlineData("linear-gradient(to red, blue, lime)")]
    [InlineData("linear-gradient(red, 10%)")]
    [InlineData("linear-gradient(10%, red, blue)")]
    [InlineData("radial-gradient(circle 10%, red, blue)")]
    [InlineData("conic-gradient(from, red, blue)")]
    [InlineData("image-set(url(a.png) 1x)")]
    public void InvalidImagesAreRejected(string css)
    {
        ComputedValues v = Compute("background-image", css);
        Assert.Equal(ImageSourceKind.None, v.Background.Images[0].Kind);
    }

    [Fact]
    public void ShadowsParseColorAnywhereAndInset()
    {
        ComputedValues v = Compute("box-shadow", "inset 1px 2px 3px 4px red, blue 5px 6px");
        Assert.Equal(2, v.BoxShadow.Count);
        ShadowSpec a = v.BoxShadow.Items[0];
        Assert.True(a.Inset);
        Assert.Equal((1, 2, 3, 4), (a.OffsetX, a.OffsetY, a.Blur, a.Spread));
        Assert.Equal(1f, a.Color.R);
        Assert.Equal(0f, v.BoxShadow.Items[1].Blur);

        v = Compute("box-shadow", "1px 2px -3px");
        Assert.Equal(0, v.BoxShadow.Count); // a negative blur is invalid
        v = Compute("text-shadow", "1px 2px 3px 4px");
        Assert.Equal(0, v.TextShadow.Count); // text-shadow has no spread
    }

    [Fact]
    public void OutlineHasZeroWidthWithoutAStyleAndAutoIsRemembered()
    {
        ComputedValues v = Compute("outline", "5px solid red");
        Assert.Equal(5, v.OutlineWidth);
        Assert.Equal(BorderStyleKind.Solid, v.OutlineStyle);
        v = Compute("outline-width", "5px");
        Assert.Equal(0, v.OutlineWidth);
        v = Compute("outline-style", "auto");
        Assert.True(v.OutlineAuto);
        Assert.Equal(3, v.OutlineWidth);
        v = Compute("outline-style", "hidden");
        Assert.False(v.OutlineAuto);
        Assert.Equal(BorderStyleKind.None, v.OutlineStyle);
    }

    [Fact]
    public void TextDecorationShorthandAndInheritance()
    {
        ComputedValues v = Compute("text-decoration", "underline overline wavy red 3px");
        Assert.Equal(TextDecorationLines.Underline | TextDecorationLines.Overline, v.TextDecorationLine);
        Assert.Equal(TextDecorationStyleKind.Wavy, v.TextDecorationStyle);
        Assert.Equal(3, v.TextDecorationThickness.Value);

        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("html", new StyleDeclaration().Set("text-decoration", "underline").Set("text-shadow", "1px 1px red").Set("text-underline-offset", "4px"));
        StyledNode child = tree.AddElement(root, "span");
        Assert.Equal(TextDecorationLines.None, child.Values.TextDecorationLine); // not inherited
        Assert.Equal(1, child.Values.TextShadow.Count); // inherited
        Assert.Equal(4, child.Values.TextUnderlineOffset.Value);
    }

    [Fact]
    public void BorderImageLonghandsAndShorthand()
    {
        ComputedValues v = Compute("border-image", "url(b.png) 10 20% fill / 1 2px / 3px stretch round");
        BorderImageSpec b = v.BorderImage;
        Assert.Equal("b.png", b.Source.Url);
        Assert.True(b.Fill);
        Assert.Equal(LengthKind.Number, b.Slice[0].Kind);
        Assert.Equal(LengthKind.Percent, b.Slice[1].Kind);
        Assert.Equal(20, b.Slice[3].Value);
        Assert.Equal(2, b.Width[1].Value);
        Assert.Equal(3, b.Outset[0].Value);
        Assert.Equal(BorderImageRepeatKind.Stretch, b.RepeatX);
        Assert.Equal(BorderImageRepeatKind.Round, b.RepeatY);
    }

    [Fact]
    public void ObjectFitPositionAndImageRendering()
    {
        ComputedValues v = Compute("object-fit", "scale-down");
        Assert.Equal(ObjectFitKind.ScaleDown, v.ObjectFit);
        v = Compute("object-position", "right 10px bottom 20px");
        Assert.True(v.ObjectPosition.X.FromEnd);
        Assert.Equal(20, v.ObjectPosition.Y.Offset.Value);
        v = Compute("image-rendering", "pixelated");
        Assert.Equal(ImageRenderingKind.Pixelated, v.ImageRendering);
    }
}
