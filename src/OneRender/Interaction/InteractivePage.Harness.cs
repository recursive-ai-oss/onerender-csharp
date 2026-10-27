using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

public sealed partial class InteractivePage
{
    /// <summary>
    /// Finds an element by name, for test harnesses: the first element in document order whose <c>id</c> is <paramref name="name"/>, else the
    /// first form control (<c>button</c>, <c>fieldset</c>, <c>input</c>, <c>object</c>, <c>output</c>, <c>select</c>, <c>textarea</c>) whose
    /// <c>name</c> attribute is <paramref name="name"/>. Comparison is exact. Allocates nothing.
    /// </summary>
    /// <param name="name">The id or form-control name.</param>
    /// <returns>The element, or the null node when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Node FindElement(string name) => DocumentNames.Find(_page.Document, name);

    /// <summary>
    /// Activates an element as HTML's <c>click()</c> does (HTML Standard §6.6.4 "fire a synthetic pointer event"): a <c>click</c> event (a
    /// <see cref="PointerEvent"/> with <c>detail</c> 0 and no pointer position) goes to the element along the normal path (capture, target,
    /// bubble), and when no listener cancels it the element's activation runs: a checkbox or radio button toggles (before the event, as the
    /// HTML pre-activation steps say, and back if a listener cancels), a label activates its control, a <c>select</c> opens or closes. No
    /// <c>pointerdown</c>, <c>mousedown</c>, <c>pointerup</c> or <c>mouseup</c> fire, nothing is hit-tested and focus does not move, so the
    /// result does not depend on layout, scrolling or what covers the element. A disabled form control does nothing, as <c>click()</c> says.
    /// </summary>
    /// <param name="element">An element of the current <see cref="Page"/>'s document; find it with <see cref="FindElement"/>.</param>
    /// <param name="time">The host's time in milliseconds, used as the event time stamp. The engine reads no clock.</param>
    /// <param name="cancellationToken">Cancels a restyle that follows the click.</param>
    /// <returns>What happened, so the host knows what to repaint.</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> is not an element of this page's document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled while restyling.</exception>
    public InputOutcome Click(Node element, double time, CancellationToken cancellationToken = default)
    {
        if (!element.IsElement || !ReferenceEquals(element.Document, _page.Document))
        {
            throw new ArgumentException("The node is not an element of this page's document.", nameof(element));
        }

        if (IsDisabledControl(element))
        {
            return InputOutcome.None;
        }

        InputOutcome outcome = Activate(element, time);
        return outcome | FlushStates(cancellationToken);
    }
}
