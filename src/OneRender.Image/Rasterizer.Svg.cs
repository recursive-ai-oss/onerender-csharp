using System.Globalization;
using System.Numerics;
using System.Text;
using OneImage.Svg;
using OneRender.Painting;
using IntersectionRule = OneImage.Drawing.IntersectionRule;
using Rgba32 = OneImage.Rgba32;

namespace OneRender.Image;

// Inline SVG: the document is rendered by OneImage.Svg at the size it will be drawn and placed like an image.
internal sealed partial class Rasterizer
{
    private static readonly SvgLimits SvgBounds = new()
    {
        MaxDocumentBytes = 2 * 1024 * 1024,
        MaxElements = 20_000,
        MaxDepth = 64,
        MaxInstances = 20_000,
        MaxReferenceDepth = 8,
        MaxLayerDepth = 8,
        MaxPatternTilePixels = 4 * 1024 * 1024,
        MaxPathSegments = 200_000,
        MaxCssBytes = 128 * 1024,
        MaxCssRules = 2_000,
        MaxCssMatchSteps = 2_000_000,
        MaxImageDepth = 1,
        MaxImageBytes = 4 * 1024 * 1024,
        MaxTotalImageBytes = 8 * 1024 * 1024,
        MaxFilterPixels = 4 * 1024 * 1024,
        MaxFilterPrimitives = 32,
        MaxFilterDepth = 2,
        MaxTextCharacters = 20_000,
    };

    private static readonly OneImage.LoadOptions SvgLoad = new()
    {
        Configuration = new OneImage.ImageConfiguration().Add(new SvgFormat(SvgBounds, null, new SvgFontOptions { UseSystemFonts = false })),
        Limits = new OneImage.ImageLimits { MaxPixelsPerFrame = 1L << 24, MaxDimension = 8192 },
    };

    private void DrawSvg(in DisplayItem it)
    {
        string? markup = it.SvgMarkup;
        RectF dst = it.Rect;
        if (markup is null || dst.IsEmpty)
        {
            return;
        }

        float s = ScaleOf(_ctm);
        long pw = (long)Math.Ceiling(dst.Width * s), ph = (long)Math.Ceiling(dst.Height * s);
        if (pw < 1 || ph < 1 || pw > 8192 || ph > 8192 || pw * ph > (1L << 24))
        {
            _diagnostics?.Add(DiagnosticKind.LimitExceeded, "inline svg: the drawn size is larger than the renderer's limit, so it is not drawn");
            return;
        }

        try
        {
            string sized = SvgMarkup.WithSize(markup, dst.Width, dst.Height, it.SvgColor);
            byte[] bytes = Encoding.UTF8.GetBytes(sized);
            OneImage.Image image = OneImage.Image.Load(bytes, SvgLoad).Resize((int)pw, (int)ph, OneImage.Fit.Stretch);
            Rgba32[] px = image.ToArray<Rgba32>();
            byte[] raw = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                raw[i * 4] = px[i].R;
                raw[(i * 4) + 1] = px[i].G;
                raw[(i * 4) + 2] = px[i].B;
                raw[(i * 4) + 3] = px[i].A;
            }

            var img = new RgbaImage((int)pw, (int)ph, raw);
            float kx = dst.Width / pw, ky = dst.Height / ph;
            var toLocal = new Matrix3x2(kx, 0, 0, ky, dst.X, dst.Y);
            OneImage.Drawing.Brush brush = new ImageSamplerBrush(img, default, toLocal * _ctm, true);
            DrawPath(brush, Shapes.Rect(dst), IntersectionRule.NonZero);
        }
        catch (Exception ex) when (ex is OneImage.ImageException or ArgumentException or InvalidOperationException or FormatException)
        {
            _diagnostics?.Add(DiagnosticKind.InvalidValue, "inline svg: the document could not be rendered (" + ex.GetType().Name + ")");
        }
    }
}

