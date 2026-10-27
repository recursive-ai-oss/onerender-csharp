using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    private PointerEvent _pe = null!;
    private MouseEvent _me = null!;
    private WheelEvent _we = null!;
    private KeyboardEvent _ke = null!;
    private FocusEvent _fe = null!;
    private DomEvent _se = null!;

    private double _lastX;
    private double _lastY;
    private bool _havePointer;
    private bool _suppressMouse;
    private MouseButtons _lastButtons;
    private KeyModifiers _lastModifiers;
    private double _lastTime;

    private readonly Node[] _downTarget = new Node[5];
    private int _clickCount;
    private int _lastDownButton = -1;
    private double _lastDownTime;
    private double _lastDownX;
    private double _lastDownY;
    private Node _lastDownNode;

    private Node[] _chainOld = new Node[32];
    private Node[] _chainNew = new Node[32];

    private void InitEvents()
    {
        _pe = new PointerEvent("pointermove", true, true);
        _me = new MouseEvent("mousemove", true, true);
        _we = new WheelEvent();
        _ke = new KeyboardEvent("keydown");
        _fe = new FocusEvent("focus");
        _se = new DomEvent("scroll");
    }

    /// <summary>
    /// Handles a pointer input: it finds the element under the pointer, fires the pointer and compatibility mouse events with the
    /// boundary events (<c>over</c>, <c>out</c>, <c>enter</c>, <c>leave</c>) when the hovered element changes, <c>click</c>,
    /// <c>dblclick</c>, <c>auxclick</c> and <c>contextmenu</c> after a release, and runs the default actions: pressing focuses the nearest
    /// focusable ancestor (or clears focus), and hover and active states follow the pointer.
    /// </summary>
    /// <param name="input">The input; positions are viewport CSS px and times the host's clock in milliseconds.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened, so the host knows what to repaint.</returns>
    /// <exception cref="OperationCanceledException">Cancelled while restyling.</exception>
    public InputOutcome HandlePointer(in PointerInput input, CancellationToken cancellationToken = default)
    {
        _lastX = input.X;
        _lastY = input.Y;
        _lastButtons = input.Buttons;
        _lastModifiers = input.Modifiers;
        _lastTime = input.Time;
        _havePointer = input.Action != PointerAction.Leave;
        _lastInputWasPointer = true;
        InputOutcome outcome = InputOutcome.None;

        Node target = default;
        if (input.Action != PointerAction.Leave && _hits.HitTest(input.X, input.Y, out HitResult hit))
        {
            target = hit.Node;
        }

        if (target.IsNull)
        {
            // Outside the viewport (or nothing to hit): the pointer is no longer over any element.
            outcome |= SetHovered(default, in input);
            if (input.Action is PointerAction.Up or PointerAction.Cancel)
            {
                outcome |= FinishPress(in input, default);
            }

            return outcome | FlushStates(cancellationToken);
        }

        outcome |= SetHovered(target, in input);
        if (!_dragRange.IsNull)
        {
            outcome |= ContinueRangeDrag(in input, cancellationToken);
        }

        switch (input.Action)
        {
            case PointerAction.Move:
                FirePair("pointermove", "mousemove", true, true, target, in input, 0, default);
                outcome |= HighlightUnderPointer(target, cancellationToken);
                break;
            case PointerAction.Down:
                outcome |= CloseDropdownOnOutsidePress(target, cancellationToken);
                outcome |= PointerDown(target, in input);
                break;
            case PointerAction.Up:
                outcome |= PointerUp(target, in input);
                break;
            case PointerAction.Cancel:
                FirePointerOnly("pointercancel", true, false, target, in input, default);
                outcome |= FinishPress(in input, target);
                _suppressMouse = false;
                break;
        }

        return outcome | FlushStates(cancellationToken);
    }

    // ------------------------------------------------------------------ boundary events

    // Pointer Events §4.2 and UI Events: out and over fire on the elements themselves, leave and enter on every ancestor that the
    // pointer leaves or enters (not on the ones it stays inside). Each pointer event is followed by its mouse counterpart.
    private InputOutcome SetHovered(Node next, in PointerInput p)
    {
        Node old = _hovered;
        if (old.Equals(next))
        {
            return InputOutcome.None;
        }

        int oldCount = Chain(old, ref _chainOld);
        int newCount = Chain(next, ref _chainNew);
        // The chains run from the element to the root; the shared tail is the set of ancestors the pointer stays inside.
        int a = oldCount, b = newCount;
        while (a > 0 && b > 0 && _chainOld[a - 1].Equals(_chainNew[b - 1]))
        {
            a--;
            b--;
        }

        if (!old.IsNull)
        {
            FirePair("pointerout", "mouseout", true, true, old, in p, 0, next, suppressible: false);
            for (int i = 0; i < a; i++)
            {
                FirePair("pointerleave", "mouseleave", false, false, _chainOld[i], in p, 0, next, suppressible: false);
            }
        }

        _hovered = next;
        MarkDirty();
        if (!next.IsNull)
        {
            FirePair("pointerover", "mouseover", true, true, next, in p, 0, old, suppressible: false);
            for (int i = b - 1; i >= 0; i--)
            {
                FirePair("pointerenter", "mouseenter", false, false, _chainNew[i], in p, 0, old, suppressible: false);
            }
        }

        return InputOutcome.None;
    }

    // Elements from the node up to the root element, innermost first, capped at the event path length.
    private static int Chain(Node start, ref Node[] buffer)
    {
        int n = 0;
        for (Node e = start; e.IsElement; e = e.Parent)
        {
            if (n == EventDispatcher.MaxPathLength)
            {
                break;
            }

            if (n == buffer.Length)
            {
                Array.Resize(ref buffer, Math.Min(EventDispatcher.MaxPathLength, buffer.Length * 2));
            }

            buffer[n++] = e;
        }

        return n;
    }

    // Re-checks what is under the pointer after the content moved under it (scrolling, relayout), firing boundary events only.
    private InputOutcome RefreshHover()
    {
        if (!_havePointer)
        {
            return InputOutcome.None;
        }

        Node target = _hits.HitTest(_lastX, _lastY, out HitResult hit) ? hit.Node : default;
        var p = new PointerInput { Action = PointerAction.Move, X = _lastX, Y = _lastY, Buttons = _lastButtons, Modifiers = _lastModifiers, Time = _lastTime };
        return SetHovered(target, in p);
    }

    // ------------------------------------------------------------------ press and release

    private InputOutcome PointerDown(Node target, in PointerInput p)
    {
        InputOutcome outcome = InputOutcome.None;
        int button = Math.Clamp(p.Button, 0, 4);
        _downTarget[button] = target;
        if (button == 0)
        {
            _active = target;
            MarkDirty();
        }

        // UI Events: the click count grows while presses of the same button stay close in time and place (host time, host distance).
        bool again = _lastDownButton == button && p.Time - _lastDownTime <= _io.DoubleClickInterval
            && Math.Abs(p.X - _lastDownX) <= _io.DoubleClickDistance && Math.Abs(p.Y - _lastDownY) <= _io.DoubleClickDistance;
        _clickCount = again ? _clickCount + 1 : 1;
        _lastDownButton = button;
        _lastDownTime = p.Time;
        _lastDownX = p.X;
        _lastDownY = p.Y;
        _lastDownNode = target;

        bool pointerCanceled = !FirePointerOnly("pointerdown", true, true, target, in p, default, detail: 0);
        // Pointer Events §11: a canceled pointerdown suppresses the compatibility mouse events until the buttons are released.
        _suppressMouse = pointerCanceled;
        bool mouseCanceled = false;
        if (!pointerCanceled)
        {
            mouseCanceled = !FireMouseOnly("mousedown", true, true, target, in p, _clickCount, default);
        }

        if (mouseCanceled || pointerCanceled)
        {
            outcome |= InputOutcome.DefaultPrevented;
        }

        // Default action of mousedown (HTML §6.6): focus the nearest focusable ancestor, or take focus away.
        if (button == 0 && !mouseCanceled)
        {
            Node focusable = FocusableAncestorOrSelf(target);
            _navigationStart = target;
            outcome |= SetFocus(focusable, FocusReason.PointingDevice, scroll: false);
            outcome |= BeginRangeDrag(target, in p, default);
        }

        return outcome;
    }

    private InputOutcome PointerUp(Node target, in PointerInput p)
    {
        int button = Math.Clamp(p.Button, 0, 4);
        FirePointerOnly("pointerup", true, true, target, in p, default);
        if (!_suppressMouse)
        {
            FireMouseOnly("mouseup", true, true, target, in p, _clickCount, default);
        }

        InputOutcome outcome = InputOutcome.None;
        Node down = _downTarget[button];
        _downTarget[button] = default;

        // Click goes to the nearest common inclusive ancestor of the press and release targets (UI Events, click).
        if (!down.IsNull)
        {
            Node clickTarget = CommonAncestor(down, target);
            if (!clickTarget.IsNull)
            {
                if (button == 0)
                {
                    FirePointerClick(clickTarget, in p, _clickCount, ref outcome);
                    if (_clickCount == 2)
                    {
                        FireMouseOnly("dblclick", true, true, clickTarget, in p, 2, default);
                    }
                }
                else if (button == 1)
                {
                    FirePointerOnly("auxclick", true, true, clickTarget, in p, default, detail: _clickCount);
                }
                else if (button == 2)
                {
                    if (!FirePointerOnly("contextmenu", true, true, clickTarget, in p, default, detail: 0))
                    {
                        outcome |= InputOutcome.DefaultPrevented;
                    }
                }
            }
        }

        if (p.Buttons == MouseButtons.None)
        {
            _suppressMouse = false;
        }

        return outcome | FinishPress(in p, target);
    }

    private InputOutcome FinishPress(in PointerInput p, Node target)
    {
        if (p.Action == PointerAction.Cancel)
        {
            Array.Clear(_downTarget);
        }

        if ((p.Action == PointerAction.Cancel || (p.Action == PointerAction.Up && p.Button == 0)) && !_active.IsNull)
        {
            _active = default;
            MarkDirty();
        }

        return InputOutcome.None;
    }

    private static Node CommonAncestor(Node a, Node b)
    {
        int da = Depth(a), db = Depth(b);
        while (da > db)
        {
            a = a.Parent;
            da--;
        }

        while (db > da)
        {
            b = b.Parent;
            db--;
        }

        while (!a.IsNull && !a.Equals(b))
        {
            a = a.Parent;
            b = b.Parent;
        }

        return a.IsElement ? a : default;
    }

    private static int Depth(Node n)
    {
        int d = 0;
        for (Node p = n; !p.IsNull; p = p.Parent)
        {
            d++;
        }

        return d;
    }

    // ------------------------------------------------------------------ firing

    private bool FirePair(string pointerType, string mouseType, bool bubbles, bool cancelable, Node target, in PointerInput p, int detail, Node related, bool suppressible = true)
    {
        bool ok = FirePointerOnly(pointerType, bubbles, cancelable, target, in p, related, detail);
        if (!(suppressible && _suppressMouse))
        {
            ok &= FireMouseOnly(mouseType, bubbles, cancelable, target, in p, detail, related);
        }

        return ok;
    }

    private bool FirePointerOnly(string type, bool bubbles, bool cancelable, Node target, in PointerInput p, Node related, int detail = 0)
    {
        PointerEvent e = _pe;
        e.Reset(type, bubbles, cancelable, p.Time);
        FillMouse(e, in p, detail, related);
        e.PointerId = p.PointerId == 0 ? 1 : p.PointerId;
        e.PointerType = p.PointerType ?? "mouse";
        e.IsPrimary = true;
        e.Pressure = p.Pressure > 0 ? p.Pressure : (p.Buttons != MouseButtons.None ? 0.5 : 0);
        return _events.Dispatch(e, EventTarget.Of(target));
    }

    // A click, with the default action of the control it activates.
    private bool FirePointerClick(Node target, in PointerInput p, int detail, ref InputOutcome outcome)
    {
        PointerEvent e = _pe;
        e.Reset("click", true, true, p.Time);
        FillMouse(e, in p, detail, default);
        e.PointerId = p.PointerId == 0 ? 1 : p.PointerId;
        e.PointerType = p.PointerType ?? "mouse";
        e.IsPrimary = true;
        e.Pressure = p.Pressure > 0 ? p.Pressure : (p.Buttons != MouseButtons.None ? 0.5 : 0);
        return DispatchClick(target, e, ref outcome, default);
    }

    private bool FireMouseOnly(string type, bool bubbles, bool cancelable, Node target, in PointerInput p, int detail, Node related)
    {
        MouseEvent e = _me;
        e.Reset(type, bubbles, cancelable, p.Time);
        FillMouse(e, in p, detail, related);
        return _events.Dispatch(e, EventTarget.Of(target));
    }

    private void FillMouse(MouseEvent e, in PointerInput p, int detail, Node related)
    {
        e.Detail = detail;
        e.ClientX = p.X;
        e.ClientY = p.Y;
        e.ScreenX = p.X;
        e.ScreenY = p.Y;
        ViewportOffset(out double sx, out double sy);
        e.PageX = p.X + sx;
        e.PageY = p.Y + sy;
        e.Button = p.Action is PointerAction.Down or PointerAction.Up ? p.Button : 0;
        e.Buttons = p.Buttons;
        e.Modifiers = p.Modifiers;
        e.RelatedTarget = related.IsNull ? default : EventTarget.Of(related);
    }
}
