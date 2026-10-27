using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using OneFont;

namespace OneRender.Text;

/// <summary>Why a font file could not be loaded.</summary>
public enum FontLoadStatus : byte
{
    /// <summary>The font loaded.</summary>
    Ok = 0,

    /// <summary>No data was supplied.</summary>
    Empty,

    /// <summary>The data is larger than <see cref="FontLimits.MaxInputBytes"/>.</summary>
    InputTooLarge,

    /// <summary>The signature is not one of TrueType, OpenType, WOFF or WOFF2, or the file is a collection.</summary>
    UnsupportedFormat,

    /// <summary>A WOFF or WOFF2 container is damaged: truncated, inconsistent offsets or lengths, or an invalid stream.</summary>
    InvalidContainer,

    /// <summary>The decoded font would exceed <see cref="FontLimits.MaxDecodedBytes"/> or a table count limit.</summary>
    LimitExceeded,

    /// <summary>The font tables are damaged or use a feature the font reader does not support.</summary>
    InvalidFont,
}

/// <summary>Bounds applied to font data before memory is allocated for it. Font files come from documents and are untrusted.</summary>
public sealed class FontLimits
{
    /// <summary>The limits used when none are given: 32 MiB of input, 64 MiB of decoded font, 256 tables.</summary>
    public static FontLimits Default { get; } = new();

    /// <summary>The largest file accepted, in bytes. Default 32 MiB.</summary>
    public int MaxInputBytes { get; init; } = 32 << 20;

    /// <summary>The largest decoded sfnt (the sum of all table sizes after decompression and reconstruction), in bytes. Default 64 MiB.</summary>
    public int MaxDecodedBytes { get; init; } = 64 << 20;

    /// <summary>The most tables a font may declare. Default 256, the font reader's own limit.</summary>
    public int MaxTables { get; init; } = 256;
}

/// <summary>The outcome of loading font data: a face, or a status saying why not. Never an exception.</summary>
public readonly struct FontLoadResult
{
    internal FontLoadResult(FontFace? face, FontLoadStatus status, string? detail)
    {
        Face = face;
        Status = status;
        Detail = detail;
    }

    /// <summary>The loaded face, or null when <see cref="Status"/> is not <see cref="FontLoadStatus.Ok"/>.</summary>
    public FontFace? Face { get; }

    /// <summary>Whether the font loaded.</summary>
    public FontLoadStatus Status { get; }

    /// <summary>A short, fixed description of what went wrong; null when the font loaded.</summary>
    public string? Detail { get; }
}

/// <summary>
/// Loads font data of any format a web page can supply: TrueType and OpenType (with TrueType or CFF outlines), WOFF 1.0 and
/// WOFF 2.0. The format is taken from the file signature, never from a name or a <c>format()</c> hint. Damaged or oversized
/// data produces a <see cref="FontLoadResult"/> with a status; nothing here throws for bad font data.
/// </summary>
public static class FontLoader
{
    private const uint SigWoff = 0x774F4646; // wOFF
    private const uint SigWoff2 = 0x774F4632; // wOF2
    private const uint SigTtcf = 0x74746366; // ttcf

    /// <summary>Loads a font from bytes.</summary>
    /// <param name="data">The file contents; not modified and not kept (a plain sfnt is passed to the font reader, which may keep a reference).</param>
    /// <param name="limits">Bounds to apply; <see cref="FontLimits.Default"/> when null.</param>
    /// <returns>The face, or the reason it could not be loaded.</returns>
    public static FontLoadResult Load(byte[]? data, FontLimits? limits = null)
    {
        limits ??= FontLimits.Default;
        if (data is null || data.Length < 4)
        {
            return Fail(FontLoadStatus.Empty, "The font data is empty.");
        }

        if (data.Length > limits.MaxInputBytes)
        {
            return Fail(FontLoadStatus.InputTooLarge, "The font data is larger than the input limit.");
        }

        uint sig = BinaryPrimitives.ReadUInt32BigEndian(data);
        byte[] sfnt = data;
        if (sig == SigWoff)
        {
            FontLoadStatus s = DecodeWoff(data, limits, out sfnt, out string? why);
            if (s != FontLoadStatus.Ok)
            {
                return Fail(s, why);
            }
        }
        else if (sig == SigWoff2)
        {
            FontLoadStatus s = Woff2Decoder.Decode(data, limits, out sfnt, out string? why);
            if (s != FontLoadStatus.Ok)
            {
                return Fail(s, why);
            }
        }
        else if (sig == SigTtcf)
        {
            return Fail(FontLoadStatus.UnsupportedFormat, "Font collections are not supported as web fonts.");
        }
        else if (sig is not (0x00010000 or 0x74727565 or 0x4F54544F))
        {
            return Fail(FontLoadStatus.UnsupportedFormat, "The data is not a TrueType, OpenType, WOFF or WOFF2 font.");
        }

        try
        {
            return new FontLoadResult(FontFace.Load(sfnt), FontLoadStatus.Ok, null);
        }
        catch (FontFormatException)
        {
            return Fail(FontLoadStatus.InvalidFont, "The font tables are damaged or unsupported.");
        }
    }

