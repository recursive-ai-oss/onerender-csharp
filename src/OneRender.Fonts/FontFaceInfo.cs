using System.IO;
using System.Reflection;

namespace OneRender.Fonts;

/// <summary>Whether a face is upright or italic.</summary>
public enum FontSlant
{
    /// <summary>An upright face.</summary>
    Normal = 0,

    /// <summary>An italic (or oblique) face.</summary>
    Italic = 1,
}

/// <summary>
/// One font file shipped in the package: plain data describing it, and a way to open its bytes.
/// The bytes are the upstream release file, unmodified.
/// </summary>
public sealed class FontFaceInfo
{
    private readonly string _resourceName;
    private readonly string _licenseResourceName;
    private readonly Assembly? _assembly;

    internal FontFaceInfo(string family, int weight, FontSlant slant, string fileName, string folder, int length)
    {
        Family = family;
        Weight = weight;
        Slant = slant;
        FileName = fileName;
        Length = length;
        _resourceName = "fonts/" + folder + "/" + fileName;
        _licenseResourceName = "fonts/" + folder + "/OFL.txt";
        Stretch = 100;
    }

    /// <summary>Describes a font file embedded in another assembly as <c>fonts/&lt;fileName&gt;</c>, with the licence at <c>fonts/OFL.txt</c> (an optional fonts package).</summary>
    /// <param name="assembly">The assembly that embeds the file.</param>
    /// <param name="family">The family name to register the face under.</param>
    /// <param name="weight">The CSS weight, 1 to 1000.</param>
    /// <param name="slant">Upright or italic.</param>
    /// <param name="fileName">The file name as published upstream.</param>
    /// <param name="length">The size of the file in bytes.</param>
    /// <param name="faceIndex">Which face of a TrueType Collection to use; 0 for a plain font file.</param>
    public FontFaceInfo(Assembly assembly, string family, int weight, FontSlant slant, string fileName, int length, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(fileName);
        _assembly = assembly;
        Family = family;
        Weight = weight;
        Slant = slant;
        FileName = fileName;
        Length = length;
        FaceIndex = faceIndex;
        _resourceName = "fonts/" + fileName;
        _licenseResourceName = "fonts/OFL.txt";
        Stretch = 100;
    }

    /// <summary>The family name as the font declares it, for example <c>Liberation Sans</c>. Never null or empty.</summary>
    public string Family { get; }

    /// <summary>The CSS weight, 1 to 1000. The shipped faces are 400 (regular) or 700 (bold).</summary>
    public int Weight { get; }

    /// <summary>Upright or italic.</summary>
    public FontSlant Slant { get; }

    /// <summary>The CSS <c>font-stretch</c> as a percentage; 100 (normal) for every shipped face.</summary>
    public int Stretch { get; }

    /// <summary>The file name as published upstream, for example <c>LiberationSans-Bold.ttf</c>.</summary>
    public string FileName { get; }

    /// <summary>Which face of the file to use: 0 for a plain font file, the index inside a TrueType Collection otherwise. Several entries can share one <see cref="FileName"/> with different indexes.</summary>
    public int FaceIndex { get; }

    internal string LicenseResourceName => _licenseResourceName;

    internal Stream OpenLicense() => OpenFrom(_assembly, _licenseResourceName);

    /// <summary>The size of the font file in bytes.</summary>
    public int Length { get; }

    /// <summary>
    /// Opens the font file as a read-only, seekable stream over the bytes embedded in the assembly.
    /// The font data is not copied: the only allocation is the small stream object (one
    /// <see cref="UnmanagedMemoryStream"/>). Dispose the stream when done; each call returns a new stream.
    /// </summary>
    /// <returns>A stream positioned at the start of the font file. Never null.</returns>
    /// <exception cref="InvalidOperationException">The resource is missing from the assembly (a broken build).</exception>
    public Stream Open() => OpenFrom(_assembly, _resourceName);

    internal static Stream OpenFrom(Assembly? assembly, string name) =>
        assembly is null ? DefaultFonts.OpenResource(name) : assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Embedded font resource is missing: " + name);

    /// <summary>Reads the whole font file into a new array of <see cref="Length"/> bytes (one allocation, one copy).</summary>
    /// <returns>A new array holding the font file.</returns>
    public byte[] ReadAllBytes()
    {
        byte[] bytes = new byte[Length];
        using Stream s = Open();
        s.ReadExactly(bytes);
        return bytes;
    }

    /// <inheritdoc />
    public override string ToString() => Family + " " + Weight + (Slant == FontSlant.Italic ? " italic" : "");
}
