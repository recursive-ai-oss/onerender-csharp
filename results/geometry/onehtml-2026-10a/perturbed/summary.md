# Geometry comparison: onehtml-2026-10a / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 600 | 380595 | 349409 | 91.806% | 7695 | 3752 | 3711 | 11625 | 3803 | 600 | 0 | 0 | 91.798% |
| 390x844 | 600 | 359756 | 330144 | 91.769% | 7285 | 3533 | 3497 | 11122 | 3575 | 600 | 0 | 0 | 91.765% |
| all | 1200 | 740351 | 679553 | 91.788% | 14980 | 7285 | 7208 | 22747 | 7378 | 1200 | 0 | 0 | 91.786% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 7823, size-only 3935, position+size 3904, text-run 11118.

Tags with the most mismatches: a 13199/241314, div 12380/214010, span 8127/123518, li 7176/188728, p 2353/31748, ul 1614/36726, img 1569/20164, svg 1465/23454, path 1009/17694, use 974/13196
