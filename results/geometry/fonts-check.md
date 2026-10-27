# Fonts check

Browser: Chrome/156.0.8078.47  
Font set: 8768bf2774f6c8e8 (20 files)

| font-family as written | expected | Chrome used (family: glyphs) | ok |
|---|---|---|---|
| `Arial` | Liberation Sans | Liberation Sans: 26 | yes |
| `Helvetica` | Liberation Sans | Liberation Sans: 26 | yes |
| `sans-serif` | Liberation Sans | Liberation Sans: 26 | yes |
| `system-ui` | Liberation Sans | Liberation Sans: 26 | yes |
| `ui-sans-serif` | Liberation Sans | Liberation Sans: 26 | yes |
| `Times New Roman` | Liberation Serif | Liberation Serif: 26 | yes |
| `Times` | Liberation Serif | Liberation Serif: 26 | yes |
| `serif` | Liberation Serif | Liberation Serif: 26 | yes |
| `ui-serif` | Liberation Serif | Liberation Serif: 26 | yes |
| `Courier New` | Liberation Mono | Liberation Mono: 26 | yes |
| `Courier` | Liberation Mono | Liberation Mono: 26 | yes |
| `monospace` | Liberation Mono | Liberation Mono: 26 | yes |
| `ui-monospace` | Liberation Mono | Liberation Mono: 26 | yes |
| `Calibri` | Carlito | Carlito: 25 | yes |
| `Cambria` | Caladea | Caladea: 26 | yes |
| `cursive` | Liberation Serif | Liberation Serif: 26 | yes |
| `fantasy` | Liberation Serif | Liberation Serif: 26 | yes |
| `Liberation Sans` | Liberation Sans | Liberation Sans: 26 | yes |
| `Carlito` | Carlito | Carlito: 25 | yes |
| `Caladea` | Caladea | Caladea: 26 | yes |
| `Helvetica Neue, sans-serif` | Liberation Sans | Liberation Sans: 26 | yes |
| `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif` | Liberation Sans | Liberation Sans: 26 | yes |
| `ui-sans-serif, system-ui, sans-serif, "Apple Color Emoji"` | Liberation Sans | Liberation Sans: 26 | yes |
| `ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace` | Liberation Mono | Liberation Mono: 26 | yes |
| `Georgia, serif` | Liberation Serif | Liberation Serif: 26 | yes |
| `default (no font-family)` | Liberation Serif | Liberation Serif: 26 | yes |
| `Arial bold italic` | Liberation Sans | Liberation Sans: 15 | yes |
| `Calibri bold` | Carlito | Carlito: 14 | yes |

Result: every node rendered only with shipped faces, as expected.
