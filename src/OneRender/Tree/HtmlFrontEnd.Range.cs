using OneRender.Forms;
using OneRender.Style;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tree;

// The parts of an input type=range (CSS Forms 1 section 4.2; HTML section 15.5.x): inside the input sit a container, the track in it and the
// thumb in the track, as three styled nodes with no element of their own. The track and the thumb take their style from the cascade
// (::slider-track, ::slider-thumb and the browsers' names for them), so an author's boxes are laid out and painted like any other box.
// What follows was measured from Chrome 156's rendering (docs/forms.md, "Styling a range"):
//  - the container is a flex container that fills the input's content box and centers the track vertically (the track may overflow
//    both ends of a shorter input);
//  - the track is a border-box flex item that grows to the container's width, whatever width it declares, and is as high as its content;
//  - the thumb is a border-box block in the track; layout moves it by its share of the room the track's content box leaves beside it
//    (RelativeOffset), and its top edge is the track's content top plus its margin;
//  - a native thumb (the input's appearance is auto, or the thumb's own is) is 16 px square and ignores the author's size and paint.
public sealed partial class HtmlFrontEnd
{
    // The styled-node indices of range inputs that were built, so that a state change which only moves the parts' styles (the input's
    // :hover with a ::slider-thumb rule) can find them. Entries of removed or reused slots are checked on use.
    private readonly List<int> _ranges = [];

    private void AddRangeParts(int input)
    {
        _ranges.Add(input);
        StyleId inputStyle = _tree.StyleOf(input);
        int container = _tree.NewNode(StyledNodeKind.SliderContainer, input, inputStyle, -1);
        if (container < 0)
        {
            return;
        }

        int track = _tree.NewNode(StyledNodeKind.SliderTrack, container, inputStyle, -1);
        if (track < 0)
        {
            return;
        }

        int thumb = _tree.NewNode(StyledNodeKind.SliderThumb, track, inputStyle, -1);
        _tree.ReplaceStyle(container, SliderPartStyle(container, inputStyle));
        _tree.ReplaceStyle(track, SliderPartStyle(track, _tree.StyleOf(container)));
        if (thumb >= 0)
        {
            _tree.ReplaceStyle(thumb, SliderPartStyle(thumb, _tree.StyleOf(track)));
        }
    }

    /// <summary>
    /// Marks the parts of every range for a new style, for a state change that moved their rules' results without changing any input's
    /// own style.
    /// </summary>
    internal void MarkRangePartsDirty()
    {
        for (int i = _ranges.Count - 1; i >= 0; i--)
        {
            int input = _ranges[i];
            if (!IsLiveRange(input))
            {
                _ranges[i] = _ranges[^1];
                _ranges.RemoveAt(_ranges.Count - 1);
                continue;
            }

            MarkPartsDirty(input);
        }
    }

    private bool IsLiveRange(int input) =>
        input < _tree.Count && !_tree.IsFreeSlot(input) && _tree.KindOf(input) == (byte)StyledNodeKind.Element && _tree.Styles[_tree.StyleOf(input)].Control == ControlKind.Range;

    // The container, track and thumb under a range input, in that order, as the nodes of its subtree.
    private void MarkPartsDirty(int input)
    {
        for (int c = _tree.FirstChildOf(input); c >= 0; c = _tree.FirstChildOf(c))
        {
            if (_tree.KindOf(c) is not ((byte)StyledNodeKind.SliderContainer or (byte)StyledNodeKind.SliderTrack or (byte)StyledNodeKind.SliderThumb))
            {
                break;
            }

            _tree.MarkStyleDirty(c);
        }
    }

    // The range input a slider part belongs to: the nearest ancestor that is an element.
    private int RangeInputOf(int part)
    {
        int n = _tree.ParentOf(part);
        while (n >= 0 && _tree.KindOf(n) != (byte)StyledNodeKind.Element)
        {
            n = _tree.ParentOf(n);
        }

        return n;
    }

    private StyleId SliderPartStyle(int node, StyleId parentStyle)
    {
        StyleTable table = _tree.Styles;
        var kind = (StyledNodeKind)_tree.KindOf(node);
        if (kind == StyledNodeKind.SliderContainer)
        {
            StyleId anon = table.GetAnonymous(parentStyle, new DisplayType(OuterDisplay.Block, InnerDisplay.Flex));
            ComputedValues c = table[anon];
            c.AlignItems = AlignKind.Center;
            c.Height = Length.FromPercent(100);
            return table.Derive(anon, in c);
        }

        int input = RangeInputOf(node);
        StyleSource styles = _current;
        if (input < 0 || styles.IsNull || _tree.SourceIndexOf(input) < 0)
        {
            return _tree.StyleOf(node);
        }

        Node element = styles.Document.GetNode(_tree.SourceIndexOf(input));
        ComputedStyle? ps = styles.GetPseudoStyle(element, kind == StyledNodeKind.SliderTrack ? PseudoElementKind.SliderTrack : PseudoElementKind.SliderThumb);
        StyleId resolved;
        if (ps is not null)
        {
            resolved = Resolve(ps, parentStyle, true, false);
        }
        else
        {
            // The cascade always answers for a range (its user-agent sheet styles both parts); this keeps the geometry sound if it does not.
            StyleId anon = table.GetAnonymous(parentStyle, DisplayType.Block);
            ComputedValues d = table[anon];
            d.BoxSizing = BoxSizingKind.BorderBox;
            if (kind == StyledNodeKind.SliderTrack)
            {
                d.FlexGrow = 1;
                d.FlexShrink = 1;
                d.FlexBasis = Length.FromPercent(0);
                d.AlignSelf = AlignKind.Center;
            }

            resolved = table.Derive(anon, in d);
        }

        if (kind == StyledNodeKind.SliderTrack)
        {
            return resolved;
        }

        // The thumb. The input's values are read first: deriving may move the table's rows.
        ComputedValues iv = table[_tree.StyleOf(input)];
        bool inputNative = iv.Appearance == AppearanceKind.Auto;
        double fraction = Math.Clamp(iv.ControlFraction, 0, 1);
        ControlStates disabled = iv.ControlState & ControlStates.Disabled;
        bool rtl = iv.Direction == DirectionKind.Rtl;
        ComputedValues v = table[resolved];
        v.ThumbFraction = rtl ? 1 - fraction : fraction;
        v.ControlState = disabled;
        if (inputNative || v.Appearance == AppearanceKind.Auto)
        {
            // Probe (Chrome 156): while the input is native the thumb's own appearance and size are ignored, and so is its paint.
            v.Control = ControlKind.RangeThumb;
            v.Appearance = AppearanceKind.Auto;
            v.Width = Length.FromPx(16);
            v.Height = Length.FromPx(16);
            v.MinWidth = Length.Auto;
            v.MinHeight = Length.Auto;
            v.MaxWidth = Length.None;
            v.MaxHeight = Length.None;
            v.BoxSizing = BoxSizingKind.BorderBox;
            v.Background = BackgroundSpec.Default;
            v.BackgroundColor = ColorValue.Transparent;
            v.BoxShadow = ShadowList.None;
            v.BorderTopWidth = v.BorderRightWidth = v.BorderBottomWidth = v.BorderLeftWidth = 0;
            v.BorderTopStyle = v.BorderRightStyle = v.BorderBottomStyle = v.BorderLeftStyle = BorderStyleKind.None;
        }

        return table.Derive(resolved, in v);
    }
}
