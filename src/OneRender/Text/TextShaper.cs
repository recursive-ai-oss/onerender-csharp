using OneFont;

namespace OneRender.Text;

/// <summary>
/// The text service inline layout calls: it chooses a face for every character (CSS Fonts 4 §5.2 to §5.4), shapes the text
/// with OneFont and returns <see cref="ShapedText"/>s, caching them by text, font, size, direction and features.
/// </summary>
/// <remarks>
/// A shaper belongs to one thread at a time. Frame-path use is allocation-free: <see cref="Shape"/> returns a cached run
/// without allocating, and <see cref="ShapeInto"/> reuses the caller's run once its buffers have grown to fit.
/// </remarks>
public sealed class TextShaper
{
    /// <summary>The longest text one call shapes, in UTF-16 code units. Longer text is refused with an <see cref="ArgumentException"/>.</summary>
    public const int MaxRunLength = 1 << 20;

    /// <summary>The longest text the cache keeps; longer runs are shaped but not cached.</summary>
    public const int MaxCachedLength = 2048;

    private const int MaxClusterCodePoints = 32;
    private const int Ways = 4;

    private struct CacheEntry
    {
        public int Hash;
        public int Tick;
        public float Size;
        public bool Rtl;
        public ResolvedFontList? List;
        public ShapedText? Run;
    }

    private readonly FontCatalog _fonts;
    private readonly CacheEntry[] _cache;
    private readonly int _mask;
    private int _tick;
    private int _cached;
    private readonly ShapedRun _shaped = new();
    private int[] _segStart = new int[16];
    private int[] _segLength = new int[16];
    private ResolvedFace?[] _segFace = new ResolvedFace?[16];
    private int _segCount;

    /// <summary>Creates a shaper over a collection.</summary>
    /// <param name="fonts">The faces to match and shape with.</param>
    /// <param name="cacheCapacity">The most runs the cache holds, rounded up to a power of two (at least 4, at most 1,048,576). Default 4096. A full cache replaces its least recently used entry.</param>
    public TextShaper(FontCatalog fonts, int cacheCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cacheCapacity, 1 << 20);
        _fonts = fonts;
        int size = Ways;
        while (size < cacheCapacity)
        {
            size <<= 1;
        }

