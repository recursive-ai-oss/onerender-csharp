using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    private void ViewportOffset(out double x, out double y)
    {
        ScrollRange r = _hits.ViewportRange;
        x = r.ClampX(_page.Scroll.ViewportX);
        y = r.ClampY(_page.Scroll.ViewportY);
    }

    // ------------------------------------------------------------------ public scrolling

    /// <summary>Scrolls the viewport to an offset (<c>window.scrollTo</c>), clamped to the scrollable area, and fires <c>scroll</c> at the document when the position changed.</summary>
    /// <param name="x">Horizontal offset in CSS px.</param>
    /// <param name="y">Vertical offset in CSS px.</param>
    /// <returns>True when the position changed. The host then patches its display list (<c>Painter.PatchScroll</c>); no relayout is needed.</returns>
    public bool ScrollViewportTo(double x, double y)
    {
        ViewportOffset(out double ox, out double oy);
        ScrollRange r = _hits.ViewportRange;
        double nx = r.ClampX(x), ny = r.ClampY(y);
        if (nx == ox && ny == oy)
        {
            return false;
        }

        _page.Scroll.SetViewportOffset(nx, ny);
        FireScroll(_page.Document.Root);
        return true;
    }

    /// <summary>
    /// Scrolls a scroll container to an offset (<c>scrollLeft</c>/<c>scrollTop</c>), clamped, and fires <c>scroll</c> at it when the position changed.
    /// For the root element this scrolls the viewport.
    /// </summary>
    /// <param name="element">A scroll container (<c>overflow</c> other than visible or clip) or the root element.</param>
    /// <param name="x">Horizontal offset in CSS px.</param>
    /// <param name="y">Vertical offset in CSS px.</param>
    /// <returns>True when the position changed; false when the element is not a scroll container or is already there.</returns>
    public bool ScrollElementTo(Node element, double x, double y)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, _page.Document))
        {
            return false;
        }

        if (element.Equals(_page.Document.DocumentElement))
        {
            return ScrollViewportTo(x, y);
        }

        int f = FragmentIndexOf(element);
        return f >= 0 && _hits.IsScrollContainer(f) && SetContainerScroll(f, x, y);
    }

    /// <summary>Scrolls the element into view with block and inline <c>nearest</c> alignment (CSSOM View, <c>scrollIntoView</c>), through every scroll container above it and then the viewport.</summary>
    /// <param name="element">The element.</param>
    /// <returns>True when any position changed.</returns>
    public bool ScrollIntoView(Node element) => !element.IsNull && ScrollIntoViewCore(element) != InputOutcome.None;

    private int FragmentIndexOf(Node element)
    {
        StyledNode s = _page.Styled.FindBySource(element.Index);
        if (s.IsNull)
        {
            return -1;
        }

        Fragment f = _page.Fragments.FragmentOf(_page.Boxes.PrincipalBox(s));
        return f.IsNull ? -1 : f.Index;
    }

    private bool SetContainerScroll(int fragment, double x, double y)
    {
        ScrollRange r = _hits.RangeOf(fragment);
        Box box = _page.Boxes.Make(_page.Fragments.Make(fragment).Box.Index);
        (double ox, double oy) = _page.Scroll.GetOffset(_page.Fragments, box);
        double nx = r.ClampX(x), ny = r.ClampY(y);
        if (nx == ox && ny == oy)
        {
            return false;
        }

        _page.Scroll.SetOffset(box, nx, ny);
        StyledNode sn = box.Node;
        if (sn.SourceIndex >= 0)
        {
            FireScroll(_page.Document.GetNode(sn.SourceIndex));
        }

        return true;
    }

    // CSSOM View: scroll events do not bubble from elements, and bubble from the document to the window.
    private void FireScroll(Node target)
    {
        DomEvent e = _se;
        bool isDocument = target.Kind == NodeKind.Document;
        e.Reset("scroll", isDocument, false, _lastTime);
        _events.Dispatch(e, EventTarget.Of(target));
    }

    private InputOutcome ScrollIntoViewCore(Node element)
    {
        int f = FragmentIndexOf(element);
        if (f < 0)
        {
            return InputOutcome.None;
        }

        bool any = false;
        for (int s = _hits.ContainingOf(f); s >= 0; s = _hits.ContainingOf(s))
        {
            if (!_hits.IsScrollContainer(s) || !_hits.RangeOf(s).CanScroll)
            {
                continue;
            }

            Rect target = _queries.ViewportRectOf(element);
            Fragment sf = _page.Fragments.Make(s);
            Rect abs = _hits.AbsoluteBox(s);
            Insets b = sf.Border;
            Rect port = _hits.ToViewport(sf, new Rect(abs.X + b.Left, abs.Y + b.Top, Math.Max(0, abs.Width - b.Horizontal), Math.Max(0, abs.Height - b.Vertical)));
            Box box = _page.Boxes.Make(sf.Box.Index);
            (double ox, double oy) = _page.Scroll.GetOffset(_page.Fragments, box);
            double dx = NearestDelta(target.X, target.Right, port.X, port.Right);
            double dy = NearestDelta(target.Y, target.Bottom, port.Y, port.Bottom);
            if ((dx != 0 || dy != 0) && SetContainerScroll(s, ox + dx, oy + dy))
            {
                any = true;
            }
        }

        if (_hits.ViewportRange.CanScroll)
        {
            Rect target = _queries.ViewportRectOf(element);
            ViewportOffset(out double vx, out double vy);
            double dx = NearestDelta(target.X, target.Right, 0, _options.ViewportWidth);
            double dy = NearestDelta(target.Y, target.Bottom, 0, _options.ViewportHeight);
            if ((dx != 0 || dy != 0) && ScrollViewportTo(vx + dx, vy + dy))
            {
                any = true;
            }
        }

        return any ? InputOutcome.Scrolled : InputOutcome.None;
    }

    // CSSOM View, "scroll an element into view", behavior nearest: edge A is the start edge and edge B the end edge.
    private static double NearestDelta(double start, double end, double portStart, double portEnd)
    {
        bool aOut = start < portStart;
        bool bOut = end > portEnd;
        double elementSize = end - start;
        double portSize = portEnd - portStart;
        if (aOut && bOut)
        {
            return 0;
        }

        if ((aOut && elementSize < portSize) || (bOut && elementSize > portSize))
        {
            return start - portStart;
        }

        if ((aOut && elementSize > portSize) || (bOut && elementSize < portSize))
        {
            return end - portEnd;
        }

        return 0;
    }

    // ------------------------------------------------------------------ wheel

    /// <summary>
    /// Handles a wheel input: fires a cancelable <c>wheel</c> event at the element under the pointer and, when no listener cancels it,
    /// scrolls the nearest scroll container that can move in each direction, passing what a container at its limit cannot take to the
    /// next container up and finally to the viewport. Scroll positions go to <see cref="Scroll"/> (no relayout) and <c>scroll</c>
    /// events fire for each container that moved.
    /// </summary>
    /// <param name="input">The wheel input.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows.</param>
    /// <returns>What happened.</returns>
    public InputOutcome HandleWheel(in WheelInput input, CancellationToken cancellationToken = default)
    {
        // A wheel turn anywhere but over the open drop-down's list closes it, as scrolling the page does in a browser.
        InputOutcome closed = InputOutcome.None;
        if (_page.Form.OpenIndex >= 0 && _hits.HitTest(input.X, input.Y, out HitResult over))
        {
            closed = CloseDropdownOnOutsidePress(over.Node, cancellationToken);
        }

        return closed | HandleWheelCore(in input, cancellationToken);
    }

    private InputOutcome HandleWheelCore(in WheelInput input, CancellationToken cancellationToken)
    {
        _lastX = input.X;
        _lastY = input.Y;
        _lastButtons = input.Buttons;
        _lastModifiers = input.Modifiers;
        _lastTime = input.Time;
        _havePointer = true;
        _lastInputWasPointer = true;
        if (!_hits.HitTest(input.X, input.Y, out HitResult hit) || hit.Node.IsNull)
        {
            return InputOutcome.None;
        }

        InputOutcome outcome = SetHovered(hit.Node, new PointerInput { Action = PointerAction.Move, X = input.X, Y = input.Y, Buttons = input.Buttons, Modifiers = input.Modifiers, Time = input.Time });
        WheelEvent e = _we;
        e.Reset("wheel", true, true, input.Time);
        e.Detail = 0;
        e.ClientX = input.X;
        e.ClientY = input.Y;
        e.ScreenX = input.X;
        e.ScreenY = input.Y;
        ViewportOffset(out double vx, out double vy);
        e.PageX = input.X + vx;
        e.PageY = input.Y + vy;
        e.Button = 0;
        e.Buttons = input.Buttons;
        e.Modifiers = input.Modifiers;
        e.RelatedTarget = default;
        e.DeltaX = input.DeltaX;
        e.DeltaY = input.DeltaY;
        e.DeltaZ = 0;
        e.DeltaMode = input.Mode;
        if (!_events.Dispatch(e, EventTarget.Of(hit.Node)))
        {
            return outcome | InputOutcome.DefaultPrevented | FlushStates(cancellationToken);
        }

        double dx = ToPixels(input.DeltaX, input.Mode, _options.ViewportWidth);
        double dy = ToPixels(input.DeltaY, input.Mode, _options.ViewportHeight);
        if ((input.Modifiers & KeyModifiers.Shift) != 0 && dx == 0)
        {
            // The usual convention: Shift turns the vertical wheel into a horizontal one.
            dx = dy;
            dy = 0;
        }

        if (ScrollChain(hit.Fragment.Index, dx, dy))
        {
            outcome |= InputOutcome.Scrolled | RefreshHover();
        }

        return outcome | FlushStates(cancellationToken);
    }

    private double ToPixels(double delta, WheelDeltaMode mode, double pageSize) => mode switch
    {
        WheelDeltaMode.Line => delta * _io.LineScrollPixels,
        WheelDeltaMode.Page => delta * Math.Max(_io.LineScrollPixels, pageSize * _io.PageScrollFraction),
        _ => delta,
    };

    /// <summary>
    /// Scrolls, starting at a fragment, the first scroll container up its containing-block chain that can move in each direction
    /// (CSS Overflow 3: only <c>auto</c> and <c>scroll</c> are user-scrollable), then the viewport. A container already at its limit in
    /// the direction of a delta passes that delta on.
    /// </summary>
    private bool ScrollChain(int start, double dx, double dy)
    {
        bool moved = false;
        for (int cur = start; cur >= 0 && (dx != 0 || dy != 0); cur = _hits.ContainingOf(cur))
        {
            if (!_hits.IsScrollContainer(cur))
            {
                continue;
            }

            ScrollRange r = _hits.RangeOf(cur);
            Box box = _page.Boxes.Make(_page.Fragments.Make(cur).Box.Index);
            ref readonly ComputedValues v = ref box.Values;
            (double ox, double oy) = _page.Scroll.GetOffset(_page.Fragments, box);
            bool takeX = dx != 0 && v.OverflowX is OverflowKind.Auto or OverflowKind.Scroll && CanMove(ox, dx, r.MinX, r.MaxX);
            bool takeY = dy != 0 && v.OverflowY is OverflowKind.Auto or OverflowKind.Scroll && CanMove(oy, dy, r.MinY, r.MaxY);
            if (!takeX && !takeY)
            {
                continue;
            }

            double nx = takeX ? ox + dx : ox;
            double ny = takeY ? oy + dy : oy;
            if (takeX)
            {
                dx = 0;
            }

            if (takeY)
            {
                dy = 0;
            }

            moved |= SetContainerScroll(cur, nx, ny);
        }

        if ((dx != 0 || dy != 0) && _hits.ViewportRange.CanScroll)
        {
            ViewportOverflow.Resolve(_page.Boxes, out OverflowKind vox, out OverflowKind voy, out _);
            ScrollRange r = _hits.ViewportRange;
            ViewportOffset(out double ox, out double oy);
            bool takeX = dx != 0 && vox != OverflowKind.Hidden && CanMove(ox, dx, r.MinX, r.MaxX);
            bool takeY = dy != 0 && voy != OverflowKind.Hidden && CanMove(oy, dy, r.MinY, r.MaxY);
            if (takeX || takeY)
            {
                moved |= ScrollViewportTo(takeX ? ox + dx : ox, takeY ? oy + dy : oy);
            }
        }

        return moved;
    }

    private static bool CanMove(double offset, double delta, double min, double max) =>
        max > min && (delta > 0 ? offset < max : offset > min);
}
