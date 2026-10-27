namespace OneRender.Style;

/// <summary>The <c>pointer-events</c> property as it applies to HTML content (CSS UI 4 §6.3).</summary>
public enum PointerEventsKind : byte
{
    /// <summary><c>auto</c>, and the SVG-only painted/fill/stroke keywords, which behave as <c>auto</c> outside SVG.</summary>
    Auto,

    /// <summary><c>none</c>: the element is never the target of a pointer event; its descendants still can be.</summary>
    None,
}

public partial struct ComputedValues
{
    private static void InitInteraction(ref ComputedValues v)
    {
        v.PointerEvents = PointerEventsKind.Auto;
        v.Transform = Transform2D.Identity;
        v.TransformOriginX = Length.FromPercent(50);
        v.TransformOriginY = Length.FromPercent(50);
    }

    internal static bool InteractionEquals(in ComputedValues a, in ComputedValues b) =>
        a.PointerEvents == b.PointerEvents
        && a.Transform.Equals(b.Transform)
        && a.TransformOriginX.Equals(b.TransformOriginX)
        && a.TransformOriginY.Equals(b.TransformOriginY);

    internal static void InteractionHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.PointerEvents);
        h.Add(v.Transform);
        h.Add(v.TransformOriginX);
        h.Add(v.TransformOriginY);
    }
}
