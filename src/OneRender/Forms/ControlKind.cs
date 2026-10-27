namespace OneRender.Forms;

/// <summary>The kinds of form control the engine sizes and paints itself (HTML §15.5).</summary>
public enum ControlKind : byte
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

    /// <summary>An <c>option</c> of a list box; a selected one shows the selection color.</summary>
    Option,

    /// <summary><c>fieldset</c>: its top border runs through the middle of the rendered legend, with a gap for it.</summary>
    Fieldset,

    /// <summary>The rendered legend of a fieldset (its first <c>legend</c> child).</summary>
    Legend,
}

/// <summary>The state of a control that the way it is drawn depends on.</summary>
[Flags]
public enum ControlStates : ushort
{
    /// <summary>No state.</summary>
    None = 0,

    /// <summary>A checked checkbox or radio button; a selected option.</summary>
    Checked = 1,

    /// <summary>An indeterminate checkbox, or a progress bar without a value.</summary>
    Indeterminate = 2,

    /// <summary>A disabled control: drawn in the washed-out colors.</summary>
    Disabled = 4,

    /// <summary>A read-only text field.</summary>
    ReadOnly = 8,

    /// <summary>A meter whose value is in the suboptimum region (drawn yellow).</summary>
    MeterSuboptimum = 16,

    /// <summary>A meter whose value is in the even-less-good region (drawn red).</summary>
    MeterBad = 32,

    /// <summary>The control is the pressed one.</summary>
    Pressed = 64,

    /// <summary>The select is open (its popup is showing).</summary>
    Open = 128,
}
