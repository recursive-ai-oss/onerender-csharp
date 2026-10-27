# Geometry comparison: text-block / perturbed

- reference: Chrome/156.0.8078.47; ours: capture; tolerance: max(1 px, 0.5% of the reference extent)
- font set: 8768bf2774f6c8e8 (20 files)
- gate: 99.000% of elements within tolerance per viewport; met: no

| viewport | pages | elements | within tolerance | rate | position-only | size-only | position+size | text-run | missing-ours | missing-chrome | tag conflicts | pages at gate | median page |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1280x800 | 18 | 802 | 724 | 90.274% | 18 | 1 | 3 | 33 | 5 | 18 | 0 | 0 | 89.063% |
| 390x844 | 18 | 802 | 724 | 90.274% | 18 | 1 | 3 | 33 | 5 | 18 | 0 | 0 | 89.063% |
| all | 36 | 1604 | 1448 | 90.274% | 36 | 2 | 6 | 66 | 10 | 36 | 0 | 0 | 89.063% |

Root causes (mismatches with no mismatching ancestor), all viewports: position-only 28, size-only 0, position+size 6, text-run 42.

Tags with the most mismatches: p 26/298, div 16/218, li 12/162, b 8/34, span 8/132, img 6/50, a 4/70, br 4/16, code 4/42, dd 4/24
