# Third-party notices

OneRender's own code is licensed `MIT OR Apache-2.0`. Material from other projects that ships in a package, or is used by the tests, is listed here with its source, licence and hash.

## Fonts in `RecursiveAI.OneRender.Fonts`

Each font file is the unmodified upstream release file. It is embedded in the package assembly, and each family's licence text is shipped beside it as `OFL.txt` (in `licenses/` in the package, and in the repository under `src/OneRender.Fonts/fonts/`). All three families are licensed under the SIL Open Font License, Version 1.1 (<https://openfontlicense.org>). The Reserved Font Names bind only modified versions; the files are never modified. The source URL and SHA-256 of every file are in `src/OneRender.Fonts/fonts/MANIFEST.tsv`, and `tools/fonts/verify-fonts.sh` re-downloads and checks them.

| Family | Version / source | Faces | Copyright |
|---|---|---|---|
| Liberation Sans, Liberation Serif, Liberation Mono | 2.1.5, <https://github.com/liberationfonts/liberation-fonts> | Regular, Bold, Italic, Bold Italic of each | Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine. Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation. |
| Carlito | commit 3a810cab78ebd6e2e4eed42af9e8453c4f9b850a of <https://github.com/googlefonts/carlito> | Regular, Bold, Italic, Bold Italic | Copyright 2013 The Carlito Project Authors (<https://github.com/googlefonts/carlito>), with Reserved Font Name "Carlito" |
| Caladea | commit 336a529cfad3d103d6527752686f8331d13e820a of <https://github.com/huertatipografica/Caladea> | Regular, Bold, Italic, Bold Italic | Copyright 2012 The Caladea Project Authors (<https://github.com/huertatipografica/Caladea>) |

The full licence text for each family is the `OFL.txt` in its folder.

## web-platform-tests (test use only)

The reftest runner reads the web-platform-tests suite at the commit pinned in `tools/wpt/WPT_COMMIT`. It is fetched by `tools/wpt/fetch-wpt.sh` into a local cache and is not committed. Result files under `testdata/wpt-results/` contain test file names only.

- Source: https://github.com/web-platform-tests/wpt, `LICENSE.md` (3-Clause BSD)
- Copyright: web-platform-tests contributors; tests carry their own authors' notices.

Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

## web-platform-tests parsing and computed-value files (committed test data)

`tests/OneRender.Tests/testdata/wpt-block/css/` also holds unmodified copies of one reftest and its reference from `css/CSS2/normal-flow/`, used by the block layout tests. The licence notice below applies to them as well.

`tests/OneRender.Tests/testdata/wpt-floats/css/` holds unmodified copies of web-platform-tests reftests from `css/CSS2/floats/` and `css/CSS2/floats-clear/` with their references (some under `css/CSS2/reference/` and `css/reference/`), taken at the commit in `tools/wpt/WPT_COMMIT`. `tests.txt` beside them is ours. They are used only by the tests, and the licence notice below applies to them as well.

`tests/OneRender.Tests/testdata/wpt-style/css/` holds unmodified copies of the web-platform-tests files named `*-valid.html`, `*-invalid.html` and `*-computed.html` that exercise a property the style pipeline models, taken at the commit in `tools/wpt/WPT_COMMIT` by `tools/wpt/select-style-tests.sh`. `expected-failures.tsv` beside them is ours. They are used only by the tests.

- Source: https://github.com/web-platform-tests/wpt, `LICENSE.md` (3-Clause BSD)
- Copyright: web-platform-tests contributors

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
> 3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## Chrome for Testing (test use only)

The reftest runner can drive Chrome for Testing 156.0.8078.47 as a black box to calibrate the comparator. It is fetched by `tools/wpt/fetch-chrome.sh` (SHA-256 checked), is never redistributed and is not part of any package.

## Chrome geometry differential (test use only)

The tools under `tools/geomdiff/` drive Chrome for Testing 156.0.8078.47 as a black box, on frozen offline pages, and compare its layout with ours. It is fetched by `tools/geomdiff/scripts/fetch-chrome.sh` (SHA-256 checked), is never redistributed and is not part of any package. The pages in `testdata/tailwind/` are our own; they are compiled on the test machine with the Tailwind CSS v4.3.3 standalone CLI (MIT, Copyright (c) Tailwind Labs, Inc.), daisyUI 5.7.50 (MIT, Copyright (c) 2020 Pouya Saadeghi) and Flowbite 4.0.2 (MIT, Copyright (c) 2023 Bergside Inc.), each fetched at a pinned version and hash by `tools/geomdiff/scripts/build-tailwind.sh`. None of their files is committed or shipped; the compiled style sheets stay in the local cache.

- Tailwind CSS: https://github.com/tailwindlabs/tailwindcss, `LICENSE` (MIT)
- daisyUI: https://github.com/saadeghi/daisyui, `LICENSE` (MIT)
- Flowbite: https://github.com/themesberg/flowbite, `LICENSE.md` (MIT)

The MIT notice for each: "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software."
