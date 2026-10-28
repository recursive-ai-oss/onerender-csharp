using OneRender.Forms;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Selectors;

namespace OneRender.Interaction;

// Form controls (HTML §4.10): the default actions of clicks and keys on checkboxes, radio buttons, buttons, ranges and selects, and the
// events they fire. The document is read-only, so what the user changed lives in a FormState beside it that the page reads when it
// styles a control. The cascade's `:checked`, `:indeterminate`, `:disabled` and related selectors follow the same state: every change
// is passed to the live cascade as a form-state override (HTML §4.16.3), so a restyled control equals a page parsed with that state.
public sealed partial class InteractivePage
{
    private static readonly SelectorList InvalidSelector = SelectorList.Parse(":invalid");
    private static readonly SelectorList FormSelector = SelectorList.Parse("form");
    private static readonly SelectorList DisabledSelector = SelectorList.Parse(":disabled");

    private Node[] _formScratch = new Node[16];

    // ------------------------------------------------------------------ queries

    /// <summary>Whether a checkbox or radio button is checked now: what the user set, else the <c>checked</c> attribute.</summary>
    /// <param name="input">An <c>input</c> element.</param>
    /// <returns>True when checked; false for anything that is not a checkbox or radio button.</returns>
    public bool IsChecked(Node input)
    {
        if (!IsCheckable(input))
        {
            return false;
        }

        return _page.Form.TryGetChecked(input, out bool on) ? on : FocusRules.HasAttribute(input, "checked");
    }

    /// <summary>The value of a range input in its own units: what the user set, else the <c>value</c> attribute, else the midpoint.</summary>
    /// <param name="input">An <c>input type=range</c> element.</param>
    /// <returns>The value; 0 for anything else.</returns>
    public double GetRangeValue(Node input)
    {
        if (!IsRange(input))
        {
            return 0;
        }

        if (_page.Form.TryGetRangeValue(input, out double v))
        {
            return v;
        }

        FormValues.RangeSpec spec = RangeOf(input);
        return FormValues.TryParseNumber(DocumentSnapshots.Attr(input, "value"), out double a) ? spec.Snap(a) : spec.Default;
    }

