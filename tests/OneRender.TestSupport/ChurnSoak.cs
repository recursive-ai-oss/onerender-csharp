using System;
using System.Collections.Generic;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.TestSupport;

/// <summary>
/// Long-running churn on the game-UI page: every frame a floating layer of about fifty nodes (damage numbers) is added and the previous
/// one removed, and a chat line is appended while the oldest is dropped, each followed by a flush. A running game does this for hours;
/// the tree's slots, text arena and style caches must stay the size of what is live.
/// </summary>
public sealed class ChurnSoak
{
    private const int Rows = 16;
    private const int Variants = 64;

    private readonly bool _fresh;
    private readonly StyledTree _tree;
    private readonly StyledNode _chat;
    private readonly StyleDeclaration _layer, _chatLine, _chatName, _chatText;
    private readonly StyleDeclaration[] _rows = new StyleDeclaration[Variants];
    private readonly StyleDeclaration[] _cells = new StyleDeclaration[Variants];
    private readonly string[] _numbers = new string[Variants];
    private readonly StyledNode _mover;
    private readonly StyledNode _moverLabel;
    private StyledNode _previous;
    private int _frame;

    /// <summary>Builds the page.</summary>
    /// <param name="fonts">The text service.</param>
    /// <param name="moving">True to add an element whose style takes a new value every frame (its <c>left</c> is the frame number in px, its label's width is the frame number modulo 997), the way a host moves an element or sizes a timer label through styles. Every such value is a style never seen before.</param>
    /// <param name="fresh">True to give every inserted node a declaration made for it, as a host that builds styles on the fly does; false to share a fixed set, which is what a host that keeps its styles does.</param>
    public ChurnSoak(IFontProvider fonts, bool fresh, bool moving = false)
    {
        _fresh = fresh;
        _tree = new StyledTree();
        GameUiScene.SceneNodes nodes = GameUiScene.Populate(_tree);
        Page = new LivePage(_tree, new LivePageOptions(fonts) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height });
        _chat = nodes["quest3"].Parent.Parent.Parent.LastChild.LastChild; // the chat log of the bottom bar
        _layer = GameUiScene.Declaration("position:absolute;left:400px;top:120px;width:300px;height:300px;display:flex;flex-direction:column;");
        if (moving)
        {
            _mover = _tree.AddElement(_tree.Root, "div", GameUiScene.Declaration("position:absolute;left:0px;top:40px;width:120px;height:30px;"));
            _tree.AddText(_mover, "m");
            _moverLabel = _tree.AddElement(_mover, "span", GameUiScene.Declaration("width:1px;height:10px;"));
            _tree.AddText(_moverLabel, "t");
            Page.Flush();
        }

        _chatLine = GameUiScene.Declaration("display:flex;gap:6px;height:16px;font-size:12px;");
        _chatName = GameUiScene.Declaration("display:block;color:#7fd09a;white-space:nowrap;");
        _chatText = GameUiScene.Declaration("display:block;white-space:nowrap;");
        for (int i = 0; i < Variants; i++)
        {
            _rows[i] = Row(i);
            _cells[i] = Cell(i);
            _numbers[i] = "-" + (100 + (i * 37 % 900)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The page.</summary>
    public LivePage Page { get; }

    /// <summary>The styled tree.</summary>
    public StyledTree Tree => _tree;

    /// <summary>Frames run so far.</summary>
    public int Frames => _frame;

    private static StyleDeclaration Row(int i) => GameUiScene.Declaration($"display:flex;height:16px;margin-left:{i}px;color:hsl({i * 5},70%,60%);font-size:13px;");

    private static StyleDeclaration Cell(int i) => GameUiScene.Declaration($"display:block;opacity:{(i % 10) / 10.0 + 0.05:0.00};");

    private StyledNode BuildLayer(int f)
    {
        StyledNode layer = _tree.AddElement(_tree.Root, "div", _layer);
        for (int i = 0; i < Rows; i++)
        {
            int v = (f + (i * 5)) % Variants;
            StyledNode row = _tree.AddElement(layer, "div", _fresh ? Row(v) : _rows[v]);
            StyledNode cell = _tree.AddElement(row, "span", _fresh ? Cell(v) : _cells[v]);
            _tree.AddText(cell, _numbers[v]);
        }

        return layer;
    }

    /// <summary>Adds the floating layer (about fifty nodes) and flushes. Does nothing when it is already there.</summary>
    public void InsertLayer()
    {
        if (_previous.IsNull)
        {
            _previous = BuildLayer(_frame++);
            Page.Flush();
        }
    }

    /// <summary>Removes the floating layer and flushes. Does nothing when it is not there.</summary>
    public void RemoveLayer()
    {
        if (!_previous.IsNull)
        {
            _tree.Remove(_previous);
            _previous = default;
            Page.Flush();
        }
    }

    /// <summary>Moves the floating layer to the other end of the root's children, or into the chat log and back, and flushes. Does nothing when it is not there.</summary>
    public void MoveLayer()
    {
        if (_previous.IsNull)
        {
            return;
        }

        _moved = !_moved;
        if (_moved)
        {
            _tree.Move(_previous, _chat.Parent);
        }
        else
        {
            _tree.Move(_previous, _tree.Root);
        }

        Page.Flush();
    }

    private bool _moved;

    /// <summary>Runs one frame: churn, then flush.</summary>
    public void Frame()
    {
        Edit();
        FlushEdits();
    }

    /// <summary>Runs the flush of a frame whose edits <see cref="Edit"/> made. Together they are <see cref="Frame"/>; apart, a test can measure the engine's share of a frame without the host's own work of building values.</summary>
    public void FlushEdits()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Page.Flush();
        FlushBytes += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Bytes the engine allocated in the flushes so far, leaving out the host's own work in <see cref="Edit"/> (building declarations, parsing values).</summary>
    public long FlushBytes { get; private set; }

    /// <summary>Makes one frame's edits without flushing.</summary>
    public void Edit()
    {
        int f = _frame++;
        if (!_mover.IsNull)
        {
            _tree.SetProperty(_mover, "left", Px(f));
            _tree.SetProperty(_moverLabel, "width", Px((f % 997) + 1));
        }

        if (!_previous.IsNull)
        {
            _tree.Remove(_previous);
        }

        _previous = BuildLayer(f);

        // One chat line in, the oldest out.
        _tree.Remove(_chat.FirstChild);
        StyledNode line = _tree.AddElement(_chat, "div", _chatLine);
        _tree.AddText(_tree.AddElement(line, "span", _chatName), _numbers[f % Variants]);
        _tree.AddText(_tree.AddElement(line, "span", _chatText), _numbers[(f + 3) % Variants]);
    }

    private static string Px(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";

    /// <summary>The nodes the tree holds, the most it has ever needed at once, the columns' capacity and the style rows.</summary>
    public string Describe() =>
        $"live {_tree.LiveCount}, slots {_tree.Count}, capacity {_tree.Capacity}, free {_tree.FreeSlots}, style rows {_tree.Styles.Count}, boxes {Page.Boxes.Count}, fragments {Page.Fragments.Count}";
}
