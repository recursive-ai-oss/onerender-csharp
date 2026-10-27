using System.Globalization;
using OneRender.Animation;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>What one entry of <c>transition-property</c> names (CSS Transitions 1 §2.1).</summary>
internal enum TransitionTargetKind : byte
{
    /// <summary><c>none</c>: no property.</summary>
    None,

    /// <summary><c>all</c>: every animatable property.</summary>
    All,

    /// <summary>A property the engine computes.</summary>
    Property,

    /// <summary>A shorthand name such as <c>margin</c>; it names every longhand it sets.</summary>
    Shorthand,

    /// <summary>A name the engine does not know (an unsupported or unregistered property): kept for serialization, never transitions.</summary>
    Unknown,
}

/// <summary>One entry of <c>transition-property</c>.</summary>
internal readonly record struct TransitionTarget(TransitionTargetKind Kind, PropertyId Property, string Name)
{
    /// <summary>The initial value, <c>all</c>.</summary>
    public static TransitionTarget All { get; } = new(TransitionTargetKind.All, default, "all");

    /// <summary><c>none</c>.</summary>
    public static TransitionTarget None { get; } = new(TransitionTargetKind.None, default, "none");
}

/// <summary><c>animation-direction</c> (CSS Animations 1 §3.5).</summary>
internal enum AnimationDirectionKind : byte
{
    /// <summary><c>normal</c>.</summary>
    Normal,

    /// <summary><c>reverse</c>.</summary>
    Reverse,

    /// <summary><c>alternate</c>.</summary>
    Alternate,

    /// <summary><c>alternate-reverse</c>.</summary>
    AlternateReverse,
}

/// <summary><c>animation-fill-mode</c> (CSS Animations 1 §3.7).</summary>
internal enum AnimationFillKind : byte
{
    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>forwards</c>.</summary>
    Forwards,

    /// <summary><c>backwards</c>.</summary>
    Backwards,

    /// <summary><c>both</c>.</summary>
    Both,
}

/// <summary>
/// The computed values of <c>transition-*</c> and <c>animation-*</c>. Every list is immutable and shared between styles; a style
/// that does not mention them holds the shared initial lists, so styles stay cheap to compare.
/// </summary>
public partial struct ComputedValues
{
    /// <summary>Most items one comma-separated transition or animation list keeps; a longer list is invalid.</summary>
    internal const int MaxMotionListItems = 64;


    // A nested class so these exist before CreateInitial runs: static field order across the partial files is not defined.
    private static class Defaults
    {
        internal static readonly TransitionTarget[] InitialTransitionProperty = [TransitionTarget.All];
        internal static readonly double[] ZeroTime = [0];
        internal static readonly EasingFunction[] InitialEasing = [EasingFunction.Ease];
        internal static readonly bool[] InitialBehavior = [false];
        internal static readonly string?[] InitialAnimationName = [null];
        internal static readonly double[] InitialAnimationDuration = [double.NaN];
        internal static readonly double[] InitialIterations = [1];
        internal static readonly AnimationDirectionKind[] InitialDirection = [AnimationDirectionKind.Normal];
        internal static readonly AnimationFillKind[] InitialFill = [AnimationFillKind.None];
        internal static readonly bool[] InitialPlaying = [true];
    }

