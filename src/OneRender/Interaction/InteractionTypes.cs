using OneRender.Style;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Interaction;

/// <summary>Why an element received focus. It decides <c>:focus-visible</c> (Selectors 4 §11, HTML §6.6.4).</summary>
public enum FocusReason : byte
{
    /// <summary>A script (the host calling <see cref="InteractivePage.Focus"/>). The element shows focus unless the last user input was a pointer and the previously focused element did not show it.</summary>
    Script = 0,

    /// <summary>A pointer press. Only text-entry controls and editing hosts then match <c>:focus-visible</c>.</summary>
    PointingDevice = 1,

    /// <summary>The keyboard (Tab navigation or another key). The element matches <c>:focus-visible</c>.</summary>
    Keyboard = 2,
}

/// <summary>The interaction pseudo-classes of an element, as a set.</summary>
[Flags]
public enum ElementStates : byte
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary><c>:hover</c>: the element is the hovered one or an ancestor of it.</summary>
    Hover = 1,

    /// <summary><c>:active</c>: the element is the pressed one or an ancestor of it.</summary>
    Active = 2,

    /// <summary><c>:focus</c>: the element is the focused one.</summary>
    Focus = 4,

    /// <summary><c>:focus-within</c>: the element is the focused one or an ancestor of it.</summary>
    FocusWithin = 8,

    /// <summary><c>:focus-visible</c>: the element is focused and the focus should be shown.</summary>
    FocusVisible = 16,
}

/// <summary>One element whose interaction states changed.</summary>
public readonly struct ElementStateChange
{
    internal ElementStateChange(Node element, ElementStates before, ElementStates after)
    {
        Element = element;
        Before = before;
        After = after;
    }

    /// <summary>The element.</summary>
    public Node Element { get; }

    /// <summary>The states it had before.</summary>
    public ElementStates Before { get; }

    /// <summary>The states it has now.</summary>
    public ElementStates After { get; }

    /// <summary>The states that differ.</summary>
    public ElementStates Changed => Before ^ After;
}

/// <summary>
/// The seam for incremental restyling: after hover, active or focus changes, the page tells the handler which elements' states
/// changed and with which new <see cref="StyleContext"/>. A handler that restyles, relays out and repaints only what is affected
/// returns true. When there is no handler, or it returns false, the page restyles and relays out the whole document.
/// </summary>
public interface IStateChangeHandler
{
    /// <summary>Called once per input event that changed states, before the page applies them itself.</summary>
    /// <param name="page">The page.</param>
    /// <param name="changes">The elements whose states changed (each at most once), valid during the call.</param>
    /// <param name="context">The style context that now holds the hover, active and focus elements.</param>
    /// <returns>True when the handler has brought the page up to date, so the page does nothing more.</returns>
    bool ElementStatesChanged(InteractivePage page, ReadOnlySpan<ElementStateChange> changes, StyleContext context);
}

/// <summary>What an input call did, for the host to decide what to repaint.</summary>
[Flags]
public enum InputOutcome : byte
{
    /// <summary>Nothing visible happened.</summary>
    None = 0,

    /// <summary>A listener (or the engine) canceled the main event, so its default action did not run.</summary>
    DefaultPrevented = 1,

    /// <summary>Some scroll position changed; the display list can be brought up to date with <c>Painter.PatchScroll</c>.</summary>
    Scrolled = 2,

    /// <summary>Hover, active or focus states changed.</summary>
    StatesChanged = 4,

    /// <summary>The page was restyled and laid out again: <see cref="InteractivePage.Page"/> is a new object and the display list must be painted again.</summary>
    Relaid = 8,

    /// <summary>Focus moved to another element or was cleared.</summary>
    FocusChanged = 16,

    /// <summary>
    /// <see cref="InteractivePage.Advance"/> changed the page. Without <see cref="Relaid"/> only transform or opacity changed and
    /// <see cref="InteractivePage.PatchAnimations"/> brings the display list up to date; with it, paint again.
    /// </summary>
    Animated = 32,
}

/// <summary>Values for the host-dependent numbers of input handling.</summary>
public sealed class InteractionOptions
{
    /// <summary>Longest time between two presses that count as a double click, in the host's time units (milliseconds). Default 500.</summary>
    public double DoubleClickInterval { get; init; } = 500;

