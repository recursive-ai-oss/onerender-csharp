# Facts comparison: templates

- reference: Chrome/156.0.8078.47 `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same serialized document
- tolerance for boxes: max(1 px, 0.5% of the reference extent) per axis; rectangles of CSSOM View, paint order, stacking, colors and styles are compared exactly unless the kind says otherwise

| viewport | page captures | refused by the mapping | failed | element and text nodes |
|---|---|---|---|---|
| 1280x800 | 6 | 0 | 0 | 6494 |
| 390x844 | 6 | 0 | 0 | 6494 |

Agreement per fact kind (agree / compared, rate):

| fact kind | 1280x800 | 390x844 | all |
|---|---|---|---|
| bg.color.exact | 2054/2054 100.000% | 1869/1870 99.947% | 3923/3924 99.975% |
| bg.color.within2 | 2054/2054 100.000% | 1869/1870 99.947% | 3923/3924 99.975% |
| bg.presence | 3052/3052 100.000% | 2868/3052 93.971% | 5920/6104 96.986% |
| box.borderBox | 3051/3052 99.967% | 3051/3052 99.967% | 6102/6104 99.967% |
| box.exists.element | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| box.exists.text | 2651/2653 99.925% | 2651/2653 99.925% | 5302/5306 99.925% |
| generated.bounds | 44/146 30.137% | 44/146 30.137% | 88/292 30.137% |
| generated.text | 0/146 0.000% | 0/146 0.000% | 0/292 0.000% |
| generated.types | 146/146 100.000% | 146/146 100.000% | 292/292 100.000% |
| paint.order.exact | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| paint.order.relative | 3046/3046 100.000% | 3046/3046 100.000% | 6092/6092 100.000% |
| rect.client | 3037/3052 99.509% | 3013/3052 98.722% | 6050/6104 99.115% |
| rect.client.within1 | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| rect.offset | 2384/3052 78.113% | 2354/3052 77.130% | 4738/6104 77.621% |
| rect.offset.within1 | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| rect.scroll | 3037/3052 99.509% | 3013/3052 98.722% | 6050/6104 99.115% |
| rect.scroll.within1 | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| stacking.context | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| stacking.root | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |
| style.all | 236168/241108 97.951% | 236015/241108 97.888% | 472183/482216 97.919% |
| style.uncomputed | 0/51884 0.000% | 0/51884 0.000% | 0/103768 0.000% |
| text.bounds | 2649/2651 99.925% | 2649/2651 99.925% | 5298/5302 99.925% |
| text.boxes.bounds | 2649/2651 99.925% | 2649/2651 99.925% | 5298/5302 99.925% |
| text.boxes.count | 2651/2651 100.000% | 2651/2651 100.000% | 5302/5302 100.000% |
| text.boxes.ranges | 2651/2651 100.000% | 2651/2651 100.000% | 5302/5302 100.000% |
| text.layoutText | 2651/2651 100.000% | 2651/2651 100.000% | 5302/5302 100.000% |
| text.opacity | 3052/3052 100.000% | 3052/3052 100.000% | 6104/6104 100.000% |

Computed-style agreement by property, all viewports (properties below 99.9%):

| property | agree / compared | rate |
|---|---|---|
| font-family | 684/6104 | 11.206% |
| width | 2280/6104 | 37.353% |
| text-align | 5620/6104 | 92.071% |
| height | 5907/6104 | 96.773% |
| display | 6008/6104 | 98.427% |
