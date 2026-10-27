# OneRender

An HTML and CSS layout and rendering engine for .NET, written from the specifications. Styled nodes come in from OneHtml's DOM and cascade (or from a C# builder); the engine builds a box tree, lays it out incrementally, and paints a retained display list that backends turn into pixels or a tagged PDF. No JavaScript, no networking, no clock: the host passes time and resolves resources.

The name is provisional. The code is at an early stage: every package below is currently a stub with one public type.

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `RecursiveAI.OneRender` | The engine. | OneHtml, OneFont |
| `RecursiveAI.OneRender.Image` | Display list to pixels. | OneRender, OneImage |
| `RecursiveAI.OneRender.Pdf` | Display list to tagged PDF. | OneRender, OnePdf (core) |
| `RecursiveAI.OneRender.Accessibility` | Rendered page facts (boxes, paint order, text boxes, colors behind text) in OneHtml's `RenderedFacts` shape. See `docs/accessibility-facts.md`. | OneRender, OneHtml.Accessibility |
| `RecursiveAI.OneRender.Fonts` | Default fonts: Liberation, Carlito, Caladea, embedded unmodified, with a family-name lookup. | fonts only |
| `RecursiveAI.OneRender.Razor` | Razor component to PDF or image in one call ([docs/razor.md](docs/razor.md)). | the ASP.NET Core shared framework |

Opt-in script-coverage font packages (Noto scripts, CJK, emoji) are not created yet; they will be added beside `.Fonts`.

Every library targets `net10.0` and is NativeAOT-, trim- and WebAssembly-clean. Family libraries come from NuGet at the versions pinned in `Directory.Packages.props`.

## Build and test

You need the .NET SDK named in `global.json` (a newer feature band also works).

```
dotnet restore OneRender.slnx
dotnet build --no-restore OneRender.slnx
dotnet test --no-build --solution OneRender.slnx
```

Build output goes to `artifacts/` at the repository root, or to the directory in the `ONERENDER_ARTIFACTS` environment variable.

Smoke apps check that the libraries survive ahead-of-time compilation and trimming:

```
dotnet publish tests/OneRender.AotSmoke -c Release -r linux-x64 -p:PublishAot=true -warnaserror
dotnet publish tests/OneRender.WasmSmoke -c Release -warnaserror
```

Use your own runtime identifier for the first command (`win-x64`, `linux-arm64`, `osx-arm64`). The browser-wasm publish needs no workload; installing `wasm-tools` (`dotnet workload install wasm-tools`) only adds ahead-of-time compilation and native relinking, which these checks do not use. Native AOT on Linux needs `clang` and `zlib1g-dev`.

Benchmarks: `dotnet run -c Release --project bench/OneRender.Benchmarks -- --filter '*'`.

The display list backends draw from is specified in `docs/display-list.md`. A game engine that draws the list itself (atlas keys, damage, host scopes, test-harness helpers) starts at `docs/game-hosts.md`. Fuzz targets live in `fuzz/` (`fuzz/run.sh displaylist <seconds>`, needing libFuzzer and SharpFuzz tools, see the script header).

## Licence

`MIT OR Apache-2.0`, copyright Recursive AI, LLC (`LICENSE-MIT`, `LICENSE-APACHE`). The fonts package is `(MIT OR Apache-2.0) AND OFL-1.1`. Bundled third-party material is listed in `THIRD-PARTY-NOTICES.md`.
