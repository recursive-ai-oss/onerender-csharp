using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>How a source supplies the value of one property for one node.</summary>
internal enum ValueOrigin : byte
{
    /// <summary>Not declared: inherited properties inherit, the others take their initial value.</summary>
    Unset,

    /// <summary>A value to parse.</summary>
    Specified,

    /// <summary>The property's initial value (<c>initial</c>).</summary>
    Initial,

    /// <summary>The parent's computed value (<c>inherit</c>).</summary>
    Inherit,
}

/// <summary>
/// Where declared values come from. The OneHtml front end answers from the cascade (which has already resolved
/// <c>inherit</c>, <c>initial</c>, <c>unset</c> and <c>var()</c>); the builder front end answers from its own declarations.
/// </summary>
internal abstract class DeclarationSource
{
    /// <summary>The value of <paramref name="id"/>; <paramref name="value"/> is meaningful only for <see cref="ValueOrigin.Specified"/>.</summary>
    public abstract ValueOrigin Get(PropertyId id, out ComponentValueList value);
}

/// <summary>What styles are computed against: the viewport (for <c>vw</c> and friends) and the root element's font size (for <c>rem</c>).</summary>
internal readonly record struct ComputeEnvironment(double ViewportWidth, double ViewportHeight, double RootFontSize);

/// <summary>
/// Turns declared values into <see cref="ComputedValues"/>: parses each property against its grammar and applies the
/// "Computed value" line of its definition. One instance is reused for many nodes; it is not thread-safe.
/// </summary>
internal sealed partial class StyleComputer
{
    /// <summary>Most space-separated parts a value may have; anything longer is invalid. Checked before any part is split out.</summary>
    public const int MaxParts = 256;

    /// <summary>Longest value text, in UTF-16 units, the builder accepts for one declaration.</summary>
    public const int MaxValueLength = 1 << 16;

    private readonly ComponentValue[] _parts = new ComponentValue[MaxParts];
    private readonly double[] _rawBorderWidth = new double[4];
    private ComputedValues _r;
    private ComputedValues _parent;
    private bool _hasParent;
    private LengthContext _ctx = LengthContext.Default;
    private LengthContext _parentCtx = LengthContext.Default;
    private ComputeEnvironment _env;
    private int _floatLogical;
    private int _clearLogical;

    /// <summary>Counts declarations that did not parse, in the last <see cref="Compute"/>.</summary>
    public int InvalidCount { get; private set; }

    /// <summary>The first property that did not parse in the last <see cref="Compute"/>, or null.</summary>
    public string? FirstInvalid { get; private set; }

    /// <summary>Computes one node's style.</summary>
    /// <param name="source">The declared values.</param>
    /// <param name="parent">The parent's computed values; ignored when <paramref name="hasParent"/> is false.</param>
    /// <param name="hasParent">False for the root.</param>
    /// <param name="env">Viewport and root font size.</param>
    /// <param name="isRoot">True for the document's root element (its display is blockified).</param>
    public ComputedValues Compute(DeclarationSource source, in ComputedValues parent, bool hasParent, in ComputeEnvironment env, bool isRoot)
    {
        InvalidCount = 0;
        FirstInvalid = null;
        _env = env;
        _hasParent = hasParent;
        _parent = hasParent ? parent : ComputedValues.Initial;
        _r = hasParent ? InheritedFrom(in parent) : ComputedValues.Initial;
        _floatLogical = 0;
        _clearLogical = 0;
        _rawBorderWidth[0] = _rawBorderWidth[1] = _rawBorderWidth[2] = _rawBorderWidth[3] = 3;
        _rawColumnRuleWidth = 3;
        _parentCtx = LengthContext.Default with
        {
            FontSize = _parent.FontSize,
            RootFontSize = env.RootFontSize,
            ViewportWidth = env.ViewportWidth,
            ViewportHeight = env.ViewportHeight,
        };
        _ctx = _parentCtx;
        for (int i = 0; i < Properties.Count; i++)
        {
            var id = (PropertyId)i;
            ValueOrigin origin = source.Get(id, out ComponentValueList value);
            switch (origin)
            {
                case ValueOrigin.Unset:
                    break;
                case ValueOrigin.Initial:
                    SetFromInitial(id);
                    break;
                case ValueOrigin.Inherit:
                    CopyFromParent(id);
                    break;
                default:
                    if (!Apply(id, value))
                    {
                        // Invalid at computed-value time: behaves as `unset` (CSS Variables 1 §3.2 for the invalid-at-computed-value case; the cascade here
                        // cannot fall back to an earlier declaration).
                        InvalidCount++;
                        FirstInvalid ??= Properties.NameOf(id);
                        if (Properties.IsInherited(id) && hasParent)
                        {
                            CopyFromParent(id);
                        }
                        else
                        {
                            SetFromInitial(id);
                        }
                    }

                    break;
            }

            if (id == PropertyId.FontSize)
            {
                // Everything after font-size resolves em against the element's own size (CSS Values 4 §6.1.1).
                _r.FontSize = Math.Max(0, _r.FontSize);
                _ctx = _parentCtx with { FontSize = _r.FontSize, RootFontSize = isRoot ? _r.FontSize : _parentCtx.RootFontSize };
            }
        }

        Finalize(isRoot);
        return _r;
    }

