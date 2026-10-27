namespace OneRender.WptRunner;

/// <summary>
/// The <c>reftest-pages</c> range syntax of WPT print reftests:
/// <c>specifier-item ("," specifier-item)*</c> where an item is <c>int</c> or <c>int? "-" int?</c>; pages count from 1 and an open end runs to the
/// first or last page.
/// </summary>
public static class PageRanges
{
    /// <summary>
    /// The pages of <paramref name="all"/> selected by <paramref name="spec"/>, in document order and each once. A null or blank spec selects every page;
    /// items that do not parse are ignored; numbers beyond the page count select nothing.
    /// </summary>
    public static IReadOnlyList<T> Select<T>(IReadOnlyList<T> all, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return all;
        }

        var keep = new bool[all.Count];
        foreach (string raw in spec.Split(','))
        {
            string item = raw.Trim();
            int dash = item.IndexOf('-', StringComparison.Ordinal);
            int lo, hi;
            if (dash < 0)
            {
                if (!int.TryParse(item, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out lo))
                {
                    continue;
                }

                hi = lo;
            }
            else
            {
                string a = item[..dash].Trim(), b = item[(dash + 1)..].Trim();
                lo = 1;
                hi = int.MaxValue;
                if ((a.Length > 0 && !int.TryParse(a, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out lo))
                    || (b.Length > 0 && !int.TryParse(b, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out hi)))
                {
                    continue;
                }
            }

            for (int p = Math.Max(lo, 1); p <= Math.Min(hi, all.Count); p++)
            {
                keep[p - 1] = true;
            }
        }

        var result = new List<T>();
        for (int i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                result.Add(all[i]);
            }
        }

        return result;
    }
}
