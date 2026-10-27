namespace OneRender.Interaction;

/// <summary>
/// The event of CSS Transitions 1 §6: <c>transitionrun</c>, <c>transitionstart</c>, <c>transitionend</c> and <c>transitioncancel</c>.
/// They bubble and cannot be canceled. The page reuses one instance per dispatch, so read what you need inside the listener.
/// </summary>
public sealed class TransitionEvent : DomEvent
{
    /// <summary>Creates a transition event.</summary>
    /// <param name="type">The event type, for example <c>transitionend</c>.</param>
    public TransitionEvent(string type)
        : base(type, true, false)
    {
    }

    /// <summary>The name of the transitioning property, as written in <c>transition-property</c> (lower case, for example <c>background-color</c>).</summary>
    public string PropertyName { get; internal set; } = string.Empty;

    /// <summary>The time the transition has been running, in seconds, not counting its delay (CSS Transitions 1 §6.1).</summary>
    public double ElapsedTime { get; internal set; }

    /// <summary>The pseudo-element the transition runs on, always empty: pseudo-elements do not transition.</summary>
    public string PseudoElement { get; internal set; } = string.Empty;
}

/// <summary>
/// The event of CSS Animations 1 §4.2: <c>animationstart</c>, <c>animationiteration</c>, <c>animationend</c> and <c>animationcancel</c>.
/// They bubble and cannot be canceled. The page reuses one instance per dispatch, so read what you need inside the listener.
/// </summary>
public sealed class AnimationEvent : DomEvent
{
    /// <summary>Creates an animation event.</summary>
    /// <param name="type">The event type, for example <c>animationend</c>.</param>
    public AnimationEvent(string type)
        : base(type, true, false)
    {
    }

    /// <summary>The value of <c>animation-name</c> that started the animation.</summary>
    public string AnimationName { get; internal set; } = string.Empty;

    /// <summary>The time the animation has been running, in seconds, not counting its delay (CSS Animations 1 §4.2).</summary>
    public double ElapsedTime { get; internal set; }

    /// <summary>The pseudo-element the animation runs on, always empty: pseudo-elements do not animate.</summary>
    public string PseudoElement { get; internal set; } = string.Empty;
}