/// <summary>Edits the root start tag of an SVG document so that it renders at a given size.</summary>
internal static class SvgMarkup
{
    /// <summary>
    /// Returns the markup with the root's <c>width</c> and <c>height</c> set to the drawn size, a <c>viewBox</c> kept (or made from
    /// the old size) so <c>preserveAspectRatio</c> maps the drawing into the box, an <c>xmlns</c> and a <c>color</c> for <c>currentColor</c>.
    /// Only the first start tag is read, in one pass over at most 64 KiB of attributes.
    /// </summary>
    public static string WithSize(string markup, float width, float height, PaintColor color)
    {
        int open = markup.IndexOf("<svg", StringComparison.Ordinal);
        if (open < 0)
        {
            return markup;
        }

        int i = open + 4;
        int end = -1;
        char quote = '\0';
        for (; i < markup.Length && i < open + 65536; i++)
        {
            char c = markup[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return markup;
        }

        string tag = markup.Substring(open + 4, end - open - 4);
        bool selfClose = tag.EndsWith('/');
        if (selfClose)
        {
            tag = tag.Substring(0, tag.Length - 1);
        }

        var sb = new StringBuilder(tag.Length + 128);
        string? oldW = null, oldH = null;
        bool hasViewBox = false, hasXmlns = false, hasColor = false;
        int p = 0;
        while (p < tag.Length)
        {
            while (p < tag.Length && char.IsWhiteSpace(tag[p]))
            {
                p++;
            }

            int ns = p;
            while (p < tag.Length && tag[p] != '=' && !char.IsWhiteSpace(tag[p]))
            {
                p++;
            }

            if (p == ns)
            {
                break;
            }

            string name = tag.Substring(ns, p - ns);
            while (p < tag.Length && char.IsWhiteSpace(tag[p]))
            {
                p++;
            }

            string? value = null;
            if (p < tag.Length && tag[p] == '=')
            {
                p++;
                while (p < tag.Length && char.IsWhiteSpace(tag[p]))
                {
                    p++;
                }

                if (p < tag.Length && tag[p] is '"' or '\'')
                {
                    char q = tag[p++];
                    int vs = p;
                    while (p < tag.Length && tag[p] != q)
                    {
                        p++;
                    }

                    value = tag.Substring(vs, p - vs);
                    p++;
                }
            }

            if (name == "width")
            {
                oldW = value;
            }
            else if (name == "height")
            {
                oldH = value;
            }
            else
            {
                hasViewBox |= name == "viewBox";
                hasXmlns |= name == "xmlns";
                hasColor |= name == "color";
                sb.Append(' ').Append(name);
                if (value is not null)
                {
                    sb.Append("=\"").Append(value.Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
                }
            }
        }

        if (!hasViewBox && TryNumber(oldW, out double ow) && TryNumber(oldH, out double oh) && ow > 0 && oh > 0)
        {
            sb.Append(" viewBox=\"0 0 ").Append(ow.ToString("0.####", CultureInfo.InvariantCulture)).Append(' ').Append(oh.ToString("0.####", CultureInfo.InvariantCulture)).Append('"');
        }

        if (!hasXmlns)
        {
            sb.Append(" xmlns=\"http://www.w3.org/2000/svg\"");
        }

        if (!hasColor)
        {
            sb.Append(" color=\"rgb(").Append(PaintColor.ToByte(color.R)).Append(',').Append(PaintColor.ToByte(color.G)).Append(',').Append(PaintColor.ToByte(color.B)).Append(")\"");
        }

        sb.Append(" width=\"").Append(width.ToString("0.####", CultureInfo.InvariantCulture)).Append("\" height=\"").Append(height.ToString("0.####", CultureInfo.InvariantCulture)).Append('"');
        return string.Concat(markup.AsSpan(0, open + 4), sb.ToString(), selfClose ? "/" : string.Empty, markup.AsSpan(end));
    }

    private static bool TryNumber(string? s, out double v)
    {
        v = 0;
        if (s is null)
        {
            return false;
        }

        s = s.Trim();
        if (s.EndsWith("px", StringComparison.Ordinal))
        {
            s = s.Substring(0, s.Length - 2);
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
    }
}
