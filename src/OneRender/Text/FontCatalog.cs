using OneRender.Style;
using System.Globalization;
using System.Text;
using OneFont;

namespace OneRender.Text;

/// <summary>
/// The fonts a document can use and the CSS Fonts 4 §5 matching of a <see cref="FontRequest"/> against them. It holds the
/// faces registered by the host (usually the packaged default fonts), the faces of <c>@font-face</c> rules, and, when the host
/// opts in, a provider of installed fonts. Font files named by <c>@font-face</c> are read through the host's
/// <see cref="IFontResourceResolver"/>, once, the first time text needs them.
/// </summary>
/// <remarks>
/// A collection and everything resolved from it belong to one thread at a time. Share fonts between threads by giving each
/// thread its own collection (the font files are parsed per collection).
/// </remarks>
public sealed class FontCatalog
{
    /// <summary>The most faces a collection holds (registered, <c>@font-face</c> and installed together). Further ones are refused.</summary>
    public const int MaxFaces = 4096;

    private const int MaxResolved = 4096;
    private const int MaxMissingMemo = 4096;

    private readonly Dictionary<string, List<FaceEntry>> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FaceEntry>> _web = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FaceEntry>> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _registeredOrder = [];
    private readonly Dictionary<GenericFamily, string> _generic = [];
    private readonly Dictionary<FontRequest, ResolvedFontList> _resolved = new(StyleComparer.Instance);
    private readonly Dictionary<SystemFontFace, ResolvedFace> _systemFallback = [];
    private readonly HashSet<int> _noSystemFallback = [];
    private int _nextId;
    private int _nextListId;

    /// <summary>Creates a collection.</summary>
    /// <param name="resolver">Supplies <c>@font-face</c> files; null means every <c>url()</c> source is reported missing.</param>
    /// <param name="aliases">Maps generic keywords and alias names to registered families; null maps nothing.</param>
    /// <param name="systemFonts">Opt-in installed fonts; null (the default) uses only registered and <c>@font-face</c> fonts.</param>
    /// <param name="limits">Bounds on font data; <see cref="FontLimits.Default"/> when null.</param>
    public FontCatalog(IFontResourceResolver? resolver = null, IFontFamilyAliases? aliases = null, ISystemFontProvider? systemFonts = null, FontLimits? limits = null)
    {
        Resolver = resolver;
        Aliases = aliases;
        SystemFonts = systemFonts;
        Limits = limits ?? FontLimits.Default;
    }

    /// <summary>Problems found while loading and using fonts. Bounded; equal reports are kept once.</summary>
    public TextDiagnostics Diagnostics { get; } = new();

    /// <summary>The host's font file source for <c>@font-face</c>. May be set at any time; changing it does not retry fonts that already failed.</summary>
    public IFontResourceResolver? Resolver { get; set; }

    /// <summary>Alias and generic family names.</summary>
    public IFontFamilyAliases? Aliases { get; set; }

    /// <summary>The opt-in installed-font provider; null by default.</summary>
    public ISystemFontProvider? SystemFonts { get; set; }

    /// <summary>The bounds applied to every font file.</summary>
    public FontLimits Limits { get; }

    /// <summary>The number of faces held.</summary>
    public int FaceCount => _nextId;

    /// <summary>Increases whenever the set of faces changes. A <see cref="ResolvedFontList"/> made under an older version is not returned again.</summary>
    public int Version { get; private set; }

