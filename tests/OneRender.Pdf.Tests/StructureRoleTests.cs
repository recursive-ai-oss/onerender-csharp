using System.Text;
using System.Text.RegularExpressions;
using OneFont;
using OnePdf;
using OneRender.Fonts;
using OneRender.Painting;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

/// <summary>
/// The structure types of ISO 32000-1 14.8.4 that a document producer needs (Note, Reference, TOC, TOCI, Formula) and the typed
/// artifacts of 14.8.2.2, from a display list built by hand through the PDF writer.
/// </summary>
public class StructureRoleTests
{
    private static FontFace? s_sans;

    private static FontFace Sans
    {
        get
        {
            if (s_sans is null)
            {
                foreach (FontFaceInfo f in DefaultFonts.GetFaces("Liberation Sans"))
                {
                    if (f.Weight == 400 && f.Slant == FontSlant.Normal)
                    {
                        s_sans = FontFace.Load(f.ReadAllBytes());
                        break;
                    }
                }
            }

            return s_sans ?? throw new InvalidOperationException("Liberation Sans Regular is missing.");
        }
    }

    private static void Text(DisplayList l, uint id, float y)
    {
        int glyph = Sans.GetGlyphIndex('A');
        l.DrawGlyphRun(ItemId.Of(id), Sans, 12f, 10f, y, new PaintColor(0, 0, 0, 1), [glyph], [0f], default, [0], "A");
    }

    private static byte[] Write(DisplayList list, PdfProfile profile = PdfProfile.None)
    {
        list.Validate();
        using var stream = new MemoryStream();
        using (var writer = new DisplayListPdfWriter(stream, new PdfOutputOptions { Profile = profile, Compress = false, Title = "Test", Language = "en" }))
        {
            writer.AddPage(list, PdfPageSetup.A4);
            writer.Finish();
        }

        return stream.ToArray();
    }

    private static string Content(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        return string.Concat(doc.Pages[0].ContentStreams.Select(c => Encoding.Latin1.GetString(c.GetDecodedBytes().Span)));
    }

    private static List<PdfDictionary> StructureElements(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var found = new List<PdfDictionary>();
        var stack = new Stack<PdfObject>();
        if (doc.StructureTreeRoot?.Get("K") is { } root)
        {
            stack.Push(root);
        }

        while (stack.Count > 0)
        {
            PdfObject o = stack.Pop();
            if (o is PdfArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    if (arr[i] is { } item)
                    {
                        stack.Push(item);
                    }
                }
            }
            else if (o is PdfDictionary d)
            {
                if (d.GetName("S") is not null)
                {
                    found.Add(d);
                }

                if (d.Get("K") is { } kids)
                {
                    stack.Push(kids);
                }
            }
        }

