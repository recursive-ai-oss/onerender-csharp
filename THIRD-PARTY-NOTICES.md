# Third-party notices

OneRender's own code is licensed `MIT OR Apache-2.0`. Material from other projects that ships in a package, or is used by the tests, is listed here with its source, licence and hash.

## web-platform-tests (test use only)

The reftest runner reads the web-platform-tests suite at the commit pinned in `tools/wpt/WPT_COMMIT`. It is fetched by `tools/wpt/fetch-wpt.sh` into a local cache and is not committed. Result files under `testdata/wpt-results/` contain test file names only.

- Source: https://github.com/web-platform-tests/wpt, `LICENSE.md` (3-Clause BSD)
- Copyright: web-platform-tests contributors; tests carry their own authors' notices.

Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

## Chrome for Testing (test use only)

The reftest runner can drive Chrome for Testing 156.0.8078.47 as a black box to calibrate the comparator. It is fetched by `tools/wpt/fetch-chrome.sh` (SHA-256 checked), is never redistributed and is not part of any package.
