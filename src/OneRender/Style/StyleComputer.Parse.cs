using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

[Flags]
internal enum LengthAllow
{
    Length = 0,
    Percent = 1,
    Auto = 2,
    None = 4,
    Normal = 8,
    Number = 16,
    Sizing = 32,
    NonNegative = 64,
}

internal sealed partial class StyleComputer
{
    private static readonly string[] PositionWords = ["static", "relative", "absolute", "fixed", "sticky"];
    private static readonly string[] FloatWords = ["none", "left", "right", "inline-start", "inline-end"];
    private static readonly string[] ClearWords = ["none", "left", "right", "both", "inline-start", "inline-end"];
    private static readonly string[] BoxSizingWords = ["content-box", "border-box"];
    private static readonly string[] BorderStyleWords = ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset"];
    private static readonly string[] ClipMarginWords = ["padding-box", "content-box", "border-box"];
    private static readonly string[] OverflowWords = ["visible", "hidden", "clip", "scroll", "auto"];
    private static readonly string[] VisibilityWords = ["visible", "hidden", "collapse"];
    private static readonly string[] VerticalAlignWords = ["baseline", "sub", "super", "text-top", "text-bottom", "middle", "top", "bottom"];
    private static readonly string[] CollapseWords = ["collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces"];
    private static readonly string[] WrapModeWords = ["wrap", "nowrap"];
    private static readonly string[] TextAlignWords = ["start", "end", "left", "right", "center", "justify", "match-parent", "justify-all"];
    private static readonly string[] DirectionWords = ["ltr", "rtl"];
    private static readonly string[] BidiWords = ["normal", "embed", "isolate", "bidi-override", "isolate-override", "plaintext"];
    private static readonly string[] ListPositionWords = ["outside", "inside"];
    private static readonly string[] StretchWords = ["ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "normal", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded"];
    private static readonly double[] StretchPercents = [50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200];

