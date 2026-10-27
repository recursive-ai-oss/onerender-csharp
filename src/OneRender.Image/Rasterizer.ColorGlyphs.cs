using System.Numerics;
using OneFont;
using OneRender.Painting;
using OneRender.Text;
using Brush = OneImage.Drawing.Brush;
using IntersectionRule = OneImage.Drawing.IntersectionRule;

namespace OneRender.Image;

internal sealed partial class Rasterizer
{
    // Decoded color bitmaps by face and glyph; null records a glyph the font has no usable bitmap for.
    private readonly Dictionary<(FontFace Face, int Glyph), RgbaImage?> _colorGlyphs = [];

    // A run in a color bitmap font (CBDT/CBLC, as Noto Color Emoji): each glyph is its PNG, scaled from the strike's pixels per em to
    // the run's font size and placed by the glyph's bearings. The text color does not apply; the bitmap carries its own colors.
    private void DrawColorBitmapGlyphs(in DisplayItem it)
    {
        GlyphRunView run = it.GlyphRun;
        if (run.Blur > 0f)
        {
            return; // a blurred shadow of a color bitmap is not drawn
        }

        FontFace face = run.Face;
        ReadOnlySpan<int> ids = run.GlyphIds;
        ReadOnlySpan<float> xs = run.X, ys = run.Y;
        for (int i = 0; i < ids.Length; i++)
        {
            if (!ColorBitmapGlyphs.TryGet(face, ids[i], out ColorBitmapGlyph g))
            {
                continue;
            }

            if (!_colorGlyphs.TryGetValue((face, ids[i]), out RgbaImage? img))
            {
                img = ColorGlyphBitmaps.Decode(g.Png.Span);
                _colorGlyphs[(face, ids[i])] = img;
            }

            if (img is null)
            {
                continue;
            }

            float k = run.FontSize / g.Ppem;
            float left = run.OriginX + xs[i] + (g.BearingX * k);
            float top = run.OriginY + ys[i] - (g.BearingY * k);
            // The PNG may be a different size than the metrics say; the metrics decide the box, the pixels fill it.
            var dst = new RectF(left, top, g.Width * k, g.Height * k);
            Matrix3x2 srcToLocal = new(dst.Width / img.Width, 0, 0, dst.Height / img.Height, dst.X, dst.Y);
            Brush brush = new ImageSamplerBrush(img, default, srcToLocal * _ctm, smooth: true);
            DrawPath(brush, Shapes.Rect(dst), IntersectionRule.NonZero);
        }
    }
}