    /// <summary>The index of the selected option of a drop-down or single-selection list (HTML §4.10.7 <c>selectedIndex</c>), or -1 when it has no options.</summary>
    /// <param name="select">A <c>select</c> element.</param>
    /// <returns>The index among the select's options.</returns>
    public int GetSelectedIndex(Node select)
    {
        int index = 0, chosen = -1, last = -1;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o), index++)
        {
            if (IsOptionSelected(o))
            {
                chosen = index;
                if (!FocusRules.HasAttribute(select, "multiple"))
                {
                    last = index;
                }
            }
        }

        if (last >= 0)
        {
            return last;
        }

        if (chosen >= 0)
        {
            return chosen;
        }

        // The selectedness setting algorithm: a drop-down with no selected option selects its first.
        return IsMultipleOrList(select) ? -1 : (index > 0 ? 0 : -1);
    }

    /// <summary>Whether a drop-down's list is showing.</summary>
    /// <param name="select">A <c>select</c> element.</param>
    /// <returns>True while it is open.</returns>
    public bool IsDropdownOpen(Node select) => _page.Form.IsOpen(select);

    // ------------------------------------------------------------------ programmatic changes

    /// <summary>
    /// Sets the checkedness of a checkbox or radio button as a script would (no events). Checking a radio button unchecks the others of
    /// its group.
    /// </summary>
    /// <param name="input">An <c>input</c> element.</param>
    /// <param name="value">The new checkedness.</param>
    /// <param name="cancellationToken">Cancels the restyle.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SetChecked(Node input, bool value, CancellationToken cancellationToken = default)
    {
        if (!IsCheckable(input))
        {
            return InputOutcome.None;
        }

        int n = 0;
        if (!SetCheckedCore(input, value, ref n))
        {
            return InputOutcome.None;
        }

        return ApplyFormChange(n, cancellationToken);
    }

    /// <summary>Whether a checkbox is in the indeterminate state its <c>indeterminate</c> property gives (HTML §4.10.5.1.15); a click or <see cref="SetChecked"/> clears it.</summary>
    /// <param name="input">An <c>input</c> element.</param>
    /// <returns>True for a checkbox set indeterminate; false for anything else.</returns>
    public bool IsIndeterminate(Node input) => IsCheckbox(input) && _page.Form.IsIndeterminate(input);

    /// <summary>Sets a checkbox's <c>indeterminate</c> property. The native look and the <c>:indeterminate</c> selector follow it.</summary>
    /// <param name="input">An <c>input type=checkbox</c> element.</param>
    /// <param name="value">The new state.</param>
    /// <param name="cancellationToken">Cancels the restyle.</param>
    /// <returns>What changed on the page; nothing for anything that is not a checkbox.</returns>
    public InputOutcome SetIndeterminate(Node input, bool value, CancellationToken cancellationToken = default)
    {
        if (!IsCheckbox(input) || _page.Form.IsIndeterminate(input) == value)
        {
            return InputOutcome.None;
        }

        _page.Form.SetIndeterminate(input, value);
        int n = 0;
        AddScratch(ref n, input);
        return ApplyFormChange(n, cancellationToken);
    }

    private static bool IsCheckbox(Node n) =>
        FocusRules.IsHtml(n, "input") && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) && t.Equals("checkbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets the value of a range input as a script would (no events): snapped to the step and clamped to the range.</summary>
    /// <param name="input">An <c>input type=range</c> element.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">Cancels the restyle.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SetRangeValue(Node input, double value, CancellationToken cancellationToken = default)
    {
        if (!IsRange(input) || double.IsNaN(value))
        {
            return InputOutcome.None;
        }

        double snapped = RangeOf(input).Snap(value);
        if (snapped.Equals(GetRangeValue(input)))
        {
            return InputOutcome.None;
        }

        _page.Form.SetRangeValue(input, snapped);
        int n = 0;
        AddScratch(ref n, input);
        return ApplyFormChange(n, cancellationToken);
    }

    /// <summary>Selects one option of a drop-down or list by index, as <c>selectedIndex</c> does (no events), deselecting the others.</summary>
    /// <param name="select">A <c>select</c> element.</param>
    /// <param name="index">The option's index; out of range selects nothing (a drop-down then shows nothing).</param>
    /// <param name="cancellationToken">Cancels the restyle.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SetSelectedIndex(Node select, int index, CancellationToken cancellationToken = default)
    {
        if (!FocusRules.IsHtml(select, "select"))
        {
            return InputOutcome.None;
        }

        int n = 0;
        return SelectCore(select, index, ref n) ? ApplyFormChange(n, cancellationToken) : InputOutcome.None;
    }

    // ------------------------------------------------------------------ the state changes themselves

    private static bool IsRange(Node n) =>
        FocusRules.IsHtml(n, "input") && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) && t.Equals("range", StringComparison.OrdinalIgnoreCase);

    private static bool IsRadio(Node n) =>
        FocusRules.IsHtml(n, "input") && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) && t.Equals("radio", StringComparison.OrdinalIgnoreCase);

    private static FormValues.RangeSpec RangeOf(Node input) =>
        FormValues.RangeSpec.From(DocumentSnapshots.Attr(input, "min"), DocumentSnapshots.Attr(input, "max"), DocumentSnapshots.Attr(input, "step"));

    private void AddScratch(ref int count, Node n)
    {
        if (count == _formScratch.Length)
        {
            Array.Resize(ref _formScratch, count * 2);
        }

        _formScratch[count++] = n;
    }

    // Sets checkedness, unchecking the rest of a radio group. Returns whether anything changed; the changed controls go to the scratch list.
    private bool SetCheckedCore(Node input, bool value, ref int count)
    {
        if (IsChecked(input) == value && !_page.Form.IsIndeterminate(input))
        {
            return false;
        }

        if (IsRadio(input))
        {
            if (!value)
            {
                // A radio group keeps its one checked button; unchecking it directly is allowed for scripts and changes only that button.
                _page.Form.SetChecked(input, false);
                AddScratch(ref count, input);
                return true;
            }

            foreach (Node other in RadioGroup(input))
            {
                if (!other.Equals(input) && IsChecked(other))
                {
                    _page.Form.SetChecked(other, false);
                    AddScratch(ref count, other);
                }
            }
        }

        _page.Form.SetChecked(input, value);
        AddScratch(ref count, input);
        return true;
    }

    // HTML §4.10.5.1.17: the radio buttons with the same name, form owner and tree root.
    private List<Node> RadioGroup(Node radio)
    {
        var group = new List<Node>();
        string? name = DocumentSnapshots.Attr(radio, "name");
        if (string.IsNullOrEmpty(name))
        {
            group.Add(radio);
            return group;
        }

        Node owner = FormOwner(radio);
        foreach (Node n in _page.Document.DocumentElement.Descendants)
        {
            if (n.IsElement && IsRadio(n) && string.Equals(DocumentSnapshots.Attr(n, "name"), name, StringComparison.Ordinal) && FormOwner(n).Equals(owner))
            {
                group.Add(n);
            }
        }

        return group;
    }

    private Node FormOwner(Node control)
    {
        string? id = DocumentSnapshots.Attr(control, "form");
        if (!string.IsNullOrEmpty(id))
        {
            Node f = DocumentSnapshots.Original(DocumentSnapshots.Of(_page.Document).GetElementById(id), _page.Document);
            return !f.IsNull && FocusRules.IsHtml(f, "form") ? f : default;
        }

        return DocumentSnapshots.Original(DocumentSnapshots.Current(control).Closest(FormSelector), _page.Document);
    }

    // Selects the option at `index` and deselects the others. Returns whether anything changed; the select and its options go to the scratch list.
    private bool SelectCore(Node select, int index, ref int count)
    {
        int i = 0;
        bool changed = false;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o), i++)
        {
            bool want = i == index;
            if (IsOptionSelected(o) != want)
            {
                _page.Form.SetSelected(o, want);
                AddScratch(ref count, o);
                changed = true;
            }
        }

        if (changed)
        {
            AddScratch(ref count, select);
        }

        return changed;
    }

    private bool IsOptionSelected(Node option) =>
        _page.Form.TryGetSelected(option, out bool on) ? on : FocusRules.HasAttribute(option, "selected");

    private static bool IsMultipleOrList(Node select) =>
        FocusRules.HasAttribute(select, "multiple") || (int.TryParse(DocumentSnapshots.Attr(select, "size"), out int s) && s > 1);

    private static Node FirstOption(Node select) => NextOptionFrom(select, select.FirstChild);

    private static Node NextOption(Node select, Node current)
    {
        Node next = current.NextSibling;
        if (next.IsNull && !current.Parent.IsNull && !current.Parent.Equals(select))
        {
            next = current.Parent.NextSibling;
        }

        return NextOptionFrom(select, next);
    }

    private static Node NextOptionFrom(Node select, Node cursor)
    {
        while (!cursor.IsNull)
        {
            if (cursor.IsElement)
            {
                if (FocusRules.IsHtml(cursor, "option"))
                {
                    return cursor;
                }

                if (FocusRules.IsHtml(cursor, "optgroup") && !cursor.FirstChild.IsNull)
                {
                    cursor = cursor.FirstChild;
                    continue;
                }
            }

            Node n = cursor.NextSibling;
            if (n.IsNull && !cursor.Parent.IsNull && !cursor.Parent.Equals(select))
            {
                n = cursor.Parent.NextSibling;
            }

            cursor = n;
        }

        return cursor;
    }

    private FormStateChange[] _formChanges = new FormStateChange[32];

    // Passes the state of the controls in the scratch list to the live cascade, as overrides of what their attributes say (null: the
    // attribute again, after a reset). Returns how many elements' styles changed; they are listed in the cascade's changed list.
    private void StyleFormState(int count)
    {
        if (_formChanges.Length < count * 2)
        {
            _formChanges = new FormStateChange[count * 4];
        }

        int n = 0;
        for (int i = 0; i < count; i++)
        {
            Node control = _formScratch[i];
            Node inSnapshot = InSnapshot(control);
            if (IsCheckable(control))
            {
                _formChanges[n++] = FormStateChange.Checked(inSnapshot, _page.Form.TryGetChecked(control, out bool on) ? on : null);
                _formChanges[n++] = FormStateChange.Indeterminate(inSnapshot, _page.Form.IsIndeterminate(control) ? true : null);
            }
            else if (FocusRules.IsHtml(control, "option"))
            {
                _formChanges[n++] = FormStateChange.Checked(inSnapshot, _page.Form.TryGetSelected(control, out bool selected) ? selected : null);
            }
        }

        LiveDocumentStyles live = EnsureLive();
        live.ApplyFormState(new ReadOnlySpan<FormStateChange>(_formChanges, 0, n));
        _viewStale = true;
    }

    // Brings the page up to date with the controls in the scratch list.
    private InputOutcome ApplyFormChange(int count, CancellationToken ct)
    {
        if (count == 0)
        {
            return InputOutcome.None;
        }

        StyleFormState(count);
        LiveDocumentStyles live = _live!;
        var span = new ReadOnlySpan<Node>(_formScratch, 0, count);
        _motionNow = Math.Max(_motionNow, _lastTime);
        if (live.HasInteractivePseudoElementRules || !_page.TryApplyFormChange(span, new StyleSource(live), live.ChangedElementIndices, ct))
        {
            _page = _page.Rebuild(CurrentView(), ct, _motionNow);
            _hits = NewHitTester(_page);
            _queries = new LayoutQueries(_page, _hits);
        }
        else
        {
            _hits.Rebuild();
        }

        _generation++;
        return InputOutcome.Relaid;
    }

    // ------------------------------------------------------------------ activation

    private static bool IsDisabledControl(Node n) => DocumentSnapshots.Current(n).Matches(DisabledSelector);

    private static bool IsSubmitter(Node n)
    {
        if (FocusRules.IsHtml(n, "button"))
        {
            return !FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) || t.Equals("submit", StringComparison.OrdinalIgnoreCase) || !(t.Equals("button", StringComparison.OrdinalIgnoreCase) || t.Equals("reset", StringComparison.OrdinalIgnoreCase));
        }

        return FocusRules.IsHtml(n, "input") && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> ty) && (ty.Equals("submit", StringComparison.OrdinalIgnoreCase) || ty.Equals("image", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsResetter(Node n) =>
        (FocusRules.IsHtml(n, "button") || FocusRules.IsHtml(n, "input")) && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) && t.Equals("reset", StringComparison.OrdinalIgnoreCase);

    // The element whose activation behavior a click on `target` runs (DOM §2.5, HTML §4.10): the nearest inclusive ancestor that has one.
    private static Node ActivationTarget(Node target)
    {
        for (Node n = target; n.IsElement; n = n.Parent)
        {
            if (IsCheckable(n) || IsSubmitter(n) || IsResetter(n) || FocusRules.IsHtml(n, "label"))
            {
                return n;
            }

            if (FocusRules.IsHtml(n, "a") || FocusRules.IsHtml(n, "summary") || FocusRules.IsHtml(n, "select") || FocusRules.IsHtml(n, "option"))
            {
                return default;
            }
        }

        return default;
    }

    /// <summary>
    /// Dispatches a click and runs the default action of the control it activates. A checkbox or radio button is changed before the event
    /// is dispatched (HTML legacy-pre-activation behavior) so listeners see the new state, and put back when a listener cancels the click.
    /// </summary>
    private bool DispatchClick(Node target, DomEvent click, ref InputOutcome outcome, CancellationToken ct)
    {
        Node activation = ActivationTarget(target);
        if (activation.IsNull || (FocusRules.IsHtml(activation, "label") ? false : IsDisabledControl(activation)))
        {
            bool ok = _events.Dispatch(click, EventTarget.Of(target));
            if (ok && activation.IsNull)
            {
                outcome |= SelectActivation(target, ct);
            }

            return ok;
        }

        bool wasChecked = false;
        bool toggles = IsCheckable(activation);
        Node[] radios = [];
        int revertCount = 0;
        if (toggles)
        {
            wasChecked = IsChecked(activation);
            bool wasIndeterminate = _page.Form.IsIndeterminate(activation);
            if (IsRadio(activation))
            {
                if (wasChecked)
                {
                    toggles = false;
                }
                else
                {
                    var others = new List<Node>();
                    foreach (Node r in RadioGroup(activation))
                    {
                        if (!r.Equals(activation) && IsChecked(r))
                        {
                            others.Add(r);
                        }
                    }

                    radios = [.. others];
                }
            }

            if (toggles)
            {
                int n = 0;
                if (IsRadio(activation))
                {
                    SetCheckedCore(activation, true, ref n);
                }
                else
                {
                    _page.Form.SetChecked(activation, !wasChecked);
                    AddScratch(ref n, activation);
                }

                revertCount = n;
                outcome |= ApplyFormChange(n, ct);
                _ = wasIndeterminate;
            }
        }

        bool notCanceled = _events.Dispatch(click, EventTarget.Of(target));
        if (toggles && !notCanceled)
        {
            int n = 0;
            if (IsRadio(activation))
            {
                _page.Form.SetChecked(activation, false);
                AddScratch(ref n, activation);
                foreach (Node r in radios)
                {
                    _page.Form.SetChecked(r, true);
                    AddScratch(ref n, r);
                }
            }
            else
            {
                _page.Form.SetChecked(activation, wasChecked);
                AddScratch(ref n, activation);
            }

            outcome |= ApplyFormChange(n, ct);
            _ = revertCount;
        }

        if (!notCanceled)
        {
            return false;
        }

        if (toggles)
        {
            FireSimple("input", true, false, activation, click.TimeStamp);
            FireSimple("change", true, false, activation, click.TimeStamp);
        }
        else if (FocusRules.IsHtml(activation, "label"))
        {
            Node control = LabeledControl(activation);
            if (!control.IsNull && !IsInclusive(activation, target, control))
            {
                var inner = new PointerEvent("click", true, true);
                DispatchClick(control, inner, ref outcome, ct);
            }
        }
        else if (IsSubmitter(activation))
        {
            outcome |= Submit(activation, ct);
        }
        else if (IsResetter(activation))
        {
            outcome |= Reset(activation, ct);
        }

        return true;
    }

    // A click that started inside the labeled control is already that control's click.
    private static bool IsInclusive(Node label, Node target, Node control)
    {
        for (Node n = target; !n.IsNull && !n.Equals(label); n = n.Parent)
        {
            if (n.Equals(control))
            {
                return true;
            }
        }

        return false;
    }

    // HTML §4.10.4: the labeled control is the element the for attribute names, else the first labelable descendant.
    private Node LabeledControl(Node label)
    {
        string? id = DocumentSnapshots.Attr(label, "for");
        if (!string.IsNullOrEmpty(id))
        {
            Node c = DocumentSnapshots.Original(DocumentSnapshots.Of(_page.Document).GetElementById(id), _page.Document);
            return !c.IsNull && IsLabelable(c) ? c : default;
        }

        foreach (Node n in label.Descendants)
        {
            if (n.IsElement && IsLabelable(n))
            {
                return n;
            }
        }

        return default;
    }

    private static bool IsLabelable(Node n)
    {
        if (FocusRules.IsHtml(n, "input"))
        {
            return !(FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t) && t.Equals("hidden", StringComparison.OrdinalIgnoreCase));
        }

        return FocusRules.IsHtml(n, "button") || FocusRules.IsHtml(n, "select") || FocusRules.IsHtml(n, "textarea") || FocusRules.IsHtml(n, "meter") || FocusRules.IsHtml(n, "progress");
    }

    private void FireSimple(string type, bool bubbles, bool cancelable, Node target, double time)
    {
        var e = new DomEvent(type, bubbles, cancelable);
        _events.Dispatch(e, EventTarget.Of(target));
    }

    // HTML §4.10.21.3: constraint validation first (unless novalidate), then the submit event. Nothing is navigated or sent.
    private InputOutcome Submit(Node submitter, CancellationToken ct)
    {
        Node form = FormOwner(submitter);
        if (form.IsNull)
        {
            return InputOutcome.None;
        }

        bool skip = FocusRules.HasAttribute(form, "novalidate") || FocusRules.HasAttribute(submitter, "formnovalidate");
        if (!skip)
        {
            bool invalid = false;
            foreach (Node n in form.Descendants)
            {
                if (n.IsElement && DocumentSnapshots.Current(n).Matches(InvalidSelector) && !FocusRules.IsHtml(n, "fieldset") && !FocusRules.IsHtml(n, "form"))
                {
                    invalid = true;
                    FireSimple("invalid", false, true, n, _lastTime);
                }
            }

            if (invalid)
            {
                return InputOutcome.None;
            }
        }

        var e = new SubmitEvent(EventTarget.Of(submitter));
        if (!_events.Dispatch(e, EventTarget.Of(form)))
        {
            return InputOutcome.DefaultPrevented;
        }

        return InputOutcome.None;
    }

    private InputOutcome Reset(Node resetter, CancellationToken ct)
    {
        Node form = FormOwner(resetter);
        if (form.IsNull)
        {
            return InputOutcome.None;
        }

        var e = new DomEvent("reset", true, true);
        if (!_events.Dispatch(e, EventTarget.Of(form)))
        {
            return InputOutcome.DefaultPrevented;
        }

        // Every control goes back to what its attributes say (HTML §4.10.21.4).
        var changed = new List<Node>();
        foreach (Node n in form.Descendants)
        {
            if (n.IsElement && (IsCheckable(n) || IsRange(n) || FocusRules.IsHtml(n, "select") || FocusRules.IsHtml(n, "option")))
            {
                changed.Add(n);
            }
        }

        _page.Form.ClearUnder(form);
        int count = 0;
        foreach (Node n in changed)
        {
            AddScratch(ref count, n);
        }

        return ApplyFormChange(count, ct);
    }
}

