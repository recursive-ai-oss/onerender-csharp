using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

/// <summary>A listener callback. A script runtime wraps its functions in this delegate; C# code passes lambdas.</summary>
/// <param name="e">The event. For events the engine creates it is only valid during the call.</param>
public delegate void EventListener(DomEvent e);

/// <summary>Options of <see cref="EventDispatcher.AddEventListener"/> (DOM Standard §2.7, <c>AddEventListenerOptions</c>).</summary>
public readonly record struct EventListenerOptions
{
    /// <summary>Listen in the capturing phase rather than the bubbling phase. Default false.</summary>
    public bool Capture { get; init; }

    /// <summary>Remove the listener after its first call. Default false.</summary>
    public bool Once { get; init; }

    /// <summary>The listener promises not to call <see cref="DomEvent.PreventDefault"/>; the call is ignored. Default false.</summary>
    public bool Passive { get; init; }
}

/// <summary>
/// The listener registry and the dispatch algorithm of DOM Standard §2.9 for one document: capture from the window down, the
/// target, bubble back up, with <c>stopPropagation</c>, <c>stopImmediatePropagation</c>, <c>preventDefault</c>, <c>once</c> and
/// <c>passive</c> as DOM defines them. It has no events of its own; the interactive page uses it for input and a host or a
/// script runtime uses it for the rest, so there is one listener model.
/// </summary>
/// <remarks>
/// Listeners added during a dispatch do not run in that dispatch's current target; listeners removed during it do not run if they have not yet
/// (DOM Standard §2.10 "inner invoke": the list is cloned, and removed listeners are skipped). The path is capped at
/// <see cref="MaxPathLength"/> targets: for a deeper element only the nearest ancestors below the cap are on the path, followed by the
/// document and the window, so a hostile tree cannot make one event expensive. Dispatch allocates nothing once the path buffer is sized.
/// </remarks>
public sealed class EventDispatcher
{
    /// <summary>The longest event path, target and window included. 2,048.</summary>
    public const int MaxPathLength = 2048;

    private struct Entry
    {
        public string Type;
        public EventListener Callback;
        public bool Capture;
        public bool Once;
        public bool Passive;
        public bool Removed;
    }

    private sealed class Listeners
    {
        public Entry[] Items = new Entry[4];
        public int Count;
        public int Running;
        public bool Dirty;
    }

    private readonly Document _document;
    private Listeners?[] _byNode = new Listeners?[16];
    private Listeners? _window;
    private EventTarget[] _pathBuffer = new EventTarget[32];
    private int _depth;

    /// <summary>Creates a dispatcher for one document.</summary>
    /// <param name="document">The document whose nodes are targets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public EventDispatcher(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    /// <summary>The document.</summary>
    public Document Document => _document;

    /// <summary>Registers a listener. Adding the same type, callback and capture setting again does nothing (DOM Standard §2.7).</summary>
    /// <param name="target">The node or window to listen on.</param>
    /// <param name="type">The event type.</param>
    /// <param name="listener">The callback.</param>
    /// <param name="options">Capture, once and passive; defaults are all false.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The target is null or belongs to another document.</exception>
    public void AddEventListener(EventTarget target, string type, EventListener listener, EventListenerOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(listener);
        Listeners list = GetOrCreate(target);
        for (int i = 0; i < list.Count; i++)
        {
            ref Entry e = ref list.Items[i];
            if (!e.Removed && e.Capture == options.Capture && string.Equals(e.Type, type, StringComparison.Ordinal) && e.Callback.Equals(listener))
            {
                return;
            }
        }

        if (list.Running == 0 && list.Dirty)
        {
            Compact(list);
        }

        if (list.Count == list.Items.Length)
        {
            Array.Resize(ref list.Items, list.Items.Length * 2);
        }

        list.Items[list.Count++] = new Entry { Type = type, Callback = listener, Capture = options.Capture, Once = options.Once, Passive = options.Passive };
    }

