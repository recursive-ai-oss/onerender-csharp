using System.Buffers.Binary;
using System.IO.Compression;

namespace OneRender.Text;

// WOFF 2.0 (W3C Recommendation) §4 table directory, §5 table transforms, §6 decoding order.
// Everything is length-checked before it is allocated or indexed: the file is untrusted.
internal static class Woff2Decoder
{
    private static readonly uint[] s_knownTags = BuildKnownTags();

    private const uint TagGlyf = 0x676C7966;
    private const uint TagLoca = 0x6C6F6361;
    private const uint TagHmtx = 0x686D7478;
    private const uint TagHead = 0x68656164;
    private const uint TagHhea = 0x68686561;

    private static uint[] BuildKnownTags()
    {
        string[] names =
        [
            "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
            "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
            "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
            "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
        ];
        var tags = new uint[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string n = names[i];
            tags[i] = ((uint)n[0] << 24) | ((uint)n[1] << 16) | ((uint)n[2] << 8) | n[3];
        }

        return tags;
    }

    private struct Entry
    {
        public uint Tag;
        public int OrigLength;
        public int StoredLength;
        public bool Transformed;
        public int BlockOffset;
        public byte[]? Output; // the final table bytes
    }

    public static FontLoadStatus Decode(byte[] data, FontLimits limits, out byte[] sfnt, out string? why)
    {
        sfnt = [];
        why = null;
        if (data.Length < 48)
        {
            why = "The WOFF2 header is truncated.";
            return FontLoadStatus.InvalidContainer;
        }

        uint flavor = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12));
        uint compressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20));
        if (length != (uint)data.Length || numTables == 0 || flavor == 0x74746366)
        {
            why = "The WOFF2 header is inconsistent or declares a collection.";
            return FontLoadStatus.InvalidContainer;
        }

        if (numTables > limits.MaxTables)
        {
            why = "The font declares too many tables.";
            return FontLoadStatus.LimitExceeded;
        }

        var entries = new Entry[numTables];
        int pos = 48;
        long storedTotal = 0;
        long origTotal = 12 + (16L * numTables);
        for (int i = 0; i < numTables; i++)
        {
            if (pos >= data.Length)
            {
                why = "The WOFF2 table directory is truncated.";
                return FontLoadStatus.InvalidContainer;
            }

            int flags = data[pos++];
            int tagIndex = flags & 0x3F;
            int version = flags >> 6;
            uint tag;
            if (tagIndex == 63)
            {
                if (pos + 4 > data.Length)
                {
                    why = "The WOFF2 table directory is truncated.";
                    return FontLoadStatus.InvalidContainer;
                }

                tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
                pos += 4;
            }
            else
            {
                tag = s_knownTags[tagIndex];
            }

            if (!ReadBase128(data, ref pos, out uint orig))
            {
                why = "A WOFF2 table length is malformed.";
                return FontLoadStatus.InvalidContainer;
            }

            bool transformed;
            if (tag is TagGlyf or TagLoca)
            {
                if (version is not (0 or 3))
                {
                    why = "A glyf or loca table uses an unknown transform.";
                    return FontLoadStatus.InvalidContainer;
                }

                transformed = version == 0;
            }
            else if (tag == TagHmtx)
            {
                if (version > 1)
                {
                    why = "The hmtx table uses an unknown transform.";
                    return FontLoadStatus.InvalidContainer;
                }

                transformed = version == 1;
            }
            else
            {
                if (version != 0)
                {
                    why = "A table uses an unknown transform.";
                    return FontLoadStatus.InvalidContainer;
                }

                transformed = false;
            }

            uint stored = orig;
            if (transformed && !ReadBase128(data, ref pos, out stored))
            {
                why = "A WOFF2 table length is malformed.";
                return FontLoadStatus.InvalidContainer;
            }

            if (orig > (uint)limits.MaxDecodedBytes || stored > (uint)limits.MaxDecodedBytes)
            {
                why = "A table is larger than the decoded-size limit.";
                return FontLoadStatus.LimitExceeded;
            }

            storedTotal += stored;
            origTotal += (orig + 3L) & ~3L;
            if (storedTotal > limits.MaxDecodedBytes || origTotal > limits.MaxDecodedBytes)
            {
                why = "The decoded font would be larger than the limit.";
                return FontLoadStatus.LimitExceeded;
            }

            entries[i] = new Entry { Tag = tag, OrigLength = (int)orig, StoredLength = (int)stored, Transformed = transformed, BlockOffset = (int)(storedTotal - stored) };
        }

        if (compressedSize == 0 || (long)pos + compressedSize > data.Length)
        {
            why = "The WOFF2 compressed data lies outside the file.";
            return FontLoadStatus.InvalidContainer;
        }

        // §6: the table data block is one Brotli stream whose size must equal the sum of the stored lengths.
        byte[] block = new byte[storedTotal];
        int written;
        try
        {
            if (!BrotliDecoder.TryDecompress(data.AsSpan(pos, (int)compressedSize), block, out written))
            {
                written = -1;
            }
        }
        catch (PlatformNotSupportedException)
        {
            // Runtimes without a native Brotli (the browser WebAssembly runtime) cannot decode WOFF2; say so instead of failing.
            why = "This runtime has no Brotli decoder, so WOFF2 fonts cannot be decoded here.";
            return FontLoadStatus.UnsupportedFormat;
        }

        if (written != block.Length)
        {
            why = "The WOFF2 data does not decompress to the declared size.";
            return FontLoadStatus.InvalidContainer;
        }

        // The specification asks encoders to put loca right after glyf, but not every encoder does, so find it by tag.
        int glyfIndex = -1;
        int locaIndex = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Tag == TagGlyf)
            {
                glyfIndex = i;
            }
            else if (entries[i].Tag == TagLoca)
            {
                locaIndex = i;
            }
        }

        if (glyfIndex >= 0 && locaIndex >= 0 && entries[glyfIndex].Transformed != entries[locaIndex].Transformed)
        {
            why = "The glyf and loca tables must both be transformed or both not.";
            return FontLoadStatus.InvalidContainer;
        }

        if (glyfIndex >= 0 && entries[glyfIndex].Transformed && (locaIndex < 0 || entries[locaIndex].StoredLength != 0))
        {
            why = "A transformed glyf table needs a transformed, empty loca placeholder.";
            return FontLoadStatus.InvalidContainer;
        }

        int[]? xMins = null;
        int numGlyphs = -1;
        int indexFormat = 0;
        bool glyfDone = false;
        for (int i = 0; i < entries.Length; i++)
        {
            ref Entry e = ref entries[i];
            ReadOnlySpan<byte> table = block.AsSpan(e.BlockOffset, e.StoredLength);
            if (e.Tag == TagGlyf && e.Transformed)
            {
                FontLoadStatus s = ReconstructGlyf(table, limits, out byte[] glyf, out byte[] loca, out xMins, out numGlyphs, out indexFormat, out why);
                if (s != FontLoadStatus.Ok)
                {
                    return s;
                }

                e.Output = glyf;
                entries[locaIndex].Output = loca;
                glyfDone = true;
            }
            else if (e.Tag == TagLoca && e.Transformed)
            {
                // Rebuilt together with glyf.
            }
            else if (!(e.Tag == TagHmtx && e.Transformed))
            {
                e.Output = table.ToArray();
            }
        }

        for (int i = 0; i < entries.Length; i++)
        {
            ref Entry e = ref entries[i];
            if (e.Tag == TagHmtx && e.Transformed)
            {
                byte[]? hhea = null;
                for (int k = 0; k < entries.Length; k++)
                {
                    if (entries[k].Tag == TagHhea)
                    {
                        hhea = entries[k].Output;
                    }
                }

                if (xMins is null || hhea is null || hhea.Length < 36)
                {
                    why = "A transformed hmtx table needs the glyf and hhea tables.";
                    return FontLoadStatus.InvalidContainer;
                }

                int numHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34));
                byte[]? hmtx = ReconstructHmtx(block.AsSpan(e.BlockOffset, e.StoredLength), numHMetrics, numGlyphs, xMins);
                if (hmtx is null)
                {
                    why = "The transformed hmtx table is malformed.";
                    return FontLoadStatus.InvalidContainer;
                }

                e.Output = hmtx;
            }
        }

        return Assemble(entries, flavor, glyfDone, indexFormat, limits, out sfnt, out why);
    }

    private static FontLoadStatus Assemble(Entry[] entries, uint flavor, bool patchLocaFormat, int indexFormat, FontLimits limits, out byte[] sfnt, out string? why)
    {
        sfnt = [];
        why = null;
        Array.Sort(entries, static (a, b) => a.Tag.CompareTo(b.Tag));
        long total = 12 + (16L * entries.Length);
        foreach (Entry e in entries)
        {
            if (e.Output is null)
            {
                why = "A table could not be reconstructed.";
                return FontLoadStatus.InvalidContainer;
            }

            total += (e.Output.Length + 3L) & ~3L;
        }

        if (total > limits.MaxDecodedBytes)
        {
            why = "The decoded font would be larger than the limit.";
            return FontLoadStatus.LimitExceeded;
        }

        byte[] output = new byte[total];
        int n = entries.Length;
        BinaryPrimitives.WriteUInt32BigEndian(output, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)n);
        int entrySelector = 0;
        while ((2 << entrySelector) <= n)
        {
            entrySelector++;
        }

        int searchRange = (1 << entrySelector) * 16;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), (ushort)((n * 16) - searchRange));

        int pos = 12 + (16 * n);
        int headOffset = -1;
        for (int i = 0; i < n; i++)
        {
            byte[] table = entries[i].Output!;
            table.CopyTo(output, pos);
            if (entries[i].Tag == TagHead && table.Length >= 54)
            {
                headOffset = pos;
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(pos + 8), 0); // checkSumAdjustment is recomputed below
                if (patchLocaFormat)
                {
                    BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(pos + 50), (short)indexFormat);
                }
            }

            Span<byte> dir = output.AsSpan(12 + (i * 16), 16);
            BinaryPrimitives.WriteUInt32BigEndian(dir, entries[i].Tag);
            BinaryPrimitives.WriteUInt32BigEndian(dir[4..], FontLoader.Checksum(output.AsSpan(pos, table.Length)));
            BinaryPrimitives.WriteUInt32BigEndian(dir[8..], (uint)pos);
            BinaryPrimitives.WriteUInt32BigEndian(dir[12..], (uint)table.Length);
            pos += (table.Length + 3) & ~3;
        }

        if (headOffset >= 0)
        {
            uint adjustment = 0xB1B0AFBA - FontLoader.Checksum(output);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(headOffset + 8), adjustment);
        }

        sfnt = output;
        return FontLoadStatus.Ok;
    }

    // §5.4: lsb values omitted from the stored hmtx are the glyphs' xMin.
    private static byte[]? ReconstructHmtx(ReadOnlySpan<byte> t, int numHMetrics, int numGlyphs, int[] xMins)
    {
        if (t.Length < 1 || numHMetrics > numGlyphs || numHMetrics == 0)
        {
            return null;
        }

        int flags = t[0];
        bool lsbOmitted = (flags & 1) != 0;
        bool leftOmitted = (flags & 2) != 0;
        if ((flags & 0xFC) != 0 || (flags & 3) == 0)
        {
            return null;
        }

        long need = 1 + (2L * numHMetrics) + (lsbOmitted ? 0 : 2L * numHMetrics) + (leftOmitted ? 0 : 2L * (numGlyphs - numHMetrics));
        if (need != t.Length)
        {
            return null;
        }

        byte[] output = new byte[(4 * numHMetrics) + (2 * (numGlyphs - numHMetrics))];
        int adv = 1;
        int lsbAt = 1 + (2 * numHMetrics);
        for (int i = 0; i < numHMetrics; i++)
        {
            t.Slice(adv + (2 * i), 2).CopyTo(output.AsSpan(4 * i));
            if (lsbOmitted)
            {
                BinaryPrimitives.WriteInt16BigEndian(output.AsSpan((4 * i) + 2), (short)xMins[i]);
            }
            else
            {
                t.Slice(lsbAt + (2 * i), 2).CopyTo(output.AsSpan((4 * i) + 2));
            }
        }

        int leftAt = lsbAt + (lsbOmitted ? 0 : 2 * numHMetrics);
        for (int i = numHMetrics; i < numGlyphs; i++)
        {
            int o = (4 * numHMetrics) + (2 * (i - numHMetrics));
            if (leftOmitted)
            {
                BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(o), (short)xMins[i]);
            }
            else
            {
                t.Slice(leftAt + (2 * (i - numHMetrics)), 2).CopyTo(output.AsSpan(o));
            }
        }

        return output;
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        public int Pos;
        public bool Failed;

        public readonly int Remaining => _data.Length - Pos;

        public int U8()
        {
            if (Pos >= _data.Length)
            {
                Failed = true;
                return 0;
            }

            return _data[Pos++];
        }

        public int U16() => (U8() << 8) | U8();

        public int Read255()
        {
            int code = U8();
            return code switch
            {
                253 => U16(),
                255 => U8() + 253,
                254 => U8() + 506,
                _ => code,
            };
        }

        public ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || n > Remaining)
            {
                Failed = true;
                return default;
            }

            ReadOnlySpan<byte> s = _data.Slice(Pos, n);
            Pos += n;
            return s;
        }
    }

    // A growable output with a hard cap, so a hostile font cannot make reconstruction allocate without bound.
    private sealed class Sink(int cap)
    {
        private byte[] _buf = new byte[1024];
        private readonly int _cap = cap;

        public int Length { get; private set; }

        public bool Overflow { get; private set; }

        public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();

        public void Put8(int v)
        {
            Ensure(1);
            if (!Overflow)
            {
                _buf[Length++] = (byte)v;
            }
        }

        public void Put16(int v)
        {
            Put8(v >> 8);
            Put8(v);
        }

        public void Put(ReadOnlySpan<byte> s)
        {
            Ensure(s.Length);
            if (!Overflow)
            {
                s.CopyTo(_buf.AsSpan(Length));
                Length += s.Length;
            }
        }

        public void PadTo4()
        {
            while ((Length & 3) != 0 && !Overflow)
            {
                Put8(0);
            }
        }

        private void Ensure(int extra)
        {
            if (Overflow)
            {
                return;
            }

            long need = (long)Length + extra;
            if (need > _cap)
            {
                Overflow = true;
                return;
            }

            if (need > _buf.Length)
            {
                long size = Math.Max(need, (long)_buf.Length * 2);
                Array.Resize(ref _buf, (int)Math.Min(size, _cap));
            }
        }
    }

    // §5.1-§5.3.
    private static FontLoadStatus ReconstructGlyf(ReadOnlySpan<byte> t, FontLimits limits, out byte[] glyf, out byte[] loca, out int[]? xMins, out int numGlyphs, out int indexFormat, out string? why)
    {
        glyf = [];
        loca = [];
        xMins = null;
        numGlyphs = 0;
        indexFormat = 0;
        why = "The transformed glyf table is malformed.";
        if (t.Length < 36)
        {
            return FontLoadStatus.InvalidContainer;
        }

        int optionFlags = BinaryPrimitives.ReadUInt16BigEndian(t[2..]);
        numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(t[4..]);
        indexFormat = BinaryPrimitives.ReadUInt16BigEndian(t[6..]);
        if (indexFormat > 1)
        {
            return FontLoadStatus.InvalidContainer;
        }

        long sum = 36;
        Span<int> sizes = stackalloc int[7];
        for (int i = 0; i < 7; i++)
        {
            uint s = BinaryPrimitives.ReadUInt32BigEndian(t[(8 + (4 * i))..]);
            if (s > (uint)t.Length)
            {
                return FontLoadStatus.InvalidContainer;
            }

            sizes[i] = (int)s;
            sum += s;
        }

        int overlapBytes = (optionFlags & 1) != 0 ? (numGlyphs + 7) / 8 : 0;
        if (sum + overlapBytes > t.Length)
        {
            return FontLoadStatus.InvalidContainer;
        }

        int at = 36;
        var nContours = new Reader(t.Slice(at, sizes[0]));
        at += sizes[0];
        var nPoints = new Reader(t.Slice(at, sizes[1]));
        at += sizes[1];
        var flagStream = new Reader(t.Slice(at, sizes[2]));
        at += sizes[2];
        var glyphStream = new Reader(t.Slice(at, sizes[3]));
        at += sizes[3];
        var composite = new Reader(t.Slice(at, sizes[4]));
        at += sizes[4];
        ReadOnlySpan<byte> bboxAll = t.Slice(at, sizes[5]);
        at += sizes[5];
        var instructions = new Reader(t.Slice(at, sizes[6]));
        at += sizes[6];
        ReadOnlySpan<byte> overlap = t.Slice(at, overlapBytes);

        int bitmapBytes = 4 * ((numGlyphs + 31) >> 5);
        if (bboxAll.Length < bitmapBytes)
        {
            return FontLoadStatus.InvalidContainer;
        }

        ReadOnlySpan<byte> bboxBitmap = bboxAll[..bitmapBytes];
        var bbox = new Reader(bboxAll[bitmapBytes..]);

        var sink = new Sink(limits.MaxDecodedBytes);
        var offsets = new int[numGlyphs + 1];
        xMins = new int[numGlyphs];
        int[] xs = new int[64];
        int[] ys = new int[64];
        byte[] on = new byte[64];
        int[] endPts = new int[16];

        for (int g = 0; g < numGlyphs; g++)
        {
            offsets[g] = sink.Length;
            int nc = (short)nContours.U16();
            bool explicitBox = (bboxBitmap[g >> 3] & (0x80 >> (g & 7))) != 0;
            if (nContours.Failed)
            {
                return FontLoadStatus.InvalidContainer;
            }

            if (nc == 0)
            {
                if (explicitBox)
                {
                    return FontLoadStatus.InvalidContainer;
                }

                continue;
            }

            if (nc == -1)
            {
                if (!explicitBox)
                {
                    return FontLoadStatus.InvalidContainer;
                }

                int bx0 = (short)ReadS16(ref bbox);
                int by0 = (short)ReadS16(ref bbox);
                int bx1 = (short)ReadS16(ref bbox);
                int by1 = (short)ReadS16(ref bbox);
                xMins[g] = bx0;
                sink.Put16(0xFFFF);
                sink.Put16(bx0);
                sink.Put16(by0);
                sink.Put16(bx1);
                sink.Put16(by1);
                bool more;
                bool haveInstructions = false;
                do
                {
                    int flags = composite.U16();
                    int extra = 2 + ((flags & 1) != 0 ? 4 : 2);
                    if ((flags & 0x8) != 0)
                    {
                        extra += 2;
                    }
                    else if ((flags & 0x40) != 0)
                    {
                        extra += 4;
                    }
                    else if ((flags & 0x80) != 0)
                    {
                        extra += 8;
                    }

                    ReadOnlySpan<byte> args = composite.Take(extra);
                    if (composite.Failed)
                    {
                        return FontLoadStatus.InvalidContainer;
                    }

                    sink.Put16(flags);
                    sink.Put(args);
                    more = (flags & 0x20) != 0;
                    haveInstructions |= (flags & 0x100) != 0;
                }
                while (more);

                if (haveInstructions)
                {
                    int il = glyphStream.Read255();
                    ReadOnlySpan<byte> code = instructions.Take(il);
                    if (glyphStream.Failed || instructions.Failed)
                    {
                        return FontLoadStatus.InvalidContainer;
                    }

                    sink.Put16(il);
                    sink.Put(code);
                }
            }
            else if (nc > 0)
            {
                if (endPts.Length < nc)
                {
                    endPts = new int[Math.Max(nc, endPts.Length * 2)];
                }

                int total = 0;
                for (int c = 0; c < nc; c++)
                {
                    total += nPoints.Read255();
                    if (total > 65535 || nPoints.Failed)
                    {
                        return FontLoadStatus.InvalidContainer;
                    }

                    endPts[c] = total - 1;
                }

                if (total > xs.Length)
                {
                    int size = Math.Max(total, xs.Length * 2);
                    xs = new int[size];
                    ys = new int[size];
                    on = new byte[size];
                }

                ReadOnlySpan<byte> flagBytes = flagStream.Take(total);
                if (flagStream.Failed)
                {
                    return FontLoadStatus.InvalidContainer;
                }

                int px = 0;
                int py = 0;
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                for (int p = 0; p < total; p++)
                {
                    int f = flagBytes[p];
                    on[p] = (byte)((f & 0x80) != 0 ? 0 : 1);
                    if (!DecodeTriplet(f & 0x7F, ref glyphStream, out int dx, out int dy))
                    {
                        return FontLoadStatus.InvalidContainer;
                    }

                    px += dx;
                    py += dy;
                    if (px is < short.MinValue or > short.MaxValue || py is < short.MinValue or > short.MaxValue)
                    {
                        return FontLoadStatus.InvalidContainer;
                    }

                    xs[p] = px;
                    ys[p] = py;
                    minX = Math.Min(minX, px);
                    maxX = Math.Max(maxX, px);
                    minY = Math.Min(minY, py);
                    maxY = Math.Max(maxY, py);
                }

                int instrLen = glyphStream.Read255();
                ReadOnlySpan<byte> code = instructions.Take(instrLen);
                if (glyphStream.Failed || instructions.Failed)
                {
                    return FontLoadStatus.InvalidContainer;
                }

                if (explicitBox)
                {
                    minX = (short)ReadS16(ref bbox);
                    minY = (short)ReadS16(ref bbox);
                    maxX = (short)ReadS16(ref bbox);
                    maxY = (short)ReadS16(ref bbox);
                    if (bbox.Failed)
                    {
                        return FontLoadStatus.InvalidContainer;
                    }
                }

                xMins[g] = minX;
                sink.Put16(nc);
                sink.Put16(minX);
                sink.Put16(minY);
                sink.Put16(maxX);
                sink.Put16(maxY);
                for (int c = 0; c < nc; c++)
                {
                    sink.Put16(endPts[c]);
                }

                sink.Put16(instrLen);
                sink.Put(code);

                bool overlapFlag = overlapBytes != 0 && (overlap[g >> 3] & (0x80 >> (g & 7))) != 0;
                int lastX = 0;
                int lastY = 0;
                for (int p = 0; p < total; p++)
                {
                    int dx = xs[p] - lastX;
                    int dy = ys[p] - lastY;
                    lastX = xs[p];
                    lastY = ys[p];
                    if (Math.Abs(dx) > short.MaxValue || Math.Abs(dy) > short.MaxValue)
                    {
                        return FontLoadStatus.InvalidContainer; // a delta must fit the glyph table's 16-bit encoding
                    }

                    int f = on[p];
                    f |= dx == 0 ? 0x10 : (Math.Abs(dx) < 256 ? (0x02 | (dx > 0 ? 0x10 : 0)) : 0);
                    f |= dy == 0 ? 0x20 : (Math.Abs(dy) < 256 ? (0x04 | (dy > 0 ? 0x20 : 0)) : 0);
                    if (p == 0 && overlapFlag)
                    {
                        f |= 0x40;
                    }

                    sink.Put8(f);
                }

                for (int p = 0, last = 0; p < total; p++)
                {
                    int dx = xs[p] - last;
                    last = xs[p];
                    if (dx == 0)
                    {
                        continue;
                    }

                    if (Math.Abs(dx) < 256)
                    {
                        sink.Put8(Math.Abs(dx));
                    }
                    else
                    {
                        sink.Put16(dx);
                    }
                }

                for (int p = 0, last = 0; p < total; p++)
                {
                    int dy = ys[p] - last;
                    last = ys[p];
                    if (dy == 0)
                    {
                        continue;
                    }

                    if (Math.Abs(dy) < 256)
                    {
                        sink.Put8(Math.Abs(dy));
                    }
                    else
                    {
                        sink.Put16(dy);
                    }
                }
            }
            else
            {
                return FontLoadStatus.InvalidContainer;
            }

            sink.PadTo4();
            if (sink.Overflow)
            {
                why = "The reconstructed glyph table is larger than the limit.";
                return FontLoadStatus.LimitExceeded;
            }

            if (bbox.Failed)
            {
                return FontLoadStatus.InvalidContainer;
            }
        }

        offsets[numGlyphs] = sink.Length;
        if (sink.Overflow)
        {
            why = "The reconstructed glyph table is larger than the limit.";
            return FontLoadStatus.LimitExceeded;
        }

        glyf = sink.ToArray();
        long locaSize = (long)(numGlyphs + 1) * (indexFormat == 0 ? 2 : 4);
        if (locaSize > limits.MaxDecodedBytes)
        {
            why = "The reconstructed loca table is larger than the limit.";
            return FontLoadStatus.LimitExceeded;
        }

        loca = new byte[locaSize];
        for (int i = 0; i <= numGlyphs; i++)
        {
            if (indexFormat == 0)
            {
                if (offsets[i] > 0x1FFFE)
                {
                    return FontLoadStatus.InvalidContainer;
                }

                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(2 * i), (ushort)(offsets[i] / 2));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4 * i), (uint)offsets[i]);
            }
        }

        why = null;
        return FontLoadStatus.Ok;
    }

    private static int ReadS16(ref Reader r) => r.U16();

    // §5.2 triplet encoding of point coordinates.
    private static bool DecodeTriplet(int flag, ref Reader r, out int dx, out int dy)
    {
        dx = 0;
        dy = 0;
        int b0;
        int b1;
        if (flag < 10)
        {
            dy = WithSign(flag, ((flag & 14) << 7) + r.U8());
        }
        else if (flag < 20)
        {
            dx = WithSign(flag, (((flag - 10) & 14) << 7) + r.U8());
        }
        else if (flag < 84)
        {
            b0 = flag - 20;
            b1 = r.U8();
            dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
            dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
        }
        else if (flag < 120)
        {
            b0 = flag - 84;
            b1 = r.U8();
            int b2 = r.U8();
            dx = WithSign(flag, 1 + ((b0 / 12) << 8) + b1);
            dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + b2);
        }
        else if (flag < 124)
        {
            b0 = r.U8();
            b1 = r.U8();
            int b2 = r.U8();
            dx = WithSign(flag, (b0 << 4) + (b1 >> 4));
            dy = WithSign(flag >> 1, ((b1 & 0x0F) << 8) + b2);
        }
        else
        {
            b0 = r.U8();
            b1 = r.U8();
            int b2 = r.U8();
            int b3 = r.U8();
            dx = WithSign(flag, (b0 << 8) + b1);
            dy = WithSign(flag >> 1, (b2 << 8) + b3);
        }

        return !r.Failed;
    }

    private static int WithSign(int flag, int value) => (flag & 1) != 0 ? value : -value;

    // §2 UIntBase128: at most five bytes, no leading zero byte, value below 2^32.
    private static bool ReadBase128(byte[] data, ref int pos, out uint value)
    {
        value = 0;
        for (int i = 0; i < 5; i++)
        {
            if (pos >= data.Length)
            {
                return false;
            }

            byte b = data[pos++];
            if (i == 0 && b == 0x80)
            {
                return false;
            }

            if ((value & 0xFE000000) != 0)
            {
                return false;
            }

            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
