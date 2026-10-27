# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 123 | 391 | 105 | 228 | 0.1987 |
| css-flexbox | 1 | 1398 | 226 | 607 | 119 | 446 | 0.2374 |
| css-overflow | 1 | 892 | 180 | 183 | 143 | 386 | 0.3557 |
| css-position | 1 | 367 | 18 | 78 | 35 | 236 | 0.1374 |
| css2-floats | 1 | 345 | 23 | 241 | 50 | 31 | 0.0732 |
| css2-flow | 1 | 2415 | 109 | 1233 | 924 | 149 | 0.0481 |
| css2-positioning | 1 | 654 | 21 | 543 | 56 | 34 | 0.0339 |
| **all** | | 6918 | 700 | 3276 | 1432 | 1510 | 0.1294 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 388 × == reference differs; 3 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-overflow

- Failures: 179 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-position

- Failures: 78 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css2-floats

- Failures: 231 × == reference differs; 10 × != reference identical
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 1211 × == reference differs; 22 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-positioning

- Failures: 543 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
