# WPT reftest runner and results

`tools/OneRender.WptRunner` runs the [web-platform-tests](https://github.com/web-platform-tests/wpt) reftests of the modules listed in `tools/wpt/modules.json` and records per-module pass rates. Layout and paint work is gated on those rates. The runner talks to the engine through one seam, `OneRender.Image.HtmlRenderSeam.Render(html, uri, width, height, resources)`, which returns an RGBA image. The seam runs the whole pipeline: parse, cascade, box tree, layout, paint, rasterize.

## Reftest rules implemented

From the WPT reftest documentation:

- The page is rendered in an 800x600 viewport. A test and its reference are compared pixel for pixel.
- A test may link several references with `<link rel=match>` and `<link rel=mismatch>`. It passes when at least one match reference matches and every mismatch reference differs.
- A match reference that links references of its own continues a chain, which must also hold. Chains do not continue through a mismatch reference, and a reference already being checked up the chain counts as holding.
- `<meta name=fuzzy content="[ref:]maxDifference=a[-b];totalPixels=c[-d]">`, names optional. The maximum difference is the largest per-channel (R, G, B) absolute difference; the pixel count is the number of pixels differing in any of those channels. A single number n means exactly n, a range is inclusive, and a `ref:` prefix limits the allowance to that reference. Without fuzzy the images must be identical. Images of different sizes never match.

## Outcomes

| Outcome | Meaning |
|---|---|
| `pass` | The comparison holds and the test image has painted content. |
| `fail` | The comparison does not hold, or rendering threw (reason `error: ...`). |
| `blank` | The comparison holds but the test image is a single flat colour of nothing: fully transparent or opaque white throughout. A renderer that draws nothing therefore never passes. Some real tests ("passes if nothing red is visible") also land here, for every renderer, Chrome included. |
| `skipped` | The runner cannot run the test; the reason is recorded. |

Skip reasons: `testharness.js test`, `script-dependent` (script in the test or any reference), `reftest-wait (needs script)`, `print reftest (paged output)` (file name ends `-print` or a directory named `print`; paged comparison arrives with paged media), `reference file missing: <path>`.

Rate = pass / (total - skipped), or 0 when nothing was runnable.

## Module list

`tools/wpt/modules.json` maps each module id to WPT directories. Module ids are the `css/` directory names, except CSS 2 which is split:

| Module | Directories under `css/` |
|---|---|
| css2-flow | CSS2/normal-flow, box-display, box, margin-padding-clear, linebox, visudet, visuren, text, bidi-text |
| css2-floats | CSS2/floats, floats-clear |
| css2-positioning | CSS2/positioning, abspos, zindex, stacking-context, zorder, visufx |
| css2-tables | CSS2/tables |
| css2-generated | CSS2/generate, generated-content, lists |
| css-flexbox, css-grid, css-tables, css-position, css-backgrounds, css-borders, css-values, css-color, css-fonts, css-text, css-inline, css-overflow, css-transforms, css-transitions, css-animations, css-break, css-page, css-multicol, css-ui | the directory of the same name |

A test is a `.html`, `.htm`, `.xht`, `.xhtml` or `.svg` file under those directories that links a reference or loads testharness.js. Files in directories named `support`, `resources`, `reference`, `references`, `tools` are never tests.

## Fetching and running

```
tools/wpt/fetch-wpt.sh            # pinned commit in tools/wpt/WPT_COMMIT -> $ONERENDER_CACHE/wpt/<commit>/ (default ~/onerender/cache)
tools/wpt/fetch-chrome.sh         # Chrome for Testing 156.0.8078.47, SHA-256 checked -> $ONERENDER_CACHE/chrome/156.0.8078.47/
dotnet build tools/OneRender.WptRunner -c Release
R=<artifacts>/bin/OneRender.WptRunner/release/OneRender.WptRunner.dll
W=~/onerender/cache/wpt/$(cat tools/wpt/WPT_COMMIT)

dotnet $R run --wpt $W --renderer onerender --out testdata/wpt-results                       # whole module list, engine seam (seconds)
dotnet $R run --wpt $W --renderer chrome --sample 400 --parallel 6 --out testdata/wpt-results   # Chrome calibration sample
dotnet $R run --wpt $W --renderer onerender --module css-flexbox                             # one module, console only
dotnet $R run --wpt $W --renderer onerender --module css-flexbox --filter /abspos/           # a subset by path
dotnet $R run --wpt $W --renderer onerender --test css/css-flexbox/abspos/abspos-autopos-htb-ltr.html --dump dumps   # one test, PNGs of every image
```

Run from the repository root (the default module file is `tools/wpt/modules.json`; `--modules` overrides). Without `--out` nothing is written. `--sample n` takes every Nth test in path order so a module has at most about n tests; the stride is recorded in the results.

Chrome mode serves the checkout over loopback HTTP, drives headless Chrome over the DevTools protocol (one tab per `--parallel`), waits for load and web fonts, and takes the first screenshot that equals the one before it. It is a black box used to check the runner, never an engine input. `--fonts <dir>` restricts Chrome to the fonts in a directory through a private fontconfig file.

## XHTML tests

Files ending in `.xht` or `.xhtml` are XML documents in the suite, and many of them (and most of the references of CSS2) write their stylesheet as `<style><![CDATA[ ... ]]></style>`. The engine's front end is an HTML parser, which reads that as a broken selector and drops the first rule, so those pages used to render without their style and tests compared two blank pages. The runner therefore rewrites these files before handing them to the engine (`tools/OneRender.WptRunner/XhtmlSource.cs`):

- CDATA sections become their character data (XML 1.0 §2.7): verbatim inside `style` and `script`, escaped text elsewhere.
- An empty-element tag on a non-void element (`<div/>`, `<td />`) becomes a start and an end tag (XML 1.0 §3.1), since HTML ignores the slash and would nest what follows.
- The XML declaration and other processing instructions are dropped.

This is **not a conforming XML parser**: nothing is checked for well-formedness, there is no namespace processing, no DTD entity declarations, and no XML tree building (an XML document gets no implicit `html`, `head`, `body` or `tbody`; the HTML parser still adds them). Documents that depend on those differences can still pass or fail for the wrong reason. The rewrite lives in the harness, not the engine; `HtmlRender` and `HtmlPage` take HTML only.

## Results layout

```
testdata/wpt-results/<wpt-commit>/<renderer>/<module>.json   one per module
testdata/wpt-results/<wpt-commit>/<renderer>/summary.json     table of all module files in the directory
testdata/wpt-results/<wpt-commit>/<renderer>/summary.md       the same as a table, plus the most common failure and skip reasons
```

`<renderer>` is `onerender` (the engine through the seam) or `chrome`. The directory `stub` holds the zero baseline from before the engine could draw. Re-running a subset into the same directory rewrites those module files and regenerates the summaries from all files present. Module files list tests in ordinal path order so runs diff cleanly.

Module file, `formatVersion` 1:

```json
{
  "formatVersion": 1,
  "wptCommit": "<40 hex>",
  "renderer": "onerender|chrome",
  "rendererVersion": "Chrome/156.0.8078.47",
  "module": "css-flexbox",
  "discovered": 1398,
  "sampleStride": 1,
  "counts": { "total": 1398, "pass": 0, "fail": 0, "blank": 952, "skipped": 446 },
  "rate": "0.0000",
  "pass": ["css/css-flexbox/a.html"],
  "blank": ["css/css-flexbox/b.html"],
  "fail": { "css/css-flexbox/c.html": "== c-ref.html differs (max 255, 733 px)" },
  "skipped": { "css/css-flexbox/d.html": "testharness.js test" }
}
```

- `discovered` is the module's test count before sampling; `total` is how many were run (equal unless `sampleStride` > 1).
- Test ids are WPT-relative paths.
- Failure reasons: `== <ref> differs (max <largest channel difference>, <n> px)`, `!= <ref> identical`, `error: <exception>: <message>`.
- `rate` is a string with four decimals.

Floors are set against `<module>.json` of the `onerender` renderer: a floor is a minimum `pass` count or `rate`. Changing `formatVersion` means a new number, never a silent change.

## Calibration against Chrome

Chrome for Testing 156.0.8078.47 on Linux, headless, system fonts, sample stride so each module has at most about 400 tests. It shows the comparator, fuzzy handling and chains behave as WPT's own runner does: where Chrome fails, the reasons are Chrome or test conditions, not the runner. Numbers are in `testdata/wpt-results/<commit>/chrome/summary.md`: 7,326 sampled tests, 4,096 pass, 503 fail, 7 blank, 2,720 skipped, rate 0.889 (0.935 without the 227 failures in `grid-lanes` and `line-clamp`, which Chrome does not ship by default). Two runs of the same sample can differ by a test or two under heavy host load (a late-loading image or font), so compare Chrome numbers to a percent, not a test. The failures concentrate in:

- features Chrome ships only behind flags or not yet (`css-grid/grid-lanes`, `css-overflow/line-clamp` block ellipsis, `css-break` grid-lanes, `css-inline` initial-letter and text-box-trim, `css-borders` corner-shape);
- tests whose expectation does not match Chrome's current behaviour (for example `css-backgrounds/background-size/vector`: Chrome sizes the viewBox-only SVG at 256 px tall where the reference draws 128 px);
- text tests that depend on installed fonts rather than the Ahem web font (`content-*`, `table-anonymous-objects-*`, hyphenation without dictionaries, control characters).
