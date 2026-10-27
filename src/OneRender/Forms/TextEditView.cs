using OneRender.Painting;
using OneRender.Tree;

namespace OneRender.Forms;

/// <summary>
/// What the painter draws on top of an edited control: the selection highlight, the caret, the underline of an IME preedit, and the
/// horizontal scroll of a single-line field. A host that paints with <see cref="Painter"/> directly passes it in
/// <see cref="PaintOptions.Editing"/>; <see cref="Interaction.InteractivePage"/> and <see cref="Interaction.LiveTextEditor"/> keep one up to date.
/// Offsets are into the text the control displays (the value with the preedit, a password as bullets).
/// </summary>
public sealed class TextEditView
{
    /// <summary>The index of the control element in the document (<c>Node.Index</c>), or -1 when the control is a styled-tree node.</summary>
    public int SourceElement { get; internal set; } = -1;

    /// <summary>The control, for a styled tree built in code; the null node when <see cref="SourceElement"/> is used.</summary>
    public StyledNode ControlNode { get; internal set; }

    /// <summary>True for a multi-line control (<c>textarea</c>).</summary>
    public bool Multiline { get; internal set; }

    /// <summary>Start of the selected text; equal to <see cref="SelectionEnd"/> when nothing is selected.</summary>
    public int SelectionStart { get; internal set; }

    /// <summary>End of the selected text.</summary>
    public int SelectionEnd { get; internal set; }

    /// <summary>Where the caret is.</summary>
    public int CaretOffset { get; internal set; }

    /// <summary>False during the off half of the blink cycle and when the selection is not empty.</summary>
    public bool CaretVisible { get; internal set; }

    /// <summary>Start of the IME preedit, or -1 when no composition is in progress.</summary>
    public int CompositionStart { get; internal set; } = -1;

    /// <summary>End of the preedit.</summary>
    public int CompositionEnd { get; internal set; }

    /// <summary>Start of the preedit's target clause (drawn with a thicker underline); equal to <see cref="TargetEnd"/> when there is none.</summary>
    public int TargetStart { get; internal set; }

    /// <summary>End of the target clause.</summary>
    public int TargetEnd { get; internal set; }

    /// <summary>How far a single-line field's text is scrolled to the left, in CSS px. The painter keeps the caret in view and writes it back here.</summary>
    public double ScrollX { get; internal set; }

    /// <summary>The selection highlight color. Default Chrome's light-scheme selection blue.</summary>
    public PaintColor SelectionBackground { get; set; } = new(51 / 255f, 144 / 255f, 255 / 255f, 1f);

    /// <summary>The text color inside the selection; <see cref="PaintColor.IsTransparent"/> (the default) keeps the text's own color.</summary>
    public PaintColor SelectionForeground { get; set; }

    /// <summary>The caret color, or transparent for the text's own color (<c>caret-color: auto</c>).</summary>
    public PaintColor CaretColor { get; internal set; }

    /// <summary>The font line height used to place a caret in an empty field, in CSS px.</summary>
    public double LineHeight { get; internal set; }
}