    /// <summary>True when the style names at least one animation.</summary>
    internal readonly bool HasAnimations
    {
        get
        {
            foreach (string? n in AnimationName)
            {
                if (n is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static void InitMotion(ref ComputedValues v)
    {
        v.TransitionProperty = Defaults.InitialTransitionProperty;
        v.TransitionDuration = Defaults.ZeroTime;
        v.TransitionTimingFunction = Defaults.InitialEasing;
        v.TransitionDelay = Defaults.ZeroTime;
        v.TransitionBehavior = Defaults.InitialBehavior;
        v.AnimationName = Defaults.InitialAnimationName;
        v.AnimationDuration = Defaults.InitialAnimationDuration;
        v.AnimationTimingFunction = Defaults.InitialEasing;
        v.AnimationDelay = Defaults.ZeroTime;
        v.AnimationIterationCount = Defaults.InitialIterations;
        v.AnimationDirection = Defaults.InitialDirection;
        v.AnimationFillMode = Defaults.InitialFill;
        v.AnimationPlayState = Defaults.InitialPlaying;
    }

    private static bool MotionEquals(in ComputedValues a, in ComputedValues b) =>
        a.TransitionProperty.AsSpan().SequenceEqual(b.TransitionProperty) &&
        a.TransitionDuration.AsSpan().SequenceEqual(b.TransitionDuration) &&
        a.TransitionTimingFunction.AsSpan().SequenceEqual(b.TransitionTimingFunction) &&
        a.TransitionDelay.AsSpan().SequenceEqual(b.TransitionDelay) &&
        a.TransitionBehavior.AsSpan().SequenceEqual(b.TransitionBehavior) &&
        a.AnimationName.AsSpan().SequenceEqual(b.AnimationName) &&
        SameTimes(a.AnimationDuration, b.AnimationDuration) &&
        a.AnimationTimingFunction.AsSpan().SequenceEqual(b.AnimationTimingFunction) &&
        a.AnimationDelay.AsSpan().SequenceEqual(b.AnimationDelay) &&
        a.AnimationIterationCount.AsSpan().SequenceEqual(b.AnimationIterationCount) &&
        a.AnimationDirection.AsSpan().SequenceEqual(b.AnimationDirection) &&
        a.AnimationFillMode.AsSpan().SequenceEqual(b.AnimationFillMode) &&
        a.AnimationPlayState.AsSpan().SequenceEqual(b.AnimationPlayState);

    // NaN (auto) equals NaN here; double.Equals does that, SequenceEqual on doubles uses it too, but be explicit about the intent.
    private static bool SameTimes(double[] a, double[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void MotionHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.TransitionProperty.Length);
        h.Add(v.TransitionDuration.Length);
        h.Add(v.AnimationName.Length);
        foreach (TransitionTarget t in v.TransitionProperty)
        {
            h.Add(t.Name ?? string.Empty, StringComparer.Ordinal);
        }

        foreach (string? n in v.AnimationName)
        {
            h.Add(n ?? string.Empty, StringComparer.Ordinal);
        }
    }
}

internal sealed partial class StyleComputer
{
    private static readonly string[] AnimationDirectionWords = ["normal", "reverse", "alternate", "alternate-reverse"];
    private static readonly string[] FillWords = ["none", "forwards", "backwards", "both"];
    private static readonly string[] PlayStateWords = ["running", "paused"];
    private static readonly string[] BehaviorWords = ["normal", "allow-discrete"];
    private static readonly string[] CssWideWords = ["inherit", "initial", "unset", "revert", "revert-layer", "default"];

    private bool ApplyMotion(PropertyId id, ComponentValueList v)
    {
        if (!SplitCommas(v, out List<ComponentValue[]> segs) || segs.Count > ComputedValues.MaxMotionListItems)
        {
            return false;
        }

        int n = segs.Count;
        switch (id)
        {
            case PropertyId.TransitionProperty:
            {
                var list = new TransitionTarget[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1 || !TransitionTargetOf(segs[i][0], out list[i]))
                    {
                        return false;
                    }

                    // `none` stands alone (CSS Transitions 1 §2.1).
                    if (list[i].Kind == TransitionTargetKind.None && n > 1)
                    {
                        return false;
                    }
                }

                _r.TransitionProperty = list;
                return true;
            }

            case PropertyId.TransitionDuration:
            case PropertyId.AnimationDuration:
            case PropertyId.TransitionDelay:
            case PropertyId.AnimationDelay:
            {
                bool isDuration = id is PropertyId.TransitionDuration or PropertyId.AnimationDuration;
                var list = new double[n];
                for (int i = 0; i < n; i++)
                {
                    if (!TimeOf(segs[i], isDuration, id == PropertyId.AnimationDuration, out list[i]))
                    {
                        return false;
                    }
                }

                switch (id)
                {
                    case PropertyId.TransitionDuration: _r.TransitionDuration = list; break;
                    case PropertyId.AnimationDuration: _r.AnimationDuration = list; break;
                    case PropertyId.TransitionDelay: _r.TransitionDelay = list; break;
                    default: _r.AnimationDelay = list; break;
                }

                return true;
            }

            case PropertyId.TransitionTimingFunction:
            case PropertyId.AnimationTimingFunction:
            {
                var list = new EasingFunction[n];
                for (int i = 0; i < n; i++)
                {
                    if (!EasingOf(segs[i], out EasingFunction? e))
                    {
                        return false;
                    }

                    list[i] = e;
                }

                if (id == PropertyId.TransitionTimingFunction)
                {
                    _r.TransitionTimingFunction = list;
                }
                else
                {
                    _r.AnimationTimingFunction = list;
                }

                return true;
            }

            case PropertyId.TransitionBehavior:
            {
                var list = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1 || !IdentIndex(segs[i][0], BehaviorWords, out int k))
                    {
                        return false;
                    }

                    list[i] = k == 1;
                }

                _r.TransitionBehavior = list;
                return true;
            }

            case PropertyId.AnimationName:
            {
                var list = new string?[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1)
                    {
                        return false;
                    }

                    ComponentValue p = segs[i][0];
                    if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.QuotedString)
                    {
                        list[i] = p.Token.Value;
                    }
                    else if (IsIdent(p))
                    {
                        string name = p.Token.Value;
                        if (WordIndex(name, CssWideWords) >= 0)
                        {
                            return false;
                        }

                        list[i] = name.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : name;
                    }
                    else
                    {
                        return false;
                    }

                    // `none` may appear in a list; CSS Animations 1 §3.2 only forbids the CSS-wide keywords as names.
                }

                _r.AnimationName = list;
                return true;
            }

            case PropertyId.AnimationIterationCount:
            {
                var list = new double[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1)
                    {
                        return false;
                    }

                    ComponentValue p = segs[i][0];
                    if (IsIdent(p) && p.Token.ValueEqualsIgnoreCase("infinite"))
                    {
                        list[i] = double.PositiveInfinity;
                    }
                    else if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.Number && p.Token.NumberValue >= 0 && double.IsFinite(p.Token.NumberValue))
                    {
                        list[i] = p.Token.NumberValue;
                    }
                    else
                    {
                        return false;
                    }
                }

