# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| css-animations | 1 | 163 | 3 | 13 | 0 | 147 | 0.1875 |
| css-backgrounds | 1 | 847 | 294 | 317 | 8 | 228 | 0.4750 |
| css-borders | 1 | 181 | 17 | 77 | 0 | 87 | 0.1809 |
| css-break | 1 | 1191 | 154 | 890 | 0 | 147 | 0.1475 |
| css-color | 1 | 377 | 233 | 62 | 1 | 81 | 0.7872 |
| css-flexbox | 1 | 1398 | 642 | 292 | 18 | 446 | 0.6744 |
| css-fonts | 1 | 551 | 160 | 153 | 25 | 213 | 0.4734 |
| css-grid | 1 | 2481 | 517 | 1046 | 10 | 908 | 0.3287 |
| css-images | 1 | 510 | 221 | 170 | 20 | 99 | 0.5377 |
| css-inline | 1 | 255 | 14 | 149 | 0 | 92 | 0.0859 |
| css-lists | 1 | 212 | 64 | 76 | 7 | 65 | 0.4354 |
| css-multicol | 1 | 563 | 185 | 215 | 0 | 163 | 0.4625 |
| css-overflow | 1 | 892 | 112 | 383 | 11 | 386 | 0.2213 |
| css-page | 1 | 247 | 2 | 5 | 0 | 240 | 0.2857 |
| css-position | 1 | 367 | 38 | 92 | 1 | 236 | 0.2901 |
| css-tables | 1 | 308 | 79 | 59 | 0 | 170 | 0.5725 |
| css-text-decor | 1 | 357 | 83 | 194 | 8 | 72 | 0.2912 |
| css-text | 1 | 1860 | 866 | 515 | 4 | 475 | 0.6253 |
| css-transforms | 1 | 904 | 363 | 323 | 25 | 193 | 0.5105 |
| css-transitions | 1 | 131 | 1 | 0 | 0 | 130 | 1.0000 |
| css-ui | 1 | 1075 | 60 | 49 | 4 | 962 | 0.5310 |
| css-values | 1 | 497 | 91 | 76 | 0 | 330 | 0.5449 |
| css2-floats | 1 | 345 | 243 | 71 | 0 | 31 | 0.7739 |
| css2-flow | 1 | 2415 | 1905 | 359 | 2 | 149 | 0.8407 |
| css2-generated | 1 | 382 | 125 | 234 | 0 | 23 | 0.3482 |
| css2-positioning | 1 | 654 | 486 | 134 | 0 | 34 | 0.7839 |
| css2-tables | 1 | 375 | 144 | 105 | 0 | 126 | 0.5783 |
| **all** | | 19538 | 7102 | 6059 | 144 | 6233 | 0.5338 |

## Why tests failed or were skipped

### css-animations

- Failures: 13 × == reference differs
- Skipped: 127 × testharness.js test; 16 × reftest-wait (needs script); 4 × script-dependent
### css-backgrounds

- Failures: 316 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 77 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-break

- Failures: 890 × == reference differs
- Skipped: 66 × print reftest (paged output); 48 × testharness.js test; 26 × script-dependent; 7 × reftest-wait (needs script)
### css-color

- Failures: 62 × == reference differs
- Skipped: 69 × testharness.js test; 7 × reftest-wait (needs script); 5 × script-dependent
### css-flexbox

- Failures: 292 × == reference differs
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-fonts

- Failures: 128 × == reference differs; 25 × != reference identical
- Skipped: 172 × testharness.js test; 30 × reftest-wait (needs script); 9 × script-dependent; 2 × print reftest (paged output)
### css-grid

- Failures: 1046 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-images

- Failures: 170 × == reference differs
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 149 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-lists

- Failures: 76 × == reference differs
- Skipped: 37 × testharness.js test; 20 × script-dependent; 8 × reftest-wait (needs script)
### css-multicol

- Failures: 215 × == reference differs
- Skipped: 95 × testharness.js test; 43 × reftest-wait (needs script); 21 × script-dependent; 4 × print reftest (paged output)
### css-overflow

- Failures: 379 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-page

- Failures: 5 × == reference differs
- Skipped: 217 × print reftest (paged output); 23 × testharness.js test
### css-position

- Failures: 92 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-tables

- Failures: 59 × == reference differs
- Skipped: 137 × testharness.js test; 20 × script-dependent; 9 × reftest-wait (needs script); 4 × print reftest (paged output)
### css-text-decor

- Failures: 185 × == reference differs; 9 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 441 × == reference differs; 74 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css-transforms

- Failures: 256 × == reference differs; 67 × != reference identical
- Skipped: 108 × testharness.js test; 60 × reftest-wait (needs script); 23 × script-dependent; 2 × reference file missing
### css-transitions

- Skipped: 125 × testharness.js test; 3 × reftest-wait (needs script); 2 × script-dependent
### css-ui

- Failures: 41 × == reference differs; 8 × != reference identical
- Skipped: 818 × script-dependent; 115 × testharness.js test; 29 × reftest-wait (needs script)
### css-values

- Failures: 76 × == reference differs
- Skipped: 284 × testharness.js test; 27 × script-dependent; 17 × reftest-wait (needs script); 2 × print reftest (paged output)
### css2-floats

- Failures: 71 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 359 × == reference differs
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 234 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
### css2-positioning

- Failures: 134 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
### css2-tables

- Failures: 105 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
