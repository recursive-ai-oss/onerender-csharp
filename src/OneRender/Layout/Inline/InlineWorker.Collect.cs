using System.Globalization;
using System.Threading;
using OneFont;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// The state and algorithm for laying out one inline formatting context (one block container's inline content as one paragraph).
/// Split over several files: collection and white space (this file), shaping and break opportunities, the line scanner, and
/// line construction with fragments.
/// </summary>
internal sealed partial class InlineWorker
{
    // Per-character flags of the processed paragraph text.
    private const byte CfCollapsible = 1; // a U+0020 produced by collapsible white space; removed at line start and end (CSS Text 3 §4.1.2)
    private const byte CfPreserved = 2;   // a preserved U+0020; hangs at the end of a wrapping line unless break-spaces
    private const byte CfShy = 4;         // a soft hyphen (U+00AD, removed from the text) follows this character
    private const byte CfWordSep = 8;     // a word-separator character, which word-spacing and justification act on (CSS Text 3 §8.1)
    private const byte CfBreakSpace = 16; // a preserved space under break-spaces: a break may follow every one

    // Bounds checked before the work they guard.
    private const int MaxChars = 1 << 22;     // processed characters of one paragraph; more are dropped
    private const int MaxChunk = 1024;        // longest text shaped as one run, so the shaped-run cache can hold every run

    private enum EK : byte
    {
        Text,
        Open,
        Close,
        Atomic,
        Tab,
        Newline,
        Float,
    }

    [Flags]
    private enum EF : byte
    {
        None = 0,
        Wrap = 1,
        Emergency = 2,
        EmergencyMeasure = 4,
        Rtl = 8,
        Br = 16,
        WsPercent = 32,
        NoEdge = 64,
        Abs = 128,
    }

    private struct Elem
    {
        public EK Kind;
        public EF Flags;
        public byte Level;
        public int Box;      // text box, inline box, atomic box or line-break box
        public int Owner;    // the inline box whose fragment holds this (the container's own index when none)
        public int Start;    // first character in the paragraph text
        public int Len;
        public double W;     // Open/Close: margin + border + padding; Atomic: margin-box width; Tab: width of one tab stop unit
        public double W2;    // Atomic in measuring: max-content contribution (W holds min-content)
        public double Outer; // Open/Close: the margin part of W
        public double H;     // Atomic: margin-box height
        public double Base;  // Atomic: distance from the margin-box top to the baseline
        public double Ml;    // Atomic: left margin
        public double Mt;    // Atomic: top margin
        public double Ls;    // letter-spacing
        public double Ws;    // word-spacing (px, or percent when WsPercent)
        public int Frag;     // Atomic: fragment index, -1 when none
    }

    private struct CFrame
    {
        public int Box;
        public int Next;
    }

    private BoxTree _boxes = null!;
    private IFontProvider _fonts = null!;
    private IAtomicInlineHost? _host;
    private CancellationToken _ct;
    private Box _container;
    private FragmentTree _out = null!;
    private Fragment _parent;
    private double _availW;
    private InlineLayoutRequest _req;
    private bool _layout;
    private int _tick;

    // Paragraph text after white-space processing, with per-character data.
    private char[] _t = new char[256];
    private int[] _src = new int[256];
    private byte[] _cf = new byte[256];
    private byte[] _lv = new byte[256];
    private byte[] _opp = new byte[257];
    private byte[] _gs = new byte[257];
    private int _n;
    private bool _prevSpace;
    private int _paraLevel;

    private Elem[] _raw = new Elem[32];
    private int _rawN;
    private Elem[] _el = new Elem[32];
    private ShapedText?[] _shaped = new ShapedText?[32];
    private int _elN;
    private CFrame[] _cs = new CFrame[16];
    private int _sp;
    private int[] _bdepth = [];

    // ------------------------------------------------------------------ collection

    private ref readonly ComputedValues V(int box) => ref _boxes.Make(box).Values;

    private void ResetState(Box container, IFontProvider fonts, IAtomicInlineHost? host, bool layout, CancellationToken ct)
    {
        _boxes = container.Tree!;
        _container = container;
        _fonts = fonts;
        _host = host;
        _ct = ct;
        _layout = layout;
        _n = 0;
        _rawN = 0;
        _elN = 0;
        _prevSpace = false;
        _tick = 0;
        if (_bdepth.Length < _boxes.Count)
        {
            _bdepth = new int[Math.Max(_boxes.Count, _bdepth.Length * 2)];
        }
    }

