# Document gate summary (smoke)

**Smoke run: no lock, short job, shared host. Not publishable; the numbers only show where things stand.**

Times are milliseconds, memory is megabytes. `process` is a fresh process per run (the cold start included; onerender as a NativeAOT executable, and under the JIT for information); `warm` is the converter already running.

| document | pages (ours / Chrome / WeasyPrint) | tool | process ms | warm ms | peak PSS MB | peak RSS-sum MB | allocated MB (warm, ours) |
|---|---|---|---|---|---|---|---|
| invoice | 2 / 2 / 3 | onerender (native) | 40.2 | 18.2 | 29 | 31 | 8 |
|  |  | onerender (JIT process) | 415 | - | 50 | 79 | - |
|  |  | Chrome | 204 | 57.0 | 395 | 1194 | - |
|  |  | WeasyPrint | 532 | - | 48 | 66 | - |
| report | 50 / 50 / 50 | onerender (native) | 148 | 155 | 65 | 70 | 51 |
|  |  | onerender (JIT process) | 783 | - | 79 | 113 | - |
|  |  | Chrome | 355 | 212 | 472 | 1479 | - |
|  |  | WeasyPrint | 1513 | - | 89 | 101 | - |
| table10k | 213 / 213 / 218 | onerender (native) | 1609 | 1090 | 305 | 307 | 825 |
|  |  | onerender (JIT process) | 2868 | - | 317 | 347 | - |
|  |  | Chrome | 5635 | 5353 | 2090 | 3052 | - |
|  |  | WeasyPrint | 21661 | - | 898 | 909 | - |
| tailwind | 5 / 5 / 7 | onerender (native) | 147 | 129 | 116 | 137 | 83 |
|  |  | onerender (JIT process) | 726 | - | 121 | 172 | - |
|  |  | Chrome | 309 | 172 | 442 | 1447 | - |
|  |  | WeasyPrint | 677 | - | 54 | 68 | - |

## Verdicts against Chrome

- invoice: WIN
- report: WIN
- table10k: WIN
- tailwind: WIN

Machine, versions and load: see `machine.txt`.