                _r.AnimationIterationCount = list;
                return true;
            }

            case PropertyId.AnimationDirection:
            {
                var list = new AnimationDirectionKind[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1 || !IdentIndex(segs[i][0], AnimationDirectionWords, out int k))
                    {
                        return false;
                    }

                    list[i] = (AnimationDirectionKind)k;
                }

                _r.AnimationDirection = list;
                return true;
            }

            case PropertyId.AnimationFillMode:
            {
                var list = new AnimationFillKind[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1 || !IdentIndex(segs[i][0], FillWords, out int k))
                    {
                        return false;
                    }

                    list[i] = (AnimationFillKind)k;
                }

                _r.AnimationFillMode = list;
                return true;
            }

            case PropertyId.AnimationPlayState:
            {
                var list = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (segs[i].Length != 1 || !IdentIndex(segs[i][0], PlayStateWords, out int k))
                    {
                        return false;
                    }

                    list[i] = k == 0;
                }

                _r.AnimationPlayState = list;
                return true;
            }

            default: return false;
        }
    }

    /// <summary>Parses one timing function, for a keyframe that declares <c>animation-timing-function</c> inside its block.</summary>
    internal static EasingFunction? ParseEasing(string text)
    {
        ComponentValueList list = CssTree.ParseComponentValueList(text).Values;
        return SplitCommas(list, out List<ComponentValue[]> segs) && segs.Count == 1 && EasingOf(segs[0], out EasingFunction easing) ? easing : null;
    }

    private static bool IdentIndex(ComponentValue p, string[] words, out int index)
    {
        index = -1;
        if (!IsIdent(p))
        {
            return false;
        }

        index = WordIndex(p.Raw, words);
        return index >= 0;
    }

    // CSS Transitions 1 §2.1: <single-transition-property> = all | <custom-ident>; the CSS-wide keywords and `default` are not idents here.
    private static bool TransitionTargetOf(ComponentValue p, out TransitionTarget target)
    {
        target = default;
        if (!IsIdent(p))
        {
            return false;
        }

        string name = p.Token.Value;
        if (WordIndex(name, CssWideWords) >= 0)
        {
            return false;
        }

        if (name.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            target = TransitionTarget.None;
            return true;
        }

        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            target = TransitionTarget.All;
            return true;
        }

        if (!name.StartsWith("--", StringComparison.Ordinal))
        {
            name = name.ToLowerInvariant();
        }

        if (Properties.TryGet(name, out PropertyId id))
        {
            target = new TransitionTarget(TransitionTargetKind.Property, id, name);
        }
        else if (TransitionShorthands.IsShorthand(name))
        {
            target = new TransitionTarget(TransitionTargetKind.Shorthand, default, name);
        }
        else
        {
            target = new TransitionTarget(TransitionTargetKind.Unknown, default, name);
        }

        return true;
    }

    // <time>: a dimension in s or ms. Durations may not be negative; `auto` is a valid animation-duration (CSS Animations 2 §3.3).
    private static bool TimeOf(ComponentValue[] seg, bool duration, bool allowAuto, out double seconds)
    {
        seconds = 0;
        if (seg.Length != 1)
        {
            return false;
        }

        ComponentValue p = seg[0];
        if (allowAuto && IsIdent(p) && p.Token.ValueEqualsIgnoreCase("auto"))
        {
            seconds = double.NaN;
            return true;
        }

        if (p.Kind != ComponentValueKind.Token || p.Token.Kind != CssTokenKind.Dimension)
        {
            return false;
        }

        string unit = p.Token.Value;
        double n = p.Token.NumberValue;
        if (!double.IsFinite(n))
        {
            return false;
        }

        if (unit.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            seconds = n;
        }
        else if (unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
        {
            seconds = n / 1000;
        }
        else
        {
            return false;
        }

        return !duration || seconds >= 0;
    }

    // CSS Easing 1 §2: keywords, cubic-bezier(), steps() and linear().
    private static bool EasingOf(ComponentValue[] seg, out EasingFunction easing)
    {
        easing = EasingFunction.Ease;
        if (seg.Length != 1)
        {
            return false;
        }

        ComponentValue p = seg[0];
        if (IsIdent(p))
        {
            switch (p.Token.Value.ToLowerInvariant())
            {
                case "ease": easing = EasingFunction.Ease; return true;
                case "linear": easing = EasingFunction.Linear; return true;
                case "ease-in": easing = EasingFunction.EaseIn; return true;
                case "ease-out": easing = EasingFunction.EaseOut; return true;
                case "ease-in-out": easing = EasingFunction.EaseInOut; return true;
                case "step-start": easing = EasingFunction.StepStart; return true;
                case "step-end": easing = EasingFunction.StepEnd; return true;
                default: return false;
            }
        }

        if (p.Kind != ComponentValueKind.Function || !FunctionArgs(p, out List<ComponentValue[]> args))
        {
            return false;
        }

        EasingFunction? e;
        switch (p.Token.Value.ToLowerInvariant())
        {
            case "cubic-bezier":
            {
                if (args.Count != 4 || !NumberOf(args[0], out double x1) || !NumberOf(args[1], out double y1) || !NumberOf(args[2], out double x2) || !NumberOf(args[3], out double y2))
                {
                    return false;
                }

                e = EasingFunction.CubicBezier(x1, y1, x2, y2);
                break;
            }

            case "steps":
            {
                if (args.Count is < 1 or > 2 || args[0].Length != 1 || args[0][0].Kind != ComponentValueKind.Token || args[0][0].Token.Kind != CssTokenKind.Number
                    || !args[0][0].Token.IsInteger)
                {
                    return false;
                }

                double count = args[0][0].Token.NumberValue;
                if (!double.IsFinite(count) || count < 1 || count > EasingFunction.MaxSteps)
                {
                    return false;
                }

                StepPosition pos = StepPosition.JumpEnd;
                if (args.Count == 2)
                {
                    if (args[1].Length != 1 || !IsIdent(args[1][0]))
                    {
                        return false;
                    }

                    switch (args[1][0].Token.Value.ToLowerInvariant())
                    {
                        case "jump-start": case "start": pos = StepPosition.JumpStart; break;
                        case "jump-end": case "end": pos = StepPosition.JumpEnd; break;
                        case "jump-none": pos = StepPosition.JumpNone; break;
                        case "jump-both": pos = StepPosition.JumpBoth; break;
                        default: return false;
                    }
                }

                e = EasingFunction.Step((int)count, pos);
                break;
            }

            case "linear":
            {
                if (args.Count is < 1 or > EasingFunction.MaxLinearStops)
                {
                    return false;
                }

                // Each stop is <number> followed by up to two <percentage>s; two percentages make two stops with the same output.
                var outputs = new List<double>(args.Count);
                var inputs = new List<double>(args.Count);
                foreach (ComponentValue[] stop in args)
                {
                    if (stop.Length is < 1 or > 3 || stop[0].Kind != ComponentValueKind.Token || stop[0].Token.Kind != CssTokenKind.Number || !double.IsFinite(stop[0].Token.NumberValue))
                    {
                        return false;
                    }

                    double output = stop[0].Token.NumberValue;
                    if (stop.Length == 1)
                    {
                        outputs.Add(output);
                        inputs.Add(double.NaN);
                        continue;
                    }

                    for (int k = 1; k < stop.Length; k++)
                    {
                        if (stop[k].Kind != ComponentValueKind.Token || stop[k].Token.Kind != CssTokenKind.Percentage || !double.IsFinite(stop[k].Token.NumberValue))
                        {
                            return false;
                        }

                        outputs.Add(output);
                        inputs.Add(stop[k].Token.NumberValue / 100);
                    }
                }

                if (outputs.Count > EasingFunction.MaxLinearStops)
                {
                    return false;
                }

                e = EasingFunction.FromStops(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(outputs), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(inputs));
                break;
            }

            default: return false;
        }

        if (e is null)
        {
            return false;
        }

        easing = e;
        return true;
    }

    private static void CopyMotionProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.TransitionProperty: d.TransitionProperty = s.TransitionProperty; break;
            case PropertyId.TransitionDuration: d.TransitionDuration = s.TransitionDuration; break;
            case PropertyId.TransitionTimingFunction: d.TransitionTimingFunction = s.TransitionTimingFunction; break;
            case PropertyId.TransitionDelay: d.TransitionDelay = s.TransitionDelay; break;
            case PropertyId.TransitionBehavior: d.TransitionBehavior = s.TransitionBehavior; break;
            case PropertyId.AnimationName: d.AnimationName = s.AnimationName; break;
            case PropertyId.AnimationDuration: d.AnimationDuration = s.AnimationDuration; break;
            case PropertyId.AnimationTimingFunction: d.AnimationTimingFunction = s.AnimationTimingFunction; break;
            case PropertyId.AnimationDelay: d.AnimationDelay = s.AnimationDelay; break;
            case PropertyId.AnimationIterationCount: d.AnimationIterationCount = s.AnimationIterationCount; break;
            case PropertyId.AnimationDirection: d.AnimationDirection = s.AnimationDirection; break;
            case PropertyId.AnimationFillMode: d.AnimationFillMode = s.AnimationFillMode; break;
            case PropertyId.AnimationPlayState: d.AnimationPlayState = s.AnimationPlayState; break;
        }
    }
}

