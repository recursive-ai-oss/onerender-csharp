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
    private int _open = -1;

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

    public bool TryGetRangeFraction(Node element, out double fraction) => _range.TryGetValue(element.Index, out fraction);

    public void SetRangeFraction(Node element, double fraction)
    {
        _range[element.Index] = fraction;
        Version++;
    }

    public bool TryGetSelected(Node option, out bool value) => _selected.TryGetValue(option.Index, out value);

    public void SetSelected(Node option, bool value)
    {
        _selected[option.Index] = value;
        Version++;
    }

    public bool IsOpen(Node select) => _open == select.Index;

    public void SetOpen(Node select, bool open)
    {
        int want = open ? select.Index : -1;
        if (_open != want && (open || _open == select.Index))
        {
            _open = want;
            Version++;
        }
    }
}
