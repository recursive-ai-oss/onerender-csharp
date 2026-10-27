using OneFont;

namespace OneRender.Painting;

// Surface for a host that draws the list through its own atlases and render graph (format 1.3 and the keys that help it cache):
// host scopes, per-image damage, content keys for SVG images and stable keys for fonts.
public sealed partial class DisplayList
{
    // ---------------------------------------------------------------- host scopes

    /// <summary>
    /// Starts a host scope (format 1.3): everything up to the matching <see cref="PopHostScope"/> belongs to a material, layer or pass the
    /// host chose, named by one opaque integer. The engine gives the number no meaning and drawing ignores it: a backend that does not know
    /// the host's numbers (the image rasterizer, the PDF writer) draws the content as if the scope were not there. A list that contains
    /// one has <see cref="RequiredMinor"/> 3.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="value">The host's number: a material id, a layer id, anything that fits an <see cref="int"/>. Zero and negative values are allowed.</param>
    public void PushHostScope(ItemId id, int value)
    {
        Begin(ItemKind.PushHostScope, id, RectF.Empty, 0, 0);
        _items[_count - 1].A = value;
        H((ulong)(uint)value);
        _requiredMinor = Math.Max(_requiredMinor, 3);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the innermost host scope. Tags opened inside it are closed first, as for any scope.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a host scope.</exception>
    public void PopHostScope() => Pop(ItemKind.PopHostScope, ItemKind.PushHostScope);

    // ---------------------------------------------------------------- image invalidation

    /// <summary>
    /// Adds to <paramref name="damage"/> the root bounds of every <see cref="ItemKind.Image"/> item that draws <paramref name="image"/>:
    /// plain images, tiled backgrounds, the pieces of a nine-slice border image, all of them. Use it when the host replaced the pixels
    /// behind a handle and kept the size: nothing is laid out or painted again, no item changes, and the call allocates nothing.
    /// </summary>
    /// <param name="image">The handle whose pixels changed.</param>
    /// <param name="damage">Receives the areas; a null region only counts.</param>
    /// <returns>The number of items that draw the handle.</returns>
    public int InvalidateImage(ImageHandle image, DamageRegion? damage)
    {
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            ref ItemRecord r = ref _items[i];
            if (r.Kind == ItemKind.Image && r.A == image.Value)
            {
                damage?.Add(r.Device);
                n++;
            }
        }

        return n;
    }

    // ---------------------------------------------------------------- content keys

    /// <summary>
    /// The key of an inline SVG image: FNV-1a (64-bit) over the UTF-16 code units of the markup (low byte first), then over the four
    /// channels of the clamped <c>currentColor</c> as IEEE 754 single-precision bits (little-endian, R G B A). Equal markup and color give
    /// equal keys in every list, frame and process; the size is not part of it, so a host caches by (key, size × device-pixel ratio).
    /// </summary>
    internal static ulong SvgKeyOf(string markup, in PaintColor color)
    {
        const ulong Prime = 0x100000001B3UL;
        ulong h = 0xCBF29CE484222325UL;
        foreach (char c in markup)
        {
            h = (h ^ (byte)(c & 0xFF)) * Prime;
            h = (h ^ (byte)(c >> 8)) * Prime;
        }

        h = Fnv32(h, BitConverter.SingleToUInt32Bits(color.R));
        h = Fnv32(h, BitConverter.SingleToUInt32Bits(color.G));
        h = Fnv32(h, BitConverter.SingleToUInt32Bits(color.B));
        return Fnv32(h, BitConverter.SingleToUInt32Bits(color.A));
    }

    private static ulong Fnv32(ulong h, uint v)
    {
        const ulong Prime = 0x100000001B3UL;
        for (int i = 0; i < 4; i++)
        {
            h = (h ^ (byte)(v >> (8 * i))) * Prime;
        }

        return h;
    }

    /// <summary>
    /// The key of a font face: FNV-1a (64-bit) over its family, subfamily, full and PostScript names, units per em, glyph count, weight,
    /// style flags, and the 54 bytes of its <c>head</c> table (which carries the font's revision, creation date and checksum adjustment).
    /// The same font file gives the same key in every list, frame and process, so a glyph atlas can key on (face key, size × device-pixel
    /// ratio, glyph id). Two different files collide only by a 2^-64 chance.
    /// </summary>
    internal static ulong FaceKeyOf(FontFace face)
    {
        const ulong Prime = 0x100000001B3UL;
        ulong h = 0xCBF29CE484222325UL;
        h = FnvString(h, face.FamilyName);
        h = FnvString(h, face.SubfamilyName);
        h = FnvString(h, face.FullName);
        h = FnvString(h, face.PostScriptName);
        h = Fnv32(h, (uint)face.UnitsPerEm);
        h = Fnv32(h, (uint)face.GlyphCount);
        h = Fnv32(h, (uint)face.Weight);
        h = Fnv32(h, (face.IsItalic ? 1u : 0u) | (face.IsBold ? 2u : 0u) | (face.IsCff ? 4u : 0u));
        ReadOnlySpan<byte> head = face.GetTable("head").Span;
        if (head.Length > 54)
        {
            head = head[..54];
        }

        foreach (byte b in head)
        {
            h = (h ^ b) * Prime;
        }

        return h;
    }

    private static ulong FnvString(ulong h, string? s)
    {
        const ulong Prime = 0x100000001B3UL;
        if (s is null)
        {
            return (h ^ 0xFFUL) * Prime;
        }

        foreach (char c in s)
        {
            h = (h ^ (byte)(c & 0xFF)) * Prime;
            h = (h ^ (byte)(c >> 8)) * Prime;
        }

        return (h ^ 0xFEUL) * Prime;
    }

    /// <summary>The stable key of the face a glyph run refers to (see <see cref="GlyphRunView.FaceKey"/>).</summary>
    /// <param name="faceIndex">The index a run reports.</param>
    /// <returns>The key.</returns>
    public ulong GetFaceKey(int faceIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)faceIndex, (uint)_faceCount);
        return _faceHashes[faceIndex];
    }
}

public readonly partial struct DisplayItem
{
    /// <summary>The host's number of a <see cref="ItemKind.PushHostScope"/> item (format 1.3); 0 for other kinds.</summary>
    public int HostScope => Rec.Kind == ItemKind.PushHostScope ? Rec.A : 0;

    /// <summary>
    /// The content key of an <see cref="ItemKind.SvgImage"/> (format 1.3): equal for equal markup and <c>currentColor</c>, across lists,
    /// frames and processes; 0 for other kinds. A host caches its rasterization by (key, size × device-pixel ratio) and reads the
    /// markup (<see cref="SvgMarkup"/>) only on a miss.
    /// </summary>
    public ulong SvgKey => Rec.Kind == ItemKind.SvgImage ? ((ulong)(uint)Rec.C << 32) | (uint)Rec.B : 0UL;
}
