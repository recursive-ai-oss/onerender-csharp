# Grid layout results

Chrome for Testing 156.0.8078.47, same font files on both sides, tolerance max(1 px, 0.5% of Chrome's extent).

## Geometry, `grid-lab` (committed `testdata/grid-lab/`, text-free grids)

Six pages, 308 elements per viewport: tracks, placement, alignment, sizing, baselines, subgrids and nesting, absolutely positioned children.
Engine against Chrome: **100.000%** at 1280x800 and at 390x844 (616 of 616).

## Geometry, `tailwind` (7 pages, 1,462 elements; the gate is not met yet because inline text is not in this engine build)

| | 1280x800 | 390x844 | all |
|---|---|---|---|
| main (block + flex), before | 2.830% | 2.361% | 2.599% |
| main + grid, after | 6.334% | 3.611% | 4.993% |

## WPT `css-grid` (pin ffc4686d, 2,481 discovered, 1,246 skipped)

`grid-lanes` tests (masonry, out of scope) pass or fail by accident of the block fallback, so the comparison excludes them.

| | pass | fail | blank | rate of runnable |
|---|---|---|---|---|
| main (block + flex), before | 84 | 266 | 85 | 0.193 |
| main + grid, after | 157 | 197 | 81 | 0.361 |

Most remaining failures need things this build lacks: the table model (several tests use a table as the reference's error layer), inline text and `::first-line`, orthogonal writing modes, form controls, and device-pixel snapping of fractional box edges.

## Fuzz `grid` (SharpFuzz under libFuzzer)

81,071 executions in 20 minutes (coverage features 41,136, corpus 1,837), no crashes, hangs or invariant failures. An earlier 15-minute run found one non-finite height, fixed and covered by `HostileValuesNeverProduceNonFiniteBoxes`.
