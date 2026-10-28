using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tree;

/// <summary>
/// Where the page reads computed styles from: an immutable <see cref="DocumentStyles"/> view, or a <see cref="LiveDocumentStyles"/> that
/// moves in place when the interaction or form state changes. Both answer the same questions, so the incremental restyle does not care
/// which one the host page holds.
/// </summary>
internal readonly struct StyleSource
{
    private readonly DocumentStyles? _snapshot;
    private readonly LiveDocumentStyles? _live;

    public StyleSource(DocumentStyles snapshot)
    {
        _snapshot = snapshot;
        _live = null;
    }

    public StyleSource(LiveDocumentStyles live)
    {
        _snapshot = null;
        _live = live;
    }

    public static implicit operator StyleSource(DocumentStyles snapshot) => new(snapshot);

    public bool IsNull => _snapshot is null && _live is null;

    public Document Document => _live is not null ? _live.Document : _snapshot!.Document;

    public bool HasInteractivePseudoElementRules => _live is not null ? _live.HasInteractivePseudoElementRules : _snapshot!.HasInteractivePseudoElementRules;

    public ComputedStyle GetStyle(Node element) => _live is not null ? _live.GetStyle(element) : _snapshot!.GetStyle(element);

    public ComputedStyle? GetPseudoStyle(Node element, PseudoElementKind kind) =>
        _live is not null ? _live.GetPseudoStyle(element, kind) : _snapshot!.GetPseudoStyle(element, kind);

    /// <summary>The keyframes of each animation in the style's <c>animation-name</c> list (see OneHtml's <c>GetAnimationKeyframes</c>).</summary>
    public IReadOnlyList<ResolvedKeyframes?> GetAnimationKeyframes(ComputedStyle style) =>
        _live is not null ? _live.GetAnimationKeyframes(style) : _snapshot!.GetAnimationKeyframes(style);

    /// <summary>An immutable view of the current state; allocates when the source is live.</summary>
    public DocumentStyles ToSnapshot() => _live is not null ? _live.ToSnapshot() : _snapshot!;
}
