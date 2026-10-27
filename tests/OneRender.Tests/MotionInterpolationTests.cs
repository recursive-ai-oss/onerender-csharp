using OneRender.Animation;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Style;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Interpolation per animation type (CSS Values 4 and the property definitions), sampled through a running keyframe animation.</summary>
public class MotionInterpolationTests
{
    // A 1 s linear animation of `prop` from `from` to `to`, read at `progress`.
    private static string At(string prop, string from, string to, double progress, string extraCss = "#t{width:100px;height:50px}")
    {
        string css = extraCss + "@keyframes k{from{" + prop + ":" + from + "}to{" + prop + ":" + to + "}}#t{animation:k 1s linear both}";
        string html = Ix.Head.Replace("</style>", css + "</style>", StringComparison.Ordinal) + "<div id=t></div></body></html>";
        InteractivePage p = InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300 }, new InteractionOptions { AnimationStartTime = 0 });
        p.Advance(progress * 1000);
        StyledNodeAccess(p, out var values);
        Assert.True(Properties.TryGet(prop, out PropertyId id));
        return ComputedValueText.Get(id, in values);
    }

    private static void StyledNodeAccess(InteractivePage p, out ComputedValues values) =>
        values = p.Page.Styled.FindBySource(Ix.Id(p, "t").Index).Values;

    [Fact]
    public void LengthsInterpolateLinearly()
    {
        Assert.Equal("150px", At("width", "100px", "200px", 0.5));
        Assert.Equal("100px", At("width", "100px", "200px", 0));
        Assert.Equal("200px", At("width", "100px", "200px", 1));
    }

    [Fact]
    public void PercentagesAndLengthsMixIntoACalc()
    {
        string v = At("width", "100px", "50%", 0.5);
        Assert.StartsWith("calc(", v, StringComparison.Ordinal);
        Assert.Contains("25%", v, StringComparison.Ordinal);
        Assert.Contains("50px", v, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersAndOpacityInterpolate()
    {
        Assert.Equal("0.25", At("opacity", "0", "1", 0.25));
        Assert.Equal("5", At("flex-grow", "0", "10", 0.5));
        Assert.Equal("3", At("z-index", "2", "4", 0.5));
    }

    [Fact]
    public void ColorsInterpolateInPremultipliedSrgb()
    {
        Assert.Equal("rgb(128, 0, 128)", At("color", "rgb(255 0 0)", "rgb(0 0 255)", 0.5));
        // Premultiplied: the transparent end contributes no color of its own.
        Assert.Equal("rgba(255, 0, 0, 0.5)", At("background-color", "rgba(255 0 0 / 0)", "rgb(255 0 0)", 0.5));
        Assert.Equal("rgba(0, 0, 255, 0.5)", At("background-color", "transparent", "blue", 0.5));
    }

    [Fact]
    public void TransformFunctionsOfTheSameKindInterpolatePerArgument()
    {
        string v = At("transform", "translate(10px, 20px) scale(1)", "translate(30px, 40px) scale(3)", 0.5);
        Assert.Contains("translate3d(20px, 30px, 0px)", v, StringComparison.Ordinal);
        Assert.Contains("scale3d(2, 2, 1)", v, StringComparison.Ordinal);
    }

    [Fact]
    public void RotationsTakeTheArgumentPath()
    {
        // 0 to 360 degrees is not a no-op: matching functions interpolate their angles.
        Assert.Contains("rotate3d(0, 0, 1, 3.14159", At("transform", "rotate(0deg)", "rotate(360deg)", 0.5), StringComparison.Ordinal);
    }

    [Fact]
    public void MismatchedTransformListsInterpolateThroughTheirMatrices()
    {
        string v = At("transform", "translate(100px, 0)", "rotate(90deg)", 0.5);
        Assert.StartsWith("matrix3d(", v, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowsPadTheShorterListWithTransparentShadows()
    {
        string v = At("box-shadow", "none", "10px 10px 4px rgb(0 0 0)", 0.5);
        Assert.Contains("5px 5px 2px", v, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterFunctionsOfTheSameKindInterpolate()
    {
        Assert.Contains("blur(5px)", At("filter", "blur(0px)", "blur(10px)", 0.5), StringComparison.Ordinal);
        // A missing function is the identity of its kind: opacity() fades from 1.
        Assert.Contains("opacity(0.5)", At("filter", "none", "opacity(0)", 0.5), StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPathShapesOfTheSameKindInterpolate()
    {
        Assert.Contains("inset(5px 5px 5px 5px)", At("clip-path", "inset(0px)", "inset(10px)", 0.5), StringComparison.Ordinal);
        Assert.Contains("circle(30px", At("clip-path", "circle(20px at 50% 50%)", "circle(40px at 50% 50%)", 0.5), StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityIsVisibleAcrossTheIntervalWhenEitherEndIsVisible()
    {
        Assert.Equal("visible", At("visibility", "visible", "hidden", 0.9));
        Assert.Equal("hidden", At("visibility", "visible", "hidden", 1));
    }

    [Fact]
    public void DiscretePropertiesFlipAtTheMidpoint()
    {
        Assert.Equal("left", At("float", "left", "right", 0.49));
        Assert.Equal("right", At("float", "left", "right", 0.5));
    }

    [Fact]
    public void UnrelatedValueKindsFlipInsteadOfInterpolating()
    {
        // auto is not a number: the pair is not interpolable, so the value flips at the midpoint (Web Animations 1 §5.3).
        Assert.Equal("auto", At("width", "auto", "200px", 0.4));
        Assert.Equal("200px", At("width", "auto", "200px", 0.6));
    }

    [Fact]
    public void ExtrapolationAcrossEasingOvershootKeepsValuesFinite()
    {
        string v = At("width", "0px", "100px", 0.5, "#t{width:1px;transition:none}#t{animation-timing-function:cubic-bezier(0.5,2,0.5,-1)}");
        Assert.EndsWith("px", v, StringComparison.Ordinal);
    }
}

public class TransformMathTests
{
    private static double[] Load(System.Numerics.Matrix4x4 m)
    {
        var d = new double[16];
        TransformMath.Load(in m, d);
        return d;
    }

    [Fact]
    public void InterpolatingToTheSameMatrixReturnsIt()
    {
        var m = System.Numerics.Matrix4x4.CreateScale(2, 3, 1) * System.Numerics.Matrix4x4.CreateRotationZ(0.7f) * System.Numerics.Matrix4x4.CreateTranslation(10, -4, 0);
        double[] a = Load(m);
        var r = new double[16];
        Assert.True(TransformMath.InterpolateMatrices(a, a, 0.37, r));
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(a[i], r[i], 3);
        }
    }

    [Fact]
    public void InterpolatingARotationHalfwayHalvesTheAngle()
    {
        double[] a = Load(System.Numerics.Matrix4x4.Identity);
        double[] b = Load(System.Numerics.Matrix4x4.CreateRotationZ((float)(Math.PI / 2)));
        var r = new double[16];
        Assert.True(TransformMath.InterpolateMatrices(a, b, 0.5, r));
        System.Numerics.Matrix4x4 m = TransformMath.Store(r);
        Assert.Equal(Math.Cos(Math.PI / 4), m.M11, 3);
        Assert.Equal(Math.Sin(Math.PI / 4), m.M12, 3);
    }

    [Fact]
    public void TranslationAndScaleInterpolateLinearly()
    {
        double[] a = Load(System.Numerics.Matrix4x4.CreateTranslation(0, 0, 0));
        double[] b = Load(System.Numerics.Matrix4x4.CreateScale(3, 3, 1) * System.Numerics.Matrix4x4.CreateTranslation(20, 40, 0));
        var r = new double[16];
        Assert.True(TransformMath.InterpolateMatrices(a, b, 0.5, r));
        System.Numerics.Matrix4x4 m = TransformMath.Store(r);
        Assert.Equal(10, m.M41, 3);
        Assert.Equal(20, m.M42, 3);
        Assert.Equal(2, m.M11, 3);
    }

    [Fact]
    public void ASingularMatrixCannotBeInterpolated()
    {
        double[] a = Load(System.Numerics.Matrix4x4.CreateScale(0, 1, 1));
        double[] b = Load(System.Numerics.Matrix4x4.Identity);
        Assert.False(TransformMath.InterpolateMatrices(a, b, 0.5, new double[16]));
    }
}
