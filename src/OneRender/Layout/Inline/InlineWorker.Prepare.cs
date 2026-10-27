using System.Globalization;
using OneFont;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class InlineWorker
{
    private const byte OppNone = 0;
    private const byte OppAllowed = 1;
    private const byte OppMandatory = 2;

    // ------------------------------------------------------------------ opportunities

    private static bool IsCjk(char c) =>
        c is >= '\u2E80' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF';

    private static bool IsLetterOrNumber(char c)
    {
        if (char.IsSurrogate(c))
        {
            return true;
        }

        UnicodeCategory k = CharUnicodeInfo.GetUnicodeCategory(c);
        return k is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;
    }

    private static bool NoBreakNeighbour(char c) => c is '\u2060' or '\u200D' or '\uFEFF' or '\u202F' or '\u2007' or '\u2011';

    /// <summary>
    /// Turns the UAX #14 opportunities of the paragraph into the opportunities CSS allows: <c>white-space</c> and
    /// <c>text-wrap-mode</c>, <c>word-break</c>, <c>line-break: anywhere</c>, soft hyphens and the break-spaces rule (CSS Text 3 §5).
    /// Each text element's own style governs the positions after its characters, so <c>white-space: nowrap</c> on an inline box
    /// stops breaks inside it and at its end.
    /// </summary>
    private void AdjustOpportunities()
    {
        int n = _n;
        for (int r = 0; r < _rawN; r++)
        {
            ref Elem e = ref _raw[r];
            switch (e.Kind)
            {
                case EK.Text:
                {
                    ref readonly ComputedValues v = ref V(e.Box);
                    bool wrap = v.TextWrapMode == TextWrapModeKind.Wrap;
                    int end = e.Start + e.Len;
                    for (int p = e.Start + 1; p <= end && p <= n; p++)
                    {
                        if (!wrap)
                        {
                            if (_opp[p] == OppAllowed)
                            {
                                _opp[p] = OppNone;
                            }

                            continue;
                        }

                        byte prev = _cf[p - 1];
                        if ((prev & CfShy) != 0 && v.Hyphens != HyphensKind.None)
                        {
                            _opp[p] = OppAllowed;
                        }

                        if ((prev & CfBreakSpace) != 0 && p < n)
                        {
                            // CSS Text 3 §5.1: break-spaces gives a break after every preserved space, including between two.
                            _opp[p] = OppAllowed;
                        }

                        if (p >= n || _gs[p] == 0)
                        {
                            continue;
                        }

                        char a = _t[p - 1];
                        char b = _t[p];
                        switch (v.WordBreak)
                        {
                            case WordBreakKind.KeepAll:
                                // CSS Text 3 §5.2: no breaks between letters of CJK text, only at spaces and punctuation.
                                if (_opp[p] == OppAllowed && IsCjk(a) && IsCjk(b))
                                {
                                    _opp[p] = OppNone;
                                }

                                break;
                            case WordBreakKind.BreakAll:
                                if (_opp[p] == OppNone && IsLetterOrNumber(a) && IsLetterOrNumber(b))
                                {
                                    _opp[p] = OppAllowed;
                                }

                                break;
                            default:
                                break;
                        }

                        if (v.LineBreak == LineBreakKind.Anywhere && _opp[p] == OppNone && a != '\u200D' && b != '\u200D')
                        {
                            _opp[p] = OppAllowed;
                        }
                    }

                    break;
                }

                case EK.Atomic:
                {
                    bool wrap = V(e.Owner).TextWrapMode == TextWrapModeKind.Wrap;
                    int p = e.Start;
                    // CSS Text 3 §5.1: a break is allowed before and after an atomic inline, unless a glue or joiner character is adjacent.
                    if (wrap)
                    {
                        if (p > 0 && !NoBreakNeighbour(_t[p - 1]))
                        {
                            _opp[p] = OppAllowed;
                        }

                        if (p + 1 < n && !NoBreakNeighbour(_t[p + 1]))
                        {
                            _opp[p + 1] = OppAllowed;
                        }
                    }
                    else
                    {
                        _opp[p] = OppNone;
                        _opp[p + 1] = OppNone;
                    }

                    break;
                }

                case EK.Newline:
                    _opp[e.Start + 1] = OppMandatory;
                    break;
                default:
                    break;
            }
        }

        // No break at the very start of the paragraph; the end is always a break.
        _opp[0] = OppNone;
        _opp[n] = OppMandatory;
    }

    // ------------------------------------------------------------------ elements: bidi, split, shape

    private void Prepare()
    {
        int n = _n;
        ReadOnlySpan<char> text = _t.AsSpan(0, n);
        ref readonly ComputedValues cv = ref _container.Values;
        bool rtl = cv.Direction == DirectionKind.Rtl;
        if (n > 0)
        {
            TextSegmentation.FillLineBreakOpportunities(text, _opp.AsSpan(0, n + 1));
            TextSegmentation.FillGraphemeStarts(text, _gs.AsSpan(0, n + 1));
        }
        else
        {
            _opp[0] = OppMandatory;
            _gs[0] = 1;
        }

        AdjustOpportunities();
        TextDirection dir = cv.UnicodeBidi == UnicodeBidiKind.Plaintext ? TextDirection.Auto : rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight;
        _paraLevel = rtl ? 1 : 0;
        if (n > 0)
        {
            TextShaping.GetBidiLevels(text, dir, _lv.AsSpan(0, n), out _paraLevel);
        }

        BuildElements();
    }

    private byte LevelAt(int p) => p >= 0 && p < _n ? _lv[p] : (byte)_paraLevel;

    private void AddElem(in Elem e, ShapedText? shaped)
    {
        if (_elN == _el.Length)
        {
            int size = _el.Length * 2;
            Array.Resize(ref _el, size);
            Array.Resize(ref _shaped, size);
        }

        _el[_elN] = e;
        _shaped[_elN] = shaped;
        _elN++;
    }

    private void BuildElements()
    {
        for (int r = 0; r < _rawN; r++)
        {
            if ((r & 255) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            Elem e = _raw[r];
            switch (e.Kind)
            {
                case EK.Text:
                    BuildText(in e);
                    break;
                case EK.Open or EK.Close:
                {
                    // A start edge takes the level of the content after it and an end edge the level of the content before it, so a
                    // box's edges stay next to its content when the line is reordered.
                    int p = e.Start;
                    e.Level = e.Kind == EK.Open ? LevelAt(p) : LevelAt(p - 1);
                    e.Flags |= (e.Level & 1) != 0 ? EF.Rtl : EF.None;
                    AddElem(in e, null);
                    break;
                }

                case EK.Atomic:
                    e.Level = _lv[e.Start];
                    e.Flags |= (e.Level & 1) != 0 ? EF.Rtl : EF.None;
                    PrepareAtomic(ref e);
                    AddElem(in e, null);
                    break;
                case EK.Float:
                    e.Level = LevelAt(e.Start);
                    AddElem(in e, null);
                    break;
                case EK.Tab:
                {
                    ref readonly ComputedValues v = ref V(e.Box);
                    e.Level = (byte)_paraLevel;
                    e.Ls = v.LetterSpacing.Kind == LengthKind.Px ? v.LetterSpacing.Value : 0;
                    e.Ws = v.WordSpacing.Kind == LengthKind.Px ? v.WordSpacing.Value : 0;
                    Length ts = v.TabSize;
                    if (ts.Kind == LengthKind.Px)
                    {
                        e.W = Math.Max(0, ts.Value);
                    }
                    else
                    {
                        // CSS Text 3 §7.3: a number is a multiple of the advance of a space, plus the spacing it carries.
                        ShapedText space = _fonts.Shape(" ", in v);
                        e.W = Math.Max(0, ts.Value) * (space.Width + e.Ls + e.Ws);
                    }

                    AddElem(in e, null);
                    break;
                }

                default:
                    e.Level = _lv[e.Start];
                    e.Flags |= (e.Level & 1) != 0 ? EF.Rtl : EF.None;
                    AddElem(in e, null);
                    break;
            }
        }
    }

    private void BuildText(in Elem raw)
    {
        ref readonly ComputedValues v = ref V(raw.Box);
        EF baseFlags = EF.None;
        if (v.TextWrapMode == TextWrapModeKind.Wrap)
        {
            baseFlags |= EF.Wrap;
            // CSS Text 3 §5.5: break-word and anywhere allow a break inside a word when nothing else on the line fits; word-break:
            // break-word is the same. Only anywhere (and break-word) changes the min-content size.
            if (v.OverflowWrap != OverflowWrapKind.Normal || v.WordBreak == WordBreakKind.BreakWord)
            {
                baseFlags |= EF.Emergency;
            }

            if (v.OverflowWrap == OverflowWrapKind.Anywhere || v.WordBreak == WordBreakKind.BreakWord)
            {
                baseFlags |= EF.EmergencyMeasure;
            }
        }

        double ls = v.LetterSpacing.Kind == LengthKind.Px ? v.LetterSpacing.Value : 0;
        double ws = 0;
        if (v.WordSpacing.Kind == LengthKind.Px)
        {
            ws = v.WordSpacing.Value;
        }
        else if (v.WordSpacing.Kind == LengthKind.Percent)
        {
            ws = v.WordSpacing.Value / 100;
            baseFlags |= EF.WsPercent;
        }

        int pos = raw.Start;
        int end = raw.Start + raw.Len;
        while (pos < end)
        {
            byte lvl = _lv[pos];
            int runEnd = pos + 1;
            int limit = Math.Min(end, pos + MaxChunk);
            while (runEnd < limit && _lv[runEnd] == lvl)
            {
                runEnd++;
            }

            if (runEnd == pos + MaxChunk && runEnd < end && _lv[runEnd] == lvl)
            {
                // Cut a long run at a break opportunity if one is near, otherwise at a grapheme boundary; each piece is shaped alone, so
                // shaping cost is linear and every piece fits the shaped-run cache.
                int cut = -1;
                int low = Math.Max(pos + 1, runEnd - 256);
                for (int q = runEnd; q >= low; q--)
                {
                    if (_opp[q] != OppNone && _gs[q] != 0)
                    {
                        cut = q;
                        break;
                    }
                }

                if (cut < 0)
                {
                    for (int q = runEnd; q > pos; q--)
                    {
                        if (_gs[q] != 0)
                        {
                            cut = q;
                            break;
                        }
                    }
                }

                if (cut > pos)
                {
                    runEnd = cut;
                }
            }

            Elem e = raw;
            e.Start = pos;
            e.Len = runEnd - pos;
            e.Level = lvl;
            e.Ls = ls;
            e.Ws = ws;
            bool rtl = (lvl & 1) != 0;
            e.Flags = baseFlags | (rtl ? EF.Rtl : EF.None);
            ShapedText st = _fonts.Shape(_t.AsSpan(pos, runEnd - pos), in v, rtl);
            AddElem(in e, st);
            pos = runEnd;
        }
    }

    private void PrepareAtomic(ref Elem e)
    {
        if (!_layout)
        {
            if (_host is null)
            {
                return;
            }

            _host.GetAtomicContribution(_boxes.Make(e.Box), out double min, out double max);
            e.W = min;
            e.W2 = max;
            return;
        }

        e.Frag = -1;
        if (_host is null)
        {
            return;
        }

        Box b = _boxes.Make(e.Box);
        Fragment f = _host.LayoutAtomic(b, _parent, _availW, out Insets m);
        if (f.IsNull)
        {
            return;
        }

        Rect bb = f.BorderBox;
        e.Frag = f.Index;
        // An atomic inline's margins are its own: auto is zero, and a specified width never turns the right margin into the
        // leftover space the way it does for a block (CSS2 §10.3.9).
        ref readonly ComputedValues bv = ref b.Values;
        m = new Insets(Edge(bv.MarginTop, _availW), Edge(bv.MarginRight, _availW), Edge(bv.MarginBottom, _availW), Edge(bv.MarginLeft, _availW));
        e.Ml = m.Left;
        e.Mt = m.Top;
        e.W = m.Left + bb.Width + m.Right;
        e.H = m.Top + bb.Height + m.Bottom;
        e.Base = e.H;
        if (b.Kind == BoxKind.Container && b.Values.OverflowX == OverflowKind.Visible && b.Values.OverflowY == OverflowKind.Visible)
        {
            // CSS2 §10.8.1: the baseline of an inline-block is that of its last line box in normal flow; with none, or with a
            // non-visible overflow, it is the bottom margin edge.
            double last = LastLineBaseline(f);
            if (!double.IsNaN(last))
            {
                e.Base = m.Top + last;
            }
        }
    }

    /// <summary>The y of the baseline of the last in-flow line box inside a fragment, from its border-box top; NaN when there is none.</summary>
    private double LastLineBaseline(Fragment root)
    {
        double best = double.NaN;
        Fragment cur = root.FirstChild;
        double off = 0;
        int guard = 0;
        while (!cur.IsNull)
        {
            if ((++guard & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            Rect r = cur.BorderBox;
            bool descend = false;
            if (cur.Kind == FragmentKind.Line)
            {
                best = off + r.Y + cur.Baseline;
            }
            else if (cur.Kind == FragmentKind.Box)
            {
                ref readonly ComputedValues v = ref cur.Box.Values;
                descend = !v.IsOutOfFlow && v.OverflowX == OverflowKind.Visible && v.OverflowY == OverflowKind.Visible
                    && cur.Box.Kind == BoxKind.Container && cur.Box.Level == BoxLevel.Block;
            }

            if (descend && !cur.FirstChild.IsNull)
            {
                off += r.Y;
                cur = cur.FirstChild;
                continue;
            }

            while (true)
            {
                if (!cur.NextSibling.IsNull)
                {
                    cur = cur.NextSibling;
                    break;
                }

                cur = cur.Parent;
                if (cur.IsNull || cur == root)
                {
                    return best;
                }

                off -= cur.BorderBox.Y;
            }
        }

        return best;
    }
}
