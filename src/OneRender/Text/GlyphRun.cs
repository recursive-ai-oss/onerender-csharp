namespace OneRender.Text;

/// <summary>One cluster of a shaped run: the characters it covers and the glyphs that draw them. Clusters are listed in display order.</summary>
public readonly struct GlyphCluster
{
    internal GlyphCluster(int textStart, int textLength, int glyphStart, int glyphCount, float advance, bool missing)
    {
        TextStart = textStart;
        TextLength = textLength;
        GlyphStart = glyphStart;
        GlyphCount = glyphCount;
        Advance = advance;
        HasMissingGlyph = missing;
    }

    /// <summary>The UTF-16 offset of the cluster's first character in <see cref="GlyphRun.Text"/>.</summary>
    public int TextStart { get; }

    /// <summary>The number of UTF-16 code units the cluster covers. A ligature covers several characters with one glyph; a character the font splits covers one with several.</summary>
    public int TextLength { get; }

    /// <summary>The index of the cluster's first glyph in the run's glyph arrays.</summary>
    public int GlyphStart { get; }

    /// <summary>The number of glyphs; 0 for a character that draws nothing (for example a joiner).</summary>
    public int GlyphCount { get; }

    /// <summary>The cluster's width in CSS pixels: the sum of its glyph advances.</summary>
    public float Advance { get; }

    /// <summary>True when the face has no glyph for a character of the cluster and the glyph drawn is the face's missing-glyph shape.</summary>
    public bool HasMissingGlyph { get; }
}

/// <summary>A stretch of a shaped run drawn with one face. Display order.</summary>
public readonly struct GlyphRunSegment
{
    internal GlyphRunSegment(ResolvedFace face, int glyphStart, int glyphCount, int textStart, int textLength, int clusterStart, int clusterCount, float advance)
    {
        Face = face;
        GlyphStart = glyphStart;
        GlyphCount = glyphCount;
        TextStart = textStart;
        TextLength = textLength;
        ClusterStart = clusterStart;
        ClusterCount = clusterCount;
        Advance = advance;
    }

    /// <summary>The face, with the synthetic bold and slant flags for the painter.</summary>
    public ResolvedFace Face { get; }

    /// <summary>The first glyph in the run's glyph arrays.</summary>
    public int GlyphStart { get; }

    /// <summary>The number of glyphs.</summary>
    public int GlyphCount { get; }

    /// <summary>The first character in <see cref="GlyphRun.Text"/> (logical order, whatever the direction).</summary>
    public int TextStart { get; }

    /// <summary>The number of UTF-16 code units.</summary>
    public int TextLength { get; }

    /// <summary>The index of the first cluster in <see cref="GlyphRun.Clusters"/>.</summary>
    public int ClusterStart { get; }

    /// <summary>The number of clusters.</summary>
    public int ClusterCount { get; }

    /// <summary>The width in CSS pixels.</summary>
    public float Advance { get; }
}

/// <summary>
/// Text shaped in one direction at one size: glyph ids with positions, the cluster map back to the source text and the face
/// of each stretch. Everything is in display order (left to right on screen), so a right-to-left run lists its last character
/// first. Positions are in CSS pixels from the run's origin: x runs right, y runs down from the baseline.
/// </summary>
/// <remarks>
/// A run from <see cref="TextShaper.Shape"/> is shared by the shaper's cache and must not be kept past the shaper's life or
/// changed. A run filled by <see cref="TextShaper.ShapeInto"/> belongs to the caller and is replaced by the next call with it.
/// </remarks>
public sealed class GlyphRun
{
    private char[] _text = [];
    private int _textLength;
    private int[] _glyphIds = [];
    private float[] _x = [];
    private float[] _y = [];
    private float[] _advance = [];
    private int[] _cluster = [];
    private int _glyphCount;
    private GlyphCluster[] _clusters = [];
    private int _clusterCount;
    private GlyphRunSegment[] _segments = [];
    private int _segmentCount;

    /// <summary>Creates an empty run. Fill it with <see cref="TextShaper.ShapeInto"/>.</summary>
    public GlyphRun()
    {
    }

    /// <summary>The source text, as given to the shaper.</summary>
    public ReadOnlySpan<char> Text => new(_text, 0, _textLength);

    /// <summary>The font size in CSS pixels the run was shaped at.</summary>
    public float Size { get; private set; }

    /// <summary>Whether the run is right to left.</summary>
    public bool RightToLeft { get; private set; }

    /// <summary>The total advance width in CSS pixels.</summary>
    public float Width { get; private set; }

    /// <summary>The number of glyphs.</summary>
    public int GlyphCount => _glyphCount;

    /// <summary>The OpenType glyph id of each glyph, in the face of its segment.</summary>
    public ReadOnlySpan<int> GlyphIds => new(_glyphIds, 0, _glyphCount);

    /// <summary>Where each glyph's origin lies along the baseline: the pen position plus the glyph's own x offset.</summary>
    public ReadOnlySpan<float> X => new(_x, 0, _glyphCount);

    /// <summary>Each glyph's vertical offset from the baseline, positive downward (mark placement, superscript glyphs).</summary>
    public ReadOnlySpan<float> Y => new(_y, 0, _glyphCount);

