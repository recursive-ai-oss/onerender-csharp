using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

/// <summary>The phase an event is in while it is dispatched (DOM Standard §2.2, <c>Event.eventPhase</c>).</summary>
public enum EventPhase : byte
{
    /// <summary>The event is not being dispatched.</summary>
    None = 0,

    /// <summary>Travelling from the window down to the target's parent.</summary>
    Capturing = 1,

    /// <summary>At the target.</summary>
    AtTarget = 2,

    /// <summary>Travelling from the target's parent up to the window.</summary>
    Bubbling = 3,
}

/// <summary>Modifier keys held during an input event (UI Events §3.7.3, <c>KeyboardEvent.getModifierState</c>).</summary>
[Flags]
public enum KeyModifiers : byte
{
    /// <summary>None held.</summary>
    None = 0,

    /// <summary>Alt (Option).</summary>
    Alt = 1,

    /// <summary>Control.</summary>
    Control = 2,

    /// <summary>Meta (Command, Windows key).</summary>
    Meta = 4,

    /// <summary>Shift.</summary>
    Shift = 8,
}

/// <summary>The mouse buttons that are down, as the <c>buttons</c> bit field of UI Events §3.4.1 numbers them.</summary>
[Flags]
public enum MouseButtons : ushort
{
    /// <summary>No button.</summary>
    None = 0,

    /// <summary>Primary (usually left).</summary>
    Primary = 1,

    /// <summary>Secondary (usually right).</summary>
    Secondary = 2,

    /// <summary>Auxiliary (usually the wheel button).</summary>
    Auxiliary = 4,

    /// <summary>The fourth button (back).</summary>
    Back = 8,

    /// <summary>The fifth button (forward).</summary>
    Forward = 16,
}

