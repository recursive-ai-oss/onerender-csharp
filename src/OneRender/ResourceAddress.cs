namespace OneRender;

/// <summary>Resolves the relative references of a document against its base address. No I/O: it only joins strings.</summary>
public static class ResourceAddress
{
    /// <summary>Resolves <paramref name="reference"/> against <paramref name="baseAddress"/> as a URL reference (RFC 3986 §5).</summary>
    /// <param name="baseAddress">An absolute address, or null for none.</param>
    /// <param name="reference">The reference as written in the document.</param>
    /// <returns>The absolute address; <paramref name="reference"/> unchanged when it is already absolute, when there is no usable base, or when it cannot be parsed.</returns>
    public static string Resolve(string? baseAddress, string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (string.IsNullOrEmpty(baseAddress) || reference.Length == 0 || reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return reference;
        }

        if (Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? b) && Uri.TryCreate(b, reference, out Uri? r))
        {
            return r.OriginalString.Length > 0 && r.IsAbsoluteUri ? r.AbsoluteUri : reference;
        }

        return reference;
    }
}
