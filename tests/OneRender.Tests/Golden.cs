using System.IO;
using System.Runtime.CompilerServices;
using OneImage;
using OneRender.Image;

namespace OneRender.Tests;

/// <summary>
/// Compares rasterized output with PNG files committed under <c>Golden/</c>.
/// A pixel matches when every channel is within <see cref="Tolerance"/> levels of the golden value: the rasterizer is
/// deterministic, and the small allowance only absorbs last-bit differences of floating point math between CPUs.
/// Set <c>ONERENDER_UPDATE_GOLDEN=1</c> to rewrite the files from the current output, then look at them before committing.
/// </summary>
internal static class Golden
{
    public const int Tolerance = 2;

    public static void Check(string name, RgbaImage actual, [CallerFilePath] string sourceFile = "")
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourceFile)!, "Golden");
        string file = System.IO.Path.Combine(dir, name + ".png");
        if (Environment.GetEnvironmentVariable("ONERENDER_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(dir);
            Save(file, actual);
            return;
        }

        Assert.True(File.Exists(file), "Missing golden image " + name + ".png; run with ONERENDER_UPDATE_GOLDEN=1 to create it.");
        OneImage.Image img = OneImage.Image.Load(file);
        Assert.Equal(actual.Width, img.Width);
        Assert.Equal(actual.Height, img.Height);
        Rgba32[] expected = img.ToArray<Rgba32>();
        int worst = 0;
        int bad = 0;
        int first = -1;
        for (int i = 0; i < expected.Length; i++)
        {
            Rgba32 e = expected[i];
            int d = Math.Max(
                Math.Max(Math.Abs(e.R - actual.Pixels[i * 4]), Math.Abs(e.G - actual.Pixels[(i * 4) + 1])),
                Math.Max(Math.Abs(e.B - actual.Pixels[(i * 4) + 2]), Math.Abs(e.A - actual.Pixels[(i * 4) + 3])));
            if (d > Tolerance)
            {
                bad++;
                first = first < 0 ? i : first;
            }

            worst = Math.Max(worst, d);
        }

        Assert.True(bad == 0, name + ": " + bad + " pixels differ by more than " + Tolerance + " levels (worst " + worst + ", first at x=" + (first % actual.Width) + " y=" + (first / actual.Width) + ").");
    }

    public static void Save(string file, RgbaImage img)
    {
        Rgba32[] px = new Rgba32[img.Width * img.Height];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = new Rgba32(img.Pixels[i * 4], img.Pixels[(i * 4) + 1], img.Pixels[(i * 4) + 2], img.Pixels[(i * 4) + 3]);
        }

        OneImage.Image image = OneImage.Image.FromPixels<Rgba32>(px, img.Width, img.Height);
        image.Save(file);
    }

    public static Rgba32 Pixel(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return new Rgba32(img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2], img.Pixels[o + 3]);
    }
}
