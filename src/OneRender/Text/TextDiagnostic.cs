using System.Globalization;
using System.Text;

namespace OneRender.Text;

/// <summary>What a <see cref="TextDiagnostic"/> reports.</summary>
public enum TextDiagnosticKind : byte
{
    /// <summary>No face in the font list or the default faces has a glyph for a character. <see cref="TextDiagnostic.Package"/> names the opt-in package that would.</summary>
    MissingGlyph = 0,

    /// <summary>A font resource was found but its data could not be used (damaged, unsupported or over a limit).</summary>
    FontLoadFailed,

    /// <summary>The resolver returned nothing for a font URL.</summary>
    FontResourceMissing,

    /// <summary>An <c>@font-face</c> rule was dropped: it lacks a family or a usable source.</summary>
    InvalidFontFaceRule,

    /// <summary>An <c>@font-face</c> descriptor had an invalid value and was ignored.</summary>
    InvalidDescriptor,

    /// <summary>A <c>src</c> entry was skipped: an unsupported format or technology, or a <c>local()</c> name with no provider or no match.</summary>
    SourceSkipped,

    /// <summary>The collection holds no fonts at all, so text cannot be shaped.</summary>
    NoFonts,
}

/// <summary>
/// A problem found while loading or using fonts, as a plain value. Diagnostics never throw and never stop layout; a host can
/// show them or log them.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="CodePoint">The Unicode scalar value concerned, or -1.</param>
/// <param name="Package">For <see cref="TextDiagnosticKind.MissingGlyph"/>: the NuGet package id that ships fonts covering the character; null when no opt-in package covers it.</param>
/// <param name="Subject">The font family, URL or descriptor name concerned, or null.</param>
/// <param name="Detail">A fixed phrase saying what is wrong, or null.</param>
public readonly record struct TextDiagnostic(TextDiagnosticKind Kind, int CodePoint = -1, string? Package = null, string? Subject = null, string? Detail = null)
{
    /// <summary>A readable one-line description. Built on demand.</summary>
    public string Message
    {
        get
        {
            var sb = new StringBuilder();
            switch (Kind)
            {
                case TextDiagnosticKind.MissingGlyph:
                    sb.Append("No font has a glyph for U+").Append(CodePoint.ToString("X4", CultureInfo.InvariantCulture)).Append('.');
                    sb.Append(Package is null ? " No optional fonts package covers it." : " Add the " + Package + " package to cover it.");
                    break;
                case TextDiagnosticKind.NoFonts:
                    sb.Append("No fonts are registered; text cannot be shaped.");
                    break;
                default:
                    sb.Append(Kind.ToString());
                    break;
            }

            if (Subject is not null)
            {
                sb.Append(" [").Append(Subject).Append(']');
            }

            if (Detail is not null)
            {
                sb.Append(' ').Append(Detail);
            }

            return sb.ToString();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Message;
}

/// <summary>A bounded list of diagnostics. Equal reports are recorded once.</summary>
public sealed class TextDiagnostics
{
    /// <summary>The most diagnostics kept; further ones are counted in <see cref="Dropped"/>.</summary>
    public const int Capacity = 512;

    private readonly List<TextDiagnostic> _items = [];
    private readonly HashSet<TextDiagnostic> _seen = [];

    /// <summary>Creates an empty list.</summary>
    public TextDiagnostics()
    {
    }

    /// <summary>The recorded diagnostics in the order first reported.</summary>
    public IReadOnlyList<TextDiagnostic> Items => _items;

    /// <summary>How many distinct diagnostics were not kept because the list was full.</summary>
    public int Dropped { get; private set; }

    /// <summary>Forgets everything recorded so far (for example between documents).</summary>
    public void Clear()
    {
        _items.Clear();
        _seen.Clear();
        Dropped = 0;
    }

    internal void Report(in TextDiagnostic d)
    {
        // A set lookup does not allocate once the item is known, so repeated reports on the shaping path stay at zero bytes.
        if (_seen.Contains(d))
        {
            return;
        }

        if (_items.Count >= Capacity)
        {
            Dropped++;
            return;
        }

        _seen.Add(d);
        _items.Add(d);
    }
}
