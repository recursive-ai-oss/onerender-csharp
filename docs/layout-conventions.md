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

## Known deviations kept

- **Initial quote and a space.** Chrome breaks after `« ` at the start of a text run; UAX #14 (LB15a, Unicode 15.1) does not, and OneFont follows the standard. A `float` whose text starts with `«` is wider in the engine in narrow containers. Pages avoid the pattern.
- **Generated content** (`::before`/`::after`, `q` quotes, `list-style-position: inside` markers, `::first-line`) is not laid out yet; pages in `text-block` avoid it.
