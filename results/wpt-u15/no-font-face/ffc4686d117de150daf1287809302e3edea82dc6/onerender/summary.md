# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 243 | 369 | 7 | 228 | 0.3926 |
| css-borders | 1 | 181 | 17 | 77 | 0 | 87 | 0.1809 |
| css-images | 1 | 510 | 221 | 169 | 21 | 99 | 0.5377 |
| css-inline | 1 | 255 | 13 | 150 | 0 | 92 | 0.0798 |
| css-text-decor | 1 | 357 | 76 | 201 | 8 | 72 | 0.2667 |
| css-text | 1 | 1860 | 294 | 1087 | 4 | 475 | 0.2123 |
| css2-flow | 1 | 2415 | 674 | 1588 | 4 | 149 | 0.2974 |
| css2-generated | 1 | 382 | 92 | 267 | 0 | 23 | 0.2563 |
| **all** | | 6807 | 1630 | 3908 | 44 | 1225 | 0.2920 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 368 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 77 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-images

- Failures: 168 × == reference differs; 1 × error: ArgumentOutOfRangeException: Coordinates must be finite and within ±1e9. (Parameter 'v')
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 150 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-text-decor

- Failures: 189 × == reference differs; 12 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 995 × == reference differs; 92 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-flow

- Failures: 1585 × == reference differs; 3 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 267 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
