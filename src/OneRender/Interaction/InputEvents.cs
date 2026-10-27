namespace OneRender.Interaction;

/// <summary>An Input Events Level 2 <c>InputEvent</c>: <c>beforeinput</c> and <c>input</c> on an editable control.</summary>
public sealed class InputEvent : UIEvent
{
    /// <summary>Creates an input event.</summary>
    /// <param name="type"><c>beforeinput</c> or <c>input</c>.</param>
    /// <param name="bubbles">Whether it bubbles.</param>
    /// <param name="cancelable">Whether <c>preventDefault</c> has an effect.</param>
    public InputEvent(string type, bool bubbles = true, bool cancelable = false)
        : base(type, bubbles, cancelable)
    {
    }

    /// <summary>The inserted text (<c>insertText</c>, <c>insertFromPaste</c>, composition events), or null when the operation carries none.</summary>
    public string? Data { get; internal set; }

    /// <summary>The kind of edit, as Input Events Level 2 §5.1.2 names it: <c>insertText</c>, <c>deleteContentBackward</c>, <c>historyUndo</c> and so on.</summary>
    public string InputType { get; internal set; } = string.Empty;

    /// <summary>True while an IME composition is in progress.</summary>
    public bool IsComposing { get; internal set; }
}

/// <summary>A UI Events <c>CompositionEvent</c>: <c>compositionstart</c>, <c>compositionupdate</c> and <c>compositionend</c> (UI Events §5.7).</summary>
public sealed class CompositionEvent : UIEvent
{
    /// <summary>Creates a composition event.</summary>
    /// <param name="type">The type.</param>
    public CompositionEvent(string type)
        : base(type, true, false)
    {
    }

    /// <summary>The selected text for <c>compositionstart</c>, the preedit for <c>compositionupdate</c>, the committed text for <c>compositionend</c>.</summary>
    public string Data { get; internal set; } = string.Empty;
}

/// <summary>Where the host's clipboard text comes from and goes to. The engine never touches a system clipboard.</summary>
public interface ITextClipboard
{
    /// <summary>The text to paste, or null when the clipboard holds none.</summary>
    /// <returns>The text.</returns>
    string? GetText();

    /// <summary>Receives text the user copied or cut.</summary>
    /// <param name="text">The text.</param>
    void SetText(string text);
}
