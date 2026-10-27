# WPT before and after the second paint area

Single full runs (no sampling) of the pinned WPT commit through `OneRender.WptRunner` on the worker, Release build, six parallel tests.

- `base/`: `main` before the paint work (only the module list changed to add `css-images` and `css-text-decor`).
- `no-font-face/`: the paint branch with the page's `@font-face` rules switched off, to isolate what the paint work does on its own.
- `branch/`: the paint branch as committed (paint work plus `@font-face`).

| Module | base | paint only | paint + `@font-face` | `@font-face` lift (passes) |
|---|---|---|---|---|
| css-backgrounds | 0.2908 (180) | 0.3926 (243) | 0.4023 (249) | +6 |
| css-borders | 0.1809 (17) | 0.1809 (17) | 0.1809 (17) | 0 |
| css-images | 0.5766 (237) | 0.5377 (221) | 0.5377 (221) | 0 |
| css-text-decor | 0.3404 (97) | 0.2667 (76) | 0.2877 (82) | +6 |
| css-text | 0.2166 (300) | 0.2123 (294) | 0.4260 (590) | +296 |
| css-inline | 0.0675 (11) | 0.0798 (13) | 0.0859 (14) | +1 |
| css2-flow | 0.2507 (568) | 0.2974 (674) | 0.3327 (754) | +80 |
| css2-generated | 0.2841 (102) | 0.2563 (92) | 0.2563 (92) | 0 |

Rate = pass / (total - skipped); pass counts in brackets. Where a module's pass count falls (css-images, css2-generated, css-text-decor, one test in css-borders) the tests that were passing before did so because both the test and the reference painted nothing that differed: a marker, an `<embed>` image, a decoration or a gradient was missing on both sides, or only on one side. Painting those now exposes what is still missing: `list-style-position: inside` markers, `<embed>`/`<object>` images, `cross-fade()`, and the XHTML `<![CDATA[` style sheets that this engine reads as HTML. They are listed under follow-ups in the unit report.
