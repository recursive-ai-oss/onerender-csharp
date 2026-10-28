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

## Themed controls (`color-scheme`, `accent-color`)

Every value below was read from Chrome 156.0.8078.47's rendering and computed styles (`tools/forms/scheme-shots.py`, `shot-page.py`, `scheme-styles.py`, `run-scheme-probes.sh`; probe pages in `testdata/forms-probes/scheme/`). Chrome's palette is not in any specification; a port reproduces these numbers.

**Used scheme (CSS Color Adjust 1 §2.2).** `color-scheme` is inherited. The used scheme is dark when the list names `dark` and either does not name `light` or the host prefers dark; `normal`, unknown names and `only light` give light. `only` may come first or last, once; `normal` stands alone; CSS-wide keywords inside the list are invalid. A page that does not opt in with `color-scheme` stays light under a dark preference. The host's preference is `HtmlPageOptions.PrefersDarkColorScheme` (also `HtmlRenderOptions.PrefersDarkColorScheme` in `.Image`), or `StyleTable.PrefersDarkScheme` for builder trees; it also drives `@media (prefers-color-scheme)`.

**System colors** resolve when the value is computed, under the element's own used scheme; an inherited `color` keeps the parent's computed value (a dark subtree of a light page keeps black text until a keyword such as `CanvasText` is written there). `light-dark()` follows the element's used scheme. The root's initial `color` is `CanvasText` of its scheme.

| Color | light | dark |
|---|---|---|
| Canvas (and Window, Background, Menu ...) | `#fff` | `#121212` |
| CanvasText (WindowText ...) | `#000` | `#fff` |
| Field | `#fff` | `#3b3b3b` |
| FieldText, ButtonText | `#000` | `#fff` |
| ButtonFace (ThreeDFace ...) | `#efefef` | `#6b6b6b` |
| ButtonBorder | `#000` | `#fff` |
| GrayText | `#808080` | `#808080` |
| Highlight | `rgba(0,65,198,.8)` | same |
| HighlightText, AccentColorText | `#fff` | `#fff` |
| Mark / MarkText | yellow / black | same |
| AccentColor | `#0075ff` | `#0075ff` |
| ActiveText | red | red |
| LinkText | `#0000ee` | `#9e9eff` |
| VisitedText | `#551a8b` | `#d0adf0` |
| SelectedItem / SelectedItemText | `#1967d2` / `#fff` | `#99c8ff` / `#3b3b3b` |

**Canvas.** When the root element (not the body) is dark and has no background of its own, the canvas is filled `#121212` (a `FillRect` with item id `(0,5,0)`; the display list format is unchanged).

**Native look per scheme** (light is the table above):

