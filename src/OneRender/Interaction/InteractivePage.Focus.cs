using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    private Node _navigationStart;
    private int _focusToken;

    private Node[] _order = new Node[64];
    private int[] _orderOrdinal = new int[64];
    private int[] _orderTab = new int[64];
    private long[] _sortKeys = new long[64];
    private int _orderCount;
    private int _positiveCount;
    private int _startOrdinal;

    /// <summary>Gives focus to an element, firing <c>blur</c>, <c>focusout</c>, <c>focus</c> and <c>focusin</c> in that order (UI Events §3.3.2).</summary>
    /// <param name="element">A focusable element (HTML §6.6.3: a rendered element with a valid <c>tabindex</c>, a link, a non-disabled form control and so on), or the null node to clear focus.</param>
    /// <param name="reason">How focus was requested; it decides <c>:focus-visible</c> (Selectors 4 §11). Default <see cref="FocusReason.Script"/>.</param>
    /// <param name="scrollIntoView">Whether to scroll the element into view (nearest) when it is not fully visible. Default true.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened; <see cref="InputOutcome.None"/> when the element is not focusable or already focused.</returns>
    /// <exception cref="OperationCanceledException">Cancelled while restyling.</exception>
    public InputOutcome Focus(Node element, FocusReason reason = FocusReason.Script, bool scrollIntoView = true, CancellationToken cancellationToken = default)
    {
        if (!element.IsNull && (!ReferenceEquals(element.Document, _page.Document) || !FocusRules.IsFocusable(element, _page.Styled)))
        {
            return InputOutcome.None;
        }

        InputOutcome outcome = SetFocus(element, reason, scrollIntoView);
        return outcome | FlushStates(cancellationToken);
    }

    /// <summary>Removes focus from the focused element (<c>blur()</c>).</summary>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened.</returns>
    public InputOutcome Blur(CancellationToken cancellationToken = default) => Focus(default, FocusReason.Script, false, cancellationToken);

    /// <summary>
    /// Moves focus along the sequential focus navigation order (HTML §6.6.5): elements with a positive <c>tabindex</c> in ascending order,
    /// then the others with <c>tabindex</c> 0 or none in tree order; negative values are skipped.
    /// </summary>
    /// <param name="forward">True for Tab, false for Shift+Tab.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened.</returns>
    public InputOutcome MoveFocus(bool forward, CancellationToken cancellationToken = default)
    {
        _lastInputWasPointer = false;
        Node from = _focused.IsNull ? _navigationStart : _focused;
        Node next = NextTabStop(from, forward);
        InputOutcome o = SetFocus(next, FocusReason.Keyboard, scroll: true);
        if (!next.IsNull)
        {
            _navigationStart = default;
        }

        return o | FlushStates(cancellationToken);
    }

    /// <summary>The elements in sequential navigation order right now (copying; for tools and tests, not for frames).</summary>
    /// <returns>The tab stops.</returns>
    public Node[] GetTabOrder()
    {
        BuildTabOrder(default);
        return _order.AsSpan(0, _orderCount).ToArray();
    }

    private Node FocusableAncestorOrSelf(Node n)
    {
        for (Node e = n; e.IsElement; e = e.Parent)
        {
            if (FocusRules.IsFocusable(e, _page.Styled))
            {
                return e;
            }
        }

        return default;
    }

    private InputOutcome SetFocus(Node target, FocusReason reason, bool scroll)
    {
        Node old = _focused;
        if (old.Equals(target))
        {
            return InputOutcome.None;
        }

        // Selectors 4 §11 / HTML §6.6.4: keyboard focus shows the ring; pointer focus only on text entry; script focus follows the
        // last kind of user input and whether the previous focus showed.
        bool visible = reason switch
        {
            FocusReason.Keyboard => true,
            FocusReason.PointingDevice => !target.IsNull && FocusRules.IsTextEntry(target),
            _ => !_lastInputWasPointer || _focusVisible,
        };

        int token = ++_focusToken;
        if (!old.IsNull)
        {
            _focused = default;
            _focusVisible = false;
            MarkDirty();
            EndEditing(old, _lastTime);
            FireFocus("blur", false, old, target);
            FireFocus("focusout", true, old, target);
            if (token != _focusToken)
            {
                return InputOutcome.FocusChanged;
            }
        }

        if (!target.IsNull)
        {
            _focused = target;
            _focusReason = reason;
            _focusVisible = visible;
            MarkDirty();
            BeginEditing(target, reason, _lastTime);
            FireFocus("focus", false, target, old);
            FireFocus("focusin", true, target, old);
            if (token != _focusToken)
            {
                return InputOutcome.FocusChanged;
            }
        }

        InputOutcome o = InputOutcome.FocusChanged;
        if (scroll && !target.IsNull)
        {
            o |= ScrollIntoViewCore(target);
        }

        return o;
    }

    private void FireFocus(string type, bool bubbles, Node target, Node related)
    {
        FocusEvent e = _fe;
        e.Reset(type, bubbles, false, _lastTime);
        e.Detail = 0;
        e.RelatedTarget = related.IsNull ? default : EventTarget.Of(related);
        _events.Dispatch(e, EventTarget.Of(target));
    }

    // ------------------------------------------------------------------ sequential navigation order

    private void BuildTabOrder(Node startPoint)
    {
        _orderCount = 0;
        _positiveCount = 0;
        _startOrdinal = -1;
        int zeroBase = 0;

        // Pass 1 collects every tab stop with its tabindex and tree ordinal into the work arrays; pass 2 orders them.
        StyledTree styled = _page.Styled;
        Document doc = _page.Document;
        int ordinal = 0;
        Node n = doc.Root.FirstChild;
        int count = 0;
        while (!n.IsNull)
        {
            bool descend = true;
            if (n.IsElement)
            {
                ordinal++;
                if (!startPoint.IsNull && n.Equals(startPoint))
                {
                    _startOrdinal = ordinal;
                }

                StyledNode s = styled.FindBySource(n.Index);
                if (s.IsNull || s.Values.Display.IsNone || FocusRules.HasAttribute(n, "inert"))
                {
                    descend = false;
                }
                else if (IsTabStop(n, in s.Values, out int tab))
                {
                    if (count == _order.Length)
                    {
                        Array.Resize(ref _order, count * 2);
                        Array.Resize(ref _orderOrdinal, count * 2);
                        Array.Resize(ref _orderTab, count * 2);
                        Array.Resize(ref _sortKeys, count * 2);
                    }

                    _order[count] = n;
                    _orderOrdinal[count] = ordinal;
                    _orderTab[count] = tab;
                    count++;
                }
            }

            // Next node in tree order without recursion.
            if (descend && !n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            while (!n.IsNull && n.NextSibling.IsNull)
            {
                n = n.Parent;
            }

            n = n.IsNull ? default : n.NextSibling;
        }

        // Positive tabindex first, ascending by value then tree order; then the zero group in tree order.
        int w = 0;
        for (int i = 0; i < count; i++)
        {
            if (_orderTab[i] > 0)
            {
                _sortKeys[w] = ((long)_orderTab[i] << 32) | (uint)i;
                w++;
            }
        }

        _positiveCount = w;
        Array.Sort(_sortKeys, 0, w);
        var sorted = new Node[count];
        var sortedOrd = new int[count];
        var sortedTab = new int[count];
        int outIndex = 0;
        for (int k = 0; k < w; k++)
        {
            int src = (int)(_sortKeys[k] & 0xFFFFFFFF);
            sorted[outIndex] = _order[src];
            sortedOrd[outIndex] = _orderOrdinal[src];
            sortedTab[outIndex] = _orderTab[src];
            outIndex++;
        }

        zeroBase = outIndex;
        for (int i = 0; i < count; i++)
        {
            if (_orderTab[i] == 0)
            {
                sorted[outIndex] = _order[i];
                sortedOrd[outIndex] = _orderOrdinal[i];
                sortedTab[outIndex] = 0;
                outIndex++;
            }
        }

        _orderCount = outIndex;
        _positiveCount = zeroBase;
        Array.Copy(sorted, _order, outIndex);
        Array.Copy(sortedOrd, _orderOrdinal, outIndex);
        Array.Copy(sortedTab, _orderTab, outIndex);
    }

    // HTML §6.6.3: sequentially focusable means focusable with a tabindex of 0 or more, or with none and focusable by default.
    private static bool IsTabStop(Node n, in ComputedValues v, out int tab)
    {
        tab = 0;
        if (v.Display.IsContents || v.Visibility != VisibilityKind.Visible)
        {
            return false;
        }

        bool formControl = FocusRules.IsHtml(n, "button") || FocusRules.IsHtml(n, "input") || FocusRules.IsHtml(n, "select") || FocusRules.IsHtml(n, "textarea");
        if (FocusRules.TryGetTabIndex(n, out int ti))
        {
            if (ti < 0 || (formControl && FocusRules.IsDisabled(n)))
            {
                return false;
            }

            tab = ti;
            return true;
        }

        return FocusRules.IsDefaultFocusable(n);
    }

    private Node NextTabStop(Node from, bool forward)
    {
        BuildTabOrder(from);
        int count = _orderCount;
        if (count == 0)
        {
            return default;
        }

        int idx = -1;
        if (!from.IsNull)
        {
            for (int i = 0; i < count; i++)
            {
                if (_order[i].Equals(from))
                {
                    idx = i;
                    break;
                }
            }
        }

        int next;
        if (idx >= 0)
        {
            next = forward ? idx + 1 : idx - 1;
        }
        else if (from.IsNull || _startOrdinal < 0)
        {
            next = forward ? 0 : count - 1;
        }
        else if (forward)
        {
            // Not itself a tab stop: continue from its place in tree order within the group of tabindex 0 / default stops.
            next = count;
            for (int i = _positiveCount; i < count; i++)
            {
                if (_orderOrdinal[i] > _startOrdinal)
                {
                    next = i;
                    break;
                }
            }
        }
        else
        {
            next = -1;
            for (int i = count - 1; i >= _positiveCount; i--)
            {
                if (_orderOrdinal[i] < _startOrdinal)
                {
                    next = i;
                    break;
                }
            }
        }

        if (next >= 0 && next < count)
        {
            return _order[next];
        }

        if (!_io.WrapTabNavigation)
        {
            return default;
        }

        return _order[forward ? 0 : count - 1];
    }
}
