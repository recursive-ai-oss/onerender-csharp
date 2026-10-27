using System;
using System.Text;
using OneRender.Image;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a small page whose boxes carry random transforms, perspective, filters, masks, clip paths, blend modes and
/// inline svg, with values from a dictionary of valid, extreme and malformed forms (or raw bytes as a value), and renders it
/// through the whole pipeline including the rasterizer. No exception may escape and the render must finish.
/// </summary>
internal static class EffectsTarget
{
    private static readonly string[] Transforms =
    [
        "none", "rotate(45deg)", "translate(10px,20px)", "scale(2)", "scale(0)", "skew(30deg,10deg)", "matrix(1,0,0,1,5,5)", "matrix3d(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1)",
        "rotateY(80deg)", "rotate3d(1,1,1,1turn)", "perspective(100px)", "perspective(0)", "translateZ(1e9px)", "scale(1e30)", "rotate(1e30deg)", "translate(1e30px,-1e30px)",
        "matrix(0,0,0,0,0,0)", "matrix(nan,0,0,1,0,0)", "scale3d(2,2,2) rotateX(90deg)", "translate(calc(50% + 3px))", "rotate(", "foo(1)", "translate(1px,2px,3px,4px)",
    ];

    private static readonly string[] Filters =
    [
        "none", "blur(2px)", "blur(1e9px)", "blur(-1px)", "brightness(2)", "contrast(0.5)", "grayscale(1)", "hue-rotate(90deg)", "invert(100%)", "opacity(50%)", "saturate(3)", "sepia(1)",
        "drop-shadow(3px 3px 2px red)", "drop-shadow(1e9px 0 1e9px)", "url(#nope)", "blur(1px) blur(1px) blur(1px) blur(1px) blur(1px) blur(1px) blur(1px) blur(1px)", "brightness(1e30)", "blur(", "foo(1)",
    ];

    private static readonly string[] Clips =
    [
        "none", "inset(10px)", "inset(10px round 5px)", "circle(30%)", "circle(50% at 10px 10px)", "ellipse(10px 20px)", "polygon(0 0,100% 0,50% 100%)", "polygon(0 0)", "path('M0 0 L10 10 Z')",
        "path('M0 0 A1e9 1e9 0 1 1 10 10')", "content-box", "border-box circle(40%)", "url(#c)", "url(#none)", "polygon(", "inset(1e30px)", "circle(-1px)", "xywh(0 0 10px 10px)", "rect(0 10px 10px 0)",
    ];

    private static readonly string[] Masks =
    [
        "none", "linear-gradient(red,transparent)", "radial-gradient(circle,#000,transparent)", "url(#m)", "linear-gradient(red,blue),linear-gradient(blue,red)", "linear-gradient(red,blue) 10px 10px/20px 20px no-repeat",
        "linear-gradient(#000,#000) exclude", "linear-gradient(#000,#000) luminance", "conic-gradient(red,blue)", "repeating-linear-gradient(red 0,blue 1px)", "linear-gradient(",
    ];

    private static readonly string[] Blends = ["normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn", "hard-light", "soft-light", "difference", "exclusion", "hue", "saturation", "color", "luminosity", "plus-lighter", "bogus"];

    private static readonly string[] Svgs =
    [
        "<svg width='20' height='20'><rect width='20' height='20' fill='red'/></svg>",
        "<svg viewBox='0 0 10 10' width='30'><circle cx='5' cy='5' r='4'/><path d='M0 0 L10 10'/></svg>",
        "<svg width='99999999' height='99999999'><rect width='1' height='1'/></svg>",
        "<svg width='0' height='0'><defs><clipPath id='c'><circle cx='5' cy='5' r='5'/><rect width='3' height='3'/></clipPath></defs></svg>",
        "<svg width='0' height='0'><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M0 0 L1 0 L0.5 1Z'/></clipPath></svg>",
        "<svg viewBox='0 0 1 1' preserveAspectRatio='xMinYMax slice'><g transform='scale(1e30)'><use href='#x'/></g></svg>",
        "<svg><svg><svg><svg><svg><g><g><g><rect width='5' height='5'/></g></g></g></svg></svg></svg></svg></svg>",
    ];

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _pos;

        public bool More => _pos < _data.Length;

        public int Next(int n) => n <= 0 ? 0 : (_pos < _data.Length ? _data[_pos++] : 0) % n;

        public string Pick(string[] set) => set[Next(set.Length)];

