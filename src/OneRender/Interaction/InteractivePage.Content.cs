using System.Text;
using OneRender.Forms;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Selectors;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    // The document the cascade is made for. It starts as the page's own document and, after each text or class change, is the next
    // snapshot: the same tree shape and node indices, with the new characters or class attribute. The page's own document (what hosts
    // hold node handles from) never changes, so handles, listeners, focus, hover and form state stay valid.
    private Document? _snapshot;

    private Document Snapshot => _snapshot ?? _page.Document;

    /// <summary>The element's text now: the characters of its text children, including changes made with <see cref="SetTextContent"/> (a <see cref="Node"/> handle still reads the text the page was created with).</summary>
    /// <param name="element">An element of the page.</param>
    /// <returns>The concatenated text of the element and its descendants (DOM <c>textContent</c>); empty for the null node.</returns>
    public string GetTextContent(Node element)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, _page.Document))
        {
            return string.Empty;
        }

        Node live = Snapshot.GetNode(element.Index);
        if (_page.TextOverrides is not { Count: > 0 } overrides)
        {
            return live.TextContent ?? string.Empty;
        }

        var sb = new StringBuilder();
        foreach (Node n in live.Descendants)
        {
            if (n.Kind == NodeKind.Text)
            {
                sb.Append(overrides.TryGetValue(n.Index, out string? replaced) ? replaced : n.Data.ToString());
            }
        }

        return sb.ToString();
    }

    /// <summary>The element's <c>class</c> attribute now, including changes made through this page.</summary>
    /// <param name="element">An element of the page.</param>
    /// <returns>The attribute's value; empty when absent or for the null node.</returns>
    public string GetClassName(Node element)
    {
        if (element.IsNull || !element.IsElement || !ReferenceEquals(element.Document, _page.Document))
        {
            return string.Empty;
        }

        return Snapshot.GetNode(element.Index).GetAttribute("class") ?? string.Empty;
    }

    /// <summary>
    /// Replaces the text of an element that holds one text node, as a script setting <c>textContent</c> would. The page restyles, lays
    /// out and repaints only what the new text changes (an element with a text child that stays a text child, no generated content,
    /// is updated in place; anything else is built again from the edited document). Focus, hover, scroll positions, form state and
    /// listeners stay.
    /// </summary>
    /// <remarks>
    /// The result equals a page created from the equivalent HTML. The cascade does not read text, so the new characters are kept
    /// beside the document and shown without restyling: once warm this allocates nothing when the new text fits the room the old text
    /// had. (A stylesheet with a <c>:empty</c> selector can read text, so then the change goes through OneHtml's document editor, which
    /// re-parses the document source and allocates in proportion to its size.) The document keeps its tree shape: an element with no
    /// text node, several children or element children, and an empty replacement (which would remove the text node), are refused;
    /// give the element a text child (a single space shows nothing) to start from. The <see cref="Node"/> handles you hold keep
    /// reading the text the page was created with; <see cref="GetTextContent"/> reads the current text.
    /// </remarks>
    /// <param name="element">An element of this page's document with exactly one child, a text node.</param>
    /// <param name="text">The new text, not markup. Not empty.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns><see cref="InputOutcome.Relaid"/> when the text changed; <see cref="InputOutcome.None"/> when it was already that text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException">The element is null, not an element, or belongs to another document; or <paramref name="text"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The element does not hold exactly one text node, or is not an HTML element or is a <c>style</c>, <c>script</c>, <c>textarea</c>, <c>title</c>, <c>option</c> or <c>template</c> element.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome SetTextContent(Node element, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        CheckOwnElement(element);
        if (text.Length == 0)
        {
            throw new ArgumentException("An empty text would remove the text node; use a single space to show nothing.", nameof(text));
        }

        Node live = Snapshot.GetNode(element.Index);
        Node only = live.FirstChild;
        if (only.IsNull || only.Kind != NodeKind.Text || !only.NextSibling.IsNull)
        {
            throw new InvalidOperationException("The element must hold exactly one text node.");
        }

        string tag = live.LocalName;
        if (tag is "style" or "script" or "textarea" or "title" or "option" or "template")
        {
            throw new InvalidOperationException("The text of a <" + tag + "> element is not content that can be edited this way.");
        }

        if (live.Namespace != ElementNamespace.Html)
        {
            throw new InvalidOperationException("Only the text of HTML elements can be edited this way.");
        }

        Dictionary<int, string>? overrides = _page.TextOverrides;
        ReadOnlySpan<char> now = overrides is not null && overrides.TryGetValue(only.Index, out string? shown) ? shown : only.Data;
        if (now.SequenceEqual(text))
        {
            return InputOutcome.None;
        }

        if (UsesEmptySelector())
        {
            // A selector may read whether the element has text, so the cascade needs the edited snapshot; drop an older override of this node.
            overrides?.Remove(only.Index);
            return ApplyEdit(Edit.ReplaceText(only, text), element.Index, text, cancellationToken);
        }

        // The cascade does not read text, so the characters are kept beside the document (rebuilds read them from there) and the page
        // just shows them: no new snapshot, no restyle.
        InputOutcome outcome = FlushStates(cancellationToken);
        (_page.TextOverrides ??= [])[only.Index] = text;
        outcome |= ShowText(element.Index, text, cancellationToken);
        RefreshHover();
        return outcome | FlushStates(cancellationToken);
    }

    private System.Collections.Generic.IReadOnlyList<StyleSheetInfo>? _scannedSheets;
    private bool _emptySelector;

    // Whether any sheet has a `:empty` selector, the one thing in a cascade that reads whether an element has text.
    private bool UsesEmptySelector()
    {
        IReadOnlyList<StyleSheetInfo> sheets = _view.Sheets;
        if (!ReferenceEquals(_scannedSheets, sheets))
        {
            _scannedSheets = sheets;
            _emptySelector = false;
            foreach (StyleSheetInfo info in sheets)
            {
                if (info.Sheet.Text.Contains(":empty", StringComparison.OrdinalIgnoreCase))
                {
                    _emptySelector = true;
                    break;
                }
            }
        }

        return _emptySelector;
    }

    /// <summary>Sets the element's <c>class</c> attribute (<c>className</c>); the page restyles and relays out only what the new classes change.</summary>
    /// <param name="element">An element of this page's document.</param>
    /// <param name="className">The attribute's value; empty keeps an empty attribute.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns><see cref="InputOutcome.Relaid"/> when the attribute changed; <see cref="InputOutcome.None"/> when it already had that value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="className"/> is null.</exception>
    /// <exception cref="ArgumentException">The element is null, not an element, or belongs to another document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome SetClassName(Node element, string className, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(className);
        CheckOwnElement(element);
        Node live = Snapshot.GetNode(element.Index);
        if (string.Equals(live.GetAttribute("class") ?? string.Empty, className, StringComparison.Ordinal) && live.HasAttribute("class"))
        {
            return InputOutcome.None;
        }

        return ApplyEdit(Edit.SetAttribute(live, "class", className), -1, null, cancellationToken);
    }

    /// <summary>Adds a class token to the element (<c>classList.add</c>); nothing happens when it is already there.</summary>
    /// <param name="element">An element of this page's document.</param>
    /// <param name="token">One class name: not empty, no ASCII white space.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns>What happened: <see cref="InputOutcome.Relaid"/> when the classes changed.</returns>
    /// <exception cref="ArgumentException">The element is invalid or the token is empty or holds white space.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome AddClass(Node element, string token, CancellationToken cancellationToken = default) =>
        ChangeClass(element, token, add: true, remove: false, cancellationToken);

    /// <summary>Removes a class token from the element (<c>classList.remove</c>); nothing happens when it is not there.</summary>
    /// <param name="element">An element of this page's document.</param>
    /// <param name="token">One class name: not empty, no ASCII white space.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns>What happened: <see cref="InputOutcome.Relaid"/> when the classes changed.</returns>
    /// <exception cref="ArgumentException">The element is invalid or the token is empty or holds white space.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome RemoveClass(Node element, string token, CancellationToken cancellationToken = default) =>
        ChangeClass(element, token, add: false, remove: true, cancellationToken);

    /// <summary>Adds the token when it is absent and removes it when present, or forces it on or off (<c>classList.toggle</c>).</summary>
    /// <param name="element">An element of this page's document.</param>
    /// <param name="token">One class name: not empty, no ASCII white space.</param>
    /// <param name="force">True to make sure the token is present, false to make sure it is absent, null (the default) to flip it.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns>What happened: <see cref="InputOutcome.Relaid"/> when the classes changed.</returns>
    /// <exception cref="ArgumentException">The element is invalid or the token is empty or holds white space.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome ToggleClass(Node element, string token, bool? force = null, CancellationToken cancellationToken = default)
    {
        CheckOwnElement(element);
        CheckToken(token);
        bool present = HasToken(Snapshot.GetNode(element.Index).GetAttribute("class"), token);
        bool want = force ?? !present;
        return ChangeClass(element, token, add: want, remove: !want, cancellationToken);
    }

    private InputOutcome ChangeClass(Node element, string token, bool add, bool remove, CancellationToken ct)
    {
        CheckOwnElement(element);
        CheckToken(token);
        Node live = Snapshot.GetNode(element.Index);
        string current = live.GetAttribute("class") ?? string.Empty;
        bool present = HasToken(current, token);
        if ((add && present) || (remove && !present))
        {
            return InputOutcome.None;
        }

        string next;
        if (add)
        {
            // DOM "ordered set serializer": the tokens in order, single-space separated.
            next = string.Join(' ', Tokens(current)) is { Length: > 0 } joined ? joined + " " + token : token;
        }
        else
        {
            // DOM "ordered set serializer": the remaining tokens in order, single-space separated.
            var sb = new StringBuilder(current.Length);
            foreach (string t in Tokens(current))
            {
                if (!t.Equals(token, StringComparison.Ordinal))
                {
                    sb.Append(sb.Length == 0 ? "" : " ").Append(t);
                }
            }

            next = sb.ToString();
        }

        return ApplyEdit(Edit.SetAttribute(live, "class", next), -1, null, ct);
    }

    private static List<string> Tokens(string value)
    {
        var list = new List<string>();
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && IsAsciiSpace(value[i]))
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !IsAsciiSpace(value[i]))
            {
                i++;
            }

            if (i > start)
            {
                string t = value.Substring(start, i - start);
                if (!list.Contains(t))
                {
                    list.Add(t);
                }
            }
        }

        return list;
    }

    private static bool HasToken(string? value, string token) => !string.IsNullOrEmpty(value) && Tokens(value).Contains(token);

    private static bool IsAsciiSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private static void CheckToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length == 0)
        {
            throw new ArgumentException("A class token cannot be empty.", nameof(token));
        }

        foreach (char c in token)
        {
            if (IsAsciiSpace(c))
            {
                throw new ArgumentException("A class token cannot contain white space.", nameof(token));
            }
        }
    }

    private void CheckOwnElement(Node element)
    {
        if (element.IsNull || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element.", nameof(element));
        }

        if (!ReferenceEquals(element.Document, _page.Document))
        {
            throw new ArgumentException("The element belongs to another document.", nameof(element));
        }
    }

    /// <summary>An attribute's current value, including changes made through this page (DOM <c>getAttribute</c>).</summary>
    /// <param name="element">An element of the page.</param>
    /// <param name="name">The attribute name; compared as ASCII lower case, as HTML elements do.</param>
    /// <returns>The value, or null when the attribute is absent or the element is the null node.</returns>
    public string? GetAttribute(Node element, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (element.IsNull || !element.IsElement || !ReferenceEquals(element.Document, _page.Document))
        {
            return null;
        }

        return Snapshot.GetNode(element.Index).GetAttribute(AsciiLower(name));
    }

    /// <summary>
    /// Sets an attribute of an HTML element, as <c>setAttribute</c> does, and brings the page up to date. <c>class</c> takes the path of
    /// <see cref="SetClassName"/>. Every other attribute builds the page again from the edited document (the result is a page equal to one
    /// created from the equivalent HTML) and keeps focus, hover, scroll positions and listeners. See <c>docs/game-hosts.md</c> for what
    /// each kind of attribute does and costs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What follows from the new document: <c>disabled</c> (the control and, on a <c>fieldset</c>, its controls: focusability, tab order,
    /// clicks, <c>:disabled</c> styling and painting), <c>hidden</c>, <c>style</c>, <c>href</c>, <c>tabindex</c>, <c>inert</c>,
    /// <c>src</c>, <c>value</c>, <c>placeholder</c>, <c>min</c>, <c>max</c>, <c>step</c>, <c>size</c>, <c>multiple</c>, <c>selected</c>,
    /// <c>checked</c>, <c>readonly</c>, <c>maxlength</c>, <c>id</c>, <c>name</c>, <c>form</c>, <c>for</c> and the rest of the markup the engine reads.
    /// A focused element that stops being focusable loses focus (HTML focus fixup).
    /// </para>
    /// <para>
    /// Care: <c>checked</c>, <c>value</c> and <c>selected</c> are the control's default state. A control the user (or <c>SetChecked</c>,
    /// <c>SetValue</c>) has already changed keeps that state, as in a browser, so the attribute shows only when nothing changed it.
    /// <c>type</c> changes what kind of control an <c>input</c> is; the control's user-changed state is dropped, and the change is refused
    /// while the control has focus. <c>id</c>, <c>name</c>, <c>form</c> and <c>for</c> move controls between forms and radio groups, which
    /// the tab order and arrow keys follow.
    /// </para>
    /// <para>
    /// Refused with <see cref="InvalidOperationException"/>: elements that are not HTML elements (inline SVG), and <c>link</c>,
    /// <c>style</c>, <c>script</c>, <c>base</c>, <c>meta</c>, <c>template</c> and <c>head</c> elements, whose attributes load or change
    /// style sheets and base addresses. Refused with <see cref="ArgumentException"/>: an empty or malformed attribute name.
    /// </para>
    /// </remarks>
    /// <param name="element">An HTML element of this page's document.</param>
    /// <param name="name">The attribute name (ASCII letters, digits, <c>-</c>, <c>_</c>, <c>:</c> and <c>.</c>); lower-cased.</param>
    /// <param name="value">The value; may be empty (a boolean attribute is set with an empty value).</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns><see cref="InputOutcome.Relaid"/> when the attribute changed; <see cref="InputOutcome.None"/> when it already had that value.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The element is invalid or the name is malformed.</exception>
    /// <exception cref="InvalidOperationException">The element or attribute is refused, or the value cannot be written to the document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome SetAttribute(Node element, string name, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        string lower = CheckAttributeEdit(element, name);
        if (lower == "class")
        {
            return SetClassName(element, value, cancellationToken);
        }

        Node live = Snapshot.GetNode(element.Index);
        if (live.HasAttribute(lower) && string.Equals(live.GetAttribute(lower), value, StringComparison.Ordinal))
        {
            return InputOutcome.None;
        }

        BeforeAttributeEdit(element, lower);
        return ApplyEdit(Edit.SetAttribute(live, lower, value), -1, null, cancellationToken, rebuild: !PatchesInPlace(lower), () => RefreshSession(element));
    }

    /// <summary>Removes an attribute of an HTML element, as <c>removeAttribute</c> does; see <see cref="SetAttribute"/> for what follows and what is refused.</summary>
    /// <param name="element">An HTML element of this page's document.</param>
    /// <param name="name">The attribute name; lower-cased.</param>
    /// <param name="cancellationToken">Cancels the restyle and relayout.</param>
    /// <returns><see cref="InputOutcome.Relaid"/> when the attribute was removed; <see cref="InputOutcome.None"/> when it was absent.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The element is invalid or the name is malformed.</exception>
    /// <exception cref="InvalidOperationException">The element or attribute is refused.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public InputOutcome RemoveAttribute(Node element, string name, CancellationToken cancellationToken = default)
    {
        string lower = CheckAttributeEdit(element, name);
        Node live = Snapshot.GetNode(element.Index);
        if (!live.HasAttribute(lower))
        {
            return InputOutcome.None;
        }

        BeforeAttributeEdit(element, lower);
        return ApplyEdit(Edit.RemoveAttribute(live, lower), -1, null, cancellationToken, rebuild: !PatchesInPlace(lower), () => RefreshSession(element));
    }

    // Attributes whose effect reaches the page only through the cascade, so the changed elements are restyled in place. The rest
    // (anything a control or replaced element reads when its boxes are made) build the page again from the edited document.
    private static bool PatchesInPlace(string lower) => lower is "class" or "disabled" or "style" or "hidden" or "title" or "tabindex" or "inert" or "href" or "role" or "lang";

    private string CheckAttributeEdit(Node element, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        CheckOwnElement(element);
        if (name.Length == 0 || name.Length > 128)
        {
            throw new ArgumentException("The attribute name must be 1 to 128 characters.", nameof(name));
        }

        foreach (char c in name)
        {
            bool ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or ':' or '.';
            if (!ok)
            {
                throw new ArgumentException("The attribute name has a character that is not allowed: letters, digits, '-', '_', ':' and '.'.", nameof(name));
            }
        }

        Node live = Snapshot.GetNode(element.Index);
        if (live.Namespace != ElementNamespace.Html)
        {
            throw new InvalidOperationException("Only the attributes of HTML elements can be edited.");
        }

        if (live.LocalName is "link" or "style" or "script" or "base" or "meta" or "template" or "head")
        {
            throw new InvalidOperationException("The attributes of <" + live.LocalName + "> elements load or change style sheets and base addresses and cannot be edited.");
        }

        return AsciiLower(name);
    }

    // A control that becomes another kind of control starts again: what the user changed in it no longer applies.
    private void BeforeAttributeEdit(Node element, string lower)
    {
        if (lower != "type" || !FocusRules.IsHtml(element, "input"))
        {
            return;
        }

        if (_focused.Equals(element) || _editElement.Equals(element))
        {
            throw new InvalidOperationException("The type of a focused control cannot be changed; blur it first.");
        }

        _page.Form.ClearElement(element);
        if (_live is not null)
        {
            Node inSnapshot = InSnapshot(element);
            _live.ApplyFormState([FormStateChange.Checked(inSnapshot, null), FormStateChange.Indeterminate(inSnapshot, null)]);
            _viewStale = true;
        }
    }

    // A text control the user has not changed follows its value attribute (HTML: the dirty value flag is false); one they changed keeps
    // its text. Either way the attributes the session reads (placeholder, readonly, maxlength) are read again.
    private void RefreshSession(Node element)
    {
        if (!_page.Form.TryGetText(element, out TextEditSession? s))
        {
            return;
        }

        ControlKind kind = HtmlFrontEnd.Classify(element);
        if (s.Editor.Text.SequenceEqual(s.Initial))
        {
            s.Editor.SetValue(HtmlFrontEnd.InitialValue(element, kind));
            s.Editor.Select(0, 0);
            s.Initial = s.Editor.Text.ToString();
        }

        s.Placeholder = HtmlFrontEnd.PlaceholderOf(element, kind);
        ReadControlAttributes(element, s);
        s.RebuildDisplay();
        _page.Form.SetText(element, s);
    }

    private static string AsciiLower(string s)
    {
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return string.Create(s.Length, s, static (span, src) =>
                {
                    for (int i = 0; i < src.Length; i++)
                    {
                        char ch = src[i];
                        span[i] = ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch;
                    }
                });
            }
        }

        return s;
    }

    // Applies one edit to the cascade's snapshot, restyles from what the edit reaches, and updates the page from it.
    private InputOutcome ApplyEdit(Edit edit, int textElement, string? text, CancellationToken ct, bool rebuild = false, Action? afterSnapshot = null)
    {
        // The page must show the view the edit starts from, or ChangedElements would not say what differs from the screen.
        InputOutcome outcome = FlushStates(ct);
        Document before = Snapshot;
        EditResult result = before.Apply([edit], null, ct);
        if (!result.Succeeded || result.Document is null)
        {
            throw new InvalidOperationException(result.Failure ?? "The edit could not be applied.");
        }

        if (result.Document.NodeCount != before.NodeCount || result.Changes!.Added.Count != 0 || result.Changes.Removed.Count != 0)
        {
            throw new InvalidOperationException("The edit would change the shape of the document.");
        }

        // The edit starts from the cascade as it stands (hover, focus and form state included); the live cascade is made again from the result.
        DocumentStyles view = DocumentStylesEditing.Restyle(CurrentView(), result, ct).Styles;
        _snapshot = result.Document;
        DocumentSnapshots.Set(_page.Document, result.Document);
        _view = view;
        _live = null;
        _viewStale = false;
        afterSnapshot?.Invoke();
        outcome |= ShowStyles(view, false, true, textElement, text, ct, rebuild);
        // The content may have moved under a stationary pointer, as after a scroll: boundary events and :hover follow, as in a browser.
        RefreshHover();

        // HTML "focus fixup": a focused element that stopped being focusable (disabled, hidden, inert, no longer rendered) loses focus.
        if (!_focused.IsNull && !FocusRules.IsFocusable(_focused, _page.Styled))
        {
            outcome |= SetFocus(default, FocusReason.Script, false);
        }

        return outcome | FlushStates(ct);
    }
}
