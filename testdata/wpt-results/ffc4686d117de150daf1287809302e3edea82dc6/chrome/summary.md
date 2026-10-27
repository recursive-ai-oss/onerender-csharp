# WPT reftest results: chrome

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: chrome (Chrome/156.0.8078.47)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| compositing | 1 | 75 | 43 | 9 | 0 | 23 | 0.8269 |
| css-animations | 1 | 163 | 15 | 1 | 0 | 147 | 0.9375 |
| css-backgrounds | 3 | 283 | 162 | 37 | 2 | 82 | 0.8060 |
| css-borders | 1 | 181 | 83 | 11 | 0 | 87 | 0.8830 |
| css-break | 3 | 397 | 332 | 38 | 0 | 27 | 0.8973 |
| css-color | 1 | 377 | 286 | 10 | 0 | 81 | 0.9662 |
| css-flexbox | 4 | 350 | 227 | 8 | 3 | 112 | 0.9538 |
| css-fonts | 2 | 276 | 140 | 28 | 0 | 108 | 0.8333 |
| css-grid | 7 | 355 | 112 | 116 | 0 | 127 | 0.4912 |
| css-images | 2 | 255 | 141 | 62 | 3 | 49 | 0.6845 |
| css-inline | 1 | 255 | 138 | 28 | 0 | 89 | 0.8313 |
| css-lists | 1 | 212 | 75 | 72 | 0 | 65 | 0.5102 |
| css-masking | 2 | 224 | 134 | 5 | 1 | 84 | 0.9571 |
| css-multicol | 2 | 282 | 199 | 6 | 0 | 77 | 0.9707 |
| css-overflow | 3 | 298 | 87 | 88 | 1 | 122 | 0.4943 |
| css-page | 1 | 247 | 157 | 59 | 1 | 30 | 0.7235 |
| css-position | 1 | 367 | 130 | 3 | 0 | 234 | 0.9774 |
| css-tables | 1 | 308 | 132 | 8 | 0 | 168 | 0.9429 |
| css-text-decor | 1 | 357 | 189 | 95 | 1 | 72 | 0.6632 |
| css-text | 5 | 372 | 235 | 41 | 0 | 96 | 0.8514 |
| css-transforms | 3 | 302 | 236 | 2 | 1 | 63 | 0.9874 |
| css-transitions | 1 | 131 | 1 | 0 | 0 | 130 | 1.0000 |
| css-ui | 3 | 359 | 35 | 4 | 0 | 320 | 0.8974 |
| css-values | 2 | 249 | 83 | 4 | 0 | 162 | 0.9540 |
| css2-floats | 1 | 345 | 301 | 13 | 0 | 31 | 0.9586 |
| css2-flow | 7 | 345 | 316 | 8 | 0 | 21 | 0.9753 |
| css2-generated | 1 | 382 | 350 | 9 | 0 | 23 | 0.9749 |
| css2-positioning | 2 | 327 | 312 | 1 | 0 | 14 | 0.9968 |
| css2-tables | 1 | 375 | 237 | 12 | 0 | 126 | 0.9518 |
| filter-effects | 1 | 367 | 250 | 18 | 2 | 97 | 0.9259 |
| **all** | | 8816 | 5138 | 796 | 15 | 2867 | 0.8637 |

## Why tests failed or were skipped

### compositing

- Failures: 9 × == reference differs
- Skipped: 15 × testharness.js test; 7 × script-dependent; 1 × reftest-wait (needs script)
### css-animations

- Failures: 1 × == reference differs
- Skipped: 127 × testharness.js test; 16 × reftest-wait (needs script); 4 × script-dependent
### css-backgrounds

- Failures: 37 × == reference differs
- Skipped: 44 × testharness.js test; 28 × reftest-wait (needs script); 10 × script-dependent
### css-borders

- Failures: 11 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-break

- Failures: 38 × == reference differs
- Skipped: 16 × testharness.js test; 6 × script-dependent; 4 × reftest-wait (needs script); 1 × reference file missing
### css-color

- Failures: 9 × == reference differs; 1 × error: TimeoutException: DevTools command Page.captureScreenshot timed out after 30s.
- Skipped: 69 × testharness.js test; 7 × reftest-wait (needs script); 5 × script-dependent
### css-flexbox

- Failures: 8 × == reference differs
- Skipped: 96 × testharness.js test; 16 × script-dependent
### css-fonts

- Failures: 20 × == reference differs; 8 × != reference identical
- Skipped: 87 × testharness.js test; 15 × reftest-wait (needs script); 6 × script-dependent
### css-grid

- Failures: 116 × == reference differs
- Skipped: 107 × testharness.js test; 18 × script-dependent; 2 × reftest-wait (needs script)
### css-images

- Failures: 62 × == reference differs
- Skipped: 21 × testharness.js test; 15 × script-dependent; 13 × reftest-wait (needs script)
### css-inline

- Failures: 28 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script)
### css-lists

- Failures: 72 × == reference differs
- Skipped: 37 × testharness.js test; 20 × script-dependent; 8 × reftest-wait (needs script)
### css-masking

- Failures: 5 × == reference differs
- Skipped: 50 × reftest-wait (needs script); 30 × testharness.js test; 2 × reference file missing; 2 × script-dependent
### css-multicol

- Failures: 6 × == reference differs
- Skipped: 48 × testharness.js test; 21 × reftest-wait (needs script); 8 × script-dependent
### css-overflow

- Failures: 87 × == reference differs; 1 × != reference identical
- Skipped: 72 × testharness.js test; 26 × reftest-wait (needs script); 23 × script-dependent; 1 × reference file missing
### css-page

- Failures: 59 × == reference differs
- Skipped: 23 × testharness.js test; 5 × reftest-wait (needs script); 2 × script-dependent
### css-position

- Failures: 3 × == reference differs
- Skipped: 115 × testharness.js test; 82 × script-dependent; 37 × reftest-wait (needs script)
### css-tables

- Failures: 8 × == reference differs
- Skipped: 137 × testharness.js test; 22 × script-dependent; 9 × reftest-wait (needs script)
### css-text-decor

- Failures: 93 × == reference differs; 2 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 41 × == reference differs
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

- Failures: 3 × == reference differs; 1 × error: TimeoutException: DevTools command Page.captureScreenshot timed out after 30s.
- Skipped: 140 × testharness.js test; 13 × script-dependent; 9 × reftest-wait (needs script)
### css2-floats

- Failures: 13 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 8 × == reference differs
- Skipped: 13 × script-dependent; 5 × reftest-wait (needs script); 3 × testharness.js test
### css2-generated

- Failures: 9 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
### css2-positioning

- Failures: 1 × == reference differs
- Skipped: 7 × script-dependent; 6 × testharness.js test; 1 × reftest-wait (needs script)
### css2-tables

- Failures: 12 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
### filter-effects

- Failures: 17 × == reference differs; 1 × error: TimeoutException: DevTools command Page.captureScreenshot timed out after 30s.
- Skipped: 41 × reftest-wait (needs script); 33 × testharness.js test; 23 × script-dependent
