# WPT reftest results: chrome

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: chrome (Chrome/156.0.8078.47)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-animations | 1 | 163 | 15 | 1 | 0 | 147 | 0.9375 |
| css-backgrounds | 3 | 283 | 161 | 38 | 2 | 82 | 0.8010 |
| css-borders | 1 | 181 | 84 | 10 | 0 | 87 | 0.8936 |
| css-break | 3 | 397 | 313 | 36 | 0 | 48 | 0.8968 |
| css-color | 1 | 377 | 287 | 9 | 0 | 81 | 0.9696 |
| css-flexbox | 4 | 350 | 230 | 5 | 3 | 112 | 0.9664 |
| css-fonts | 2 | 276 | 139 | 28 | 0 | 109 | 0.8323 |
| css-grid | 7 | 355 | 111 | 117 | 0 | 127 | 0.4868 |
| css-inline | 1 | 255 | 135 | 28 | 0 | 92 | 0.8282 |
| css-multicol | 2 | 282 | 196 | 6 | 0 | 80 | 0.9703 |
| css-overflow | 3 | 298 | 86 | 89 | 1 | 122 | 0.4886 |
| css-page | 1 | 247 | 1 | 6 | 0 | 240 | 0.1429 |
| css-position | 1 | 367 | 127 | 4 | 0 | 236 | 0.9695 |
| css-tables | 1 | 308 | 130 | 8 | 0 | 170 | 0.9420 |
| css-text | 5 | 372 | 221 | 55 | 0 | 96 | 0.8007 |
| css-transforms | 3 | 302 | 236 | 2 | 1 | 63 | 0.9874 |
| css-transitions | 1 | 131 | 1 | 0 | 0 | 130 | 1.0000 |
| css-ui | 3 | 359 | 35 | 4 | 0 | 320 | 0.8974 |
| css-values | 2 | 249 | 83 | 3 | 0 | 163 | 0.9651 |
| css2-floats | 1 | 345 | 302 | 12 | 0 | 31 | 0.9618 |
| css2-flow | 7 | 345 | 313 | 11 | 0 | 21 | 0.9660 |
| css2-generated | 1 | 382 | 342 | 17 | 0 | 23 | 0.9526 |
| css2-positioning | 2 | 327 | 313 | 0 | 0 | 14 | 1.0000 |
| css2-tables | 1 | 375 | 235 | 14 | 0 | 126 | 0.9438 |
| **all** | | 7326 | 4096 | 503 | 7 | 2720 | 0.8893 |

## Why tests failed or were skipped

### css-animations

- Failures: 1 × == reference differs
- Skipped: 127 × testharness.js test; 16 × reftest-wait (needs script); 4 × script-dependent
### css-backgrounds

- Failures: 38 × == reference differs
- Skipped: 44 × testharness.js test; 28 × reftest-wait (needs script); 10 × script-dependent
### css-borders

- Failures: 10 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-break

- Failures: 36 × == reference differs
- Skipped: 22 × print reftest (paged output); 16 × testharness.js test; 6 × script-dependent; 4 × reftest-wait (needs script)
### css-color

- Failures: 9 × == reference differs
- Skipped: 69 × testharness.js test; 7 × reftest-wait (needs script); 5 × script-dependent
### css-flexbox

- Failures: 5 × == reference differs
- Skipped: 96 × testharness.js test; 16 × script-dependent
### css-fonts

- Failures: 21 × == reference differs; 7 × != reference identical
- Skipped: 87 × testharness.js test; 15 × reftest-wait (needs script); 6 × script-dependent; 1 × print reftest (paged output)
### css-grid

- Failures: 115 × == reference differs; 2 × error: TimeoutException: DevTools command Page.captureScreenshot timed out after 30s.
- Skipped: 107 × testharness.js test; 18 × script-dependent; 2 × reftest-wait (needs script)
### css-inline

- Failures: 28 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-multicol

- Failures: 5 × == reference differs; 1 × error: TimeoutException: DevTools command Page.captureScreenshot timed out after 30s.
- Skipped: 48 × testharness.js test; 21 × reftest-wait (needs script); 8 × script-dependent; 3 × print reftest (paged output)
### css-overflow

- Failures: 88 × == reference differs; 1 × != reference identical
- Skipped: 72 × testharness.js test; 26 × reftest-wait (needs script); 23 × script-dependent; 1 × reference file missing
### css-page

- Failures: 6 × == reference differs
- Skipped: 217 × print reftest (paged output); 23 × testharness.js test
### css-position

- Failures: 4 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-tables

- Failures: 8 × == reference differs
- Skipped: 137 × testharness.js test; 20 × script-dependent; 9 × reftest-wait (needs script); 4 × print reftest (paged output)
### css-text

- Failures: 43 × == reference differs; 12 × != reference identical
- Skipped: 73 × testharness.js test; 19 × script-dependent; 4 × reftest-wait (needs script)
### css-transforms

- Failures: 2 × == reference differs
- Skipped: 36 × testharness.js test; 17 × reftest-wait (needs script); 9 × script-dependent; 1 × reference file missing
### css-transitions

- Skipped: 125 × testharness.js test; 3 × reftest-wait (needs script); 2 × script-dependent
### css-ui

- Failures: 3 × == reference differs; 1 × != reference identical
- Skipped: 270 × script-dependent; 42 × testharness.js test; 8 × reftest-wait (needs script)
### css-values

- Failures: 3 × == reference differs
- Skipped: 140 × testharness.js test; 13 × script-dependent; 9 × reftest-wait (needs script); 1 × print reftest (paged output)
### css2-floats

- Failures: 12 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 11 × == reference differs
- Skipped: 13 × script-dependent; 5 × reftest-wait (needs script); 3 × testharness.js test
### css2-generated

- Failures: 17 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
### css2-positioning

- Skipped: 7 × script-dependent; 6 × testharness.js test; 1 × reftest-wait (needs script)
### css2-tables

- Failures: 14 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
