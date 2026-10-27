using System.IO;
using System.Security.Cryptography;
using System.Text;
using OneRender.Fonts;
using OneRender.TestSupport;

namespace OneRender.Tests;

public class FontsTests
{
    [Fact]
    public void ShipsTwentyFacesInFiveFamilies()
    {
        Assert.Equal(20, DefaultFonts.Faces.Count);
        Assert.Equal(["Liberation Sans", "Liberation Serif", "Liberation Mono", "Carlito", "Caladea"], DefaultFonts.Families);
        foreach (string family in DefaultFonts.Families)
        {
            Assert.Equal(4, DefaultFonts.GetFaces(family).Length);
        }
    }

    [Fact]
    public void EveryFaceParsesAndMatchesItsDeclaration()
    {
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            byte[] bytes = info.ReadAllBytes();
            Assert.Equal(info.Length, bytes.Length);

            OneFont.FontFace face = OneFont.FontFace.Load(bytes);
            Assert.True(face.GlyphCount > 100, info.FileName);

            // The declared family, weight and style against the font's own name and OS/2 tables.
            Assert.Equal(info.Family, face.FamilyName);
            Assert.Equal(info.Family, NameRecord(face.GetTable("name").Span, 1));
            Assert.Equal(info.Weight, face.Weight);
            Assert.Equal(info.Slant == FontSlant.Italic, face.IsItalic);

            ReadOnlySpan<byte> os2 = face.GetTable("OS/2").Span;
            int weightClass = (os2[4] << 8) | os2[5];
            int widthClass = (os2[6] << 8) | os2[7];
            int fsSelection = (os2[62] << 8) | os2[63];
            Assert.Equal(info.Weight, weightClass);
            Assert.Equal(5, widthClass); // 5 = normal width = font-stretch 100%
            Assert.Equal(100, info.Stretch);
            Assert.Equal(info.Slant == FontSlant.Italic, (fsSelection & 1) != 0);
            Assert.Equal(info.Weight == 700, (fsSelection & 0x20) != 0);

            Assert.EndsWith(info.Slant == FontSlant.Italic ? (info.Weight == 700 ? "BoldItalic.ttf" : "Italic.ttf") : (info.Weight == 700 ? "Bold.ttf" : "Regular.ttf"), info.FileName);
        }
    }

    [Fact]
    public void EmbeddedBytesMatchTheManifestHashes()
    {
        Dictionary<string, string> hashes = ReadManifest();
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            string folder = info.Family.StartsWith("Liberation", StringComparison.Ordinal) ? "Liberation" : info.Family;
            string key = folder + "/" + info.FileName;
            Assert.Equal(hashes[key], Convert.ToHexStringLower(SHA256.HashData(info.ReadAllBytes())));
        }

        foreach (string family in DefaultFonts.Families)
        {
            string folder = family.StartsWith("Liberation", StringComparison.Ordinal) ? "Liberation" : family;
            using Stream s = DefaultFonts.OpenLicense(family);
            Assert.Equal(hashes[folder + "/OFL.txt"], Convert.ToHexStringLower(SHA256.HashData(ReadAll(s))));
        }
    }

    [Fact]
    public void LicenseTextsNameTheCopyrightHolders()
    {
        Assert.Contains("Red Hat", LicenseText("Liberation Mono"));
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", LicenseText("liberation serif"));
        Assert.Contains("Carlito Project Authors", LicenseText("Carlito"));
        Assert.Contains("Caladea Project Authors", LicenseText("Caladea"));
        Assert.Throws<ArgumentException>(() => DefaultFonts.OpenLicense("Arial"));
    }

    [Theory]
    [InlineData("serif", "Liberation Serif")]
    [InlineData("sans-serif", "Liberation Sans")]
    [InlineData("monospace", "Liberation Mono")]
    [InlineData("system-ui", "Liberation Sans")]
    [InlineData("ui-sans-serif", "Liberation Sans")]
    [InlineData("ui-serif", "Liberation Serif")]
    [InlineData("ui-monospace", "Liberation Mono")]
    [InlineData("cursive", "Liberation Serif")]
    [InlineData("fantasy", "Liberation Serif")]
    [InlineData("Arial", "Liberation Sans")]
    [InlineData("HELVETICA", "Liberation Sans")]
    [InlineData("Times New Roman", "Liberation Serif")]
    [InlineData("times", "Liberation Serif")]
    [InlineData("Courier New", "Liberation Mono")]
    [InlineData("COURIER", "Liberation Mono")]
    [InlineData("Calibri", "Carlito")]
    [InlineData("cAmBrIa", "Caladea")]
    [InlineData("Liberation Sans", "Liberation Sans")]
    [InlineData("carlito", "Carlito")]
    public void ResolvesGenericsAndAliases(string name, string expected)
    {
        Assert.True(DefaultFonts.TryResolveFamily(name, out string? family));
        Assert.Equal(expected, family);
        Assert.True(DefaultFonts.TryResolveFamily(name.AsSpan(), out family));
        Assert.Equal(expected, family);
        Assert.Equal(4, DefaultFonts.GetFaces(family).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Arial ")]
    [InlineData("\"Arial\"")]
    [InlineData("Comic Sans MS")]
    [InlineData("Ariaı")] // dotless i must not fold to ASCII
    [InlineData("ſerif")] // long s must not fold to ASCII
    [InlineData("ARIAL\u212A")] // Kelvin sign, not an ASCII letter
    public void DoesNotResolveOtherNames(string name)
    {
        Assert.False(DefaultFonts.TryResolveFamily(name, out string? family));
        Assert.Null(family);
        Assert.True(DefaultFonts.GetFaces(name).IsEmpty);
    }

    [Fact]
    public void NullNameDoesNotResolve() => Assert.False(DefaultFonts.TryResolveFamily((string?)null, out _));

    [Fact]
    public void LookupsAllocateNothing()
    {
        string name = "Times New Roman";
        Action resolve = () => DefaultFonts.TryResolveFamily(name, out _);
        Action faces = () => DefaultFonts.GetFaces("Carlito");
        Allocation.AssertZero(resolve, "TryResolveFamily");
        Allocation.AssertZero(faces, "GetFaces");
    }

    [Fact]
    public void OpeningAFaceAllocatesOnlyTheStreamObject()
    {
        FontFaceInfo face = DefaultFonts.Faces[0];
        Action open = () => face.Open().Dispose();
        long perOpen = Allocation.Measure(open, warmup: 5, iterations: 100) / 100;
        // One stream object (a few dozen bytes); the 400 KB of font data is not copied.
        Assert.InRange(perOpen, 1, 256);
        using Stream s = face.Open();
        Assert.Equal(face.Length, s.Length);
        Assert.True(s.CanSeek);
        Assert.False(s.CanWrite);
    }

    private static string LicenseText(string family)
    {
        using Stream s = DefaultFonts.OpenLicense(family);
        return Encoding.UTF8.GetString(ReadAll(s));
    }

    private static byte[] ReadAll(Stream s)
    {
        using MemoryStream ms = new();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static Dictionary<string, string> ReadManifest()
    {
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        using Stream s = DefaultFonts.OpenManifest();
        using StreamReader r = new(s, Encoding.UTF8);
        while (r.ReadLine() is { } line)
        {
            if (line.StartsWith("file\t", StringComparison.Ordinal))
            {
                string[] cols = line.Split('\t');
                hashes.Add(cols[1], cols[4]);
            }
        }

        Assert.Equal(23, hashes.Count);
        return hashes;
    }

    // Reads a Windows/Unicode (platform 3) name record from an OpenType 'name' table.
    private static string NameRecord(ReadOnlySpan<byte> name, int nameId)
    {
        int count = (name[2] << 8) | name[3];
        int stringOffset = (name[4] << 8) | name[5];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> rec = name.Slice(6 + (i * 12), 12);
            int platform = (rec[0] << 8) | rec[1];
            int id = (rec[6] << 8) | rec[7];
            if (platform == 3 && id == nameId)
            {
                int length = (rec[8] << 8) | rec[9];
                int offset = (rec[10] << 8) | rec[11];
                return Encoding.BigEndianUnicode.GetString(name.Slice(stringOffset + offset, length));
            }
        }

        throw new InvalidOperationException("name record " + nameId + " missing");
    }
}
