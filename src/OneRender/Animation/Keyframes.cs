using System.Globalization;
using OneRender.Style;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Animation;

/// <summary>One keyframe of a <c>@keyframes</c> rule: an offset, the declarations that apply there and the easing toward the next keyframe.</summary>
internal sealed class KeyframeBlock
{
    internal KeyframeBlock(double offset, StyleDeclaration declaration, PropertyId[] properties, EasingFunction? easing)
    {
        Offset = offset;
        Declaration = declaration;
        Properties = properties;
        Easing = easing;
    }

    /// <summary>The offset from 0 to 1.</summary>
    public double Offset { get; }

    /// <summary>The declared values, as a builder declaration so they compute through the same parsers as any style.</summary>
    public StyleDeclaration Declaration { get; }

    /// <summary>The animatable properties the keyframe declares, in property order.</summary>
    public PropertyId[] Properties { get; }

    /// <summary>The <c>animation-timing-function</c> declared inside the keyframe, or null to use the animation's own (CSS Animations 1 §4.2).</summary>
    public EasingFunction? Easing { get; }
}

/// <summary>A parsed <c>@keyframes</c> rule (CSS Animations 1 §4).</summary>
internal sealed class KeyframesRule
{
    /// <summary>Most keyframe selectors one rule keeps; the rest are dropped with a diagnostic.</summary>
    public const int MaxBlocks = 1024;

    private KeyframesRule(string name, KeyframeBlock[] blocks)
    {
        Name = name;
        Blocks = blocks;
    }

    /// <summary>The animation name; case-sensitive.</summary>
    public string Name { get; }

    /// <summary>The keyframes sorted by offset; keyframes with the same offset keep their source order (a later one wins per property).</summary>
    public KeyframeBlock[] Blocks { get; }

    /// <summary>Builds a rule from a <c>@keyframes</c> at-rule node.</summary>
    /// <returns>The rule, or null when its name is invalid or it has no usable block.</returns>
    public static KeyframesRule? FromNode(CssNode node)
    {
        if (!node.HasBlock)
        {
            return null;
        }

        // CSS Animations 1 §4: <keyframes-name> = <custom-ident> | <string>; none and the CSS-wide keywords are not names.
        ComponentValueList prelude = node.Prelude.Trimmed();
        string? name = null;
        int parts = 0;
        foreach (ComponentValue cv in prelude)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            parts++;
            if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.QuotedString)
            {
                name = cv.Token.Value;
            }
            else if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Ident)
            {
                string id = cv.Token.Value;
                bool reserved = id.Equals("none", StringComparison.OrdinalIgnoreCase) || id.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("initial", StringComparison.OrdinalIgnoreCase) || id.Equals("unset", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("default", StringComparison.OrdinalIgnoreCase) || id.Equals("revert", StringComparison.OrdinalIgnoreCase)
                    || id.Equals("revert-layer", StringComparison.OrdinalIgnoreCase);
                name = reserved ? null : id;
            }
        }

        if (parts != 1 || name is null)
        {
            return null;
        }

        var blocks = new List<KeyframeBlock>();
        var offsets = new List<double>(4);
        foreach (CssNode rule in node.Rules)
        {
            if (rule.Kind != CssNodeKind.QualifiedRule || !rule.HasBlock)
            {
                continue;
            }

            offsets.Clear();
            if (!ParseSelectors(rule.Prelude, offsets))
            {
                continue;
            }

            var decl = new StyleDeclaration();
            EasingFunction? easing = null;
            var props = new List<PropertyId>();
            foreach (CssNode d in rule.Declarations)
            {
                string pname = d.Name;
                if (d.IsImportant || d.IsCustomProperty)
                {
                    // !important declarations in a keyframe are ignored (CSS Animations 1 §4.2).
                    continue;
                }

                if (pname.Equals("animation-timing-function", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryEasing(d.ValueText.ToString(), out EasingFunction? e))
                    {
                        easing = e;
                    }

                    continue;
                }

                if (pname.StartsWith("animation", StringComparison.OrdinalIgnoreCase) || pname.StartsWith("transition", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The declaration store treats unset as "not declared", but in a keyframe it is a value: inherit or initial by property (CSS Animations 1 §4.2).
                string text = d.ValueText.ToString();
                if (text.Trim().Equals("unset", StringComparison.OrdinalIgnoreCase) && Properties.TryGet(pname, out PropertyId unsetId))
                {
                    text = Properties.IsInherited(unsetId) ? "inherit" : "initial";
                }

                decl.TrySet(pname, text);
            }

            for (int i = 0; i < Properties.Count; i++)
            {
                var id = (PropertyId)i;
                if (decl.Get(id, out _) != ValueOrigin.Unset && PropertyAnimator.IsAnimatable(id))
                {
                    props.Add(id);
                }
            }

            PropertyId[] propArray = props.ToArray();
            foreach (double o in offsets)
            {
                if (blocks.Count >= MaxBlocks)
                {
                    break;
                }

                blocks.Add(new KeyframeBlock(o, decl, propArray, easing));
            }
        }

        if (blocks.Count == 0)
        {
            return new KeyframesRule(name, []);
        }

        // Stable sort by offset: List.Sort is not stable, so insertion keeps source order for equal offsets.
        KeyframeBlock[] sorted = blocks.ToArray();
        for (int i = 1; i < sorted.Length; i++)
        {
            KeyframeBlock cur = sorted[i];
            int j = i - 1;
            while (j >= 0 && sorted[j].Offset > cur.Offset)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = cur;
        }

        return new KeyframesRule(name, sorted);
    }

    private static bool TryEasing(string text, out EasingFunction? easing)
    {
        easing = StyleComputer.ParseEasing(text);
        return easing is not null;
    }

    // CSS Animations 1 §4.2: <keyframe-selector> = from | to | <percentage [0,100]>, comma-separated; any invalid selector drops the rule.
    private static bool ParseSelectors(ComponentValueList prelude, List<double> offsets)
    {
        bool expectValue = true;
        foreach (ComponentValue cv in prelude)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind != ComponentValueKind.Token)
            {
                return false;
            }

            CssToken t = cv.Token;
            if (t.Kind == CssTokenKind.Comma)
            {
                if (expectValue)
                {
                    return false;
                }

                expectValue = true;
                continue;
            }

            if (!expectValue)
            {
                return false;
            }

            if (t.Kind == CssTokenKind.Ident && t.Value.Equals("from", StringComparison.OrdinalIgnoreCase))
            {
                offsets.Add(0);
            }
            else if (t.Kind == CssTokenKind.Ident && t.Value.Equals("to", StringComparison.OrdinalIgnoreCase))
            {
                offsets.Add(1);
            }
            else if (t.Kind == CssTokenKind.Percentage && double.IsFinite(t.NumberValue) && t.NumberValue >= 0 && t.NumberValue <= 100)
            {
                offsets.Add(t.NumberValue / 100);
            }
            else
            {
                return false;
            }

            expectValue = false;
        }

        return !expectValue && offsets.Count > 0;
    }
}