        public string Raw()
        {
            int len = Math.Min(Next(40), _data.Length - _pos);
            string s = Encoding.Latin1.GetString(_data, _pos, len).Replace('"', ' ').Replace('<', ' ').Replace(';', ' ').Replace('}', ' ').Replace('{', ' ');
            _pos += len;
            return s;
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        var r = new Reader(data);
        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><body style='margin:0'>");
        int budget = 24;
        Content(sb, r, 0, ref budget);
        sb.Append("</body>");
        try
        {
            _ = HtmlRender.ToImage(sb.ToString(), new HtmlRenderOptions { Width = 100 + (r.Next(3) * 60), Height = 120 });
        }
        catch (ArgumentException)
        {
        }
        catch (RasterizationException)
        {
            // The typed failure for a drawing that cannot be done (a layer too large, coordinates out of range).
        }
    }

    private static void Style(StringBuilder sb, Reader r)
    {
        sb.Append(" style=\"width:").Append(10 + r.Next(60)).Append("px;height:").Append(10 + r.Next(60)).Append("px;background:#").Append(r.Next(4096).ToString("x3", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        int n = r.Next(6);
        for (int i = 0; i < n; i++)
        {
            switch (r.Next(15))
            {
                case 0: sb.Append("transform:").Append(r.Pick(Transforms)).Append(';'); break;
                case 1: sb.Append("transform-origin:").Append(r.Pick(["center", "0 0", "100% 100%", "1e9px 1e9px", "left top 50px", "bogus"])).Append(';'); break;
                case 2: sb.Append("perspective:").Append(r.Pick(["none", "50px", "0", "1e9px", "-1px"])).Append(";perspective-origin:").Append(r.Pick(["center", "10% 90%"])).Append(';'); break;
                case 3: sb.Append("transform-style:preserve-3d;backface-visibility:").Append(r.Pick(["hidden", "visible"])).Append(';'); break;
                case 4: sb.Append("filter:").Append(r.Pick(Filters)).Append(';'); break;
                case 5: sb.Append("backdrop-filter:").Append(r.Pick(Filters)).Append(';'); break;
                case 6: sb.Append("clip-path:").Append(r.Pick(Clips)).Append(';'); break;
                case 7: sb.Append("mask-image:").Append(r.Pick(Masks)).Append(';'); break;
                case 8: sb.Append("mask:").Append(r.Pick(Masks)).Append(';'); break;
                case 9: sb.Append("mix-blend-mode:").Append(r.Pick(Blends)).Append(';'); break;
                case 10: sb.Append("isolation:isolate;opacity:0.").Append(r.Next(10)).Append(';'); break;
                case 11: sb.Append("background-blend-mode:").Append(r.Pick(Blends)).Append(',').Append(r.Pick(Blends)).Append(";background-image:linear-gradient(red,blue);"); break;
                case 12: sb.Append("translate:").Append(r.Next(30)).Append("px;rotate:").Append(r.Next(90)).Append("deg;scale:1.").Append(r.Next(9)).Append(';'); break;
                case 13: sb.Append(r.Pick(["filter", "clip-path", "mask-image", "transform", "backdrop-filter", "mask-composite", "mask-mode"])).Append(':').Append(r.Raw()).Append(';'); break;
                default: sb.Append("position:").Append(r.Pick(["relative", "absolute", "fixed"])).Append(";top:5px;left:5px;overflow:").Append(r.Pick(["hidden", "auto", "visible"])).Append(';'); break;
            }
        }

        sb.Append('"');
    }

    // Recursion here is the generator's and is bounded by depth; the engine's own handling is what is under test.
    private static void Content(StringBuilder sb, Reader r, int depth, ref int budget)
    {
        int n = 1 + r.Next(3);
        for (int i = 0; i < n && r.More && budget > 0; i++)
        {
            budget--;
            switch (r.Next(6))
            {
                case 0:
                    sb.Append(r.Pick(Svgs));
                    break;
                case 1:
                    sb.Append("hello");
                    break;
                default:
                    sb.Append("<div");
                    Style(sb, r);
                    sb.Append('>');
                    if (depth < 5)
                    {
                        Content(sb, r, depth + 1, ref budget);
                    }

                    sb.Append("</div>");
                    break;
            }
        }
    }

    /// <summary>Seed inputs: random byte strings that reach different features.</summary>
    public static int WriteSeeds(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var rng = new Random(41);
        for (int i = 0; i < 32; i++)
        {
            byte[] b = new byte[64 + rng.Next(400)];
            rng.NextBytes(b);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "seed" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