    /// <summary>
    /// The style of an anonymous box: the inherited properties of <paramref name="parent"/>, every other property initial,
    /// and the given <paramref name="display"/> (CSS2 §9.2.1.1).
    /// </summary>
    public static ComputedValues Anonymous(in ComputedValues parent, DisplayType display)
    {
        ComputedValues v = InheritedFrom(in parent);
        v.Display = display;
        if (v.TextAlign == TextAlignKind.MatchParent)
        {
            v.TextAlign = TextAlignKind.Start;
        }

        return v;
    }

    private static ComputedValues InheritedFrom(in ComputedValues p)
    {
        ComputedValues v = ComputedValues.Initial;
        v.Visibility = p.Visibility;
        v.FontFamily = p.FontFamily;
        v.FontSize = p.FontSize;
        v.FontWeight = p.FontWeight;
        v.FontStyle = p.FontStyle;
        v.ObliqueAngle = p.ObliqueAngle;
        v.FontStretch = p.FontStretch;
        v.LineHeight = p.LineHeight;
        v.WhiteSpaceCollapse = p.WhiteSpaceCollapse;
        v.TextWrapMode = p.TextWrapMode;
        v.TextAlign = p.TextAlign;
        v.TextIndent = p.TextIndent;
        v.TextIndentHanging = p.TextIndentHanging;
        v.TextIndentEachLine = p.TextIndentEachLine;
        v.LetterSpacing = p.LetterSpacing;
        v.WordSpacing = p.WordSpacing;
        v.Direction = p.Direction;
        v.Color = p.Color;
        v.ListStyleType = p.ListStyleType;
        v.ListStyleTypeIsString = p.ListStyleTypeIsString;
        v.ListStylePosition = p.ListStylePosition;
        v.Orphans = p.Orphans;
        v.Widows = p.Widows;
        v.WordBreak = p.WordBreak;
        v.OverflowWrap = p.OverflowWrap;
        v.LineBreak = p.LineBreak;
        v.Hyphens = p.Hyphens;
        v.TabSize = p.TabSize;
        v.TextAlignLast = p.TextAlignLast;
        v.TextJustify = p.TextJustify;
        return v;
    }

    // ---- inherit / initial ----

    private void CopyFromParent(PropertyId id) => CopyProperty(id, ref _r, in _parent, true);

    private void SetFromInitial(PropertyId id) => CopyProperty(id, ref _r, in ComputedValues.Initial, false);

