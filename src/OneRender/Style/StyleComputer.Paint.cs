using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

// Parsing of the paint properties: backgrounds, gradients, border-image, shadows, outline, text decoration, object-fit and friends.
internal sealed partial class StyleComputer
{
    private static readonly string[] RepeatWords = ["repeat", "no-repeat", "space", "round"];
    private static readonly string[] BoxWords = ["border-box", "padding-box", "content-box", "text", "border-area"];
    private static readonly string[] AttachmentWords = ["scroll", "fixed", "local"];
    private static readonly string[] OutlineStyleWords = ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset", "auto"];
    private static readonly string[] DecorationStyleWords = ["solid", "double", "dotted", "dashed", "wavy"];
    private static readonly string[] ObjectFitWords = ["fill", "contain", "cover", "none", "scale-down"];
    private static readonly string[] BorderRepeatWords = ["stretch", "repeat", "round", "space"];
    private static readonly string[] RenderingWords = ["auto", "smooth", "high-quality", "crisp-edges", "pixelated", "-webkit-optimize-contrast"];

    private bool ApplyPaint(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.BackgroundImage:
                return Layers(v, p => TryImage(p, out ImageSpec i) ? i : null, out ImageSpec[] images) && Bg(images: images);
            case PropertyId.BackgroundRepeat:
                return LayerSegments(v, ParseRepeatPair, out (BackgroundRepeatKind, BackgroundRepeatKind)[] repeats) && Bg(repeats: repeats);
            case PropertyId.BackgroundPositionX:
                return LayerSegments(v, (ComponentValue[] s, out PositionAxis a) => ParseAxis(s, true, out a), out PositionAxis[] xs) && Bg(positionsX: xs);
            case PropertyId.BackgroundPositionY:
                return LayerSegments(v, (ComponentValue[] s, out PositionAxis a) => ParseAxis(s, false, out a), out PositionAxis[] ys) && Bg(positionsY: ys);
            case PropertyId.BackgroundSize:
                return LayerSegments(v, ParseSizePair, out BackgroundSizeSpec[] sizes) && Bg(sizes: sizes);
            case PropertyId.BackgroundOrigin:
                return LayerSegments(v, (ComponentValue[] s, out BackgroundBoxKind k) => ParseBox(s, false, out k), out BackgroundBoxKind[] origins) && Bg(origins: origins);
            case PropertyId.BackgroundClip:
                return LayerSegments(v, (ComponentValue[] s, out BackgroundBoxKind k) => ParseBox(s, true, out k), out BackgroundBoxKind[] clips) && Bg(clips: clips);
            case PropertyId.BackgroundAttachment:
                return LayerSegments(v, ParseAttachment, out BackgroundAttachmentKind[] attachments) && Bg(attachments: attachments);
            case PropertyId.BorderImageSource:
                if (SinglePart(v, out ComponentValue sp) && TryImage(sp, out ImageSpec src))
                {
                    _r.BorderImage = _r.BorderImage.With(source: src);
                    return true;
                }

                return false;
            case PropertyId.BorderImageSlice: return ParseBorderImageSlice(v);
            case PropertyId.BorderImageWidth: return ParseBorderImageSides(v, true);
            case PropertyId.BorderImageOutset: return ParseBorderImageSides(v, false);
            case PropertyId.BorderImageRepeat: return ParseBorderImageRepeat(v);
            case PropertyId.BoxShadow: return ParseShadows(v, true, out ShadowList bs) && Assign(ref _r, bs, true);
            case PropertyId.TextShadow: return ParseShadows(v, false, out ShadowList ts) && Assign(ref _r, ts, false);
            case PropertyId.OutlineWidth:
                if (ParseLineWidth(v, out double ow))
                {
                    _r.OutlineWidth = ow;
                    return true;
                }

                return false;
            case PropertyId.OutlineStyle:
                if (Word(v, OutlineStyleWords, out int os) && os != 1)
                {
                    _r.OutlineAuto = os == 10;
                    _r.OutlineStyle = os == 10 ? BorderStyleKind.None : (BorderStyleKind)os;
                    return true;
                }

                return false;
            case PropertyId.OutlineColor:
                if (IsWord(v, "invert"))
                {
                    _r.OutlineColor = ColorValue.CurrentColor;
                    return true;
                }

                return TryColorList(v, out ColorValue oc) && SetColor(ref _r, id, oc);
            case PropertyId.OutlineOffset:
                if (Len(v, LengthAllow.Length, out Length off))
                {
                    _r.OutlineOffset = off.Value;
                    return true;
                }

                return false;
            case PropertyId.TextDecorationLine: return ParseDecorationLine(v);
            case PropertyId.TextDecorationStyle:
                if (Word(v, DecorationStyleWords, out int ds))
                {
                    _r.TextDecorationStyle = (TextDecorationStyleKind)ds;
                    return true;
                }

                return false;
            case PropertyId.TextDecorationColor:
                return TryColorList(v, out ColorValue dc) && SetColor(ref _r, id, dc);
            case PropertyId.TextDecorationThickness:
                if (IsWord(v, "auto"))
                {
                    _r.TextDecorationThickness = Length.Auto;
                    return true;
                }

                if (IsWord(v, "from-font"))
                {
                    _r.TextDecorationThickness = Length.Normal;
                    return true;
                }

                if (Len(v, LengthAllow.Percent | LengthAllow.NonNegative, out Length th))
                {
                    _r.TextDecorationThickness = th;
                    return true;
                }

                return false;
            case PropertyId.TextUnderlineOffset:
                if (IsWord(v, "auto"))
                {
                    _r.TextUnderlineOffset = Length.Auto;
                    return true;
                }

                if (Len(v, LengthAllow.Percent, out Length uo))
                {
                    _r.TextUnderlineOffset = uo;
                    return true;
                }

                return false;
            case PropertyId.TextUnderlinePosition:
                return ParseUnderlinePosition(v);
            case PropertyId.ObjectFit:
                if (Word(v, ObjectFitWords, out int of))
                {
                    _r.ObjectFit = (ObjectFitKind)of;
                    return true;
                }

                return false;
            case PropertyId.ObjectPosition:
            {
                int n = SplitParts(v);
                if (n < 1 || !ParsePosition(_parts.AsSpan(0, n), out PositionSpec pos))
                {
                    return false;
                }

                _r.ObjectPosition = pos;
                return true;
            }

            case PropertyId.ImageRendering:
                if (Word(v, RenderingWords, out int ir))
                {
                    _r.ImageRendering = ir switch { 0 => ImageRenderingKind.Auto, 1 or 2 => ImageRenderingKind.Smooth, 3 or 5 => ImageRenderingKind.CrispEdges, _ => ImageRenderingKind.Pixelated };
                    return true;
                }

                return false;
            case PropertyId.ListStyleImage:
                if (SinglePart(v, out ComponentValue lp) && TryImage(lp, out ImageSpec li))
                {
                    _r.ListStyleImage = li;
                    return true;
                }

                return false;
            default:
                return ApplyEffects(id, v);
        }
    }

    private static bool Assign(ref ComputedValues r, ShadowList list, bool box)
    {
        if (box)
        {
            r.BoxShadow = list;
        }
        else
        {
            r.TextShadow = list;
        }

        return true;
    }

    private static bool SetColor(ref ComputedValues r, PropertyId id, ColorValue c)
    {
        if (id == PropertyId.OutlineColor)
        {
            r.OutlineColor = c;
        }
        else
        {
            r.TextDecorationColor = c;
        }

        return true;
    }

    private bool Bg(
        ImageSpec[]? images = null,
        (BackgroundRepeatKind X, BackgroundRepeatKind Y)[]? repeats = null,
        PositionAxis[]? positionsX = null,
        PositionAxis[]? positionsY = null,
        BackgroundSizeSpec[]? sizes = null,
        BackgroundBoxKind[]? origins = null,
        BackgroundBoxKind[]? clips = null,
        BackgroundAttachmentKind[]? attachments = null)
    {
        _r.Background = _r.Background.With(images, repeats, positionsX, positionsY, sizes, origins, clips, attachments);
        return true;
    }

    private static void CopyPaintProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.BackgroundImage: d.Background = d.Background.With(images: s.Background.Images); break;
            case PropertyId.BackgroundRepeat: d.Background = d.Background.With(repeats: s.Background.Repeats); break;
            case PropertyId.BackgroundPositionX: d.Background = d.Background.With(positionsX: s.Background.PositionsX); break;
            case PropertyId.BackgroundPositionY: d.Background = d.Background.With(positionsY: s.Background.PositionsY); break;
            case PropertyId.BackgroundSize: d.Background = d.Background.With(sizes: s.Background.Sizes); break;
            case PropertyId.BackgroundOrigin: d.Background = d.Background.With(origins: s.Background.Origins); break;
            case PropertyId.BackgroundClip: d.Background = d.Background.With(clips: s.Background.Clips); break;
            case PropertyId.BackgroundAttachment: d.Background = d.Background.With(attachments: s.Background.Attachments); break;
            case PropertyId.BorderImageSource: d.BorderImage = d.BorderImage.With(source: s.BorderImage.Source); break;
            case PropertyId.BorderImageSlice: d.BorderImage = d.BorderImage.With(slice: s.BorderImage.Slice, fill: s.BorderImage.Fill); break;
            case PropertyId.BorderImageWidth: d.BorderImage = d.BorderImage.With(width: s.BorderImage.Width); break;
            case PropertyId.BorderImageOutset: d.BorderImage = d.BorderImage.With(outset: s.BorderImage.Outset); break;
            case PropertyId.BorderImageRepeat: d.BorderImage = d.BorderImage.With(rx: s.BorderImage.RepeatX, ry: s.BorderImage.RepeatY); break;
            case PropertyId.BoxShadow: d.BoxShadow = s.BoxShadow; break;
            case PropertyId.TextShadow: d.TextShadow = s.TextShadow; break;
            case PropertyId.OutlineWidth: d.OutlineWidth = s.OutlineWidth; break;
            case PropertyId.OutlineStyle: d.OutlineStyle = s.OutlineStyle; d.OutlineAuto = s.OutlineAuto; break;
            case PropertyId.OutlineColor: d.OutlineColor = s.OutlineColor; break;
            case PropertyId.OutlineOffset: d.OutlineOffset = s.OutlineOffset; break;
            case PropertyId.TextDecorationLine: d.TextDecorationLine = s.TextDecorationLine; break;
            case PropertyId.TextDecorationStyle: d.TextDecorationStyle = s.TextDecorationStyle; break;
            case PropertyId.TextDecorationColor: d.TextDecorationColor = s.TextDecorationColor; break;
            case PropertyId.TextDecorationThickness: d.TextDecorationThickness = s.TextDecorationThickness; break;
            case PropertyId.TextUnderlineOffset: d.TextUnderlineOffset = s.TextUnderlineOffset; break;
            case PropertyId.TextUnderlinePosition: d.TextUnderlinePosition = s.TextUnderlinePosition; break;
            case PropertyId.ObjectFit: d.ObjectFit = s.ObjectFit; break;
            case PropertyId.ObjectPosition: d.ObjectPosition = s.ObjectPosition; break;
            case PropertyId.ImageRendering: d.ImageRendering = s.ImageRendering; break;
            case PropertyId.ListStyleImage: d.ListStyleImage = s.ListStyleImage; break;
        }
    }

    // ---- lists ----

    private delegate bool SegmentParser<T>(ComponentValue[] segment, out T result);

    /// <summary>Splits at top-level commas into segments of non-whitespace component values. Empty segments make the value invalid.</summary>
    private static bool SplitCommas(ComponentValueList v, out List<ComponentValue[]> segments)
    {
        segments = [];
        var cur = new List<ComponentValue>();
        foreach (ComponentValue cv in v)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Comma)
            {
                if (cur.Count == 0)
                {
                    return false;
                }

                segments.Add(cur.ToArray());
                cur.Clear();
                continue;
            }

            cur.Add(cv);
            if (segments.Count > 64 || cur.Count > MaxParts)
            {
                return false;
            }
        }

        if (cur.Count == 0)
        {
            return false;
        }

        segments.Add(cur.ToArray());
        return true;
    }

    private static bool Layers(ComponentValueList v, Func<ComponentValue, ImageSpec?> parse, out ImageSpec[] result)
    {
        result = [];
        if (!SplitCommas(v, out List<ComponentValue[]> segs))
        {
            return false;
        }

        result = new ImageSpec[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i].Length != 1 || parse(segs[i][0]) is not { } image)
            {
                return false;
            }

            result[i] = image;
        }

        return true;
    }

    private static bool LayerSegments<T>(ComponentValueList v, SegmentParser<T> parse, out T[] result)
    {
        result = [];
        if (!SplitCommas(v, out List<ComponentValue[]> segs))
        {
            return false;
        }

        result = new T[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            if (!parse(segs[i], out result[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ---- small value parsers ----

    // The environment colors are parsed in: light-dark() picks by the element's used color scheme (CSS Color 5 §4).
    private static readonly ColorParseOptions DarkColorOptions = new() { DarkScheme = true };

    private ColorParseOptions ColorOptions => _r.DarkScheme ? DarkColorOptions : ColorParseOptions.Default;

    // A parsed color as a computed one. A system color keyword takes its value under the element's used scheme (CSS Color Adjust 1 §2.2).
    private ColorValue ToColorValue(in CssColor c)
    {
        if (c.SystemName is { } system && SystemColors.TryResolve(system, _r.DarkScheme, out ColorValue resolved))
        {
            return resolved;
        }

        (double r, double g, double b, double a) = c.ToSrgb();
        return new ColorValue((float)r, (float)g, (float)b, (float)a);
    }

    private bool TryColorPart(ComponentValue p, out ColorValue color)
    {
        color = default;
        if (!CssColor.TryParse(ListOf(p), out CssColor c, ColorOptions))
        {
            return false;
        }

        color = c.IsCurrentColor ? ColorValue.CurrentColor : ToColorValue(in c);
        return true;
    }

    private bool TryColorList(ComponentValueList v, out ColorValue color)
    {
        color = default;
        return SinglePart(v, out ComponentValue p) && TryColorPart(p, out color);
    }

    private bool TryLengthPart(ComponentValue p, LengthAllow allow, out Length l) => Len(ListOf(p), allow, out l);

    private static bool TryAnglePart(ComponentValue p, out double radians)
    {
        radians = 0;
        if (p.Kind != ComponentValueKind.Token)
        {
            return false;
        }

        CssToken t = p.Token;
        if (t.Kind == CssTokenKind.Number && t.NumberValue == 0)
        {
            return true;
        }

        if (t.Kind != CssTokenKind.Dimension)
        {
            return false;
        }

        double n = t.NumberValue;
        string unit = t.Value;
        if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
        {
            radians = n * Math.PI / 180;
        }
        else if (unit.Equals("grad", StringComparison.OrdinalIgnoreCase))
        {
            radians = n * Math.PI / 200;
        }
        else if (unit.Equals("rad", StringComparison.OrdinalIgnoreCase))
        {
            radians = n;
        }
        else if (unit.Equals("turn", StringComparison.OrdinalIgnoreCase))
        {
            radians = n * 2 * Math.PI;
        }
        else
        {
            return false;
        }

        return double.IsFinite(radians);
    }

    private static bool Ident(ComponentValue p, string word) => IsIdent(p) && p.Raw.Equals(word, StringComparison.OrdinalIgnoreCase);

    // ---- images and gradients ----

    private bool TryImage(ComponentValue p, out ImageSpec image)
    {
        image = ImageSpec.None;
        if (p.Kind == ComponentValueKind.Token)
        {
            if (p.Token.Kind == CssTokenKind.Url)
            {
                image = ImageSpec.FromUrl(p.Token.Value);
                return true;
            }

            return Ident(p, "none");
        }

        if (p.Kind != ComponentValueKind.Function)
        {
            return false;
        }

        if (p.IsFunctionNamed("url"))
        {
            foreach (ComponentValue c in p.Children)
            {
                if (c.IsWhitespace)
                {
                    continue;
                }

                if (c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.QuotedString)
                {
                    image = ImageSpec.FromUrl(c.Token.Value);
                    return true;
                }

                return false;
            }

            return false;
        }

        string name = p.Token.Value;
        GradientKind kind;
        bool repeating = name.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase);
        string baseName = repeating ? name.Substring(10) : name;
        if (baseName.Equals("linear-gradient", StringComparison.OrdinalIgnoreCase))
        {
            kind = GradientKind.Linear;
        }
        else if (baseName.Equals("radial-gradient", StringComparison.OrdinalIgnoreCase))
        {
            kind = GradientKind.Radial;
        }
        else if (baseName.Equals("conic-gradient", StringComparison.OrdinalIgnoreCase))
        {
            kind = GradientKind.Conic;
        }
        else
        {
            return false;
        }

        if (!ParseGradient(p.Children, kind, repeating, out GradientSpec? g))
        {
            return false;
        }

        image = ImageSpec.FromGradient(g!);
        return true;
    }

    private bool ParseGradient(ComponentValueList args, GradientKind kind, bool repeating, out GradientSpec? gradient)
    {
        gradient = null;
        if (!SplitCommas(args, out List<ComponentValue[]> segs))
        {
            return false;
        }

        int first = 0;
        double angle = 0;
        bool corner = false;
        int cx = 0, cy = 0;
        bool circle = false;
        RadialExtent extent = RadialExtent.FarthestCorner;
        Length rx = Length.Zero, ry = Length.Zero;
        PositionAxis centerX = CenterAxis, centerY = CenterAxis;
        ComponentValue[] head = segs[0];
        switch (kind)
        {
            case GradientKind.Linear:
                if (head.Length == 1 && TryAnglePart(head[0], out double a))
                {
                    angle = a;
                    first = 1;
                }
                else if (Ident(head[0], "to"))
                {
                    if (!ParseLinearSide(head, out angle, out corner, out cx, out cy))
                    {
                        return false;
                    }

                    first = 1;
                }

                break;
            case GradientKind.Radial:
                if (IsRadialConfig(head))
                {
                    if (!ParseRadialConfig(head, out circle, out extent, out rx, out ry, out centerX, out centerY))
                    {
                        return false;
                    }

                    first = 1;
                }

                break;
            default:
                if (Ident(head[0], "from") || Ident(head[0], "at"))
                {
                    if (!ParseConicConfig(head, out angle, out centerX, out centerY))
                    {
                        return false;
                    }

                    first = 1;
                }

                break;
        }

        var stops = new List<GradientStopSpec>();
        bool lastHint = true;
        for (int i = first; i < segs.Count; i++)
        {
            if (!ParseStop(segs[i], kind == GradientKind.Conic, stops, lastHint, out lastHint))
            {
                return false;
            }
        }

        if (lastHint || stops.Count < 2)
        {
            return false;
        }

        gradient = new GradientSpec(kind, repeating, stops.ToArray())
        {
            Angle = angle,
            CornerKeyword = corner,
            CornerX = cx,
            CornerY = cy,
            Circle = circle,
            Extent = extent,
            RadiusX = rx,
            RadiusY = ry,
            CenterX = centerX,
            CenterY = centerY,
        };
        return true;
    }

    // CSS Images 3 §3.1.1: `to <side-or-corner>`.
    private static bool ParseLinearSide(ComponentValue[] head, out double angle, out bool corner, out int cx, out int cy)
    {
        angle = 0;
        corner = false;
        cx = cy = 0;
        if (head.Length is < 2 or > 3)
        {
            return false;
        }

        for (int i = 1; i < head.Length; i++)
        {
            if (Ident(head[i], "left"))
            {
                if (cx != 0)
                {
                    return false;
                }

                cx = -1;
            }
            else if (Ident(head[i], "right"))
            {
                if (cx != 0)
                {
                    return false;
                }

                cx = 1;
            }
            else if (Ident(head[i], "top"))
            {
                if (cy != 0)
                {
                    return false;
                }

                cy = -1;
            }
            else if (Ident(head[i], "bottom"))
            {
                if (cy != 0)
                {
                    return false;
                }

                cy = 1;
            }
            else
            {
                return false;
            }
        }

        if (cx != 0 && cy != 0)
        {
            corner = true;
            return head.Length == 3;
        }

        if (head.Length != 2)
        {
            return false;
        }

        angle = cy == -1 ? 0 : cy == 1 ? Math.PI : cx == 1 ? Math.PI / 2 : 3 * Math.PI / 2;
        return true;
    }

    private bool IsRadialConfig(ComponentValue[] head)
    {
        if (TryColorPart(head[0], out _))
        {
            return false;
        }

        return true;
    }

    private bool ParseRadialConfig(ComponentValue[] head, out bool circle, out RadialExtent extent, out Length rx, out Length ry, out PositionAxis cx, out PositionAxis cy)
    {
        circle = false;
        extent = RadialExtent.FarthestCorner;
        rx = ry = Length.Zero;
        cx = cy = CenterAxis;
        bool haveShape = false, ellipse = false, haveExtent = false;
        int nLen = 0;
        Length l0 = Length.Zero, l1 = Length.Zero;
        int i = 0;
        for (; i < head.Length; i++)
        {
            ComponentValue p = head[i];
            if (Ident(p, "at"))
            {
                break;
            }

            if (Ident(p, "circle"))
            {
                if (haveShape)
                {
                    return false;
                }

                haveShape = circle = true;
            }
            else if (Ident(p, "ellipse"))
            {
                if (haveShape)
                {
                    return false;
                }

                haveShape = ellipse = true;
            }
            else if (IsIdent(p))
            {
                int k = WordIndex(p.Raw, ["farthest-corner", "closest-corner", "farthest-side", "closest-side"]);
                if (k < 0 || haveExtent || nLen > 0)
                {
                    return false;
                }

                haveExtent = true;
                extent = k switch { 0 => RadialExtent.FarthestCorner, 1 => RadialExtent.ClosestCorner, 2 => RadialExtent.FarthestSide, _ => RadialExtent.ClosestSide };
            }
            else
            {
                if (haveExtent || nLen == 2 || !TryLengthPart(p, LengthAllow.Percent | LengthAllow.NonNegative, out Length l))
                {
                    return false;
                }

                if (nLen++ == 0)
                {
                    l0 = l;
                }
                else
                {
                    l1 = l;
                }
            }
        }

        if (nLen > 0)
        {
            extent = RadialExtent.Explicit;
            if (nLen == 1)
            {
                // A single length makes a circle; a percentage is not allowed for a circle's radius (Images 3 §3.2).
                if (ellipse || l0.IsPercentage)
                {
                    return false;
                }

                circle = true;
                rx = ry = l0;
            }
            else
            {
                if (circle)
                {
                    return false;
                }

                rx = l0;
                ry = l1;
            }
        }

        if (i < head.Length)
        {
            // `at <position>`
            if (!ParsePosition(head.AsSpan(i + 1), out PositionSpec pos))
            {
                return false;
            }

            cx = pos.X;
            cy = pos.Y;
        }

        return true;
    }

    private bool ParseConicConfig(ComponentValue[] head, out double angle, out PositionAxis cx, out PositionAxis cy)
    {
        angle = 0;
        cx = cy = CenterAxis;
        int i = 0;
        if (Ident(head[0], "from"))
        {
            if (head.Length < 2 || !TryAnglePart(head[1], out angle))
            {
                return false;
            }

            i = 2;
        }

        if (i < head.Length)
        {
            if (!Ident(head[i], "at") || !ParsePosition(head.AsSpan(i + 1), out PositionSpec pos))
            {
                return false;
            }

            cx = pos.X;
            cy = pos.Y;
        }

        return true;
    }

    // CSS Images 3 §3.4: <color> <length-percentage>{0,2} makes one stop, or two of the same color; a lone position is a hint.
    private bool ParseStop(ComponentValue[] seg, bool conic, List<GradientStopSpec> stops, bool afterHint, out bool isHint)
    {
        isHint = false;
        ColorValue color = default;
        bool haveColor = false;
        Length p0 = Length.Auto, p1 = Length.Auto;
        int n = 0;
        foreach (ComponentValue p in seg)
        {
            if (!haveColor && TryColorPart(p, out ColorValue c))
            {
                haveColor = true;
                color = c;
                continue;
            }

            Length l;
            if (conic && p.Kind == ComponentValueKind.Token && p.Token.Kind is CssTokenKind.Dimension or CssTokenKind.Number && TryAnglePart(p, out double rad))
            {
                l = new Length(LengthKind.Number, rad);
            }
            else if (!TryLengthPart(p, LengthAllow.Percent, out l))
            {
                return false;
            }

            if (n == 2)
            {
                return false;
            }

            if (n++ == 0)
            {
                p0 = l;
            }
            else
            {
                p1 = l;
            }
        }

        if (!haveColor)
        {
            if (n != 1 || afterHint)
            {
                return false;
            }

            stops.Add(new GradientStopSpec(default, p0, true));
            isHint = true;
            return true;
        }

        stops.Add(new GradientStopSpec(color, p0, false));
        if (n == 2)
        {
            stops.Add(new GradientStopSpec(color, p1, false));
        }

        return true;
    }

    // ---- positions ----

    // One axis of background-position: [ <keyword> ]? [ <length-percentage> ]? as in Backgrounds 4 §2.1.
    private bool ParseAxis(ComponentValue[] seg, bool horizontal, out PositionAxis axis)
    {
        axis = PositionAxis.Start;
        if (seg.Length is < 1 or > 2)
        {
            return false;
        }

        PosKw a = ClassifyPos(seg[0], out Length la);
        if ((byte)a == 255)
        {
            return false;
        }

        PosKw start = horizontal ? PosKw.Left : PosKw.Top, end = horizontal ? PosKw.Right : PosKw.Bottom;
        if (seg.Length == 1)
        {
            if (a == PosKw.Length)
            {
                axis = new PositionAxis(la, false);
                return true;
            }

            if (a != start && a != end && a != PosKw.Center)
            {
                return false;
            }

            axis = AxisOf(a, default, false);
            return true;
        }

        PosKw b = ClassifyPos(seg[1], out Length lb);
        if (b != PosKw.Length || (a != start && a != end))
        {
            return false;
        }

        axis = AxisOf(a, lb, true);
        return true;
    }

    private enum PosKw : byte
    {
        Length,
        Left,
        Right,
        Top,
        Bottom,
        Center,
    }

    private PosKw ClassifyPos(ComponentValue p, out Length l)
    {
        l = Length.Zero;
        if (IsIdent(p))
        {
            ReadOnlySpan<char> t = p.Raw;
            if (t.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                return PosKw.Left;
            }

            if (t.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                return PosKw.Right;
            }

            if (t.Equals("top", StringComparison.OrdinalIgnoreCase))
            {
                return PosKw.Top;
            }

            if (t.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            {
                return PosKw.Bottom;
            }

            if (t.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                return PosKw.Center;
            }

            return (PosKw)255;
        }

        return TryLengthPart(p, LengthAllow.Percent, out l) ? PosKw.Length : (PosKw)255;
    }

    private static readonly PositionAxis CenterAxis = new(Length.FromPercent(50), false);

    private bool ParsePosition(ReadOnlySpan<ComponentValue> p, out PositionSpec result)
    {
        result = PositionSpec.Center;
        int n = p.Length;
        if (n is < 1 or > 4)
        {
            return false;
        }

        PosKw a = ClassifyPos(p[0], out Length la);
        if ((byte)a == 255)
        {
            return false;
        }

        if (n == 1)
        {
            switch (a)
            {
                case PosKw.Length: result = new PositionSpec(new PositionAxis(la, false), CenterAxis); return true;
                case PosKw.Left: result = new PositionSpec(Edge(false), CenterAxis); return true;
                case PosKw.Right: result = new PositionSpec(Edge(true), CenterAxis); return true;
                case PosKw.Top: result = new PositionSpec(CenterAxis, Edge(false)); return true;
                case PosKw.Bottom: result = new PositionSpec(CenterAxis, Edge(true)); return true;
                default: result = PositionSpec.Center; return true;
            }
        }

        PosKw b = ClassifyPos(p[1], out Length lb);
        if ((byte)b == 255)
        {
            return false;
        }

        if (n == 2)
        {
            bool aVertical = a is PosKw.Top or PosKw.Bottom;
            bool bHorizontal = b is PosKw.Left or PosKw.Right;
            if (aVertical || bHorizontal)
            {
                // y x order: both must be keywords or center.
                if (a == PosKw.Length || b == PosKw.Length || a is PosKw.Left or PosKw.Right || b is PosKw.Top or PosKw.Bottom)
                {
                    return false;
                }

                result = new PositionSpec(AxisOf(b, default, false), AxisOf(a, default, false));
                return true;
            }

            if (a is PosKw.Left or PosKw.Right or PosKw.Center or PosKw.Length && b is PosKw.Top or PosKw.Bottom or PosKw.Center or PosKw.Length)
            {
                result = new PositionSpec(AxisOf(a, la, false), AxisOf(b, lb, false));
                return true;
            }

            return false;
        }

        // Three or four values: <edge> [<offset>]? <edge> [<offset>]?
        if (a is PosKw.Length)
        {
            return false;
        }

        int i = 1;
        Length offA = Length.Zero;
        bool hasA = false;
        if (b == PosKw.Length)
        {
            offA = lb;
            hasA = true;
            i = 2;
        }

        if (i >= n)
        {
            return false;
        }

        PosKw c = ClassifyPos(p[i], out _);
        if ((byte)c == 255 || c == PosKw.Length)
        {
            return false;
        }

        Length offC = Length.Zero;
        bool hasC = false;
        if (i + 1 < n)
        {
            if (i + 2 != n || ClassifyPos(p[i + 1], out offC) != PosKw.Length)
            {
                return false;
            }

            hasC = true;
        }
        else if (i + 1 != n)
        {
            return false;
        }

        if ((hasA && a == PosKw.Center) || (hasC && c == PosKw.Center))
        {
            return false;
        }

        bool aIsX = a is PosKw.Left or PosKw.Right;
        bool aIsY = a is PosKw.Top or PosKw.Bottom;
        bool cIsX = c is PosKw.Left or PosKw.Right;
        bool cIsY = c is PosKw.Top or PosKw.Bottom;
        if ((aIsX && cIsX) || (aIsY && cIsY))
        {
            return false;
        }

        PositionAxis axA = AxisOf(a, hasA ? offA : Length.Zero, hasA);
        PositionAxis axC = AxisOf(c, hasC ? offC : Length.Zero, hasC);
        if (aIsX || cIsY)
        {
            result = new PositionSpec(axA, axC);
        }
        else
        {
            result = new PositionSpec(axC, axA);
        }

        return true;
    }

    private static PositionAxis Edge(bool end) => new(Length.FromPercent(end ? 100 : 0), false);

    private static PositionAxis AxisOf(PosKw k, Length l, bool withOffset = true) => k switch
    {
        PosKw.Length => new PositionAxis(l, false),
        PosKw.Left or PosKw.Top => new PositionAxis(withOffset ? l : Length.FromPercent(0), false),
        PosKw.Right or PosKw.Bottom => withOffset ? new PositionAxis(l, true) : new PositionAxis(Length.FromPercent(100), false),
        _ => CenterAxis,
    };

    // ---- background longhands ----

    private bool ParseRepeatPair(ComponentValue[] seg, out (BackgroundRepeatKind, BackgroundRepeatKind) r)
    {
        r = (BackgroundRepeatKind.Repeat, BackgroundRepeatKind.Repeat);
        if (seg.Length == 1 && IsIdent(seg[0]))
        {
            if (Ident(seg[0], "repeat-x"))
            {
                r = (BackgroundRepeatKind.Repeat, BackgroundRepeatKind.NoRepeat);
                return true;
            }

            if (Ident(seg[0], "repeat-y"))
            {
                r = (BackgroundRepeatKind.NoRepeat, BackgroundRepeatKind.Repeat);
                return true;
            }
        }

        if (seg.Length is < 1 or > 2)
        {
            return false;
        }

        int x = IsIdent(seg[0]) ? WordIndex(seg[0].Raw, RepeatWords) : -1;
        int y = seg.Length == 2 ? (IsIdent(seg[1]) ? WordIndex(seg[1].Raw, RepeatWords) : -1) : x;
        if (x < 0 || y < 0)
        {
            return false;
        }

        r = ((BackgroundRepeatKind)x, (BackgroundRepeatKind)y);
        return true;
    }

    private bool ParseSizePair(ComponentValue[] seg, out BackgroundSizeSpec r)
    {
        r = BackgroundSizeSpec.Auto;
        if (seg.Length == 1 && Ident(seg[0], "cover"))
        {
            r = new BackgroundSizeSpec(BackgroundSizeKind.Cover, Length.Auto, Length.Auto);
            return true;
        }

        if (seg.Length == 1 && Ident(seg[0], "contain"))
        {
            r = new BackgroundSizeSpec(BackgroundSizeKind.Contain, Length.Auto, Length.Auto);
            return true;
        }

        if (seg.Length is < 1 or > 2)
        {
            return false;
        }

        if (!SizeValue(seg[0], out Length w))
        {
            return false;
        }

        Length h = Length.Auto;
        if (seg.Length == 2 && !SizeValue(seg[1], out h))
        {
            return false;
        }

        r = new BackgroundSizeSpec(BackgroundSizeKind.Explicit, w, h);
        return true;
    }

    private bool SizeValue(ComponentValue p, out Length l)
    {
        if (Ident(p, "auto"))
        {
            l = Length.Auto;
            return true;
        }

        return TryLengthPart(p, LengthAllow.Percent | LengthAllow.NonNegative, out l);
    }

    private static bool ParseBox(ComponentValue[] seg, bool allowText, out BackgroundBoxKind k)
    {
        k = default;
        if (seg.Length != 1 || !IsIdent(seg[0]))
        {
            return false;
        }

        int i = WordIndex(seg[0].Raw, BoxWords);
        if (i < 0 || (i >= 3 && !allowText))
        {
            return false;
        }

        k = (BackgroundBoxKind)i;
        return true;
    }

    private static bool ParseAttachment(ComponentValue[] seg, out BackgroundAttachmentKind k)
    {
        k = default;
        if (seg.Length != 1 || !IsIdent(seg[0]))
        {
            return false;
        }

        int i = WordIndex(seg[0].Raw, AttachmentWords);
        if (i < 0)
        {
            return false;
        }

        k = (BackgroundAttachmentKind)i;
        return true;
    }

    // ---- border-image ----

    private static Length[] Expand4(List<Length> v) => v.Count switch
    {
        1 => [v[0], v[0], v[0], v[0]],
        2 => [v[0], v[1], v[0], v[1]],
        3 => [v[0], v[1], v[2], v[1]],
        _ => [v[0], v[1], v[2], v[3]],
    };

    private bool ParseBorderImageSlice(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n < 1)
        {
            return false;
        }

        bool fill = false;
        var values = new List<Length>();
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (Ident(p, "fill"))
            {
                if (fill)
                {
                    return false;
                }

                fill = true;
                continue;
            }

            if (!TryLengthPart(p, LengthAllow.Percent | LengthAllow.Number | LengthAllow.NonNegative, out Length l) || l.Kind is not (LengthKind.Number or LengthKind.Percent))
            {
                return false;
            }

            values.Add(l);
        }

        if (values.Count is < 1 or > 4)
        {
            return false;
        }

        _r.BorderImage = _r.BorderImage.With(slice: Expand4(values), fill: fill);
        return true;
    }

    private bool ParseBorderImageSides(ComponentValueList v, bool width)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 4)
        {
            return false;
        }

        var values = new List<Length>();
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            Length l;
            if (width && Ident(p, "auto"))
            {
                l = Length.Auto;
            }
            else if (!TryLengthPart(p, (width ? LengthAllow.Percent : LengthAllow.Length) | LengthAllow.Number | LengthAllow.NonNegative, out l))
            {
                return false;
            }

            values.Add(l);
        }

        Length[] four = Expand4(values);
        _r.BorderImage = width ? _r.BorderImage.With(width: four) : _r.BorderImage.With(outset: four);
        return true;
    }

    private bool ParseBorderImageRepeat(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 2 || !IsIdent(_parts[0]))
        {
            return false;
        }

        int x = WordIndex(_parts[0].Raw, BorderRepeatWords);
        int y = n == 2 ? (IsIdent(_parts[1]) ? WordIndex(_parts[1].Raw, BorderRepeatWords) : -1) : x;
        if (x < 0 || y < 0)
        {
            return false;
        }

        _r.BorderImage = _r.BorderImage.With(rx: (BorderImageRepeatKind)x, ry: (BorderImageRepeatKind)y);
        return true;
    }

    // ---- shadows ----

    private bool ParseShadows(ComponentValueList v, bool box, out ShadowList list)
    {
        list = ShadowList.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        if (!SplitCommas(v, out List<ComponentValue[]> segs))
        {
            return false;
        }

        var items = new ShadowSpec[segs.Count];
        for (int i = 0; i < segs.Count; i++)
        {
            ColorValue color = ColorValue.CurrentColor;
            bool haveColor = false, inset = false;
            double[] nums = new double[4];
            int count = 0;
            foreach (ComponentValue p in segs[i])
            {
                if (box && Ident(p, "inset"))
                {
                    if (inset)
                    {
                        return false;
                    }

                    inset = true;
                    continue;
                }

                if (!haveColor && TryColorPart(p, out ColorValue c))
                {
                    haveColor = true;
                    color = c;
                    continue;
                }

                if (count == (box ? 4 : 3) || !TryLengthPart(p, LengthAllow.Length, out Length l))
                {
                    return false;
                }

                nums[count++] = l.Value;
            }

            if (count < 2 || (count >= 3 && nums[2] < 0))
            {
                return false;
            }

            items[i] = new ShadowSpec(color, nums[0], nums[1], count >= 3 ? nums[2] : 0, count == 4 ? nums[3] : 0, inset);
        }

        list = new ShadowList(items);
        return true;
    }

    // ---- outline, decoration ----

    private bool ParseLineWidth(ComponentValueList v, out double px)
    {
        px = 0;
        if (SinglePart(v, out ComponentValue bp) && IsIdent(bp))
        {
            if (Ident(bp, "thin"))
            {
                px = 1;
            }
            else if (Ident(bp, "medium"))
            {
                px = 3;
            }
            else if (Ident(bp, "thick"))
            {
                px = 5;
            }
            else
            {
                return false;
            }

            return true;
        }

        if (!Len(v, LengthAllow.NonNegative, out Length l))
        {
            return false;
        }

        px = l.Value;
        return true;
    }

    private bool ParseDecorationLine(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n < 1)
        {
            return false;
        }

        if (n == 1 && (Ident(_parts[0], "none") || Ident(_parts[0], "spelling-error") || Ident(_parts[0], "grammar-error")))
        {
            _r.TextDecorationLine = TextDecorationLines.None;
            return true;
        }

        TextDecorationLines lines = TextDecorationLines.None;
        for (int i = 0; i < n; i++)
        {
            TextDecorationLines one;
            if (Ident(_parts[i], "underline"))
            {
                one = TextDecorationLines.Underline;
            }
            else if (Ident(_parts[i], "overline"))
            {
                one = TextDecorationLines.Overline;
            }
            else if (Ident(_parts[i], "line-through"))
            {
                one = TextDecorationLines.LineThrough;
            }
            else if (Ident(_parts[i], "blink"))
            {
                continue;
            }
            else
            {
                return false;
            }

            if ((lines & one) != 0)
            {
                return false;
            }

            lines |= one;
        }

        _r.TextDecorationLine = lines;
        return true;
    }

    private bool ParseUnderlinePosition(ComponentValueList v)
    {
        int n = SplitParts(v);
        if (n is < 1 or > 2)
        {
            return false;
        }

        TextUnderlinePositionKind k = TextUnderlinePositionKind.Auto;
        for (int i = 0; i < n; i++)
        {
            if (Ident(_parts[i], "auto"))
            {
                if (n != 1)
                {
                    return false;
                }
            }
            else if (Ident(_parts[i], "from-font"))
            {
                if (n != 1)
                {
                    return false;
                }

                k = TextUnderlinePositionKind.FromFont;
            }
            else if (Ident(_parts[i], "under"))
            {
                k = TextUnderlinePositionKind.Under;
            }
            else if (!Ident(_parts[i], "left") && !Ident(_parts[i], "right"))
            {
                return false;
            }
        }

        _r.TextUnderlinePosition = k;
        return true;
    }
}