    private void GrowText(int need)
    {
        if (need <= _t.Length)
        {
            return;
        }

        int size = (int)Math.Min(Array.MaxLength, Math.Max((long)_t.Length * 2, need));
        Array.Resize(ref _t, size);
        Array.Resize(ref _src, size);
        Array.Resize(ref _cf, size);
        Array.Resize(ref _lv, size);
        _opp = new byte[size + 1];
        _gs = new byte[size + 1];
    }

    private int Put(char c, int src, byte flags)
    {
        if (_n + 1 > _t.Length)
        {
            GrowText(_n + 1);
        }

        int p = _n++;
        _t[p] = c;
        _src[p] = src;
        _cf[p] = flags;
        return p;
    }

    private ref Elem AddRaw(EK kind, int box, int owner, int start, int len)
    {
        if (_rawN == _raw.Length)
        {
            Array.Resize(ref _raw, _raw.Length * 2);
        }

        ref Elem e = ref _raw[_rawN++];
        e = default;
        e.Kind = kind;
        e.Box = box;
        e.Owner = owner;
        e.Start = start;
        e.Len = len;
        e.Frag = -1;
        return ref e;
    }

    private static double Edge(Length l, double basis) => l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0;

    private static bool IsSpaceChar(char c) => c is ' ' or '\u00A0';

