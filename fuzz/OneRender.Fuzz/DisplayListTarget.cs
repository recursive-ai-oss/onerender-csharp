using System;
using System.Numerics;
using OneFont;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Fuzz;

/// <summary>
/// Replays a byte string as a sequence of display-list operations: fills, borders, shadows, gradients, glyph runs, images,
/// clips, transforms, groups, tags, paths and in-place patches, with hostile numbers (NaN, infinities, huge and tiny values,
/// duplicate ids, unbalanced pops). The built list is committed twice to a retained list, its structure is checked, and a small
/// list is rasterized.
/// Only <see cref="DisplayListException"/>, <see cref="DisplayListLimitException"/>, <see cref="ArgumentException"/> and
/// <see cref="RasterizationException"/> may escape a library call; any other exception, a broken invariant (thrown as
/// <see cref="InvalidOperationException"/> from here), a hang or a memory blow-up is a finding.
/// </summary>
internal static class DisplayListTarget
{
    private static readonly Lazy<FontFace> Face = new(() =>
    {
        foreach (FontFaceInfo f in DefaultFonts.GetFaces("Liberation Sans"))
        {
            if (f.Weight == 400 && f.Slant == FontSlant.Normal)
            {
                return FontFace.Load(f.ReadAllBytes());
            }
        }

        throw new InvalidOperationException("no face");
    });

