# Facts comparison: text-block

- reference: Chrome/156.0.8078.47 `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same serialized document
- tolerance for boxes: max(1 px, 0.5% of the reference extent) per axis; rectangles of CSSOM View, paint order, stacking, colors and styles are compared exactly unless the kind says otherwise

| viewport | page captures | refused by the mapping | failed | element and text nodes |
|---|---|---|---|---|
| 1280x800 | 18 | 0 | 0 | 2223 |
| 390x844 | 18 | 0 | 0 | 2223 |

Agreement per fact kind (agree / compared, rate):

| fact kind | 1280x800 | 390x844 | all |
|---|---|---|---|
| bg.color.exact | 481/482 99.793% | 473/475 99.579% | 954/957 99.687% |
| bg.color.within2 | 481/482 99.793% | 473/475 99.579% | 954/957 99.687% |
| bg.presence | 784/784 100.000% | 777/784 99.107% | 1561/1568 99.554% |
| box.borderBox | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| box.exists.element | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| box.exists.text | 668/670 99.701% | 667/670 99.552% | 1335/1340 99.627% |
| generated.bounds | 11/64 17.188% | 11/64 17.188% | 22/128 17.188% |
| generated.text | 0/64 0.000% | 0/64 0.000% | 0/128 0.000% |
| generated.types | 78/81 96.296% | 78/81 96.296% | 156/162 96.296% |
| paint.order.exact | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| paint.order.relative | 766/766 100.000% | 766/766 100.000% | 1532/1532 100.000% |
| rect.client | 782/784 99.745% | 783/784 99.872% | 1565/1568 99.809% |
| rect.client.within1 | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| rect.offset | 772/784 98.469% | 772/784 98.469% | 1544/1568 98.469% |
| rect.offset.within1 | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| rect.scroll | 782/784 99.745% | 782/784 99.745% | 1564/1568 99.745% |
| rect.scroll.within1 | 784/784 100.000% | 783/784 99.872% | 1567/1568 99.936% |
| stacking.context | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| stacking.root | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |
| style.all | 61233/61936 98.865% | 61221/61936 98.846% | 122454/123872 98.855% |
| style.uncomputed | 0/13328 0.000% | 0/13328 0.000% | 0/26656 0.000% |
| text.bounds | 661/668 98.952% | 662/667 99.250% | 1323/1335 99.101% |
| text.boxes.bounds | 656/664 98.795% | 657/663 99.095% | 1313/1327 98.945% |
| text.boxes.count | 664/668 99.401% | 663/667 99.400% | 1327/1335 99.401% |
| text.boxes.ranges | 662/664 99.699% | 660/663 99.548% | 1322/1327 99.623% |
| text.layoutText | 668/668 100.000% | 667/667 100.000% | 1335/1335 100.000% |
| text.opacity | 784/784 100.000% | 784/784 100.000% | 1568/1568 100.000% |

Computed-style agreement by property, all viewports (properties below 99.9%):

| property | agree / compared | rate |
|---|---|---|
| font-family | 890/1568 | 56.760% |
| text-align | 1376/1568 | 87.755% |
| width | 1403/1568 | 89.477% |
| height | 1471/1568 | 93.814% |
| bottom | 1516/1568 | 96.684% |
| overflow-x | 1518/1568 | 96.811% |
| overflow-y | 1518/1568 | 96.811% |
| left | 1530/1568 | 97.577% |
| right | 1532/1568 | 97.704% |
| top | 1536/1568 | 97.959% |
| border-bottom-color | 1562/1568 | 99.617% |
| border-left-color | 1562/1568 | 99.617% |
| border-right-color | 1562/1568 | 99.617% |
| border-top-color | 1562/1568 | 99.617% |
