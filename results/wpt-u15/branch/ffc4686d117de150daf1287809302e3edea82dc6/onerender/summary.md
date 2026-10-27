# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 257 | 354 | 8 | 228 | 0.4152 |
| css-borders | 1 | 181 | 17 | 77 | 0 | 87 | 0.1809 |
| css-images | 1 | 510 | 221 | 170 | 20 | 99 | 0.5377 |
| css-inline | 1 | 255 | 14 | 149 | 0 | 92 | 0.0859 |
| css-text-decor | 1 | 357 | 82 | 195 | 8 | 72 | 0.2877 |
| css-text | 1 | 1860 | 633 | 748 | 4 | 475 | 0.4570 |
| css2-flow | 1 | 2415 | 793 | 1469 | 4 | 149 | 0.3500 |
| css2-generated | 1 | 382 | 93 | 266 | 0 | 23 | 0.2591 |
| **all** | | 6807 | 2110 | 3428 | 44 | 1225 | 0.3780 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 353 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 77 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-images

- Failures: 170 × == reference differs
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 149 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-text-decor

- Failures: 186 × == reference differs; 9 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 674 × == reference differs; 74 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-flow

- Failures: 1469 × == reference differs
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 266 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
