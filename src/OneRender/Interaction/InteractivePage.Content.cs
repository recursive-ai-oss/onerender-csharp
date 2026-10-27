using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

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

    // Applies one edit to the cascade's snapshot, restyles from what the edit reaches, and updates the page from it.
    private InputOutcome ApplyEdit(Edit edit, int textElement, string? text, CancellationToken ct)
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

        DocumentStyles view = DocumentStylesEditing.Restyle(_view, result, ct).Styles;
        DocumentStyles baseStyles = ReferenceEquals(_view, _baseStyles) ? view : DocumentStylesEditing.Restyle(_baseStyles, result, ct).Styles;
        _snapshot = result.Document;
        _view = view;
        _baseStyles = baseStyles;
        outcome |= ShowStyles(view, false, true, textElement, text, ct);
        // The content may have moved under a stationary pointer, as after a scroll: boundary events and :hover follow, as in a browser.
        RefreshHover();
        return outcome | FlushStates(ct);
    }
}
