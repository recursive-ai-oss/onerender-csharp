using System.Globalization;
using System.Text;

namespace OneRender.Text;

internal enum TokKind : byte
{
    Ident,
    String,
    Number,
    Percent,
    Dimension,
    Function, // name( — closed by a CloseParen token
    CloseParen,
    Url, // url(unquoted)
    UnicodeRange,
    Comma,
    Slash,
    Delim,
    Bad,
}

internal readonly record struct Tok(TokKind Kind, string Text, double Number = 0, string? Unit = null);

// A small CSS Syntax tokenizer for descriptor and font property values. It knows only what the font properties need:
// identifiers, strings, numbers with units, functions, url(), unicode-range and commas. Comments are skipped.
// Escapes follow CSS Syntax §4.3.7. Input and token count are bounded so a hostile value costs linear time.
internal static class CssValueTokenizer
{
    public const int MaxInputLength = 1 << 16;
    public const int MaxTokens = 1 << 14;

    public static bool TryTokenize(ReadOnlySpan<char> s, List<Tok> tokens)
    {
        tokens.Clear();
        if (s.Length > MaxInputLength)
        {
            return false;
        }

        int i = 0;
        while (i < s.Length)
        {
            if (tokens.Count >= MaxTokens)
            {
                return false;
            }

            char c = s[i];
            if (IsWhite(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                i = end < 0 ? s.Length : i + 2 + end + 2;
            }
            else if (c is '"' or '\'')
            {
                tokens.Add(ReadString(s, ref i));
            }
            else if ((c is 'u' or 'U') && i + 2 < s.Length && s[i + 1] == '+' && (IsHex(s[i + 2]) || s[i + 2] == '?'))
            {
                int start = i;
                i += 2;
                while (i < s.Length && (IsHex(s[i]) || s[i] == '?' || s[i] == '-'))
                {
                    i++;
                }

                tokens.Add(new Tok(TokKind.UnicodeRange, s[start..i].ToString()));
            }
            else if (IsDigit(c) || ((c is '+' or '-' or '.') && StartsNumber(s, i)))
            {
                tokens.Add(ReadNumeric(s, ref i));
            }
            else if (StartsIdent(s, i))
            {
                string name = ReadIdent(s, ref i);
                if (i < s.Length && s[i] == '(')
                {
                    i++;
                    if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
                    {
                        int j = i;
                        while (j < s.Length && IsWhite(s[j]))
                        {
                            j++;
                        }

                        if (j < s.Length && s[j] is not ('"' or '\''))
                        {
                            tokens.Add(ReadUnquotedUrl(s, ref i));
                            continue;
                        }
                    }

                    tokens.Add(new Tok(TokKind.Function, name));
                }
                else
                {
                    tokens.Add(new Tok(TokKind.Ident, name));
                }
            }
            else
            {
                i++;
                tokens.Add(c switch
                {
                    ',' => new Tok(TokKind.Comma, ","),
                    '/' => new Tok(TokKind.Slash, "/"),
                    ')' => new Tok(TokKind.CloseParen, ")"),
                    _ => new Tok(TokKind.Delim, c.ToString()),
                });
            }
        }

        return true;
    }

    private static bool IsWhite(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    internal static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool IsNameStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' || c >= 0x80;

    private static bool IsNameChar(char c) => IsNameStart(c) || IsDigit(c) || c == '-';

    private static bool StartsNumber(ReadOnlySpan<char> s, int i)
    {
        char c = s[i];
        if (c is '+' or '-')
        {
            i++;
            if (i >= s.Length)
            {
                return false;
            }

            c = s[i];
        }

        if (c == '.')
        {
            return i + 1 < s.Length && IsDigit(s[i + 1]);
        }

        return IsDigit(c);
    }

    private static bool StartsIdent(ReadOnlySpan<char> s, int i)
    {
        char c = s[i];
        if (c == '-')
        {
            if (i + 1 >= s.Length)
            {
                return false;
            }

            char d = s[i + 1];
            return IsNameStart(d) || d == '-' || (d == '\\' && i + 2 < s.Length && s[i + 2] is not ('\n' or '\r' or '\f'));
        }

        return IsNameStart(c) || (c == '\\' && i + 1 < s.Length && s[i + 1] is not ('\n' or '\r' or '\f'));
    }

    private static string ReadIdent(ReadOnlySpan<char> s, ref int i)
    {
        int start = i;
        bool simple = true;
        while (i < s.Length)
        {
            if (IsNameChar(s[i]))
            {
                i++;
            }
            else if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] is not ('\n' or '\r' or '\f'))
            {
                simple = false;
                i += 2;
            }
            else
            {
                break;
            }
        }

        if (simple)
        {
            return s[start..i].ToString();
        }

        var sb = new StringBuilder();
        int k = start;
        while (k < i)
        {
            if (s[k] == '\\')
            {
                k++;
                AppendEscape(s, ref k, sb);
            }
            else
            {
                sb.Append(s[k++]);
            }
        }

        return sb.ToString();
    }