// Range inputs: dragging the thumb and the keyboard (HTML §4.10.5.1.13).
public sealed partial class InteractivePage
{
    private Node _dragRange;
    private double _dragStartValue;

    // Probe: the thumb is 16 px across and its center travels from 8 px inside the left edge to 8 px inside the right edge of the control.
    private InputOutcome RangeFromPointer(Node input, double x, CancellationToken ct)
    {
        Layout.Rect r = _queries.GetBoundingClientRect(input);
        double travel = Math.Max(1, r.Width - 16);
        FormValues.RangeSpec spec = RangeOf(input);
        double value = spec.FromFraction((x - (r.X + 8)) / travel);
        return ChangeRange(input, value, false, ct);
    }

    // Moves the value, firing `input` when it changed and, at the end of a key press, `change`.
    private InputOutcome ChangeRange(Node input, double value, bool fireChange, CancellationToken ct)
    {
        InputOutcome o = InputOutcome.None;
        if (!value.Equals(GetRangeValue(input)))
        {
            _page.Form.SetRangeValue(input, value);
            int n = 0;
            AddScratch(ref n, input);
            o |= ApplyFormChange(n, ct);
            FireSimple("input", true, false, input, _lastTime);
            if (fireChange)
            {
                FireSimple("change", true, false, input, _lastTime);
            }
        }

        return o;
    }