/// <summary>Serialization of the transition and animation properties.</summary>
internal static partial class ComputedValueText
{
    private static string? MotionText(PropertyId id, in ComputedValues v)
    {
        switch (id)
        {
            case PropertyId.TransitionProperty:
                return Join(v.TransitionProperty, static t => t.Name);
            case PropertyId.TransitionDuration: return Join(v.TransitionDuration, static d => Seconds(d));
            case PropertyId.TransitionDelay: return Join(v.TransitionDelay, static d => Seconds(d));
            case PropertyId.TransitionTimingFunction: return Join(v.TransitionTimingFunction, static e => e.Text);
            case PropertyId.TransitionBehavior: return Join(v.TransitionBehavior, static b => b ? "allow-discrete" : "normal");
            case PropertyId.AnimationName: return Join(v.AnimationName, static n => n ?? "none");
            case PropertyId.AnimationDuration: return Join(v.AnimationDuration, static d => double.IsNaN(d) ? "auto" : Seconds(d));
            case PropertyId.AnimationDelay: return Join(v.AnimationDelay, static d => Seconds(d));
            case PropertyId.AnimationTimingFunction: return Join(v.AnimationTimingFunction, static e => e.Text);
            case PropertyId.AnimationIterationCount: return Join(v.AnimationIterationCount, static c => double.IsPositiveInfinity(c) ? "infinite" : CssText.Number(c));
            case PropertyId.AnimationDirection: return Join(v.AnimationDirection, static d => d switch { AnimationDirectionKind.Reverse => "reverse", AnimationDirectionKind.Alternate => "alternate", AnimationDirectionKind.AlternateReverse => "alternate-reverse", _ => "normal" });
            case PropertyId.AnimationFillMode: return Join(v.AnimationFillMode, static f => f switch { AnimationFillKind.Forwards => "forwards", AnimationFillKind.Backwards => "backwards", AnimationFillKind.Both => "both", _ => "none" });
            case PropertyId.AnimationPlayState: return Join(v.AnimationPlayState, static r => r ? "running" : "paused");
            default: return null;
        }
    }