    /// <summary>
    /// Walks the container's inline-level descendants in tree order with an explicit stack and builds the paragraph: the processed text
    /// (CSS Text 3 §4.1 white-space processing), and one raw element per run of text, inline-box edge, atomic inline, tab and forced break.
    /// </summary>
    private void Collect()
    {
        Box container = _container;
        ref readonly ComputedValues cv = ref container.Values;
        if (cv.UnicodeBidi is UnicodeBidiKind.BidiOverride or UnicodeBidiKind.IsolateOverride)
        {
            // CSS Writing Modes 3 §2.4: on the container the override applies to all of its content.
            Put(cv.Direction == DirectionKind.Rtl ? '\u202E' : '\u202D', -1, 0);
        }

        _sp = 0;
        PushFrame(container.Index, container.FirstChild.IsNull ? -1 : container.FirstChild.Index);
        _bdepth[container.Index] = 0;
        while (_sp > 0)
        {
            if ((++_tick & 255) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            int fb = _cs[_sp - 1].Box;
            int next = _cs[_sp - 1].Next;
            if (next < 0)
            {
                _sp--;
                if (_sp > 0)
                {
                    EmitClose(_boxes.Make(fb));
                }

                continue;
            }

            Box ch = _boxes.Make(next);
            _cs[_sp - 1].Next = ch.NextSibling.IsNull ? -1 : ch.NextSibling.Index;
            if (_n >= MaxChars)
            {
                continue;
            }

            BoxTraits flags = ch.Flags;
            switch (ch.Kind)
            {
                case BoxKind.Text:
                    AddText(ch, fb);
                    break;
                case BoxKind.Inline:
                    EmitOpen(ch, fb);
                    _bdepth[ch.Index] = _sp;
                    PushFrame(ch.Index, ch.FirstChild.IsNull ? -1 : ch.FirstChild.Index);
                    break;
                case BoxKind.LineBreak:
                {
                    int p = Put('\n', -1, 0);
                    AddRaw(EK.Newline, ch.Index, fb, p, 1).Flags = EF.Br;
                    _prevSpace = false;
                    break;
                }

                case BoxKind.Container or BoxKind.Replaced:
                    if ((flags & BoxTraits.OutsideMarker) != 0)
                    {
                        break;
                    }

                    if ((flags & BoxTraits.OutOfFlow) != 0)
                    {
                        if (ch.Values.Floating == FloatKind.None)
                        {
                            if (_layout && ch.Values.Position is PositionKind.Absolute or PositionKind.Fixed)
                            {
                                // The box takes no part in the line, but its static position is where it met the line (CSS2 §10.3.7).
                                AddRaw(EK.Float, ch.Index, fb, _n, 0).Flags = EF.Abs;
                            }

                            break;
                        }

                        if (_layout)
                        {
                            // A float is placed by the exclusion space when its line is known (CSS2 §9.5.1 rules 6 and 7).
                            AddRaw(EK.Float, ch.Index, fb, _n, 0);
                            break;
                        }

                        // Intrinsic sizes count a float like an atomic inline on the line (CSS Sizing 3 §5.2).
                        int fp = Put('￼', -1, 0);
                        AddRaw(EK.Atomic, ch.Index, fb, fp, 1);
                        _prevSpace = false;
                        break;
                    }

                    if (ch.Level == BoxLevel.Inline || (flags & BoxTraits.UnsplitBlockInInline) != 0)
                    {
                        // CSS Text 3 §5.1: an atomic inline takes part in line breaking as U+FFFC.
                        int p = Put('\uFFFC', -1, 0);
                        AddRaw(EK.Atomic, ch.Index, fb, p, 1);
                        _prevSpace = false;
                    }

                    break;
                default:
                    break; // markers inside the line are not laid out yet
            }
        }

        if (cv.UnicodeBidi is UnicodeBidiKind.BidiOverride or UnicodeBidiKind.IsolateOverride)
        {
            Put('\u202C', -1, 0);
        }
    }

    private void PushFrame(int box, int next)
    {
        if (_sp == _cs.Length)
        {
            Array.Resize(ref _cs, _cs.Length * 2);
        }

        _cs[_sp].Box = box;
        _cs[_sp].Next = next;
        _sp++;
    }

    // CSS Writing Modes 3 §2.2: unicode-bidi on an inline box becomes Unicode directional formatting characters around its content.
    private void PutBidiOpen(in ComputedValues v)
    {
        bool rtl = v.Direction == DirectionKind.Rtl;
        switch (v.UnicodeBidi)
        {
            case UnicodeBidiKind.Embed:
                Put(rtl ? '\u202B' : '\u202A', -1, 0);
                break;
            case UnicodeBidiKind.Isolate:
                Put(rtl ? '\u2067' : '\u2066', -1, 0);
                break;
            case UnicodeBidiKind.BidiOverride:
                Put(rtl ? '\u202E' : '\u202D', -1, 0);
                break;
            case UnicodeBidiKind.IsolateOverride:
                Put(rtl ? '\u2067' : '\u2066', -1, 0);
                Put(rtl ? '\u202E' : '\u202D', -1, 0);
                break;
            case UnicodeBidiKind.Plaintext:
                Put('\u2068', -1, 0);
                break;
            default:
                break;
        }
    }

    private void PutBidiClose(in ComputedValues v)
    {
        switch (v.UnicodeBidi)
        {
            case UnicodeBidiKind.Embed or UnicodeBidiKind.BidiOverride:
                Put('\u202C', -1, 0);
                break;
            case UnicodeBidiKind.Isolate or UnicodeBidiKind.Plaintext:
                Put('\u2069', -1, 0);
                break;
            case UnicodeBidiKind.IsolateOverride:
                Put('\u202C', -1, 0);
                Put('\u2069', -1, 0);
                break;
            default:
                break;
        }
    }

    private void EmitOpen(Box b, int owner)
    {
        ref readonly ComputedValues v = ref b.Values;
        bool rtl = v.Direction == DirectionKind.Rtl;
        double bw = _layout ? _availW : 0;
        double margin = Edge(rtl ? v.MarginRight : v.MarginLeft, bw);
        double inner = (rtl ? v.BorderRightWidth : v.BorderLeftWidth) + Math.Max(0, Edge(rtl ? v.PaddingRight : v.PaddingLeft, bw));
        ref Elem e = ref AddRaw(EK.Open, b.Index, b.Index, _n, 0);
        e.Outer = margin;
        e.W = margin + inner;
        if ((b.Flags & BoxTraits.Continuation) != 0)
        {
            // A later piece of an inline box split around a block continues the box: no start edge (CSS2 §9.2.1.1, slice).
            e.Outer = 0;
            e.W = 0;
            e.Flags |= EF.NoEdge;
        }
        PutBidiOpen(in v);
    }

    private void EmitClose(Box b)
    {
        ref readonly ComputedValues v = ref b.Values;
        PutBidiClose(in v);
        bool rtl = v.Direction == DirectionKind.Rtl;
        double bw = _layout ? _availW : 0;
        double margin = Edge(rtl ? v.MarginLeft : v.MarginRight, bw);
        double inner = (rtl ? v.BorderLeftWidth : v.BorderRightWidth) + Math.Max(0, Edge(rtl ? v.PaddingLeft : v.PaddingRight, bw));
        ref Elem e = ref AddRaw(EK.Close, b.Index, b.Index, _n, 0);
        e.Outer = margin;
        e.W = margin + inner;
        if (!b.NextPiece.IsNull)
        {
            e.Outer = 0;
            e.W = 0;
            e.Flags |= EF.NoEdge;
        }
    }

    /// <summary>CSS Text 3 §4.1: collapse, preserve and transform the white space of one text box into the paragraph text.</summary>
    private void AddText(Box tb, int owner)
    {
        ref readonly ComputedValues v = ref tb.Values;
        ReadOnlySpan<char> s = tb.Text;
        WhiteSpaceCollapseKind mode = v.WhiteSpaceCollapse;
        bool collapse = mode is WhiteSpaceCollapseKind.Collapse or WhiteSpaceCollapseKind.PreserveBreaks;
        bool keepBreaks = mode is WhiteSpaceCollapseKind.Preserve or WhiteSpaceCollapseKind.PreserveBreaks or WhiteSpaceCollapseKind.BreakSpaces;
        byte keepFlag = (byte)(CfPreserved | CfWordSep | (mode == WhiteSpaceCollapseKind.BreakSpaces ? CfBreakSpace : 0));
        int runStart = _n;
        for (int i = 0; i < s.Length && _n < MaxChars; i++)
        {
            char c = s[i];
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                bool segmentBreak = c == '\n';
                if (segmentBreak && keepBreaks)
                {
                    FlushRun(tb, owner, ref runStart);
                    int p = Put('\n', i, 0);
                    AddRaw(EK.Newline, tb.Index, owner, p, 1);
                    runStart = _n;
                    _prevSpace = false;
                    continue;
                }

                if (collapse || segmentBreak)
                {
                    // A segment break that is not preserved acts as a space (§4.1.1); a space after a collapsible space vanishes, even
                    // across element boundaries.
                    if (!_prevSpace)
                    {
                        Put(' ', i, (byte)(collapse ? CfCollapsible | CfWordSep : keepFlag));
                        _prevSpace = collapse;
                    }

                    continue;
                }

                // Preserved spaces and tabs.
                if (c == '\t')
                {
                    FlushRun(tb, owner, ref runStart);
                    int p = Put('\t', i, 0);
                    AddRaw(EK.Tab, tb.Index, owner, p, 1);
                    runStart = _n;
                }
                else
                {
                    Put(' ', i, keepFlag);
                }

                _prevSpace = false;
                continue;
            }

            _prevSpace = false;
            if (c == '\u00AD')
            {
                // A soft hyphen is an invisible break opportunity (CSS Text 3 §6.1); it is dropped from the text and remembered on the
                // character before it.
                if (_n > runStart)
                {
                    _cf[_n - 1] |= CfShy;
                }

                continue;
            }

            byte flags = c == '\u00A0' ? CfWordSep : (byte)0;
            if (v.TextTransform == TextTransformKind.None)
            {
                Put(c, i, flags);
            }
            else
            {
                PutTransformed(c, i, flags, v.TextTransform);
            }
        }

        FlushRun(tb, owner, ref runStart);
    }

