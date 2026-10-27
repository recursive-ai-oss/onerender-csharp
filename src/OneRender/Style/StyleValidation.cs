using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>
/// The engine's declaration grammar, offered to OneHtml's cascade so that a declaration the engine cannot parse is dropped
/// while the cascade collects declarations (CSS Syntax 3 §5.4.6, CSS Cascade 4 §4.3): it neither wins nor counts as
/// <c>!important</c>, and an earlier valid declaration of the property stands.
/// </summary>
/// <remarks>
/// Pass <see cref="Validator"/> as <c>StyleSourceOptions.DeclarationValidator</c> wherever a <c>DocumentStyles</c> is
/// created for this engine. <see cref="HtmlPage"/> does so itself. Without it an invalid value that the cascade already
/// selected computes as <c>unset</c> and raises an <see cref="DiagnosticKind.InvalidValue"/> diagnostic.
/// OneHtml asks only about properties it does not type itself, and not about values that contain <c>var()</c>; those are
/// checked at computed-value time (CSS Variables 1 §3.2) and still end up as <c>unset</c>.
/// </remarks>
public static class StyleValidation
{
    private static readonly Checker Shared = new();

    /// <summary>
    /// A deterministic, thread-safe <c>StyleSourceOptions.DeclarationValidator</c> that accepts exactly the values the engine
    /// parses for the properties it computes, and the declared name of every property it does not know (the engine ignores those).
    /// </summary>
    public static DeclarationValidator Validator => Shared.Validate;

    private sealed class Checker
    {
        private readonly object _gate = new();
        private readonly StyleComputer _computer = new();
        private readonly List<(PropertyId Id, ComponentValueList Value)> _scratch = new(8);

        public bool Validate(string property, TokenRange value, StyleOrigin origin)
        {
            bool isLonghand = Properties.TryGet(property, out PropertyId id);
            if (!isLonghand && !ShorthandFallbacks.IsHandled(property))
            {
                return true;
            }

            // OneHtml hands over token indexes into its own tree and the engine's parsers read component-value lists, which a
            // range cannot be turned into without re-reading the text. This runs once per declaration per stylesheet, not per element.
            ComponentValueList list = CssTree.ParseComponentValueList(value.RawText()).Values;
            lock (_gate)
            {
                if (isLonghand)
                {
                    return _computer.IsValid(id, list);
                }

                _scratch.Clear();
                if (!ShorthandFallbacks.Expand(property, list, _scratch))
                {
                    return false;
                }

                foreach ((PropertyId Id, ComponentValueList Value) item in _scratch)
                {
                    // Expansion repeats a CSS-wide keyword into every longhand; those need no grammar.
                    if (CssWideKeywords.Detect(item.Value) == CssWideKeyword.None && !_computer.IsValid(item.Id, item.Value))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
