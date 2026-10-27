using System.IO;
using System.Numerics;
using System.Text;
using OnePdf;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>
/// Writes display lists as the pages of a PDF file (the low-level API of this package). Each call to <c>AddPage</c> draws a region of a
/// list onto one page; the file is written to the stream as pages are added, so memory does not grow with the page count, except
/// when a <see cref="PdfProfile"/> other than <see cref="PdfProfile.None"/> is asked for (see there).
/// </summary>
/// <remarks>
/// <para>What is written: vector fills, strokes, gradients, clips, transforms, opacity and blend groups, soft masks, images and
/// embedded font subsets with <c>/ToUnicode</c> maps; with <see cref="PdfOutputOptions.Tagged"/> also the logical structure from the
/// lists' tag items. What PDF cannot express (filters other than opacity, backdrop filters, inline SVG, blurred shadows and text
/// shadows) is drawn by the reference rasterizer into an image at <see cref="PdfOutputOptions.RasterDpi"/>, and a diagnostic names it.
/// </para>
/// <para>Not thread-safe. Dispose without <see cref="Finish"/> leaves an incomplete file.</para>
/// </remarks>
public sealed class DisplayListPdfWriter : IDisposable
{
    private readonly Stream _final;
    private readonly MemoryStream? _buffer;
    private readonly PdfDocumentWriter _writer;
    private readonly PdfShared _shared;
    private readonly PdfOutputOptions _options;
    private bool[] _mark = new bool[1024];
    private bool _finished;
    private bool _disposed;

    /// <summary>Starts a document.</summary>
    /// <param name="output">Where the PDF goes. Written as pages are added (profile <see cref="PdfProfile.None"/>) or at <see cref="Finish"/>.</param>
    /// <param name="options">Options; null for the defaults.</param>
    /// <param name="images">Supplies the pixels of <see cref="ItemKind.Image"/> items by handle; null if the lists hold no images.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentException">The options are inconsistent (a profile without tagging, a document id that is not 16 bytes).</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public DisplayListPdfWriter(Stream output, PdfOutputOptions? options = null, IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        _options = options ?? new PdfOutputOptions();
        _options.Validate();
        _final = output;
        Stream target = output;
        if (_options.Profile != PdfProfile.None)
        {
            _buffer = new MemoryStream();
            target = _buffer;
        }

