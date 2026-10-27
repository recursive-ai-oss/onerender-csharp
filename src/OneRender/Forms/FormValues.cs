using System.Globalization;

namespace OneRender.Forms;

/// <summary>
/// The numbers behind <c>range</c>, <c>progress</c> and <c>meter</c> controls (HTML §4.10.5.1.13, §4.10.13 and §4.10.14): parsing
/// the attributes, clamping the value, and turning it into how far along the track the bar is drawn.
/// </summary>
public static class FormValues
{
    /// <summary>Parses a floating-point number as the HTML attribute rules accept it (§2.3.4.3): no leading plus, no exponent without digits, finite.</summary>
    /// <param name="text">The attribute value; null or empty fails.</param>
    /// <param name="value">The number.</param>
    /// <returns>True when the text is a valid floating-point number.</returns>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 64)
        {
            return false;
        }

        char c0 = text[0];
        if (c0 == '+' || (!char.IsAsciiDigit(c0) && c0 != '-' && c0 != '.'))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
        {
            return false;
        }

        value = d;
        return true;
    }

    /// <summary>
    /// The value of a range control and its position on the track (HTML §4.10.5.1.13): the minimum defaults to 0, the maximum to 100
    /// (and is at least the minimum), the default value is the midpoint, and the value snaps to the step and is clamped.
    /// </summary>
    /// <param name="valueAttribute">The <c>value</c> attribute, or null.</param>
    /// <param name="minAttribute">The <c>min</c> attribute, or null.</param>
    /// <param name="maxAttribute">The <c>max</c> attribute, or null.</param>
    /// <param name="stepAttribute">The <c>step</c> attribute, or null (1; <c>any</c> means no snapping).</param>
    /// <returns>The thumb position from 0 (minimum) to 1 (maximum).</returns>
    public static double RangeFraction(string? valueAttribute, string? minAttribute, string? maxAttribute, string? stepAttribute)
    {
        double min = TryParseNumber(minAttribute, out double mn) ? mn : 0;
        double max = TryParseNumber(maxAttribute, out double mx) ? mx : 100;
        if (max < min)
        {
            max = min;
        }

        double value = TryParseNumber(valueAttribute, out double v) ? v : min + ((max - min) / 2);
        double step = 1;
        bool any = stepAttribute is not null && stepAttribute.Trim().Equals("any", StringComparison.OrdinalIgnoreCase);
        if (!any && TryParseNumber(stepAttribute, out double st) && st > 0)
        {
            step = st;
        }

        if (!any)
        {
            // §4.10.5.1.13: the nearest valid value, with steps counted from the minimum.
            value = min + (Math.Round((value - min) / step, MidpointRounding.AwayFromZero) * step);
            if (value > max)
            {
                value = min + (Math.Floor((max - min) / step) * step);
            }
        }

        value = Math.Clamp(value, min, max);
        return max > min ? (value - min) / (max - min) : 0;
    }

    /// <summary>The fraction a <c>progress</c> bar is filled (§4.10.13): value over max (default 1); NaN when there is no valid value (an indeterminate bar).</summary>
    /// <param name="valueAttribute">The <c>value</c> attribute, or null.</param>
    /// <param name="maxAttribute">The <c>max</c> attribute, or null.</param>
    /// <returns>0 to 1, or NaN.</returns>
    public static double ProgressFraction(string? valueAttribute, string? maxAttribute)
    {
        double max = TryParseNumber(maxAttribute, out double m) && m > 0 ? m : 1;
        if (!TryParseNumber(valueAttribute, out double v) || v < 0)
        {
            return double.NaN;
        }

        return Math.Min(v, max) / max;
    }

    /// <summary>The gauge region a meter's value is in (§4.10.14).</summary>
    public enum MeterRegion
    {
        /// <summary>The optimum region: drawn green.</summary>
        Optimum,

        /// <summary>The suboptimal region: drawn yellow.</summary>
        Suboptimum,

        /// <summary>The even less good region: drawn red.</summary>
        EvenLessGood,
    }

    /// <summary>Resolves a meter's attributes into the fraction it is filled and the region its value falls in (HTML §4.10.14 "meter"; the algorithm for the gauge regions).</summary>
    /// <param name="value">The <c>value</c> attribute, or null (0).</param>
    /// <param name="min">The <c>min</c> attribute, or null (0).</param>
    /// <param name="max">The <c>max</c> attribute, or null (1).</param>
    /// <param name="low">The <c>low</c> attribute, or null (the minimum).</param>
    /// <param name="high">The <c>high</c> attribute, or null (the maximum).</param>
    /// <param name="optimum">The <c>optimum</c> attribute, or null (the midpoint).</param>
    /// <param name="region">Which region the value is in.</param>
    /// <returns>The fraction of the bar that is filled, 0 to 1.</returns>
    public static double MeterFraction(string? value, string? min, string? max, string? low, string? high, string? optimum, out MeterRegion region)
    {
        double mn = TryParseNumber(min, out double a) ? a : 0;
        double mx = TryParseNumber(max, out double b) ? Math.Max(b, mn) : Math.Max(1, mn);
        double v = Math.Clamp(TryParseNumber(value, out double c) ? c : 0, mn, mx);
        double lo = Math.Clamp(TryParseNumber(low, out double d) ? d : mn, mn, mx);
        double hi = Math.Clamp(TryParseNumber(high, out double e) ? e : mx, lo, mx);
        double opt = Math.Clamp(TryParseNumber(optimum, out double f) ? f : mn + ((mx - mn) / 2), mn, mx);

        if (opt < lo)
        {
            region = v < lo ? MeterRegion.Optimum : v <= hi ? MeterRegion.Suboptimum : MeterRegion.EvenLessGood;
        }
        else if (opt > hi)
        {
            region = v > hi ? MeterRegion.Optimum : v >= lo ? MeterRegion.Suboptimum : MeterRegion.EvenLessGood;
        }
        else
        {
            region = v >= lo && v <= hi ? MeterRegion.Optimum : MeterRegion.Suboptimum;
        }

        return mx > mn ? (v - mn) / (mx - mn) : 0;
    }
}
