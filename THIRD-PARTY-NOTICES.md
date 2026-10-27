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
