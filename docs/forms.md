# Form controls

Form controls are laid out and painted the way Chrome 156 does by default, so boxes and pixels agree with it on the same fonts. This page is the normative record: every number below was measured on Chrome 156 with Liberation Sans standing in for Arial (the same font files on both sides), as a black box through the geometry and pixel harnesses (`tools/forms/`, `testdata/forms-probes/`). The specifications leave all of it open: HTML §15.5 only says controls are replaced-like "widgets", and CSS UI 4 §6 (`appearance`) says that `appearance: none` turns the widget into an ordinary box.

Code: `src/OneRender/Forms/` (kinds, metrics, state), `Tree/HtmlFrontEnd.Forms.cs` (UA adjustments, fieldset split, select popup), `Painting/Painter.Controls.cs` (widget paint), `Interaction/InteractivePage.Forms.cs` (activation, events, select popup, range).

## Model

A control is an ordinary element box whose computed values are adjusted when the styled tree is built (`AdjustControlStyle`): its widget width and height are written into `width`/`height` when those are `auto`, the inner text is made single-line where Chrome does, and paint-only properties (`Control`, `ControlState`, `ControlFraction`) tell the painter what to draw. The widget size only stands in for `auto`: `WidthIsIntrinsic` and `HeightIsIntrinsic` keep stretching (grid, flex, both-inset absolute positioning) working. `appearance: none` leaves the box as plain CSS (a checkbox becomes 0 by 0). The value and placeholder are a synthetic text child (`RefreshContent` rebuilds it when the form state changes); a text editor plugs in there.

Text, password, email, number, search, url and tel inputs share one layout. Inputs without a native look (`hidden`) get no box.

## Sizes (border boxes; font size 13.3333px unless the author sets one)

| Control | Rule | Evidence |
|---|---|---|
| Text field content width | `floor((size - 1) * A + W + 0.75)`, A = OS/2 `xAvgCharWidth` / upem * font size (kept when its fraction is below 0.5, else rounded up), W = `head` (xMax - xMin) / upem * font size. `size` defaults to 20. Default font: 185 px border box. | `testdata/forms-probes/text*`, sizes 1..40 and font sizes 10..30 |
| Text field height | line (round(ascent) + round(descent) + round(line gap)) + 6 (2 px borders, 1 px padding). `height` smaller than the line is respected; the text is centered and overflows evenly. | probes `text`, WPT `css-overflow/input-scrollable-region-001` |
| Text field baseline | border + padding + round(ascent), also when empty | probe `text` |
| Textarea | width round(cols * A) + 15 + padding + border; height rows * line; default 20 x 2; monospace 13.3333px, padding 2, 1 px border | probe `text2` |
| Drop-down select | width ceil(widest option label) + 20 + padding + border; height round(ascent) + round(descent) + 4 | probe `select` |
| Listbox select | rows (`size`, or 4 when `multiple`) * (max(line, floor(1.2 em)) + 1); width widest label + 4 + 2 | probe `select` |
| Button, `input` button types | padding 1px 6px, 2 px outset border, `#efefef` face; shrink-to-fit to its label | probe `misc` |
| Checkbox, radio | 13 x 13; margins 3 3 3 4 (checkbox) and 3 3 0 5 (radio); baseline at the bottom border edge | probe `misc` |
| Range | 129 x 16, margin 2; baseline at the bottom border edge | probe `misc` |
| `progress` | 10 em x 1 em with its own `em`, `vertical-align: -0.2em` | probe `misc` |
| `meter` | 5 em x 1 em, same alignment | probe `misc` |
| `fieldset` | 2px groove `#efefef`, padding .35em .75em .625em; the legend is shrink-to-fit | probe `misc` |

A control stretches like an `auto` width in a grid column or flex cross axis (`WidthIsIntrinsic`). A range, textarea and select also stretch in height (`HeightIsIntrinsic`); a checkbox, radio, progress and meter keep their size. Probes: WPT `css-grid/alignment/grid-self-alignment-stretch-input-range`, `css-flexbox/flexitem-stretch-range`, `css-position/position-absolute-semi-replaced-stretch-*`.

A percentage `height` that cannot resolve behaves as `auto` (CSS Sizing 3 §5.2.1), so the control keeps its own height (a minimum height of the widget height does it); an inline-block's percentage height resolves against the height of the block container it sits in (CSS2 §10.5).

With both insets given and `auto` size, an absolutely positioned control fills the space between them (CSS Position 3 §5.3), text fields and buttons included.

**Centered content.** The text of a button, text field and drop-down is centered in the box when the box is taller than the text (a stretched item, `min-height`, an explicit height). With a length `height` the extra goes into the top padding at style time (probe: height 50 / 60 px puts the text 17.5 / 22 px below the top); otherwise layout moves the line boxes after the content is laid out. A text field also centers when the box is too short (the text overflows evenly at both ends).

## Colors and metrics (paint)

Chrome paints native widgets itself; these are the values read from its screenshots (`tools/forms/`, pixel dumps).

