namespace OneRender.Tree;

public sealed partial class StyledTree
{
    // Names given to builder nodes. Kept apart from the per-node columns: a name is test and harness state, not something layout reads.
    private readonly Dictionary<string, int> _nameToNode = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _nodeToName = [];

    /// <summary>
    /// Gives a node a name that <see cref="FindByName"/> finds it by (a builder tree has no <c>id</c> attributes). Names are unique: giving a name
    /// that another node holds moves it to this node. A node has one name; a null or empty name removes it. Names are cleared with the tree.
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="name">The name, compared exactly; null or empty for none.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> is null or belongs to another tree.</exception>
    public void SetName(StyledNode node, string? name)
    {
        if (node.IsNull || !ReferenceEquals(node.Tree, this))
        {
            throw new ArgumentException("The node does not belong to this tree.", nameof(node));
        }

        if (_nodeToName.Remove(node.Index, out string? old))
        {
            _nameToNode.Remove(old);
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (_nameToNode.TryGetValue(name, out int other))
        {
            _nodeToName.Remove(other);
        }

        _nameToNode[name] = node.Index;
        _nodeToName[node.Index] = name;
    }

    /// <summary>The name <see cref="SetName"/> gave the node, or null.</summary>
    /// <param name="node">A node of this tree.</param>
    /// <returns>The name, or null.</returns>
    public string? NameOf(StyledNode node) =>
        !node.IsNull && ReferenceEquals(node.Tree, this) && _nodeToName.TryGetValue(node.Index, out string? name) ? name : null;

    /// <summary>
    /// Finds a node by name, for test harnesses. The order is: the node given this name with <see cref="SetName"/>; then, in a tree built from
    /// HTML, the first element in document order with this <c>id</c>; then the first form control (<c>button</c>, <c>fieldset</c>,
    /// <c>input</c>, <c>object</c>, <c>output</c>, <c>select</c>, <c>textarea</c>) with this <c>name</c> attribute. Comparison is exact. A node
    /// that was removed from the tree is not found.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The node, or the null handle.</returns>
    public StyledNode FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_nameToNode.TryGetValue(name, out int index) && index < _count && IsAttached(index))
        {
            return Make(index);
        }

        if (_document is not null)
        {
            RecursiveAI.OneHtml.Node element = DocumentNames.Find(_document, name);
            if (!element.IsNull)
            {
                StyledNode styled = FindBySource(element.Index);
                if (!styled.IsNull && IsAttached(styled.Index))
                {
                    return styled;
                }
            }
        }

        return default;
    }

    // A removed subtree keeps its columns until the tree is cleared, but it no longer hangs from the root.
    private bool IsAttached(int index)
    {
        int i = index;
        for (int steps = 0; steps <= _depth[index]; steps++)
        {
            int p = _parent[i];
            if (p < 0)
            {
                return i == 0;
            }

            i = p;
        }

        return false;
    }
}
