using System.Globalization;
using System.Text;

namespace OneRender.Tests.Wpt;

internal enum JsKind
{
    /// <summary>A name or expression the replay cannot evaluate.</summary>
    Unknown,
    String,
    Number,
    Bool,
    Null,
    Object,
    Array,

    /// <summary>The <c>neutralKeyframe</c> marker of the interpolation helpers.</summary>
    Neutral,
}

/// <summary>A JavaScript literal as the test files write their option objects: strings, numbers, booleans, objects and arrays.</summary>
internal sealed class JsValue
{
    public JsKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public double Number { get; init; }

    public List<KeyValuePair<string, JsValue>> Members { get; } = new();

    public List<JsValue> Items { get; } = new();

    public JsValue? Get(string key)
    {
        foreach (KeyValuePair<string, JsValue> m in Members)
        {
            if (m.Key == key)
            {
                return m.Value;
            }
        }

        return null;
    }

    public string? GetString(string key) => Get(key) is { Kind: JsKind.String } v ? v.Text : null;

    /// <summary>True when this value and everything inside it is a literal the replay understands.</summary>
    public bool IsFullyKnown()
    {
        if (Kind == JsKind.Unknown)
        {
            return false;
        }

        foreach (KeyValuePair<string, JsValue> m in Members)
        {
            if (!m.Value.IsFullyKnown())
            {
                return false;
            }
        }

        foreach (JsValue i in Items)
        {
            if (!i.IsFullyKnown())
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A small reader for JavaScript literals. It never executes anything; an expression it does not understand becomes <see cref="JsKind.Unknown"/>.</summary>
internal sealed class JsLiteralReader
{
    private readonly string _s;
    private int _p;

    public JsLiteralReader(string text, int position)
    {
        _s = text;
        _p = position;
    }

    public int Position => _p;

    private static readonly JsValue UnknownValue = new() { Kind = JsKind.Unknown };

    public void SkipSpace()
    {
        while (_p < _s.Length)
        {
            if (char.IsWhiteSpace(_s[_p]))
            {
                _p++;
            }
            else if (_s[_p] == '/' && _p + 1 < _s.Length && _s[_p + 1] == '/')
            {
                int nl = _s.IndexOf('\n', _p);
                _p = nl < 0 ? _s.Length : nl + 1;
            }
            else if (_s[_p] == '/' && _p + 1 < _s.Length && _s[_p + 1] == '*')
            {
                int e = _s.IndexOf("*/", _p + 2, StringComparison.Ordinal);
                _p = e < 0 ? _s.Length : e + 2;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>Reads one value, including <c>a + b</c> concatenations of strings; stops before a comma or a closing bracket.</summary>
    public JsValue ReadValue()
    {
        JsValue left = ReadPrimary();
        while (true)
        {
            SkipSpace();
            if (_p < _s.Length && _s[_p] == '+')
            {
                _p++;
                JsValue right = ReadPrimary();
                if (left.Kind == JsKind.String && right.Kind is JsKind.String or JsKind.Number)
                {
                    left = new JsValue { Kind = JsKind.String, Text = left.Text + (right.Kind == JsKind.String ? right.Text : right.Number.ToString(CultureInfo.InvariantCulture)) };
                }
                else
                {
                    left = UnknownValue;
                }

                continue;
            }

            break;
        }

        // Anything else before the separator means an expression we do not model (a call, a ternary, a member access).
        SkipSpace();
        if (_p < _s.Length && _s[_p] is not (',' or ')' or ']' or '}' or ';'))
        {
            SkipExpressionRest();
            return UnknownValue;
        }

        return left;
    }

    private void SkipExpressionRest()
    {
        int depth = 0;
        while (_p < _s.Length)
        {
            char c = _s[_p];
            if (c is '"' or '\'' or '`')
            {
                _p = SkipString(_p);
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    return;
                }

                depth--;
            }
            else if (c is ',' or ';' && depth == 0)
            {
                return;
            }

            _p++;
        }
    }

    private int SkipString(int start)
    {
        char q = _s[start];
        int i = start + 1;
        while (i < _s.Length && _s[i] != q)
        {
            i += _s[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, _s.Length);
    }

    private JsValue ReadPrimary()
    {
        SkipSpace();
        if (_p >= _s.Length)
        {
            return UnknownValue;
        }

        char c = _s[_p];
        if (c is '"' or '\'')
        {
            return ReadString();
        }

        if (c == '`')
        {
            int end = SkipString(_p);
            string raw = _s.Substring(_p + 1, Math.Max(0, end - _p - 2));
            _p = end;
            return raw.Contains("${", StringComparison.Ordinal) ? UnknownValue : new JsValue { Kind = JsKind.String, Text = Unescape(raw) };
        }

        if (c == '{')
        {
            return ReadObject();
        }

        if (c == '[')
        {
            return ReadArray();
        }

        if (c == '-' || c == '+' || c == '.' || char.IsAsciiDigit(c))
        {
            int s = _p;
            _p++;
            while (_p < _s.Length && (char.IsAsciiDigit(_s[_p]) || _s[_p] is '.' or 'e' or 'E' || (_s[_p] is '-' or '+' && _s[_p - 1] is 'e' or 'E')))
            {
                _p++;
            }

            if (s + 1 == _p && c is '-' or '+')
            {
                // -Infinity and friends.
                SkipIdent();
                return UnknownValue;
            }

            return double.TryParse(_s.AsSpan(s, _p - s), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? new JsValue { Kind = JsKind.Number, Number = d }
                : UnknownValue;
        }

        string id = SkipIdent();
        return id switch
        {
            "true" => new JsValue { Kind = JsKind.Bool, Number = 1 },
            "false" => new JsValue { Kind = JsKind.Bool, Number = 0 },
            "null" => new JsValue { Kind = JsKind.Null },
            "neutralKeyframe" => new JsValue { Kind = JsKind.Neutral },
            _ => UnknownValue,
        };
    }

    private string SkipIdent()
    {
        int s = _p;
        while (_p < _s.Length && (char.IsAsciiLetterOrDigit(_s[_p]) || _s[_p] is '_' or '$'))
        {
            _p++;
        }

        if (_p == s)
        {
            _p++;
        }

        return _s.Substring(s, _p - s);
    }

    private JsValue ReadString()
    {
        int end = SkipString(_p);
        string raw = _s.Substring(_p + 1, Math.Max(0, end - _p - 2));
        _p = end;
        return new JsValue { Kind = JsKind.String, Text = Unescape(raw) };
    }

    private static string Unescape(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal))
        {
            return raw;
        }

        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
                sb.Append(raw[i] switch { 'n' => '\n', 't' => '\t', _ => raw[i] });
            }
            else
            {
                sb.Append(raw[i]);
            }
        }

        return sb.ToString();
    }

    private JsValue ReadObject()
    {
        var obj = new JsValue { Kind = JsKind.Object };
        _p++;
        while (true)
        {
            SkipSpace();
            if (_p >= _s.Length)
            {
                return UnknownValue;
            }

            if (_s[_p] == '}')
            {
                _p++;
                return obj;
            }

            if (_s[_p] == ',')
            {
                _p++;
                continue;
            }

            string key;
            if (_s[_p] is '"' or '\'')
            {
                key = ReadString().Text;
            }
            else if (_s[_p] == '[')
            {
                // A computed key.
                SkipExpressionRest();
                return UnknownValue;
            }
            else
            {
                key = SkipIdent();
            }

            SkipSpace();
            if (_p < _s.Length && _s[_p] == ':')
            {
                _p++;
                obj.Members.Add(new KeyValuePair<string, JsValue>(key, ReadValue()));
            }
            else if (_p < _s.Length && _s[_p] == '(')
            {
                // A method: the object has behavior we cannot replay.
                SkipExpressionRest();
                obj.Members.Add(new KeyValuePair<string, JsValue>(key, UnknownValue));
            }
            else
            {
                // Shorthand member.
                obj.Members.Add(new KeyValuePair<string, JsValue>(key, UnknownValue));
            }
        }
    }

    private JsValue ReadArray()
    {
        var arr = new JsValue { Kind = JsKind.Array };
        _p++;
        while (true)
        {
            SkipSpace();
            if (_p >= _s.Length)
            {
                return UnknownValue;
            }

            if (_s[_p] == ']')
            {
                _p++;
                return arr;
            }

            if (_s[_p] == ',')
            {
                _p++;
                continue;
            }

            arr.Items.Add(ReadValue());
        }
    }
}
