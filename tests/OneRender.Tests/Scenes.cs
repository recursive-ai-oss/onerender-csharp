using System.Numerics;
using OneFont;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

/// <summary>
/// Small display lists that exercise one feature each; shared by the golden-image tests and the allocation tests.
/// Colors and sizes are chosen so the result is easy to judge by eye.
/// </summary>
internal static class Scenes
{
    private static FontFace? s_sans;

    public static FontFace Sans => s_sans ??= LoadSans();

    private static FontFace LoadSans()
    {
        foreach (FontFaceInfo f in DefaultFonts.GetFaces("Liberation Sans"))
        {
            if (f.Weight == 400 && f.Slant == FontSlant.Normal)
            {
                return FontFace.Load(f.ReadAllBytes());
            }
        }

        throw new InvalidOperationException("Liberation Sans Regular is missing.");
    }

    public static ItemId Id(uint n, ushort part = 0) => ItemId.Of(n, part);

    public static PaintColor C(byte r, byte g, byte b, byte a = 255) => PaintColor.FromBytes(r, g, b, a);

    public static GradientStop S(float at, PaintColor c) => new(at, c);

    public static void Backdrop(DisplayList l, uint id, float w, float h)
    {
        // A light checkerboard-free backdrop: plain light gray so translucent effects are visible against white.
        l.FillRect(Id(id), new RectF(0, 0, w, h), C(235, 238, 242));
    }

    public static DisplayList Fills()
    {
        DisplayList l = new();
        l.FillRect(Id(1), new RectF(8, 8, 60, 40), C(200, 40, 40));
        l.FillRoundedRect(Id(2), new RectF(80, 8, 70, 40), new CornerRadii(30, 20, 10, 10, 30, 20, 0, 0), C(30, 90, 200));
        l.FillRoundedRect(Id(3), new RectF(8, 60, 60, 60), CornerRadii.Uniform(30), C(20, 150, 80));
        PathBuffer star = new();
        for (int i = 0; i < 5; i++)
        {
            double a = ((i * 2 * 2 * Math.PI) / 5) - (Math.PI / 2);
            float x = 115 + (float)(32 * Math.Cos(a)), y = 92 + (float)(32 * Math.Sin(a));
            if (i == 0)
            {
                star.MoveTo(x, y);
            }
            else
            {
                star.LineTo(x, y);
            }
        }

        star.Close();
        l.FillPath(Id(4), star, C(240, 170, 20), FillRule.EvenOdd);
        // Translucent overlap shows source-over blending.
        l.FillRect(Id(5), new RectF(130, 90, 40, 40), C(0, 0, 0, 100));
        // Radii larger than the box are scaled down to fit (CSS Backgrounds 3 §5.5).
        l.FillRoundedRect(Id(6), new RectF(160, 8, 30, 40), CornerRadii.Uniform(100), C(120, 40, 160));
        return l;
    }

