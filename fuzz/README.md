# Fuzzing

`fuzz/OneRender.Fuzz` holds the fuzz targets; SharpFuzz instruments `OneRender.dll` and libFuzzer drives it.

| Target | Input | Surface |
|---|---|---|
| `displaylist` | a mode byte and display list edit commands | the retained display list, patching and the raster backend |
| `grid` | random grid pages built from the bytes: templates, areas, placements, alignment, subgrids, absolutely positioned children | grid layout end to end; boxes stay finite and non-negative |
| `flow` | random flow pages: nested blocks, inline boxes and text with floats, clear, negative margins, sizes and limits, positioning, overflow, white space modes, text transforms, line breaks, font sizes and families | block and inline layout end to end; boxes stay finite and non-negative |
| `table` | random table pages built from the bytes: HTML tables and `display: table-*` boxes mixed (anonymous table objects), column groups and columns with huge spans, captions, header and footer groups, fixed, percentage and auto widths, separate and collapsing borders with conflicts, collapsed rows and columns, vertical alignment, nested tables | table layout end to end; boxes stay finite and non-negative |
| `effects` | random pages whose boxes carry transforms, perspective, filters, backdrop filters, clip paths, masks, blend modes and inline svg, with valid, extreme and malformed values (and raw bytes as a value) | the effect value parsers, scope nesting in the painter and the rasterizer's layers, masks and filters; the render must finish |
| `incremental` | the bytes choose a random styled-node page and up to 48 edits (declarations, states, text, inserts, removals, bursts of several inserts and removals before one flush, moves, viewport changes, including transforms, filters, clip paths, masks and blending); a bit of the first byte picks the profile that mostly inserts and removes, so freed node slots are reused all the time; after each flush the tree must hold exactly the reachable nodes and every removed node's handle must be stale | incremental restyle, box patching, subtree reuse in layout and the retained display list: after every edit the page must equal a page built from scratch |
| `paged` | random paged documents built from the bytes: `@page` rules with sizes, margins, selectors and margin boxes whose content uses counters, strings, targets and leaders, named pages, every kind of forced and avoided break, tables with repeated headers and footers, `box-decoration-break`, reversed counters, `display: contents` | pagination, page layers and generated content end to end; page count bounded, geometry finite, only typed exceptions |
| `pdf` | the `displaylist` inputs, plus page size, margins and profile bytes | display list to PDF: pages of hostile sizes, every item kind, tags, islands; the file must reopen with OnePdf or the write must fail with the typed exception |
| `fontdata` | a mode byte, then a font file, `@font-face` descriptor text plus a font, CSS property text, or a `data:` URL payload | font loading (TrueType, OpenType, WOFF, WOFF2 with the glyf and hmtx transforms), `@font-face` and font property parsing, matching, fallback, shaping, the shaped-run cache; the clusters must tile the text and the segments the glyphs |
| `colorglyphs` | a u16 glyph id, a u16 length, that many bytes of `CBLC`, then the rest as `CBDT` (seeds: the emoji font real tables) | the color bitmap glyph reader (strikes, index subtables of formats 1 and 3, image formats 17 and 18): a returned bitmap lies inside the data table with positive sizes; nothing escapes |

Nothing may escape the library on hostile input except an `ArgumentException` for text over the shaper's limit. Any other exception, a failed invariant, a hang (`-timeout`) or a memory blow-up (`-rss_limit_mb`) is a finding.

## Running

    export ONERENDER_FUZZ_TOOLS=/path/to/tools   # holds fuzz/libfuzzer-dotnet and sharpfuzz/sharpfuzz
    export ONERENDER_FUZZ_WORK=/path/to/workdir  # builds, corpora, findings; must be outside the checkout
    fuzz/run.sh fontdata 600                     # fuzz for 10 minutes
    fuzz/run.sh replay fontdata some-input...    # run saved inputs once, without libFuzzer

`run.sh` builds the harness in release mode into `$ONERENDER_FUZZ_WORK/bin`, writes seeds (the committed web fonts and sample descriptors) into `$ONERENDER_FUZZ_WORK/corpus/fontdata/`, instruments `OneRender.dll` and runs libFuzzer. Findings land in `$ONERENDER_FUZZ_WORK/findings/fontdata/`.

The tools: `libfuzzer-dotnet` is the C++ host from the SharpFuzz project (`clang++ -fsanitize=fuzzer`); `sharpfuzz` is `dotnet tool install SharpFuzz.CommandLine --tool-path <dir>/sharpfuzz`. Run one fuzz process at a time.

## Findings

Minimize the input, save it under `testdata/fuzz/` (under 1 MB, from our own or redistributable files), and add a test that feeds it and asserts a typed result.
