# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 123 | 387 | 109 | 228 | 0.1987 |
| css-overflow | 1 | 892 | 182 | 176 | 148 | 386 | 0.3597 |
| css-position | 1 | 367 | 18 | 78 | 35 | 236 | 0.1374 |
| css2-flow | 1 | 2415 | 105 | 1141 | 1020 | 149 | 0.0463 |
| css2-positioning | 1 | 654 | 21 | 542 | 57 | 34 | 0.0339 |
| **all** | | 5175 | 449 | 2324 | 1369 | 1033 | 0.1084 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 384 × == reference differs; 3 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-overflow

- Failures: 172 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-position

- Failures: 78 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css2-flow

- Failures: 1119 × == reference differs; 22 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-positioning

- Failures: 542 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
