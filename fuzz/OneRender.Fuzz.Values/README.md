# Fuzz harness for the style value parsers and the tree builders

Targets (`dotnet OneRender.Fuzz.Values.dll --list` is not needed; run without arguments for usage):

- `values`: first byte picks a property (odd bytes: the text is UTF-16), the rest is a value declared on a parent and a child, computed through the builder front end, then a box tree is built.
- `tree`: bytes are operations that grow a random styled tree with small limits; a second build into the retained arenas must give the same box dump.
- `html`: bytes are an HTML document (a form feed starts an author style sheet); the HTML front end and box tree run on it.
- `layout`: as `html`, then block layout (floats, clearance, boxes beside floats) at a width taken from the first byte; laying out again into the retained fragment tree and into a fresh one must give the same fragments.

A target must never throw. Modes: `<target> <file>` replays one input; `<target> --mutate <seconds> <seed-dir>` is a coverage-blind mutation loop; `<target>` alone is the libFuzzer target under SharpFuzz. `run.sh <target> <seconds> <seed-dir> <out-dir>` publishes, instruments `OneRender.dll` and runs the libFuzzer driver (the prebuilt `libfuzzer-dotnet`, pinned by tag and SHA-256 in `setup.sh`), user-local, no sudo. Seeds are in `seeds/<target>/`.