        return found;
    }

    [Theory]
    [InlineData(TagRole.Note, "Note")]
    [InlineData(TagRole.Reference, "Reference")]
    [InlineData(TagRole.TOC, "TOC")]
    [InlineData(TagRole.TOCI, "TOCI")]
    [InlineData(TagRole.Formula, "Formula")]
    public void NewRolesRoundTripAndBecomeTheirStructureType(TagRole role, string type)
    {
        var list = new DisplayList();
        list.BeginTag(ItemId.Of(1), role, role == TagRole.Formula ? "x squared" : null);
        Text(list, 2, 40);
        list.EndTag();
        Assert.Equal(role, list[0].Role);

        byte[] pdf = Write(list);
        Assert.Contains(StructureElements(pdf), d => d.GetName("S") == type);
    }

    [Fact]
    public void FormulaKeepsItsAlternativeText()
    {
        var list = new DisplayList();
        list.BeginTag(ItemId.Of(1), TagRole.Formula, "E equals m c squared");
        Text(list, 2, 40);
        list.EndTag();
        Assert.Equal("E equals m c squared", list[0].AltText);
        byte[] pdf = Write(list);
        PdfDictionary formula = Assert.Single(StructureElements(pdf), d => d.GetName("S") == "Formula");
        Assert.Contains("E equals m c squared", formula.Get("Alt")?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RolesPastTheLastMemberAreRefused()
    {
        var list = new DisplayList();
        Assert.Throws<ArgumentOutOfRangeException>(() => list.BeginTag(ItemId.Of(1), (TagRole)35));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.BeginTag(ItemId.Of(1), (ArtifactType)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.BeginTag(ItemId.Of(1), ArtifactType.Pagination, (ArtifactSubtype)4));
    }

    [Theory]
    [InlineData(ArtifactType.Pagination, ArtifactSubtype.Header, "/Artifact <</Type /Pagination/Subtype /Header>> BDC")]
    [InlineData(ArtifactType.Pagination, ArtifactSubtype.Footer, "/Artifact <</Type /Pagination/Subtype /Footer>> BDC")]
    [InlineData(ArtifactType.Pagination, ArtifactSubtype.Watermark, "/Artifact <</Type /Pagination/Subtype /Watermark>> BDC")]
    [InlineData(ArtifactType.Background, ArtifactSubtype.None, "/Artifact <</Type /Background>> BDC")]
    [InlineData(ArtifactType.Layout, ArtifactSubtype.None, "/Artifact <</Type /Layout>> BDC")]
    public void ATypedArtifactWritesItsPropertyList(ArtifactType type, ArtifactSubtype subtype, string expected)
    {
        var list = new DisplayList();
        list.BeginTag(ItemId.Of(1), type, subtype);
        Text(list, 2, 800);
        list.EndTag();
        DisplayItem it = list[0];
        Assert.Equal(TagRole.Artifact, it.Role);
        Assert.Equal(type, it.ArtifactType);
        Assert.Equal(subtype, it.ArtifactSubtype);
        Assert.Equal(1, it.ColumnSpan);

        string content = Content(Write(list));
        Assert.Contains(expected, content, StringComparison.Ordinal);
        Assert.DoesNotContain(StructureElements(Write(list)), d => d.GetName("S") == "Span");
    }

    [Fact]
    public void ATypedArtifactInsideTaggedContentLeavesTheStructureAlone()
    {
        var list = new DisplayList();
        list.BeginTag(ItemId.Of(1), TagRole.Paragraph);
        Text(list, 2, 40);
        list.EndTag();
        list.BeginTag(ItemId.Of(3), ArtifactType.Pagination, ArtifactSubtype.Footer);
        Text(list, 4, 800);
        list.EndTag();
        list.BeginTag(ItemId.Of(5), TagRole.Artifact);
        Text(list, 6, 820);
        list.EndTag();

        byte[] pdf = Write(list, PdfProfile.PdfUA1);
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Empty(doc.CheckPdfUA());
        string content = Content(pdf);
        Assert.Contains("/Artifact <</Type /Pagination/Subtype /Footer>> BDC", content, StringComparison.Ordinal);
        // The untyped scope after it stays a plain artifact: the kind does not leak past its EndTag.
        Assert.Matches(new Regex(@"EMC\s+/Artifact BMC"), content);
        Assert.Single(Regex.Matches(content, @"/Artifact <<"));
    }

    [Fact]
    public void WithoutAKindTheOutputIsTheSameAsAPlainArtifact()
    {
        DisplayList Build(bool viaKind)
        {
            var l = new DisplayList();
            if (viaKind)
            {
                l.BeginTag(ItemId.Of(1), ArtifactType.None);
            }
            else
            {
                l.BeginTag(ItemId.Of(1), TagRole.Artifact);
            }

            Text(l, 2, 40);
            l.EndTag();
            return l;
        }

        DisplayList a = Build(true), b = Build(false);
        Assert.Equal(b[0].ContentHash, a[0].ContentHash);
        Assert.Equal(Write(b), Write(a));
        Assert.Contains("/Artifact BMC", Content(Write(a)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheKindIsPartOfTheContentHashSoDamageSeesAChange()
    {
        ulong Hash(ArtifactType t, ArtifactSubtype s)
        {
            var l = new DisplayList();
            l.BeginTag(ItemId.Of(1), t, s);
            l.EndTag();
            return l[0].ContentHash;
        }

        ulong none = Hash(ArtifactType.None, ArtifactSubtype.None);
        ulong header = Hash(ArtifactType.Pagination, ArtifactSubtype.Header);
        ulong footer = Hash(ArtifactType.Pagination, ArtifactSubtype.Footer);
        Assert.NotEqual(none, header);
        Assert.NotEqual(header, footer);
    }
}