    private static FontLoadResult Fail(FontLoadStatus status, string? detail) => new(null, status, detail);

    // WOFF 1.0 §5-§6: a 44-byte header, a table directory of 20-byte entries, and per-table zlib streams (a table is stored
    // raw when its compressed length equals its original length).
    private static FontLoadStatus DecodeWoff(byte[] data, FontLimits limits, out byte[] sfnt, out string? why)
    {
        sfnt = [];
        why = null;
        if (data.Length < 44)
        {
            why = "The WOFF header is truncated.";
            return FontLoadStatus.InvalidContainer;
        }

        uint flavor = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12));
        if (length != (uint)data.Length || numTables == 0)
        {
            why = "The WOFF header is inconsistent.";
            return FontLoadStatus.InvalidContainer;
        }

        if (numTables > limits.MaxTables)
        {
            why = "The font declares too many tables.";
            return FontLoadStatus.LimitExceeded;
        }

        if (44 + (long)numTables * 20 > data.Length)
        {
            why = "The WOFF table directory is truncated.";
            return FontLoadStatus.InvalidContainer;
        }

        // Check every length before allocating anything.
        long total = 12 + 16L * numTables;
        for (int i = 0; i < numTables; i++)
        {
            ReadOnlySpan<byte> e = data.AsSpan(44 + (i * 20), 20);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(e[4..]);
            uint comp = BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
            uint orig = BinaryPrimitives.ReadUInt32BigEndian(e[12..]);
            if ((long)offset + comp > data.Length || comp > orig)
            {
                why = "A WOFF table lies outside the file or is longer compressed than original.";
                return FontLoadStatus.InvalidContainer;
            }

            total += (orig + 3L) & ~3L;
            if (total > limits.MaxDecodedBytes)
            {
                why = "The decoded font would be larger than the limit.";
                return FontLoadStatus.LimitExceeded;
            }
        }

        byte[] output = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(output, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)numTables);
        int entrySelector = 0;
        while ((2 << entrySelector) <= numTables)
        {
            entrySelector++;
        }

        int searchRange = (1 << entrySelector) * 16;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), (ushort)((numTables * 16) - searchRange));

        int pos = 12 + (16 * numTables);
        for (int i = 0; i < numTables; i++)
        {
            ReadOnlySpan<byte> e = data.AsSpan(44 + (i * 20), 20);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(e[4..]);
            int comp = (int)BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
            int orig = (int)BinaryPrimitives.ReadUInt32BigEndian(e[12..]);
            Span<byte> dir = output.AsSpan(12 + (i * 16), 16);
            e[..4].CopyTo(dir);
            e[16..20].CopyTo(dir[4..]); // origChecksum
            BinaryPrimitives.WriteUInt32BigEndian(dir[8..], (uint)pos);
            BinaryPrimitives.WriteUInt32BigEndian(dir[12..], (uint)orig);
            Span<byte> dest = output.AsSpan(pos, orig);
            ReadOnlySpan<byte> src = data.AsSpan((int)offset, comp);
            if (comp == orig)
            {
                src.CopyTo(dest);
            }
            else if (!InflateExactly(src, dest))
            {
                why = "A WOFF table does not decompress to its declared length.";
                return FontLoadStatus.InvalidContainer;
            }

            pos += (orig + 3) & ~3;
        }

        sfnt = output;
        return FontLoadStatus.Ok;
    }

    private static bool InflateExactly(ReadOnlySpan<byte> source, Span<byte> dest)
    {
        try
        {
            using var input = new MemoryStream(source.ToArray(), writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            int got = 0;
            while (got < dest.Length)
            {
                int n = z.Read(dest[got..]);
                if (n == 0)
                {
                    return false;
                }

                got += n;
            }

            // Anything left over means the stream is longer than declared.
            Span<byte> probe = stackalloc byte[1];
            return z.Read(probe) == 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // Corrupt zlib data surfaces as InvalidDataException or, for some corruptions, as the runtime's internal IOException subclass.
            return false;
        }
    }

    internal static uint Checksum(ReadOnlySpan<byte> table)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 4 <= table.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(table[i..]);
        }

        if (i < table.Length)
        {
            Span<byte> last = stackalloc byte[4];
            table[i..].CopyTo(last);
            sum += BinaryPrimitives.ReadUInt32BigEndian(last);
        }

        return sum;
    }
}