    /// <summary>
    /// CSS Text 3 §2.1: <c>text-transform</c> by simple (one-to-one) case mapping, plus the one-to-many mapping of the German sharp s
    /// to <c>SS</c> for <c>uppercase</c>. Every output character keeps the source index of the character it came from. Mapping is
    /// per UTF-16 unit with the invariant culture, so it does not depend on the host's locale. <c>capitalize</c> puts the first
    /// letter of each word in upper case, where a word continues over letters, digits and an apostrophe between them.
    /// </summary>
    private void PutTransformed(char c, int src, byte flags, TextTransformKind kind)
    {
        switch (kind)
        {
            case TextTransformKind.Uppercase:
                if (c == '\u00DF')
                {
                    Put('S', src, flags);
                    Put('S', src, flags);
                }
                else
                {
                    Put(char.ToUpperInvariant(c), src, flags);
                }

                break;
            case TextTransformKind.Lowercase:
                Put(char.ToLowerInvariant(c), src, flags);
                break;
            case TextTransformKind.Capitalize:
                Put(char.IsLetter(c) && !InWord() ? char.ToUpperInvariant(c) : c, src, flags);
                break;
            default:
                Put(c, src, flags);
                break;
        }
    }

    /// <summary>Whether the paragraph text so far ends inside a word: a letter or digit, or an apostrophe that follows one.</summary>
    private bool InWord()
    {
        if (_n == 0)
        {
            return false;
        }

        char last = _t[_n - 1];
        if (char.IsLetterOrDigit(last))
        {
            return true;
        }

        return last is '\'' or '\u2019' && _n > 1 && char.IsLetterOrDigit(_t[_n - 2]);
    }

    private void FlushRun(Box tb, int owner, ref int runStart)
    {
        if (_n > runStart)
        {
            AddRaw(EK.Text, tb.Index, owner, runStart, _n - runStart);
        }

        runStart = _n;
    }
}
