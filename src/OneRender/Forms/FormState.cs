using RecursiveAI.OneHtml;

namespace OneRender.Forms;

/// <summary>
/// What the user changed in a document's form controls, kept beside the document because a parsed document is read-only: the
/// checkedness of checkboxes and radio buttons, range values, which options are selected and which drop-down is open. The page reads
/// it when it styles a control, so a control with an entry shows that state instead of the one its attributes give.
/// </summary>
internal sealed class FormState
{
    private readonly Dictionary<int, bool> _checked = [];
    private readonly Dictionary<int, bool> _indeterminate = [];
    private readonly Dictionary<int, double> _range = [];
    private readonly Dictionary<int, bool> _selected = [];
    private readonly Dictionary<int, TextEditSession> _text = [];
    private int _open = -1;

    /// <summary>What the painter draws over the control being edited, or null.</summary>
    internal TextEditView? ActiveView { get; set; }

    /// <summary>Bumped on every change, so a page can tell whether it is up to date.</summary>
    public int Version { get; private set; }

    public bool TryGetChecked(Node element, out bool value) => _checked.TryGetValue(element.Index, out value);

    public void SetChecked(Node element, bool value)
    {
        _checked[element.Index] = value;
        _indeterminate.Remove(element.Index);
        Version++;
    }

    public bool IsIndeterminate(Node element) => _indeterminate.TryGetValue(element.Index, out bool v) && v;

    public void SetIndeterminate(Node element, bool value)
    {
        _indeterminate[element.Index] = value;
        Version++;
    }

    public bool TryGetRangeValue(Node element, out double value) => _range.TryGetValue(element.Index, out value);

    public void SetRangeValue(Node element, double value)
    {
        _range[element.Index] = value;
        Version++;
    }

    /// <summary>The editing session of a text control the user (or a script) has edited or focused.</summary>
    internal bool TryGetText(Node element, out TextEditSession session) => _text.TryGetValue(element.Index, out session!);

    internal void SetText(Node element, TextEditSession session)
    {
        _text[element.Index] = session;
        Version++;
    }

    /// <summary>Forgets what the user changed in one element (its type changed, so the state no longer applies).</summary>
    internal void ClearElement(Node element)
    {
        _checked.Remove(element.Index);
        _indeterminate.Remove(element.Index);
        _range.Remove(element.Index);
        _selected.Remove(element.Index);
        _text.Remove(element.Index);
        Version++;
    }

    /// <summary>Forgets what the user changed in the elements under <paramref name="root"/> (a form reset).</summary>
    public void ClearUnder(Node root)
    {
        RemoveUnder(_text, root);
        RemoveUnder(_checked, root);
        RemoveUnder(_indeterminate, root);
        RemoveUnder(_range, root);
        RemoveUnder(_selected, root);
        Version++;
    }

    private static void RemoveUnder<T>(Dictionary<int, T> map, Node root)
    {
        if (map.Count == 0)
        {
            return;
        }

        foreach (Node n in root.Descendants)
        {
            map.Remove(n.Index);
        }
    }

    public bool TryGetSelected(Node option, out bool value) => _selected.TryGetValue(option.Index, out value);

    public void SetSelected(Node option, bool value)
    {
        _selected[option.Index] = value;
        Version++;
    }

    public bool IsOpen(Node select) => _open == select.Index;

    /// <summary>The index of the node of the open drop-down, or -1.</summary>
    public int OpenIndex => _open;

    private int _highlight = -1;

    /// <summary>The option of the open drop-down that is highlighted (by the pointer or the keyboard), or -1.</summary>
    public int HighlightIndex => _highlight;

    public bool IsHighlighted(Node option) => _highlight == option.Index;

    public void SetHighlight(Node option)
    {
        int want = option.IsNull ? -1 : option.Index;
        if (_highlight != want)
        {
            _highlight = want;
            Version++;
        }
    }

    public void SetOpen(Node select, bool open)
    {
        int want = open ? select.Index : -1;
        if (_open != want && (open || _open == select.Index))
        {
            _open = want;
            _highlight = -1;
            Version++;
        }
    }
}
