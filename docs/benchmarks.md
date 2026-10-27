# Interactive benchmarks and the gate

The interactive bar, measured on a 1,000-node game-UI page (`testdata/game-ui/hud.html`, 1280×720):

- a typical change takes at most 1 ms for restyle, relayout and display-list update together, and allocates 0 B;
- a frame in which nothing changed allocates 0 B and does next to no work;
- transform and opacity animations patch the display list without laying anything out;
- the engine is no slower than OneGame.UI on the same changes.

## Running the gate

On the benchmark host, from the repository root:

```
tools/bench/run-interactive-gate.sh            # the gate: takes the host's benchmark lock, publishable
tools/bench/run-interactive-gate.sh --smoke    # short, no lock: proves the harness runs, numbers are not publishable
```

The script builds both benchmark projects in Release, then runs BenchmarkDotNet with fixed settings (gate: 15 warm-up and 40 measured
iterations of 200 ms in 3 launches; smoke: 5 and 10 in one launch), pinned with `taskset` to `GATE_CORES` (default `8-15`; smoke uses
`SMOKE_CORES`, default `0-7`, at low priority). The gate takes `~/WORKER-BENCH.lock` atomically and releases it on exit; it never
waits, and exits with status 3 and the holder's line when the lock is held. It needs the .NET 10 SDK (`DOTNET_DIR`, default `~/dotnet10`).
`ONERENDER_WORK` (default `~/onerender`) is where build output and results go, and `GATE_COMMIT` is recorded in the machine record.

## Reading the results

The results folder (`$ONERENDER_WORK/bench/<mode>-<time>/`) holds:

- `machine.txt`: CPU model, logical cores, pinned cores, governor, SDK, BenchmarkDotNet arguments, and the load average with the busiest
  processes before the run, every 30 s during it and after it. A gate run is publishable only if nothing else heavy shows up there.
- `summary.md`: two tables.
  - *Bar*: every engine benchmark with its mean in microseconds and allocated bytes; PASS needs a mean of at most 1000 µs and 0 B.
  - *Comparison*: the same four changes through OneGame.UI and through the engine; the last column says whether the engine is no slower.
  - *Like for like*: OneGame.UI's own flex-only page through the engine's builder front end (see below), next to OneGame.UI's numbers, with the
    ratio, and the scene check that ran first (`equivalence.txt`).
- `<project>/BenchmarkDotNet.Artifacts/results/`: BenchmarkDotNet's markdown, CSV, HTML and JSON per benchmark class, and `<project>/run.log`.

The benchmarks:

| Class | Benchmark | What one call does |
|---|---|---|
| `IncrementalBenchmarks` | `IdleFrame` | `LivePage.Flush` with nothing pending |
| | `HoverRestyle` | moves hover from one inventory slot to another, then flushes (restyle, boxes, layout of what changed, repaint with damage) |
| | `TextChange` | sets a label's text, then flushes |
| | `SubtreeRelayout` | resizes one quest row (a declaration swap), then flushes |
| `MotionBenchmarks` | `CompositorTick100` | advances the clock 16 ms with 100 transform and opacity animations running, then patches the display list |
| | `MixedTick100Compositor4Layout` | the same 100, plus width animations on two slots and color and background-color animations; repaints when layout or paint properties changed |
| | `WidthTransitionTick` | a CSS transition on one row's width (`transition: width` on `#quest3`, started by hovering it, 400,000 px over 100,000 s so it never ends and moves about 0.06 px a frame): advances the clock 16 ms, interpolates the width, lays out the row's subtree again and updates the display list. The frame of a game UI that animates a layout property; it has its own table in `summary.md` |
| | `IdleAdvance` | advances the clock on a page with no animation |
| `OneGameUiBenchmarks` | the same four as `IncrementalBenchmarks` | the change, then `UiTree.Frame()` (bindings, style, layout, draw-list update) |
| `FlexHudBenchmarks` | `IdleFrame`, `HoverRestyle`, `HoverWithHitTest`, `TextChange`, `SubtreeRelayout` | the same changes on the page `OneGameUiBenchmarks` builds, built through the engine's `StyledTree` (see below) |

The setup of each benchmark runs its changes 200–400 times first so the JIT has promoted the code, because a running game is warm.

## The OneGame.UI comparison

`bench/OneRender.Benchmarks.OneGameUi` references OneGame.UI through its published package (`RecursiveAI.OneGame.UI` and `.Text`, pinned in
`Directory.Packages.props`) and uses only its public builder, style-sheet and input API. It is a benchmark project only; nothing under `src/`
refers to it. It contains `OneGameUiBenchmarks`, which builds the same page as `GameUiScene` with `Ui.Row`, `Ui.Column`, `Ui.Label` and style
sheet `:hover` rules, shaped with the same Liberation Sans files, and it compiles the engine's `IncrementalBenchmarks` in, so one report
holds both sides. Differences to keep in mind when reading it:

- the page's CSS grids become wrapping rows of fixed-size cells, because OneGame.UI lays out with flexbox only;
- the engine side runs the builder front end of the engine (`LivePage`), the way a game host would drive it;
- OneGame.UI's hover goes through `UiTree.Dispatch` with a pointer move, which includes its hit test; the engine's hover sets the element
  states directly.

## Like for like: the flex-only page through the engine

