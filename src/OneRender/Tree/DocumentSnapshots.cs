using System.Runtime.CompilerServices;
using RecursiveAI.OneHtml;

namespace OneRender.Tree;

/// <summary>
/// Where the latest edited snapshot of a page's document is. An interactive page keeps the <see cref="Document"/> it was created from
/// (so every node handle a host holds stays valid) and, after an attribute or class change, a newer snapshot of the same tree shape
/// that carries the new attributes. Reads of attributes and selector matches go through <see cref="Current"/>, which maps a node of the
/// original document to the same node (same index) of the latest snapshot.
/// </summary>
internal static class DocumentSnapshots
{
    private static readonly ConditionalWeakTable<Document, Document> Latest = new();
    private static int _any;

    /// <summary>Records <paramref name="snapshot"/> as the newest version of <paramref name="original"/>.</summary>
    public static void Set(Document original, Document snapshot)
    {
        Latest.AddOrUpdate(original, snapshot);
        Volatile.Write(ref _any, 1);
    }

    /// <summary>The newest snapshot of the document, or the document itself when it was never edited.</summary>
    public static Document Of(Document document) =>
        Volatile.Read(ref _any) != 0 && Latest.TryGetValue(document, out Document? s) ? s : document;

    /// <summary>The same node in the newest snapshot of its document; the node itself when the document was never edited, or is a snapshot.</summary>
    public static Node Current(Node node)
    {
        if (node.IsNull || Volatile.Read(ref _any) == 0)
        {
            return node;
        }

        return Latest.TryGetValue(node.Document!, out Document? s) ? s.GetNode(node.Index) : node;
    }

    /// <summary>The same node in <paramref name="original"/>, for results found in a snapshot.</summary>
    public static Node Original(Node node, Document original) => node.IsNull ? node : original.GetNode(node.Index);

    /// <summary>An attribute's current value, or null when absent.</summary>
    public static string? Attr(Node node, string name) => Current(node).GetAttribute(name);
}