    public static DisplayList Borders()
    {
        DisplayList l = new();
        BorderStyle[] styles = [BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dotted, BorderStyle.DoubleLine, BorderStyle.Groove, BorderStyle.Ridge, BorderStyle.Inset, BorderStyle.Outset];
        for (int i = 0; i < styles.Length; i++)
        {
            float x = 8 + ((i % 4) * 56), y = 8 + ((i / 4) * 56);
            PaintColor c = C(60, 110, 190);
            BorderSide s = new(8, styles[i], c);
            l.DrawBorder(Id((uint)(i + 1)), new RectF(x, y, 48, 48), CornerRadii.Zero, s, s, s, s);
        }

        // Per-side widths and colors with a rounded outline.
        l.DrawBorder(
            Id(20),
            new RectF(8, 120, 100, 60),
            new CornerRadii(30, 24, 10, 10, 20, 20, 0, 0),
            new BorderSide(10, BorderStyle.Solid, C(200, 40, 40)),
            new BorderSide(4, BorderStyle.Solid, C(40, 160, 60)),
            new BorderSide(10, BorderStyle.Solid, C(40, 80, 200)),
            new BorderSide(16, BorderStyle.Solid, C(230, 180, 20)));
        // Rounded dashed and dotted.
        l.DrawBorder(Id(21), new RectF(120, 120, 50, 60), CornerRadii.Uniform(20), new BorderSide(4, BorderStyle.Dashed, C(0, 0, 0)), new BorderSide(4, BorderStyle.Dashed, C(0, 0, 0)), new BorderSide(4, BorderStyle.Dashed, C(0, 0, 0)), new BorderSide(4, BorderStyle.Dashed, C(0, 0, 0)));
        l.DrawBorder(Id(22), new RectF(180, 120, 44, 60), CornerRadii.Uniform(14), new BorderSide(5, BorderStyle.Dotted, C(160, 30, 120)), new BorderSide(5, BorderStyle.Dotted, C(160, 30, 120)), new BorderSide(5, BorderStyle.Dotted, C(160, 30, 120)), new BorderSide(5, BorderStyle.Dotted, C(160, 30, 120)));
        return l;
    }

    public static DisplayList Gradients()
    {
        DisplayList l = new();
        PaintColor red = C(220, 40, 40), blue = C(40, 60, 220), yellow = C(250, 220, 40), green = C(30, 170, 80);
        // Linear: diagonal, three stops.
        l.FillRect(Id(1), new RectF(8, 8, 70, 50), l.LinearGradient(8, 8, 78, 58, [S(0, red), S(0.5f, yellow), S(1, blue)]));
        // Hard stops make bands.
        l.FillRect(Id(2), new RectF(86, 8, 70, 50), l.LinearGradient(86, 8, 156, 8, [S(0, red), S(0.33f, red), S(0.33f, green), S(0.66f, green), S(0.66f, blue), S(1, blue)]));
        // Transparent stop: premultiplied interpolation keeps red from turning gray.
        l.FillRect(Id(3), new RectF(164, 8, 60, 50), l.LinearGradient(164, 8, 224, 8, [S(0, red), S(1, C(0, 0, 255, 0))]));
        // Repeating linear.
        l.FillRect(Id(4), new RectF(8, 66, 70, 50), l.LinearGradient(8, 66, 28, 86, [S(0, red), S(0.5f, yellow), S(1, red)], repeating: true));
        // Radial ellipse and repeating radial.
        l.FillRect(Id(5), new RectF(86, 66, 70, 50), l.RadialGradient(121, 91, 35, 25, [S(0, yellow), S(0.6f, red), S(1, blue)]));
        l.FillRect(Id(6), new RectF(164, 66, 60, 50), l.RadialGradient(194, 91, 20, 20, [S(0, blue), S(0.5f, C(255, 255, 255)), S(1, blue)], repeating: true));
        // Conic: from the top, clockwise; and a repeating conic with a start angle.
        l.FillRect(Id(7), new RectF(8, 124, 70, 60), l.ConicGradient(43, 154, 0, [S(0, red), S(0.25f, yellow), S(0.5f, green), S(0.75f, blue), S(1, red)]));
        l.FillRoundedRect(Id(8), new RectF(86, 124, 70, 60), CornerRadii.Uniform(30), l.ConicGradient(121, 154, (float)(Math.PI / 4), [S(0, C(0, 0, 0)), S(0.125f, C(255, 255, 255)), S(0.25f, C(0, 0, 0))], repeating: true));
        // A gradient on a path.
        PathBuffer p = new();
        p.MoveTo(194, 124);
        p.LineTo(224, 184);
        p.LineTo(164, 184);
        p.Close();
        l.FillPath(Id(9), p, l.LinearGradient(164, 124, 224, 184, [S(0, green), S(1, blue)]));
        return l;
    }

