namespace OneRender.Painting;

/// <summary>An image the host can supply: its handle in the display list and its natural size.</summary>
/// <param name="Handle">The name the display list uses for the image; the host maps it to pixels when it draws.</param>
/// <param name="Width">Natural width in CSS px; 0 or less when the image has no intrinsic width.</param>
/// <param name="Height">Natural height in CSS px; 0 or less when the image has no intrinsic height.</param>
public readonly record struct ImageInfo(ImageHandle Handle, double Width, double Height);

/// <summary>
/// Tells the painter about the images a document refers to (<c>url()</c> in styles, <c>src</c> of <c>img</c>). The engine never
/// loads, decodes or fetches anything: the host answers from what it has. Lookups happen on every paint, so an implementation should
/// answer from a table without allocating.
/// </summary>
public interface IImageSource
{
    /// <summary>Looks up an image.</summary>
    /// <param name="address">The address exactly as written in the document (<c>url(...)</c> contents or the <c>src</c> attribute); relative references are the host's to resolve.</param>
    /// <param name="image">The handle and natural size.</param>
    /// <returns>False when the host has no such image; the painter then draws nothing for it and records a diagnostic.</returns>
    bool TryGetImage(string address, out ImageInfo image);

    /// <summary>Looks up an image, optionally with the pixel layout stored in the file (CSS Images 3 §5.2, <c>image-orientation: none</c>).</summary>
    /// <param name="address">The address as in <see cref="TryGetImage(string, out ImageInfo)"/>.</param>
    /// <param name="storedOrientation">True to ignore the EXIF orientation of the file; false to rotate and flip the pixels upright (<c>from-image</c>).</param>
    /// <param name="image">The handle and natural size.</param>
    /// <returns>False when the host has no such image. The default implementation ignores <paramref name="storedOrientation"/>.</returns>
    bool TryGetImage(string address, bool storedOrientation, out ImageInfo image) => TryGetImage(address, out image);
}