    // k is just after the backslash.
    private static void AppendEscape(ReadOnlySpan<char> s, ref int k, StringBuilder sb)
    {
        if (k < s.Length && IsHex(s[k]))
        {
            int v = 0;
            int n = 0;
            while (k < s.Length && n < 6 && IsHex(s[k]))
            {
                v = (v * 16) + HexValue(s[k]);
                k++;
                n++;
            }

            if (k < s.Length && IsWhite(s[k]))
            {
                k += s[k] == '\r' && k + 1 < s.Length && s[k + 1] == '\n' ? 2 : 1;
            }

            if (v == 0 || v > 0x10FFFF || (v >= 0xD800 && v <= 0xDFFF))
            {
                v = 0xFFFD;
            }

            sb.Append(char.ConvertFromUtf32(v));
        }
        else if (k < s.Length)
        {
            sb.Append(s[k++]);
        }
    }

    private static Tok ReadString(ReadOnlySpan<char> s, ref int i)
    {
        char quote = s[i++];
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (c == quote)
            {
                i++;
                return new Tok(TokKind.String, sb.ToString());
            }

            if (c is '\n' or '\r' or '\f')
            {
                return new Tok(TokKind.Bad, string.Empty); // an unescaped newline ends a string in error
            }

            i++;
            if (c == '\\')
            {
                if (i >= s.Length)
                {
                    break;
                }

                if (s[i] is '\n' or '\f')
                {
                    i++;
                }
                else if (s[i] == '\r')
                {
                    i += i + 1 < s.Length && s[i + 1] == '\n' ? 2 : 1;
                }
                else
                {
                    AppendEscape(s, ref i, sb);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return new Tok(TokKind.Bad, string.Empty); // an unterminated string
    }

    private static Tok ReadUnquotedUrl(ReadOnlySpan<char> s, ref int i)
    {
        while (i < s.Length && IsWhite(s[i]))
        {
            i++;
        }

        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ')')
            {
                i++;
                return new Tok(TokKind.Url, sb.ToString());
            }

            if (IsWhite(c))
            {
                while (i < s.Length && IsWhite(s[i]))
                {
                    i++;
                }

                if (i < s.Length && s[i] == ')')
                {
                    i++;
                    return new Tok(TokKind.Url, sb.ToString());
                }

                return new Tok(TokKind.Bad, string.Empty);
            }

            if (c is '"' or '\'' or '(')
            {
                return new Tok(TokKind.Bad, string.Empty);
            }

            i++;
            if (c == '\\')
            {
                AppendEscape(s, ref i, sb);
            }
            else
            {
                sb.Append(c);
            }
        }

        return new Tok(TokKind.Bad, string.Empty);
    }

    private static Tok ReadNumeric(ReadOnlySpan<char> s, ref int i)
    {
        int start = i;
        if (s[i] is '+' or '-')
        {
            i++;
        }

        while (i < s.Length && IsDigit(s[i]))
        {
            i++;
        }

        if (i + 1 < s.Length && s[i] == '.' && IsDigit(s[i + 1]))
        {
            i++;
            while (i < s.Length && IsDigit(s[i]))
            {
                i++;
            }
        }

        if (i < s.Length && (s[i] is 'e' or 'E'))
        {
            int j = i + 1;
            if (j < s.Length && s[j] is '+' or '-')
            {
                j++;
            }

            if (j < s.Length && IsDigit(s[j]))
            {
                while (j < s.Length && IsDigit(s[j]))
                {
                    j++;
                }

                i = j;
            }
        }

        double value = double.Parse(s[start..i], NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value) || double.IsNaN(value))
        {
            value = 0;
        }

        string text = s[start..i].ToString();
        if (i < s.Length && s[i] == '%')
        {
            i++;
            return new Tok(TokKind.Percent, text, value);
        }

        if (i < s.Length && StartsIdent(s, i))
        {
            string unit = ReadIdent(s, ref i);
            return new Tok(TokKind.Dimension, text, value, unit);
        }

        return new Tok(TokKind.Number, text, value);
    }
}