    public static DisplayList Shadows()
    {
        DisplayList l = new();
        Backdrop(l, 1, 240, 200);
        PaintColor white = C(255, 255, 255);
        // Sharp, blurred, spread, offset, colored, rounded.
        l.DrawBoxShadow(Id(2), new RectF(16, 16, 50, 36), CornerRadii.Zero, C(0, 0, 0, 160), 6, 6, 0, 0, false);
        l.FillRect(Id(3), new RectF(16, 16, 50, 36), white);
        l.DrawBoxShadow(Id(4), new RectF(96, 16, 50, 36), CornerRadii.Uniform(8), C(0, 0, 0, 200), 0, 4, 12, 0, false);
        l.FillRoundedRect(Id(5), new RectF(96, 16, 50, 36), CornerRadii.Uniform(8), white);
        l.DrawBoxShadow(Id(6), new RectF(176, 16, 40, 36), CornerRadii.Uniform(18), C(200, 30, 30, 220), 0, 0, 10, 6, false);
        l.FillRoundedRect(Id(7), new RectF(176, 16, 40, 36), CornerRadii.Uniform(18), white);
        // Inset: sharp offset, blurred, spread.
        l.FillRect(Id(8), new RectF(16, 90, 60, 60), white);
        l.DrawBoxShadow(Id(9), new RectF(16, 90, 60, 60), CornerRadii.Zero, C(0, 0, 0, 200), 5, 5, 0, 0, true);
        l.FillRoundedRect(Id(10), new RectF(96, 90, 60, 60), CornerRadii.Uniform(14), white);
        l.DrawBoxShadow(Id(11), new RectF(96, 90, 60, 60), CornerRadii.Uniform(14), C(0, 0, 80, 220), 0, 0, 14, 2, true);
        l.FillRoundedRect(Id(12), new RectF(176, 90, 50, 60), CornerRadii.Uniform(10), white);
        l.DrawBoxShadow(Id(13), new RectF(176, 90, 50, 60), CornerRadii.Uniform(10), C(0, 100, 0, 220), -4, 6, 8, 4, true);
        return l;
    }

    public static DisplayList Clips()
    {
        DisplayList l = new();
        Backdrop(l, 1, 220, 130);
        // Rectangle clip.
        l.PushClipRect(Id(2), new RectF(8, 8, 50, 50));
        l.FillRect(Id(3), new RectF(0, 0, 100, 100), C(200, 40, 40));
        l.PopClip();
        // Rounded clip, with a nested rectangular clip.
        l.PushClipRoundedRect(Id(4), new RectF(70, 8, 60, 50), CornerRadii.Uniform(25));
        l.PushClipRect(Id(5), new RectF(100, 0, 100, 100));
        l.FillRect(Id(6), new RectF(60, 0, 100, 100), C(30, 90, 200));
        l.PopClip();
        l.FillRect(Id(7), new RectF(70, 40, 20, 30), C(255, 255, 255, 120));
        l.PopClip();
        // Path clip (a triangle) with even-odd rule.
        PathBuffer tri = new();
        tri.MoveTo(165, 8);
        tri.LineTo(210, 58);
        tri.LineTo(140, 58);
        tri.Close();
        l.PushClipPath(Id(8), tri, FillRule.EvenOdd);
        l.FillRect(Id(9), new RectF(130, 0, 100, 100), C(20, 150, 80));
        l.PopClip();
        // A clip under rotation is a rotated rectangle.
        l.PushTransform(Id(10), Matrix3x2.CreateRotation(MathF.PI / 6, new Vector2(50, 95)));
        l.PushClipRect(Id(11), new RectF(20, 75, 60, 40));
        l.FillRect(Id(12), new RectF(0, 60, 120, 80), C(240, 170, 20));
        l.PopClip();
        l.PopTransform();
        return l;
    }

