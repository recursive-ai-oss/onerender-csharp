namespace OneRender.Forms;

/// <summary>The kinds of form control the engine sizes and paints itself (HTML §15.5).</summary>
internal enum ControlKind : byte
{
    /// <summary>Not a control, or one the engine leaves to ordinary CSS (hidden, image, file, color and date inputs).</summary>
    None,

    /// <summary>A single-line text field: text, search, tel, url, email and number inputs, and input types the engine does not know.</summary>
    TextField,

    /// <summary><c>input type=password</c>: a text field whose value shows as bullets.</summary>
    Password,

    /// <summary><c>input type=button</c>, <c>submit</c> or <c>reset</c>: a push button whose label is its value.</summary>
    ButtonInput,

    /// <summary><c>input type=checkbox</c>.</summary>
    Checkbox,

    /// <summary><c>input type=radio</c>.</summary>
    Radio,

    /// <summary><c>input type=range</c>.</summary>
    Range,

    /// <summary><c>textarea</c>.</summary>
    TextArea,

    /// <summary><c>button</c>: a push button whose label is its content.</summary>
    Button,

    /// <summary>A <c>select</c> shown as a drop-down (no <c>multiple</c>, no <c>size</c> above 1).</summary>
    SelectMenu,

    /// <summary>A <c>select</c> shown as a list box.</summary>
    SelectList,

    /// <summary><c>progress</c>.</summary>
    Progress,

    /// <summary><c>meter</c>.</summary>
    Meter,
}
