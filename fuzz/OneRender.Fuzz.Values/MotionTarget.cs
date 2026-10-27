using System;
using System.Text;
using OneRender.Animation;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Style;
using RecursiveAI.OneHtml;

namespace OneRender.Fuzz;

/// <summary>
/// Transitions and animations on hostile values. Bytes pick animation and transition longhands from small vocabularies and splice raw
/// text into the value of one of them and into a keyframe declaration; a page with a few animated elements is built, then ticked through
/// times that include huge, negative and non-finite ones. Computed numbers must stay finite, a tick must not throw, and the easing
/// functions must give finite outputs for any input.
/// </summary>
internal static class MotionTarget
{
    private static readonly string[] Props =
    [
        "animation-name", "animation-duration", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode",
        "animation-play-state", "animation-timing-function", "animation", "transition", "transition-property", "transition-duration",
        "transition-delay", "transition-timing-function", "transition-behavior",
    ];

    private static readonly string[] Animatable =
    [
        "opacity", "transform", "width", "height", "color", "background-color", "box-shadow", "filter", "clip-path", "margin-left", "font-size",
        "line-height", "letter-spacing", "z-index", "visibility", "display", "border-radius", "translate", "rotate", "scale", "flex-grow",
        "text-shadow", "vertical-align", "background-position", "outline-offset",
    ];

    private static readonly string[] Times = ["0s", "1ms", "100ms", "1s", "-1s", "-500ms", "1e9s", "9999999999999999s", "calc(1s * 1e308)", "auto", "infinite"];

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        string raw = Encoding.UTF8.GetString(data[4..]);
        int cut = raw.IndexOf('\n', StringComparison.Ordinal);
        string first = cut < 0 ? raw : raw[..cut];
        string second = cut < 0 ? raw : raw[(cut + 1)..];
        var css = new StringBuilder("@keyframes k{from{").Append(Animatable[data[0] % Animatable.Length]).Append(':').Append(first).Append("}50%{")
            .Append(Animatable[data[1] % Animatable.Length]).Append(':').Append(second).Append("}to{")
            .Append(Animatable[data[0] % Animatable.Length]).Append(':').Append(second).Append("}}");
        css.Append("@keyframes j{0%,100%{opacity:0}33%,66%{opacity:1;transform:rotate(1e9turn)}}");
        string prop = Props[data[2] % Props.Length];
        css.Append(".a{animation:k 1s linear infinite;").Append(prop).Append(':').Append(first).Append('}');
        css.Append(".b{animation:j ").Append(Times[data[3] % Times.Length]).Append(' ').Append(Times[(data[3] >> 3) % Times.Length]).Append(' ').Append(Times[data[1] % Times.Length]).Append(" both;")
            .Append("transition:all ").Append(Times[data[2] % Times.Length]).Append(";}");
        css.Append(".c{").Append(prop).Append(':').Append(second).Append(";transition:all 1s}.c:focus{").Append(Animatable[data[1] % Animatable.Length]).Append(':').Append(first).Append('}');
        string html = "<style>" + css + "</style><div class=a id=a tabindex=0>t<span>u</span></div><div class=b>x</div><div class=c id=c tabindex=0>y</div>";

        InteractivePage p;
        try
        {
            p = InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 200 }, new InteractionOptions { AnimationStartTime = 0 });
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return;
        }

        double[] times = [0, 16, 500, 1000, 1e9, 1e300, -5, double.NaN, double.PositiveInfinity, 2e9, 1234.5];
        p.Focus(p.Page.Document.GetElementById("c"));
        foreach (double t in times)
        {
            p.Advance(t);
            Check(p, "a");
            Check(p, "c");
        }

        Easing(data);
    }

    private static void Check(InteractivePage p, string id)
    {
        StyledAccess(p, id, out ComputedValues v);
        if (!double.IsFinite(v.Opacity) || v.Opacity is < 0 or > 1)
        {
            throw new InvalidOperationException("opacity " + v.Opacity);
        }

        if (!double.IsFinite(p.AnimationTime))
        {
            throw new InvalidOperationException("clock " + p.AnimationTime);
        }
    }

    private static void StyledAccess(InteractivePage p, string id, out ComputedValues v)
    {
        Node n = p.Page.Document.GetElementById(id);
        v = n.IsNull ? ComputedValues.Initial : p.Page.Styled.FindBySource(n.Index).Values;
    }

    // Easing functions built from arbitrary numbers must give finite outputs for any finite input, or not exist.
    private static void Easing(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
        {
            return;
        }

        var list = new System.Collections.Generic.List<EasingFunction>();
        double d0 = BitConverter.ToDouble(data.Slice(0, 8)), d1 = BitConverter.ToDouble(data.Slice(8, 8));
        double d2 = BitConverter.ToDouble(data.Slice(16, 8)), d3 = BitConverter.ToDouble(data.Slice(24, 8));
        if (EasingFunction.CubicBezier(d0, d1, d2, d3) is { } b)
        {
            list.Add(b);
        }

        if (EasingFunction.Step(BitConverter.ToInt32(data.Slice(0, 4)), (StepPosition)(data[4] & 3)) is { } s)
        {
            list.Add(s);
        }

        int n = Math.Min(8, (data.Length - 32) / 16);
        if (n > 0)
        {
            var outputs = new double[n];
            var inputs = new double[n];
            for (int i = 0; i < n; i++)
            {
                outputs[i] = BitConverter.ToDouble(data.Slice(32 + (i * 16), 8));
                inputs[i] = data[32 + (i * 16) + 8] < 128 ? double.NaN : BitConverter.ToDouble(data.Slice(32 + (i * 16) + 8, 8));
            }

            if (EasingFunction.FromStops(outputs, inputs) is { } l)
            {
                list.Add(l);
            }
        }

        bool finiteInputs = double.IsFinite(d0) && double.IsFinite(d1) && double.IsFinite(d2) && double.IsFinite(d3);
        foreach (EasingFunction f in list)
        {
            foreach (double x in new[] { -1e300, -2, -0.5, 0, 0.1, 0.5, 0.999, 1, 1.5, 3, 1e300 })
            {
                if (finiteInputs && !double.IsFinite(f.Evaluate(x)))
                {
                    throw new InvalidOperationException(f.Text + " at " + x);
                }
            }
        }
    }
}