    public static DisplayList Transforms()
    {
        DisplayList l = new();
        Backdrop(l, 1, 220, 130);
        l.PushTransform(Id(2), Matrix3x2.CreateTranslation(10, 10));
        l.FillRect(Id(3), new RectF(0, 0, 40, 30), C(200, 40, 40));
        l.PopTransform();
        l.PushTransform(Id(4), Matrix3x2.CreateScale(1.5f, 0.75f) * Matrix3x2.CreateTranslation(60, 10));
        l.FillRoundedRect(Id(5), new RectF(0, 0, 40, 40), CornerRadii.Uniform(10), C(30, 90, 200));
        l.PopTransform();
        // Rotation nested in a translation, with a border and text-free content inside.
        l.PushTransform(Id(6), Matrix3x2.CreateTranslation(150, 40));
        l.PushTransform(Id(7), Matrix3x2.CreateRotation(MathF.PI / 5));
        l.DrawBorder(Id(8), new RectF(-25, -15, 50, 30), CornerRadii.Uniform(6), new BorderSide(4, BorderStyle.Solid, C(20, 150, 80)), new BorderSide(4, BorderStyle.Solid, C(20, 150, 80)), new BorderSide(4, BorderStyle.Solid, C(20, 150, 80)), new BorderSide(4, BorderStyle.Solid, C(20, 150, 80)));
        l.PopTransform();
        l.PopTransform();
        // A 3D matrix flattened to 2D: a rotation about z with a z translation that is ignored.
        Matrix4x4 m3 = Matrix4x4.CreateRotationZ(-MathF.PI / 8) * Matrix4x4.CreateTranslation(40, 95, 50);
        l.PushTransform3D(Id(9), m3);
        l.FillRect(Id(10), new RectF(0, 0, 60, 20), C(120, 40, 160));
        l.PopTransform();
        return l;
    }

    public static DisplayList Groups()
    {
        DisplayList l = new();
        Backdrop(l, 1, 240, 190);
        // Two overlapping rectangles in a half-transparent group: the overlap does not show through.
        l.PushGroup(Id(2), 0.5f);
        l.FillRect(Id(3), new RectF(8, 8, 50, 40), C(200, 40, 40));
        l.FillRect(Id(4), new RectF(32, 24, 50, 40), C(30, 90, 200));
        l.PopGroup();
        // The same rectangles without a group: the overlap shows the top one's color.
        l.FillRect(Id(5), new RectF(100, 8, 50, 40), C(200, 40, 40, 128));
        l.FillRect(Id(6), new RectF(124, 24, 50, 40), C(30, 90, 200, 128));
        // Blend modes over a fixed backdrop.
        BlendMode[] modes = [BlendMode.Multiply, BlendMode.Screen, BlendMode.Difference, BlendMode.Overlay, BlendMode.Luminosity, BlendMode.PlusLighter];
        for (int i = 0; i < modes.Length; i++)
        {
            float x = 8 + (i * 38);
            l.FillRect(Id(10 + (uint)i), new RectF(x, 86, 32, 40), C(250, 200, 60));
            l.PushGroup(Id(20 + (uint)i), 1f, modes[i]);
            l.FillRect(Id(30 + (uint)i), new RectF(x + 8, 96, 32, 40), C(60, 120, 220));
            l.PopGroup();
        }

        // Nested groups with a clip: opacity multiplies.
        l.PushClipRoundedRect(Id(40), new RectF(8, 144, 100, 38), CornerRadii.Uniform(12));
        l.PushGroup(Id(41), 0.6f);
        l.PushGroup(Id(42), 0.5f);
        l.FillRect(Id(43), new RectF(0, 130, 200, 80), C(20, 20, 20));
        l.PopGroup();
        l.PopGroup();
        l.PopClip();
        // isolation: isolate with a blend inside does not reach the backdrop.
        l.PushGroup(Id(50), 1f);
        l.FillRect(Id(51), new RectF(130, 146, 40, 34), C(250, 200, 60));
        l.PushGroup(Id(52), 1f, BlendMode.Multiply);
        l.FillRect(Id(53), new RectF(150, 156, 40, 34), C(60, 120, 220));
        l.PopGroup();
        l.PopGroup();
        return l;
    }