- **Text field, textarea, menu select, listbox:** the layout border is 2/1/1 px; the paint is a 1 px `#767676` border at the border-box edge, radius about 2, white face (a menu select's face is white although its computed background is `#efefef`).
- **Button:** 1 px `#767676` border, radius about 3, face `#efefef`. Hover `#e5e5e5` with border `#4f4f4f`, active `#f5f5f5` with `#464646` are not probed.
- **Disabled:** face `rgba(239,239,239,.3)`, border `rgba(118,118,118,.3)`, text `#545454`; a select's text about `#a5a5a5`, a button's about `#ababab`.
- **Select arrow:** a chevron of two 2 px strokes through (R-13, cy-3.2), (R-9, cy+1.6), (R-5, cy-3.2), R the right border edge, cy the vertical center. **Textarea resizer:** two 1 px `#666` lines, (R-3.5, B-9.5) to (R-9.5, B-3.5) and (R-3.5, B-5.5) to (R-5.5, B-3.5).
- **Checkbox:** 13 x 13, radius 2, 1 px `#767676` border, white; checked fills with the accent color (default `#0075ff`) and draws a white check (polyline about (3.2, 6.4), (5.5, 8.7), (9.7, 2.8), width 1.9; black when the accent is light). Disabled: face `#f8f8f8`, border `#d1d1d1`, checked `#d1d1d1` with a `#ededed` mark. A checkbox stretched by a grid column draws 13 px, centered.
- **Radio:** 13 px circle with a 1 px `#767676` ring; checked: 1 px accent ring, 2 px white, a 7 px accent dot.
- **Range:** the track is 8 px tall, centered, inset 1 px from each end, a pill with a 1 px `#b2b2b2` border and `#efefef` inside; the part up to the value is the accent color; the thumb is a 16 px accent circle whose center travels from left + 8 to right - 8. Disabled: `#cbcbcb`, `#fafafa`, `#d2d2d2`.
- **Progress:** the same track edge to edge, the fill reaching the border with a square end; indeterminate draws a chunk from 0.24 to 0.44 of the width. **Meter:** track border `#cbcbcb`, inside `#efefef`, fill inset 1 px: optimum `#107c10`, even-less-good `#d83b01` (the suboptimum yellow `#ffb900` is not probed).
- **`accent-color`:** `auto` is `#0075ff`; any color, `currentcolor` resolved against the control's own `color` (WPT `css-ui/accent-color-parent-currentcolor`).
- **Focus ring (`:focus-visible`):** 2 px `#101010`; its outer edge is at the border-box edge for boxes with a border (inputs, buttons, select, textarea, a bordered `div`), 1 px outside for borderless boxes; checkbox and radio use `outline-offset: 2px`; a range uses an outer offset of 1.
- **Fieldset:** the top border is drawn through the middle of the legend (legend top + floor((legend height - border) / 2)); the gap in it is exactly the legend's border box. A `display: grid | flex` fieldset becomes a block holding the legend plus an anonymous box that carries the original display and its grid or flex properties: the first row starts after the legend and padding, with no gap (daisyUI's fieldset, `tailwind/daisy-app`). A subgrid fieldset keeps its own grid (WPT `css-grid/subgrid/independent-formatting-context-fieldset`).

## Interaction (`InteractivePage`)

- **Activation:** a click toggles a checkbox or radio before the `click` listeners run (legacy pre-activation, reverted when `preventDefault` is called), then fires `input` and `change`. Radios in one group (same form owner and `name`) uncheck each other. A click on a `label` activates its control. Disabled controls do nothing.
- **Buttons:** a submit button fires `submit` (a `SubmitEvent` with `submitter`) after constraint validation; invalid controls get an `invalid` event and the submit is cancelled; `novalidate` skips validation. A reset button restores defaults and fires `reset`. Nothing navigates.
- **Range:** pointer drag and the arrow keys (one `step`), Page Up and Page Down (a tenth of the range) and Home and End change the value and fire `input`; `change` follows when the drag ends or the key is released.
- **Select:** a click (or Space, Arrow Down and Arrow Up on the focused select) opens the popup; in the open list the arrows, Home and End move the highlight, Enter or Space or a click chooses and fires `input` and `change`, and Escape or a click outside closes it. A listbox selects on click and arrow keys; modifier-extended multiple selection and type-ahead are not implemented.
- **Popup:** a box under the select, as wide as it, with the highest `z-index` of its stacking context. Chrome draws this list in a native window that screenshots do not capture, so its look is ours, and it is not a true top layer yet (an ancestor's `overflow` or a stacking context can still clip it).
- **State and selectors:** the user's changes live in `FormState`; the rendered content, `form.inputChecked`, `inputValue` and `optionSelected` facts follow it. `:checked`, `:disabled`, `:indeterminate` and `:placeholder-shown` selectors are evaluated by OneHtml against the parsed attributes and do not follow later user changes yet (OneHtml has no dynamic form state); the painter reads the live state, so the widget is right, but a `:checked` rule does not restyle after a click.

## Not in this version

Text editing (caret, selection, clipboard, IME) in text controls: the value and placeholder are drawn statically. Date, time, color and file pickers, `datalist`, PDF form fields. Hover and active colors of buttons, the suboptimum meter color and the popup's look are unprobed.

## Allocation

Toggling a checkbox and hovering a button on a builder-made tree (`StyledTree.SetControl`, `SetStateStyle`) allocate nothing once warm (test `TogglingACheckboxAndHoveringAButtonOnABuilderTreeAllocateNothing`). On an HTML page the state change goes through OneHtml's element-state update, which allocates until its non-allocating variant is available.
