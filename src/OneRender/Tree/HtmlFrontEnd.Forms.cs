using OneRender.Forms;
using OneRender.Style;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Selectors;

namespace OneRender.Tree;

// Form controls (HTML §15.5). A control is an ordinary box whose widget size Chrome fixes from the font and attributes. The front end
// states that size as the control's own width, height or minimum height when the author left them auto, and puts the text a control
// shows (a value, a placeholder, the selected option) into the tree as an anonymous text node, so inline layout, baselines, clipping
// and painting of that text need no special cases.
public sealed partial class HtmlFrontEnd
{
    // Set by AddElement for the element it has just added: its children are the control's own insides and are not boxes.
    private bool _noDescend;

    // The form state the user changed (checkedness, a range value, a selection); null when the document's attributes are all there is.
    internal Forms.FormState? Form { get; set; }

    private static readonly SelectorList DisabledSelector = SelectorList.Parse(":disabled");
    private static readonly SelectorList CheckedSelector = SelectorList.Parse(":checked");
    private static readonly SelectorList IndeterminateSelector = SelectorList.Parse(":indeterminate");

    private static bool IsHtmlTag(Node element, string tag) => element.Namespace == ElementNamespace.Html && element.LocalName.Equals(tag, StringComparison.Ordinal);

    /// <summary>Which control an element is, for the sizing and painting rules; <see cref="ControlKind.None"/> for everything else.</summary>
    internal static ControlKind Classify(Node element)
    {
        if (element.Namespace != ElementNamespace.Html)
        {
            return ControlKind.None;
        }

        string tag = element.LocalName;
        switch (tag)
        {
            case "input":
                return ClassifyInput(element.GetAttribute("type"));
            case "textarea": return ControlKind.TextArea;
            case "button": return ControlKind.Button;
            case "select":
                return IsListbox(element) ? ControlKind.SelectList : ControlKind.SelectMenu;
            case "progress": return ControlKind.Progress;
            case "meter": return ControlKind.Meter;
            case "fieldset": return ControlKind.Fieldset;
            default: return ControlKind.None;
        }
    }

    internal static ControlKind ClassifyInput(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return ControlKind.TextField;
        }

