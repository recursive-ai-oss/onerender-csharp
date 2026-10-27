namespace OneRender.Pdf;

/// <summary>Which accessibility or archival identification the output carries.</summary>
/// <remarks>
/// <see cref="None"/> streams: pages are written as they are added and memory stays flat. The identifications need the XMP packet
/// and checks that only a finished, whole file can give, so the other values hold the file in memory while it is finished and
/// then write it to the output.
/// </remarks>
public enum PdfProfile
{
    /// <summary>Tagged (unless <see cref="PdfOutputOptions.Tagged"/> is off) but not identified as PDF/UA or PDF/A. Streams.</summary>
    None = 0,

    /// <summary>PDF/UA-1 (ISO 14289-1). Requires tagging; the write fails with <see cref="PdfRenderException"/> if the file breaks a rule OnePdf checks.</summary>
    PdfUA1,

    /// <summary>PDF/A-2a (ISO 19005-2, level A, tagged).</summary>
    PdfA2a,

    /// <summary>PDF/A-2a and PDF/UA-1 in one file.</summary>
    PdfA2aAndUA1,
}

/// <summary>A page size and its margins, in CSS px (1/96 inch).</summary>
/// <remarks>The content area is the size minus the margins; the document is laid out at the content width and cut into content-height pieces.</remarks>
public readonly record struct PdfPageSetup
{
    /// <summary>Creates a page setup.</summary>
    /// <param name="width">Page width in CSS px, 1 to 14,400.</param>
    /// <param name="height">Page height in CSS px, 1 to 14,400.</param>
    /// <param name="marginTop">Top margin in px, 0 or more.</param>
    /// <param name="marginRight">Right margin in px.</param>
    /// <param name="marginBottom">Bottom margin in px.</param>
    /// <param name="marginLeft">Left margin in px.</param>
    public PdfPageSetup(double width, double height, double marginTop = 0, double marginRight = 0, double marginBottom = 0, double marginLeft = 0)
    {
        Width = width;
        Height = height;
        MarginTop = marginTop;
        MarginRight = marginRight;
        MarginBottom = marginBottom;
        MarginLeft = marginLeft;
    }

    /// <summary>ISO A4, 210 × 297 mm, with 0.5 inch margins.</summary>
    public static PdfPageSetup A4 { get; } = new(793.7007874, 1122.5196850, 48, 48, 48, 48);

    /// <summary>US Letter, 8.5 × 11 inch, with 0.5 inch margins.</summary>
    public static PdfPageSetup Letter { get; } = new(816, 1056, 48, 48, 48, 48);

    /// <summary>Page width in CSS px.</summary>
    public double Width { get; init; }

    /// <summary>Page height in CSS px.</summary>
    public double Height { get; init; }

    /// <summary>Top margin in CSS px.</summary>
    public double MarginTop { get; init; }

    /// <summary>Right margin in CSS px.</summary>
    public double MarginRight { get; init; }

    /// <summary>Bottom margin in CSS px.</summary>
    public double MarginBottom { get; init; }

    /// <summary>Left margin in CSS px.</summary>
    public double MarginLeft { get; init; }

    /// <summary>Width of the content area, <c>Width - MarginLeft - MarginRight</c>.</summary>
    public double ContentWidth => Width - MarginLeft - MarginRight;

    /// <summary>Height of the content area, <c>Height - MarginTop - MarginBottom</c>.</summary>
    public double ContentHeight => Height - MarginTop - MarginBottom;

    /// <summary>The largest page edge accepted, in CSS px (150 inch, PDF's own practical limit of 14,400 user units).</summary>
    public const double MaxEdge = 14400;

    /// <summary>Checks the numbers and throws when they cannot make a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite or outside its range, a margin is negative, or the margins leave no content area.</exception>
    public void Validate()
    {
        if (!(Width >= 1 && Width <= MaxEdge))
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "The page width must be 1 to 14,400 px.");
        }

        if (!(Height >= 1 && Height <= MaxEdge))
        {
            throw new ArgumentOutOfRangeException(nameof(Height), "The page height must be 1 to 14,400 px.");
        }

        if (!(MarginTop >= 0 && MarginRight >= 0 && MarginBottom >= 0 && MarginLeft >= 0) || !(ContentWidth >= 1) || !(ContentHeight >= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(MarginTop), "Margins must be 0 or more and leave at least 1 px of content area.");
        }
    }
}

