namespace OneRender.Style;

/// <summary>The outer display type of a box (CSS Display 3 §2.1), plus the two values that generate no box.</summary>
public enum OuterDisplay : byte
{
    /// <summary>Block-level: the box takes part in a block formatting context.</summary>
    Block,

    /// <summary>Inline-level: the box takes part in an inline formatting context.</summary>
    Inline,

    /// <summary>A table-internal type (<c>table-row</c>, <c>table-cell</c>, ...); see <see cref="InnerDisplay"/> for which.</summary>
    TableInternal,

    /// <summary><c>display: none</c>: no box and no descendants' boxes.</summary>
    None,

    /// <summary><c>display: contents</c>: no box of its own, children are generated as if they were the parent's.</summary>
    Contents,
}

/// <summary>The inner display type of a box (CSS Display 3 §2.2) or, with <see cref="OuterDisplay.TableInternal"/>, the internal table role.</summary>
public enum InnerDisplay : byte
{
    /// <summary><c>flow</c>: block and inline layout.</summary>
    Flow,

    /// <summary><c>flow-root</c>: a block container that always establishes a new block formatting context.</summary>
    FlowRoot,

    /// <summary><c>table</c>.</summary>
    Table,

    /// <summary><c>flex</c>.</summary>
    Flex,

    /// <summary><c>grid</c>.</summary>
    Grid,

    /// <summary><c>ruby</c>. Boxes are generated; ruby layout is not part of the first version.</summary>
    Ruby,

    /// <summary><c>table-row-group</c>.</summary>
    TableRowGroup,

    /// <summary><c>table-header-group</c>.</summary>
    TableHeaderGroup,

    /// <summary><c>table-footer-group</c>.</summary>
    TableFooterGroup,

    /// <summary><c>table-row</c>.</summary>
    TableRow,

    /// <summary><c>table-cell</c>.</summary>
    TableCell,

    /// <summary><c>table-column-group</c>.</summary>
    TableColumnGroup,

    /// <summary><c>table-column</c>.</summary>
    TableColumn,

    /// <summary><c>table-caption</c>.</summary>
    TableCaption,
}

/// <summary>The <c>position</c> property (CSS Positioned Layout 3 §2).</summary>
public enum PositionKind : byte
{
    /// <summary><c>static</c> (initial).</summary>
    Static,

    /// <summary><c>relative</c>.</summary>
    Relative,

    /// <summary><c>absolute</c>.</summary>
    Absolute,

    /// <summary><c>fixed</c>.</summary>
    Fixed,

    /// <summary><c>sticky</c>.</summary>
    Sticky,
}

/// <summary>The <c>float</c> property (CSS2 §9.5.1). Logical <c>inline-start</c>/<c>inline-end</c> compute to left or right by direction.</summary>
public enum FloatKind : byte
{
    /// <summary><c>none</c> (initial).</summary>
    None,

    /// <summary><c>left</c>.</summary>
    Left,

    /// <summary><c>right</c>.</summary>
    Right,
}

/// <summary>The <c>clear</c> property (CSS2 §9.5.2).</summary>
public enum ClearKind : byte
{
    /// <summary><c>none</c> (initial).</summary>
    None,

    /// <summary><c>left</c>.</summary>
    Left,

    /// <summary><c>right</c>.</summary>
    Right,

    /// <summary><c>both</c>.</summary>
    Both,
}

/// <summary>The <c>box-sizing</c> property (CSS Sizing 3 §4.1).</summary>
public enum BoxSizingKind : byte
{
    /// <summary><c>content-box</c> (initial).</summary>
    ContentBox,

    /// <summary><c>border-box</c>.</summary>
    BorderBox,
}

/// <summary>A border style (CSS Backgrounds 3 §3.2).</summary>
public enum BorderStyleKind : byte
{
    /// <summary><c>none</c> (initial).</summary>
    None,

    /// <summary><c>hidden</c>.</summary>
    Hidden,

    /// <summary><c>dotted</c>.</summary>
    Dotted,

    /// <summary><c>dashed</c>.</summary>
    Dashed,

    /// <summary><c>solid</c>.</summary>
    Solid,

    /// <summary><c>double</c>.</summary>
    DoubleLine,

    /// <summary><c>groove</c>.</summary>
    Groove,

    /// <summary><c>ridge</c>.</summary>
    Ridge,

    /// <summary><c>inset</c>.</summary>
    Inset,

    /// <summary><c>outset</c>.</summary>
    Outset,
}

