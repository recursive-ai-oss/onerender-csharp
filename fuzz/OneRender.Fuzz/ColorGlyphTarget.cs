using System;
using System.IO;
using OneRender.Text;

namespace OneRender.Fuzz;

/// <summary>
/// Hostile CBLC and CBDT tables through the color bitmap glyph reader. Input layout: a u16 glyph id, a u16 length, that many bytes of
/// <c>CBLC</c>, then the rest as <c>CBDT</c>. Nothing may escape, and a returned bitmap must lie inside the data table with positive sizes.
/// </summary>
internal static class ColorGlyphTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        int glyph = (data[0] << 8) | data[1];
        int length = Math.Min((data[2] << 8) | data[3], data.Length - 4);
        byte[] cblc = data.Slice(4, length).ToArray();
        byte[] cbdt = data[(4 + length)..].ToArray();
        if (ColorBitmapGlyphs.TryGet(cblc, cbdt, glyph, out ColorBitmapGlyph g))
        {
            if (g.Png.Length == 0 || g.Width <= 0 || g.Height <= 0 || g.Ppem <= 0 || g.Png.Length > cbdt.Length)
            {
                throw new InvalidOperationException("a bitmap glyph with impossible metrics");
            }
        }
    }

    /// <summary>Seeds: the real tables of a color bitmap font (a few glyphs' worth of data) for the glyph ids around the first strike.</summary>
    public static int WriteSeeds(string outDir, string fontFile)
    {
        Directory.CreateDirectory(outDir);
        OneFont.FontFace face = OneFont.FontFace.Load(File.ReadAllBytes(fontFile));
        byte[] cblc = face.GetTable("CBLC").ToArray();
        byte[] cbdt = face.GetTable("CBDT").ToArray();
        int n = 0;
        foreach (int glyph in new[] { 0, 1, 2, 100, 1596, 2022, 4000 })
        {
            using var ms = new MemoryStream();
            ms.WriteByte((byte)(glyph >> 8));
            ms.WriteByte((byte)glyph);
            ms.WriteByte((byte)(cblc.Length >> 8));
            ms.WriteByte((byte)cblc.Length);
            ms.Write(cblc, 0, Math.Min(cblc.Length, 0xFFFF));
            ms.Write(cbdt, 0, Math.Min(cbdt.Length, 200_000));
            File.WriteAllBytes(Path.Combine(outDir, "cbdt-" + n++), ms.ToArray());
        }

        return 0;
    }
}
