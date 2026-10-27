# WPT reftest results: onerender

- WPT commit: `ffc4686d117de150daf1287809302e3edea82dc6`
- Renderer: onerender (OneRender 0.0.1.0)
- Rate = pass / (total - skipped). Blank = the comparison held but nothing was painted; it never counts as a pass.
- Stride N means every Nth discovered test was run.

| Module | Stride | Total | Pass | Fail | Blank | Skipped | Rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| compositing | 1 | 75 | 26 | 22 | 4 | 23 | 0.5000 |
| css-animations | 1 | 163 | 8 | 8 | 0 | 147 | 0.5000 |
| css-backgrounds | 1 | 847 | 306 | 305 | 8 | 228 | 0.4943 |
| css-borders | 1 | 181 | 18 | 76 | 0 | 87 | 0.1915 |
| css-break | 1 | 1191 | 176 | 932 | 1 | 82 | 0.1587 |
| css-color | 1 | 377 | 234 | 62 | 0 | 81 | 0.7905 |
| css-flexbox | 1 | 1398 | 655 | 285 | 15 | 443 | 0.6859 |
| css-fonts | 1 | 551 | 163 | 151 | 26 | 211 | 0.4794 |
| css-grid | 1 | 2481 | 532 | 1033 | 8 | 908 | 0.3382 |
| css-images | 1 | 510 | 231 | 161 | 19 | 99 | 0.5620 |
| css-inline | 1 | 255 | 14 | 152 | 0 | 89 | 0.0843 |
| css-lists | 1 | 212 | 67 | 80 | 0 | 65 | 0.4558 |
| css-masking | 1 | 448 | 94 | 180 | 8 | 166 | 0.3333 |
| css-multicol | 1 | 563 | 190 | 214 | 0 | 159 | 0.4703 |
| css-overflow | 1 | 892 | 119 | 376 | 11 | 386 | 0.2352 |
| css-page | 1 | 247 | 78 | 139 | 0 | 30 | 0.3594 |
| css-position | 1 | 367 | 40 | 92 | 1 | 234 | 0.3008 |
| css-tables | 1 | 308 | 83 | 57 | 0 | 168 | 0.5929 |
| css-text-decor | 1 | 357 | 84 | 193 | 8 | 72 | 0.2947 |
| css-text | 1 | 1860 | 888 | 493 | 4 | 475 | 0.6412 |
| css-transforms | 1 | 904 | 551 | 139 | 21 | 193 | 0.7750 |
| css-transitions | 1 | 131 | 1 | 0 | 0 | 130 | 1.0000 |
| css-ui | 1 | 1075 | 65 | 45 | 3 | 962 | 0.5752 |
| css-values | 1 | 497 | 103 | 66 | 0 | 328 | 0.6095 |
| css2-floats | 1 | 345 | 245 | 69 | 0 | 31 | 0.7803 |
| css2-flow | 1 | 2415 | 1984 | 280 | 2 | 149 | 0.8756 |
| css2-generated | 1 | 382 | 301 | 58 | 0 | 23 | 0.8384 |
| css2-positioning | 1 | 654 | 489 | 131 | 0 | 34 | 0.7887 |
| css2-tables | 1 | 375 | 144 | 105 | 0 | 126 | 0.5783 |
| filter-effects | 1 | 367 | 148 | 118 | 4 | 97 | 0.5481 |
| **all** | | 20428 | 8037 | 6022 | 143 | 6226 | 0.5659 |

## Why tests failed or were skipped

### compositing

- Failures: 21 × == reference differs; 1 × != reference identical
- Skipped: 15 × testharness.js test; 7 × script-dependent; 1 × reftest-wait (needs script)
### css-animations

- Failures: 8 × == reference differs
- Skipped: 127 × testharness.js test; 16 × reftest-wait (needs script); 4 × script-dependent
### css-backgrounds

- Failures: 304 × == reference differs; 1 × != reference identical
- Skipped: 132 × testharness.js test; 71 × reftest-wait (needs script); 25 × script-dependent
### css-borders

- Failures: 76 × == reference differs
- Skipped: 79 × testharness.js test; 4 × reftest-wait (needs script); 4 × script-dependent
### css-break

- Failures: 931 × == reference differs; 1 × != reference identical
- Skipped: 48 × testharness.js test; 26 × script-dependent; 7 × reftest-wait (needs script); 1 × reference file missing
### css-color

