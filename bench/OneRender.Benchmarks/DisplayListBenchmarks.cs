using System;
using System.Collections.Generic;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using OneFont;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Benchmarks;

/// <summary>
/// Display list costs: rebuilding and patching a retained list (steady-state frame work, expected 0 B), and rasterizing a
/// mixed scene through oneimage (the reference rasterizer; allocation here is expected).
/// </summary>
[MemoryDiagnoser]
public class DisplayListBenchmarks
{
    private readonly RetainedDisplayList _retained = new();
    private readonly PathBuffer _path = new();
    private readonly DisplayList _scene = new();
    private FontFace _face = null!;
    private int[] _glyphs = null!;
    private float[] _xs = null!;
    private int[] _clusters = null!;
    private int _frame;
    private float _t;

    [GlobalSetup]
    public void Setup()
    {
        foreach (FontFaceInfo f in DefaultFonts.GetFaces("Liberation Sans"))
        {
            if (f.Weight == 400 && f.Slant == FontSlant.Normal)
            {
                _face = FontFace.Load(f.ReadAllBytes());
                break;
            }
        }

        string text = "Display list";
        IReadOnlyList<ShapedCluster> clusters = TextShaping.ShapeRun(text, _face);
        List<int> ids = [], cl = [];
        List<float> xs = [];
        float pen = 0;
        foreach (ShapedCluster c in clusters)
        {
            foreach (ShapedGlyph g in c.Glyphs)
            {
                ids.Add(g.GlyphId);
                xs.Add(pen);
                cl.Add(c.Start);
                pen += g.XAdvance * 14f / _face.UnitsPerEm;
            }
        }

        _glyphs = [.. ids];
        _xs = [.. xs];
        _clusters = [.. cl];
        _path.MoveTo(0, 0);
        _path.LineTo(8, 0);
        _path.LineTo(4, 8);
        _path.Close();
        BuildThousand(_retained.Begin());
        _retained.Commit();
        BuildScene(_scene);
    }

    private void BuildThousand(DisplayList l)
    {
        PaintColor c = (_frame & 1) == 0 ? PaintColor.FromBytes(200, 40, 40) : PaintColor.FromBytes(40, 40, 200);
        for (uint i = 0; i < 125; i++)
        {
            uint b = (i * 8) + 1;
            float x = (i % 25) * 10, y = (i / 25) * 10;
            l.FillRect(ItemId.Of(b), new RectF(x, y, 9, 9), c);
            l.FillRoundedRect(ItemId.Of(b + 1), new RectF(x, y, 9, 9), CornerRadii.Uniform(3), c);
            l.DrawBorder(ItemId.Of(b + 2), new RectF(x, y, 9, 9), CornerRadii.Zero, new BorderSide(1, BorderStyle.Solid, c), new BorderSide(1, BorderStyle.Solid, c), new BorderSide(1, BorderStyle.Solid, c), new BorderSide(1, BorderStyle.Solid, c));
            l.PushClipRect(ItemId.Of(b + 3), new RectF(x, y, 9, 9));
            l.FillPath(ItemId.Of(b + 4), _path, c);
            l.PopClip();
            l.DrawGlyphRun(ItemId.Of(b + 5), _face, 10, x, y + 8, c, _glyphs.AsSpan(0, 3), _xs.AsSpan(0, 3), default, _clusters.AsSpan(0, 3), "Dis");
            l.PushTransform(ItemId.Of(b + 6), Matrix3x2.CreateTranslation(1, 1));
            l.PopTransform();
        }

        l.PushTransform(ItemId.Of(900001), Matrix3x2.Identity);
        l.PushGroup(ItemId.Of(900002), 1f);
        l.FillRect(ItemId.Of(900003), new RectF(0, 0, 50, 50), c);
        l.PopGroup();
        l.PopTransform();
    }

    private void BuildScene(DisplayList l)
    {
        l.Clear();
        PaintColor white = PaintColor.White;
        l.FillRect(ItemId.Of(1), new RectF(0, 0, 512, 384), PaintColor.FromBytes(235, 238, 242));
        for (uint i = 0; i < 12; i++)
        {
            float x = 12 + ((i % 4) * 124), y = 12 + ((i / 4) * 122);
            RectF r = new(x, y, 110, 100);
            l.DrawBoxShadow(ItemId.Of(10 + (i * 10)), r, CornerRadii.Uniform(10), PaintColor.FromBytes(0, 0, 0, 90), 0, 3, 10, 0, false);
            Paint p = l.LinearGradient(x, y, x + 110, y + 100, [new GradientStop(0, PaintColor.FromBytes(60, 120, 220)), new GradientStop(1, PaintColor.FromBytes(240, 240, 255))]);
            l.FillRoundedRect(ItemId.Of(11 + (i * 10)), r, CornerRadii.Uniform(10), p);
            l.DrawBorder(ItemId.Of(12 + (i * 10)), r, CornerRadii.Uniform(10), new BorderSide(2, BorderStyle.Solid, white), new BorderSide(2, BorderStyle.Solid, white), new BorderSide(2, BorderStyle.Solid, white), new BorderSide(2, BorderStyle.Solid, white));
            l.DrawGlyphRun(ItemId.Of(13 + (i * 10)), _face, 14, x + 8, y + 24, PaintColor.Black, _glyphs, _xs, default, _clusters, "Display list");
        }
    }

    /// <summary>Rebuilds ~1,000 items into the retained list and diffs against the previous frame: the steady-state frame cost.</summary>
    [Benchmark]
    public int RebuildThousandItems()
    {
        _frame++;
        BuildThousand(_retained.Begin());
        _retained.Commit();
        _retained.ClearDamage();
        return _retained.Current.Count;
    }

    /// <summary>Patches a transform and an opacity in place: the animation frame cost.</summary>
    [Benchmark]
    public int PatchTransformAndOpacity()
    {
        _t += 0.5f;
        _retained.PatchTransform(ItemId.Of(900001), Matrix3x2.CreateTranslation(_t, 0));
        _retained.PatchOpacity(ItemId.Of(900002), 0.5f);
        _retained.ClearDamage();
        return _retained.Damage.Count;
    }

    /// <summary>Rasterizes a 512 by 384 scene (12 shadowed gradient cards with borders and text) at 1x.</summary>
    [Benchmark]
    public int Rasterize512x384() => DisplayListRasterizer.Render(_scene, 512, 384).Width;

    /// <summary>The same scene at 2x device-pixel ratio.</summary>
    [Benchmark]
    public int Rasterize512x384At2x() => DisplayListRasterizer.Render(_scene, 1024, 768, new RasterOptions { DevicePixelRatio = 2f }).Width;
}