    private InputOutcome BeginRangeDrag(Node target, in PointerInput p, CancellationToken ct)
    {
        if (!IsRange(target) || IsDisabledControl(target))
        {
            return InputOutcome.None;
        }

        _dragRange = target;
        _dragStartValue = GetRangeValue(target);
        return RangeFromPointer(target, p.X, ct);
    }

    private InputOutcome ContinueRangeDrag(in PointerInput p, CancellationToken ct)
    {
        Node input = _dragRange;
        switch (p.Action)
        {
            case PointerAction.Move:
                return RangeFromPointer(input, p.X, ct);
            case PointerAction.Up:
            {
                _dragRange = default;
                InputOutcome o = RangeFromPointer(input, p.X, ct);
                if (!GetRangeValue(input).Equals(_dragStartValue))
                {
                    FireSimple("change", true, false, input, _lastTime);
                }

                return o;
            }

            case PointerAction.Cancel:
                _dragRange = default;
                return ChangeRange(input, _dragStartValue, false, ct);
            default:
                return InputOutcome.None;
        }
    }

    // Arrow keys move by one step, Page Up and Page Down by a tenth of the range, Home and End to the ends.
    private bool TryRangeKey(string key, CancellationToken ct, out InputOutcome outcome)
    {
        outcome = InputOutcome.None;
        Node input = _focused;
        if (!IsRange(input) || IsDisabledControl(input))
        {
            return false;
        }

        FormValues.RangeSpec spec = RangeOf(input);
        double step = spec.Step > 0 ? spec.Step : Math.Max((spec.Max - spec.Min) / 100, 1e-9);
        double cur = GetRangeValue(input), next;
        switch (key)
        {
            case "ArrowRight":
            case "ArrowUp": next = cur + step; break;
            case "ArrowLeft":
            case "ArrowDown": next = cur - step; break;
            case "PageUp": next = cur + Math.Max(step, (spec.Max - spec.Min) / 10); break;
            case "PageDown": next = cur - Math.Max(step, (spec.Max - spec.Min) / 10); break;
            case "Home": next = spec.Min; break;
            case "End": next = spec.Max; break;
            default: return false;
        }

        outcome = ChangeRange(input, spec.Snap(next), true, ct);
        return true;
    }
}

