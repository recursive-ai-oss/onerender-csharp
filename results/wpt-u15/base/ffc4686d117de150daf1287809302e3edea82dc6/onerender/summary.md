# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-backgrounds | 1 | 847 | 180 | 406 | 33 | 228 | 0.2908 |
| css-borders | 1 | 181 | 17 | 72 | 5 | 87 | 0.1809 |
| css-images | 1 | 510 | 237 | 113 | 61 | 99 | 0.5766 |
| css-inline | 1 | 255 | 11 | 152 | 0 | 92 | 0.0675 |
| css-text-decor | 1 | 357 | 97 | 176 | 12 | 72 | 0.3404 |
| css-text | 1 | 1860 | 300 | 1081 | 4 | 475 | 0.2166 |
| css2-flow | 1 | 2415 | 568 | 1694 | 4 | 149 | 0.2507 |
| css2-generated | 1 | 382 | 102 | 257 | 0 | 23 | 0.2841 |
| **all** | | 6807 | 1512 | 3951 | 119 | 1225 | 0.2709 |

## Why tests failed or were skipped

### css-backgrounds

- Failures: 404 × == reference differs; 2 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 72 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-images

- Failures: 111 × == reference differs; 2 × != reference identical
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 152 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-text-decor

- Failures: 153 × == reference differs; 23 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 989 × == reference differs; 92 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css2-flow

- Failures: 1681 × == reference differs; 13 × != reference identical
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 257 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
