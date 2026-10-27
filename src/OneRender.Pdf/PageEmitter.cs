using System.Numerics;
using OnePdf;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>
/// Draws the part of a display list that falls on one page (or inside one form XObject) into a PDF content builder. The list's
/// coordinate system (CSS px, y down) is the user space inside the page-level transform, so every item is drawn with its own numbers
/// and the scale to points, the flip and the margins live in one <c>cm</c> at the top of the page.
/// </summary>
internal sealed partial class PageEmitter
{
    // PDF viewers guarantee 28 nested graphics states (ISO 32000-2 Annex C.2); OnePdf enforces the same. The page itself, the
    // per-leaf state and clip/gradient/shadow helpers inside a leaf need a few, so scopes stop short of the limit.
    private const int MaxScopeDepth = 21;
    private const int MaxFormDepth = 6;

    private enum Closer : byte
    {
        None,
        Restore,
        InverseTransform,
        Alpha,
        MaskRestore,
    }

    private struct Scope
    {
        public Closer Closer;
        public Matrix3x2 Inverse;
        public float PrevAlpha;
        public BlendMode PrevBlend;
        public byte TagKind;
    }

    private readonly PdfShared _s;
    private readonly DisplayList _list;
    private readonly bool[] _mark;
    private readonly PdfContentBuilder _b;
    private readonly PdfStreamPageBuilder? _page;
    private readonly int _formDepth;
    private TagRef[] _chain = new TagRef[16];
    private int _chainN;
    private readonly Matrix3x2 _toPoints;
    private Scope[] _scopes = new Scope[16];
    private int _sp;
    private int _depth;
    private int _artifactTags;
    private bool _mcOpen;
    private int _mcKind;
    private PdfStructureElement? _mcElement;
    private float _alpha = 1f;
    private BlendMode _blend = BlendMode.Normal;
    private bool _warnedDepth;
    private bool _warnedBlend;
    private readonly CancellationToken _token;
    private int _steps;

    /// <param name="shared">Document state.</param>
    /// <param name="list">The list.</param>
    /// <param name="mark">Which items to draw: the visible leaves and every push around them.</param>
    /// <param name="builder">Where to write.</param>
    /// <param name="page">The page builder when structure is written; null inside forms.</param>
    /// <param name="formDepth">0 on a page, one more inside each nested form.</param>
    /// <param name="toPoints">Maps list space to page points, for link rectangles.</param>
    /// <param name="token">Cancellation.</param>
    public PageEmitter(PdfShared shared, DisplayList list, bool[] mark, PdfContentBuilder builder, PdfStreamPageBuilder? page, int formDepth, in Matrix3x2 toPoints, CancellationToken token)
    {
        _s = shared;
        _list = list;
        _mark = mark;
        _b = builder;
        _page = page;
        _formDepth = formDepth;
        _toPoints = toPoints;
        _token = token;
    }

    private bool Tagging => _page is not null && _s.Tags.Enabled;

    /// <summary>The graphics-state levels this emitter has open (the caller's own page-level state is not counted).</summary>
    public int Depth => _depth;

    /// <summary>Closes marked content and every scope still open.</summary>
    public void Finish()
    {
        CloseMc();
        while (_sp > 0)
        {
            PopScope();
        }
    }

