using System.Text;
using OneRender.Style;

namespace OneRender.Animation;

/// <summary>The family of an <see cref="EasingFunction"/>.</summary>
internal enum EasingKind : byte
{
    /// <summary>A cubic Bézier curve (CSS Easing 1 §2.2).</summary>
    CubicBezier,

    /// <summary>A step function (CSS Easing 1 §2.3).</summary>
    Steps,

    /// <summary>A piecewise linear function (CSS Easing 1 §2.1).</summary>
    Linear,
}

/// <summary>The jump terms of <c>steps()</c> (CSS Easing 1 §2.3).</summary>
internal enum StepPosition : byte
{
    /// <summary><c>jump-end</c> (also <c>end</c>).</summary>
    JumpEnd,

    /// <summary><c>jump-start</c> (also <c>start</c>).</summary>
    JumpStart,

    /// <summary><c>jump-none</c>.</summary>
    JumpNone,

    /// <summary><c>jump-both</c>.</summary>
    JumpBoth,
}

/// <summary>
/// An easing function: a map from input progress to output progress (CSS Easing 1). Immutable; evaluating allocates nothing.
/// </summary>
internal sealed class EasingFunction : IEquatable<EasingFunction>
{
    /// <summary>Most stops a <c>linear()</c> function keeps; later ones make the value invalid.</summary>
    public const int MaxLinearStops = 256;

    /// <summary>Most steps <c>steps()</c> accepts; larger counts are invalid (the output would be a staircase finer than a double can show).</summary>
    public const int MaxSteps = 1 << 20;

    private const double Epsilon = 1e-7;

    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;
    private readonly int _steps;
    private readonly StepPosition _position;
    private readonly double[] _inputs;
    private readonly double[] _outputs;

    private EasingFunction(string text, double x1, double y1, double x2, double y2)
    {
        Kind = EasingKind.CubicBezier;
        Text = text;
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        _inputs = _outputs = [];
    }

    private EasingFunction(string text, int steps, StepPosition position)
    {
        Kind = EasingKind.Steps;
        Text = text;
        _steps = steps;
        _position = position;
        _inputs = _outputs = [];
    }

    private EasingFunction(string text, double[] inputs, double[] outputs)
    {
        Kind = EasingKind.Linear;
        Text = text;
        _inputs = inputs;
        _outputs = outputs;
    }

    /// <summary><c>ease</c>: cubic-bezier(0.25, 0.1, 0.25, 1).</summary>
    public static EasingFunction Ease { get; } = new("ease", 0.25, 0.1, 0.25, 1);

    /// <summary><c>ease-in</c>: cubic-bezier(0.42, 0, 1, 1).</summary>
    public static EasingFunction EaseIn { get; } = new("ease-in", 0.42, 0, 1, 1);

    /// <summary><c>ease-out</c>: cubic-bezier(0, 0, 0.58, 1).</summary>
    public static EasingFunction EaseOut { get; } = new("ease-out", 0, 0, 0.58, 1);

    /// <summary><c>ease-in-out</c>: cubic-bezier(0.42, 0, 0.58, 1).</summary>
    public static EasingFunction EaseInOut { get; } = new("ease-in-out", 0.42, 0, 0.58, 1);

    /// <summary><c>linear</c>: the identity, as <c>linear(0, 1)</c>.</summary>
    public static EasingFunction Linear { get; } = new("linear", [0, 1], [0, 1]);

    /// <summary><c>step-start</c>: steps(1, jump-start).</summary>
    public static EasingFunction StepStart { get; } = new("steps(1, start)", 1, StepPosition.JumpStart);

    /// <summary><c>step-end</c>: steps(1, jump-end).</summary>
    public static EasingFunction StepEnd { get; } = new("steps(1)", 1, StepPosition.JumpEnd);

    /// <summary>The family.</summary>
    public EasingKind Kind { get; }

    /// <summary>The canonical text: the computed-value serialization (CSS Easing 1 §2.5 and CSS Transitions 1 §2.2).</summary>
    public string Text { get; }