    /// <summary>Registers a font file. The family, weight, style and stretch come from the font unless given.</summary>
    /// <param name="data">The font file (TrueType, OpenType, WOFF or WOFF2).</param>
    /// <param name="family">The family name to register under; the font's own family name when null.</param>
    /// <param name="weight">The weight; the font's own when null.</param>
    /// <param name="style">The style; the font's own (italic or normal) when null.</param>
    /// <param name="stretch">The stretch percentage; the font's width class when null.</param>
    /// <returns>True when the face was added; false when the data could not be loaded (see <see cref="Diagnostics"/>) or the collection is full.</returns>
    public bool AddFont(byte[] data, string? family = null, float? weight = null, FontStyleKind? style = null, float? stretch = null)
    {
        FontLoadResult r = FontLoader.Load(data, Limits);
        if (r.Face is null)
        {
            Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontLoadFailed, Subject: family, Detail: r.Detail));
            return false;
        }

        return AddFont(r.Face, family ?? r.Face.FamilyName, weight ?? r.Face.Weight, style ?? (r.Face.IsItalic ? FontStyleKind.Italic : FontStyleKind.Normal), stretch ?? WidthClassToPercent(r.Face));
    }

    /// <summary>Registers an already loaded face.</summary>
    /// <param name="face">The face.</param>
    /// <param name="family">The family name to register under.</param>
    /// <param name="weight">The weight, 1 to 1000.</param>
    /// <param name="style">The style kind.</param>
    /// <param name="stretch">The stretch as a percentage of normal.</param>
    /// <returns>False when the collection is full.</returns>
    public bool AddFont(FontFace face, string family, float weight, FontStyleKind style, float stretch = 100)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(family);
        if (_nextId >= MaxFaces)
        {
            Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontLoadFailed, Subject: family, Detail: "The collection is full."));
            return false;
        }

        var e = FaceEntry.FromFace(_nextId++, family, face, weight, style, stretch);
        if (!_registered.TryGetValue(family, out List<FaceEntry>? list))
        {
            _registered[family] = list = [];
            _registeredOrder.Add(family);
        }

        list.Add(e);
        Changed();
        return true;
    }

    /// <summary>Adds the faces of an <c>@font-face</c> rule. The font file is read the first time text needs it.</summary>
    /// <param name="rule">The parsed rule.</param>
    /// <returns>False when the collection is full.</returns>
    public bool AddFontFace(FontFaceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (_nextId >= MaxFaces)
        {
            Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.InvalidFontFaceRule, Subject: rule.Family, Detail: "The collection is full."));
            return false;
        }

        FaceEntry e = FaceEntry.FromRule(_nextId++, rule);
        if (!_web.TryGetValue(rule.Family, out List<FaceEntry>? list))
        {
            _web[rule.Family] = list = [];
        }

        list.Add(e);
        Changed();
        return true;
    }

    /// <summary>Chooses the family a generic keyword stands for, overriding <see cref="Aliases"/>.</summary>
    /// <param name="generic">The generic family.</param>
    /// <param name="family">A registered or <c>@font-face</c> family name.</param>
    public void SetGenericFamily(GenericFamily generic, string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        _generic[generic] = family;
        Changed();
    }

    /// <summary>
    /// Matches a request against the collection (CSS Fonts 4 §5.2). Equal requests (ignoring size) return the same object, with no
    /// allocation, until the faces change.
    /// </summary>
    /// <param name="request">The computed font properties.</param>
    /// <returns>The matching faces in family order, then the default faces.</returns>
    public ResolvedFontList Resolve(FontRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_resolved.TryGetValue(request, out ResolvedFontList? hit))
        {
            return hit;
        }

        if (_resolved.Count >= MaxResolved)
        {
            _resolved.Clear();
        }

        FontSlot[] slots = BuildSlots(request, out int listed);
        var list = new ResolvedFontList(this, request, slots, listed, ++_nextListId);
        _resolved[request] = list;
        return list;
    }

    internal void NoteFaceFailed() => Changed();

    private void Changed()
    {
        Version++;
        // Lists made before the change keep working but are no longer handed out.
        foreach (ResolvedFontList l in _resolved.Values)
        {
            l.IsStale = true;
        }

        _resolved.Clear();
    }

    private sealed class StyleComparer : IEqualityComparer<FontRequest>
    {
        public static readonly StyleComparer Instance = new();

        public bool Equals(FontRequest? x, FontRequest? y) => x is null ? y is null : x.StyleEquals(y);

        public int GetHashCode(FontRequest obj) => obj.StyleHash;
    }

    // ---- matching --------------------------------------------------------------------------------------------------

    private FontSlot[] BuildSlots(FontRequest request, out int listed)
    {
        var slots = new List<FontSlot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamilyName f in request.Families)
        {
            AddSlot(slots, seen, f, request);
        }

        listed = slots.Count;

        // The default faces: the three generic families, then every other registered family, in registration order.
        AddSlot(slots, seen, FontFamilyName.OfGeneric(GenericFamily.SansSerif), request);
        AddSlot(slots, seen, FontFamilyName.OfGeneric(GenericFamily.Serif), request);
        AddSlot(slots, seen, FontFamilyName.OfGeneric(GenericFamily.Monospace), request);
        foreach (string family in _registeredOrder)
        {
            AddSlot(slots, seen, new FontFamilyName(family), request);
        }

        return slots.ToArray();
    }

    private void AddSlot(List<FontSlot> slots, HashSet<string> seen, FontFamilyName name, FontRequest request)
    {
        bool generic = name.Generic != GenericFamily.None;
        string lookup = name.Name;
        if (generic)
        {
            lookup = ResolveGeneric(name.Generic);
        }

        List<FaceEntry>? faces = FindFamily(lookup, generic, out string canonical);
        if (faces is null || faces.Count == 0 || !seen.Add(canonical))
        {
            return;
        }

        FaceEntry[] group = Narrow(faces, request);
        if (group.Length == 0)
        {
            return;
        }

        var resolved = new ResolvedFace[group.Length];
        for (int i = 0; i < group.Length; i++)
        {
            FaceEntry e = group[i];
            bool bold = (request.Synthesis & FontSynthesis.Weight) != 0 && request.Weight >= 600 && e.WeightMax < 600;
            bool italic = (request.Synthesis & FontSynthesis.Style) != 0 && request.Style != FontStyleKind.Normal && e.Style == FontStyleKind.Normal;
            resolved[i] = new ResolvedFace(e, bold, italic, request.SyntheticSlant);
        }

        slots.Add(new FontSlot(group, resolved, generic));
    }

    private string ResolveGeneric(GenericFamily generic)
    {
        if (_generic.TryGetValue(generic, out string? set))
        {
            return set;
        }

        if (Aliases is not null && Aliases.TryResolve(FontFamilyName.GenericKeyword(generic), out string? mapped))
        {
            return mapped;
        }

        // Without a mapping the first registered family stands in, so text still renders.
        return _registeredOrder.Count > 0 ? _registeredOrder[0] : string.Empty;
    }

    // @font-face first, then registered fonts, then aliases, then installed fonts (CSS Fonts 4 §5.2). When a family is defined
    // by @font-face rules, a font of the same name is never matched instead.
    private List<FaceEntry>? FindFamily(string name, bool generic, out string canonical)
    {
        canonical = name;
        if (name.Length == 0)
        {
            return null;
        }

        if (_web.TryGetValue(name, out List<FaceEntry>? web))
        {
            return web;
        }

        if (_registered.TryGetValue(name, out List<FaceEntry>? reg))
        {
            return reg;
        }

        if (!generic && Aliases is not null && Aliases.TryResolve(name.AsSpan(), out string? target))
        {
            canonical = target;
            if (_web.TryGetValue(target, out List<FaceEntry>? aliasWeb))
            {
                return aliasWeb;
            }

            if (_registered.TryGetValue(target, out List<FaceEntry>? aliasReg))
            {
                return aliasReg;
            }
        }

        if (SystemFonts is not null)
        {
            if (!_installed.TryGetValue(name, out List<FaceEntry>? sys))
            {
                sys = [];
                foreach (SystemFontFace f in SystemFonts.GetFaces(name))
                {
                    if (_nextId < MaxFaces)
                    {
                        sys.Add(FaceEntry.FromSystem(_nextId++, f));
                    }
                }

                _installed[name] = sys;
            }

            return sys;
        }

        return null;
    }

    // CSS Fonts 4 §5.2: narrow by width, then style, then weight. Faces whose font failed to load are treated as not present.
    private static FaceEntry[] Narrow(List<FaceEntry> family, FontRequest request)
    {
        var set = new List<FaceEntry>(family.Count);
        foreach (FaceEntry e in family)
        {
            if (!e.IsFailed)
            {
                set.Add(e);
            }
        }

        if (set.Count == 0)
        {
            return [];
        }

        NarrowByStretch(set, request.Stretch);
        NarrowByStyle(set, request);
        NarrowByWeight(set, request.Weight);

        // Last defined first: of faces with overlapping unicode-range, the later rule wins.
        var result = new FaceEntry[set.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = set[set.Count - 1 - i];
        }

        return result;
    }

    // A rank is (tier, distance): lower is better. The best value of a range is the one with the lowest rank.
    private delegate (int Tier, float Dist) RankFunc(float value, float desired);

    private static (int, float) StretchRank(float v, float d)
    {
        if (d <= 100)
        {
            return v <= d ? (0, d - v) : (1, v - d);
        }

        return v >= d ? (0, v - d) : (1, d - v);
    }

    private static (int, float) WeightRank(float v, float d)
    {
        if (d >= 400 && d <= 500)
        {
            if (v >= d && v <= 500)
            {
                return (0, v - d);
            }

            if (v < d)
            {
                return (1, d - v);
            }

            return (2, v - 500);
        }

        if (d < 400)
        {
            return v <= d ? (0, d - v) : (1, v - d);
        }

        return v >= d ? (0, v - d) : (1, d - v);
    }

    private static void NarrowByStretch(List<FaceEntry> set, float desired) =>
        NarrowByRank(set, desired, static e => (e.StretchMin, e.StretchMax), StretchRank, null);

    private static void NarrowByWeight(List<FaceEntry> set, float desired) =>
        NarrowByRank(set, desired, static e => (e.WeightMin, e.WeightMax), WeightRank, 500f);

    private static void NarrowByRank(List<FaceEntry> set, float desired, Func<FaceEntry, (float Lo, float Hi)> range, RankFunc rank, float? pivot)
    {
        if (set.Count <= 1)
        {
            return;
        }

        bool have = false;
        (int Tier, float Dist) best = default;
        float bestValue = 0;
        foreach (FaceEntry e in set)
        {
            (float lo, float hi) = range(e);
            // Candidate values of a range: its ends, the desired value clamped into it, and the pivot clamped into it.
            Span<float> cands = [lo, hi, Math.Clamp(desired, lo, hi), Math.Clamp(pivot ?? desired, lo, hi)];
            foreach (float v in cands)
            {
                (int, float) r = rank(v, desired);
                if (!have || r.CompareTo(best) < 0 || (r.CompareTo(best) == 0 && v < bestValue))
                {
                    have = true;
                    best = r;
                    bestValue = v;
                }
            }
        }

        set.RemoveAll(e =>
        {
            (float lo, float hi) = range(e);
            return bestValue < lo || bestValue > hi;
        });
    }

    private const float Eps = 1e-4f;

    private static void NarrowByStyle(List<FaceEntry> set, FontRequest request)
    {
        // Italic faces and oblique-like faces (normal is oblique 0deg). A step that finds something wins.
        float theta = request.Style == FontStyleKind.Oblique ? request.ObliqueAngle : 0;
        bool mirror = request.Style == FontStyleKind.Oblique && theta < 0;
        if (mirror)
        {
            theta = -theta;
        }

        List<FaceEntry>? picked = null;
        switch (request.Style)
        {
            case FontStyleKind.Normal:
                picked = Oblique(set, 0, float.PositiveInfinity, ascending: true, mirror: false)
                    ?? Italic(set)
                    ?? Oblique(set, float.NegativeInfinity, -Eps, ascending: false, mirror: false);
                break;
            case FontStyleKind.Italic:
                picked = Italic(set)
                    ?? Oblique(set, 11, float.PositiveInfinity, ascending: true, mirror: false)
                    ?? Oblique(set, Eps, 11 - Eps, ascending: false, mirror: false)
                    ?? Oblique(set, float.NegativeInfinity, 0, ascending: false, mirror: false);
                break;
            default:
                if (theta >= 11)
                {
                    picked = Contains(set, theta, mirror)
                        ?? Oblique(set, theta + Eps, float.PositiveInfinity, ascending: true, mirror: mirror)
                        ?? Oblique(set, Eps, theta - Eps, ascending: false, mirror: mirror)
                        ?? Italic(set)
                        ?? Oblique(set, float.NegativeInfinity, 0, ascending: false, mirror: mirror);
                }
                else
                {
                    picked = Contains(set, theta, mirror)
                        ?? Oblique(set, Eps, theta - Eps, ascending: false, mirror: mirror)
                        ?? Oblique(set, theta + Eps, float.PositiveInfinity, ascending: true, mirror: mirror)
                        ?? Italic(set)
                        ?? Oblique(set, float.NegativeInfinity, 0, ascending: false, mirror: mirror);
                }

                break;
        }

        if (picked is not null)
        {
            set.Clear();
            set.AddRange(picked);
        }
    }

    private static List<FaceEntry>? Italic(List<FaceEntry> set)
    {
        List<FaceEntry>? r = null;
        foreach (FaceEntry e in set)
        {
            if (e.Style == FontStyleKind.Italic)
            {
                (r ??= []).Add(e);
            }
        }

        return r;
    }

    private static (float Lo, float Hi)? Angles(FaceEntry e, bool mirror)
    {
        switch (e.Style)
        {
            case FontStyleKind.Normal:
                return (0, 0);
            case FontStyleKind.Oblique:
                return mirror ? (-e.ObliqueMax, -e.ObliqueMin) : (e.ObliqueMin, e.ObliqueMax);
            default:
                return null;
        }
    }

    private static List<FaceEntry>? Contains(List<FaceEntry> set, float angle, bool mirror)
    {
        List<FaceEntry>? r = null;
        foreach (FaceEntry e in set)
        {
            if (Angles(e, mirror) is { } a && angle >= a.Lo && angle <= a.Hi)
            {
                (r ??= []).Add(e);
            }
        }

        return r;
    }

    // The faces holding the nearest angle inside the window [wlo, whi]: the lowest angle when ascending, the highest when not.
    private static List<FaceEntry>? Oblique(List<FaceEntry> set, float wlo, float whi, bool ascending, bool mirror)
    {
        bool have = false;
        float best = 0;
        foreach (FaceEntry e in set)
        {
            if (Angles(e, mirror) is not { } a)
            {
                continue;
            }

            float lo = Math.Max(a.Lo, wlo);
            float hi = Math.Min(a.Hi, whi);
            if (lo > hi)
            {
                continue;
            }

            float v = ascending ? lo : hi;
            if (!have || (ascending ? v < best : v > best))
            {
                have = true;
                best = v;
            }
        }

        if (!have)
        {
            return null;
        }

        List<FaceEntry>? r = null;
        foreach (FaceEntry e in set)
        {
            if (Angles(e, mirror) is { } a && best >= a.Lo && best <= a.Hi)
            {
                (r ??= []).Add(e);
            }
        }

        return r;
    }

    private static float WidthClassToPercent(FontFace face)
    {
        ReadOnlySpan<byte> os2 = face.GetTable("OS/2").Span;
        if (os2.Length >= 8)
        {
            int w = (os2[6] << 8) | os2[7];
            return w switch
            {
                1 => 50,
                2 => 62.5f,
                3 => 75,
                4 => 87.5f,
                6 => 112.5f,
                7 => 125,
                8 => 150,
                9 => 200,
                _ => 100,
            };
        }

        return 100;
    }

    // ---- fallback --------------------------------------------------------------------------------------------------

    internal ResolvedFace? FindSystemFallback(int codePoint)
    {
        ISystemFontProvider? provider = SystemFonts;
        if (provider is null || _noSystemFallback.Contains(codePoint))
        {
            return null;
        }

        SystemFontFace? sf = provider.FindFallback(codePoint);
        if (sf is not null)
        {
            if (!_systemFallback.TryGetValue(sf, out ResolvedFace? rf))
            {
                if (_nextId >= MaxFaces)
                {
                    return null;
                }

                var entry = FaceEntry.FromSystem(_nextId++, sf);
                rf = new ResolvedFace(entry, false, false, 0);
                _systemFallback[sf] = rf;
            }

            if (rf.Entry.EnsureLoaded(this) && rf.Entry.Face!.HasGlyph(codePoint))
            {
                return rf;
            }
        }

        if (_noSystemFallback.Count >= MaxMissingMemo)
        {
            _noSystemFallback.Clear();
        }

        _noSystemFallback.Add(codePoint);
        return null;
    }

    // ---- loading ---------------------------------------------------------------------------------------------------

    internal FontFace? LoadFace(FaceEntry entry, byte[]? data, FontFaceRule? rule, SystemFontFace? system)
    {
        if (data is not null)
        {
            return Finish(FontLoader.Load(data, Limits), entry.Family);
        }

        if (system is not null)
        {
            byte[]? bytes = system.Load();
            if (bytes is null)
            {
                Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontResourceMissing, Subject: system.Family, Detail: "The installed font could not be read."));
                return null;
            }

            return Finish(FontLoader.Load(bytes, Limits), system.Family);
        }

        if (rule is null)
        {
            return null;
        }

        // CSS Fonts 4 §4.3: try each src entry in order; the first that loads is used.
        foreach (FontSource src in rule.Sources)
        {
            if (src.Kind == FontSourceKind.Local)
            {
                ISystemFontProvider? provider = SystemFonts;
                if (provider is null)
                {
                    Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.SourceSkipped, Subject: src.Value, Detail: "local() needs a system font provider."));
                    continue;
                }

                SystemFontFace? sf = provider.FindByName(src.Value);
                byte[]? local = sf?.Load();
                if (local is null)
                {
                    Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.SourceSkipped, Subject: src.Value, Detail: "No installed font has this name."));
                    continue;
                }

                FontFace? lf = Finish(FontLoader.Load(local, Limits), src.Value);
                if (lf is not null)
                {
                    return lf;
                }

                continue;
            }

            if (!FormatSupported(src.Format) || !TechSupported(src.Tech))
            {
                Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.SourceSkipped, Subject: src.Value, Detail: "The format or technology is not supported."));
                continue;
            }

            byte[]? file = src.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? DecodeDataUrl(src.Value, Limits.MaxInputBytes)
                : Resolver?.LoadFont(src.Value, rule.BaseUrl);
            if (file is null)
            {
                Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontResourceMissing, Subject: src.Value, Detail: Resolver is null && !src.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "No resource resolver is set." : "The resource was not found."));
                continue;
            }

            FontFace? face = Finish(FontLoader.Load(file, Limits), src.Value);
            if (face is not null)
            {
                return face;
            }
        }

        return null;
    }

    private FontFace? Finish(FontLoadResult r, string subject)
    {
        if (r.Face is null)
        {
            Diagnostics.Report(new TextDiagnostic(TextDiagnosticKind.FontLoadFailed, Subject: subject, Detail: r.Detail));
        }

        return r.Face;
    }

    private static bool FormatSupported(string? format)
    {
        if (format is null)
        {
            return true;
        }

        return format is "woff2" or "woff" or "truetype" or "opentype" or "woff2-variations" or "woff-variations" or "truetype-variations" or "opentype-variations";
    }

    private static bool TechSupported(IReadOnlyList<string> tech)
    {
        foreach (string t in tech)
        {
            // Variable fonts load at their default instance; colour, AAT and palette technologies are not drawn.
            if (t is not ("features-opentype" or "variations" or "incremental"))
            {
                return false;
            }
        }

        return true;
    }

    // RFC 2397: data:[<mediatype>][;base64],<data>. Bounded by the input limit before anything is allocated.
    internal static byte[]? DecodeDataUrl(string url, int maxBytes)
    {
        int comma = url.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return null;
        }

        bool base64 = url.AsSpan(5, comma - 5).EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<char> payload = url.AsSpan(comma + 1);
        if (base64)
        {
            if (payload.Length / 4L * 3 > maxBytes + 3)
            {
                return null;
            }

            var sb = new StringBuilder(payload.Length);
            string text = payload.Contains('%') ? PercentDecode(url, comma + 1, maxBytes * 2)[(comma + 1)..] : payload.ToString();
            foreach (char c in text)
            {
                if (c is not (' ' or '\t' or '\n' or '\r' or '\f'))
                {
                    sb.Append(c);
                }
            }

            byte[] buffer = new byte[(sb.Length / 4 * 3) + 3];
            return Convert.TryFromBase64String(sb.ToString(), buffer, out int written) && written <= maxBytes ? buffer.AsSpan(0, written).ToArray() : null;
        }

        if (payload.Length > maxBytes * 3L)
        {
            return null;
        }

        var bytes = new List<byte>(payload.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            char c = payload[i];
            if (c == '%' && i + 2 < payload.Length && IsHex(payload[i + 1]) && IsHex(payload[i + 2]))
            {
                bytes.Add((byte)((CssValueTokenizer.HexValue(payload[i + 1]) << 4) | CssValueTokenizer.HexValue(payload[i + 2])));
                i += 2;
            }
            else if (c < 0x80)
            {
                bytes.Add((byte)c);
            }
            else
            {
                return null;
            }

            if (bytes.Count > maxBytes)
            {
                return null;
            }
        }

        return bytes.ToArray();
    }

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static string PercentDecode(string url, int from, int cap)
    {
        var sb = new StringBuilder(url.Length);
        sb.Append(url, 0, from);
        for (int i = from; i < url.Length && sb.Length < cap; i++)
        {
            if (url[i] == '%' && i + 2 < url.Length && IsHex(url[i + 1]) && IsHex(url[i + 2]))
            {
                sb.Append((char)((CssValueTokenizer.HexValue(url[i + 1]) << 4) | CssValueTokenizer.HexValue(url[i + 2])));
                i += 2;
            }
            else
            {
                sb.Append(url[i]);
            }
        }

        return sb.ToString();
    }
}
