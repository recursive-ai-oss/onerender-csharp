# WPT before and after the second paint area

Single full runs (no sampling) of the pinned WPT commit through `OneRender.WptRunner` on the worker, Release build, six parallel tests.

- `base/`: `main` (7c923e3, with OneHtml 1.1.0, multicol and inline) before the paint work (only the module list changed to add `css-images` and `css-text-decor`).
- `no-font-face/`: the paint branch with the page's `@font-face` rules switched off, to isolate what the paint work does on its own.
- `branch/`: the paint branch as committed (paint work plus `@font-face`).

| Module | base | paint only | paint + `@font-face` | `@font-face` lift (passes) |
|---|---|---|---|---|
| css-backgrounds | 0.2989 (185) | 0.4055 (251) | 0.4152 (257) | +6 |
| css-borders | 0.1809 (17) | 0.1809 (17) | 0.1809 (17) | 0 |
| css-images | 0.5766 (237) | 0.5377 (221) | 0.5377 (221) | 0 |
| css-text-decor | 0.3860 (110) | 0.2632 (75) | 0.2877 (82) | +7 |
| css-text | 0.2477 (343) | 0.2433 (337) | 0.4570 (633) | +296 |
| css-inline | 0.0675 (11) | 0.0798 (13) | 0.0859 (14) | +1 |
| css2-flow | 0.2674 (606) | 0.3142 (712) | 0.3500 (793) | +81 |
| css2-generated | 0.2869 (103) | 0.2591 (93) | 0.2591 (93) | 0 |

Rate = pass / (total - skipped); pass counts in brackets. Where a module's pass count falls (css-images, css2-generated, css-text-decor) the tests that were passing before did so because both the test and the reference painted nothing that differed: a marker, an `<embed>` image, a decoration or a gradient was missing on both sides, or only on one side. Painting those now exposes what is still missing: `list-style-position: inside` markers, `<embed>`/`<object>` images, `cross-fade()`, and the XHTML `<![CDATA[` style sheets that this engine reads as HTML. They are listed under follow-ups in the unit report.
