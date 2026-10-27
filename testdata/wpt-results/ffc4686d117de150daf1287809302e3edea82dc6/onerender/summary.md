# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| compositing | 1 | 75 | 26 | 22 | 4 | 23 | 0.5000 |
| css-animations | 1 | 163 | 4 | 12 | 0 | 147 | 0.2500 |
| css-backgrounds | 1 | 847 | 297 | 314 | 8 | 228 | 0.4798 |
| css-borders | 1 | 181 | 18 | 76 | 0 | 87 | 0.1915 |
| css-break | 1 | 1191 | 166 | 878 | 0 | 147 | 0.1590 |
| css-color | 1 | 377 | 234 | 62 | 0 | 81 | 0.7905 |
| css-flexbox | 1 | 1398 | 648 | 286 | 18 | 446 | 0.6807 |
| css-fonts | 1 | 551 | 162 | 151 | 25 | 213 | 0.4793 |
| css-grid | 1 | 2481 | 533 | 1030 | 10 | 908 | 0.3388 |
| css-images | 1 | 510 | 231 | 161 | 19 | 99 | 0.5620 |
| css-inline | 1 | 255 | 14 | 149 | 0 | 92 | 0.0859 |
| css-lists | 1 | 212 | 64 | 76 | 7 | 65 | 0.4354 |
| css-masking | 1 | 448 | 94 | 180 | 8 | 166 | 0.3333 |
| css-multicol | 1 | 563 | 185 | 215 | 0 | 163 | 0.4625 |
| css-overflow | 1 | 892 | 115 | 380 | 11 | 386 | 0.2273 |
| css-page | 1 | 247 | 2 | 5 | 0 | 240 | 0.2857 |
| css-position | 1 | 367 | 38 | 92 | 1 | 236 | 0.2901 |
| css-tables | 1 | 308 | 81 | 57 | 0 | 170 | 0.5870 |
| css-text-decor | 1 | 357 | 83 | 194 | 8 | 72 | 0.2912 |
| css-text | 1 | 1860 | 867 | 514 | 4 | 475 | 0.6260 |
| css-transforms | 1 | 904 | 548 | 142 | 21 | 193 | 0.7707 |
| css-transitions | 1 | 131 | 1 | 0 | 0 | 130 | 1.0000 |
| css-ui | 1 | 1075 | 60 | 49 | 4 | 962 | 0.5310 |
| css-values | 1 | 497 | 96 | 71 | 0 | 330 | 0.5749 |
| css2-floats | 1 | 345 | 243 | 71 | 0 | 31 | 0.7739 |
| css2-flow | 1 | 2415 | 1904 | 360 | 2 | 149 | 0.8402 |
| css2-generated | 1 | 382 | 126 | 233 | 0 | 23 | 0.3510 |
| css2-positioning | 1 | 654 | 486 | 134 | 0 | 34 | 0.7839 |
| css2-tables | 1 | 375 | 144 | 105 | 0 | 126 | 0.5783 |
| filter-effects | 1 | 367 | 129 | 137 | 4 | 97 | 0.4778 |
| **all** | | 20428 | 7599 | 6156 | 154 | 6519 | 0.5463 |

## Why tests failed or were skipped

### compositing

- Failures: 21 × == reference differs; 1 × != reference identical
- Skipped: 15 × testharness.js test; 7 × script-dependent; 1 × reftest-wait (needs script)
### css-animations

- Failures: 12 × == reference differs
- Skipped: 127 × testharness.js test; 16 × reftest-wait (needs script); 4 × script-dependent
### css-backgrounds

- Failures: 313 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 76 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-break

- Failures: 878 × == reference differs
- Skipped: 66 × print reftest (paged output); 48 × testharness.js test; 26 × script-dependent; 7 × reftest-wait (needs script)
### css-color

- Failures: 62 × == reference differs
- Skipped: 69 × testharness.js test; 7 × reftest-wait (needs script); 5 × script-dependent
### css-flexbox

- Failures: 286 × == reference differs
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-fonts

- Failures: 126 × == reference differs; 25 × != reference identical
- Skipped: 172 × testharness.js test; 30 × reftest-wait (needs script); 9 × script-dependent; 2 × print reftest (paged output)
### css-grid

- Failures: 1030 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-images

- Failures: 161 × == reference differs
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 149 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-lists

- Failures: 76 × == reference differs
- Skipped: 37 × testharness.js test; 20 × script-dependent; 8 × reftest-wait (needs script)
### css-masking

- Failures: 180 × == reference differs
- Skipped: 95 × reftest-wait (needs script); 64 × testharness.js test; 4 × script-dependent; 3 × reference file missing
### css-multicol

- Failures: 215 × == reference differs
- Skipped: 95 × testharness.js test; 43 × reftest-wait (needs script); 21 × script-dependent; 4 × print reftest (paged output)
### css-overflow

- Failures: 376 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-page

- Failures: 5 × == reference differs
- Skipped: 217 × print reftest (paged output); 23 × testharness.js test
### css-position

- Failures: 92 × == reference differs
- Skipped: 115 × testharness.js test; 81 × script-dependent; 37 × reftest-wait (needs script); 3 × print reftest (paged output)
### css-tables

- Failures: 57 × == reference differs
- Skipped: 137 × testharness.js test; 20 × script-dependent; 9 × reftest-wait (needs script); 4 × print reftest (paged output)
### css-text-decor

- Failures: 185 × == reference differs; 9 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 440 × == reference differs; 74 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css-transforms

- Failures: 141 × == reference differs; 1 × != reference identical
- Skipped: 108 × testharness.js test; 60 × reftest-wait (needs script); 23 × script-dependent; 2 × reference file missing
### css-transitions

- Skipped: 125 × testharness.js test; 3 × reftest-wait (needs script); 2 × script-dependent
### css-ui

- Failures: 41 × == reference differs; 8 × != reference identical
- Skipped: 818 × script-dependent; 115 × testharness.js test; 29 × reftest-wait (needs script)
### css-values

- Failures: 71 × == reference differs
- Skipped: 284 × testharness.js test; 27 × script-dependent; 17 × reftest-wait (needs script); 2 × print reftest (paged output)
### css2-floats

- Failures: 71 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 360 × == reference differs
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 233 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
### css2-positioning

- Failures: 134 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
### css2-tables

- Failures: 105 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
### filter-effects

- Failures: 136 × == reference differs; 1 × error: ArgumentException: '0' cannot be greater than -3.2033605E-11.
- Skipped: 41 × reftest-wait (needs script); 33 × testharness.js test; 23 × script-dependent