    /// <summary>Copies one property (all the fields that make it up) from <paramref name="s"/> to <paramref name="d"/>.</summary>
    private void CopyProperty(PropertyId id, ref ComputedValues d, in ComputedValues s, bool fromParent)
    {
        switch (id)
        {
            case PropertyId.FontSize: d.FontSize = s.FontSize; break;
            case PropertyId.Display: d.Display = s.Display; break;
            case PropertyId.Position: d.Position = s.Position; break;
            case PropertyId.Float: d.Floating = s.Floating; _floatLogical = 0; break;
            case PropertyId.Clear: d.Clear = s.Clear; _clearLogical = 0; break;
            case PropertyId.BoxSizing: d.BoxSizing = s.BoxSizing; break;
            case PropertyId.Top: d.Top = s.Top; break;
            case PropertyId.Right: d.Right = s.Right; break;
            case PropertyId.Bottom: d.Bottom = s.Bottom; break;
            case PropertyId.Left: d.Left = s.Left; break;
            case PropertyId.Width: d.Width = s.Width; break;
            case PropertyId.Height: d.Height = s.Height; break;
            case PropertyId.MinWidth: d.MinWidth = s.MinWidth; break;
            case PropertyId.MinHeight: d.MinHeight = s.MinHeight; break;
            case PropertyId.MaxWidth: d.MaxWidth = s.MaxWidth; break;
            case PropertyId.MaxHeight: d.MaxHeight = s.MaxHeight; break;
            case PropertyId.AspectRatio: d.AspectRatioAuto = s.AspectRatioAuto; d.AspectRatioWidth = s.AspectRatioWidth; d.AspectRatioHeight = s.AspectRatioHeight; break;
            case PropertyId.MarginTop: d.MarginTop = s.MarginTop; break;
            case PropertyId.MarginRight: d.MarginRight = s.MarginRight; break;
            case PropertyId.MarginBottom: d.MarginBottom = s.MarginBottom; break;
            case PropertyId.MarginLeft: d.MarginLeft = s.MarginLeft; break;
            case PropertyId.PaddingTop: d.PaddingTop = s.PaddingTop; break;
            case PropertyId.PaddingRight: d.PaddingRight = s.PaddingRight; break;
            case PropertyId.PaddingBottom: d.PaddingBottom = s.PaddingBottom; break;
            case PropertyId.PaddingLeft: d.PaddingLeft = s.PaddingLeft; break;
            case PropertyId.BorderTopWidth: _rawBorderWidth[0] = fromParent ? s.BorderTopWidth : 3; break;
            case PropertyId.BorderRightWidth: _rawBorderWidth[1] = fromParent ? s.BorderRightWidth : 3; break;
            case PropertyId.BorderBottomWidth: _rawBorderWidth[2] = fromParent ? s.BorderBottomWidth : 3; break;
            case PropertyId.BorderLeftWidth: _rawBorderWidth[3] = fromParent ? s.BorderLeftWidth : 3; break;
            case PropertyId.BorderTopStyle: d.BorderTopStyle = s.BorderTopStyle; break;
            case PropertyId.BorderRightStyle: d.BorderRightStyle = s.BorderRightStyle; break;
            case PropertyId.BorderBottomStyle: d.BorderBottomStyle = s.BorderBottomStyle; break;
            case PropertyId.BorderLeftStyle: d.BorderLeftStyle = s.BorderLeftStyle; break;
            case PropertyId.BorderTopColor: d.BorderTopColor = s.BorderTopColor; break;
            case PropertyId.BorderRightColor: d.BorderRightColor = s.BorderRightColor; break;
            case PropertyId.BorderBottomColor: d.BorderBottomColor = s.BorderBottomColor; break;
            case PropertyId.BorderLeftColor: d.BorderLeftColor = s.BorderLeftColor; break;
            case PropertyId.BorderTopLeftRadius: d.BorderTopLeftRadius = s.BorderTopLeftRadius; break;
            case PropertyId.BorderTopRightRadius: d.BorderTopRightRadius = s.BorderTopRightRadius; break;
            case PropertyId.BorderBottomRightRadius: d.BorderBottomRightRadius = s.BorderBottomRightRadius; break;
            case PropertyId.BorderBottomLeftRadius: d.BorderBottomLeftRadius = s.BorderBottomLeftRadius; break;
            case PropertyId.OverflowX: d.OverflowX = s.OverflowX; break;
            case PropertyId.OverflowY: d.OverflowY = s.OverflowY; break;
            case PropertyId.ZIndex: d.ZIndexAuto = s.ZIndexAuto; d.ZIndex = s.ZIndex; break;
            case PropertyId.Visibility: d.Visibility = s.Visibility; break;
            case PropertyId.FontFamily: d.FontFamily = s.FontFamily; break;
            case PropertyId.FontWeight: d.FontWeight = s.FontWeight; break;
            case PropertyId.FontStyle: d.FontStyle = s.FontStyle; d.ObliqueAngle = s.ObliqueAngle; break;
            case PropertyId.FontStretch: d.FontStretch = s.FontStretch; break;
            case PropertyId.LineHeight: d.LineHeight = s.LineHeight; break;
            case PropertyId.VerticalAlign: d.VerticalAlign = s.VerticalAlign; d.VerticalAlignOffset = s.VerticalAlignOffset; break;
            case PropertyId.WhiteSpaceCollapse: d.WhiteSpaceCollapse = s.WhiteSpaceCollapse; break;
            case PropertyId.TextWrapMode: d.TextWrapMode = s.TextWrapMode; break;
            case PropertyId.TextAlign: d.TextAlign = s.TextAlign; break;
            case PropertyId.TextIndent: d.TextIndent = s.TextIndent; d.TextIndentHanging = s.TextIndentHanging; d.TextIndentEachLine = s.TextIndentEachLine; break;
            case PropertyId.LetterSpacing: d.LetterSpacing = s.LetterSpacing; break;
            case PropertyId.WordSpacing: d.WordSpacing = s.WordSpacing; break;
            case PropertyId.Direction: d.Direction = s.Direction; break;
            case PropertyId.UnicodeBidi: d.UnicodeBidi = s.UnicodeBidi; break;
            case PropertyId.Color: d.Color = s.Color; break;
            case PropertyId.Opacity: d.Opacity = s.Opacity; break;
            case PropertyId.BackgroundColor: d.BackgroundColor = s.BackgroundColor; break;
            case PropertyId.Content: d.Content = s.Content; d.ContentText = s.ContentText; break;
            case PropertyId.ListStyleType: d.ListStyleType = s.ListStyleType; d.ListStyleTypeIsString = s.ListStyleTypeIsString; break;
            case PropertyId.ListStylePosition: d.ListStylePosition = s.ListStylePosition; break;
            case PropertyId.WordBreak: d.WordBreak = s.WordBreak; break;
            case PropertyId.OverflowWrap: d.OverflowWrap = s.OverflowWrap; break;
            case PropertyId.LineBreak: d.LineBreak = s.LineBreak; break;
            case PropertyId.Hyphens: d.Hyphens = s.Hyphens; break;
            case PropertyId.TabSize: d.TabSize = s.TabSize; break;
            case PropertyId.TextAlignLast: d.TextAlignLast = s.TextAlignLast; break;
            case PropertyId.TextJustify: d.TextJustify = s.TextJustify; break;
            case PropertyId.OverflowClipMargin: d.OverflowClipMarginPx = s.OverflowClipMarginPx; d.OverflowClipMarginBox = s.OverflowClipMarginBox; break;
            default: CopyFlexProperty(id, ref d, in s, fromParent); CopyGridProperty(id, ref d, in s); break;
        }
    }