        return type.ToLowerInvariant() switch
        {
            "hidden" or "image" or "file" or "color" or "date" or "datetime-local" or "month" or "week" or "time" => ControlKind.None,
            "checkbox" => ControlKind.Checkbox,
            "radio" => ControlKind.Radio,
            "range" => ControlKind.Range,
            "button" or "submit" or "reset" => ControlKind.ButtonInput,
            "password" => ControlKind.Password,
            _ => ControlKind.TextField,
        };
    }

    // HTML §4.10.7: a select with the multiple attribute, or a size attribute above 1, is a list box.
    private static bool IsListbox(Node select) => select.HasAttribute("multiple") || ListSize(select) > 1;

    private static int ListSize(Node select)
    {
        string? s = select.GetAttribute("size");
        return s is not null && TryParsePositive(s, out int n) ? n : 0;
    }

    private static bool TryParsePositive(ReadOnlySpan<char> s, out int value)
    {
        s = s.Trim();
        value = 0;
        if (s.IsEmpty || s.Length > 6)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                // HTML §2.3.4.1: parsing stops at the first character that is not a digit.
                break;
            }

            value = (value * 10) + (c - '0');
        }

        return value > 0;
    }

    /// <summary>
    /// Returns the style of a control element with its widget size applied: the used width and height Chrome gives an auto-sized
    /// control (probe tables in <c>docs/forms.md</c>), and for a select the room its inner text needs.
    /// </summary>
    private StyleId AdjustControlStyle(Node element, StyleId style, StyleId parentStyle)
    {
        ControlKind kind = Classify(element);
        if (kind == ControlKind.None)
        {
            if (IsHtmlTag(element, "option"))
            {
                return AdjustOption(element, style);
            }

            return IsRenderedLegend(element) ? AdjustLegend(style, parentStyle) : style;
        }

        StyleTable table = _tree.Styles;
        ComputedValues v = table[style];
        if (v.Display.IsNone)
        {
            return style;
        }

        Layout.IFontProvider? fonts = table.Fonts;
        bool borderBox = v.BoxSizing == BoxSizingKind.BorderBox;
        bool native = v.Appearance == AppearanceKind.Auto;
        switch (kind)
        {
            case ControlKind.TextField:
            case ControlKind.Password:
            {
                int size = 20;
                string? sa = element.GetAttribute("size");
                if (sa is not null && TryParsePositive(sa, out int n))
                {
                    size = n;
                }

                double line = FormMetrics.UsedLineHeight(fonts, in v);
                SetContentWidth(ref v, FormMetrics.TextFieldWidth(fonts, in v, size), borderBox);
                if (v.MinHeight.IsAuto)
                {
                    v.MinHeight = Length.FromPx(line + (borderBox ? FormMetrics.Extra(in v, false) : 0));
                }

                SingleLine(ref v);
                CenterVertically(ref v, line, borderBox);
                break;
            }

            case ControlKind.ButtonInput:
                if (v.MinHeight.IsAuto)
                {
                    v.MinHeight = Length.FromPx(FormMetrics.UsedLineHeight(fonts, in v) + (borderBox ? FormMetrics.Extra(in v, false) : 0));
                }

                SingleLine(ref v);
                CenterVertically(ref v, FormMetrics.UsedLineHeight(fonts, in v), borderBox);
                break;
            case ControlKind.Button:
                CenterVertically(ref v, FormMetrics.LineSpacing(fonts, in v, withGap: true), borderBox);
                break;
            case ControlKind.Checkbox:
            case ControlKind.Radio:
                if (native)
                {
                    SetContentWidth(ref v, 13, borderBox);
                    SetContentHeight(ref v, 13, borderBox);
                    v.BaselineAtBorderEdge = true;
                }

                break;
            case ControlKind.Range:
                if (native)
                {
                    SetContentWidth(ref v, 129, borderBox);
                    SetContentHeight(ref v, 16, borderBox);
                    v.BaselineAtBorderEdge = true;
                }

                break;
            case ControlKind.TextArea:
            {
                int cols = 20, rows = 2;
                string? ca = element.GetAttribute("cols");
                if (ca is not null && TryParsePositive(ca, out int c))
                {
                    cols = c;
                }

                string? ra = element.GetAttribute("rows");
                if (ra is not null && TryParsePositive(ra, out int r))
                {
                    rows = r;
                }

                bool bar = v.OverflowY is OverflowKind.Auto or OverflowKind.Scroll;
                SetContentWidth(ref v, FormMetrics.TextAreaWidth(fonts, in v, cols, bar), borderBox);
                SetContentHeight(ref v, rows * FormMetrics.UsedLineHeight(fonts, in v), borderBox);
                break;
            }

            case ControlKind.SelectMenu:
                AdjustMenulist(element, ref v, fonts, borderBox);
                break;
            case ControlKind.SelectList:
            {
                int rows = ListSize(element);
                if (rows <= 1)
                {
                    rows = 4; // HTML §15.5.14: a multiple select without a size shows four rows.
                }

                // Probe: a row is the larger of the line and 1.2em, plus the 1 px under each option's text; an empty list's rows have no extra.
                double row = Math.Max(FormMetrics.LineSpacing(fonts, in v, withGap: true), Math.Floor(1.2 * v.FontSize * 64) / 64);
                SetContentHeight(ref v, rows * (row + (element.FirstChild.IsNull ? 0 : 1)), borderBox);
                break;
            }
        }

        StampControl(element, kind, ref v);
        if (kind is ControlKind.TextField or ControlKind.Password or ControlKind.ButtonInput or ControlKind.SelectMenu)
        {
            v.InnerBaseline = InnerBaselineOf(fonts, in v, kind == ControlKind.SelectMenu);
        }

        return table.Derive(style, in v);
    }

    // What the native look of a control depends on, from its attributes and the state the user changed (HTML §4.10.5.1 and §4.10.13-14).
    private void StampControl(Node element, ControlKind kind, ref ComputedValues v)
    {
        v.Control = kind;
        ControlStates st = ControlStates.None;
        if (element.Matches(DisabledSelector))
        {
            st |= ControlStates.Disabled;
        }

        if (element.HasAttribute("readonly"))
        {
            st |= ControlStates.ReadOnly;
        }

        v.ControlFraction = 0;
        switch (kind)
        {
            case ControlKind.Checkbox:
            case ControlKind.Radio:
                if (Form is not null && Form.TryGetChecked(element, out bool on) ? on : element.Matches(CheckedSelector))
                {
                    st |= ControlStates.Checked;
                }

                if (kind == ControlKind.Checkbox && Form is not null && Form.IsIndeterminate(element))
                {
                    st |= ControlStates.Indeterminate;
                }

                break;
            case ControlKind.Range:
                v.ControlFraction = Form is not null && Form.TryGetRangeFraction(element, out double rf)
                    ? rf
                    : FormValues.RangeFraction(element.GetAttribute("value"), element.GetAttribute("min"), element.GetAttribute("max"), element.GetAttribute("step"));
                break;
            case ControlKind.Progress:
                v.ControlFraction = FormValues.ProgressFraction(element.GetAttribute("value"), element.GetAttribute("max"));
                if (double.IsNaN(v.ControlFraction))
                {
                    st |= ControlStates.Indeterminate;
                    v.ControlFraction = 0;
                }

                break;
            case ControlKind.Meter:
                v.ControlFraction = FormValues.MeterFraction(element.GetAttribute("value"), element.GetAttribute("min"), element.GetAttribute("max"), element.GetAttribute("low"), element.GetAttribute("high"), element.GetAttribute("optimum"), out FormValues.MeterRegion region);
                st |= region == FormValues.MeterRegion.Suboptimum ? ControlStates.MeterSuboptimum : region == FormValues.MeterRegion.EvenLessGood ? ControlStates.MeterBad : ControlStates.None;
                break;
        }

        if (Form is not null && Form.IsOpen(element))
        {
            st |= ControlStates.Open;
        }

        v.ControlState = st;
    }

    // A selected option of a list box shows the selection color (Chrome: #cbcbcb while the list is unfocused).
    private StyleId AdjustOption(Node option, StyleId style)
    {
        Node select = option.Parent;
        if (!select.IsNull && IsHtmlTag(select, "optgroup"))
        {
            select = select.Parent;
        }

        if (select.IsNull || !IsHtmlTag(select, "select") || !IsListbox(select) || !IsOptionSelected(option))
        {
            return style;
        }

        StyleTable table = _tree.Styles;
        ComputedValues v = table[style];
        v.Control = ControlKind.Option;
        v.ControlState = ControlStates.Checked;
        if (v.BackgroundColor.A == 0)
        {
            v.BackgroundColor = new ColorValue(203 / 255f, 203 / 255f, 203 / 255f, 1);
        }

        return table.Derive(style, in v);
    }

    private bool IsOptionSelected(Node option) => Form is not null && Form.TryGetSelected(option, out bool on) ? on : option.HasAttribute("selected");

    // HTML §15.3.13: a fieldset's rendered legend is not part of the fieldset's own layout (a grid or flex container lays out only the
    // content that follows it), so for `display: grid | flex` the fieldset becomes a block and everything but the legend goes into an
    // anonymous box that carries the original display and its flex or grid properties. Probe: a grid fieldset's first row starts after the
    // legend and the fieldset's padding, with no grid gap between them (daisyUI's fieldset; docs/forms.md).
    private readonly Dictionary<int, int> _fieldsetOrig = [];
    private readonly Dictionary<int, int> _wrapperOf = [];

    private StyleId SplitFieldset(Node element, int idx, StyleId style)
    {
        if (!IsHtmlTag(element, "fieldset"))
        {
            return style;
        }

        StyleTable table = _tree.Styles;
        ComputedValues v = table[style];
        if (v.Display.IsNone || v.Display.Inside is not (InnerDisplay.Flex or InnerDisplay.Grid))
        {
            _fieldsetOrig.Remove(idx);
            return style;
        }

        _fieldsetOrig[idx] = style.Index;
        v.Display = new DisplayType(v.Display.Outside, InnerDisplay.Flow);
        return table.Derive(style, in v);
    }

    // The content box's style: what the fieldset inherits, with the fieldset's own display and the properties that arrange its children.
    private StyleId FieldsetContentStyle(StyleId fieldsetStyle, int fieldset)
    {
        StyleTable table = _tree.Styles;
        if (!_fieldsetOrig.TryGetValue(fieldset, out int origIndex))
        {
            return fieldsetStyle;
        }

        ComputedValues o = table[new StyleId(origIndex)];
        StyleId anon = table.GetAnonymous(fieldsetStyle, new DisplayType(OuterDisplay.Block, o.Display.Inside));
        ComputedValues v = table[anon];
        v.FlexDirection = o.FlexDirection;
        v.FlexWrap = o.FlexWrap;
        v.JustifyContent = o.JustifyContent;
        v.JustifyContentOverflow = o.JustifyContentOverflow;
        v.AlignContent = o.AlignContent;
        v.AlignContentOverflow = o.AlignContentOverflow;
        v.AlignItems = o.AlignItems;
        v.AlignItemsOverflow = o.AlignItemsOverflow;
        v.JustifyItems = o.JustifyItems;
        v.JustifyItemsOverflow = o.JustifyItemsOverflow;
        v.RowGap = o.RowGap;
        v.ColumnGap = o.ColumnGap;
        v.GridTemplateColumns = o.GridTemplateColumns;
        v.GridTemplateRows = o.GridTemplateRows;
        v.GridTemplateAreas = o.GridTemplateAreas;
        v.GridAutoColumns = o.GridAutoColumns;
        v.GridAutoRows = o.GridAutoRows;
        v.GridAutoFlow = o.GridAutoFlow;
        v.GridAutoFlowDense = o.GridAutoFlowDense;
        return table.Derive(anon, in v);
    }

    private int EnsureFieldsetContent(ref Frame f)
    {
        if (f.Wrap < 0)
        {
            StyleId s = FieldsetContentStyle(_tree.StyleOf(f.Fieldset), f.Fieldset);
            f.Wrap = _tree.NewNode(StyledNodeKind.Element, f.Fieldset, s, -1);
            if (f.Wrap >= 0)
            {
                _wrapperOf[f.Wrap] = f.Fieldset;
            }
        }

        return f.Wrap >= 0 ? f.Wrap : f.Fieldset;
    }

    // The baseline of a line of the control's text, from the top of its content box: the rounded ascent, moved by half the difference
    // between the used line height and the face's line spacing. Probe: 12 px below the content top for Arial 13.33 px (input baseline 15).
    private static double InnerBaselineOf(Layout.IFontProvider? fonts, in ComputedValues v, bool menulist)
    {
        if (fonts is null)
        {
            return double.NaN;
        }

        double asc = FormMetrics.Round(fonts.GetMetrics(in v).Ascent);
        double natural = FormMetrics.LineSpacing(fonts, in v, withGap: !menulist);
        double used = menulist ? natural : FormMetrics.UsedLineHeight(fonts, in v);
        return asc + ((used - natural) / 2);
    }

    // HTML §15.3.13: the first legend child of a fieldset is the rendered legend. It sits over the fieldset's top border, so its box starts
    // at the fieldset's border edge and the fieldset's own padding comes after it. In flow terms: the legend's top margin cancels the
    // fieldset's top border and padding, and the padding comes back as `LegendGap`, space after the legend that does not collapse with the next box's margin. Probe: a fieldset with a 18 px legend and 18 px of
    // content is 53.59 px tall (18 + 5.6 + 18 + 10 + 2), with the legend at the fieldset's top and 24 px from its left edge.
    private StyleId AdjustLegend(StyleId style, StyleId parentStyle)
    {
        StyleTable table = _tree.Styles;
        ComputedValues p = table[parentStyle];
        ComputedValues v = table[style];
        double pad = FormMetrics.Px(p.PaddingTop);
        v.MarginTop = Length.FromPx(-(p.BorderTopWidth + pad));
        v.LegendGap = pad;
        v.Control = ControlKind.Legend;
        return table.Derive(style, in v);
    }

    private static bool IsRenderedLegend(Node element)
    {
        if (!IsHtmlTag(element, "legend") || element.Parent.IsNull || !IsHtmlTag(element.Parent, "fieldset"))
        {
            return false;
        }

        for (Node c = element.Parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Equals(element))
            {
                return true;
            }

            if (c.Kind == NodeKind.Element && IsHtmlTag(c, "legend"))
            {
                return false;
            }
        }

        return false;
    }

    // A select shown as a drop-down: its text is the selected option's, with 4 px before it and 16 px after (the arrow's room);
    // Probe: an empty select is 22 px wide with 1 px borders, one option "A" at 13.33 px is 31, and the width is the widest option's text
    // rounded up plus those 20 px. The height is the rounded ascent and descent plus 2 px of padding inside the borders.
    private static void AdjustMenulist(Node select, ref ComputedValues v, Layout.IFontProvider? fonts, bool borderBox)
    {
        double widest = 0;
        if (fonts is not null)
        {
            for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o))
            {
                string label = OptionLabel(o);
                if (label.Length > 0)
                {
                    widest = Math.Max(widest, fonts.Shape(label, in v).Width);
                }
            }
        }

        double inner = Math.Ceiling(widest);
        double line = FormMetrics.LineSpacing(fonts, in v, withGap: false);
        v.ControlInnerPadding = 20;
        v.PaddingLeft = Length.FromPx(FormMetrics.Px(v.PaddingLeft) + 4);
        v.PaddingRight = Length.FromPx(FormMetrics.Px(v.PaddingRight) + 16);
        v.PaddingTop = Length.FromPx(FormMetrics.Px(v.PaddingTop) + 1);
        v.PaddingBottom = Length.FromPx(FormMetrics.Px(v.PaddingBottom) + 1);
        SetContentWidth(ref v, inner, borderBox);
        SetContentHeight(ref v, line, borderBox);
        SingleLine(ref v);
        CenterVertically(ref v, line, borderBox);
    }

    // The inner text of a text field, button or drop-down is centered in the content box when the box is taller than the text (UA rule
    // align-content: center). Probe: input and button with height 50 px / 60 px have their text 17.5 px / 22 px below the top. Only a
    // height given as a length can be centered here, by moving the extra space into the top padding; the border box does not change.
    private static void CenterVertically(ref ComputedValues v, double lineHeight, bool borderBox)
    {
        if (v.Height.Kind != LengthKind.Px)
        {
            return;
        }

        double content = v.Height.Value - (borderBox ? FormMetrics.Extra(in v, false) : 0);
        double off = Math.Floor((content - lineHeight) / 2 * 64) / 64;
        if (off <= 0)
        {
            return;
        }

        v.PaddingTop = Length.FromPx(FormMetrics.Px(v.PaddingTop) + off);
        if (!borderBox)
        {
            v.Height = Length.FromPx(v.Height.Value - off);
        }
    }

    // A control's text stays on one line, in the way an <input> shows its value: no wrapping, spaces kept.
    private static void SingleLine(ref ComputedValues v)
    {
        v.WhiteSpaceCollapse = WhiteSpaceCollapseKind.Preserve;
        v.TextWrapMode = TextWrapModeKind.NoWrap;
    }

    private static void SetContentWidth(ref ComputedValues v, double content, bool borderBox)
    {
        v.ControlWidth = content;
        if (v.Width.IsAuto)
        {
            v.WidthIsIntrinsic = true;
            v.Width = Length.FromPx(content + (borderBox ? FormMetrics.Extra(in v, true) : 0));
        }
    }

    private static void SetContentHeight(ref ComputedValues v, double content, bool borderBox)
    {
        if (v.Height.IsAuto)
        {
            v.Height = Length.FromPx(content + (borderBox ? FormMetrics.Extra(in v, false) : 0));
        }
    }

    // ---- options ----

    private static Node FirstOption(Node select) => NextOptionFrom(select, select.FirstChild);

    private static Node NextOption(Node select, Node current)
    {
        // Options are children of the select or of an optgroup child of it (HTML §4.10.10).
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
            if (cursor.Kind == NodeKind.Element && cursor.Namespace == ElementNamespace.Html)
            {
                if (cursor.LocalName.Equals("option", StringComparison.Ordinal))
                {
                    return cursor;
                }

                if (cursor.LocalName.Equals("optgroup", StringComparison.Ordinal) && !cursor.FirstChild.IsNull)
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

    /// <summary>HTML §4.10.10: an option's label is its label attribute when that is not empty, otherwise its text with white space stripped and collapsed.</summary>
    private static string OptionLabel(Node option)
    {
        string? label = option.GetAttribute("label");
        if (!string.IsNullOrEmpty(label))
        {
            return label;
        }

        var sb = new System.Text.StringBuilder();
        for (Node c = option.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == NodeKind.Text)
            {
                sb.Append(c.Data);
            }
        }

        return CollapseSpaces(sb);
    }

    private static string CollapseSpaces(System.Text.StringBuilder sb)
    {
        var o = new System.Text.StringBuilder(sb.Length);
        bool space = true;
        for (int i = 0; i < sb.Length; i++)
        {
            char ch = sb[i];
            if (ch is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!space)
                {
                    o.Append(' ');
                }

                space = true;
            }
            else
            {
                o.Append(ch);
                space = false;
            }
        }

        if (o.Length > 0 && o[^1] == ' ')
        {
            o.Length--;
        }

        return o.ToString();
    }

    /// <summary>The option a drop-down shows: the last one with the selected attribute, else the first (HTML §4.10.7, selectedness setting algorithm).</summary>
    private Node SelectedOption(Node select)
    {
        Node first = default;
        Node chosen = default;
        for (Node o = FirstOption(select); !o.IsNull; o = NextOption(select, o))
        {
            if (first.IsNull)
            {
                first = o;
            }

            if (IsOptionSelected(o))
            {
                chosen = o;
            }
        }

        return chosen.IsNull ? first : chosen;
    }

    // ---- the text a control shows ----

    private void AddControlContent(Node element, int idx, ControlKind kind)
    {
        string? text = null;
        bool placeholder = false;
        switch (kind)
        {
            case ControlKind.TextField:
            case ControlKind.Password:
            {
                string value = Sanitize(element.GetAttribute("value"));
                if (value.Length == 0)
                {
                    value = Sanitize(element.GetAttribute("placeholder"));
                    placeholder = value.Length > 0;
                }
                else if (kind == ControlKind.Password)
                {
                    value = new string('\u2022', value.Length);
                }

                text = value;
                break;
            }

            case ControlKind.ButtonInput:
            {
                string? value = element.GetAttribute("value");
                string? type = element.GetAttribute("type");
                if (string.IsNullOrEmpty(value))
                {
                    // HTML §15.5.1: a submit button without a value shows a localized "Submit", a reset button "Reset".
                    value = type is not null && type.Equals("submit", StringComparison.OrdinalIgnoreCase) ? "Submit"
                        : type is not null && type.Equals("reset", StringComparison.OrdinalIgnoreCase) ? "Reset" : string.Empty;
                }

                text = Sanitize(value);
                break;
            }

            case ControlKind.TextArea:
            {
                // HTML §4.10.11: the placeholder shows while the value is empty. The value is the element's text.
                string? ph = element.GetAttribute("placeholder");
                if (!string.IsNullOrEmpty(ph) && element.FirstChild.IsNull)
                {
                    text = ph.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                    placeholder = true;
                }

                break;
            }

            case ControlKind.SelectMenu:
            {
                Node o = SelectedOption(element);
                text = o.IsNull ? string.Empty : OptionLabel(o);
                break;
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        StyleTable table = _tree.Styles;
        StyleId owner = _tree.StyleOf(idx);
        StyleId ts = table.GetAnonymous(owner, DisplayType.Inline);
        if (placeholder)
        {
            // Chrome's placeholder color (darkgray at 75% luminance).
            ComputedValues tv = table[ts];
            tv.Color = new ColorValue(117 / 255f, 117 / 255f, 117 / 255f, 1);
            ts = table.Derive(ts, in tv);
        }

        int t = _tree.NewNode(StyledNodeKind.Text, idx, ts, -1);
        if (t >= 0)
        {
            _tree.SetText(t, text);
        }
    }

    // HTML §4.10.5.1.x value sanitization algorithm for single-line fields: line breaks are removed.
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny('\r', '\n') < 0 ? value : value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