    /// <summary>How far the pen moves after each glyph.</summary>
    public ReadOnlySpan<float> Advances => new(_advance, 0, _glyphCount);

    /// <summary>For each glyph, the UTF-16 offset in <see cref="Text"/> of the first character of its cluster.</summary>
    public ReadOnlySpan<int> GlyphClusters => new(_cluster, 0, _glyphCount);

    /// <summary>The clusters in display order.</summary>
    public ReadOnlySpan<GlyphCluster> Clusters => new(_clusters, 0, _clusterCount);

    /// <summary>The stretches drawn with one face each, in display order. They cover all glyphs and clusters.</summary>
    public ReadOnlySpan<GlyphRunSegment> Segments => new(_segments, 0, _segmentCount);

    /// <summary>The number of clusters that have no glyph in any face (drawn with a missing-glyph shape).</summary>
    public int MissingGlyphCount { get; private set; }

    /// <summary>
    /// The first missing glyph of the run, with the package that would cover it; null when none is missing. The same value is recorded
    /// once in <see cref="FontCatalog.Diagnostics"/>.
    /// </summary>
    public TextDiagnostic? FirstMissingGlyph { get; private set; }

    internal int ClusterCountInternal => _clusterCount;

    internal void Begin(ReadOnlySpan<char> text, float size, bool rightToLeft)
    {
        if (_text.Length < text.Length)
        {
            _text = new char[Grow(text.Length, 16)];
        }

        text.CopyTo(_text);
        _textLength = text.Length;
        Size = size;
        RightToLeft = rightToLeft;
        Width = 0;
        _glyphCount = 0;
        _clusterCount = 0;
        _segmentCount = 0;
        MissingGlyphCount = 0;
        FirstMissingGlyph = null;
    }

    internal void AddGlyph(int glyphId, float advance, float xOffset, float yOffset, int clusterStart)
    {
        if (_glyphCount == _glyphIds.Length)
        {
            int n = Grow(_glyphCount + 1, 16);
            Array.Resize(ref _glyphIds, n);
            Array.Resize(ref _x, n);
            Array.Resize(ref _y, n);
            Array.Resize(ref _advance, n);
            Array.Resize(ref _cluster, n);
        }

        _glyphIds[_glyphCount] = glyphId;
        _x[_glyphCount] = xOffset;
        _y[_glyphCount] = yOffset;
        _advance[_glyphCount] = advance;
        _cluster[_glyphCount] = clusterStart;
        _glyphCount++;
    }

    internal void AddCluster(int textStart, int textLength, int glyphStart, int glyphCount, float advance, bool missing)
    {
        if (_clusterCount == _clusters.Length)
        {
            Array.Resize(ref _clusters, Grow(_clusterCount + 1, 8));
        }

        _clusters[_clusterCount++] = new GlyphCluster(textStart, textLength, glyphStart, glyphCount, advance, missing);
    }

    internal void AddSegment(ResolvedFace face, int glyphStart, int glyphCount, int textStart, int textLength, int clusterStart, int clusterCount, float advance)
    {
        if (_segmentCount == _segments.Length)
        {
            Array.Resize(ref _segments, Grow(_segmentCount + 1, 4));
        }

        _segments[_segmentCount++] = new GlyphRunSegment(face, glyphStart, glyphCount, textStart, textLength, clusterStart, clusterCount, advance);
    }

    internal void NoteMissing(in TextDiagnostic diagnostic)
    {
        MissingGlyphCount++;
        FirstMissingGlyph ??= diagnostic;
    }

    // Turns the logical-order arrays into display order, then places the glyphs: x becomes the pen position plus the glyph's offset.
    internal void Finish(bool reverse)
    {
        int n = _glyphCount;
        int c = _clusterCount;
        int s = _segmentCount;
        if (reverse)
        {
            Array.Reverse(_glyphIds, 0, n);
            Array.Reverse(_x, 0, n);
            Array.Reverse(_y, 0, n);
            Array.Reverse(_advance, 0, n);
            Array.Reverse(_cluster, 0, n);
            Array.Reverse(_clusters, 0, c);
            for (int i = 0; i < c; i++)
            {
                GlyphCluster k = _clusters[i];
                _clusters[i] = new GlyphCluster(k.TextStart, k.TextLength, n - (k.GlyphStart + k.GlyphCount), k.GlyphCount, k.Advance, k.HasMissingGlyph);
            }

            Array.Reverse(_segments, 0, s);
            for (int i = 0; i < s; i++)
            {
                GlyphRunSegment g = _segments[i];
                _segments[i] = new GlyphRunSegment(g.Face, n - (g.GlyphStart + g.GlyphCount), g.GlyphCount, g.TextStart, g.TextLength, c - (g.ClusterStart + g.ClusterCount), g.ClusterCount, g.Advance);
            }
        }

        float pen = 0;
        for (int i = 0; i < n; i++)
        {
            _x[i] += pen;
            pen += _advance[i];
        }

        Width = pen;
    }

    private static int Grow(int needed, int min)
    {
        int size = Math.Max(min, needed);
        // Round up to a power of two so repeated reuse settles on one size.
        size--;
        size |= size >> 1;
        size |= size >> 2;
        size |= size >> 4;
        size |= size >> 8;
        size |= size >> 16;
        return size + 1;
    }
}