    private bool Apply(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.FontSize: return ParseFontSize(v);
            case PropertyId.Display: return ParseDisplay(v);
            case PropertyId.Position:
            {
                if (IsWord(v, "-webkit-sticky"))
                {
                    _r.Position = PositionKind.Sticky;
                    return true;
                }

                if (!Word(v, PositionWords, out int k))
                {
                    return false;
                }

                _r.Position = (PositionKind)k;
                return true;
            }

            case PropertyId.Float:
            {
                if (!Word(v, FloatWords, out int k))
                {
                    return false;
                }

                _floatLogical = k >= 3 ? k - 2 : 0;
                _r.Floating = k is 1 or 2 ? (FloatKind)k : FloatKind.None;
                return true;
            }

            case PropertyId.Clear:
            {
                if (!Word(v, ClearWords, out int k))
                {
                    return false;
                }

                _clearLogical = k >= 4 ? k - 3 : 0;
                _r.Clear = k <= 3 ? (ClearKind)k : ClearKind.None;
                return true;
            }

            case PropertyId.BoxSizing:
            {
                if (!Word(v, BoxSizingWords, out int k))
                {
                    return false;
                }

                _r.BoxSizing = (BoxSizingKind)k;
                return true;
            }

            case PropertyId.Top: return Len(v, LengthAllow.Percent | LengthAllow.Auto, out Length lt) && Set(ref _r, id, lt);
            case PropertyId.Right: return Len(v, LengthAllow.Percent | LengthAllow.Auto, out Length lr) && Set(ref _r, id, lr);
            case PropertyId.Bottom: return Len(v, LengthAllow.Percent | LengthAllow.Auto, out Length lb) && Set(ref _r, id, lb);
            case PropertyId.Left: return Len(v, LengthAllow.Percent | LengthAllow.Auto, out Length ll) && Set(ref _r, id, ll);
            case PropertyId.Width:
            case PropertyId.Height:
                return Len(v, LengthAllow.Percent | LengthAllow.Auto | LengthAllow.Sizing | LengthAllow.NonNegative, out Length size) && Set(ref _r, id, size);
            case PropertyId.MinWidth:
            case PropertyId.MinHeight:
                return Len(v, LengthAllow.Percent | LengthAllow.Auto | LengthAllow.Sizing | LengthAllow.NonNegative, out Length min) && Set(ref _r, id, min);
            case PropertyId.MaxWidth:
            case PropertyId.MaxHeight:
                return Len(v, LengthAllow.Percent | LengthAllow.None | LengthAllow.Sizing | LengthAllow.NonNegative, out Length max) && Set(ref _r, id, max);
            case PropertyId.AspectRatio: return ParseAspectRatio(v);
            case PropertyId.MarginTop:
            case PropertyId.MarginRight:
            case PropertyId.MarginBottom:
            case PropertyId.MarginLeft:
                return Len(v, LengthAllow.Percent | LengthAllow.Auto, out Length margin) && Set(ref _r, id, margin);
            case PropertyId.PaddingTop:
            case PropertyId.PaddingRight:
            case PropertyId.PaddingBottom:
            case PropertyId.PaddingLeft:
                return Len(v, LengthAllow.Percent | LengthAllow.NonNegative, out Length padding) && Set(ref _r, id, padding);
            case PropertyId.BorderTopWidth:
            case PropertyId.BorderRightWidth:
            case PropertyId.BorderBottomWidth:
            case PropertyId.BorderLeftWidth:
                return ParseBorderWidth(id, v);
            case PropertyId.BorderTopStyle:
            case PropertyId.BorderRightStyle:
            case PropertyId.BorderBottomStyle:
            case PropertyId.BorderLeftStyle:
            {
                if (!Word(v, BorderStyleWords, out int k))
                {
                    return false;
                }

                var style = (BorderStyleKind)k;
                switch (id)
                {
                    case PropertyId.BorderTopStyle: _r.BorderTopStyle = style; break;
                    case PropertyId.BorderRightStyle: _r.BorderRightStyle = style; break;
                    case PropertyId.BorderBottomStyle: _r.BorderBottomStyle = style; break;
                    default: _r.BorderLeftStyle = style; break;
                }

                return true;
            }

            case PropertyId.BorderTopColor:
            case PropertyId.BorderRightColor:
            case PropertyId.BorderBottomColor:
            case PropertyId.BorderLeftColor:
            case PropertyId.BackgroundColor:
            case PropertyId.Color:
                return ParseColorProperty(id, v);
            case PropertyId.BorderTopLeftRadius:
            case PropertyId.BorderTopRightRadius:
            case PropertyId.BorderBottomRightRadius:
            case PropertyId.BorderBottomLeftRadius:
                return ParseRadius(id, v);
            case PropertyId.OverflowX:
            case PropertyId.OverflowY:
            {
                if (!Word(v, OverflowWords, out int k))
                {
                    return false;
                }

                if (id == PropertyId.OverflowX)
                {
                    _r.OverflowX = (OverflowKind)k;
                }
                else
                {
                    _r.OverflowY = (OverflowKind)k;
                }

                return true;
            }

            case PropertyId.OverflowClipMargin: return ParseOverflowClipMargin(v);
            case PropertyId.ZIndex: return ParseZIndex(v);
            case PropertyId.Visibility:
            {
                if (!Word(v, VisibilityWords, out int k))
                {
                    return false;
                }

                _r.Visibility = (VisibilityKind)k;
                return true;
            }

            case PropertyId.FontFamily: return ParseFontFamily(v);
            case PropertyId.FontWeight: return ParseFontWeight(v);
            case PropertyId.FontStyle: return ParseFontStyle(v);
            case PropertyId.FontStretch: return ParseFontStretch(v);
            case PropertyId.LineHeight: return ParseLineHeight(v);
            case PropertyId.VerticalAlign: return ParseVerticalAlign(v);
            case PropertyId.WhiteSpaceCollapse:
            {
                if (!Word(v, CollapseWords, out int k))
                {
                    return false;
                }

                _r.WhiteSpaceCollapse = (WhiteSpaceCollapseKind)k;
                return true;
            }

            case PropertyId.TextWrapMode:
            {
                if (!Word(v, WrapModeWords, out int k))
                {
                    return false;
                }

                _r.TextWrapMode = (TextWrapModeKind)k;
                return true;
            }

            case PropertyId.TextAlign:
            {
                if (!Word(v, TextAlignWords, out int k))
                {
                    return false;
                }

                _r.TextAlign = (TextAlignKind)k;
                return true;
            }

            case PropertyId.TextIndent: return ParseTextIndent(v);
            case PropertyId.LetterSpacing:
                return Len(v, LengthAllow.Normal, out Length ls) && Assign(ref _r, id, ls);
            case PropertyId.WordSpacing:
                return Len(v, LengthAllow.Normal | LengthAllow.Percent, out Length ws) && Assign(ref _r, id, ws);
            case PropertyId.Direction:
            {
                if (!Word(v, DirectionWords, out int k))
                {
                    return false;
                }

                _r.Direction = (DirectionKind)k;
                return true;
            }

            case PropertyId.UnicodeBidi:
            {
                if (!Word(v, BidiWords, out int k))
                {
                    return false;
                }

                _r.UnicodeBidi = (UnicodeBidiKind)k;
                return true;
            }

            case PropertyId.Opacity:
            {
                if (!TypedProperties.TryParseOpacity(v, out double o))
                {
                    return false;
                }

                _r.Opacity = o;
                return true;
            }

            case PropertyId.Content: return ParseContent(v);
            case PropertyId.ListStyleType: return ParseListStyleType(v);
            case PropertyId.ListStylePosition:
            {
                if (!Word(v, ListPositionWords, out int k))
                {
                    return false;
                }

                _r.ListStylePosition = (ListStylePositionKind)k;
                return true;
            }

            default:
                return false;
        }
    }

    // ---- small helpers ----

    /// <summary>True when the value is exactly one component value (whitespace around it ignored).</summary>
    private static bool SinglePart(ComponentValueList v, out ComponentValue part)
    {
        part = default;
        bool found = false;
        foreach (ComponentValue cv in v)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            part = cv;
            found = true;
        }

        return found;
    }

    private static bool IsIdent(ComponentValue part) => part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Ident;

    private static bool IsWord(ComponentValueList v, string word) =>
        SinglePart(v, out ComponentValue p) && IsIdent(p) && p.Raw.Equals(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Matches a value that is exactly one identifier from <paramref name="words"/> (ASCII case-insensitive).</summary>
    private static bool Word(ComponentValueList v, string[] words, out int index)
    {
        index = -1;
        if (!SinglePart(v, out ComponentValue p) || !IsIdent(p))
        {
            return false;
        }

        ReadOnlySpan<char> text = p.Raw;
        for (int i = 0; i < words.Length; i++)
        {
            if (text.Equals(words[i], StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private static int WordIndex(ReadOnlySpan<char> text, string[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            if (text.Equals(words[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int SplitParts(ComponentValueList v)
    {
        int n = 0;
        foreach (ComponentValue cv in v)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (n == MaxParts)
            {
                return -1;
            }

            _parts[n++] = cv;
        }

        return n;
    }

    private static ComponentValueList ListOf(ComponentValue part) => CssTree.ParseComponentValueList(part.Raw.ToString()).Values;

    private static bool IsSlash(ComponentValue part) => part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Delim && part.Raw.Length == 1 && part.Raw[0] == '/';

    private static bool IsIdentPart(ComponentValue part, out ReadOnlySpan<char> text)
    {
        if (part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Ident)
        {
            text = part.Raw;
            return true;
        }

        text = default;
        return false;
    }

    // ---- lengths ----

    private bool Len(ComponentValueList v, LengthAllow allow, out Length result) => Len(v, allow, in _ctx, out result);

    private static bool Len(ComponentValueList v, LengthAllow allow, in LengthContext ctx, out Length result)
    {
        result = Length.Zero;
        if (!SinglePart(v, out ComponentValue cv))
        {
            return false;
        }

        if (IsIdent(cv))
        {
            ReadOnlySpan<char> t = cv.Raw;
            if ((allow & LengthAllow.Auto) != 0 && t.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                result = Length.Auto;
                return true;
            }

            if ((allow & LengthAllow.None) != 0 && t.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                result = Length.None;
                return true;
            }

            if ((allow & LengthAllow.Normal) != 0 && t.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                result = Length.Normal;
                return true;
            }

            if ((allow & LengthAllow.Sizing) != 0)
            {
                if (t.Equals("min-content", StringComparison.OrdinalIgnoreCase) || t.Equals("-webkit-min-content", StringComparison.OrdinalIgnoreCase))
                {
                    result = new Length(LengthKind.MinContent, 0);
                    return true;
                }

                if (t.Equals("max-content", StringComparison.OrdinalIgnoreCase) || t.Equals("-webkit-max-content", StringComparison.OrdinalIgnoreCase))
                {
                    result = new Length(LengthKind.MaxContent, 0);
                    return true;
                }

                if (t.Equals("fit-content", StringComparison.OrdinalIgnoreCase) || t.Equals("-webkit-fit-content", StringComparison.OrdinalIgnoreCase))
                {
                    result = new Length(LengthKind.FitContent, 0);
                    return true;
                }

                if (t.Equals("stretch", StringComparison.OrdinalIgnoreCase) || t.Equals("-webkit-fill-available", StringComparison.OrdinalIgnoreCase))
                {
                    result = new Length(LengthKind.Stretch, 0);
                    return true;
                }
            }

            return false;
        }

        if ((allow & LengthAllow.Sizing) != 0 && cv.IsFunctionNamed("fit-content"))
        {
            // CSS Sizing 3 §5.1: fit-content(<length-percentage [0,∞]>). A calculation with a percentage inside is not supported.
            ComponentValueList arg = cv.Children;
            if (!CssQuantity.TryParse(arg, QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength | QuantityKinds.NonNegative, out CssQuantity aq))
            {
                return false;
            }

            if (aq.Calc is null && aq.Unit == CssUnit.Percent)
            {
                result = new Length(LengthKind.FitContentPercent, aq.Value);
                return true;
            }

            if (aq.IsPercentage)
            {
                return false;
            }

            result = new Length(LengthKind.FitContentPx, Math.Max(0, Px(aq, in ctx)));
            return true;
        }

        QuantityKinds kinds = QuantityKinds.Length | QuantityKinds.UnitlessZeroLength;
        if ((allow & LengthAllow.Percent) != 0)
        {
            kinds |= QuantityKinds.Percentage;
        }

        if ((allow & LengthAllow.Number) != 0)
        {
            kinds |= QuantityKinds.Number;
        }

        bool nonNegative = (allow & LengthAllow.NonNegative) != 0;
        if (nonNegative)
        {
            kinds |= QuantityKinds.NonNegative;
        }

        if (!CssQuantity.TryParse(v, kinds, out CssQuantity q))
        {
            return false;
        }

        result = ToLength(q, in ctx, nonNegative, (allow & LengthAllow.Number) != 0);
        return true;
    }

    private static double Px(CssQuantity q, in LengthContext ctx)
    {
        double px = q.Resolve(in ctx, 0);
        return double.IsNaN(px) ? 0 : px;
    }

    private static Length ToLength(CssQuantity q, in LengthContext ctx, bool nonNegative, bool numberAllowed)
    {
        if (q.Calc is not null)
        {
            if (q.Dimension == CssDimension.Number)
            {
                double n = q.Calc.Resolve(in ctx, 0);
                return new Length(LengthKind.Number, double.IsNaN(n) ? 0 : (nonNegative ? Math.Max(0, n) : n));
            }

            if (q.Calc.ContainsPercentage)
            {
                return new Length(LengthKind.Calc, 0, new CalcValue(q, in ctx));
            }

            double px = Px(q, in ctx);
            return new Length(LengthKind.Px, nonNegative ? Math.Max(0, px) : px);
        }

        if (q.Unit == CssUnit.Percent)
        {
            return new Length(LengthKind.Percent, q.Value);
        }

        if (q.Unit == CssUnit.None && numberAllowed)
        {
            return new Length(LengthKind.Number, q.Value);
        }

        return new Length(LengthKind.Px, Px(q, in ctx));
    }

    private static bool Set(ref ComputedValues r, PropertyId id, Length l)
    {
        Assign(ref r, id, l);
        return true;
    }

    private static bool Assign(ref ComputedValues r, PropertyId id, Length l)
    {
        switch (id)
        {
            case PropertyId.Top: r.Top = l; break;
            case PropertyId.Right: r.Right = l; break;
            case PropertyId.Bottom: r.Bottom = l; break;
            case PropertyId.Left: r.Left = l; break;
            case PropertyId.Width: r.Width = l; break;
            case PropertyId.Height: r.Height = l; break;
            case PropertyId.MinWidth: r.MinWidth = l; break;
            case PropertyId.MinHeight: r.MinHeight = l; break;
            case PropertyId.MaxWidth: r.MaxWidth = l; break;
            case PropertyId.MaxHeight: r.MaxHeight = l; break;
            case PropertyId.MarginTop: r.MarginTop = l; break;
            case PropertyId.MarginRight: r.MarginRight = l; break;
            case PropertyId.MarginBottom: r.MarginBottom = l; break;
            case PropertyId.MarginLeft: r.MarginLeft = l; break;
            case PropertyId.PaddingTop: r.PaddingTop = l; break;
            case PropertyId.PaddingRight: r.PaddingRight = l; break;
            case PropertyId.PaddingBottom: r.PaddingBottom = l; break;
            case PropertyId.PaddingLeft: r.PaddingLeft = l; break;
            case PropertyId.LetterSpacing: r.LetterSpacing = l; break;
            case PropertyId.WordSpacing: r.WordSpacing = l; break;
            default: return false;
        }

        return true;
    }

    // ---- individual properties ----

    private bool ParseDisplay(ComponentValueList v)
    {
        if (!DisplayValue.TryParse(v, out DisplayValue d))
        {
            return false;
        }

        switch (d.Kind)
        {
            case DisplayKind.None:
                _r.Display = DisplayType.None;
                return true;
            case DisplayKind.Contents:
                _r.Display = new DisplayType(OuterDisplay.Contents, InnerDisplay.Flow);
                return true;
            case DisplayKind.Internal:
            {
                InnerDisplay inner;
                switch (d.InternalName)
                {
                    case "table-row-group": inner = InnerDisplay.TableRowGroup; break;
                    case "table-header-group": inner = InnerDisplay.TableHeaderGroup; break;
                    case "table-footer-group": inner = InnerDisplay.TableFooterGroup; break;
                    case "table-row": inner = InnerDisplay.TableRow; break;
                    case "table-cell": inner = InnerDisplay.TableCell; break;
                    case "table-column-group": inner = InnerDisplay.TableColumnGroup; break;
                    case "table-column": inner = InnerDisplay.TableColumn; break;
                    case "table-caption": inner = InnerDisplay.TableCaption; break;
                    default:
                        // ruby-base, ruby-text and friends: boxes are generated like inline ruby; ruby layout is a later feature.
                        _r.Display = new DisplayType(OuterDisplay.Inline, InnerDisplay.Ruby);
                        return true;
                }

                _r.Display = new DisplayType(OuterDisplay.TableInternal, inner);
                return true;
            }

            default:
            {
                if (d.Outside == RecursiveAI.OneHtml.Css.DisplayOutside.RunIn)
                {
                    return false; // run-in is not supported by browsers and was dropped from the specification.
                }

                OuterDisplay outer = d.Outside == RecursiveAI.OneHtml.Css.DisplayOutside.Block ? OuterDisplay.Block : OuterDisplay.Inline;
                InnerDisplay inner = d.Inside switch
                {
                    RecursiveAI.OneHtml.Css.DisplayInside.FlowRoot => InnerDisplay.FlowRoot,
                    RecursiveAI.OneHtml.Css.DisplayInside.Table => InnerDisplay.Table,
                    RecursiveAI.OneHtml.Css.DisplayInside.Flex => InnerDisplay.Flex,
                    RecursiveAI.OneHtml.Css.DisplayInside.Grid => InnerDisplay.Grid,
                    RecursiveAI.OneHtml.Css.DisplayInside.Ruby => InnerDisplay.Ruby,
                    _ => InnerDisplay.Flow,
                };
                // list-item only applies to flow and flow-root inner types (CSS Display 3 §2.5).
                _r.Display = new DisplayType(outer, inner, d.IsListItem && inner is InnerDisplay.Flow or InnerDisplay.FlowRoot);
                return true;
            }
        }
    }

    private bool ParseFontSize(ComponentValueList v)
    {
        // CSS Fonts 4 §2.5: em and % refer to the parent's font size.
        if (!TypedProperties.TryParseFontSize(v, out FontSizeValue? fs) || fs is null)
        {
            return false;
        }

        double px = fs.ResolvePx(in _parentCtx);
        if (double.IsNaN(px))
        {
            return false;
        }

        if (px < 0)
        {
            // A negative literal is invalid, but a math function that resolves below zero is clamped (CSS Values 4 §10.1 range clamping).
            if (!(SinglePart(v, out ComponentValue only) && only.Raw.Contains('(')))
            {
                return false;
            }

            px = 0;
        }

        _r.FontSize = px;
        return true;
    }

    private bool ParseFontWeight(ComponentValueList v)
    {
        if (!TypedProperties.TryParseFontWeight(v, out FontWeightValue? w) || w is null)
        {
            return false;
        }

        _r.FontWeight = w.Resolve(_parent.FontWeight);
        return true;
    }

    private bool ParseFontStyle(ComponentValueList v)
    {
        if (!TypedProperties.TryParseFontStyle(v, out FontStyleValue s))
        {
            return false;
        }

        switch (s.Keyword)
        {
            case "italic":
                _r.FontStyle = FontStyleKind.Italic;
                break;
            case "oblique":
                // CSS Fonts 4 §2.3: oblique 0deg computes to normal.
                _r.FontStyle = s.AngleWritten && s.ObliqueAngleDegrees == 0 ? FontStyleKind.Normal : FontStyleKind.Oblique;
                _r.ObliqueAngle = s.ObliqueAngleDegrees;
                break;
            default:
                _r.FontStyle = FontStyleKind.Normal;
                break;
        }

        return true;
    }

    private bool ParseFontFamily(ComponentValueList v)
    {
        if (!TypedProperties.TryParseFontFamily(v, out FontFamilyValue? f) || f is null || f.Names.Count > 64)
        {
            return false;
        }

        var names = new string[f.Names.Count];
        var generic = new bool[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = f.Names[i];
            generic[i] = f.IsGeneric[i];
        }

        _r.FontFamily = new FontFamilyList(names, generic);
        return true;
    }

    private bool ParseFontStretch(ComponentValueList v)
    {
        if (SinglePart(v, out ComponentValue sp) && IsIdent(sp))
        {
            int k = WordIndex(sp.Raw, StretchWords);
            if (k < 0)
            {
                return false;
            }

            _r.FontStretch = StretchPercents[k];
            return true;
        }

        if (!CssQuantity.TryParse(v, QuantityKinds.Percentage | QuantityKinds.NonNegative, out CssQuantity q))
        {
            return false;
        }

        double p = q.Calc is not null ? q.Calc.Resolve(in _ctx, 100) : q.Value;
        _r.FontStretch = double.IsNaN(p) ? 100 : Math.Max(0, p);
        return true;
    }

    private bool ParseLineHeight(ComponentValueList v)
    {
        // CSS Inline 3 §4.2: normal | <number [0,∞]> | <length-percentage [0,∞]>; a percentage computes to a length from the element's own font size.
        if (!Len(v, LengthAllow.Percent | LengthAllow.Normal | LengthAllow.Number | LengthAllow.NonNegative, out Length l))
        {
            return false;
        }

        if (l.IsPercentage)
        {
            l = new Length(LengthKind.Px, l.Resolve(_r.FontSize));
        }

        _r.LineHeight = l;
        return true;
    }

    private bool ParseVerticalAlign(ComponentValueList v)
    {
        if (Word(v, VerticalAlignWords, out int k))
        {
            _r.VerticalAlign = (VerticalAlignKind)k;
            _r.VerticalAlignOffset = Length.Zero;
            return true;
        }

        if (!Len(v, LengthAllow.Percent, out Length l))
        {
            return false;
        }

        _r.VerticalAlign = VerticalAlignKind.Length;
        _r.VerticalAlignOffset = l;
        return true;
    }

    private bool ParseTextIndent(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 3)
        {
            return false;
        }

        bool hanging = false, eachLine = false, haveLength = false;
        Length length = Length.Zero;
        for (int i = 0; i < n; i++)
        {
            if (IsIdentPart(_parts[i], out ReadOnlySpan<char> t))
            {
                if (t.Equals("hanging", StringComparison.OrdinalIgnoreCase) && !hanging)
                {
                    hanging = true;
                }
                else if (t.Equals("each-line", StringComparison.OrdinalIgnoreCase) && !eachLine)
                {
                    eachLine = true;
                }
                else
                {
                    return false;
                }
            }
            else if (!haveLength && Len(n == 1 ? v : ListOf(_parts[i]), LengthAllow.Percent, out length))
            {
                haveLength = true;
            }
            else
            {
                return false;
            }
        }

        if (!haveLength)
        {
            return false;
        }

        _r.TextIndent = length;
        _r.TextIndentHanging = hanging;
        _r.TextIndentEachLine = eachLine;
        return true;
    }

    private bool ParseAspectRatio(ComponentValueList v)
    {
        // CSS Sizing 4 §5.1: auto || <ratio>, <ratio> = <number [0,∞]> [ / <number [0,∞]> ]?
        int n = SplitParts(v);
        if (n < 1)
        {
            return false;
        }

        bool auto = false;
        bool haveRatio = false;
        double ratioW = 0, ratioH = 0;
        int i = 0;
        while (i < n)
        {
            if (IsIdentPart(_parts[i], out ReadOnlySpan<char> t))
            {
                if (!t.Equals("auto", StringComparison.OrdinalIgnoreCase) || auto)
                {
                    return false;
                }

                auto = true;
                i++;
                continue;
            }

            if (haveRatio || !PartNumber(_parts[i], out double w))
            {
                return false;
            }

            double h = 1;
            i++;
            if (i < n && IsSlash(_parts[i]))
            {
                if (i + 1 >= n || !PartNumber(_parts[i + 1], out h))
                {
                    return false;
                }

                i += 2;
            }

            haveRatio = true;
            ratioW = w;
            ratioH = h;
        }

        if (!auto && !haveRatio)
        {
            return false;
        }

        _r.AspectRatioAuto = auto;
        _r.AspectRatioWidth = ratioW;
        _r.AspectRatioHeight = ratioH;
        return true;
    }

    private static bool PartNumber(ComponentValue part, out double value)
    {
        value = 0;
        if (part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Number)
        {
            return false;
        }

        value = part.Token.NumberValue;
        return value >= 0 && !double.IsInfinity(value);
    }

    private bool ParseBorderWidth(PropertyId id, ComponentValueList v)
    {
        // CSS Backgrounds 3 §4.1: <line-width> = <length [0,∞]> | thin | medium | thick. Chrome's values are 1, 3 and 5 px.
        double px;
        if (SinglePart(v, out ComponentValue bp) && IsIdent(bp))
        {
            ReadOnlySpan<char> t = bp.Raw;
            if (t.Equals("thin", StringComparison.OrdinalIgnoreCase))
            {
                px = 1;
            }
            else if (t.Equals("medium", StringComparison.OrdinalIgnoreCase))
            {
                px = 3;
            }
            else if (t.Equals("thick", StringComparison.OrdinalIgnoreCase))
            {
                px = 5;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (!Len(v, LengthAllow.NonNegative, out Length l))
            {
                return false;
            }

            px = l.Value;
        }

        _rawBorderWidth[id - PropertyId.BorderTopWidth] = px;
        return true;
    }

    private bool ParseColorProperty(PropertyId id, ComponentValueList v)
    {
        if (!CssColor.TryParse(v, out CssColor c))
        {
            return false;
        }

        ColorValue value;
        if (c.IsCurrentColor)
        {
            // CSS Color 4 §4.5: for the `color` property itself currentcolor means inherit.
            if (id == PropertyId.Color)
            {
                value = _hasParent ? _parent.Color : ColorValue.Black;
            }
            else
            {
                value = ColorValue.CurrentColor;
            }
        }
        else
        {
            (double r, double g, double b, double a) = c.ToSrgb();
            value = new ColorValue((float)r, (float)g, (float)b, (float)a);
        }

        switch (id)
        {
            case PropertyId.BorderTopColor: _r.BorderTopColor = value; break;
            case PropertyId.BorderRightColor: _r.BorderRightColor = value; break;
            case PropertyId.BorderBottomColor: _r.BorderBottomColor = value; break;
            case PropertyId.BorderLeftColor: _r.BorderLeftColor = value; break;
            case PropertyId.BackgroundColor: _r.BackgroundColor = value; break;
            default: _r.Color = value; break;
        }

        return true;
    }

    private bool ParseRadius(PropertyId id, ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 2)
        {
            return false;
        }

        const LengthAllow Allow = LengthAllow.Percent | LengthAllow.NonNegative;
        if (!Len(n == 1 ? v : ListOf(_parts[0]), Allow, out Length h))
        {
            return false;
        }

        Length vert = h;
        if (n == 2 && !Len(ListOf(_parts[1]), Allow, out vert))
        {
            return false;
        }

        var radius = new CornerRadius(h, vert);
        switch (id)
        {
            case PropertyId.BorderTopLeftRadius: _r.BorderTopLeftRadius = radius; break;
            case PropertyId.BorderTopRightRadius: _r.BorderTopRightRadius = radius; break;
            case PropertyId.BorderBottomRightRadius: _r.BorderBottomRightRadius = radius; break;
            default: _r.BorderBottomLeftRadius = radius; break;
        }

        return true;
    }

    private bool ParseOverflowClipMargin(ComponentValueList v)
    {
        // CSS Overflow 4 §3.2: <visual-box> || <length [0,∞]>; the box defaults to padding-box.
        int n = SplitParts(v);
        if (n < 1 || n > 2)
        {
            return false;
        }

        bool haveBox = false, haveLength = false;
        var box = ClipMarginBoxKind.PaddingBox;
        double px = 0;
        for (int i = 0; i < n; i++)
        {
            if (IsIdentPart(_parts[i], out ReadOnlySpan<char> t))
            {
                int k = WordIndex(t, ClipMarginWords);
                if (k < 0 || haveBox)
                {
                    return false;
                }

                box = (ClipMarginBoxKind)k;
                haveBox = true;
            }
            else
            {
                if (haveLength || !Len(RecursiveAI.OneHtml.Css.CssTree.ParseComponentValueList(_parts[i].Raw.ToString()).Values, LengthAllow.NonNegative, out Length l) || l.Kind != LengthKind.Px)
                {
                    return false;
                }

                px = l.Value;
                haveLength = true;
            }
        }

        _r.OverflowClipMarginBox = box;
        _r.OverflowClipMarginPx = px;
        return true;
    }

    private bool ParseZIndex(ComponentValueList v)
    {
        if (IsWord(v, "auto"))
        {
            _r.ZIndexAuto = true;
            _r.ZIndex = 0;
            return true;
        }

        if (!CssQuantity.TryParse(v, QuantityKinds.Number | QuantityKinds.IntegerOnly, out CssQuantity q))
        {
            return false;
        }

        double n = q.Calc is not null ? q.Calc.Resolve(in _ctx, 0) : q.Value;
        if (double.IsNaN(n))
        {
            n = 0;
        }

        _r.ZIndexAuto = false;
        _r.ZIndex = (int)Math.Clamp(Math.Round(n), int.MinValue, int.MaxValue);
        return true;
    }

    private bool ParseContent(ComponentValueList v)
    {
        if (IsWord(v, "normal"))
        {
            _r.Content = ContentKind.Normal;
            _r.ContentText = string.Empty;
            return true;
        }

        if (IsWord(v, "none"))
        {
            _r.Content = ContentKind.None;
            _r.ContentText = string.Empty;
            return true;
        }

        int n = SplitParts(v);
        if (n < 1)
        {
            return false;
        }

        // CSS Generated Content 3 §2: [ <content-replacement> | <content-list> ] [ / [ <string> | <counter> ]+ ]?
        // Only strings are expanded here; every other item makes the value `Other`, which generates an empty box until counters and quotes are built.
        bool onlyStrings = true;
        string text = string.Empty;
        int strings = 0;
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.QuotedString)
            {
                string s = p.Token.Value;
                text = strings == 0 ? s : string.Concat(text, s);
                strings++;
            }
            else if (IsSlash(p))
            {
                break;
            }
            else
            {
                onlyStrings = false;
            }
        }

        if (onlyStrings && strings > 0)
        {
            _r.Content = ContentKind.Text;
            _r.ContentText = text;
        }
        else
        {
            _r.Content = ContentKind.Other;
            _r.ContentText = v.Raw.ToString().Trim();
        }

        return true;
    }

    private bool ParseListStyleType(ComponentValueList v)
    {
        if (!SinglePart(v, out ComponentValue p))
        {
            return false;
        }

        if (p.Kind == ComponentValueKind.Function)
        {
            // symbols() and friends: kept as written until the marker support understands them.
            _r.ListStyleType = p.Raw.ToString();
            _r.ListStyleTypeIsString = false;
            return true;
        }

        if (p.Kind != ComponentValueKind.Token)
        {
            return false;
        }

        switch (p.Token.Kind)
        {
            case CssTokenKind.Ident:
                _r.ListStyleType = p.Token.Value;
                _r.ListStyleTypeIsString = false;
                return true;
            case CssTokenKind.QuotedString:
                _r.ListStyleType = p.Token.Value;
                _r.ListStyleTypeIsString = true;
                return true;
            default:
                return false;
        }
    }
}