| Part | light | dark |
|---|---|---|
| Field, textarea, menu select, listbox: face / border / text | `#fff` / `#767676` / black | `#3b3b3b` / `#858585` / white |
| Placeholder text | `#757575` | `#757575` |
| Button: face / border / text | `#efefef` / `#767676` / black | `#6b6b6b` / `#6b6b6b` / white |
| Button active face | `#f5f5f5` | `#616161` |
| Button hover | unchanged in light (not tinted in Chrome's headless rendering) | none |
| Disabled field: face / border / text | as before | `#3b3b3b` / `#626262` / `#aaa` |
| Disabled button: face / border / text | `#eee` | `#363636` / same as face / `rgba(255,255,255,.3)` |
| Disabled select: face / border / text | as before | `#2e2e2e` / `#4a4a4a` / `rgba(255,255,255,.38)` |
| Listbox selected option, list unfocused / disabled | `#cecece` / `#dcdcdc` | `#545454` / `#404040` |
| Listbox selected option, list focused | SelectedItem pair | SelectedItem pair |
| Disabled listbox: face / border | white / `#e2e2e2` | `#2e2e2e` / `#3b3b3b` |
| Textarea resizer strokes (dark, light) | `#666`, white (+1 px x) | `#171717`, `#b0b0b0` |
| Checkbox, radio unchecked: face / border | `#fff` / `#767676` | `#3b3b3b` / `#858585` |
| Disabled checkbox/radio: face / border / checked fill / mark | `#f8f8f8` / `#d1d1d1` / `#d1d1d1` / `#ededed` | `#3b3b3b` / `#626262` / `#757575` / `#3b3b3b` |
| Range, progress track: fill / border | `#efefef` / `#b2b2b2` | `#3b3b3b` / `#858585` |
| Disabled range track: fill / border / value | `#fafafa` / `#d2d2d2` / `#cbcbcb` | `#363636` / `#626262` / `#757575` |
| Automatic accent | `#0075ff` | `#99c8ff` |
| Meter: track border / optimum / suboptimum / bad | `#cbcbcb` / `#107c10` / `#ffb900` / `#d83b01` | `#858585` / `#74b374` / `#f2c812` / `#e98f6d` |
| Fieldset border | `#efefef`-based groove | `#bfbfbf`-based groove |
| Select popup (ours: Chrome's is a native window) face / border / highlighted option | white / `#767676` / `#0075ff` + white | `#3b3b3b` / `#858585` / `#99c8ff` + `#3b3b3b` |

A field or button whose border or background the author set keeps the author's values (the native look is only used while they are the user agent's). Meter ignores `accent-color`. Disabled controls ignore `accent-color`.

**Accent rules.** Contrast is the WCAG relative-luminance ratio. Measured with grey and colour sweeps; the flip points are exact.

- *Mark of a checked checkbox.* Light scheme: white when contrast(accent, white) is at least 3 (`#949494` yes, `#959595` no), else `#3b3b3b`. Dark scheme: `#3b3b3b` when contrast(accent, `#3b3b3b`) is at least 3 (`#848484` no, `#858585` yes), else white.
- *Track of a range and progress.* Light scheme: the light track (`#efefef` / `#b2b2b2`) when contrast(accent, `#efefef`) is at least 3, else the dark track (`#3b3b3b` / `#858585`) (flip `#8a8a8a` / `#8b8b8b`). Dark scheme: the dark track when contrast(accent, `#3b3b3b`) is at least 3, else the light track (flip `#848484` / `#858585`).
- *Ring over the filled part* (range and progress): the 1 px track border is drawn over the accent fill. On a light track it is the mix `((accent + 118) >> 1)` per 8-bit channel (`#0075ff` gives `#3b75ba`); on a dark track it is `#858585`.
- *Radio checked:* 1 px accent ring, 2 px of the face colour, a 7 px accent dot.

**Focus ring (`outline: auto`).** An inner 2 px ring (`#101010` light, `#eeeeee` dark) plus a 1 px outer ring just outside it (white light, `#101010` dark); offsets as above.

**`::placeholder`.** The placeholder takes `color` from a matching `::placeholder` rule; without one it is `#757575` under both schemes, also when disabled (Chrome's computed colour of the pseudo-element equals the control's, but it paints `#757575`).

**Styling a range.** Code: `Tree/HtmlFrontEnd.Range.cs` (the parts), `Layout/Block/BlockLayoutEngine.Positioned.cs` (`RelativeOffset`, the thumb's shift), `Painting/Painter.Controls.cs`, `Interaction/InteractivePage.Forms.cs` (`RangeFromPointer`). Everything below was measured on Chrome 156 (CDP screenshots and `getBoundingClientRect`; the parts themselves are in a shadow tree that no geometry snapshot lists, so their boxes were read from pixels), with Liberation Sans standing in for Arial. The specifications give the part names (CSS Forms 1 §4.2: `::slider-track`, `::slider-thumb`, `::slider-fill`) and leave the box structure to the browser.

- **Structure.** Inside the input, three boxes with no element of their own: a container, the track in it and the thumb in the track. The container is a flex container with `align-items: center` and `height: 100%`; the track (OneHtml's user-agent sheet gives it `display: block; box-sizing: border-box; flex: 1 1 0%; align-self: center`) is its only item; the thumb (`display: block; box-sizing: border-box`) is the track's only child. In the styled tree they are `SliderContainer`, `SliderTrack` and `SliderThumb` nodes; hit testing and events resolve them to the input.
- **Size.** A range is 129 px wide (content box) under either appearance, and as high as its track, which is as high as its thumb's margin box unless the author gives the track a `height`. A track's own `width` has no effect (it is a flex item with a zero basis); its `margin`, `border` and `padding` do, as for any border-box block. An input taller than its track centers the track, and a shorter one lets the track overflow at both ends equally (a 16 px thumb in an 8 px input hangs 4 px out of each side without any margin).
- **Thumb position.** A block at the top of the track's content box, then shifted horizontally by `value fraction × (track content width − thumb border-box width)`. The thumb's margins do not enter the travel: `margin-left: 5px` moves it 5 px, `margin-right` does nothing. `margin-top: -4px` lifts it 4 px, which is how an 8 px track centers a 16 px thumb. A thumb with `width: auto` fills the track and has no travel. In a right-to-left range the fraction is measured from the right.
- **Appearance.** With the input's `appearance: auto` the native look decides: the input draws a native track (8 px, 1 px in from the border box, filled to the thumb in the accent color) and the thumb is native whatever its own `appearance` or size says (16 px, accent circle); the input's own border and background are not painted (their space stays; its box-shadow and outline are), and a styled track is drawn over the native one. With the input's `appearance: none` the input draws nothing, a styled track and thumb are plain boxes, and a thumb whose `appearance` is still `auto` stays native. `-webkit-appearance` is the same property.
- **State.** Parts are not dimmed for `disabled`: the author's thumb keeps its colors (a rule like `input:disabled::-webkit-slider-thumb` is how an author changes them). The thumb inherits from the track (`color`, so `background: currentColor` on the thumb takes the track's color) and the track from the input.
- **The fill.** Chrome 156 has no box for it: `::slider-fill` and `::-moz-range-progress` rules paint nothing, and so here. (OneHtml still serves the style.)
- **Rules that depend on state** (`input:hover::-webkit-slider-thumb`) are restyled in place. OneHtml drops its cached part styles on every state change when any such rule exists, so a hover costs about 800 bytes in the cascade's own recomputation (two ranges on the page); with rules that read no state the parts' styles are cached and a hover allocates nothing.
- **Drag.** The pointer sets the thumb's center, from half a thumb inside the track content box's start edge to half a thumb inside its end edge, using the thumb as laid out.

**Range gate set.** `testdata/range-parts/`: custom sliders (Tailwind-style and plain), native with styled parts, the parts' box model, states, ranges in flow (baseline, flex and grid), the dark scheme. `tools/forms/run-forms.sh range-parts` (geometry; Chrome's snapshot lists only the input box) and `tools/geomdiff/scripts/run-pixels.sh range-parts ~/onerender/corpus/range-parts results/pixels` (SSIM). Results are in `results/geometry/range-parts` and `results/pixels/range-parts`.

**Known differences.** The fieldset groove: our groove paints the border colour as the light half and shades the dark half at 2/3, so a dark-scheme fieldset border is computed as `#bfbfbf` where Chrome reports `#6b6b6b` and shades `#161616` (the box is the same size). Button hover is not tinted in Chrome's headless rendering, so light hover is the pre-existing guess. The select popup is ours.

**Gate set.** `testdata/forms-themed/` (generated by `tools/forms/gen-themed.py`): light, dark, `light dark`, nested schemes, accent colours under both schemes, disabled controls, `outline: auto` rings. Run it twice: `tools/forms/run-forms.sh forms-themed` and, with the host preferring dark, `GEOMDIFF_PREFERS_DARK=1 SRC_DIR=testdata/forms-themed tools/forms/run-forms.sh forms-themed-dark` (the variable makes Chrome emulate `prefers-color-scheme: dark` and the engine side set `PrefersDarkColorScheme`); pixel similarity with `tools/geomdiff/scripts/run-pixels.sh` and the same variable. Results are in `results/geometry/forms-themed*` and `results/pixels/forms-themed*`.

## Interaction (`InteractivePage`)

- **Activation:** a click toggles a checkbox or radio before the `click` listeners run (legacy pre-activation, reverted when `preventDefault` is called), then fires `input` and `change`. Radios in one group (same form owner and `name`) uncheck each other. A click on a `label` activates its control. Disabled controls do nothing.
- **Radio groups and Tab.** A radio button group (same form owner and non-empty `name`; a nameless radio is its own group) is one tab stop (HTML sequential focus navigation, radio button group). Tab lands on the group's checked button, or, when none is checked, on the first button going forward and the last going backward (Shift+Tab). Checkedness is read live, so `SetChecked`, a click or an arrow key moves the stop; the `checked` attribute only supplies the initial state. Arrow Down/Right move focus to the next enabled button of the group and Up/Left to the previous one, wrapping, and check it with a synthetic click (so `click`, `input` and `change` fire); the arrow's scroll is suppressed. Disabled buttons are skipped.
- **Buttons:** a submit button fires `submit` (a `SubmitEvent` with `submitter`) after constraint validation; invalid controls get an `invalid` event and the submit is cancelled; `novalidate` skips validation. A reset button restores defaults and fires `reset`. Nothing navigates.
- **Range:** pointer drag and the arrow keys (one `step`), Page Up and Page Down (a tenth of the range) and Home and End change the value and fire `input`; `change` follows when the drag ends or the key is released.
- **Select:** a click (or Space, Arrow Down and Arrow Up on the focused select) opens the popup; in the open list the arrows, Home and End move the highlight, Enter or Space or a click chooses and fires `input` and `change`, and Escape, a click outside or a wheel scroll closes it. Typing characters moves the highlight to the next option whose label starts with the typed prefix (type-ahead; the prefix resets after 1 second of host time). A listbox selects on click and arrow keys; Ctrl/Cmd-click toggles one option, Shift-click or Shift+Arrow extends from the anchor, and Ctrl+A selects all in a `multiple` list.
- **Popup (top layer):** a box under the select, as wide as it. It is painted after everything else in the root stacking context, outside every ancestor clip and stacking context, and the hit tester tries it first. Chrome draws this list in a native window that screenshots do not capture, so its look is ours.
- **State and selectors:** the user's changes live in `FormState`; the rendered content, `form.inputChecked`, `inputValue` and `optionSelected` facts follow it. Every change (a click, an arrow key in a radio group, `SetChecked`, `SetIndeterminate`, a selected option, a form reset) is also passed to OneHtml's live cascade as a form-state override (`LiveDocumentStyles.ApplyFormState`), so `:checked`, `:indeterminate`, `:disabled`, `:enabled`, `:read-only` and `:read-write` (and what depends on them: `:checked + label`, `li:has(:checked)`, a disabled `fieldset` and the controls in it, the other radios of a group) restyle in place and equal a page parsed with that state in its attributes. `:checked` follows checkedness, not the `checked` attribute. A `disabled` attribute edit (`SetAttribute`) is an edit of the document snapshot, which the cascade reads directly. `:default`, `:required`, `:placeholder-shown` and the validity pseudo-classes keep reading attributes. A restyle for a form state change allocates nothing once warm; a pointer click also allocates its DOM event objects. A text control's `::selection` colors (`color`, `background-color`) are read from the cascade when editing starts.

## Text editing

Text-like inputs (text, password, email, number, search, url, tel) and `textarea` are editable. A control's content is a value text node (always present, possibly empty) plus a placeholder node that shows text only while the value is empty. `InteractivePage` owns one editing session per control; the builder path uses `LiveTextEditor` with the same engine and no DOM events.

- **Caret and selection.** The caret is a 1 px bar in `caret-color` (`auto` is the text color), blinking with a 500 ms half period on host time (`InteractivePage.Advance`; `NextCaretBlinkTime` says when the next toggle is due, and any edit or move shows the caret). Focusing a field by script puts the caret at 0; tabbing into a single-line input selects all its text. A click places the caret, a double click selects a word, a triple click the line (the whole value in a single-line input), and a drag extends from the press point. Arrows move by grapheme cluster, Ctrl/Alt+Arrow by word (to the end of the word, as on Linux), Home and End to the line ends, Ctrl+Home/End to the value ends, and Shift extends. Ctrl or Cmd+A selects all. Up and Down move between lines in a textarea and keep the goal column.
- **Editing.** Typing, Backspace and Delete (by cluster, or by word with Ctrl/Alt), Enter in a textarea, `maxlength` (counted in UTF-16 units; paste is truncated), `readonly` (selection and copy only) and `disabled` (nothing). Newlines are normalized to `\n`; a single-line field drops them. An `<input type=number>` accepts only digits, one `.`, signs and `e`. A password field draws one bullet per UTF-16 unit and never copies or cuts its text. Undo and redo (Ctrl/Cmd+Z, Shift+Z or Y) keep up to 2,048 entries and 1,048,576 characters; typing runs and deletion runs coalesce into one entry.
- **Scrolling.** A single-line field scrolls horizontally to keep the caret visible; a textarea wraps (`white-space: pre-wrap`) and scrolls vertically.
- **Clipboard.** The host sets `InteractivePage.Clipboard` (an `ITextClipboard`); Ctrl/Cmd+C, X, V and the `Copy`/`Cut`/`Paste` methods call it, fire `copy`/`cut`/`paste` events (cancelable), and the engine touches nothing else.
- **IME.** The host calls `CompositionStart`, `CompositionUpdate(preedit, caret, targetStart, targetEnd)` any number of times, then `CompositionEnd(committed)` (an empty string cancels). The preedit is spliced into the drawn text with an underline (2 px for the target clause, 1 px otherwise); `TryGetCompositionRect` and `TryGetCaretRect` return the rectangles in page coordinates for the candidate window. Keys during a composition are ignored by the editor.
- **Events.** Per UI Events and Input Events 2: `keydown`, then `beforeinput` (cancelable; `inputType`, `data`, `isComposing`), the edit, `input`, then `keyup`. A composition fires `compositionstart`, `beforeinput` (insertCompositionText), `compositionupdate`, `input`, and ends with `compositionend`. `change` fires when a field loses focus or Enter is pressed with a changed value; `select` and `selectionchange` fire when the selection moves. Enter in a single-line input submits through the form's default button.
- **Paint.** Selection, caret and preedit underline are ordinary `FillRect` items with stable ids (`ControlId(fragment, 10..18)`) inside a clip and a translate for the field window, so no display-list item kind was added and the format version is unchanged. The selected text is drawn again in the selection foreground over its fill when one is set. The default selection fill is `#3390ff` (not probed against Chrome); `TextEditView.SelectionBackground`/`SelectionForeground` set it, because OneHtml 1.2.0 has no `::selection` pseudo-element yet.
- **Limits.** Caret movement and selection follow logical order, not visual order, in mixed-direction text. The arrow keys do not step a number input. A trailing newline in a textarea does not add a blank line to the scroll height. Date, time, color and file pickers, `datalist` and PDF form fields are not built; neither are hover and active button colors, the suboptimum meter color, or the popup's look beyond ours.

## Allocation

Toggling a checkbox and hovering a button on a builder-made tree (`StyledTree.SetControl`, `SetStateStyle`) allocate nothing once warm (test `TogglingACheckboxAndHoveringAButtonOnABuilderTreeAllocateNothing`). On an HTML page the state change goes through OneHtml's element-state update, which allocates until its non-allocating variant is available. Typing a character allocates nothing once warm, on an HTML page, on the 1,000-node HUD page and on a builder tree (`TextEditingTests`, `GameUiIncrementalTests`); it takes about 0.7 ms on the HUD page in a release build.