        _writer = new PdfDocumentWriter(
            target,
            new PdfDocumentWriterOptions
            {
                CompressStreams = _options.Compress,
                DocumentId = _options.DocumentId ?? DeriveId(_options.Title),
                Language = _options.Language,
                DisplayDocumentTitle = _options.Title is not null,
                LeaveOpen = _buffer is not null || _options.LeaveOpen,
            });
        Diagnostics = new DiagnosticBag();
        _shared = new PdfShared(_writer, _options, images, Diagnostics);
        SetInfo("Title", _options.Title);
        SetInfo("Author", _options.Author);
        SetInfo("Subject", _options.Subject);
        SetInfo("Keywords", _options.Keywords);
        SetInfo("Creator", _options.Creator);
        SetInfo("Producer", "OneRender");
        SetInfo("CreationDate", _options.CreationDate);
    }

    /// <summary>What was approximated or left out: rasterized islands, dropped images and fonts, flattened projections, limits.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>The number of pages written so far.</summary>
    public int PageCount => _shared.Pages;

    /// <summary>Bytes written to the output so far (0 until <see cref="Finish"/> for a profile other than <see cref="PdfProfile.None"/>).</summary>
    public long BytesWritten => _buffer is null ? _writer.BytesWritten : _final is { CanSeek: true } ? _final.Position : 0;

    /// <summary>
    /// Names the in-document anchors that links may point to. A link whose target is <c>#name</c> becomes a link annotation only when
    /// <c>name</c> is declared here, so declare every name before the pages that link to it, and define each with
    /// <see cref="AddDestination"/> once its page exists and before <see cref="Finish"/>.
    /// </summary>
    /// <param name="names">The anchor names, without the <c>#</c>. At most 100,000 are kept.</param>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is null.</exception>
    public void DeclareDestinations(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        HashSet<string> set = _shared.Tags.Destinations ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (string n in names)
        {
            if (n.Length > 0 && set.Count < 100_000)
            {
                set.Add(n);
            }
        }
    }

    /// <summary>Defines the anchor <paramref name="name"/> as a place on an already written page.</summary>
    /// <param name="name">The anchor name, without the <c>#</c>.</param>
    /// <param name="pageIndex">The 0-based page, already added.</param>
    /// <param name="page">The setup that page was added with.</param>
    /// <param name="y">The distance of the anchor below the top of the page area, in CSS px.</param>
    /// <exception cref="ArgumentException">The name is empty or already defined.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page does not exist or <paramref name="y"/> is not finite.</exception>
    public void AddDestination(string name, int pageIndex, in PdfPageSetup page, double y)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (pageIndex < 0 || pageIndex >= _shared.Pages)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        const double K = 0.75;
        double top = (page.Height - page.MarginTop - Math.Max(0, y)) * K;
        try
        {
            _writer.AddNamedDestination(name, pageIndex, top);
        }
        catch (PdfException e)
        {
            throw new PdfRenderException("The PDF writer refused the destination: " + e.Message, e);
        }
    }

    private void SetInfo(string key, string? value)
    {
        if (value is not null)
        {
            _writer.SetInfo(key, value);
        }
    }

    private static byte[] DeriveId(string? title)
    {
        // The trailer /ID must be 16 bytes; with no clock and no randomness it is a hash of the title, so equal input gives equal bytes.
        byte[] id = new byte[16];
        ulong h1 = 14695981039346656037UL, h2 = 1099511628211UL * 31;
        foreach (char c in title ?? string.Empty)
        {
            h1 = (h1 ^ c) * 1099511628211UL;
            h2 = (h2 ^ c) * 14029467366897019727UL;
        }

        for (int i = 0; i < 8; i++)
        {
            id[i] = (byte)(h1 >> (8 * i));
            id[i + 8] = (byte)(h2 >> (8 * i));
        }

        return id;
    }

    /// <summary>Draws the top-left content-sized region of <paramref name="list"/> onto a new page.</summary>
    /// <param name="list">A finished list (no open scopes).</param>
    /// <param name="page">The page size and margins.</param>
    /// <param name="cancellationToken">Cancels the write; the output is then incomplete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page setup is invalid.</exception>
    /// <exception cref="DisplayListException">The list has open scopes.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or the writer cannot express the list.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public void AddPage(DisplayList list, in PdfPageSetup page, CancellationToken cancellationToken = default) =>
        AddPage(list, page, new RectF(0, 0, (float)page.ContentWidth, (float)page.ContentHeight), null, cancellationToken);

    /// <summary>Draws <paramref name="region"/> of <paramref name="list"/> onto a new page; the region's top-left lands at the top-left of the content area.</summary>
    /// <param name="list">A finished list (no open scopes).</param>
    /// <param name="page">The page size and margins.</param>
    /// <param name="region">The part of the list's space to show, in CSS px. Content outside it is clipped. Normally the size of the content area.</param>
    /// <param name="cancellationToken">Cancels the write; the output is then incomplete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page setup or the region is invalid.</exception>
    /// <exception cref="DisplayListException">The list has open scopes.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or the writer cannot express the list.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public void AddPage(DisplayList list, in PdfPageSetup page, in RectF region, CancellationToken cancellationToken = default) =>
        AddPage(list, page, region, null, cancellationToken);

    /// <summary>
    /// Draws <paramref name="region"/> of <paramref name="list"/> inside the page's margins and, below it, a page layer in page coordinates:
    /// the page background and page-margin boxes of paged media. The layer is drawn as artifacts, outside the structure tree.
    /// </summary>
    /// <param name="list">A finished list (no open scopes).</param>
    /// <param name="page">The page size and margins.</param>
    /// <param name="region">The part of the list's space to show, in CSS px.</param>
    /// <param name="layer">A finished list whose origin is the page's top-left corner, or null for none. It is not modified.</param>
    /// <param name="cancellationToken">Cancels the write; the output is then incomplete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page setup or the region is invalid.</exception>
    /// <exception cref="DisplayListException">A list has open scopes.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or the writer cannot express a list.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public void AddPage(DisplayList list, in PdfPageSetup page, in RectF region, DisplayList? layer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            throw new InvalidOperationException("The document is finished.");
        }

        page.Validate();
        if (region.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The region must have a positive size.");
        }

        list.Validate();
        layer?.Validate();
        if (Math.Max(list.RequiredMinor, layer?.RequiredMinor ?? 0) > DisplayList.FormatMinor)
        {
            throw new PdfRenderException("The display list needs a newer format than this writer reads (up to 1." + DisplayList.FormatMinor + ").");
        }

        if (_shared.Pages >= _options.MaxPages)
        {
            throw new PdfRenderException("The document would have more than " + _options.MaxPages + " pages.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Emit(list, page, region, layer, cancellationToken);
        }
        catch (PdfException e)
        {
            throw new PdfRenderException("The PDF writer refused the page: " + e.Message, e);
        }
        catch (InvalidOperationException e)
        {
            throw new PdfRenderException("The PDF writer refused the page: " + e.Message, e);
        }
    }

    private void Emit(DisplayList list, in PdfPageSetup setup, in RectF region, DisplayList? layer, CancellationToken ct)
    {
        const double K = 0.75; // points per CSS px
        _shared.Tags.Start();
        PdfStreamPageBuilder page = _writer.BeginPage(setup.Width * K, setup.Height * K);
        _shared.Pages++;
        if (layer is not null)
        {
            // The page layer is in page coordinates: its origin is the page's top-left corner, with no margins.
            EmitRegion(page, layer, new RectF(0, 0, (float)setup.Width, (float)setup.Height), 0, setup.Height * K, setup.Width, setup.Height, ct, true);
        }

        double tx = (setup.MarginLeft - region.X) * K, ty = (setup.Height - setup.MarginTop + region.Y) * K;
        EmitRegion(page, list, region, tx, ty, Math.Min(region.Width, setup.ContentWidth), Math.Min(region.Height, setup.ContentHeight), ct);
        _shared.Tags.FlushLinks(page);
        page.Commit();
    }

    // Draws the part of a list inside region with the region's top-left at (tx, ty) in page points (y up), clipped to clipW x clipH.
    private void EmitRegion(PdfStreamPageBuilder page, DisplayList list, in RectF region, double tx, double ty, double clipW, double clipH, CancellationToken ct, bool artifacts = false)
    {
        const double K = 0.75;
        int count = list.Count;
        if (_mark.Length < count)
        {
            _mark = new bool[Math.Max(count, _mark.Length * 2)];
        }
        else
        {
            Array.Clear(_mark, 0, count);
        }

        MarkVisible(list, region);
        page.Save();
        page.Transform(K, 0, 0, -K, tx, ty);
        page.Rectangle(region.X, region.Y, clipW, clipH);
        page.Clip();
        var toPoints = new Matrix3x2((float)K, 0, 0, (float)-K, (float)tx, (float)ty);
        var emitter = new PageEmitter(_shared, list, _mark, page, page, 0, toPoints, ct);
        if (artifacts)
        {
            emitter.DrawAsArtifacts();
        }

        emitter.EmitRange(0, count);
        emitter.Finish();
        page.Restore();
    }

    private void MarkVisible(DisplayList list, in RectF region)
    {
        int count = list.Count;
        RectF r = region;
        for (int i = 0; i < count; i++)
        {
            DisplayItem it = list[i];
            if (!PageEmitter.IsLeaf(it.Kind))
            {
                continue;
            }

            RectF b = it.RootBounds;
            if (b.IsEmpty || b.X >= r.Right || b.Right <= r.X || b.Y >= r.Bottom || b.Bottom <= r.Y)
            {
                continue;
            }

            _mark[i] = true;
            int p = it.ParentIndex;
            while (p >= 0 && !_mark[p])
            {
                DisplayItem push = list[p];
                _mark[p] = true;
                _mark[push.MatchIndex] = true;
                p = push.ParentIndex;
            }
        }
    }

    /// <summary>
    /// Ends the document: closes the structure tree, subsets and writes the fonts, and writes the page tree and trailer. With a
    /// <see cref="PdfProfile"/> the finished file is then checked and rewritten with the identification.
    /// </summary>
    /// <exception cref="PdfRenderException">The file cannot be finished, or the profile's rules are broken (the message lists them).</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public void Finish(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            return;
        }

        if (_shared.Pages == 0)
        {
            throw new PdfRenderException("A document needs at least one page.");
        }

        try
        {
            _shared.Tags.Finish(null);
            _writer.Finish(cancellationToken);
            _finished = true;
            if (_buffer is not null)
            {
                Identify(cancellationToken);
            }
        }
        catch (PdfException e)
        {
            throw new PdfRenderException("The PDF could not be finished: " + e.Message, e);
        }
        catch (InvalidOperationException e)
        {
            throw new PdfRenderException("The PDF could not be finished: " + e.Message, e);
        }
        finally
        {
            Dispose();
        }
    }

    private void Identify(CancellationToken ct)
    {
        _buffer!.Position = 0;
        using PdfDocument doc = PdfDocument.Open(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), new PdfOpenOptions { AllowEditing = true });
        bool ua = _options.Profile is PdfProfile.PdfUA1 or PdfProfile.PdfA2aAndUA1;
        bool a = _options.Profile is PdfProfile.PdfA2a or PdfProfile.PdfA2aAndUA1;
        var save = new PdfSaveOptions
        {
            CompressStreams = _options.Compress,
            DocumentId = _options.DocumentId ?? DeriveId(_options.Title),
            PdfUA = ua ? new PdfUAOptions { Language = _options.Language } : null,
            PdfA = a ? new PdfAOptions { Part = PdfAPart.Part2, Conformance = PdfAConformance.A, Language = _options.Language } : null,
        };
        try
        {
            doc.Save(_final, save, ct);
        }
        catch (PdfUAException e)
        {
            throw new PdfRenderException("The document does not meet PDF/UA-1: " + Describe(e.Violations), e);
        }
        catch (PdfAException e)
        {
            throw new PdfRenderException("The document does not meet PDF/A-2a: " + e.Message, e);
        }
    }

    private static string Describe(IReadOnlyList<PdfUAViolation> violations)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < violations.Count && i < 8; i++)
        {
            sb.Append(i > 0 ? "; " : string.Empty).Append(violations[i].Clause).Append(": ").Append(violations[i].Message);
        }

        if (violations.Count > 8)
        {
            sb.Append("; and ").Append(violations.Count - 8).Append(" more");
        }

        return sb.ToString();
    }

    /// <summary>Releases the writer; the output stream is closed unless <see cref="PdfOutputOptions.LeaveOpen"/> is set. Without <see cref="Finish"/> the file is incomplete.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
        _buffer?.Dispose();
        if (!_options.LeaveOpen)
        {
            _final.Dispose();
        }
    }
}