    public void EmitRange(int from, int to)
    {
        int i = from;
        while (i < to)
        {
            if ((++_steps & 1023) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            if (!_mark[i])
            {
                DisplayItem skip = _list[i];
                i = IsPush(skip.Kind) ? skip.MatchIndex + 1 : i + 1;
                continue;
            }

            DisplayItem it = _list[i];
            switch (it.Kind)
            {
                case ItemKind.FillRect:
                    FillRectItem(in it);
                    break;
                case ItemKind.FillRoundedRect:
                    FillShape(new Shape(it.Rect, it.Radii), it.Paint, in it);
                    break;
                case ItemKind.FillPath:
                    FillShape(new Shape(it.PathVerbs, it.PathCoords, it.FillRule == FillRule.EvenOdd), it.Paint, in it);
                    break;
                case ItemKind.Border:
                    BorderItem(in it);
                    break;
                case ItemKind.BoxShadow:
                    ShadowItem(in it, i);
                    break;
                case ItemKind.Image:
                    ImageItem(in it);
                    break;
                case ItemKind.GlyphRun:
                    GlyphRunItem(in it, i);
                    break;
                case ItemKind.SvgImage:
                case ItemKind.BackdropFilter:
                    Island(i, i, it.Kind == ItemKind.SvgImage ? "inline SVG" : "backdrop-filter");
                    break;
                case ItemKind.PushClipRect:
                case ItemKind.PushClipRoundedRect:
                case ItemKind.PushClipPath:
                    PushClip(in it);
                    break;
                case ItemKind.PushTransform:
                    i = PushTransform(in it, i);
                    continue;
                case ItemKind.PushTransform3D:
                    i = PushTransform3D(in it, i);
                    continue;
                case ItemKind.PushGroup:
                    i = PushGroup(in it, i, it.Opacity, it.Blend);
                    continue;
                case ItemKind.PushFilter:
                    i = PushFilter(in it, i);
                    continue;
                case ItemKind.PushMask:
                    i = PushMask(in it, i);
                    continue;
                case ItemKind.BeginTag:
                    BeginTag(in it);
                    break;
                case ItemKind.EndTag:
                    EndTag();
                    break;
                case ItemKind.PopClip:
                case ItemKind.PopTransform:
                case ItemKind.PopGroup:
                case ItemKind.PopFilter:
                case ItemKind.PopMask:
                case ItemKind.PopMaskLayer:
                    PopScope();
                    break;
            }

            i++;
        }
    }

    internal static bool IsPush(ItemKind k) =>
        k is ItemKind.PushClipRect or ItemKind.PushClipRoundedRect or ItemKind.PushClipPath or ItemKind.PushTransform or ItemKind.PushGroup or ItemKind.BeginTag
            or ItemKind.PushTransform3D or ItemKind.PushFilter or ItemKind.PushMask or ItemKind.PushMaskLayer;

    // ------------------------------------------------------------------ scopes

    private ref Scope Open(Closer closer)
    {
        if (_sp == _scopes.Length)
        {
            Array.Resize(ref _scopes, _sp * 2);
        }

        ref Scope s = ref _scopes[_sp++];
        s = default;
        s.Closer = closer;
        return ref s;
    }

    private void PopScope()
    {
        if (_sp == 0)
        {
            return;
        }

        ref Scope s = ref _scopes[--_sp];
        if (s.TagKind != 0)
        {
            return;
        }

        switch (s.Closer)
        {
            case Closer.Restore:
            case Closer.MaskRestore:
                CloseMc();
                _b.Restore();
                _depth--;
                break;
            case Closer.InverseTransform:
                CloseMc();
                _b.Transform(s.Inverse.M11, s.Inverse.M12, s.Inverse.M21, s.Inverse.M22, s.Inverse.M31, s.Inverse.M32);
                break;
            case Closer.Alpha:
                _alpha = s.PrevAlpha;
                _blend = s.PrevBlend;
                break;
        }
    }

    private bool CanOpen()
    {
        if (_depth < MaxScopeDepth)
        {
            return true;
        }

        if (!_warnedDepth)
        {
            _warnedDepth = true;
            _s.Diagnostics.Add(DiagnosticKind.LimitExceeded, "clips and transforms nest deeper than a PDF viewer guarantees (28 graphics states); the innermost are dropped");
        }

        return false;
    }

    private void PushClip(in DisplayItem it)
    {
        if (!CanOpen())
        {
            Open(Closer.None);
            return;
        }

        CloseMc();
        _b.Save();
        _depth++;
        Open(Closer.Restore);
        switch (it.Kind)
        {
            case ItemKind.PushClipRect:
                {
                    RectF r = it.Rect;
                    if (r.IsEmpty)
                    {
                        _b.Rectangle(0, 0, 0, 0);
                    }
                    else
                    {
                        _b.Rectangle(r.X, r.Y, r.Width, r.Height);
                    }

                    break;
                }

            case ItemKind.PushClipRoundedRect:
                {
                    RectF r = it.Rect;
                    if (r.IsEmpty)
                    {
                        _b.Rectangle(0, 0, 0, 0);
                    }
                    else
                    {
                        Geo.RoundedRect(_b, r.X, r.Y, r.Width, r.Height, it.Radii);
                    }

                    break;
                }

            default:
                Geo.Verbs(_b, it.PathVerbs, it.PathCoords);
                if (it.PathVerbs.IsEmpty)
                {
                    _b.Rectangle(0, 0, 0, 0);
                }

                break;
        }

        _b.Clip(it.Kind == ItemKind.PushClipPath && it.FillRule == FillRule.EvenOdd);
    }

    private int PushTransform(in DisplayItem it, int index)
    {
        Matrix3x2 m = it.Matrix;
        return ApplyTransform(m, index, it.MatchIndex);
    }

    private int PushTransform3D(in DisplayItem it, int index)
    {
        // A projection with perspective cannot be written as a PDF matrix. The plane is drawn flat with the affine part of the
        // matrix (what the builder does for a transform without perspective) and the loss is reported.
        Matrix4x4 m = it.Matrix4;
        _s.Diagnostics.Add(DiagnosticKind.Unsupported, "perspective projection (item " + it.Id.Value + ") is flattened to its affine part in PDF");
        float w = m.M44 == 0f ? 1f : m.M44;
        var flat = new Matrix3x2(m.M11 / w, m.M12 / w, m.M21 / w, m.M22 / w, m.M41 / w, m.M42 / w);
        return ApplyTransform(flat, index, it.MatchIndex);
    }

    private int ApplyTransform(Matrix3x2 m, int index, int match)
    {
        float det = (m.M11 * m.M22) - (m.M12 * m.M21);
        if (!float.IsFinite(det) || MathF.Abs(det) < 1e-12f)
        {
            return match + 1; // a singular matrix flattens its content to nothing
        }

        if (!CanOpen())
        {
            if (!Matrix3x2.Invert(m, out Matrix3x2 inv))
            {
                return match + 1;
            }

            CloseMc();
            _b.Transform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
            ref Scope t = ref Open(Closer.InverseTransform);
            t.Inverse = inv;
            return index + 1;
        }

        CloseMc();
        _b.Save();
        _depth++;
        Open(Closer.Restore);
        _b.Transform(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
        return index + 1;
    }

    // ------------------------------------------------------------------ tags

    private void BeginTag(in DisplayItem it)
    {
        ref Scope s = ref Open(Closer.None);
        _chainVersion++;
        if (it.Role == TagRole.Artifact)
        {
            s.TagKind = 1;
            _artifactTags++;
        }
        else
        {
            s.TagKind = 2;
            if (_chainN == _chain.Length)
            {
                Array.Resize(ref _chain, _chainN * 2);
            }

            _chain[_chainN++] = new TagRef(it.Id.Value >> 16, it.Role, it.AltText, it.LinkTarget, it.Language);
        }
    }

    private void EndTag()
    {
        // Tags and the other scopes nest strictly, so the tag being closed is the top of the stack.
        if (_sp == 0 || _scopes[_sp - 1].TagKind == 0)
        {
            return;
        }

        byte kind = _scopes[--_sp].TagKind;
        _chainVersion++;
        if (kind == 1)
        {
            _artifactTags--;
        }
        else if (_chainN > 0)
        {
            _chainN--;
        }
    }

    private int _chainVersion;
    private int _mcChainVersion;

    private void CloseMc()
    {
        if (_mcOpen)
        {
            _b.EndMarkedContent();
            _mcOpen = false;
            _mcElement = null;
        }
    }

    /// <summary>
    /// Opens (or continues) the marked-content sequence a leaf belongs to. <paramref name="content"/> leaves (text, images) belong to
    /// the innermost open structure element; decoration (fills, borders, shadows) is an artifact (ISO 32000-2 14.8.2.2).
    /// </summary>
    private void BeginLeaf(bool content, in RectF rootBounds, ReadOnlySpan<char> text = default)
    {
        if (!Tagging)
        {
            return;
        }

        PdfStreamPageBuilder page = _page!;
        if (!content || _artifactTags > 0)
        {
            if (_mcOpen && _mcKind == 2)
            {
                return;
            }

            CloseMc();
            page.BeginArtifact();
            _mcOpen = true;
            _mcKind = 2;
            return;
        }

        // Same chain as the sequence that is open: keep going.
        if (_mcOpen && _mcKind == 1 && _mcChainVersion == _chainVersion)
        {
            NoteLink(in rootBounds, text);
            return;
        }

        CloseMc();
        PdfStructureElement? el = _s.Tags.Reconcile(new ReadOnlySpan<TagRef>(_chain, 0, _chainN), page);
        if (el is null)
        {
            page.BeginArtifact();
            _mcOpen = true;
            _mcKind = 2;
            return;
        }

        page.BeginTaggedContent(el);
        _mcOpen = true;
        _mcKind = 1;
        _mcElement = el;
        _mcChainVersion = _chainVersion;
        NoteLink(in rootBounds, text);
    }

    private void NoteLink(in RectF root, ReadOnlySpan<char> text)
    {
        if (root.IsEmpty)
        {
            return;
        }

        RectF r = root.Transform(_toPoints);
        // Page space is y up: the transform has already flipped y, so the rectangle's top edge is its larger y.
        _s.Tags.NoteContent(r.X, r.Y, r.Right, r.Bottom, text);
    }

    // ------------------------------------------------------------------ state for one leaf

    /// <summary>Opens a graphics state with the opacity and blend mode in force; returns whether one was opened.</summary>
    private bool BeginAlpha(float colorAlpha)
    {
        float a = colorAlpha * _alpha;
        if (!(a < 0.9995f) && _blend == BlendMode.Normal)
        {
            return false;
        }

        _b.Save();
        _b.SetFillOpacity(Math.Clamp(a, 0f, 1f));
        _b.SetStrokeOpacity(Math.Clamp(a, 0f, 1f));
        if (_blend != BlendMode.Normal)
        {
            PdfBlendMode m = Geo.Blend(_blend, out bool exact);
            if (!exact && !_warnedBlend)
            {
                _warnedBlend = true;
                _s.Diagnostics.Add(DiagnosticKind.Unsupported, "blend mode " + _blend + " has no operator in the PDF writer; drawn as normal");
            }

            _b.SetBlendMode(m);
        }

        return true;
    }

    private void EndAlpha(bool saved)
    {
        if (saved)
        {
            _b.Restore();
        }
    }
}