/// <summary>An <c>overflow-x</c>/<c>overflow-y</c> value (CSS Overflow 3 §3.1).</summary>
public enum OverflowKind : byte
{
    /// <summary><c>visible</c> (initial).</summary>
    Visible,

    /// <summary><c>hidden</c>.</summary>
    Hidden,

    /// <summary><c>clip</c>.</summary>
    Clip,

    /// <summary><c>scroll</c>.</summary>
    Scroll,

    /// <summary><c>auto</c>.</summary>
    Auto,
}

/// <summary>The <c>visibility</c> property (CSS2 §11.2).</summary>
public enum VisibilityKind : byte
{
    /// <summary><c>visible</c> (initial).</summary>
    Visible,

    /// <summary><c>hidden</c>.</summary>
    Hidden,

    /// <summary><c>collapse</c>.</summary>
    Collapse,
}

/// <summary>The <c>font-style</c> property (CSS Fonts 4 §2.3).</summary>
public enum FontStyleKind : byte
{
    /// <summary><c>normal</c> (initial).</summary>
    Normal,

    /// <summary><c>italic</c>.</summary>
    Italic,

    /// <summary><c>oblique</c>, with the angle in <see cref="ComputedValues.ObliqueAngle"/>.</summary>
    Oblique,
}

/// <summary>The keyword forms of <c>vertical-align</c> (CSS Inline 3 §4.1.1 values the first version covers).</summary>
public enum VerticalAlignKind : byte
{
    /// <summary><c>baseline</c> (initial).</summary>
    Baseline,

    /// <summary><c>sub</c>.</summary>
    Sub,

    /// <summary><c>super</c>.</summary>
    Super,

    /// <summary><c>text-top</c>.</summary>
    TextTop,

    /// <summary><c>text-bottom</c>.</summary>
    TextBottom,

    /// <summary><c>middle</c>.</summary>
    Middle,

    /// <summary><c>top</c>.</summary>
    Top,

    /// <summary><c>bottom</c>.</summary>
    Bottom,

    /// <summary>A length or percentage, in <see cref="ComputedValues.VerticalAlignOffset"/>.</summary>
    Length,
}

/// <summary>The <c>white-space-collapse</c> property (CSS Text 4 §4.1).</summary>
public enum WhiteSpaceCollapseKind : byte
{
    /// <summary><c>collapse</c> (initial).</summary>
    Collapse,

    /// <summary><c>preserve</c>.</summary>
    Preserve,

    /// <summary><c>preserve-breaks</c>.</summary>
    PreserveBreaks,

    /// <summary><c>preserve-spaces</c>.</summary>
    PreserveSpaces,

    /// <summary><c>break-spaces</c>.</summary>
    BreakSpaces,
}

/// <summary>The <c>text-wrap-mode</c> property (CSS Text 4 §5.1).</summary>
public enum TextWrapModeKind : byte
{
    /// <summary><c>wrap</c> (initial).</summary>
    Wrap,

    /// <summary><c>nowrap</c>.</summary>
    NoWrap,
}

/// <summary>The <c>text-align</c> property (CSS Text 3 §7.1).</summary>
public enum TextAlignKind : byte
{
    /// <summary><c>start</c> (initial).</summary>
    Start,

    /// <summary><c>end</c>.</summary>
    End,

    /// <summary><c>left</c>.</summary>
    Left,

    /// <summary><c>right</c>.</summary>
    Right,

    /// <summary><c>center</c>.</summary>
    Center,

    /// <summary><c>justify</c>.</summary>
    Justify,

    /// <summary><c>match-parent</c>, resolved against the parent's direction at computed-value time.</summary>
    MatchParent,

    /// <summary><c>justify-all</c>.</summary>
    JustifyAll,
}

/// <summary>The <c>direction</c> property (CSS Writing Modes 4 §2.1).</summary>
public enum DirectionKind : byte
{
    /// <summary><c>ltr</c> (initial).</summary>
    Ltr,

    /// <summary><c>rtl</c>.</summary>
    Rtl,
}

/// <summary>The <c>unicode-bidi</c> property (CSS Writing Modes 4 §2.2).</summary>
public enum UnicodeBidiKind : byte
{
    /// <summary><c>normal</c> (initial).</summary>
    Normal,

    /// <summary><c>embed</c>.</summary>
    Embed,

    /// <summary><c>isolate</c>.</summary>
    Isolate,

    /// <summary><c>bidi-override</c>.</summary>
    BidiOverride,

    /// <summary><c>isolate-override</c>.</summary>
    IsolateOverride,

    /// <summary><c>plaintext</c>.</summary>
    Plaintext,
}

