using System.Numerics;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

/// <summary>Random edit sequences: what the damage says changed must cover every pixel that really changed, and a patched list must equal a rebuilt one.</summary>
public class DisplayListPropertyTests
{
    private sealed class Spec
    {
        public float Tx, Ty, Opacity = 1f;
        public bool Group, Clip, Transform;
        public readonly List<(uint Id, float X, float Y, float W, float H, byte R, byte G, byte B, byte Kind)> Items = [];
    }

    private static List<Spec> RandomScene(Random rng)
    {
        List<Spec> scene = [];
        uint id = 1;
        int blocks = rng.Next(1, 5);
        for (int b = 0; b < blocks; b++)
        {
            Spec s = new()
            {
                Group = rng.Next(2) == 0,
                Clip = rng.Next(3) == 0,
                Transform = rng.Next(2) == 0,
                Tx = rng.Next(-8, 9),
                Ty = rng.Next(-8, 9),
                Opacity = rng.Next(1, 5) / 4f,
            };
            int n = rng.Next(1, 5);
            for (int i = 0; i < n; i++)
            {
                s.Items.Add((id++, rng.Next(0, 40), rng.Next(0, 40), rng.Next(2, 24), rng.Next(2, 24), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(4)));
            }

            scene.Add(s);
        }

        return scene;
    }

    // Block ids live far above item ids so they never collide.
    private static DisplayList Build(List<Spec> scene)
    {
        DisplayList l = new();
        for (int b = 0; b < scene.Count; b++)
        {
            Spec s = scene[b];
            uint bid = 1000000u + ((uint)b * 10);
            if (s.Transform)
            {
                l.PushTransform(ItemId.Of(bid), Matrix3x2.CreateTranslation(s.Tx, s.Ty));
            }

            if (s.Clip)
            {
                l.PushClipRoundedRect(ItemId.Of(bid + 1), new RectF(10, 10, 40, 36), CornerRadii.Uniform(8));
            }

            if (s.Group)
            {
                l.PushGroup(ItemId.Of(bid + 2), s.Opacity);
            }

            foreach ((uint id, float x, float y, float w, float h, byte r, byte g, byte bl, byte kind) in s.Items)
            {
                PaintColor c = PaintColor.FromBytes(r, g, bl);
                RectF rect = new(x, y, w, h);
                switch (kind)
                {
                    case 0: l.FillRect(ItemId.Of(id), rect, c); break;
                    case 1: l.FillRoundedRect(ItemId.Of(id), rect, CornerRadii.Uniform(5), c); break;
                    case 2:
                        l.DrawBorder(ItemId.Of(id), rect, CornerRadii.Zero, new BorderSide(2, BorderStyle.Solid, c), new BorderSide(2, BorderStyle.Solid, c), new BorderSide(2, BorderStyle.Solid, c), new BorderSide(2, BorderStyle.Solid, c));
                        break;
                    default: l.DrawBoxShadow(ItemId.Of(id), rect, CornerRadii.Zero, c, 2, 2, 4, 0, false); break;
                }
            }

            if (s.Group)
            {
                l.PopGroup();
            }

            if (s.Clip)
            {
                l.PopClip();
            }

            if (s.Transform)
            {
                l.PopTransform();
            }
        }

        return l;
    }