    private static string Seconds(double s) => CssText.Number(s) + "s";

    private static string Join<T>(T[] items, Func<T, string> text)
    {
        if (items.Length == 1)
        {
            return text(items[0]);
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(text(items[i]));
        }

        return sb.ToString();
    }
}

/// <summary>The shorthand names <c>transition-property</c> accepts and the longhands each stands for (CSS Transitions 1 §2.1).</summary>
internal static class TransitionShorthands
{
    private static readonly Dictionary<string, PropertyId[]> Map = Build();

    public static bool IsShorthand(string name) => Map.ContainsKey(name);

    public static bool TryExpand(string name, out PropertyId[] longhands) => Map.TryGetValue(name, out longhands!);

    private static Dictionary<string, PropertyId[]> Build()
    {
        var m = new Dictionary<string, PropertyId[]>(StringComparer.Ordinal)
        {
            ["margin"] = [PropertyId.MarginTop, PropertyId.MarginRight, PropertyId.MarginBottom, PropertyId.MarginLeft],
            ["padding"] = [PropertyId.PaddingTop, PropertyId.PaddingRight, PropertyId.PaddingBottom, PropertyId.PaddingLeft],
            ["inset"] = [PropertyId.Top, PropertyId.Right, PropertyId.Bottom, PropertyId.Left],
            ["border-width"] = [PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth],
            ["border-style"] = [PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle],
            ["border-color"] = [PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor],
            ["border-radius"] = [PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius],
            ["border-top"] = [PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, PropertyId.BorderTopColor],
            ["border-right"] = [PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, PropertyId.BorderRightColor],
            ["border-bottom"] = [PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, PropertyId.BorderBottomColor],
            ["border-left"] = [PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, PropertyId.BorderLeftColor],
            ["border"] =
            [
                PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth,
                PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle,
                PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor,
            ],
            ["gap"] = [PropertyId.RowGap, PropertyId.ColumnGap],
            ["flex"] = [PropertyId.FlexGrow, PropertyId.FlexShrink, PropertyId.FlexBasis],
            ["outline"] = [PropertyId.OutlineWidth, PropertyId.OutlineStyle, PropertyId.OutlineColor],
            ["overflow"] = [PropertyId.OverflowX, PropertyId.OverflowY],
            ["background"] = [PropertyId.BackgroundColor, PropertyId.BackgroundImage, PropertyId.BackgroundPositionX, PropertyId.BackgroundPositionY, PropertyId.BackgroundSize, PropertyId.BackgroundRepeat, PropertyId.BackgroundOrigin, PropertyId.BackgroundClip, PropertyId.BackgroundAttachment],
            ["background-position"] = [PropertyId.BackgroundPositionX, PropertyId.BackgroundPositionY],
            ["columns"] = [PropertyId.ColumnWidth, PropertyId.ColumnCount],
            ["column-rule"] = [PropertyId.ColumnRuleWidth, PropertyId.ColumnRuleStyle, PropertyId.ColumnRuleColor],
            ["text-decoration"] = [PropertyId.TextDecorationLine, PropertyId.TextDecorationStyle, PropertyId.TextDecorationColor, PropertyId.TextDecorationThickness],
            ["list-style"] = [PropertyId.ListStyleType, PropertyId.ListStylePosition, PropertyId.ListStyleImage],
            ["font"] = [PropertyId.FontFamily, PropertyId.FontSize, PropertyId.FontStyle, PropertyId.FontWeight, PropertyId.FontStretch, PropertyId.LineHeight],
            ["mask"] = [PropertyId.MaskImage, PropertyId.MaskMode, PropertyId.MaskRepeat, PropertyId.MaskPosition, PropertyId.MaskSize, PropertyId.MaskOrigin, PropertyId.MaskClip, PropertyId.MaskComposite],
            ["flex-flow"] = [PropertyId.FlexDirection, PropertyId.FlexWrap],
            ["place-content"] = [PropertyId.AlignContent, PropertyId.JustifyContent],
            ["place-items"] = [PropertyId.AlignItems, PropertyId.JustifyItems],
            ["place-self"] = [PropertyId.AlignSelf, PropertyId.JustifySelf],
            ["grid-gap"] = [PropertyId.RowGap, PropertyId.ColumnGap],
            ["grid-row-gap"] = [PropertyId.RowGap],
            ["grid-column-gap"] = [PropertyId.ColumnGap],
            ["border-image"] = [PropertyId.BorderImageSource, PropertyId.BorderImageSlice, PropertyId.BorderImageWidth, PropertyId.BorderImageOutset, PropertyId.BorderImageRepeat],
        };
        return m;
    }
}
