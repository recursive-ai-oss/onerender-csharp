using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    /// <summary>
    /// Handles a key input: fires <c>keydown</c> or <c>keyup</c> at the focused element (the <c>body</c> when nothing is focused) and,
    /// when no listener cancels a key press, runs its default action: Tab and Shift+Tab move focus, Enter and Space activate buttons,
    /// links and summaries with a <c>click</c>, and the arrow, Page Up/Down, Home, End and Space keys scroll the nearest scroll container
    /// of the focused element (or of the last pressed place), then the viewport. Keys that edit text are left to the focused control.
    /// </summary>
    /// <param name="input">The key input.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened.</returns>
    public InputOutcome HandleKey(in KeyInput input, CancellationToken cancellationToken = default)
    {
        _lastTime = input.Time;
        _lastInputWasPointer = false;
        Node target = _focused;
        if (target.IsNull)
        {
            target = KeyFallbackTarget();
        }

        if (target.IsNull)
        {
            return InputOutcome.None;
        }

        string key = input.Key ?? string.Empty;
        KeyboardEvent e = _ke;
        e.Reset(input.Down ? "keydown" : "keyup", true, true, input.Time);
        e.Detail = 0;
        e.Key = key;
        e.Code = input.Code ?? string.Empty;
        e.Repeat = input.Repeat;
        e.IsComposing = input.IsComposing;
        e.Location = 0;
        e.Modifiers = input.Modifiers;
        bool notCanceled = _events.Dispatch(e, EventTarget.Of(target));
        InputOutcome outcome = InputOutcome.None;
        if (!notCanceled)
        {
            return outcome | InputOutcome.DefaultPrevented | FlushStates(cancellationToken);
        }

        if (input.IsComposing)
        {
            return outcome | FlushStates(cancellationToken);
        }

        bool plain = (input.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0;
        bool shift = (input.Modifiers & KeyModifiers.Shift) != 0;
        if (input.Down && plain && key == "Tab")
        {
            outcome |= MoveFocus(!shift, cancellationToken);
            return outcome;
        }

        if (plain && ActivationKey(key, input.Down, _focused))
        {
            Activate(_focused, input.Time);
        }
        else if (input.Down && plain && !(!_focused.IsNull && FocusRules.IsTextEntry(_focused)))
        {
            outcome |= ScrollByKey(key, shift);
        }

        return outcome | FlushStates(cancellationToken);
    }

    // The body is where key events go when nothing has focus (HTML: the focused area of the document is the document itself).
    private Node KeyFallbackTarget()
    {
        Node root = _page.Document.DocumentElement;
        for (Node c = root.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (FocusRules.IsHtml(c, "body"))
            {
                return c;
            }
        }

        return root;
    }

    // Enter on links, buttons and summaries activates on key down; Space on buttons and summaries on key up (HTML §4.10.5, §4.11.1).
    private static bool ActivationKey(string key, bool down, Node focused)
    {
        if (focused.IsNull)
        {
            return false;
        }

        bool link = FocusRules.IsHtml(focused, "a") && FocusRules.HasAttribute(focused, "href");
        bool button = FocusRules.IsHtml(focused, "button") || FocusRules.IsHtml(focused, "summary") || IsButtonInput(focused);
        if (key == "Enter" && down)
        {
            return link || button;
        }

        return key == " " && !down && (button || IsCheckable(focused));
    }

    private static bool IsButtonInput(Node n)
    {
        if (!FocusRules.IsHtml(n, "input") || !FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t))
        {
            return false;
        }

        return t.Equals("button", StringComparison.OrdinalIgnoreCase) || t.Equals("submit", StringComparison.OrdinalIgnoreCase)
            || t.Equals("reset", StringComparison.OrdinalIgnoreCase) || t.Equals("image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCheckable(Node n) =>
        FocusRules.IsHtml(n, "input") && FocusRules.TryGetAttribute(n, "type", out ReadOnlySpan<char> t)
        && (t.Equals("checkbox", StringComparison.OrdinalIgnoreCase) || t.Equals("radio", StringComparison.OrdinalIgnoreCase));

    // A keyboard-synthesized click has no pointer position (detail 0), as HTML's "fire a synthetic pointer event" says.
    private void Activate(Node target, double time)
    {
        var p = new PointerInput { Action = PointerAction.Up, X = 0, Y = 0, Button = 0, Buttons = MouseButtons.None, Time = time };
        PointerEvent e = _pe;
        e.Reset("click", true, true, time);
        FillMouse(e, in p, 0, default);
        e.PointerId = 1;
        e.PointerType = string.Empty;
        e.IsPrimary = false;
        e.Pressure = 0;
        _events.Dispatch(e, EventTarget.Of(target));
    }

    private InputOutcome ScrollByKey(string key, bool shift)
    {
        double line = _io.LineScrollPixels;
        double pageY = Math.Max(line, _options.ViewportHeight * _io.PageScrollFraction);
        double dx = 0, dy = 0;
        switch (key)
        {
            case "ArrowDown": dy = line; break;
            case "ArrowUp": dy = -line; break;
            case "ArrowRight": dx = line; break;
            case "ArrowLeft": dx = -line; break;
            case "PageDown": dy = pageY; break;
            case "PageUp": dy = -pageY; break;
            case " ": dy = shift ? -pageY : pageY; break;
            case "Home": dy = -1e9; break;
            case "End": dy = 1e9; break;
            default: return InputOutcome.None;
        }

        // Start from the focused element, else from the last place the user pressed, else from the viewport.
        Node from = !_focused.IsNull ? _focused : _navigationStart;
        int start = from.IsNull ? -1 : FragmentIndexOf(from);
        return ScrollChain(start, dx, dy) ? InputOutcome.Scrolled | RefreshHover() : InputOutcome.None;
    }
}
