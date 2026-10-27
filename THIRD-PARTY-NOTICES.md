# Third-party notices

OneRender's own code is licensed `MIT OR Apache-2.0`. Material from other projects that ships in a package, or is used by the tests, is listed here with its source, licence and hash.

## Chrome geometry differential (test use only)

The tools under `tools/geomdiff/` drive Chrome for Testing 156.0.8078.47 as a black box, on frozen offline pages, and compare its layout with ours. It is fetched by `tools/geomdiff/scripts/fetch-chrome.sh` (SHA-256 checked), is never redistributed and is not part of any package. The pages in `testdata/tailwind/` are our own; they are compiled on the test machine with the Tailwind CSS v4.3.3 standalone CLI (MIT, Copyright (c) Tailwind Labs, Inc.), daisyUI 5.7.50 (MIT, Copyright (c) 2020 Pouya Saadeghi) and Flowbite 4.0.2 (MIT, Copyright (c) 2023 Bergside Inc.), each fetched at a pinned version and hash by `tools/geomdiff/scripts/build-tailwind.sh`. None of their files is committed or shipped; the compiled style sheets stay in the local cache.

- Tailwind CSS: https://github.com/tailwindlabs/tailwindcss, `LICENSE` (MIT)
- daisyUI: https://github.com/saadeghi/daisyui, `LICENSE` (MIT)
- Flowbite: https://github.com/themesberg/flowbite, `LICENSE.md` (MIT)

The MIT notice for each: "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software."