The comparison above runs the engine on the page written in full CSS (a 192-cell grid for the minimap, a grid for the inventory), which is
more than OneGame.UI can express, so the subtree row is not like for like. `FlexHudBenchmarks` reverses it: the page `OneGameUiBenchmarks`
builds with OneGame.UI (about 1,000 nodes, flex only: wrapping rows of fixed-size cells in place of the grids) is built again as a
`StyledTree` by `FlexHudScene`, with style declarations and no HTML, and driven through `LivePage` as a game host would. Every OneGame.UI
`Row`, `Column`, `Box` and `Label` is one element, in the same order, with the other library's flex defaults written out
(`display:flex`, row direction, `box-sizing:border-box`, `flex-shrink:1`).

- **The scene check.** Before the benchmarks, `OneRender.Benchmarks.OneGameUi.dll --verify-scene` builds both pages and walks them
  together: the same number of elements with the same nesting, and every border box within 0.5 px, after the first layout and again after
  the text change and the resize. Any difference stops the run (setup of the benchmark class makes the same check). The result is in
  `equivalence.txt` and in the summary. Two things were needed to make the pages agree: labels carry `line-height: 1.1499`, which is the
  fractional line height OneGame.UI takes from the font (CSS `normal` rounds the line to a whole pixel, which drifts by 0.2 px per label), and the
  bold face is registered with the OneGame.UI font library under the same family name as the regular one so that `font-weight: 700`
  selects it (the earlier setup used a separate family name, so bold labels there were measured in the regular face; the numbers of
  `OneGameUiBenchmarks` are not affected beyond that).
- **The changes** are the ones above. `HoverRestyle` sets the element states directly; `HoverWithHitTest` finds the element under the
  pointer first, which is what OneGame.UI's pointer dispatch does, so it is the fairer partner of OneGame.UI's hover row.
- **Reading it.** The two sides still differ in what a frame includes: OneGame.UI's `Frame()` also runs data bindings and builds its own
  draw list; the engine's `Flush` restyles, patches boxes, lays out what changed and repaints into the retained display list with damage.

# The document benchmark

HTML to tagged PDF (`HtmlPdf.ToPdf`, default options) against headless Chrome print-to-PDF, with WeasyPrint run for information.
One command on the benchmark host: `tools/bench/run-documents-gate.sh` (takes the benchmark lock inside the job, exits 3 if it is
held, records CPU, cores and load before, during and after) or `tools/bench/run-documents-gate.sh --smoke` (no lock, short, not
publishable). It writes `summary.md`, `machine.txt`, the raw per-run lines and the PDFs under `$ONERENDER_WORK/bench/`; a copy of a
run's summary and raw lines is kept in `results/bench/documents/`.

## The documents

`tools/bench/documents/make-documents.py` writes them as single HTML files with the style sheets inlined:

| Document | Source | Size |
|---|---|---|
| `invoice` | `testdata/templates/invoice` | 2 pages |
| `report` | the report template's generator with 19 chapters | 50 pages |
| `table10k` | one table of 10,000 rows with a repeating header | about 210 pages |
| `tailwind` | a marketing page built with Tailwind 4.3.3 plus daisyUI and Flowbite (`tools/geomdiff/scripts/build-tailwind.sh`), 420 KB of CSS | 5 pages |

## The tools and what is measured

- **onerender, process**: `tools/bench/OneRender.PdfCli`, published as a NativeAOT executable, one fresh process per run. A command-line
  or serverless host ships the engine this way. The same call under the JIT (`dotnet OneRender.Benchmarks.Documents.dll render`) is
  measured too, for information; its first run includes JIT compilation of the whole pipeline.
- **onerender, warm**: BenchmarkDotNet (`bench/OneRender.Benchmarks.Documents`, `MemoryDiagnoser`), one call in an already running
  process: mean time and bytes allocated.
- **Chrome**: `tools/bench/OneRender.ChromePdf` drives Chrome for Testing 156.0.8078.47 over the DevTools Protocol (the client of the
  geometry differential) with `Page.printToPDF` on A4, half-inch margins, backgrounds, tagged output, `@page` size honoured. The
  same launch flags as the differential (headless, no GPU, site isolation off). Fonts are limited by the fontconfig file to the font
  files the fonts package ships. *Cold*: a new browser per run, timed from the start of its process to the arrival of the PDF. *Warm*:
  further prints, each in a new tab, in the last cold browser.
- **WeasyPrint**: a fresh `weasyprint` process per run from its own `uv` environment (version recorded in `machine.txt`), same fontconfig
  file.
- **Memory**: peak of the whole process tree, sampled every 10 ms from `/proc`: the proportional set size (shared pages divided among
  the processes that map them; the figure the verdict uses) and the sum of resident sizes (shared pages counted in every process, so
  it overstates a multi-process browser). Warm Chrome memory is not reported: closed tabs leave their processes behind, so it only grows.
- **Verdict** for a document: onerender is ahead of Chrome on process wall time, warm time and peak PSS.

The output page counts are in the table so a difference in pagination is visible; they are not required to match.

## Changing what is compared

Everything lives in the three scripts under `tools/bench/` and `tools/bench/documents/`; the number of runs is at the top of
`run-documents-gate.sh`. The set of font files for Chrome and WeasyPrint is `tools/geomdiff/scripts/fetch-fonts.sh`.