// Selects: the drop-down list and list boxes (HTML §4.10.7, §15.5.14).
public sealed partial class InteractivePage
{
    // The select an event target belongs to (the target is the select, an option, an optgroup or something inside the popup), and the option when there is one.
    private static Node SelectOfTarget(Node target, out Node option)
    {
        option = default;
        for (Node n = target; n.IsElement; n = n.Parent)
        {
            if (FocusRules.IsHtml(n, "option") && option.IsNull)
            {
                option = n;
            }
            else if (FocusRules.IsHtml(n, "select"))
            {
                return n;
            }
        }

        option = default;
        return default;
    }

    private static int IndexOfOption(Node select, Node option)
    {
        int i = 0;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o), i++)
        {
            if (o.Equals(option))
            {
                return i;
            }
        }

        return -1;
    }

    private static Node OptionAt(Node select, int index)
    {
        int i = 0;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o), i++)
        {
            if (i == index)
            {
                return o;
            }
        }

        return default;
    }

    private static int OptionCount(Node select)
    {
        int i = 0;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o))
        {
            i++;
        }

        return i;
    }

    // The next option from `from` in a direction that can be chosen (not disabled).
    private static int NextEnabledOption(Node select, int from, int step)
    {
        int count = OptionCount(select);
        for (int i = from + step; i >= 0 && i < count; i += step)
        {
            Node o = OptionAt(select, i);
            if (!o.IsNull && !IsDisabledControl(o))
            {
                return i;
            }
        }

        return -1;
    }

    private InputOutcome Rebuild(CancellationToken ct)
    {
        _motionNow = Math.Max(_motionNow, _lastTime);
        _page = _page.Rebuild(CurrentView(), ct, _motionNow);
        _hits = NewHitTester(_page);
        _queries = new LayoutQueries(_page, _hits);
        _generation++;
        return InputOutcome.Relaid;
    }

    private InputOutcome OpenDropdown(Node select, CancellationToken ct)
    {
        if (_page.Form.IsOpen(select) || IsDisabledControl(select))
        {
            return InputOutcome.None;
        }

        _page.Form.SetOpen(select, true);
        int sel = GetSelectedIndex(select);
        _page.Form.SetHighlight(sel >= 0 ? OptionAt(select, sel) : default);
        return Rebuild(ct);
    }

    private InputOutcome CloseDropdown(CancellationToken ct)
    {
        int open = _page.Form.OpenIndex;
        if (open < 0)
        {
            return InputOutcome.None;
        }

        _page.Form.SetOpen(_page.Document.GetNode(open), false);
        return Rebuild(ct);
    }

    // Moves the highlight inside the open popup, restyling the two options involved.
    private InputOutcome Highlight(Node select, Node option, CancellationToken ct)
    {
        int old = _page.Form.HighlightIndex;
        if (option.IsNull || old == option.Index)
        {
            return InputOutcome.None;
        }

        int n = 0;
        if (old >= 0)
        {
            AddScratch(ref n, _page.Document.GetNode(old));
        }

        _page.Form.SetHighlight(option);
        AddScratch(ref n, option);
        return ApplyFormChange(n, ct);
    }

    // After a click that no listener canceled: open or close a drop-down, or choose an option.
    private InputOutcome SelectActivation(Node target, CancellationToken ct)
    {
        Node select = SelectOfTarget(target, out Node option);
        if (select.IsNull || IsDisabledControl(select) || (!option.IsNull && IsDisabledControl(option)))
        {
            return InputOutcome.None;
        }

        InputOutcome o = InputOutcome.None;
        if (!IsMultipleOrList(select))
        {
            if (!_page.Form.IsOpen(select))
            {
                return OpenDropdown(select, ct);
            }

            if (!option.IsNull)
            {
                o |= ChooseOption(select, IndexOfOption(select, option), ct);
            }

            return o | CloseDropdown(ct);
        }

        if (option.IsNull)
        {
            return InputOutcome.None;
        }

        bool multiple = FocusRules.HasAttribute(select, "multiple");
        int clicked = IndexOfOption(select, option);
        if (multiple && (_lastModifiers & KeyModifiers.Shift) != 0)
        {
            // A shift-click selects the range from the anchor to the clicked option (the anchor stays), keeping the rest with Ctrl.
            int anchor = _listAnchorSelect == select.Index && _listAnchor >= 0 ? _listAnchor : Math.Max(0, GetSelectedIndex(select));
            return RangeSelect(select, anchor, clicked, (_lastModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0, ct);
        }

        _listAnchor = clicked;
        _listAnchorSelect = select.Index;
        bool toggle = multiple && (_lastModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (toggle)
        {
            int n = 0;
            _page.Form.SetSelected(option, !IsOptionSelected(option));
            AddScratch(ref n, select);
            AddScratch(ref n, option);
            o |= ApplyFormChange(n, ct);
            FireSimple("input", true, false, select, _lastTime);
            FireSimple("change", true, false, select, _lastTime);
            return o;
        }

        return ChooseOption(select, IndexOfOption(select, option), ct);
    }

    // The options from `a` to `b` (inclusive, either order) become the selection; with `keep` the options already selected stay selected.
    private InputOutcome RangeSelect(Node select, int a, int b, bool keep, CancellationToken ct)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b), n = 0, i = 0;
        bool changed = false;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o), i++)
        {
            bool want = (i >= lo && i <= hi && !IsDisabledControl(o)) || (keep && IsOptionSelected(o));
            if (IsOptionSelected(o) != want)
            {
                _page.Form.SetSelected(o, want);
                AddScratch(ref n, o);
                changed = true;
            }
        }

        if (!changed)
        {
            return InputOutcome.None;
        }

        AddScratch(ref n, select);
        InputOutcome outcome = ApplyFormChange(n, ct);
        FireSimple("input", true, false, select, _lastTime);
        FireSimple("change", true, false, select, _lastTime);
        return outcome;
    }

    private int _listAnchor = -1;
    private int _listAnchorSelect = -1;
    private int _listFocus = -1;
    private readonly char[] _typeAhead = new char[32];
    private int _typeAheadLength;
    private double _typeAheadTime = double.NegativeInfinity;

    // Type-ahead (HTML §4.10.7 and what Chrome does): printable keys typed within a second of each other form a prefix; the first enabled
    // option whose label starts with it (ignoring case) is chosen, starting from the current one. Typing one letter repeatedly cycles
    // through the options that start with it.
    private bool TypeAhead(Node select, string key, bool open, CancellationToken ct, out InputOutcome outcome)
    {
        outcome = InputOutcome.None;
        bool continuing = _lastTime - _typeAheadTime <= 1000 && _typeAheadLength > 0;
        if (key.Length != 1 || char.IsControl(key[0]) || (key[0] == ' ' && !continuing))
        {
            return false;
        }

        if (!continuing)
        {
            _typeAheadLength = 0;
        }

        _typeAheadTime = _lastTime;
        if (_typeAheadLength < _typeAhead.Length)
        {
            _typeAhead[_typeAheadLength++] = char.ToLowerInvariant(key[0]);
        }

        bool same = true;
        for (int k = 1; k < _typeAheadLength; k++)
        {
            same &= _typeAhead[k] == _typeAhead[0];
        }

        ReadOnlySpan<char> prefix = same ? _typeAhead.AsSpan(0, 1) : _typeAhead.AsSpan(0, _typeAheadLength);
        int count = OptionCount(select);
        int current = open && _page.Form.HighlightIndex >= 0 ? IndexOfOption(select, _page.Document.GetNode(_page.Form.HighlightIndex)) : GetSelectedIndex(select);
        int start = same ? current + 1 : Math.Max(current, 0);
        for (int k = 0; k < count; k++)
        {
            int idx = (((start + k) % count) + count) % count;
            Node o = OptionAt(select, idx);
            if (o.IsNull || IsDisabledControl(o) || !HtmlFrontEnd.OptionLabel(o).AsSpan().TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            outcome = open ? Highlight(select, o, ct) : ChooseOption(select, idx, ct);
            return true;
        }

        return true;
    }

    // Selects an option as the user does: the selection changes, then `input` and `change` fire when it did.
    private InputOutcome ChooseOption(Node select, int index, CancellationToken ct)
    {
        int n = 0;
        if (index < 0 || !SelectCore(select, index, ref n))
        {
            return InputOutcome.None;
        }

        InputOutcome o = ApplyFormChange(n, ct);
        FireSimple("input", true, false, select, _lastTime);
        FireSimple("change", true, false, select, _lastTime);
        return o;
    }

    // A press outside an open drop-down closes it.
    private InputOutcome CloseDropdownOnOutsidePress(Node target, CancellationToken ct)
    {
        int open = _page.Form.OpenIndex;
        if (open < 0)
        {
            return InputOutcome.None;
        }

        Node select = SelectOfTarget(target, out _);
        return !select.IsNull && select.Index == open ? InputOutcome.None : CloseDropdown(ct);
    }

    // The pointer over an option of the open popup highlights it.
    private InputOutcome HighlightUnderPointer(Node target, CancellationToken ct)
    {
        int open = _page.Form.OpenIndex;
        if (open < 0)
        {
            return InputOutcome.None;
        }

        Node select = SelectOfTarget(target, out Node option);
        return !select.IsNull && select.Index == open && !option.IsNull && !IsDisabledControl(option) ? Highlight(select, option, ct) : InputOutcome.None;
    }

    // Keys on a focused select (HTML §4.10.7, and what Chrome does): arrows change the choice of a closed drop-down, Space and Alt+Down
    // open it; in the open list arrows move the highlight, Enter and Space choose it and Escape closes the list.
    private bool TrySelectKey(string key, KeyModifiers mods, CancellationToken ct, out InputOutcome outcome)
    {
        outcome = InputOutcome.None;
        Node select = _focused;
        if (!FocusRules.IsHtml(select, "select") || IsDisabledControl(select))
        {
            return false;
        }

        bool alt = mods == KeyModifiers.Alt;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        bool command = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (command || (mods & KeyModifiers.Alt) != 0 && !alt)
        {
            if (command && key is "a" or "A" && FocusRules.HasAttribute(select, "multiple"))
            {
                outcome = RangeSelect(select, 0, OptionCount(select) - 1, false, ct);
                return true;
            }

            return false;
        }

        bool list = IsMultipleOrList(select);
        bool open = _page.Form.IsOpen(select);
        int count = OptionCount(select);
        if (count == 0 && !open)
        {
            return key is " " or "ArrowDown" or "ArrowUp";
        }

        int sel = GetSelectedIndex(select);
        if (!alt && TypeAhead(select, key, open, ct, out InputOutcome typed))
        {
            outcome = typed;
            return true;
        }

        if (shift && !open && FocusRules.HasAttribute(select, "multiple") && key is "ArrowDown" or "ArrowUp")
        {
            // Shift+arrow extends the selection from the anchor.
            int from = _listFocus >= 0 && _listAnchorSelect == select.Index ? _listFocus : Math.Max(sel, 0);
            int next = NextEnabledOption(select, from, key == "ArrowDown" ? 1 : -1);
            if (next >= 0)
            {
                if (_listAnchorSelect != select.Index || _listAnchor < 0)
                {
                    _listAnchor = Math.Max(sel, 0);
                    _listAnchorSelect = select.Index;
                }

                _listFocus = next;
                outcome = RangeSelect(select, _listAnchor, next, false, ct);
            }

            return true;
        }

        _listFocus = -1;
        if (open)
        {
            Node hl = _page.Form.HighlightIndex >= 0 ? _page.Document.GetNode(_page.Form.HighlightIndex) : default;
            int hi = hl.IsNull ? sel : IndexOfOption(select, hl);
            switch (key)
            {
                case "ArrowDown":
                case "ArrowRight":
                {
                    int next = NextEnabledOption(select, hi, 1);
                    outcome = next >= 0 ? Highlight(select, OptionAt(select, next), ct) : InputOutcome.None;
                    return true;
                }

                case "ArrowUp":
                case "ArrowLeft":
                {
                    int next = NextEnabledOption(select, hi, -1);
                    outcome = next >= 0 ? Highlight(select, OptionAt(select, next), ct) : InputOutcome.None;
                    return true;
                }

                case "Home":
                {
                    int next = NextEnabledOption(select, -1, 1);
                    outcome = next >= 0 ? Highlight(select, OptionAt(select, next), ct) : InputOutcome.None;
                    return true;
                }

                case "End":
                {
                    int next = NextEnabledOption(select, count, -1);
                    outcome = next >= 0 ? Highlight(select, OptionAt(select, next), ct) : InputOutcome.None;
                    return true;
                }

                case "Enter":
                case " ":
                    outcome = (hi >= 0 ? ChooseOption(select, hi, ct) : InputOutcome.None) | CloseDropdown(ct);
                    return true;
                case "Escape":
                    outcome = CloseDropdown(ct);
                    return true;
                default:
                    return false;
            }
        }

        switch (key)
        {
            case " " when !list:
                outcome = OpenDropdown(select, ct);
                return true;
            case "ArrowDown" when alt && !list:
                outcome = OpenDropdown(select, ct);
                return true;
            case "F4" when !list:
                outcome = OpenDropdown(select, ct);
                return true;
            case "ArrowDown":
            case "ArrowRight":
            {
                int next = NextEnabledOption(select, sel, 1);
                outcome = next >= 0 ? ChooseOption(select, next, ct) : InputOutcome.None;
                return true;
            }

            case "ArrowUp":
            case "ArrowLeft":
            {
                int next = NextEnabledOption(select, sel < 0 ? count : sel, -1);
                outcome = next >= 0 ? ChooseOption(select, next, ct) : InputOutcome.None;
                return true;
            }

            case "Home":
            {
                int next = NextEnabledOption(select, -1, 1);
                outcome = next >= 0 ? ChooseOption(select, next, ct) : InputOutcome.None;
                return true;
            }

            case "End":
            {
                int next = NextEnabledOption(select, count, -1);
                outcome = next >= 0 ? ChooseOption(select, next, ct) : InputOutcome.None;
                return true;
            }

            default:
                return false;
        }
    }
}
