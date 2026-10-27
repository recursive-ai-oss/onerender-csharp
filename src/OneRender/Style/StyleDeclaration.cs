using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>
/// A set of declared property values for one node of a builder-made tree, written as CSS text:
/// <c>new StyleDeclaration().Set("width", "50%").Set("margin", "0 auto")</c>. The text goes through the same parsers
/// as a stylesheet, so a game UI and an HTML page compute identically. Longhands and the shorthands OneHtml expands
/// (<c>margin</c>, <c>padding</c>, <c>border</c>, <c>font</c>, ...) are accepted, and <c>white-space</c>, <c>border-radius</c>
/// and <c>overflow</c> too. Declarations are not inherited from other declarations; inheritance comes from the tree.
/// </summary>
/// <remarks>
/// A declaration object can be shared by many nodes and reused across rebuilds; the style it produces is cached per
/// parent style, so rebuilding with the same objects computes nothing. Changing it afterwards makes the next build
/// compute again. Not thread-safe.
/// </remarks>
public sealed class StyleDeclaration
{
    private readonly ValueOrigin[] _origin = new ValueOrigin[Properties.Count];
    private readonly ComponentValueList[] _values = new ComponentValueList[Properties.Count];
    private readonly List<(PropertyId Id, ComponentValueList Value)> _scratch = new(8);

    /// <summary>Changes whenever a value is set; part of the key that caches the computed style.</summary>
    internal int Version { get; private set; }

    /// <summary>Declares one property, ignoring a declaration the library cannot parse or does not know (see <see cref="TrySet"/>).</summary>
    /// <param name="property">A property name in any ASCII case, for example <c>margin-top</c> or <c>padding</c>.</param>
    /// <param name="value">The value as CSS text. <c>inherit</c>, <c>initial</c> and <c>unset</c> work. <c>var()</c> is not available here.</param>
    /// <returns>This declaration, for chaining.</returns>
    public StyleDeclaration Set(string property, string value)
    {
        TrySet(property, value);
        return this;
    }

    /// <summary>Declares a length property in px.</summary>
    /// <param name="property">A property name that takes a length.</param>
    /// <param name="px">The size; a non-finite number is ignored.</param>
    /// <returns>This declaration, for chaining.</returns>
    public StyleDeclaration SetPx(string property, double px)
    {
        if (double.IsFinite(px))
        {
            TrySet(property, CssText.Number(px) + "px");
        }

        return this;
    }

    /// <summary>Declares one property.</summary>
    /// <param name="property">A property name in any ASCII case.</param>
    /// <param name="value">The value as CSS text, at most 65,536 characters.</param>
    /// <returns>False when the property is unknown, the value is too long or empty, or a shorthand's value does not match its grammar; the declaration is unchanged then. A value that is well-formed for the tokenizer but wrong for the property is accepted here and reported as a diagnostic when the style is computed.</returns>
    public bool TrySet(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is 0 or > StyleComputer.MaxValueLength || property.Length > 64)
        {
            return false;
        }

        ComponentValueList list = CssTree.ParseComponentValueList(value).Values;
        if (list.IsEmpty)
        {
            return false;
        }

        if (Properties.TryGet(property, out PropertyId id))
        {
            Put(id, list);
            return true;
        }

        _scratch.Clear();
        if (ShorthandFallbacks.IsHandled(property))
        {
            if (!ShorthandFallbacks.Expand(property, list, _scratch))
            {
                return false;
            }
        }
        else
        {
            ShorthandExpansion e = Shorthands.Expand(property.ToLowerInvariant(), list);
            if (e.Status != ShorthandStatus.Expanded)
            {
                return false;
            }

            foreach (ExpandedLonghand l in e.Longhands)
            {
                if (Properties.TryGet(l.Name, out PropertyId lid))
                {
                    _scratch.Add((lid, l.Keyword != CssWideKeyword.None ? CssTree.ParseComponentValueList(CssWideKeywords.ToCss(l.Keyword)).Values : l.Value));
                }
            }
        }

        foreach ((PropertyId Id, ComponentValueList Value) item in _scratch)
        {
            Put(item.Id, item.Value);
        }

        return true;
    }

    /// <summary>Forgets every declared value.</summary>
    public void Clear()
    {
        Array.Clear(_origin);
        Array.Clear(_values);
        Version++;
    }

    internal ValueOrigin Get(PropertyId id, out ComponentValueList value)
    {
        value = _values[(int)id];
        return _origin[(int)id];
    }

    private void Put(PropertyId id, ComponentValueList list)
    {
        switch (CssWideKeywords.Detect(list))
        {
            case CssWideKeyword.Initial:
                _origin[(int)id] = ValueOrigin.Initial;
                break;
            case CssWideKeyword.Inherit:
                _origin[(int)id] = ValueOrigin.Inherit;
                break;
            case CssWideKeyword.Unset:
            case CssWideKeyword.Revert:
            case CssWideKeyword.RevertLayer:
                // A builder tree has no earlier origin or layer to revert to, so these behave as unset (CSS Cascade 5 §7.3.2).
                _origin[(int)id] = ValueOrigin.Unset;
                break;
            default:
                _origin[(int)id] = ValueOrigin.Specified;
                _values[(int)id] = list;
                break;
        }

        Version++;
    }

    /// <summary>Adapts a declaration to the computer's input.</summary>
    internal sealed class Source : DeclarationSource
    {
        public StyleDeclaration? Declaration { get; set; }

        public override ValueOrigin Get(PropertyId id, out ComponentValueList value)
        {
            if (Declaration is null)
            {
                value = default;
                return ValueOrigin.Unset;
            }

            return Declaration.Get(id, out value);
        }
    }
}

