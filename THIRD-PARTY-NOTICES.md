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

## Fonts in the opt-in packages

The script, CJK and emoji packages (`RecursiveAI.OneRender.Fonts.Noto.<Script>`, `.Fonts.Cjk`, `.Fonts.Emoji`) ship unmodified upstream release files, embedded in the package assembly. Each package carries the upstream licence text as `OFL.txt` (in `licenses/` in the package, and in the repository under `src/OneRender.Fonts.<name>/fonts/`) and a `MANIFEST.tsv` with every file's source URL and SHA-256. All of them are licensed under the SIL Open Font License 1.1: "This Font Software is licensed under the SIL Open Font License, Version 1.1." None declares a Reserved Font Name for the files shipped, and none is modified (the PDF writer subsets at output time, which the licence allows for documents). `tools/fonts/optional_fonts.py verify` re-downloads every file and compares it with the manifest and the committed bytes.

| Package | Families | Release | Faces | Copyright |
|---|---|---|---|---|
| `RecursiveAI.OneRender.Fonts.Noto.Armenian` | Noto Sans Armenian | [NotoSansArmenian-v2.008](https://github.com/notofonts/armenian/releases/tag/NotoSansArmenian-v2.008) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/armenian>) |
| `RecursiveAI.OneRender.Fonts.Noto.Hebrew` | Noto Sans Hebrew | [NotoSansHebrew-v3.001](https://github.com/notofonts/hebrew/releases/tag/NotoSansHebrew-v3.001) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/hebrew>) |
| `RecursiveAI.OneRender.Fonts.Noto.Arabic` | Noto Sans Arabic | [NotoSansArabic-v2.013](https://github.com/notofonts/arabic/releases/tag/NotoSansArabic-v2.013) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/arabic>) |
| `RecursiveAI.OneRender.Fonts.Noto.Syriac` | Noto Sans Syriac | [NotoSansSyriac-v3.000](https://github.com/notofonts/syriac/releases/tag/NotoSansSyriac-v3.000) | Regular | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/syriac>) |
| `RecursiveAI.OneRender.Fonts.Noto.Thaana` | Noto Sans Thaana | [NotoSansThaana-v3.001](https://github.com/notofonts/thaana/releases/tag/NotoSansThaana-v3.001) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/thaana>) |
| `RecursiveAI.OneRender.Fonts.Noto.Nko` | Noto Sans NKo | [NotoSansNKo-v2.004](https://github.com/notofonts/nko/releases/tag/NotoSansNKo-v2.004) | Regular | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/nko>) |
| `RecursiveAI.OneRender.Fonts.Noto.Devanagari` | Noto Sans Devanagari | [NotoSansDevanagari-v2.007](https://github.com/notofonts/devanagari/releases/tag/NotoSansDevanagari-v2.007) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/devanagari>) |
| `RecursiveAI.OneRender.Fonts.Noto.Bengali` | Noto Sans Bengali | [NotoSansBengali-v3.011](https://github.com/notofonts/bengali/releases/tag/NotoSansBengali-v3.011) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/bengali>) |
| `RecursiveAI.OneRender.Fonts.Noto.Gurmukhi` | Noto Sans Gurmukhi | [NotoSansGurmukhi-v2.004](https://github.com/notofonts/gurmukhi/releases/tag/NotoSansGurmukhi-v2.004) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/gurmukhi>) |
| `RecursiveAI.OneRender.Fonts.Noto.Gujarati` | Noto Sans Gujarati | [NotoSansGujarati-v2.106](https://github.com/notofonts/gujarati/releases/tag/NotoSansGujarati-v2.106) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/gujarati>) |
| `RecursiveAI.OneRender.Fonts.Noto.Oriya` | Noto Sans Oriya | [NotoSansOriya-v2.007](https://github.com/notofonts/oriya/releases/tag/NotoSansOriya-v2.007) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/oriya>) |
| `RecursiveAI.OneRender.Fonts.Noto.Tamil` | Noto Sans Tamil | [NotoSansTamil-v2.004](https://github.com/notofonts/tamil/releases/tag/NotoSansTamil-v2.004) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/tamil>) |
| `RecursiveAI.OneRender.Fonts.Noto.Telugu` | Noto Sans Telugu | [NotoSansTelugu-v2.005](https://github.com/notofonts/telugu/releases/tag/NotoSansTelugu-v2.005) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/telugu>) |
| `RecursiveAI.OneRender.Fonts.Noto.Kannada` | Noto Sans Kannada | [NotoSansKannada-v2.006](https://github.com/notofonts/kannada/releases/tag/NotoSansKannada-v2.006) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/kannada>) |
| `RecursiveAI.OneRender.Fonts.Noto.Malayalam` | Noto Sans Malayalam | [NotoSansMalayalam-v2.104](https://github.com/notofonts/malayalam/releases/tag/NotoSansMalayalam-v2.104) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/malayalam>) |
| `RecursiveAI.OneRender.Fonts.Noto.Sinhala` | Noto Sans Sinhala | [NotoSansSinhala-v3.000](https://github.com/notofonts/sinhala/releases/tag/NotoSansSinhala-v3.000) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/sinhala>) |
| `RecursiveAI.OneRender.Fonts.Noto.Thai` | Noto Sans Thai | [NotoSansThai-v2.002](https://github.com/notofonts/thai/releases/tag/NotoSansThai-v2.002) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/thai>) |
| `RecursiveAI.OneRender.Fonts.Noto.Lao` | Noto Sans Lao | [NotoSansLao-v2.003](https://github.com/notofonts/lao/releases/tag/NotoSansLao-v2.003) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/lao>) |
| `RecursiveAI.OneRender.Fonts.Noto.Tibetan` | Noto Serif Tibetan | [NotoSerifTibetan-v2.103](https://github.com/notofonts/tibetan/releases/tag/NotoSerifTibetan-v2.103) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/tibetan>) |
| `RecursiveAI.OneRender.Fonts.Noto.Myanmar` | Noto Sans Myanmar | [NotoSansMyanmar-v2.107](https://github.com/notofonts/myanmar/releases/tag/NotoSansMyanmar-v2.107) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/myanmar>) |
| `RecursiveAI.OneRender.Fonts.Noto.Georgian` | Noto Sans Georgian | [NotoSansGeorgian-v2.005](https://github.com/notofonts/georgian/releases/tag/NotoSansGeorgian-v2.005) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/georgian>) |
| `RecursiveAI.OneRender.Fonts.Noto.Ethiopic` | Noto Sans Ethiopic | [NotoSansEthiopic-v2.102](https://github.com/notofonts/ethiopic/releases/tag/NotoSansEthiopic-v2.102) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/ethiopic>) |
| `RecursiveAI.OneRender.Fonts.Noto.Cherokee` | Noto Sans Cherokee | [NotoSansCherokee-v2.001](https://github.com/notofonts/cherokee/releases/tag/NotoSansCherokee-v2.001) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/cherokee>) |
| `RecursiveAI.OneRender.Fonts.Noto.Khmer` | Noto Sans Khmer | [NotoSansKhmer-v2.004](https://github.com/notofonts/khmer/releases/tag/NotoSansKhmer-v2.004) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/khmer>) |
| `RecursiveAI.OneRender.Fonts.Noto.Mongolian` | Noto Sans Mongolian | [NotoSansMongolian-v3.103](https://github.com/notofonts/mongolian/releases/tag/NotoSansMongolian-v3.103) | Regular | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/mongolian>) |
| `RecursiveAI.OneRender.Fonts.Noto.Balinese` | Noto Sans Balinese | [NotoSansBalinese-v2.006](https://github.com/notofonts/balinese/releases/tag/NotoSansBalinese-v2.006) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/balinese>) |
| `RecursiveAI.OneRender.Fonts.Noto.Javanese` | Noto Sans Javanese | [NotoSansJavanese-v2.005](https://github.com/notofonts/javanese/releases/tag/NotoSansJavanese-v2.005) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/javanese>) |
| `RecursiveAI.OneRender.Fonts.Noto.Symbols` | Noto Sans Symbols, Noto Sans Symbols 2 | [NotoSansSymbols-v2.003](https://github.com/notofonts/symbols/releases/tag/NotoSansSymbols-v2.003), [NotoSansSymbols2-v2.008](https://github.com/notofonts/symbols/releases/tag/NotoSansSymbols2-v2.008) | Regular, Bold | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/symbols>) |
| `RecursiveAI.OneRender.Fonts.Noto.Math` | Noto Sans Math | [NotoSansMath-v3.000](https://github.com/notofonts/math/releases/tag/NotoSansMath-v3.000) | Regular | Copyright 2022 The Noto Project Authors (<https://github.com/notofonts/math>) |
| `RecursiveAI.OneRender.Fonts.Cjk` | Noto Sans CJK JP, KR, SC, TC, HK | [Sans2.004](https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004) (commit 523d033d6cb47f4a80c58a35753646f5c3608a78), asset `03_NotoSansCJK-OTC.zip` | Regular, Bold (the `.ttc` collections, unmodified; the Mono faces in them are not registered) | The fonts declare "© 2014-2021 Adobe (http://www.adobe.com/)."; the licence file carries no copyright line |
| `RecursiveAI.OneRender.Fonts.Emoji` | Noto Color Emoji | [v2.051](https://github.com/googlefonts/noto-emoji/releases/tag/v2.051) (commit 8998f5dd683424a73e2314a8c1f1e359c19e8742), `fonts/NotoColorEmoji_WindowsCompatible.ttf` | Regular (color bitmaps) | The font declares "Copyright 2022 Google Inc."; the licence file carries no copyright line |

The Noto script files are the `unhinted/ttf` files of the release archives published on the project's GitHub releases page. The emoji repository's README states: "Emoji fonts (under the fonts subdirectory) are under the SIL Open Font License, version 1.1. Tools and most image resources are under the Apache license, version 2.0." Only the font file is shipped.

The copyright line in a Noto script package's `OFL.txt` is the repository's ("Copyright 2022 The Noto Project Authors"). The fonts' own copyright strings differ in year for Bengali (2025), Hebrew (2024) and Mongolian (2023), and Noto Sans Math declares "Copyright 2022 Google LLC. All Rights Reserved." under the same licence.

## web-platform-tests (test use only)

The reftest runner reads the web-platform-tests suite at the commit pinned in `tools/wpt/WPT_COMMIT`. It is fetched by `tools/wpt/fetch-wpt.sh` into a local cache and is not committed. Result files under `testdata/wpt-results/` contain test file names only.

- Source: https://github.com/web-platform-tests/wpt, `LICENSE.md` (3-Clause BSD)
- Copyright: web-platform-tests contributors; tests carry their own authors' notices.

Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

## web-platform-tests parsing and computed-value files (committed test data)

`tests/OneRender.Tests/testdata/wpt-block/css/` also holds unmodified copies of one reftest and its reference from `css/CSS2/normal-flow/`, used by the block layout tests. The licence notice below applies to them as well.

`tests/OneRender.Tests/testdata/wpt-floats/css/` holds unmodified copies of web-platform-tests reftests from `css/CSS2/floats/` and `css/CSS2/floats-clear/` with their references (some under `css/CSS2/reference/` and `css/reference/`), taken at the commit in `tools/wpt/WPT_COMMIT`. `tests.txt` beside them is ours. They are used only by the tests, and the licence notice below applies to them as well.

`tests/OneRender.Tests/testdata/wpt-style/css/` holds unmodified copies of the web-platform-tests files named `*-valid.html`, `*-invalid.html` and `*-computed.html` that exercise a property the style pipeline models, taken at the commit in `tools/wpt/WPT_COMMIT` by `tools/wpt/select-style-tests.sh`. `expected-failures.tsv` beside them is ours. They are used only by the tests.

`tests/OneRender.Tests/testdata/wpt-motion/css/` holds unmodified copies of the web-platform-tests testharness files of `css/css-transitions` and `css/css-animations` that the tests can replay without a script engine (the parsing files and the files that call the interpolation helpers), taken at the commit in `tools/wpt/WPT_COMMIT`. `excluded.tsv`, `expected-failures.tsv` and `floors.tsv` beside them are ours. They are used only by the tests, and the licence notice below applies to them as well.

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

## Noto Sans subsets (test use only)

`testdata/fonts/` holds small Noto Sans test fonts (Latin/Greek/Cyrillic and bidi coverage, Arabic, Hebrew) used by the text tests and the font-data fuzz seeds. They are never part of any package.

- Source: Google Noto fonts, <https://github.com/notofonts> and <https://github.com/googlefonts/noto-fonts>, licensed under the SIL Open Font License 1.1; the copyright lines and licence text are in `testdata/fonts/OFL.txt`.
- Modified: the `.ttf` files are subsets of the upstream fonts (fewer glyphs, hinting removed); `NotoSansHebrew-Subset-Regular.woff` and `.woff2` are format conversions of the Hebrew subset made by `testdata/fonts/make-web-fonts.py` (fontTools). The subsets were taken from the OneFont test data.
- `NotoSans-Bidi-Subset.ttf` is modified further: it merges the Noto Sans, Noto Sans Arabic and Noto Sans Hebrew subsets into one font.
- SHA-256: `NotoSans-Bidi-Subset.ttf` 7e55e394…cbef520, `NotoSansArabic-Subset-Regular.ttf` dc004195…ce0cae1, `NotoSansHebrew-Subset-Regular.ttf` 89d6ab25…d1e898 (full values: `sha256sum testdata/fonts/*`).

## Sample style sheet

`samples/InvoiceApp/wwwroot/css/app.css` is compiled by the Tailwind CSS v4.3.3 standalone CLI (MIT, Copyright (c) Tailwind Labs, Inc.,
https://github.com/tailwindlabs/tailwindcss, `LICENSE`). The file keeps the CLI's `/*! tailwindcss v4.3.3 | MIT License | https://tailwindcss.com */`
header and is not part of any package.
