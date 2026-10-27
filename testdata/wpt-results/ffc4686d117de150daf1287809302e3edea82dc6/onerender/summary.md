# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 120 | 371 | 128 | 228 | 0.1939 |
| css2-flow | 1 | 2415 | 104 | 1028 | 1134 | 149 | 0.0459 |
| **all** | | 3262 | 224 | 1399 | 1262 | 377 | 0.0776 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 368 × == reference differs; 3 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css2-flow

- Failures: 1005 × == reference differs; 23 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