    private static void Edit(List<Spec> scene, Random rng)
    {
        Spec s = scene[rng.Next(scene.Count)];
        switch (rng.Next(6))
        {
            case 0:
                {
                    int i = rng.Next(s.Items.Count);
                    (uint id, float x, float y, float w, float h, byte r, byte g, byte b, byte k) = s.Items[i];
                    s.Items[i] = (id, x, y, w, h, (byte)rng.Next(256), g, b, k);
                    break;
                }

            case 1:
                {
                    int i = rng.Next(s.Items.Count);
                    (uint id, float x, float y, float w, float h, byte r, byte g, byte b, byte k) = s.Items[i];
                    s.Items[i] = (id, rng.Next(0, 40), rng.Next(0, 40), w, h, r, g, b, k);
                    break;
                }

            case 2:
                if (s.Items.Count > 1)
                {
                    s.Items.RemoveAt(rng.Next(s.Items.Count));
                }

                break;
            case 3:
                {
                    uint max = 0;
                    foreach (Spec sp in scene)
                    {
                        foreach (var it in sp.Items)
                        {
                            max = Math.Max(max, it.Id);
                        }
                    }

                    s.Items.Insert(rng.Next(s.Items.Count + 1), (max + 1, rng.Next(0, 40), rng.Next(0, 40), rng.Next(2, 24), rng.Next(2, 24), (byte)rng.Next(256), 90, 40, (byte)rng.Next(4)));
                    break;
                }

            case 4:
                s.Tx = rng.Next(-8, 9);
                s.Ty = rng.Next(-8, 9);
                break;
            default:
                s.Opacity = rng.Next(0, 5) / 4f;
                break;
        }
    }

    private static RgbaImage Raster(DisplayList l) =>
        DisplayListRasterizer.Render(l, 96, 96, new RasterOptions { Background = PaintColor.White });

    [Fact]
    public void DamageCoversEveryChangedPixel()
    {
        Random rng = new(1234);
        long changed = 0;
        for (int round = 0; round < 60; round++)
        {
            List<Spec> scene = RandomScene(rng);
            DisplayList before = Build(scene);
            RgbaImage a = Raster(before);
            for (int step = 0; step < 4; step++)
            {
                Edit(scene, rng);
                DisplayList after = Build(scene);
                RgbaImage b = Raster(after);
                DamageRegion d = new();
                DisplayList.ComputeDamage(before, after, d);
                RectI px = d.ToDevicePixels(1f);
                for (int y = 0; y < 96; y++)
                {
                    for (int x = 0; x < 96; x++)
                    {
                        int o = ((y * 96) + x) * 4;
                        bool same = a.Pixels[o] == b.Pixels[o] && a.Pixels[o + 1] == b.Pixels[o + 1] && a.Pixels[o + 2] == b.Pixels[o + 2] && a.Pixels[o + 3] == b.Pixels[o + 3];
                        if (!same)
                        {
                            changed++;
                            bool covered = false;
                            for (int k = 0; k < d.Count && !covered; k++)
                            {
                                RectF r = d[k];
                                covered = x + 1 > r.X && x < r.Right && y + 1 > r.Y && y < r.Bottom;
                            }

                            Assert.True(covered && !px.IsEmpty, $"round {round} step {step}: pixel {x},{y} changed outside the damage");
                        }
                    }
                }

                before = after;
                a = b;
            }
        }

        // The check must not be vacuous: the edits really change pixels.
        Assert.True(changed > 5000, "only " + changed + " pixels changed");
    }

    [Fact]
    public void PatchingEqualsRebuilding()
    {
        Random rng = new(99);
        for (int round = 0; round < 200; round++)
        {
            List<Spec> scene = RandomScene(rng);
            int b = rng.Next(scene.Count);
            Spec s = scene[b];
            DisplayList patched = Build(scene);
            uint bid = 1000000u + ((uint)b * 10);
            DamageRegion d = new();
            if (s.Transform)
            {
                s.Tx = rng.Next(-8, 9);
                s.Ty = rng.Next(-8, 9);
                patched.PatchTransform(ItemId.Of(bid), Matrix3x2.CreateTranslation(s.Tx, s.Ty), d);
            }

            if (s.Group)
            {
                s.Opacity = rng.Next(0, 5) / 4f;
                patched.PatchOpacity(ItemId.Of(bid + 2), s.Opacity, d);
            }

            DisplayList rebuilt = Build(scene);
            DamageRegion none = new();
            DisplayList.ComputeDamage(patched, rebuilt, none);
            Assert.True(none.IsEmpty, "round " + round + ": patched list differs from rebuilt list");
            Assert.Equal(patched.Count, rebuilt.Count);
            for (int i = 0; i < patched.Count; i++)
            {
                Assert.Equal(rebuilt[i].RootBounds, patched[i].RootBounds);
            }
        }
    }
}