/// <summary>The <c>list-style-position</c> property (CSS Lists 3 §3.2).</summary>
public enum ListStylePositionKind : byte
{
    /// <summary><c>outside</c> (initial).</summary>
    Outside,

    /// <summary><c>inside</c>.</summary>
    Inside,
}

/// <summary>How a <c>content</c> value generates boxes (CSS Generated Content 3 §2).</summary>
public enum ContentKind : byte
{
    /// <summary><c>normal</c> (initial): an element renders its children; a pseudo-element generates nothing.</summary>
    Normal,

    /// <summary><c>none</c>: a pseudo-element generates nothing.</summary>
    None,

    /// <summary>A list of strings only, joined in <see cref="ComputedValues.ContentText"/>.</summary>
    Text,

    /// <summary>Something that needs more than strings (<c>counter()</c>, <c>attr()</c>, <c>url()</c>, <c>open-quote</c>): the pseudo-element generates an empty box until generated-content support lands.</summary>
    Other,
}

/// <summary>What kind of replaced content an element carries (HTML Standard §15.4).</summary>
public enum ReplacedKind : byte
{
    /// <summary>Not a replaced element.</summary>
    None,

    /// <summary>An <c>img</c> or an <c>input type=image</c>.</summary>
    Image,

    /// <summary>A <c>video</c>.</summary>
    Video,

    /// <summary>A <c>canvas</c>.</summary>
    Canvas,

    /// <summary>An <c>iframe</c>, <c>embed</c> or <c>object</c>.</summary>
    Embedded,

    /// <summary>Inline SVG.</summary>
    Svg,
}

/// <summary>The <c>word-break</c> property (CSS Text 3 §5.2).</summary>
public enum WordBreakKind : byte
{
    /// <summary><c>normal</c>.</summary>
    Normal,

    /// <summary><c>break-all</c>: any typographic letter unit may break.</summary>
    BreakAll,

    /// <summary><c>keep-all</c>: no breaks inside CJK words.</summary>
    KeepAll,

    /// <summary><c>break-word</c>: <c>normal</c> plus <c>overflow-wrap: anywhere</c>.</summary>
    BreakWord,
}

/// <summary>The <c>overflow-wrap</c> property (CSS Text 3 §5.5).</summary>
public enum OverflowWrapKind : byte
{
    /// <summary><c>normal</c>.</summary>
    Normal,

    /// <summary><c>break-word</c>: an otherwise unbreakable word may break, but only when nothing else fits.</summary>
    BreakWord,

    /// <summary><c>anywhere</c>: like <c>break-word</c>, and the break points count for min-content.</summary>
    Anywhere,
}

/// <summary>The <c>line-break</c> property (CSS Text 3 §5.3).</summary>
public enum LineBreakKind : byte
{
    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>loose</c>.</summary>
    Loose,

    /// <summary><c>normal</c>.</summary>
    Normal,

    /// <summary><c>strict</c>.</summary>
    Strict,

    /// <summary><c>anywhere</c>: a break is allowed after every typographic character unit.</summary>
    Anywhere,
}

/// <summary>The <c>hyphens</c> property (CSS Text 3 §6.1).</summary>
public enum HyphensKind : byte
{
    /// <summary><c>manual</c>: only at U+00AD and U+2010-style explicit hyphens.</summary>
    Manual,

    /// <summary><c>none</c>: no breaks at soft hyphens either.</summary>
    None,

    /// <summary><c>auto</c>.</summary>
    Auto,
}

/// <summary>The <c>text-align-last</c> property (CSS Text 3 §7.2).</summary>
public enum TextAlignLastKind : byte
{
    /// <summary><c>auto</c>: follows <c>text-align</c>, with <c>justify</c> falling back to <c>start</c>.</summary>
    Auto,

    /// <summary><c>start</c>.</summary>
    Start,

    /// <summary><c>end</c>.</summary>
    End,

    /// <summary><c>left</c>.</summary>
    Left,

    /// <summary><c>right</c>.</summary>
    Right,

    /// <summary><c>center</c>.</summary>
    Center,

    /// <summary><c>justify</c>.</summary>
    Justify,

    /// <summary><c>match-parent</c>; resolved against the parent when the style is computed.</summary>
    MatchParent,
}

/// <summary>The <c>text-justify</c> property (CSS Text 3 §7.3).</summary>
public enum TextJustifyKind : byte
{
    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>none</c>: no justification.</summary>
    None,

    /// <summary><c>inter-word</c>.</summary>
    InterWord,

    /// <summary><c>inter-character</c>.</summary>
    InterCharacter,
}