/// <summary>
/// Something an event can be dispatched at: a node of the document (an element, or the document node) or the window.
/// The default value refers to nothing.
/// </summary>
public readonly struct EventTarget : IEquatable<EventTarget>
{
    private readonly Node _node;
    private readonly bool _window;

    private EventTarget(Node node, bool window)
    {
        _node = node;
        _window = window;
    }

    /// <summary>The window, the last target of every event path except <c>load</c>.</summary>
    public static EventTarget Window => new(default, true);

    /// <summary>A node as a target.</summary>
    /// <param name="node">An element or the document node; the null node gives the null target.</param>
    public static EventTarget Of(Node node) => new(node, false);

    /// <summary>The node, or the null node for the window and the null target.</summary>
    public Node Node => _node;

    /// <summary>True for the window.</summary>
    public bool IsWindow => _window;

    /// <summary>True for the default value.</summary>
    public bool IsNull => !_window && _node.IsNull;

    /// <summary>True when the target is an element.</summary>
    public bool IsElement => !_window && _node.IsElement;

    /// <summary>True when the target is the document node.</summary>
    public bool IsDocument => !_window && !_node.IsNull && _node.Kind == NodeKind.Document;

    /// <summary>Wraps a node as a target.</summary>
    /// <param name="node">The node.</param>
    public static implicit operator EventTarget(Node node) => Of(node);

    /// <inheritdoc />
    public bool Equals(EventTarget other) => _window == other._window && _node.Equals(other._node);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EventTarget t && Equals(t);

    /// <inheritdoc />
    public override int GetHashCode() => _window ? -1 : _node.GetHashCode();

    /// <summary>Equality.</summary>
    public static bool operator ==(EventTarget left, EventTarget right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(EventTarget left, EventTarget right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => _window ? "window" : _node.IsNull ? "(null)" : _node.ToString();
}

/// <summary>
/// A DOM <c>Event</c> (named <c>DomEvent</c> because <c>Event</c> is a reserved word in some .NET languages) (DOM Standard §2.2). The engine dispatches events of its own kind (<see cref="MouseEvent"/>, <see cref="KeyboardEvent"/>
/// and so on) and a host, or a later script runtime, can create and dispatch any other with <see cref="EventDispatcher.Dispatch"/>.
/// </summary>
/// <remarks>
/// To keep input handling free of allocations the engine reuses its event objects: an event the engine dispatched is valid only
/// inside the listener call. A listener that wants to keep data copies it.
/// </remarks>
public class DomEvent
{
    private string _type;
    private bool _canceled;
    private bool _stopPropagation;
    private bool _stopImmediate;
    private bool _passive;
    private EventTarget[] _path = [];
    private int _pathLength;

    /// <summary>Creates an event that is not yet dispatched.</summary>
    /// <param name="type">The event type, such as <c>click</c>. Compared ordinally and case-sensitively.</param>
    /// <param name="bubbles">Whether the event bubbles. Default false.</param>
    /// <param name="cancelable">Whether <see cref="PreventDefault"/> has an effect. Default false.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public DomEvent(string type, bool bubbles = false, bool cancelable = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
    }

    /// <summary>The event type.</summary>
    public string Type => _type;

    /// <summary>Whether the event goes through the bubbling phase.</summary>
    public bool Bubbles { get; private protected set; }

    /// <summary>Whether <see cref="PreventDefault"/> can cancel the event.</summary>
    public bool Cancelable { get; private protected set; }

    /// <summary>Whether the event would cross a shadow boundary. Always true for events the engine creates; there are no shadow trees.</summary>
    public bool Composed { get; init; }

    /// <summary>True for events the engine made from host input, false for events created by a caller.</summary>
    public bool IsTrusted { get; internal set; }

    /// <summary>The time the event was created, in the host's clock (milliseconds); 0 when the host gave none.</summary>
    public double TimeStamp { get; internal set; }

    /// <summary>The target the event was dispatched at; null before and after dispatch the way DOM leaves <c>target</c> set afterwards is kept: it stays after dispatch.</summary>
    public EventTarget Target { get; internal set; }

    /// <summary>The target whose listeners are running; null outside dispatch.</summary>
    public EventTarget CurrentTarget { get; internal set; }

    /// <summary>The current phase.</summary>
    public EventPhase Phase { get; internal set; }

    /// <summary>True when a cancelable event had <see cref="PreventDefault"/> called by a non-passive listener.</summary>
    public bool DefaultPrevented => _canceled;

    /// <summary>True while the event is being dispatched.</summary>
    public bool IsDispatching { get; internal set; }

    /// <summary>The legacy alias of <see cref="StopPropagation"/>: reading it tells whether propagation was stopped; setting it true stops it.</summary>
    public bool CancelBubble
    {
        get => _stopPropagation;
        set
        {
            if (value)
            {
                _stopPropagation = true;
            }
        }
    }

    /// <summary>The targets of the event path from the target outward, valid during dispatch; empty otherwise (DOM Standard §2.9, <c>composedPath()</c>).</summary>
    /// <returns>The path. The span is only valid until the event dispatch ends.</returns>
    public ReadOnlySpan<EventTarget> ComposedPath() => IsDispatching ? new ReadOnlySpan<EventTarget>(_path, 0, _pathLength) : default;

    /// <summary>Cancels the event's default action when it is cancelable and the current listener is not passive.</summary>
    public void PreventDefault()
    {
        if (Cancelable && !_passive)
        {
            _canceled = true;
        }
    }

    /// <summary>Stops the event from reaching the listeners of later targets on the path. Listeners of the current target still run.</summary>
    public void StopPropagation() => _stopPropagation = true;

    /// <summary>Stops the event from reaching any further listener, including later ones on the current target.</summary>
    public void StopImmediatePropagation()
    {
        _stopPropagation = true;
        _stopImmediate = true;
    }

    internal bool StopPropagationFlag => _stopPropagation;

    internal bool StopImmediateFlag => _stopImmediate;

    internal bool Passive
    {
        set => _passive = value;
    }

    internal EventTarget[] PathBuffer => _path;

    internal void SetPath(EventTarget[] buffer, int length)
    {
        _path = buffer;
        _pathLength = length;
    }

    internal void ClearPath()
    {
        _path = [];
        _pathLength = 0;
    }

    // Prepares a reused event for its next dispatch (DOM Standard §2.9 step "unset the flags" is done after dispatch; this is the engine's pooling).
    internal void Reset(string type, bool bubbles, bool cancelable, double timeStamp)
    {
        _type = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
        _canceled = false;
        _stopPropagation = false;
        _stopImmediate = false;
        _passive = false;
        Target = default;
        CurrentTarget = default;
        Phase = EventPhase.None;
        TimeStamp = timeStamp;
        IsTrusted = true;
    }

    internal void ClearFlagsAfterDispatch()
    {
        _stopPropagation = false;
        _stopImmediate = false;
        _passive = false;
    }
}

/// <summary>A UI Events <c>UIEvent</c>: an event with a <see cref="Detail"/> (UI Events §3.2).</summary>
public class UIEvent : DomEvent
{
    /// <summary>Creates a UI event.</summary>
    /// <param name="type">The type.</param>
    /// <param name="bubbles">Whether it bubbles.</param>
    /// <param name="cancelable">Whether it is cancelable.</param>
    public UIEvent(string type, bool bubbles = false, bool cancelable = false)
        : base(type, bubbles, cancelable)
    {
    }

    /// <summary>The click count for mouse events, and 0 for events that carry no detail.</summary>
    public int Detail { get; internal set; }
}

/// <summary>A UI Events <c>FocusEvent</c> (UI Events §3.3).</summary>
public sealed class FocusEvent : UIEvent
{
    /// <summary>Creates a focus event.</summary>
    /// <param name="type">The type: <c>focus</c>, <c>blur</c>, <c>focusin</c> or <c>focusout</c>.</param>
    /// <param name="bubbles">Whether it bubbles.</param>
    public FocusEvent(string type, bool bubbles = false)
        : base(type, bubbles, false)
    {
    }

    /// <summary>The element losing focus (for <c>focus</c>/<c>focusin</c>) or gaining it (for <c>blur</c>/<c>focusout</c>), or null.</summary>
    public EventTarget RelatedTarget { get; internal set; }
}

/// <summary>A UI Events <c>MouseEvent</c> (UI Events §3.4). Coordinates are CSS px.</summary>
public class MouseEvent : UIEvent
{
    /// <summary>Creates a mouse event.</summary>
    /// <param name="type">The type.</param>
    /// <param name="bubbles">Whether it bubbles.</param>
    /// <param name="cancelable">Whether it is cancelable.</param>
    public MouseEvent(string type, bool bubbles = false, bool cancelable = false)
        : base(type, bubbles, cancelable)
    {
    }

    /// <summary>The pointer's x in the viewport.</summary>
    public double ClientX { get; internal set; }

    /// <summary>The pointer's y in the viewport.</summary>
    public double ClientY { get; internal set; }

    /// <summary>The pointer's x in the document: <see cref="ClientX"/> plus the viewport's scroll offset.</summary>
    public double PageX { get; internal set; }

    /// <summary>The pointer's y in the document.</summary>
    public double PageY { get; internal set; }

    /// <summary>The pointer's x on the screen; the engine has no screen, so this equals <see cref="ClientX"/> unless the host supplies an origin.</summary>
    public double ScreenX { get; internal set; }

    /// <summary>The pointer's y on the screen.</summary>
    public double ScreenY { get; internal set; }

    /// <summary>The button that changed state: 0 primary, 1 auxiliary, 2 secondary, 3 back, 4 forward; 0 for events that change no button (UI Events §3.4.1).</summary>
    public int Button { get; internal set; }

    /// <summary>The buttons down after the event.</summary>
    public MouseButtons Buttons { get; internal set; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifiers Modifiers { get; internal set; }

    /// <summary>The other element involved: the one left (<c>mouseover</c>, <c>mouseenter</c>) or entered (<c>mouseout</c>, <c>mouseleave</c>); null otherwise.</summary>
    public EventTarget RelatedTarget { get; internal set; }

    /// <summary>True when Alt was held.</summary>
    public bool AltKey => (Modifiers & KeyModifiers.Alt) != 0;

    /// <summary>True when Control was held.</summary>
    public bool CtrlKey => (Modifiers & KeyModifiers.Control) != 0;

    /// <summary>True when Meta was held.</summary>
    public bool MetaKey => (Modifiers & KeyModifiers.Meta) != 0;

    /// <summary>True when Shift was held.</summary>
    public bool ShiftKey => (Modifiers & KeyModifiers.Shift) != 0;
}

/// <summary>A Pointer Events <c>PointerEvent</c> (Pointer Events §4.1).</summary>
public sealed class PointerEvent : MouseEvent
{
    /// <summary>Creates a pointer event.</summary>
    /// <param name="type">The type.</param>
    /// <param name="bubbles">Whether it bubbles.</param>
    /// <param name="cancelable">Whether it is cancelable.</param>
    public PointerEvent(string type, bool bubbles = false, bool cancelable = false)
        : base(type, bubbles, cancelable)
    {
    }

    /// <summary>The pointer's identifier; 1 for the mouse.</summary>
    public int PointerId { get; internal set; }

    /// <summary><c>mouse</c>, <c>pen</c> or <c>touch</c>.</summary>
    public string PointerType { get; internal set; } = "mouse";

    /// <summary>True for the primary pointer of its type.</summary>
    public bool IsPrimary { get; internal set; }

    /// <summary>Normalised pressure, 0 to 1.</summary>
    public double Pressure { get; internal set; }

    /// <summary>The contact width in CSS px; 1 for a mouse.</summary>
    public double Width { get; internal set; } = 1;

    /// <summary>The contact height in CSS px; 1 for a mouse.</summary>
    public double Height { get; internal set; } = 1;
}

/// <summary>The unit of the deltas of a <see cref="WheelEvent"/> (UI Events §3.4.4, <c>deltaMode</c>).</summary>
public enum WheelDeltaMode : byte
{
    /// <summary>CSS px.</summary>
    Pixel = 0,

    /// <summary>Lines of text.</summary>
    Line = 1,

    /// <summary>Pages.</summary>
    Page = 2,
}

/// <summary>A UI Events <c>WheelEvent</c> (UI Events §3.4.4). Positive deltas scroll content toward the start (the page moves down for a positive <see cref="DeltaY"/>).</summary>
public sealed class WheelEvent : MouseEvent
{
    /// <summary>Creates a wheel event.</summary>
    /// <param name="type">The type, <c>wheel</c>.</param>
    public WheelEvent(string type = "wheel")
        : base(type, true, true)
    {
    }

    /// <summary>The horizontal delta in <see cref="DeltaMode"/> units.</summary>
    public double DeltaX { get; internal set; }

    /// <summary>The vertical delta in <see cref="DeltaMode"/> units.</summary>
    public double DeltaY { get; internal set; }

    /// <summary>The z delta in <see cref="DeltaMode"/> units.</summary>
    public double DeltaZ { get; internal set; }

    /// <summary>The unit of the deltas.</summary>
    public WheelDeltaMode DeltaMode { get; internal set; }
}

/// <summary>A UI Events <c>KeyboardEvent</c> (UI Events §3.7).</summary>
public sealed class KeyboardEvent : UIEvent
{
    /// <summary>Creates a keyboard event.</summary>
    /// <param name="type">The type: <c>keydown</c> or <c>keyup</c>.</param>
    public KeyboardEvent(string type)
        : base(type, true, true)
    {
    }

    /// <summary>The key value (UI Events Key Values), such as <c>a</c>, <c>Enter</c>, <c>ArrowDown</c>.</summary>
    public string Key { get; internal set; } = string.Empty;

    /// <summary>The physical key code (UI Events Code Values), such as <c>KeyA</c>.</summary>
    public string Code { get; internal set; } = string.Empty;

    /// <summary>True when the key is held down and repeating.</summary>
    public bool Repeat { get; internal set; }

    /// <summary>True during IME composition.</summary>
    public bool IsComposing { get; internal set; }

    /// <summary>0 standard, 1 left, 2 right, 3 numpad.</summary>
    public int Location { get; internal set; }

    /// <summary>The modifier keys held.</summary>
    public KeyModifiers Modifiers { get; internal set; }

    /// <summary>True when Alt was held.</summary>
    public bool AltKey => (Modifiers & KeyModifiers.Alt) != 0;

    /// <summary>True when Control was held.</summary>
    public bool CtrlKey => (Modifiers & KeyModifiers.Control) != 0;

    /// <summary>True when Meta was held.</summary>
    public bool MetaKey => (Modifiers & KeyModifiers.Meta) != 0;

    /// <summary>True when Shift was held.</summary>
    public bool ShiftKey => (Modifiers & KeyModifiers.Shift) != 0;
}
