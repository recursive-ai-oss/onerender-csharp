# Fuzzing

`fuzz/OneRender.Fuzz` holds the fuzz targets; SharpFuzz instruments `OneRender.dll` and libFuzzer drives it.

| Target | Input | Surface |
|---|---|---|
| `displaylist` | a mode byte and display list edit commands | the retained display list, patching and the raster backend |
| `fontdata` | a mode byte, then a font file, `@font-face` descriptor text plus a font, CSS property text, or a `data:` URL payload | font loading (TrueType, OpenType, WOFF, WOFF2 with the glyf and hmtx transforms), `@font-face` and font property parsing, matching, fallback, shaping, the shaped-run cache; the clusters must tile the text and the segments the glyphs |

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
