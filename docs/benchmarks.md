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
| | `IdleAdvance` | advances the clock on a page with no animation |
| `OneGameUiBenchmarks` | the same four as `IncrementalBenchmarks` | the change, then `UiTree.Frame()` (bindings, style, layout, draw-list update) |

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
