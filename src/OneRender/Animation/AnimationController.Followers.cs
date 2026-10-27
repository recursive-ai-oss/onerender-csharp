using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Animation;

internal sealed partial class AnimationController
{
    /// <summary>Most descendants one animated element drags along.</summary>
    public const int MaxFollowers = 4096;

    // Inherited properties (CSS Cascade 4 §7.1): when an element's color, say, is animated, the text inside it and the descendants that
    // inherit it must show the animated value too. The styles of those descendants were computed from the cascaded parent, so each gets
    // a private row of its own and receives the animated value of every inherited property the effects touch, in place, at each tick.
    // A descendant that sets the property itself, or has effects of its own, keeps its value and cuts the walk below it. Properties
    // whose descendants depend on the value through relative units (font-size and em) are copied as computed values, which text
    // follows exactly; em lengths of element descendants are not recomputed.
    private void BuildFollowers(StyledTree tree, ElementEffects e, int node)
    {
        e.FollowerNodes = [];
        e.FollowerRows = [];
        PropertyId[] touched = e.Touched;
        int inheritedCount = 0;
        for (int i = 0; i < touched.Length; i++)
        {
            if (Properties.IsInherited(touched[i]))
            {
                inheritedCount++;
            }
        }

        if (inheritedCount == 0)
        {
            e.Inherited = [];
            return;
        }

        var inherited = new PropertyId[inheritedCount];
        int k = 0;
        for (int i = 0; i < touched.Length; i++)
        {
            if (Properties.IsInherited(touched[i]))
            {
                inherited[k++] = touched[i];
            }
        }

        e.Inherited = inherited;
        StyleTable table = tree.Styles;
        var nodes = new List<int>();
        var rows = new List<StyleId>();

        // Preorder over the subtree without recursion; `next` skips the subtree below a node that does not follow.
        int d = tree.FirstChildOf(node);
        while (d >= 0 && nodes.Count < MaxFollowers)
        {
            bool follows = FollowsParent(tree, table, d, e, inherited);
            if (follows && AllocateRow(table, in table[tree.StyleOf(d)], out StyleId row))
            {
                tree.ReplaceStyle(d, row);
                nodes.Add(d);
                rows.Add(row);
            }
            else
            {
                follows = false;
            }

            int child = follows ? tree.FirstChildOf(d) : -1;
            if (child >= 0)
            {
                d = child;
                continue;
            }

            while (d >= 0 && d != node)
            {
                int sibling = tree.NextSiblingOf(d);
                if (sibling >= 0)
                {
                    d = sibling;
                    break;
                }

                d = tree.ParentOf(d);
                if (d == node)
                {
                    d = -1;
                }
            }

            if (d == node)
            {
                break;
            }
        }

        e.FollowerNodes = nodes.ToArray();
        e.FollowerRows = rows.ToArray();
    }

    private static bool FollowsParent(StyledTree tree, StyleTable table, int d, ElementEffects e, PropertyId[] inherited)
    {
        byte kind = tree.KindOf(d);
        if (kind != (byte)StyledNodeKind.Element && kind != (byte)StyledNodeKind.Text)
        {
            return false;
        }

        ref readonly ComputedValues v = ref table[tree.StyleOf(d)];
        if (kind == (byte)StyledNodeKind.Element && (HasTransitionsConfigured(in v) || v.HasAnimations))
        {
            return false;
        }

        for (int i = 0; i < inherited.Length; i++)
        {
            if (!PropertyAnimator.ValuesEqual(table, inherited[i], in v, in e.Base))
            {
                return false;
            }
        }

        return true;
    }

    // Copies the animated inherited values into the followers' rows. Allocation free.
    private static void PropagateToFollowers(StyleTable table, ElementEffects e)
    {
        int[] rows = e.Inherited.Length == 0 ? [] : e.FollowerNodes;
        if (rows.Length == 0)
        {
            return;
        }

        ref readonly ComputedValues source = ref table[e.Row];
        PropertyId[] inherited = e.Inherited;
        StyleId[] ids = e.FollowerRows;
        for (int f = 0; f < ids.Length; f++)
        {
            ref ComputedValues target = ref table.MutableRow(ids[f]);
            for (int i = 0; i < inherited.Length; i++)
            {
                table.CopyProperty(inherited[i], ref target, in source);
            }
        }
    }
}
