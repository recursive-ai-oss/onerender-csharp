# Pixel similarity against Chrome

Information only: no gate depends on it. It shows how close our pixels are to Chrome's on the pages the geometry differential uses, and where they differ most. The tool is the `pixels` command of `tools/geomdiff` (`Pixels.cs`, `Ssim.cs`), run per set by `tools/geomdiff/scripts/run-pixels.sh`; the summaries are in `results/pixels/<set>/` (`summary.md` and `pages.tsv`, one row per page and viewport).

## Method

- **Chrome side:** the frozen page is replayed offline exactly as for the geometry capture (same Chrome for Testing 156.0.8078.47 flags, fonts through the private fontconfig file, 1280×800 and 390×844, device scale factor 1, settled page: images loaded, animations at their end, `content-visibility` forced visible). The screenshot is `Page.captureScreenshot` with `captureBeyondViewport` over the whole document, from the top left, cropped to 4000 CSS px tall (`--max-height`).
- **Our side:** the same HTML and resources through `HtmlPage`, `Painter` and `DisplayListRasterizer` (what `HtmlRender.ToImage` does), with the layout viewport at the capture's size, so `vh` and percentages resolve as in Chrome, and an image as tall as the screenshot, on a white page.
- **Score:** SSIM (Wang et al. 2004) on Rec. 601 luma, uniform 8×8 windows moved 4 px, K1 = 0.01, K2 = 0.03, range 255, mean over windows; 1 is identical. `pages.tsv` also gives the share of pixels whose RGB differs at all, which is harsh: one pixel of anti-aliasing counts.

## Results

Chrome for Testing 156.0.8078.47, font set from the `.Fonts` package files, engine at the commit that adds this page.

| Set | Viewport | Pages | Mean SSIM | Min SSIM | Lowest page |
|---|---|---|---|---|---|
| text-block | 1280×800 | 18 | 0.9885 | 0.9694 | field-notes |
| text-block | 390×844 | 18 | 0.9749 | 0.9504 | field-notes |
| templates | 1280×800 | 6 | 0.9880 | 0.9822 | report |
| templates | 390×844 | 6 | 0.9793 | 0.9596 | report |
| tailwind | 1280×800 | 7 | 0.9725 | 0.9272 | flowbite-components |
| tailwind | 390×844 | 7 | 0.9506 | 0.8709 | flowbite-components |
| chrome-probes | 1280×800 | 9 | 0.9998 | 0.9996 | foot |
| chrome-probes | 390×844 | 9 | 0.9995 | 0.9990 | foot |
| grid-lab | 1280×800 | 6 | 1.0000 | 1.0000 | (all identical) |
| grid-lab | 390×844 | 6 | 1.0000 | 1.0000 | (all identical) |
| range-parts | 1280×800 | 6 | 0.9988 | 0.9962 | native |
| range-parts | 390×844 | 6 | 0.9963 | 0.9879 | native |

What the lowest page shows (looked at side by side, `flowbite-components` at 390×844): the form controls (inputs, checkbox, password field) are not drawn in Chrome's default style yet, a card header that Chrome paints with a gradient is plain white in ours (a Tailwind 4 gradient utility; the cause is not yet traced), and the timeline dots are squares. Pages of the other sets were not inspected one by one; their layout agrees with Chrome's (geometry 100% and 99.97%), so the remaining difference is mostly glyph and edge anti-aliasing.

## Reproduce

On a host with the geometry differential set up (see `geometry-differential.md`), with the frozen sets under `$ONERENDER_HOME/corpus/<set>`:

```
tools/geomdiff/scripts/build.sh
NO_SANDBOX=1 JOBS=4 tools/geomdiff/scripts/run-pixels.sh tailwind ~/onerender/corpus/tailwind results/pixels [dump-dir]
```

With a dump directory, `<page>-<viewport>-chrome.png` and `-ours.png` are written for looking at by eye; they are not committed.