    public static DisplayList Text()
    {
        DisplayList l = new();
        l.FillRect(Id(1), new RectF(0, 0, 240, 70), C(255, 255, 255));
        Run(l, 2, "Hello, World", 24, 8, 30, C(20, 20, 40), false);
        Run(l, 3, "Kerning AVAT fi fl", 16, 8, 56, C(180, 40, 40), false);
        // Rotated text through a transform.
        l.PushTransform(Id(4), Matrix3x2.CreateRotation(-MathF.PI / 12, new Vector2(8, 120)));
        Run(l, 5, "Rotated", 20, 8, 120, C(30, 90, 200), false);
        l.PopTransform();
        return l;
    }

    public static void Run(DisplayList l, uint id, string text, float size, float x, float y, PaintColor color, bool rtl)
    {
        FontFace face = Sans;
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun(text, face, rtl);
        int n = 0;
        foreach (ShapedCluster c in clusters)
        {
            n += c.Glyphs.Length;
        }

        int[] ids = new int[n];
        float[] xs = new float[n], ys = new float[n];
        int[] cl = new int[n];
        float scale = size / face.UnitsPerEm;
        float pen = 0;
        int k = 0;
        foreach (ShapedCluster c in clusters)
        {
            foreach (ShapedGlyph g in c.Glyphs)
            {
                ids[k] = g.GlyphId;
                xs[k] = pen + (g.XOffset * scale);
                ys[k] = -g.YOffset * scale;
                cl[k] = c.Start;
                pen += g.XAdvance * scale;
                k++;
            }
        }

        l.DrawGlyphRun(Id(id), face, size, x, y, color, ids, xs, ys, cl, text, rtl);
    }

    /// <summary>A 4 by 4 test image: a red-to-blue ramp with a transparent corner.</summary>
    public static RgbaImage TestImage()
    {
        byte[] px = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int o = ((y * 4) + x) * 4;
                px[o] = (byte)(255 - (x * 85));
                px[o + 1] = (byte)(y * 85);
                px[o + 2] = (byte)(x * 85);
                px[o + 3] = x == 3 && y == 0 ? (byte)0 : (byte)255;
            }
        }

        return new RgbaImage(4, 4, px);
    }

    public static DisplayList Images()
    {
        DisplayList l = new();
        Backdrop(l, 1, 200, 120);
        l.DrawImage(Id(2), new ImageHandle(7), default, new RectF(8, 8, 80, 80), ImageSampling.Smooth);
        l.DrawImage(Id(3), new ImageHandle(7), default, new RectF(100, 8, 80, 80), ImageSampling.Pixelated);
        // A source rectangle selects the lower-left 2 by 2 pixels.
        l.DrawImage(Id(4), new ImageHandle(7), new RectF(0, 2, 2, 2), new RectF(8, 96, 20, 20), ImageSampling.Pixelated);
        // An unknown handle is skipped.
        l.DrawImage(Id(5), new ImageHandle(99), default, new RectF(40, 96, 20, 20));
        return l;
    }

    public sealed class OneImageResolver : IImageResolver
    {
        public RgbaImage? Resolve(ImageHandle handle) => handle.Value == 7 ? TestImage() : null;
    }

    public static DisplayList Structure()
    {
        DisplayList l = new();
        l.BeginTag(Id(1), TagRole.Document);
        l.BeginTag(Id(2), TagRole.Heading1);
        Run(l, 3, "Title", 20, 8, 28, C(0, 0, 0), false);
        l.EndTag();
        l.BeginTag(Id(4), TagRole.Link, linkTarget: "https://example.test/", language: "en");
        l.FillRect(Id(5), new RectF(8, 40, 40, 10), C(0, 0, 200));
        l.EndTag();
        l.BeginTag(Id(6), TagRole.Figure, altText: "A red square");
        l.FillRect(Id(7), new RectF(60, 40, 10, 10), C(200, 0, 0));
        l.EndTag();
        l.EndTag();
        return l;
    }
}