    // ---- computed-value fix-ups that depend on several properties ----

    private void Finalize(bool isRoot)
    {
        // CSS Backgrounds 3 §4.1: a border whose style is none or hidden has a computed width of 0.
        _r.ColumnRuleWidth = VisibleBorder(_r.ColumnRuleStyle) ? _rawColumnRuleWidth : 0;
        _r.BorderTopWidth = VisibleBorder(_r.BorderTopStyle) ? _rawBorderWidth[0] : 0;
        _r.BorderRightWidth = VisibleBorder(_r.BorderRightStyle) ? _rawBorderWidth[1] : 0;
        _r.BorderBottomWidth = VisibleBorder(_r.BorderBottomStyle) ? _rawBorderWidth[2] : 0;
        _r.BorderLeftWidth = VisibleBorder(_r.BorderLeftStyle) ? _rawBorderWidth[3] : 0;

        // CSS Logical 1 §3.3: inline-start / inline-end of float and clear are left or right by the element's direction.
        bool rtl = _r.Direction == DirectionKind.Rtl;
        if (_floatLogical != 0)
        {
            _r.Floating = (_floatLogical == 1) == rtl ? FloatKind.Right : FloatKind.Left;
        }

        if (_clearLogical != 0)
        {
            _r.Clear = (_clearLogical == 1) == rtl ? ClearKind.Right : ClearKind.Left;
        }

        // CSS Text 3 §7.1: match-parent computes against the parent's direction.
        if (_r.TextAlign == TextAlignKind.MatchParent)
        {
            bool parentRtl = _parent.Direction == DirectionKind.Rtl;
            _r.TextAlign = !_hasParent ? TextAlignKind.Start : _parent.TextAlign switch
            {
                TextAlignKind.Start => parentRtl ? TextAlignKind.Right : TextAlignKind.Left,
                TextAlignKind.End => parentRtl ? TextAlignKind.Left : TextAlignKind.Right,
                TextAlignKind.MatchParent => TextAlignKind.Start,
                TextAlignKind.JustifyAll => TextAlignKind.Justify,
                TextAlignKind other => other,
            };
        }

        // CSS Text 3 §7.2: text-align-last: match-parent computes like text-align.
        if (_r.TextAlignLast == TextAlignLastKind.MatchParent)
        {
            bool parentRtl = _parent.Direction == DirectionKind.Rtl;
            _r.TextAlignLast = !_hasParent ? TextAlignLastKind.Start : _parent.TextAlignLast switch
            {
                TextAlignLastKind.Start => parentRtl ? TextAlignLastKind.Right : TextAlignLastKind.Left,
                TextAlignLastKind.End => parentRtl ? TextAlignLastKind.Left : TextAlignLastKind.Right,
                TextAlignLastKind.MatchParent => TextAlignLastKind.Start,
                TextAlignLastKind other => other,
            };
        }

        // CSS2 §9.7: absolute and fixed positioning force float to none, and any float or out-of-flow box,
        // and the root, has a blockified display (CSS Display 3 §2.7).
        if (_r.Position is PositionKind.Absolute or PositionKind.Fixed)
        {
            _r.Floating = FloatKind.None;
        }

        if ((_r.Floating != FloatKind.None || _r.Position is PositionKind.Absolute or PositionKind.Fixed || isRoot) && !_r.Display.IsNone && !_r.Display.IsContents)
        {
            _r.Display = _r.Display.Blockified();
        }

        // CSS Overflow 3 §3.1: visible and clip cannot be combined with a scrolling value on the other axis.
        bool xScroll = _r.OverflowX is not (OverflowKind.Visible or OverflowKind.Clip);
        bool yScroll = _r.OverflowY is not (OverflowKind.Visible or OverflowKind.Clip);
        if (xScroll != yScroll)
        {
            if (xScroll)
            {
                _r.OverflowY = _r.OverflowY == OverflowKind.Visible ? OverflowKind.Auto : OverflowKind.Hidden;
            }
            else
            {
                _r.OverflowX = _r.OverflowX == OverflowKind.Visible ? OverflowKind.Auto : OverflowKind.Hidden;
            }
        }
    }

    private static bool VisibleBorder(BorderStyleKind s) => s is not (BorderStyleKind.None or BorderStyleKind.Hidden);

    private void Invalid(PropertyId id)
    {
        InvalidCount++;
        FirstInvalid ??= Properties.NameOf(id);
    }
}
