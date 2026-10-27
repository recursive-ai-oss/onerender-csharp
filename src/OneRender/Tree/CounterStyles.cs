using System.Globalization;
using System.Text;

namespace OneRender.Tree;

/// <summary>Formats counter values in the predefined counter styles that <c>counter()</c> and <c>counters()</c> name (CSS Counter Styles 3 §6).</summary>
internal static class CounterStyles
{
    /// <summary>Appends <paramref name="value"/> in <paramref name="style"/>. An unknown style, or a value the style cannot represent, falls back to decimal as §3.2 says.</summary>
    public static void Append(StringBuilder sb, int value, string style)
    {
        switch (style.ToLowerInvariantAscii())
        {
            case "none":
                return;
            case "disc":
                sb.Append('\u2022');
                return;
            case "circle":
                sb.Append('\u25E6');
                return;
            case "square":
                sb.Append('\u25AA');
                return;
            case "decimal-leading-zero":
                if (value is > -10 and < 10)
                {
                    if (value < 0)
                    {
                        sb.Append('-');
                    }

                    sb.Append('0').Append(Math.Abs(value).ToString(CultureInfo.InvariantCulture));
                    return;
                }

                break;
            case "lower-alpha":
            case "lower-latin":
                if (value > 0)
                {
                    Alpha(sb, value, 'a');
                    return;
                }

                break;
            case "upper-alpha":
            case "upper-latin":
                if (value > 0)
                {
                    Alpha(sb, value, 'A');
                    return;
                }

                break;
            case "lower-roman":
                if (value is > 0 and < 4000)
                {
                    Roman(sb, value, false);
                    return;
                }

                break;
            case "upper-roman":
                if (value is > 0 and < 4000)
                {
                    Roman(sb, value, true);
                    return;
                }

                break;
            case "lower-greek":
                if (value > 0)
                {
                    // 24 letters, skipping the final sigma: alpha .. omega.
                    int n = value - 1;
                    var tmp = new StringBuilder();
                    do
                    {
                        tmp.Insert(0, (char)(0x03B1 + (n % 24) + (n % 24 >= 17 ? 1 : 0)));
                        n = (n / 24) - 1;
                    }
                    while (n >= 0 && tmp.Length < 16);
                    sb.Append(tmp);
                    return;
                }

                break;
        }

        sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    // CSS Counter Styles 3 §6.2.4 (alphabetic): bijective base 26.
    private static void Alpha(StringBuilder sb, int value, char a)
    {
        Span<char> buf = stackalloc char[8];
        int len = 0;
        int n = value;
        while (n > 0 && len < buf.Length)
        {
            n--;
            buf[len++] = (char)(a + (n % 26));
            n /= 26;
        }

        for (int i = len - 1; i >= 0; i--)
        {
            sb.Append(buf[i]);
        }
    }

    private static readonly int[] RomanValues = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
    private static readonly string[] RomanUpper = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

    private static void Roman(StringBuilder sb, int value, bool upper)
    {
        int n = value;
        for (int i = 0; i < RomanValues.Length; i++)
        {
            while (n >= RomanValues[i])
            {
                sb.Append(upper ? RomanUpper[i] : RomanUpper[i].ToLowerInvariantAscii());
                n -= RomanValues[i];
            }
        }
    }
}
