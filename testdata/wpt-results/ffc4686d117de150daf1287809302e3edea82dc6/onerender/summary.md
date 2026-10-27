# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 180 | 406 | 33 | 228 | 0.2908 |
| css-flexbox | 1 | 1398 | 439 | 497 | 16 | 446 | 0.4611 |
| css-grid | 1 | 2481 | 371 | 1183 | 19 | 908 | 0.2359 |
| css-inline | 1 | 255 | 11 | 152 | 0 | 92 | 0.0675 |
| css-overflow | 1 | 892 | 180 | 183 | 143 | 386 | 0.3557 |
| css-position | 1 | 367 | 36 | 94 | 1 | 236 | 0.2748 |
| css-text | 1 | 1860 | 300 | 1081 | 4 | 475 | 0.2166 |
| css2-floats | 1 | 345 | 84 | 230 | 0 | 31 | 0.2675 |
| css2-flow | 1 | 2415 | 568 | 1694 | 4 | 149 | 0.2507 |
| css2-positioning | 1 | 654 | 46 | 574 | 0 | 34 | 0.0742 |
| **all** | | 11514 | 2215 | 6094 | 220 | 2985 | 0.2597 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 404 × == reference differs; 2 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-flexbox

- Failures: 497 × == reference differs
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-grid

- Failures: 1183 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-inline

- Failures: 152 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-overflow

- Failures: 179 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-position

- Failures: 94 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-text

- Failures: 989 × == reference differs; 92 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-floats

- Failures: 230 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 1681 × == reference differs; 13 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-positioning

- Failures: 574 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
