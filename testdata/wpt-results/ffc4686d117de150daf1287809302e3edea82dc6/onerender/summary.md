# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 180 | 406 | 33 | 228 | 0.2908 |
| css-break | 1 | 1191 | 88 | 956 | 0 | 147 | 0.0843 |
| css-flexbox | 1 | 1398 | 439 | 497 | 16 | 446 | 0.4611 |
| css-grid | 1 | 2481 | 371 | 1183 | 19 | 908 | 0.2359 |
| css-inline | 1 | 255 | 11 | 152 | 0 | 92 | 0.0675 |
| css-multicol | 1 | 563 | 51 | 349 | 0 | 163 | 0.1275 |
| css-overflow | 1 | 892 | 106 | 384 | 16 | 386 | 0.2095 |
| css-position | 1 | 367 | 37 | 93 | 1 | 236 | 0.2824 |
| css-text | 1 | 1860 | 343 | 1038 | 4 | 475 | 0.2477 |
| css2-floats | 1 | 345 | 82 | 232 | 0 | 31 | 0.2611 |
| css2-flow | 1 | 2415 | 590 | 1672 | 4 | 149 | 0.2604 |
| css2-positioning | 1 | 654 | 46 | 574 | 0 | 34 | 0.0742 |
| **all** | | 13268 | 2344 | 7536 | 93 | 3295 | 0.2350 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 404 × == reference differs; 2 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-break

- Failures: 956 × == reference differs
- Skipped: 66 × print reftest (paged output); 48 × testharness.js test; 26 × script-dependent; 7 × reftest-wait (needs script)
### css-flexbox

- Failures: 497 × == reference differs
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-grid

- Failures: 1183 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-inline

- Failures: 152 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-multicol

- Failures: 347 × == reference differs; 1 × error: DisplayListException: Item id 21475491840 is used twice in one list.; 1 × error: DisplayListException: Item id 47245295616 is used twice in one list.
- Skipped: 95 × testharness.js test; 43 × reftest-wait (needs script); 21 × script-dependent; 4 × print reftest (paged output)
### css-overflow

- Failures: 380 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-position

- Failures: 93 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-text

- Failures: 943 × == reference differs; 95 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-floats

- Failures: 232 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 1659 × == reference differs; 13 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-positioning

- Failures: 574 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