        _cache = new CacheEntry[size];
        _mask = size - 1;
    }

    /// <summary>The collection the shaper matches against.</summary>
    public FontCatalog Fonts => _fonts;

    /// <summary>The number of runs in the cache.</summary>
    public int CachedRuns => _cached;

    /// <summary>The most runs the cache holds.</summary>
    public int CacheCapacity => _cache.Length;

    /// <summary>Empties the cache.</summary>
    public void ClearCache()
    {
        Array.Clear(_cache);
        _cached = 0;
    }

    /// <summary>
    /// Shapes text, or returns the cached run for the same text, font, size, direction and features. A cache hit allocates nothing.
    /// </summary>
    /// <param name="text">The text of one directional run (the caller splits paragraphs by bidi level; see <see cref="TextAnalysis"/>).</param>
    /// <param name="request">The computed font properties, including the size.</param>
    /// <param name="rightToLeft">Whether the run is right to left.</param>
    /// <returns>The shared run; do not change it. An empty text gives an empty run.</returns>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>.</exception>
    public ShapedText Shape(ReadOnlySpan<char> text, FontRequest request, bool rightToLeft = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (text.Length > MaxCachedLength)
        {
            var big = new ShapedText();
            ShapeInto(big, text, request, rightToLeft);
            return big;
        }

        ResolvedFontList list = _fonts.Resolve(request);
        int hash = Hash(text, list.Id, request.Size, rightToLeft);
        int baseIndex = hash & _mask & ~(Ways - 1);
        for (int k = 0; k < Ways; k++)
        {
            ref CacheEntry e = ref _cache[baseIndex + k];
            if (e.Run is not null && e.Hash == hash && ReferenceEquals(e.List, list) && e.Size == request.Size && e.Rtl == rightToLeft && text.SequenceEqual(e.Run.Text))
            {
                e.Tick = ++_tick;
                return e.Run;
            }
        }

        var run = new ShapedText();
        ShapeInto(run, text, request, rightToLeft);
        list = _fonts.Resolve(request); // a font that failed to load while shaping makes the first list stale

        int victim = baseIndex;
        for (int k = 0; k < Ways; k++)
        {
            ref CacheEntry e = ref _cache[baseIndex + k];
            if (e.Run is null)
            {
                victim = baseIndex + k;
                break;
            }

            if (e.Tick < _cache[victim].Tick)
            {
                victim = baseIndex + k;
            }
        }

        if (_cache[victim].Run is null)
        {
            _cached++;
        }

        _cache[victim] = new CacheEntry { Hash = Hash(text, list.Id, request.Size, rightToLeft), Tick = ++_tick, Size = request.Size, Rtl = rightToLeft, List = list, Run = run };
        return run;
    }

    /// <summary>
    /// Shapes text into a run the caller keeps and reuses. After the run's buffers have grown to fit, a call allocates nothing.
    /// The cache is not used.
    /// </summary>
    /// <param name="run">Receives the result; its previous content is replaced.</param>
    /// <param name="text">The text of one directional run.</param>
    /// <param name="request">The computed font properties, including the size.</param>
    /// <param name="rightToLeft">Whether the run is right to left.</param>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxRunLength"/>.</exception>
    public void ShapeInto(ShapedText run, ReadOnlySpan<char> text, FontRequest request, bool rightToLeft = false)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(request);
        if (text.Length > MaxRunLength)
        {
            throw new ArgumentException("The text is too long to shape in one run.", nameof(text));
        }

        // A face that fails to load while we work makes the list stale; match again without it. Every retry has one more
        // failed face, so the loop ends.
        for (int attempt = 0; ; attempt++)
        {
            ResolvedFontList list = _fonts.Resolve(request);
            run.Begin(text, request.Size, rightToLeft);
            if (text.Length == 0)
            {
                return;
            }

            if (list.PrimaryFace is null)
            {
                _fonts.Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.NoFonts));
                run.Finish(false);
                return;
            }

            SegmentByFace(text, list);
            bool stale = list.IsStale;
            if (stale && attempt < FontCatalog.MaxFaces)
            {
                continue;
            }

            ShapeSegments(run, text, list, request.Size, rightToLeft);
            return;
        }
    }

    // ---- choosing faces --------------------------------------------------------------------------------------------

    private void SegmentByFace(ReadOnlySpan<char> text, ResolvedFontList list)
    {
        _segCount = 0;
        ResolvedFace primary = list.PrimaryFace!;
        ResolvedFace? previous = null;
        int pos = 0;
        while (pos < text.Length)
        {
            int length = TextSegmentation.GetGraphemeLength(text[pos..]);
            if (length < 1)
            {
                length = 1;
            }

            previous = ChooseForCluster(text.Slice(pos, length), pos, list, primary, previous);
            pos += length;
        }
    }

    private ResolvedFace ChooseForCluster(ReadOnlySpan<char> cluster, int offset, ResolvedFontList list, ResolvedFace primary, ResolvedFace? previous)
    {
        Span<int> cps = stackalloc int[MaxClusterCodePoints];
        Span<int> at = stackalloc int[MaxClusterCodePoints];
        int count = 0;
        int baseIndex = -1;
        for (int i = 0; i < cluster.Length && count < MaxClusterCodePoints;)
        {
            int cp = CodePointAt(cluster, i, out int len);
            at[count] = i;
            cps[count] = cp;
            if (baseIndex < 0 && !IsInvisible(cp))
            {
                baseIndex = count;
            }

            count++;
            i += len;
        }

        if (baseIndex < 0)
        {
            // Only invisible characters (line feeds, joiners, selectors): they follow the cluster before.
            ResolvedFace f = previous ?? primary;
            AddUnit(offset, cluster.Length, f);
            return f;
        }

        int baseCp = cps[baseIndex];
        ResolvedFace? cover = null;
        ResolvedFace? whole = null;
        int slots = IsPrivateUse(baseCp) ? list.ListedSlots : list.Slots.Length;
        for (int s = 0; s < slots && whole is null; s++)
        {
            FontSlot slot = list.Slots[s];
            if (IsPrivateUse(baseCp) && slot.IsGeneric)
            {
                continue; // §5.4: private-use characters are matched only against named families
            }

            ResolvedFace? f = slot.Select(baseCp, list);
            if (f is null)
            {
                continue;
            }

            cover ??= f;
            if (CoversAll(f, cps, count, baseIndex))
            {
                whole = f;
            }
        }

        if (whole is not null)
        {
            AddUnit(offset, cluster.Length, whole);
            return whole;
        }

        if (cover is null)
        {
            ResolvedFace? sys = IsPrivateUse(baseCp) ? null : _fonts.FindSystemFallback(baseCp);
            if (sys is not null && CoversAll(sys, cps, count, baseIndex))
            {
                AddUnit(offset, cluster.Length, sys);
                return sys;
            }

            // Nothing has the base character: the first available face draws the missing-glyph shape for the whole cluster.
            AddUnit(offset, cluster.Length, primary);
            return primary;
        }

        // §5.3 step 3: the longest prefix one face supports stays together; the rest are matched one character at a time.
        int split = baseIndex + 1;
        while (split < count && (IsInvisible(cps[split]) || (cover.Entry.InRange(cps[split]) && cover.Entry.HasGlyph(cps[split], _fonts))))
        {
            split++;
        }

        int firstEnd = split < count ? at[split] : cluster.Length;
        AddUnit(offset, firstEnd, cover);
        for (int k = split; k < count; k++)
        {
            int from = at[k];
            int to = k + 1 < count ? at[k + 1] : cluster.Length;
            ResolvedFace? single = IsInvisible(cps[k]) ? cover : FindSingle(list, cps[k]);
            AddUnit(offset + from, to - from, single ?? cover);
        }

        // Code points beyond the inspected ones stay with the face of the last inspected one.
        return cover;
    }

    private bool CoversAll(ResolvedFace f, Span<int> cps, int count, int baseIndex)
    {
        for (int i = 0; i < count; i++)
        {
            if (i == baseIndex || IsInvisible(cps[i]))
            {
                continue;
            }

            if (!f.Entry.InRange(cps[i]) || !f.Entry.HasGlyph(cps[i], _fonts))
            {
                return false;
            }
        }

        return true;
    }

    private ResolvedFace? FindSingle(ResolvedFontList list, int cp)
    {
        int slots = IsPrivateUse(cp) ? list.ListedSlots : list.Slots.Length;
        for (int s = 0; s < slots; s++)
        {
            FontSlot slot = list.Slots[s];
            if (IsPrivateUse(cp) && slot.IsGeneric)
            {
                continue;
            }

            ResolvedFace? f = slot.Select(cp, list);
            if (f is not null)
            {
                return f;
            }
        }

        return IsPrivateUse(cp) ? null : _fonts.FindSystemFallback(cp);
    }

    private void AddUnit(int start, int length, ResolvedFace face)
    {
        if (length <= 0)
        {
            return;
        }

        if (_segCount > 0 && ReferenceEquals(_segFace[_segCount - 1], face) && _segStart[_segCount - 1] + _segLength[_segCount - 1] == start)
        {
            _segLength[_segCount - 1] += length;
            return;
        }

        if (_segCount == _segStart.Length)
        {
            int n = _segCount * 2;
            Array.Resize(ref _segStart, n);
            Array.Resize(ref _segLength, n);
            Array.Resize(ref _segFace, n);
        }

        _segStart[_segCount] = start;
        _segLength[_segCount] = length;
        _segFace[_segCount] = face;
        _segCount++;
    }

    internal static int CodePointAt(ReadOnlySpan<char> s, int index, out int length)
    {
        // A lone surrogate decodes as U+FFFD with length 1, as the shaper treats it.
        System.Text.Rune.DecodeFromUtf16(s[index..], out System.Text.Rune r, out length);
        return r.Value;
    }

    private static bool IsPrivateUse(int cp) => cp is (>= 0xE000 and <= 0xF8FF) or (>= 0xF0000 and <= 0xFFFFD) or (>= 0x100000 and <= 0x10FFFD);

    // Characters that draw nothing and so never decide which face a cluster uses.
    internal static bool IsInvisible(int cp) =>
        cp < 0x20 || (cp >= 0x7F && cp <= 0x9F) || cp is 0x00AD or 0x034F or 0x061C or 0xFEFF
        || (cp >= 0x180B && cp <= 0x180F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0x2028 && cp <= 0x202E)
        || (cp >= 0x2060 && cp <= 0x206F) || (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0000 && cp <= 0xE0FFF);

    // ---- shaping ---------------------------------------------------------------------------------------------------

    private void ShapeSegments(ShapedText run, ReadOnlySpan<char> text, ResolvedFontList list, float size, bool rtl)
    {
        for (int s = 0; s < _segCount; s++)
        {
            ResolvedFace face = _segFace[s]!;
            int start = _segStart[s];
            int length = _segLength[s];
            float scale = size * face.SizeAdjust / face.Face.UnitsPerEm;
            int glyphStart = run.GlyphCount;
            int clusterStart = run.ClusterCountInternal;
            float advance = 0;
            try
            {
                TextShaping.ShapeRun(text.Slice(start, length), face.Face, _shaped, rtl, list.Options);
            }
            catch (FontFormatException)
            {
                // Damaged glyph data: the segment draws nothing, and the cluster map still covers the text.
                _fonts.Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontLoadFailed, Subject: face.FamilyName, Detail: "The font data is damaged."));
                run.AddCluster(start, length, glyphStart, 0, 0, false);
                run.AddSegment(face, glyphStart, 0, start, length, clusterStart, 1, 0);
                continue;
            }

            foreach (ShapedClusterRange c in _shaped.Clusters)
            {
                int clusterGlyphStart = run.GlyphCount;
                float clusterAdvance = 0;
                foreach (ShapedGlyph g in _shaped.GlyphsOf(c))
                {
                    float adv = g.XAdvance * scale;
                    run.AddGlyph(g.GlyphId, adv, g.XOffset * scale, -g.YOffset * scale, start + c.Start);
                    clusterAdvance += adv;
                }

                advance += clusterAdvance;
                run.AddCluster(start + c.Start, c.Length, clusterGlyphStart, run.GlyphCount - clusterGlyphStart, clusterAdvance, c.HasMissingGlyph);
                if (c.HasMissingGlyph)
                {
                    ReportMissing(run, text, start + c.Start, c.Length);
                }
            }

            run.AddSegment(face, glyphStart, run.GlyphCount - glyphStart, start, length, clusterStart, run.ClusterCountInternal - clusterStart, advance);
        }

        run.Finish(rtl);
    }

    private void ReportMissing(ShapedText run, ReadOnlySpan<char> text, int at, int length)
    {
        // Name the first character of the cluster that draws something; controls and joiners never count as missing.
        for (int i = at; i < at + length && i < text.Length;)
        {
            int cp = CodePointAt(text, i, out int len);
            i += len;
            if (IsInvisible(cp))
            {
                continue;
            }

            var d = new TextDiagnostic(TextDiagnosticKind.MissingGlyph, cp, FontPackages.ForCodePoint(cp));
            run.NoteMissing(d);
            _fonts.Diagnostics.Report(d);
            return;
        }
    }

    private static int Hash(ReadOnlySpan<char> text, int listId, float size, bool rtl)
    {
        // FNV-1a over the code units: stable across runs, unlike the framework's randomized string hash.
        uint h = 2166136261;
        foreach (char c in text)
        {
            h = (h ^ c) * 16777619;
        }

        h = (h ^ (uint)listId) * 16777619;
        h = (h ^ (uint)BitConverter.SingleToInt32Bits(size)) * 16777619;
        h = (h ^ (rtl ? 1u : 0u)) * 16777619;
        h ^= h >> 15;
        return (int)h;
    }
}
