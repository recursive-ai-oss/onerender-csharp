# Geometry comparison: onehtml-2026-10a / stub-engine

- reference: Chrome/156.0.8078.47; ours: engine; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 600 | 379995 | 0 | 0.000% | 0 | 0 | 0 | 0 | 379995 | 0 | 0 | 0 | 0.000% |
| 390x844 | 600 | 359156 | 0 | 0.000% | 0 | 0 | 0 | 0 | 359156 | 0 | 0 | 0 | 0.000% |
| all | 1200 | 739151 | 0 | 0.000% | 0 | 0 | 0 | 0 | 739151 | 0 | 0 | 0 | 0.000% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 0, size-only 0, position+size 0, text-run 0.

Tags with the most mismatches: div 172967/214010, a 146506/241314, li 101610/188728, span 92916/123518, p 28463/31748, ul 22820/36726, img 17379/20164, svg 17245/23454, use 11167/13196, path 11044/17694
