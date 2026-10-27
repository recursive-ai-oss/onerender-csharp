# Document gate summary (gate)

Times are milliseconds, memory is megabytes. `process` is a fresh process per run (the cold start included; onerender as a NativeAOT executable, and under the JIT for information); `warm` is the converter already running.

| document | pages (ours / Chrome / WeasyPrint) | tool | process ms | warm ms | peak PSS MB | peak RSS-sum MB | allocated MB (warm, ours) |
|---|---|---|---|---|---|---|---|
| invoice | 2 / 2 / 3 | onerender (native) | 51.1 | 31.3 | 30 | 32 | 8 |
|  |  | onerender (JIT process) | 584 | - | 44 | 79 | - |
|  |  | Chrome | 194 | 59.5 | 369 | 1007 | - |
|  |  | WeasyPrint | 630 | - | 59 | 66 | - |
| report | 50 / 50 / 50 | onerender (native) | 245 | 147 | 58 | 75 | 51 |
|  |  | onerender (JIT process) | 1085 | - | 84 | 113 | - |
|  |  | Chrome | 386 | 207 | 439 | 1305 | - |
|  |  | WeasyPrint | 1695 | - | 95 | 101 | - |
| table10k | 213 / 213 / 218 | onerender (native) | 3246 | 1643 | 304 | 316 | 838 |
|  |  | onerender (JIT process) | 4215 | - | 332 | 368 | - |
|  |  | Chrome | 6180 | 5752 | 2065 | 2912 | - |
|  |  | WeasyPrint | 28143 | - | 903 | 909 | - |
| tailwind | 5 / 5 / 7 | onerender (native) | 122 | 101 | 30 | 68 | 34 |
|  |  | onerender (JIT process) | 771 | - | 73 | 104 | - |
|  |  | Chrome | 317 | 174 | 417 | 1269 | - |
|  |  | WeasyPrint | 973 | - | 61 | 68 | - |

## Verdicts against Chrome

- invoice: WIN
- report: WIN
- table10k: WIN
- tailwind: WIN

Machine, versions and load: see `machine.txt`.
