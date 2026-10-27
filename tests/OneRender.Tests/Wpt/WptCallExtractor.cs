using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;

namespace OneRender.Tests.Wpt;

/// <summary>One <c>test_valid_value</c>, <c>test_invalid_value</c> or <c>test_computed_value</c> call whose arguments are string literals.</summary>
internal readonly record struct WptCall(string Function, string Property, string Value, string? Expected);

/// <summary>
/// Finds the calls the parsing and computed-value helpers of web-platform-tests are driven by in a test file.
/// Only calls whose first two arguments are plain string literals are returned; calls built from variables or loops
/// are not understood and are counted by the caller as not applicable.
/// </summary>
internal static class WptCallExtractor
{
    private static readonly string[] Functions = ["test_valid_value", "test_invalid_value", "test_computed_value", "test_valid_and_computed_value"];

    /// <summary>All recognized calls, in file order, and the number of calls of those functions that could not be read.</summary>
    public static List<WptCall> Extract(string text, out int unreadable)
    {
        var calls = new List<WptCall>();
        unreadable = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int nl = text.IndexOf('\n', i);
                i = nl < 0 ? text.Length : nl + 1;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else if (c is '"' or '\'' or '`')
            {
                i = SkipString(text, i);
            }
            else if (c == 't' && (i == 0 || !IsIdentChar(text[i - 1])))
            {
                string? name = null;
                foreach (string f in Functions)
                {
                    if (string.CompareOrdinal(text, i, f, 0, f.Length) == 0 && i + f.Length < text.Length && text[i + f.Length] == '(')
                    {
                        name = f;
                        break;
                    }
                }

                if (name is null)
                {
                    i++;
                    continue;
                }

                int p = i + name.Length + 1;
                var args = new List<string?>();
                while (p < text.Length)
                {
                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                    {
                        p++;
                    }

                    if (p >= text.Length || text[p] == ')')
                    {
                        break;
                    }

                    if (text[p] is '"' or '\'' or '`')
                    {
                        int end = SkipString(text, p);
                        string? lit = Unquote(text, p, end);
                        args.Add(lit);
                        p = end;
                    }
                    else
                    {
                        args.Add(null);
                        p = SkipExpression(text, p);
                    }

                    while (p < text.Length && char.IsWhiteSpace(text[p]))
                    {
                        p++;
                    }

                    if (p < text.Length && text[p] == ',')
                    {
                        p++;
                    }
                    else if (p < text.Length && text[p] != ')')
                    {
                        p = SkipExpression(text, p);
                    }
                }

                if (args.Count >= 2 && args[0] is { } prop && args[1] is { } value)
                {
                    calls.Add(new WptCall(name, prop, value, args.Count >= 3 ? args[2] : null));
                }
                else
                {
                    unreadable++;
                }

                i = p;
            }
            else
            {
                i++;
            }
        }

        return calls;
    }

    private static bool IsIdentChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$' or '.';

    private static int SkipString(string s, int start)
    {
        char q = s[start];
        int i = start + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\')
            {
                i += 2;
            }
            else if (s[i] == q)
            {
                return i + 1;
            }
            else
            {
                i++;
            }
        }

        return s.Length;
    }

    /// <summary>Skips one argument expression: up to the next top-level comma or closing parenthesis.</summary>
    private static int SkipExpression(string s, int start)
    {
        int depth = 0;
        int i = start;
        while (i < s.Length)
        {
            char c = s[i];
            if (c is '"' or '\'' or '`')
            {
                i = SkipString(s, i);
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
                    return i;
                }

                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                return i;
            }

            i++;
        }

        return s.Length;
    }

    /// <summary>The value of the string literal in <c>s[start..end)</c>, or null for a template literal with substitutions.</summary>
    private static string? Unquote(string s, int start, int end)
    {
        if (end - start < 2 || s[end - 1] != s[start])
        {
            return null;
        }

        var sb = new StringBuilder();
        for (int i = start + 1; i < end - 1; i++)
        {
            char c = s[i];
            if (c == '$' && s[start] == '`' && i + 1 < end - 1 && s[i + 1] == '{')
            {
                return null;
            }

            if (c != '\\' || i + 1 >= end - 1)
            {
                sb.Append(c);
                continue;
            }

            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'u' when i + 4 < end && int.TryParse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u):
                    sb.Append((char)u);
                    i += 4;
                    break;
                case 'x' when i + 2 < end && int.TryParse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int x):
                    sb.Append((char)x);
                    i += 2;
                    break;
                case '\n': break;
                default: sb.Append(n); break;
            }
        }

        return sb.ToString();
    }
}
