# Layout conventions

Places where the specifications leave room, or where the engine follows what Chrome does and the specification text is looser. Each entry says what is done, the spec text it rests on, and the evidence. A port can reproduce flow layout from these notes and the specifications.

Evidence is Chrome 156 (headless, fonts as in `geometry-differential.md`) measured through the geometry differential or a one-page probe; `testdata/text-block/` holds the pages.

## User-agent defaults the shared sheet lacks

**Body margin.** `body { margin: 8px }`, applied as a user-origin rule so author rules and presentational hints override it. HTML §15.3.2 (The page): when no `marginwidth`/`leftmargin`/`marginheight`/`topmargin` attribute gives the body's margins "a default value of 8px is expected to be used". Pages that do not reset it shift every box by 8 px.

## Fonts

**Generic `monospace` size.** A `font-family` list that is exactly the generic keyword `monospace` takes a size 13/16 of what the other lists take, wherever the size comes from a keyword (the default `medium` included). `monospace, monospace` and `"Courier New", monospace` are lists and keep the full size. CSS Fonts 4 §2.5 leaves the keyword table to the user agent.

- A size given as a length (`font-size: 20px`, `1.2em`) is never scaled, and a length size that is only inherited keeps its value when the family changes.
- An inherited keyword size is recomputed in the child's own family, so a proportional `span` inside a monospace block returns to the full size.
- Keyword sizes inside a bare monospace list scale by the same 13/16 (the table is 9, 10, 13, 16, 18, 24, 32, 48 px at medium 16 px).
- Evidence: `<code>` and `<pre>` heights on `essay`; `<span style="font-family:monospace">` inside `font-size:20px` is 20 px (no scaling), inside a keyword size it is scaled.

**Relative sizes and weights do not compound.** `font-size` given as `em`, `%`, `larger` or `smaller`, and `font-weight: bolder`/`lighter`, are resolved once, against the parent, on the element that declares them. A descendant that only inherits takes the parent's computed value (CSS Values 4 §7.1, CSS Cascade 4 §7.3). Evidence: `field-notes` (`blockquote { font-size: 1.05em }` around a paragraph).

## Text

**`white-space` through ancestors.** The `white-space` shorthand on an ancestor sets `white-space-collapse` and `text-wrap-mode` on it; both inherit, so inline elements and anonymous boxes inside keep preserved newlines, tabs and no-wrap (CSS Text 4 §3, CSS Text 3 §4.1.1). Evidence: `preformatted`, `<pre><code>`.

**Preserved newlines.** A preserved segment break always ends a line, and one on a line of its own leaves an empty line as tall as the strut (the same as `<br>`). A trailing newline does not add a line (CSS Text 3 §4.1.3). Evidence: `preformatted` (`pre-line` with a blank line).

**`text-transform`.** `none`, `capitalize`, `uppercase`, `lowercase`, by simple one-to-one case mapping per UTF-16 unit with the invariant culture, plus `ß` to `SS` for `uppercase`; every output character keeps the source index of the character it came from. `capitalize` puts the first letter of a word in upper case; a word continues over letters, digits and an apostrophe between letters, and across inline-element boundaries. Not implemented: `full-width`, `full-size-kana`, special casing beyond `ß`, language-specific mappings. Evidence: `terms` (uppercase headings and a `text-transform: uppercase` paragraph change where lines wrap).

## Flow

**Empty block with `clear`.** CSS2 §9.5.2 says clearance "potentially" arises. For a block with no content and `height: auto` or `0` whose `clear` names a side that has floats in its formatting context, margins do not collapse through it, whether or not the floats reach it (CSS2 §8.3.1 requires "no clearance" for collapsing through; Chrome applies the rule whenever floats are present on that side). With the box placed at its hypothetical position or just below the floats (§9.5.2), the margins after it run from its top margin edge:

    next.y = box.y − margin-top + max(margin-top, margin-bottom, next.margin-top)

With `margin-top: 0` that is the familiar margin before plus the larger of the margin after and the next box's margin. As the last child of its parent the plain rule holds (the parent reaches the box's border edge).

Evidence: thirteen probe pages (float above, float reaching, margins 0/1/10/40, no float, no clear, adjacent float, new formatting context after it) all agree to the pixel; four are committed as `testdata/text-block/clear-*`. WPT `clear-clearance-calculation-003` and `margin-collapse-018`/`-027` hold under the same rule.

## Geometry grid

**Line heights are cut to 1/64 px.** A used `line-height` that is not a whole number of 1/64 px (1.6 × 16 px = 25.6 px) is rounded down to the 1/64 px grid before the line box is built: 25.59375 px, not 25.6. Three such lines are 76.78125 px; without the rule they are 76.8 px, and over a long page the 0.02 px per paragraph adds up to whole pixels (the `report` and `statement` templates were off from the first mismatching paragraph to the end). Other lengths are not quantized: after this rule `templates` agrees with the browser to the tolerance everywhere else, so the engine keeps doubles. `LayoutUnits.Floor` is the one place the grid is written.

Evidence: `testdata/templates/report` (Carlito 16 px / 1.6 inside `p`: browser heights 76.78 for three lines, 25.59 per `li`), `testdata/templates/statement`; before the rule 1,117 and 330 elements of the two templates were off, after it none.

## Line breaking where the browser differs from UAX #14

OneFont follows UAX #14. The browser breaks at a few more places and at a few fewer; the engine adjusts the opportunities after OneFont returns them (`InlineWorker.AdjustOpportunities`). Each row was measured as the width of `width: min-content` text in Chrome 156 (`testdata/chrome-probes/breaks`, `slash`).

