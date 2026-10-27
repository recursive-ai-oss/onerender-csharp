# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 257 | 354 | 8 | 228 | 0.4152 |
| css-break | 1 | 1191 | 81 | 963 | 0 | 147 | 0.0776 |
| css-flexbox | 1 | 1398 | 458 | 479 | 15 | 446 | 0.4811 |
| css-grid | 1 | 2481 | 393 | 1170 | 10 | 908 | 0.2498 |
| css-multicol | 1 | 563 | 45 | 355 | 0 | 163 | 0.1125 |
| css-tables | 1 | 308 | 32 | 106 | 0 | 170 | 0.2319 |
| css-text | 1 | 1860 | 633 | 748 | 4 | 475 | 0.4570 |
| css2-floats | 1 | 345 | 88 | 226 | 0 | 31 | 0.2803 |
| css2-flow | 1 | 2415 | 793 | 1469 | 4 | 149 | 0.3500 |
| css2-positioning | 1 | 654 | 86 | 534 | 0 | 34 | 0.1387 |
| css2-tables | 1 | 375 | 46 | 203 | 0 | 126 | 0.1847 |
| **all** | | 12437 | 2912 | 6607 | 41 | 2877 | 0.3046 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 353 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-break

- Failures: 963 × == reference differs
- Skipped: 66 × print reftest (paged output); 48 × testharness.js test; 26 × script-dependent; 7 × reftest-wait (needs script)
### css-flexbox

- Failures: 479 × == reference differs
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-grid

- Failures: 1170 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-multicol

- Failures: 353 × == reference differs; 1 × error: DisplayListException: Item id 21475491840 is used twice in one list.; 1 × error: DisplayListException: Item id 47245295616 is used twice in one list.
- Skipped: 95 × testharness.js test; 43 × reftest-wait (needs script); 21 × script-dependent; 4 × print reftest (paged output)
### css-tables

- Failures: 106 × == reference differs
- Skipped: 137 × testharness.js test; 20 × script-dependent; 9 × reftest-wait (needs script); 4 × print reftest (paged output)
### css-text

- Failures: 674 × == reference differs; 74 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-floats

- Failures: 226 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 1469 × == reference differs
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-positioning

- Failures: 534 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
### css2-tables

- Failures: 203 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
