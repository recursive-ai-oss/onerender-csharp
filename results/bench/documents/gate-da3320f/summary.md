# Document gate summary (gate)

Times are milliseconds, memory is megabytes. `process` is a fresh process per run (the cold start included; onerender as a NativeAOT executable, and under the JIT for information); `warm` is the converter already running.

| document | pages (ours / Chrome / WeasyPrint) | tool | process ms | warm ms | peak PSS MB | peak RSS-sum MB | allocated MB (warm, ours) |
|---|---|---|---|---|---|---|---|
| invoice | 2 / 2 / 3 | onerender (native) | 46.4 | 20.8 | 30 | 32 | 8 |
|  |  | onerender (JIT process) | 433 | - | 51 | 79 | - |
|  |  | Chrome | 193 | 62.9 | 385 | 1093 | - |
|  |  | WeasyPrint | 558 | - | 58 | 66 | - |
| report | 50 / 50 / 50 | onerender (native) | 142 | 127 | 66 | 75 | 51 |
|  |  | onerender (JIT process) | 819 | - | 73 | 113 | - |
|  |  | Chrome | 397 | 214 | 451 | 1307 | - |
|  |  | WeasyPrint | 1751 | - | 95 | 101 | - |
| table10k | 213 / 213 / 218 | onerender (native) | 1811 | 1250 | 305 | 313 | 835 |
|  |  | onerender (JIT process) | 3199 | - | 321 | 350 | - |
|  |  | Chrome | 6527 | 6869 | 2094 | 2919 | - |
|  |  | WeasyPrint | 28555 | - | 903 | 910 | - |
| tailwind | 5 / 5 / 7 | onerender (native) | 163 | 180 | 120 | 137 | 85 |
|  |  | onerender (JIT process) | 782 | - | 142 | 175 | - |
|  |  | Chrome | 353 | 177 | 416 | 1262 | - |
|  |  | WeasyPrint | 855 | - | 61 | 68 | - |

## Verdicts against Chrome

- invoice: WIN
- report: WIN
- table10k: WIN
- tailwind: behind on warm time

Machine, versions and load: see `machine.txt`.