| Text | Browser | UAX #14 | Engine |
|---|---|---|---|
| `INV-2026-0418`, `1-2`, `Q1-2026`, `Net-30` (hyphen after a letter or digit, digits next) | breaks after each hyphen | no break (LB25 HY × NU) | breaks after the hyphen when the character before it is a letter or digit |
| `-5`, `£12-3` start, `1.5%-2` (hyphen as a sign: at the start of a word or after `%`, `$`, `£`) | no break | no break | no break |
| `a/b`, `and/or`, `a/1`, `http://x.test/a/b`, `dir/file.txt` | no break | break after `/` before a letter | no break after `/` when a letter, digit or `/` follows |
| `a\|b`, `a!b` | no break | break after `\|`, break after `!` | no break when a letter or digit follows |
| `a?b`, `a-b`, `a;b` | break after `?` and `-` | same | unchanged |

A URL therefore wraps after a `?` or a hyphen and nowhere else, and a long path overflows its box instead of breaking (CSS Text 3 §5.5 `overflow-wrap` still applies).

Not adjusted: `a--b` (the browser breaks between the hyphens).

## Tables and cells

**`align` on a cell does not reach a table inside it.** `td align=center` centers the cell's content (HTML §15.3.8 "align descendants to the center") and places a table child by auto margins, but text inside that nested table starts over with the initial `text-align`. Email templates rely on it: a `td align=center` wrapping a left-aligned content table. Evidence: `testdata/templates/email`, the footer link was 74 px to the right before the rule.

## Box sizing

**An over-wide box has no negative auto margin.** When a box with `margin-left: auto` (or right) is wider than its container, the auto margin is 0, not negative (CSS2 §10.3.3: if the width plus margins exceed the container, auto margins count as zero). A right-aligned table 320 px wide in a 278 px container starts at the container's left edge. Evidence: `templates/invoice` at 390 px.

**Aspect ratio on flex containers.** A flex container with `aspect-ratio`, a definite width and an auto height takes its height from the ratio; its content height is the automatic minimum unless the box scrolls or has an explicit `min-height` (`min-height: 0` included), which turns that minimum off (CSS Sizing 4 §5). Block containers follow the same rule. Evidence: `chrome-probes/aspect` (an `avatar` of a 36 px wide flex box with a one-line label: 36 px tall, not 20 px).

**Baseline of an inline flex or grid container.** It sits on the baseline of its first item (Flexbox §8.5, Grid §11.6) whatever its `overflow`, and an item without a baseline contributes the bottom edge of its border box. The line box around such a container is then no taller than the container when the first item's baseline lies above the strut's descender. This also holds when the container is only measured, as the content of a flex item. Evidence: `chrome-probes/ifx`, `ifx2`, `menu` (a menu `ul` of `display: inline-flex` in a navbar: 49 px, not 56 px; a stepper `display: inline-grid; overflow: auto` followed by a block: no 7 px descender).

## Columns

**Balanced columns start from an equal share.** The balanced column height is the larger of the smallest height at which the content fits and an equal share of everything in the multicol container, trailing margin included; nothing trails the columns after that. Two columns of 9 items of 25.59 px with 4 px margins are 266.34 px high (the share, margin included); 18 items split 9/9 over 390 px columns where the second column is the taller one end at the last item's border edge, 4 px less than a content-plus-margin measure. Evidence: `templates/report`, the contents list at 1280 px and 390 px.

## Custom properties

**`@property` initial values.** A custom property registered with `@property` and an `initial-value` has that value on every element (on the root, for `inherits: true`) until a declaration changes it. The cascade library does not read `@property`, so the engine feeds the initial values back as a user-origin sheet (`*, *::before, *::after { --x: v }` or `:root { --x: v }`), which any author declaration overrides. Registrations are read from top-level rules and from `@layer` and `@supports` blocks; at most 4,096 are used, values over 4,096 characters or containing `{`, `}` or `;` are ignored. Tailwind 4 declares `--tw-border-style: solid` this way, so without the rule every `border` utility draws no border. Evidence: `tailwind/flowbite-components` (cards lost 1 px of border on every side).

## HTML presentation

**Closed `details`.** Everything in a `details` without `open` except its first `summary` has no box (HTML §15.5.5: the content slot is `content-visibility: hidden`). The engine applies `details:not([open]) > :not(summary:first-of-type) { display: none !important }` as a user-origin rule so an author's `display` on the hidden content cannot reveal it. Evidence: `tailwind/daisy-showcase` (a menu's dropdown `ul`).

## Known deviations kept

- **Initial quote and a space.** Chrome breaks after `« ` at the start of a text run; UAX #14 (LB15a, Unicode 15.1) does not, and OneFont follows the standard. A `float` whose text starts with `«` is wider in the engine in narrow containers. Pages avoid the pattern.
- **Generated content** (`::before`/`::after`, `q` quotes, `list-style-position: inside` markers, `::first-line`) is not laid out yet; pages in `text-block` avoid it.
- **Form controls** (`input`, `select`, `textarea`, `button`, `fieldset`, `progress`) have no intrinsic size or inner layout yet; `option` children of a `select` get boxes. The remaining mismatches of the `tailwind` set are all controls or the boxes that move after one.
- **Transforms in the differential.** The browser reports a transformed element's box after the transform; the engine reports the layout box. `templates/receipt` has one such element (`scaleY(1.6)`).