- Failures: 62 × == reference differs
- Skipped: 69 × testharness.js test; 7 × reftest-wait (needs script); 5 × script-dependent
### css-flexbox

- Failures: 284 × == reference differs; 1 × != reference identical
- Skipped: 376 × testharness.js test; 62 × script-dependent; 5 × reftest-wait (needs script)
### css-fonts

- Failures: 126 × == reference differs; 25 × != reference identical
- Skipped: 172 × testharness.js test; 30 × reftest-wait (needs script); 9 × script-dependent
### css-grid

- Failures: 1033 × == reference differs
- Skipped: 758 × testharness.js test; 129 × script-dependent; 19 × reftest-wait (needs script); 2 × reference file missing
### css-images

- Failures: 161 × == reference differs
- Skipped: 42 × testharness.js test; 30 × script-dependent; 27 × reftest-wait (needs script)
### css-inline

- Failures: 152 × == reference differs
- Skipped: 59 × testharness.js test; 25 × script-dependent; 5 × reftest-wait (needs script)
### css-lists

- Failures: 80 × == reference differs
- Skipped: 37 × testharness.js test; 20 × script-dependent; 8 × reftest-wait (needs script)
### css-masking

- Failures: 180 × == reference differs
- Skipped: 95 × reftest-wait (needs script); 64 × testharness.js test; 4 × script-dependent; 3 × reference file missing
### css-multicol

- Failures: 214 × == reference differs
- Skipped: 95 × testharness.js test; 43 × reftest-wait (needs script); 21 × script-dependent
### css-overflow

- Failures: 372 × == reference differs; 4 × != reference identical
- Skipped: 228 × testharness.js test; 84 × reftest-wait (needs script); 73 × script-dependent; 1 × reference file missing
### css-page

- Failures: 138 × == reference differs; 1 × error: ArgumentOutOfRangeException: width ('24960') must be less than or equal to '16384'. (Parameter 'width')
- Skipped: 23 × testharness.js test; 5 × reftest-wait (needs script); 2 × script-dependent
### css-position

- Failures: 92 × == reference differs
- Skipped: 115 × testharness.js test; 82 × script-dependent; 37 × reftest-wait (needs script)
### css-tables

- Failures: 57 × == reference differs
- Skipped: 137 × testharness.js test; 22 × script-dependent; 9 × reftest-wait (needs script)
### css-text-decor

- Failures: 184 × == reference differs; 9 × != reference identical
- Skipped: 48 × testharness.js test; 16 × script-dependent; 8 × reftest-wait (needs script)
### css-text

- Failures: 419 × == reference differs; 74 × != reference identical
- Skipped: 366 × testharness.js test; 89 × script-dependent; 20 × reftest-wait (needs script)
### css-transforms

- Failures: 138 × == reference differs; 1 × != reference identical
- Skipped: 108 × testharness.js test; 60 × reftest-wait (needs script); 23 × script-dependent; 2 × reference file missing
### css-transitions

- Skipped: 125 × testharness.js test; 3 × reftest-wait (needs script); 2 × script-dependent
### css-ui

- Failures: 41 × == reference differs; 4 × != reference identical
- Skipped: 818 × script-dependent; 115 × testharness.js test; 29 × reftest-wait (needs script)
### css-values

- Failures: 66 × == reference differs
- Skipped: 284 × testharness.js test; 27 × script-dependent; 17 × reftest-wait (needs script)
### css2-floats

- Failures: 69 × == reference differs
- Skipped: 20 × testharness.js test; 7 × script-dependent; 4 × reftest-wait (needs script)
### css2-flow

- Failures: 280 × == reference differs
- Skipped: 83 × script-dependent; 35 × reftest-wait (needs script); 31 × testharness.js test
### css2-generated

- Failures: 58 × == reference differs
- Skipped: 19 × script-dependent; 3 × reftest-wait (needs script); 1 × reference file missing
### css2-positioning

- Failures: 131 × == reference differs
- Skipped: 15 × script-dependent; 13 × testharness.js test; 6 × reftest-wait (needs script)
### css2-tables

- Failures: 105 × == reference differs
- Skipped: 120 × script-dependent; 3 × reftest-wait (needs script); 2 × testharness.js test; 1 × reference file missing
### filter-effects

- Failures: 117 × == reference differs; 1 × error: ArgumentException: '0' cannot be greater than -3.2033605E-11.
- Skipped: 41 × reftest-wait (needs script); 33 × testharness.js test; 23 × script-dependent