    /// <summary>Furthest the pointer may move between two presses of a double click, in CSS px. Default 5.</summary>
    public double DoubleClickDistance { get; init; } = 5;

    /// <summary>CSS px that one line of wheel or arrow-key scrolling covers. Default 40.</summary>
    public double LineScrollPixels { get; init; } = 40;

    /// <summary>The fraction of the scrollport that a page-wise scroll covers. Default 0.875.</summary>
    public double PageScrollFraction { get; init; } = 0.875;

    /// <summary>Whether Tab past the last (or first) focusable element wraps around to the other end. Default true; when false, focus is cleared instead so a host can move it to its own controls.</summary>
    public bool WrapTabNavigation { get; init; } = true;

    /// <summary>The page's animation clock at creation, in the host's milliseconds. Animations and transitions started by the first style begin at this time. Default 0.</summary>
    public double AnimationStartTime { get; init; }
}

/// <summary>The kind of a <see cref="PointerInput"/>.</summary>
public enum PointerAction : byte
{
    /// <summary>The pointer moved (or its properties changed without a button change).</summary>
    Move,

    /// <summary>A button went down; <see cref="PointerInput.Button"/> says which.</summary>
    Down,

    /// <summary>A button went up.</summary>
    Up,

    /// <summary>The system canceled the pointer (for example a touch taken over by a gesture).</summary>
    Cancel,

    /// <summary>The pointer left the viewport.</summary>
    Leave,
}

/// <summary>A pointer input from the host. Positions are viewport CSS px; times are in the host's clock, in milliseconds.</summary>
public readonly struct PointerInput
{
    /// <summary>What happened.</summary>
    public PointerAction Action { get; init; }

    /// <summary>Viewport x.</summary>
    public double X { get; init; }

    /// <summary>Viewport y.</summary>
    public double Y { get; init; }

    /// <summary>For <see cref="PointerAction.Down"/> and <see cref="PointerAction.Up"/>: the button that changed, numbered as UI Events does (0 primary, 1 auxiliary, 2 secondary, 3 back, 4 forward).</summary>
    public int Button { get; init; }

    /// <summary>The buttons that are down after this input.</summary>
    public MouseButtons Buttons { get; init; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>The host's time in milliseconds; used for event time stamps and the double-click interval.</summary>
    public double Time { get; init; }

    /// <summary>The pointer id; 1 for the mouse.</summary>
    public int PointerId { get; init; }

    /// <summary><c>mouse</c> (default), <c>pen</c> or <c>touch</c>. Null means <c>mouse</c>.</summary>
    public string? PointerType { get; init; }

    /// <summary>Pressure 0 to 1; 0.5 while a button is down and 0 otherwise when left at 0 for a mouse.</summary>
    public double Pressure { get; init; }
}

/// <summary>A wheel input from the host.</summary>
public readonly struct WheelInput
{
    /// <summary>Pointer x in the viewport.</summary>
    public double X { get; init; }

    /// <summary>Pointer y in the viewport.</summary>
    public double Y { get; init; }

    /// <summary>Horizontal delta; positive scrolls toward the end (right).</summary>
    public double DeltaX { get; init; }

    /// <summary>Vertical delta; positive scrolls toward the end (down).</summary>
    public double DeltaY { get; init; }

    /// <summary>The unit of the deltas. Default pixels.</summary>
    public WheelDeltaMode Mode { get; init; }

    /// <summary>The buttons down.</summary>
    public MouseButtons Buttons { get; init; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>The host's time in milliseconds.</summary>
    public double Time { get; init; }
}

/// <summary>A key input from the host.</summary>
public readonly struct KeyInput
{
    /// <summary>True for a key press, false for a release.</summary>
    public bool Down { get; init; }

    /// <summary>The key value (UI Events Key Values): <c>a</c>, <c>Tab</c>, <c>Enter</c>, <c>ArrowDown</c>, <c>PageDown</c>, <c> </c> for space.</summary>
    public string Key { get; init; }

    /// <summary>The physical key (UI Events Code Values), such as <c>KeyA</c> or <c>Space</c>; may be empty.</summary>
    public string? Code { get; init; }

    /// <summary>True when the key repeats because it is held.</summary>
    public bool Repeat { get; init; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>True during IME composition.</summary>
    public bool IsComposing { get; init; }

    /// <summary>The host's time in milliseconds.</summary>
    public double Time { get; init; }
}
