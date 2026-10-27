namespace OneRender;

/// <summary>What a <see cref="Diagnostic"/> reports.</summary>
public enum DiagnosticKind : byte
{
    /// <summary>A declared value did not match the property's grammar and was ignored, as if it had not been declared.</summary>
    InvalidValue,

    /// <summary>A size, depth or count limit was reached; the input past the limit was not built.</summary>
    LimitExceeded,

    /// <summary>A feature the engine does not implement yet was met and approximated.</summary>
    Unsupported,

    /// <summary>The host's resource resolver returned nothing for a resource.</summary>
    MissingResource,
}

/// <summary>One thing the engine noticed while building a tree. Diagnostics never stop a build.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Message">A short description in English; stable enough to read in logs, not to parse.</param>
public readonly record struct Diagnostic(DiagnosticKind Kind, string Message);

/// <summary>
/// A bounded list of <see cref="Diagnostic"/> values. At most <see cref="Capacity"/> are kept; further ones only
/// raise <see cref="Dropped"/>, so hostile input cannot grow it.
/// </summary>
public sealed class DiagnosticBag
{
    /// <summary>The most diagnostics kept.</summary>
    public const int Capacity = 256;

    private readonly List<Diagnostic> _items = new(16);

    /// <summary>The kept diagnostics in the order they were raised.</summary>
    public IReadOnlyList<Diagnostic> Items => _items;

    /// <summary>How many diagnostics were raised beyond <see cref="Capacity"/> and not kept.</summary>
    public int Dropped { get; private set; }

    /// <summary>Removes everything; keeps the storage.</summary>
    public void Clear()
    {
        _items.Clear();
        Dropped = 0;
    }

    internal void Add(DiagnosticKind kind, string message)
    {
        if (_items.Count < Capacity)
        {
            _items.Add(new Diagnostic(kind, message));
        }
        else
        {
            Dropped++;
        }
    }
}
