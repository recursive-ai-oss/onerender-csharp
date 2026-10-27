using System.Text;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal static partial class ShorthandFallbacks
{
    private static readonly PropertyId[] MaskLonghands =
    [
        PropertyId.MaskImage, PropertyId.MaskMode, PropertyId.MaskRepeat, PropertyId.MaskPosition,
        PropertyId.MaskSize, PropertyId.MaskOrigin, PropertyId.MaskClip, PropertyId.MaskComposite,
    ];

    private static readonly string[] MaskRepeatWords = ["repeat-x", "repeat-y", "repeat", "no-repeat", "space", "round"];
    private static readonly string[] MaskBoxWords = ["border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box", "margin-box"];
    private static readonly string[] MaskCompositeWords = ["add", "subtract", "intersect", "exclude"];
    private static readonly string[] MaskModeWords = ["alpha", "luminance", "match-source"];
    private static readonly string[] MaskPositionWords = ["left", "right", "top", "bottom", "center"];

    private static bool In(ReadOnlySpan<char> w, string[] words)
    {
        foreach (string s in words)
        {
            if (w.Equals(s, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSlash(ComponentValue p) => p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.Delim && p.Raw.Length == 1 && p.Raw[0] == '/';

    private static bool IsLengthLike(ComponentValue p) =>
        (p.Kind == ComponentValueKind.Token && p.Token.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension)
        || p.IsFunctionNamed("calc") || p.IsFunctionNamed("min") || p.IsFunctionNamed("max") || p.IsFunctionNamed("clamp");

    // Masking 1 §6.10:
    // <mask-layer># = <mask-reference> || <position> [ / <bg-size> ]? || <repeat-style> || <geometry-box> || [ <geometry-box> | no-clip ] || <compositing-operator> || <masking-mode>
    private static bool ExpandMask(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        var cols = new StringBuilder[8];
        for (int i = 0; i < 8; i++)
        {
            cols[i] = new StringBuilder();
        }

        var layer = new List<ComponentValue>();
        int layers = 0;
        bool ok = true;

        void Flush()
        {
            if (layers >= 16 || !MaskLayer(layer, cols, layers))
            {
                ok = false;
            }

            layers++;
            layer.Clear();
        }

        foreach (ComponentValue cv in value)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Comma)
            {
                Flush();
                if (!ok)
                {
                    return false;
                }

                continue;
            }

            layer.Add(cv);
            if (layer.Count > 24)
            {
                return false;
            }
        }

        Flush();
        if (!ok)
        {
            return false;
        }

        for (int i = 0; i < 8; i++)
        {
            output.Add((MaskLonghands[i], Text(cols[i].ToString())));
        }

        return true;
    }

    private static bool MaskLayer(List<ComponentValue> parts, StringBuilder[] cols, int index)
    {
        if (parts.Count == 0)
        {
            return false;
        }

        string image = "none", mode = "match-source", composite = "add";
        var repeat = new List<string>();
        var pos = new List<string>();
        var size = new List<string>();
        var boxes = new List<string>();
        bool slash = false, haveImage = false, haveMode = false, haveComposite = false;
        foreach (ComponentValue p in parts)
        {
            string raw = p.Raw.ToString();
            if (IsSlash(p))
            {
                if (slash || pos.Count == 0)
                {
                    return false;
                }

                slash = true;
                continue;
            }

            if (p.Kind == ComponentValueKind.Token && p.Token.Kind == CssTokenKind.Ident)
            {
                ReadOnlySpan<char> w = p.Raw;
                if (w.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    if (haveImage)
                    {
                        return false;
                    }

                    haveImage = true;
                }
                else if (slash && (w.Equals("auto", StringComparison.OrdinalIgnoreCase) || w.Equals("cover", StringComparison.OrdinalIgnoreCase) || w.Equals("contain", StringComparison.OrdinalIgnoreCase)))
                {
                    size.Add(raw);
                }
                else if (In(w, MaskRepeatWords))
                {
                    repeat.Add(raw);
                }
                else if (In(w, MaskBoxWords) || w.Equals("no-clip", StringComparison.OrdinalIgnoreCase))
                {
                    boxes.Add(raw);
                }
                else if (In(w, MaskCompositeWords))
                {
                    if (haveComposite)
                    {
                        return false;
                    }

                    composite = raw;
                    haveComposite = true;
                }
                else if (In(w, MaskModeWords))
                {
                    if (haveMode)
                    {
                        return false;
                    }

                    mode = raw;
                    haveMode = true;
                }
                else if (!slash && In(w, MaskPositionWords))
                {
                    pos.Add(raw);
                }
                else
                {
                    return false;
                }
            }
            else if (IsLengthLike(p))
            {
                (slash ? size : pos).Add(raw);
            }
            else
            {
                if (haveImage)
                {
                    return false;
                }

                image = raw; // url(), a gradient or another image function: the longhand parser judges it
                haveImage = true;
            }
        }

        if (pos.Count > 4 || size.Count > 2 || repeat.Count > 2 || boxes.Count > 2)
        {
            return false;
        }

        string origin = "border-box", clip = "border-box";
        if (boxes.Count == 1)
        {
            if (boxes[0].Equals("no-clip", StringComparison.OrdinalIgnoreCase))
            {
                clip = "no-clip";
            }
            else
            {
                origin = clip = boxes[0];
            }
        }
        else if (boxes.Count == 2)
        {
            origin = boxes[0];
            clip = boxes[1];
        }

        string sep = index > 0 ? ", " : string.Empty;
        cols[0].Append(sep).Append(image);
        cols[1].Append(sep).Append(mode);
        cols[2].Append(sep).Append(repeat.Count == 0 ? "repeat" : string.Join(' ', repeat));
        cols[3].Append(sep).Append(pos.Count == 0 ? "0% 0%" : string.Join(' ', pos));
        cols[4].Append(sep).Append(size.Count == 0 ? "auto" : string.Join(' ', size));
        cols[5].Append(sep).Append(origin);
        cols[6].Append(sep).Append(clip);
        cols[7].Append(sep).Append(composite);
        return true;
    }
}