/// <summary>Reads the cascaded values of OneHtml's <c>ComputedStyle</c>.</summary>
internal sealed class OneHtmlSource : DeclarationSource
{
    private readonly ValueOrigin[] _originOverride = new ValueOrigin[Properties.Count];
    private readonly ComponentValueList[] _valueOverride = new ComponentValueList[Properties.Count];
    private readonly List<(PropertyId Id, ComponentValueList Value)> _scratch = new(8);
    private ComputedStyle? _style;
    private bool _hasOverride;

    public void Reset(ComputedStyle? style)
    {
        _style = style;
        _hasOverride = false;
        if (style is null)
        {
            return;
        }

        // The cascade keeps these shorthands whole; expand the ones that were declared so the longhands can be computed.
        foreach (string shorthand in ShorthandFallbacks.Names)
        {
            StyleValue? sv = style.GetValue(shorthand);
            if (sv is null || sv.IsInitial || sv.Tokens.IsEmpty)
            {
                continue;
            }

            if (sv.IsInherited)
            {
                continue;
            }

            _scratch.Clear();
            if (!ShorthandFallbacks.Expand(shorthand, sv.Tokens, _scratch))
            {
                continue;
            }

            if (!_hasOverride)
            {
                Array.Clear(_originOverride);
                _hasOverride = true;
            }

            foreach ((PropertyId id, ComponentValueList value) in _scratch)
            {
                switch (CssWideKeywords.Detect(value))
                {
                    case CssWideKeyword.Initial: _originOverride[(int)id] = ValueOrigin.Initial; break;
                    case CssWideKeyword.Inherit: _originOverride[(int)id] = ValueOrigin.Inherit; break;
                    case CssWideKeyword.Unset: _originOverride[(int)id] = ValueOrigin.Unset; break;
                    default:
                        _originOverride[(int)id] = ValueOrigin.Specified;
                        _valueOverride[(int)id] = value;
                        break;
                }
            }
        }
    }

    public override ValueOrigin Get(PropertyId id, out ComponentValueList value)
    {
        value = default;
        StyleValue? sv = _style?.GetValue(Properties.NameOf(id));
        bool declared = sv is not null && !sv.IsInitial && !sv.IsInherited && !sv.Tokens.IsEmpty;
        if (!declared && _hasOverride && _originOverride[(int)id] != ValueOrigin.Unset)
        {
            value = _valueOverride[(int)id];
            return _originOverride[(int)id];
        }

        if (id == PropertyId.VerticalAlign && sv is { IsInitial: true } && _style is not null && (_style.IsDeclared("vertical-align") || _style.IsDeclared("alignment-baseline")))
        {
            // The cascade reads a shorthand that was given a CSS-wide keyword as its initial value, for `inherit` too. The HTML table rules
            // (tr, td, th: vertical-align: inherit) need the inherited value, so a declared-but-initial value inherits. An author's explicit
            // `initial` is therefore read as `inherit`, which differs only below a parent that is not baseline-aligned.
            return ValueOrigin.Inherit;
        }

        if (sv is null)
        {
            return ValueOrigin.Unset;
        }

        if (sv.IsInherited)
        {
            return ValueOrigin.Inherit;
        }

        if (id is PropertyId.LineHeight or PropertyId.LetterSpacing or PropertyId.WordSpacing or PropertyId.TextIndent or PropertyId.TabSize
                or PropertyId.FontSize or PropertyId.FontWeight
            && _style is not null && !_style.IsDeclared(Properties.NameOf(id)))
        {
            // The cascade hands a descendant the ancestor's specified text. Values with relative lengths (em, a line-height percentage)
            // inherit as the ancestor's computed value (CSS Values 4 §7.1), not as text to resolve against the descendant's own font.
            // The same holds for font-size (em, %, larger, smaller) and font-weight (bolder, lighter), which are relative to the parent.
            return ValueOrigin.Unset;
        }

        if (sv.IsInitial || sv.Tokens.IsEmpty)
        {
            if (_style is not null && Properties.IsInherited(id) && !_style.IsDeclared(Properties.NameOf(id)))
            {
                // Nothing in the cascade declared this inherited property for the element or an ancestor, but an ancestor may have set it
                // through a shorthand that was expanded here (white-space, for one): CSS Cascade 4 §7.3 gives such an element its
                // parent's computed value, not the initial one.
                return ValueOrigin.Unset;
            }

            return ValueOrigin.Initial;
        }

        value = sv.Tokens;
        return ValueOrigin.Specified;
    }
}
