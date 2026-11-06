using System.Numerics;
using OneRender.Painting;

namespace OneRender.Tests;

/// <summary>What bounds push items report, as written in docs/display-list.md.</summary>
public class PushBoundsTests
{
    [Fact]
    public void ClipsCarryTheirShapeAndOtherPushesCarryNothing()
    {
        var l = new DisplayList();
        l.PushTransform(ItemId.Of(1), Matrix3x2.CreateTranslation(10, 20));
        l.PushClipRect(ItemId.Of(2), new RectF(5, 6, 30, 40));
        l.PushGroup(ItemId.Of(3), 0.5f);
        l.BeginTag(ItemId.Of(4), TagRole.Div);
        l.FillRect(ItemId.Of(5), new RectF(0, 0, 8, 8), new PaintColor(1, 0, 0, 1));
        l.EndTag();
        l.PopGroup();
        l.PopClip();
        l.PopTransform();
        l.Validate();

        Assert.True(l[0].LocalBounds.IsEmpty);
        Assert.Equal(new RectF(5, 6, 30, 40), l[1].LocalBounds);
        Assert.True(l[2].LocalBounds.IsEmpty);
        Assert.True(l[3].LocalBounds.IsEmpty);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(l[i].RootBounds.IsEmpty);
            Assert.True(l[i].LayerBounds.IsEmpty);
        }

        // The paint item inside gets both: moved by the transform, cut by the clip (RootBounds only).
        Assert.Equal(new RectF(10, 20, 8, 8), l[4].LayerBounds);
        Assert.Equal(new RectF(15, 26, 3, 2), l[4].RootBounds);
        for (int i = 5; i < l.Count; i++)
        {
            Assert.True(l[i].LocalBounds.IsEmpty && l[i].RootBounds.IsEmpty && l[i].LayerBounds.IsEmpty);
        }
    }
}
