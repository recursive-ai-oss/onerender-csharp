# Documentation

Normative descriptions of how the engine behaves where the specifications leave room, and of its public formats. Start with the [README](../README.md) for what the packages are and how to call them.

| Page | What it covers |
|---|---|
| [architecture.md](architecture.md) | The pipeline: styled-node tree, computed values, box tree, layout algorithms (block, inline, float, positioned, flex, grid, table, multi-column, paged), paint, incremental updates, interaction, animations. Each section lists what checks it. |
| [layout-conventions.md](layout-conventions.md) | Choices made where the specifications allow several results, each with the browser evidence and the spec text, and the user agent style sheet. |
| [text.md](text.md) | The text service: font resolution and fallback, shaping, `@font-face`, limits on hostile font data. |
| [display-list.md](display-list.md) | The versioned display list format: items, ids, scopes, damage, in-place patching. What a backend or a GPU adapter consumes. |
| [forms.md](forms.md) | Form controls, their metrics and states, text editing and IME composition. |
| [animations.md](animations.md) | CSS Transitions and Animations on the host's clock, and where they deviate from the specifications. |
| [pdf.md](pdf.md) | PDF output: pages, vector or raster islands, tags, limits, known limitations, how it is validated. |
| [razor.md](razor.md) | Rendering a Razor component to a PDF or an image. |
| [accessibility-facts.md](accessibility-facts.md) | The rendered page facts produced for accessibility checks. |
| [geometry-differential.md](geometry-differential.md) | How layout is compared with Chrome, element by element: formats, tolerance, corpus sets. |
| [pixel-similarity.md](pixel-similarity.md) | Pixel comparison with Chrome screenshots (information only). |
| [wpt-results.md](wpt-results.md) | The Web Platform Tests reftest runner, how results are counted, and the floors. |
| [benchmarks.md](benchmarks.md) | The interactive and document benchmarks, how they are run and how to read them. |

Measured results are in [`results/`](../results). Fuzz targets are described in [`fuzz/README.md`](../fuzz/README.md), CI in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) and the package checks in [`tools/ci/`](../tools/ci).