    private static readonly float[] Specials = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e10f, -1e10f, 1e-30f, 1e9f, -1e9f];

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _data = data.ToArray();
        private int _pos;

        public bool More => _pos < _data.Length;

        public int Byte() => _pos < _data.Length ? _data[_pos++] : 0;

        // Mostly small values in a 100-pixel world, sometimes a special value.
        public float F()
        {
            int b = Byte();
            return b >= 248 ? Specials[b - 248] : (b - 40) * 0.75f;
        }

        public PaintColor Color() => new(Byte() / 255f, Byte() / 255f, Byte() / 255f, Byte() / 255f);

        public RectF Rect() => new(F(), F(), F(), F());

        public CornerRadii Radii() => new(F(), F(), F(), F(), F(), F(), F(), F());

        public ItemId Id() => ItemId.Of((uint)(Byte() % 48) + 1, (ushort)(Byte() & 3));
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        RetainedDisplayList retained = new();
        for (int pass = 0; pass < 2; pass++)
        {
            DisplayList list = retained.Begin();
            Build(list, data, pass);
            Close(list);
            retained.Commit();
            Check(retained.Current);
        }

        if (retained.Current.Count <= 300)
        {
            try
            {
                RgbaImage img = DisplayListRasterizer.Render(retained.Current, 24, 24, new RasterOptions { DevicePixelRatio = 1 + (data[0] & 1) });
                if (img.Width != 24)
                {
                    throw new InvalidOperationException("wrong image size");
                }
            }
            catch (RasterizationException)
            {
            }
        }
    }

    // Pops are chosen by the target's own stack so that the list can always be finished.
    private static readonly System.Collections.Generic.List<int> Stack = [];

    /// <summary>Builds a closed, validated list from the bytes (the first pass of <see cref="Run"/>), for targets that consume lists.</summary>
    internal static DisplayList BuildClosed(ReadOnlySpan<byte> data)
    {
        var list = new DisplayList();
        Build(list, data, 0);
        Close(list);
        return list;
    }

    private static void Close(DisplayList list)
    {
        while (Stack.Count > 0)
        {
            Pop(list, Stack[^1]);
            Stack.RemoveAt(Stack.Count - 1);
        }

        list.Validate();
    }

    private static void Pop(DisplayList l, int family)
    {
        switch (family)
        {
            case 0: l.PopClip(); break;
            case 1: l.PopTransform(); break;
            case 2: l.PopGroup(); break;
            case 4: l.PopHostScope(); break;
            default: l.EndTag(); break;
        }
    }

    private static void Build(DisplayList l, ReadOnlySpan<byte> input, int pass)
    {
        Stack.Clear();
        Reader r = new(input);
        int ops = 0;
        while (r.More && ops++ < 400)
        {
            int op = r.Byte() & 31;
            try
            {
                Step(l, r, op, pass);
            }
            catch (Exception ex) when (ex is DisplayListException or DisplayListLimitException or ArgumentException)
            {
                // Refused input is the expected outcome for hostile values; the list must stay usable.
            }
        }
    }

    private static void Step(DisplayList l, Reader r, int op, int pass)
    {
        switch (op)
        {
            case 0: l.FillRect(r.Id(), r.Rect(), r.Color()); break;
            case 1: l.FillRoundedRect(r.Id(), r.Rect(), r.Radii(), r.Color()); break;
            case 2:
                {
                    ItemId id = r.Id();
                    RectF rect = r.Rect();
                    CornerRadii radii = r.Radii();
                    l.DrawBorder(id, rect, radii, Side(r), Side(r), Side(r), Side(r));
                    break;
                }

            case 3: l.DrawBoxShadow(r.Id(), r.Rect(), r.Radii(), r.Color(), r.F(), r.F(), r.F(), r.F(), (r.Byte() & 1) != 0); break;
            case 4:
                {
                    int kind = r.Byte() % 3;
                    ItemId id = r.Id();
                    if (kind == 0)
                    {
                        l.PushClipRect(id, r.Rect());
                    }
                    else if (kind == 1)
                    {
                        l.PushClipRoundedRect(id, r.Rect(), r.Radii());
                    }
                    else
                    {
                        PathBuffer p = Path(r);
                        l.PushClipPath(id, p, (r.Byte() & 1) != 0 ? FillRule.EvenOdd : FillRule.NonZero);
                    }

                    Stack.Add(0);
                    break;
                }

            case 5: PopIf(l, 0); break;
            case 6:
                {
                    ItemId id = r.Id();
                    Matrix3x2 m = new(r.F() * 0.1f, r.F() * 0.1f, r.F() * 0.1f, r.F() * 0.1f, r.F(), r.F());
                    if ((r.Byte() & 7) == 0)
                    {
                        l.PushTransform3D(id, new Matrix4x4(m));
                    }
                    else
                    {
                        l.PushTransform(id, m);
                    }

                    Stack.Add(1);
                    break;
                }

            case 7: PopIf(l, 1); break;
            case 8:
                {
                    ItemId id = r.Id();
                    int o = r.Byte();
                    l.PushGroup(id, o >= 248 ? Specials[o - 248] : o / 255f, (BlendMode)(r.Byte() % 18));
                    Stack.Add(2);
                    break;
                }

            case 9: PopIf(l, 2); break;
            case 10:
                {
                    ItemId id = r.Id();
                    l.BeginTag(id, (TagRole)(r.Byte() % 31), (r.Byte() & 1) != 0 ? "alt" : null, (r.Byte() & 1) != 0 ? "#x" : null);
                    Stack.Add(3);
                    break;
                }

            case 11: PopIf(l, 3); break;
            case 12:
                {
                    ItemId id = r.Id();
                    RectF rect = r.Rect();
                    int n = (r.Byte() % 5) + 1;
                    GradientStop[] stops = new GradientStop[n];
                    for (int i = 0; i < n; i++)
                    {
                        stops[i] = new GradientStop(r.F() * 0.05f, r.Color());
                    }

                    int kind = r.Byte() % 3;
                    bool rep = (r.Byte() & 1) != 0;
                    Paint p = kind == 0
                        ? l.LinearGradient(r.F(), r.F(), r.F(), r.F(), stops, rep)
                        : kind == 1 ? l.RadialGradient(r.F(), r.F(), r.F(), r.F(), stops, rep) : l.ConicGradient(r.F(), r.F(), r.F() * 0.1f, stops, rep);
                    l.FillRect(id, rect, p);
                    break;
                }

            case 13:
                {
                    FontFace face = Face.Value;
                    int n = r.Byte() % 8;
                    int[] ids = new int[n];
                    float[] xs = new float[n];
                    int[] cl = new int[n];
                    char[] text = new char[r.Byte() % 8];
                    for (int i = 0; i < n; i++)
                    {
                        ids[i] = r.Byte() * 3;
                        xs[i] = r.F();
                        cl[i] = r.Byte() % 9;
                    }

                    for (int i = 0; i < text.Length; i++)
                    {
                        text[i] = (char)(32 + (r.Byte() % 90));
                    }

                    l.DrawGlyphRun(r.Id(), face, r.F(), r.F(), r.F(), r.Color(), ids, xs, default, cl, text, (r.Byte() & 1) != 0);
                    break;
                }

            case 14: l.DrawImage(r.Id(), new ImageHandle(r.Byte() % 3), r.Rect(), r.Rect(), (ImageSampling)(r.Byte() & 1)); break;
            case 15: l.FillPath(r.Id(), Path(r), r.Color(), (r.Byte() & 1) != 0 ? FillRule.EvenOdd : FillRule.NonZero); break;
            case 16:
                if (pass == 1)
                {
                    l.PatchTransform(r.Id(), new Matrix3x2(r.F() * 0.1f, 0, 0, r.F() * 0.1f, r.F(), r.F()));
                }

                break;
            case 17:
                if (pass == 1)
                {
                    l.PatchOpacity(r.Id(), r.Byte() / 255f);
                }

                break;
            case 18:
                {
                    ItemId id = r.Id();
                    l.PushHostScope(id, r.Byte() - 128);
                    Stack.Add(4);
                    break;
                }

            case 19: PopIf(l, 4); break;
            default:
                // Everything else is a plain fill so that most random bytes still make progress.
                l.FillRect(r.Id(), r.Rect(), r.Color());
                break;
        }
    }

    private static void PopIf(DisplayList l, int family)
    {
        if (Stack.Count > 0 && Stack[^1] == family)
        {
            Pop(l, family);
            Stack.RemoveAt(Stack.Count - 1);
        }
        else
        {
            // An unbalanced pop must be refused with the typed exception, never corrupt the list.
            try
            {
                Pop(l, family);
            }
            catch (DisplayListException)
            {
            }
        }
    }

    private static BorderSide Side(Reader r) => new(r.F() * 0.2f, (BorderStyle)(r.Byte() % 11), r.Color());

    private static PathBuffer Path(Reader r)
    {
        PathBuffer p = new();
        int n = r.Byte() % 12;
        for (int i = 0; i < n; i++)
        {
            switch (r.Byte() % 5)
            {
                case 0: p.MoveTo(r.F(), r.F()); break;
                case 1: p.LineTo(r.F(), r.F()); break;
                case 2: p.QuadTo(r.F(), r.F(), r.F(), r.F()); break;
                case 3: p.CubicTo(r.F(), r.F(), r.F(), r.F(), r.F(), r.F()); break;
                default: p.Close(); break;
            }
        }

        return p;
    }

    // Structural invariants every finished list must satisfy.
    private static void Check(DisplayList l)
    {
        for (int i = 0; i < l.Count; i++)
        {
            DisplayItem it = l[i];
            if (it.ParentIndex >= i)
            {
                throw new InvalidOperationException("parent after child at " + i);
            }

            bool push = it.Kind is ItemKind.PushClipRect or ItemKind.PushClipRoundedRect or ItemKind.PushClipPath or ItemKind.PushTransform or ItemKind.PushGroup or ItemKind.BeginTag or ItemKind.PushHostScope;
            bool pop = it.Kind is ItemKind.PopClip or ItemKind.PopTransform or ItemKind.PopGroup or ItemKind.EndTag or ItemKind.PopHostScope;
            if (push && (it.MatchIndex <= i || l[it.MatchIndex].MatchIndex != i))
            {
                throw new InvalidOperationException("push/pop mismatch at " + i);
            }

            if (pop && (it.MatchIndex >= i || l[it.MatchIndex].MatchIndex != i))
            {
                throw new InvalidOperationException("pop/push mismatch at " + i);
            }

            if (!pop && !it.Id.IsValid)
            {
                throw new InvalidOperationException("item without id at " + i);
            }

            if (!l.TryFind(it.Id, out int found) && it.Id.IsValid || (it.Id.IsValid && found != i))
            {
                throw new InvalidOperationException("id lookup failed at " + i);
            }
        }
    }

    public static int WriteSeeds(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        Random rng = new(7);
        for (int i = 0; i < 24; i++)
        {
            byte[] b = new byte[40 + (i * 25)];
            rng.NextBytes(b);
            for (int k = 0; k < b.Length; k++)
            {
                // Keep most bytes out of the special-value range so seeds start with ordinary geometry.
                if (b[k] >= 248 && rng.Next(4) != 0)
                {
                    b[k] = (byte)rng.Next(248);
                }
            }

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "seed-" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)), b);
        }

        return 0;
    }
}