    /// <summary>Removes a listener added with the same type, callback and capture setting.</summary>
    /// <param name="target">The node or window.</param>
    /// <param name="type">The event type.</param>
    /// <param name="listener">The callback.</param>
    /// <param name="capture">The capture setting it was added with.</param>
    /// <returns>True when a listener was removed.</returns>
    public bool RemoveEventListener(EventTarget target, string type, EventListener listener, bool capture = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(listener);
        Listeners? list = Find(target);
        if (list is null)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ref Entry e = ref list.Items[i];
            if (!e.Removed && e.Capture == capture && string.Equals(e.Type, type, StringComparison.Ordinal) && e.Callback.Equals(listener))
            {
                RemoveAt(list, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>True when any listener of <paramref name="type"/> is registered on <paramref name="target"/>.</summary>
    /// <param name="target">The node or window.</param>
    /// <param name="type">The event type.</param>
    public bool HasListeners(EventTarget target, string type)
    {
        Listeners? list = Find(target);
        if (list is null)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (!list.Items[i].Removed && string.Equals(list.Items[i].Type, type, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Dispatches an event at a target (DOM Standard §2.9) and runs the listeners on the path.
    /// </summary>
    /// <param name="e">The event. Its flags are cleared first, as dispatching an event twice is allowed once the first dispatch ended.</param>
    /// <param name="target">The node or window to dispatch at.</param>
    /// <returns>False when the event is cancelable and a listener canceled it, true otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is null.</exception>
    /// <exception cref="ArgumentException">The target is null or belongs to another document.</exception>
    /// <exception cref="InvalidOperationException">The event is already being dispatched.</exception>
    public bool Dispatch(DomEvent e, EventTarget target)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.IsDispatching)
        {
            throw new InvalidOperationException("The event is already being dispatched.");
        }

        CheckTarget(target);
        e.ClearFlagsAfterDispatch();
        e.Target = target;
        int count = BuildPath(target, e.Type);
        EventTarget[] path = _pathBuffer;
        e.SetPath(path, count);
        e.IsDispatching = true;
        _depth++;
        try
        {
            // DOM §2.9 step 5.9: capturing pass from the outermost target down; the target itself runs its capture listeners with
            // eventPhase AT_TARGET.
            for (int i = count - 1; i >= 0 && !e.StopPropagationFlag; i--)
            {
                e.Phase = i == 0 ? EventPhase.AtTarget : EventPhase.Capturing;
                Invoke(path[i], e, capturing: true);
            }

            // Step 5.10: bubbling pass, from the target outward; a non-bubbling event runs only at the target.
            for (int i = 0; i < count && !e.StopPropagationFlag; i++)
            {
                if (i == 0)
                {
                    e.Phase = EventPhase.AtTarget;
                }
                else if (!e.Bubbles)
                {
                    break;
                }
                else
                {
                    e.Phase = EventPhase.Bubbling;
                }

                Invoke(path[i], e, capturing: false);
            }
        }
        finally
        {
            _depth--;
            e.IsDispatching = false;
            e.Phase = EventPhase.None;
            e.CurrentTarget = default;
            e.ClearPath();
            e.ClearFlagsAfterDispatch();
        }

        return !e.DefaultPrevented;
    }

    // DOM §2.10 "inner invoke": the listeners that exist now are visited; removed ones are skipped, added ones lie beyond the snapshot.
    private void Invoke(EventTarget target, DomEvent e, bool capturing)
    {
        Listeners? list = Find(target);
        if (list is null || list.Count == 0)
        {
            return;
        }

        e.CurrentTarget = target;
        int snapshot = list.Count;
        list.Running++;
        try
        {
            for (int i = 0; i < snapshot; i++)
            {
                ref Entry entry = ref list.Items[i];
                if (entry.Removed || entry.Capture != capturing || !string.Equals(entry.Type, e.Type, StringComparison.Ordinal))
                {
                    continue;
                }

                EventListener callback = entry.Callback;
                if (entry.Once)
                {
                    RemoveAt(list, i);
                }

                e.Passive = entry.Passive;
                try
                {
                    callback(e);
                }
                finally
                {
                    e.Passive = false;
                }

                if (e.StopImmediateFlag)
                {
                    break;
                }
            }
        }
        finally
        {
            list.Running--;
            if (list.Running == 0 && list.Dirty)
            {
                Compact(list);
            }
        }
    }

    // Walks parents without recursion. Past the cap the middle of the chain is dropped so the document and window stay on the path.
    private int BuildPath(EventTarget target, string type)
    {
        int n = 0;
        Add(ref n, target);
        if (target.IsWindow)
        {
            return n;
        }

        bool reachedDocument = target.Node.Kind == NodeKind.Document;
        if (!reachedDocument)
        {
            for (Node p = target.Node.Parent; !p.IsNull; p = p.Parent)
            {
                if (p.Kind == NodeKind.Document)
                {
                    Add(ref n, EventTarget.Of(p));
                    reachedDocument = true;
                    break;
                }

                if (n < MaxPathLength - 2)
                {
                    Add(ref n, EventTarget.Of(p));
                }
            }
        }

        // DOM §2.9 get the parent of a Document: the window, except for load events.
        if (reachedDocument && !string.Equals(type, "load", StringComparison.Ordinal))
        {
            Add(ref n, EventTarget.Window);
        }

        return n;
    }

    private void Add(ref int n, EventTarget t)
    {
        if (n == _pathBuffer.Length)
        {
            Array.Resize(ref _pathBuffer, Math.Min(MaxPathLength, _pathBuffer.Length * 2));
        }

        _pathBuffer[n++] = t;
    }

    private void CheckTarget(EventTarget target)
    {
        if (target.IsNull)
        {
            throw new ArgumentException("The target is the null target.", nameof(target));
        }

        if (!target.IsWindow && !ReferenceEquals(target.Node.Document, _document))
        {
            throw new ArgumentException("The target belongs to another document.", nameof(target));
        }
    }

    private Listeners GetOrCreate(EventTarget target)
    {
        CheckTarget(target);
        if (target.IsWindow)
        {
            return _window ??= new Listeners();
        }

        int index = target.Node.Index;
        if (index >= _byNode.Length)
        {
            Array.Resize(ref _byNode, Math.Max(index + 1, _byNode.Length * 2));
        }

        return _byNode[index] ??= new Listeners();
    }

    private Listeners? Find(EventTarget target)
    {
        if (target.IsWindow)
        {
            return _window;
        }

        int index = target.Node.Index;
        return (uint)index < (uint)_byNode.Length ? _byNode[index] : null;
    }

    private static void RemoveAt(Listeners list, int i)
    {
        list.Items[i].Removed = true;
        list.Dirty = true;
        if (list.Running == 0)
        {
            Compact(list);
        }
    }

    private static void Compact(Listeners list)
    {
        int w = 0;
        for (int r = 0; r < list.Count; r++)
        {
            if (!list.Items[r].Removed)
            {
                list.Items[w++] = list.Items[r];
            }
        }

        Array.Clear(list.Items, w, list.Count - w);
        list.Count = w;
        list.Dirty = false;
    }
}