/// <summary>How the PDF file is written, independent of what it contains.</summary>
public sealed class PdfOutputOptions
{
    /// <summary>The document title (<c>/Info /Title</c>, XMP <c>dc:title</c>). Default none; PDF/UA requires one.</summary>
    public string? Title { get; init; }

    /// <summary>The author, or null.</summary>
    public string? Author { get; init; }

    /// <summary>The subject, or null.</summary>
    public string? Subject { get; init; }

    /// <summary>Keywords, or null.</summary>
    public string? Keywords { get; init; }

    /// <summary>The producing application written to <c>/Info /Creator</c>. Default <c>OneRender</c>.</summary>
    public string? Creator { get; init; } = "OneRender";

    /// <summary>
    /// The creation date, as a PDF date string such as <c>D:20261027120000Z</c>. The writer never reads the clock, so the default
    /// is no date and the same input gives the same bytes.
    /// </summary>
    public string? CreationDate { get; init; }

    /// <summary>The document language (a BCP 47 tag such as <c>en-US</c>), written to the catalog. Default none; PDF/UA requires one, and <see cref="HtmlPdf"/> takes it from the <c>lang</c> attribute of the root element when this is null.</summary>
    public string? Language { get; init; }

    /// <summary>Write the logical structure tree and mark every painted object as content or artifact (ISO 32000-2 14.7, 14.8). Default true.</summary>
    public bool Tagged { get; init; } = true;

    /// <summary>The identification to write. Default <see cref="PdfProfile.None"/>, which streams.</summary>
    public PdfProfile Profile { get; init; }

    /// <summary>Flate-compress content and other streams. Default true.</summary>
    public bool Compress { get; init; } = true;

    /// <summary>Sixteen bytes for the trailer <c>/ID</c>. Default null: a fixed value derived from the title, so equal input gives equal bytes.</summary>
    public byte[]? DocumentId { get; init; }

    /// <summary>
    /// Resolution, in pixels per inch, of the images that stand in for what PDF cannot express (blur, other filters, blurred shadows,
    /// inline SVG). 24 to 600; default 192 (twice the 96 px CSS inch). Each such image is at most 16 Mpixel; larger areas are drawn
    /// at a lower resolution and a diagnostic says so.
    /// </summary>
    public double RasterDpi { get; init; } = 192;

    /// <summary>The most pages one document may have. Default 100,000; the write fails with <see cref="PdfRenderException"/> beyond it.</summary>
    public int MaxPages { get; init; } = 100_000;

    /// <summary>Leave the output stream open after <c>Finish</c> or disposal. Default false.</summary>
    public bool LeaveOpen { get; init; }

    internal PdfOutputOptions With(string? title, string? language) => new()
    {
        Title = title,
        Author = Author,
        Subject = Subject,
        Keywords = Keywords,
        Creator = Creator,
        CreationDate = CreationDate,
        Language = language,
        Tagged = Tagged,
        Profile = Profile,
        Compress = Compress,
        DocumentId = DocumentId,
        RasterDpi = RasterDpi,
        MaxPages = MaxPages,
        LeaveOpen = LeaveOpen,
    };

    internal PdfOutputOptions WithLeaveOpen() => new()
    {
        Title = Title,
        Author = Author,
        Subject = Subject,
        Keywords = Keywords,
        Creator = Creator,
        CreationDate = CreationDate,
        Language = Language,
        Tagged = Tagged,
        Profile = Profile,
        Compress = Compress,
        DocumentId = DocumentId,
        RasterDpi = RasterDpi,
        MaxPages = MaxPages,
        LeaveOpen = true,
    };

    internal void Validate()
    {
        if (!(RasterDpi >= 24 && RasterDpi <= 600))
        {
            throw new ArgumentOutOfRangeException(nameof(RasterDpi), "The raster resolution must be 24 to 600 dpi.");
        }

        if (MaxPages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPages));
        }

        if (DocumentId is { Length: not 16 })
        {
            throw new ArgumentException("The document id must be 16 bytes.", nameof(DocumentId));
        }

        if (Profile != PdfProfile.None && !Tagged)
        {
            throw new ArgumentException("PDF/UA and PDF/A level A need a tagged document.", nameof(Profile));
        }
    }
}

/// <summary>Thrown when a document cannot be written: a limit was reached or a profile's rule is broken.</summary>
public sealed class PdfRenderException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PdfRenderException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public PdfRenderException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public PdfRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