/// <summary>The <c>@keyframes</c> rules of a document, looked up by name (CSS Animations 1 §4.3: the last rule of a name wins).</summary>
internal sealed class KeyframesRegistry
{
    /// <summary>Most distinct names kept.</summary>
    public const int MaxRules = 4096;

    private const int MaxRuleDepth = 32;
    private readonly Dictionary<string, KeyframesRule> _rules = new(StringComparer.Ordinal);

    /// <summary>An empty registry.</summary>
    public static KeyframesRegistry Empty { get; } = new();

    /// <summary>The number of names.</summary>
    public int Count => _rules.Count;

    /// <summary>Finds a rule by exact name.</summary>
    public bool TryGet(string name, out KeyframesRule rule) => _rules.TryGetValue(name, out rule!);

    /// <summary>Adds a rule; a rule of the same name replaces the earlier one.</summary>
    public void Add(KeyframesRule rule)
    {
        if (_rules.Count < MaxRules || _rules.ContainsKey(rule.Name))
        {
            _rules[rule.Name] = rule;
        }
    }

    /// <summary>Collects the <c>@keyframes</c> rules of every style sheet, descending into media, supports and layer groups whose condition holds.</summary>
    public static KeyframesRegistry Collect(DocumentStyles styles, MediaFeatures media, CancellationToken cancellationToken = default)
    {
        var registry = new KeyframesRegistry();
        var stack = new Stack<IEnumerator<CssNode>>();
        foreach (StyleSheetInfo info in styles.Sheets)
        {
            stack.Push(((IEnumerable<CssNode>)info.Sheet.Rules).GetEnumerator());
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerator<CssNode> rules = stack.Peek();
                if (!rules.MoveNext())
                {
                    rules.Dispose();
                    stack.Pop();
                    continue;
                }

                CssNode node = rules.Current;
                if (node.Kind != CssNodeKind.AtRule)
                {
                    continue;
                }

                switch (node.AtRuleKind)
                {
                    case AtRuleKind.Keyframes:
                        if (KeyframesRule.FromNode(node) is { } rule)
                        {
                            registry.Add(rule);
                        }

                        break;
                    case AtRuleKind.Media:
                        if (node.HasBlock && stack.Count < MaxRuleDepth && MediaQueryList.Parse(node.Prelude.Trimmed()).Matches(media))
                        {
                            stack.Push(node.Rules.GetEnumerator());
                        }

                        break;
                    case AtRuleKind.Layer:
                    case AtRuleKind.Supports:
                        if (node.HasBlock && stack.Count < MaxRuleDepth)
                        {
                            stack.Push(node.Rules.GetEnumerator());
                        }

                        break;
                }
            }
        }

        return registry;
    }
}