    /// <summary>A cubic Bézier curve.</summary>
    /// <returns>The function, or null when an x coordinate is outside [0, 1] or a coordinate is not finite.</returns>
    public static EasingFunction? CubicBezier(double x1, double y1, double x2, double y2)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2) || x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1)
        {
            return null;
        }

        if (x1 == 0.25 && y1 == 0.1 && x2 == 0.25 && y2 == 1)
        {
            return Ease;
        }

        if (x1 == 0.42 && y1 == 0 && x2 == 1 && y2 == 1)
        {
            return EaseIn;
        }

        if (x1 == 0 && y1 == 0 && x2 == 0.58 && y2 == 1)
        {
            return EaseOut;
        }

        if (x1 == 0.42 && y1 == 0 && x2 == 0.58 && y2 == 1)
        {
            return EaseInOut;
        }

        string text = "cubic-bezier(" + CssText.Number(x1) + ", " + CssText.Number(y1) + ", " + CssText.Number(x2) + ", " + CssText.Number(y2) + ")";
        return new EasingFunction(text, x1, y1, x2, y2);
    }

    /// <summary>A step function.</summary>
    /// <returns>The function, or null when the count is below 1 (below 2 for <c>jump-none</c>) or above <see cref="MaxSteps"/>.</returns>
    public static EasingFunction? Step(int count, StepPosition position)
    {
        if (count < 1 || count > MaxSteps || (position == StepPosition.JumpNone && count < 2))
        {
            return null;
        }

        string text = position switch
        {
            StepPosition.JumpEnd => "steps(" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
            StepPosition.JumpStart => "steps(" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", start)",
            StepPosition.JumpNone => "steps(" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", jump-none)",
            _ => "steps(" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", jump-both)",
        };
        return new EasingFunction(text, count, position);
    }

    /// <summary>
    /// A <c>linear()</c> function from its stops. A stop's input is NaN when the author left it out; the rules of CSS Easing 1 §2.1
    /// (first stop 0%, last 100%, inputs never below an earlier one, gaps spread evenly) fill them in.
    /// </summary>
    /// <param name="outputs">The output of each stop, at least one, at most <see cref="MaxLinearStops"/>.</param>
    /// <param name="inputs">The input of each stop in 0 to 1 units, NaN when unspecified; same length as <paramref name="outputs"/>.</param>
    /// <returns>The function, or null when the lists do not fit the limits.</returns>
    public static EasingFunction? FromStops(ReadOnlySpan<double> outputs, ReadOnlySpan<double> inputs)
    {
        int n = outputs.Length;
        if (n < 1 || n > MaxLinearStops || inputs.Length != n)
        {
            return null;
        }

        var o = outputs.ToArray();
        var x = inputs.ToArray();
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(o[i]) || double.IsInfinity(x[i]))
            {
                return null;
            }
        }

        if (n == 1)
        {
            // A single stop: the constant function. It serializes with an explicit input as the other forms do.
            x[0] = double.IsNaN(x[0]) ? 0 : x[0];
            return new EasingFunction(LinearText(o, x), [x[0]], o);
        }

        if (double.IsNaN(x[0]))
        {
            x[0] = 0;
        }

        if (double.IsNaN(x[n - 1]))
        {
            x[n - 1] = Math.Max(1, x[0]);
        }

        double max = x[0];
        for (int i = 1; i < n; i++)
        {
            if (!double.IsNaN(x[i]))
            {
                if (x[i] < max)
                {
                    x[i] = max;
                }

                max = x[i];
            }
        }

        for (int i = 1; i < n - 1; i++)
        {
            if (!double.IsNaN(x[i]))
            {
                continue;
            }

            int j = i;
            while (double.IsNaN(x[j + 1]))
            {
                j++;
            }

            double a = x[i - 1];
            double b = x[j + 1];
            int span = j - i + 2;
            for (int k = i; k <= j; k++)
            {
                x[k] = a + ((b - a) * (k - i + 1) / span);
            }

            i = j;
        }

        if (n == 2 && x[0] == 0 && x[1] == 1 && o[0] == 0 && o[1] == 1)
        {
            return Linear;
        }

        return new EasingFunction(LinearText(o, x), x, o);
    }

    private static string LinearText(double[] outputs, double[] inputs)
    {
        var sb = new StringBuilder("linear(");
        for (int i = 0; i < outputs.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(CssText.Number(outputs[i])).Append(' ').Append(CssText.Number(inputs[i] * 100)).Append('%');
        }

        return sb.Append(')').ToString();
    }

    /// <summary>The output progress for an input progress, as the easing function maps it (CSS Easing 1 §2).</summary>
    /// <param name="progress">The input progress; may be outside 0 to 1 (extrapolated by the function's rules).</param>
    /// <param name="before">The "before flag" of the timing model, which decides which side of a step jump an exact boundary falls on.</param>
    /// <returns>The output progress; finite for finite input.</returns>
    public double Evaluate(double progress, bool before = false)
    {
        if (!double.IsFinite(progress))
        {
            return double.IsNaN(progress) ? 0 : progress > 0 ? 1 : 0;
        }

        return Kind switch
        {
            EasingKind.CubicBezier => EvaluateBezier(progress),
            EasingKind.Steps => EvaluateSteps(progress, before),
            _ => EvaluateLinear(progress),
        };
    }

    private double EvaluateBezier(double x)
    {
        // CSS Easing 1 §2.2: outside [0, 1] the curve continues along the tangent at the nearer end.
        if (x < 0)
        {
            double slope = _x1 > 0 ? _y1 / _x1 : _y1 == 0 && _x2 > 0 ? _y2 / _x2 : 0;
            return slope * x;
        }

        if (x > 1)
        {
            double slope = _x2 < 1 ? (_y2 - 1) / (_x2 - 1) : _y2 == 1 && _x1 < 1 ? (_y1 - 1) / (_x1 - 1) : 0;
            return 1 + (slope * (x - 1));
        }

        if (x == 0 || x == 1)
        {
            return x;
        }

        // Solve x(t) = input for t by Newton's method with bisection as the safe fallback; fixed iteration counts keep it deterministic.
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double e = BezierX(t) - x;
            if (Math.Abs(e) < Epsilon)
            {
                return BezierY(t);
            }

            double d = BezierDx(t);
            if (Math.Abs(d) < 1e-6)
            {
                break;
            }

            t -= e / d;
        }

        double lo = 0, hi = 1;
        t = x;
        for (int i = 0; i < 64; i++)
        {
            double e = BezierX(t);
            if (Math.Abs(e - x) < Epsilon)
            {
                break;
            }

            if (e < x)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }

            t = (lo + hi) / 2;
        }

        return BezierY(t);
    }

    private double BezierX(double t) => Cubic(_x1, _x2, t);

    private double BezierY(double t) => Cubic(_y1, _y2, t);

    private static double Cubic(double p1, double p2, double t)
    {
        double u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }

    private double BezierDx(double t)
    {
        double u = 1 - t;
        return (3 * u * u * _x1) + (6 * u * t * (_x2 - _x1)) + (3 * t * t * (1 - _x2));
    }

    private double EvaluateSteps(double input, bool before)
    {
        // CSS Easing 1 §2.3.
        int n = _steps;
        double currentStep = Math.Floor(input * n);
        if (_position is StepPosition.JumpStart or StepPosition.JumpBoth)
        {
            currentStep += 1;
        }

        // On a jump point the before flag picks the lower step.
        if (before && input * n == Math.Floor(input * n))
        {
            currentStep -= 1;
        }

        if (input >= 0 && currentStep < 0)
        {
            currentStep = 0;
        }

        int steps = _position switch
        {
            StepPosition.JumpNone => n - 1,
            StepPosition.JumpBoth => n + 1,
            _ => n,
        };

        if (input <= 1 && currentStep > steps)
        {
            currentStep = steps;
        }

        return currentStep / steps;
    }

    private double EvaluateLinear(double input)
    {
        double[] xs = _inputs;
        double[] ys = _outputs;
        int n = xs.Length;
        if (n == 1)
        {
            return ys[0];
        }

        // Outside the stops the function continues along the first or last segment (CSS Easing 1 §2.1).
        if (input < xs[0])
        {
            return Extrapolate(input);
        }

        if (input >= xs[n - 1])
        {
            return Extrapolate(input);
        }

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (xs[mid] <= input)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        // With equal inputs the later stop wins at the jump (the segment [lo, hi] then has zero width and is skipped by xs[lo] <= input).
        double w = xs[hi] - xs[lo];
        return w <= 0 ? ys[hi] : ys[lo] + ((ys[hi] - ys[lo]) * (input - xs[lo]) / w);
    }

    private double Extrapolate(double input)
    {
        double[] xs = _inputs;
        double[] ys = _outputs;
        // Find a segment with width, scanning inward from the end that is being extended.
        if (input < xs[0])
        {
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                if (xs[i + 1] > xs[i])
                {
                    return ys[i] + ((ys[i + 1] - ys[i]) * (input - xs[i]) / (xs[i + 1] - xs[i]));
                }
            }

            return ys[0];
        }

        for (int i = xs.Length - 1; i > 0; i--)
        {
            if (xs[i] > xs[i - 1])
            {
                return ys[i] + ((ys[i] - ys[i - 1]) * (input - xs[i]) / (xs[i] - xs[i - 1]));
            }
        }

        return ys[^1];
    }

    /// <inheritdoc />
    public bool Equals(EasingFunction? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as EasingFunction);

    /// <inheritdoc />
    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Text;
}
