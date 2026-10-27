# Facts comparison: tailwind

- reference: Chrome/156.0.8078.47 `DOMSnapshot.captureSnapshot` facts; ours: the engine's facts for the same serialized document
- tolerance for boxes: max(1 px, 0.5% of the reference extent) per axis; rectangles of CSSOM View, paint order, stacking, colors and styles are compared exactly unless the kind says otherwise

| viewport | page captures | refused by the mapping | failed | element and text nodes |
|---|---|---|---|---|
| 1280x800 | 7 | 0 | 0 | 1459 |
| 390x844 | 7 | 0 | 0 | 1459 |

Agreement per fact kind (agree / compared, rate):

| fact kind | 1280x800 | 390x844 | all |
|---|---|---|---|
| bg.color.exact | 159/389 40.874% | 143/367 38.965% | 302/756 39.947% |
| bg.color.within2 | 160/389 41.131% | 144/367 39.237% | 304/756 40.212% |
| bg.presence | 737/737 100.000% | 708/715 99.021% | 1445/1452 99.518% |
| box.borderBox | 206/737 27.951% | 36/715 5.035% | 242/1452 16.667% |
| box.exists.element | 737/744 99.059% | 715/722 99.030% | 1452/1466 99.045% |
| box.exists.text | 417/427 97.658% | 401/411 97.567% | 818/838 97.613% |
| form.inputChecked | 4/4 100.000% | 4/4 100.000% | 8/8 100.000% |
| form.inputValue | 14/14 100.000% | 14/14 100.000% | 28/28 100.000% |
| form.optionSelected | 3/3 100.000% | 3/3 100.000% | 6/6 100.000% |
| form.textValue | 1/1 100.000% | 1/1 100.000% | 2/2 100.000% |
| generated.bounds | 10/25 40.000% | 1/25 4.000% | 11/50 22.000% |
| generated.text | 18/25 72.000% | 18/25 72.000% | 36/50 72.000% |
| generated.types | 66/66 100.000% | 66/66 100.000% | 132/132 100.000% |
| paint.order.exact | 521/737 70.692% | 499/715 69.790% | 1020/1452 70.248% |
| paint.order.relative | 728/730 99.726% | 706/708 99.718% | 1434/1438 99.722% |
| rect.client | 396/737 53.731% | 311/715 43.497% | 707/1452 48.691% |
| rect.client.within1 | 397/737 53.867% | 312/715 43.636% | 709/1452 48.829% |
| rect.offset | 221/737 29.986% | 94/715 13.147% | 315/1452 21.694% |
| rect.offset.within1 | 228/737 30.936% | 101/715 14.126% | 329/1452 22.658% |
| rect.scroll | 388/737 52.646% | 301/715 42.098% | 689/1452 47.452% |
| rect.scroll.within1 | 389/737 52.782% | 302/715 42.238% | 691/1452 47.590% |
| stacking.context | 737/737 100.000% | 715/715 100.000% | 1452/1452 100.000% |
| stacking.root | 737/737 100.000% | 715/715 100.000% | 1452/1452 100.000% |
| style.all | 50644/58223 86.983% | 49053/56485 86.843% | 99697/114708 86.914% |
| style.uncomputed | 0/12529 0.000% | 0/12155 0.000% | 0/24684 0.000% |
| text.bounds | 98/417 23.501% | 30/401 7.481% | 128/818 15.648% |
| text.boxes.bounds | 98/416 23.558% | 30/392 7.653% | 128/808 15.842% |
| text.boxes.count | 416/417 99.760% | 392/401 97.756% | 808/818 98.778% |
| text.boxes.ranges | 406/416 97.596% | 378/392 96.429% | 784/808 97.030% |
| text.layoutText | 417/417 100.000% | 401/401 100.000% | 818/818 100.000% |
| text.opacity | 737/737 100.000% | 715/715 100.000% | 1452/1452 100.000% |

Computed-style agreement by property, all viewports (properties below 99.9%):

| property | agree / compared | rate |
|---|---|---|
| font-family | 0/1452 | 0.000% |
| color | 45/1452 | 3.099% |
| text-decoration-color | 45/1452 | 3.099% |
| outline-color | 55/1452 | 3.788% |
| border-bottom-color | 59/1452 | 4.063% |
| border-left-color | 59/1452 | 4.063% |
| border-right-color | 59/1452 | 4.063% |
| border-top-color | 59/1452 | 4.063% |
| width | 568/1452 | 39.118% |
| height | 1037/1452 | 71.419% |
| text-align | 1174/1452 | 80.854% |
| background-color | 1195/1452 | 82.300% |
| display | 1201/1452 | 82.713% |
| padding-left | 1215/1452 | 83.678% |
| padding-right | 1215/1452 | 83.678% |
| padding-bottom | 1262/1452 | 86.915% |
| padding-top | 1262/1452 | 86.915% |
| bottom | 1352/1452 | 93.113% |
| right | 1352/1452 | 93.113% |
| top | 1352/1452 | 93.113% |
| left | 1356/1452 | 93.388% |
| background-size | 1384/1452 | 95.317% |
| box-shadow | 1390/1452 | 95.730% |
| background-attachment | 1392/1452 | 95.868% |
| background-clip | 1392/1452 | 95.868% |
| background-repeat | 1392/1452 | 95.868% |
| text-shadow | 1418/1452 | 97.658% |
| margin-left | 1436/1452 | 98.898% |
| margin-top | 1441/1452 | 99.242% |
| background-image | 1442/1452 | 99.311% |
| margin-bottom | 1442/1452 | 99.311% |
| border-right-style | 1444/1452 | 99.449% |
| border-right-width | 1446/1452 | 99.587% |
| line-height | 1446/1452 | 99.587% |
| margin-right | 1446/1452 | 99.587% |
| min-height | 1448/1452 | 99.725% |
| min-width | 1448/1452 | 99.725% |
| overflow-x | 1448/1452 | 99.725% |
| overflow-y | 1448/1452 | 99.725% |
| border-bottom-style | 1450/1452 | 99.862% |
| border-left-style | 1450/1452 | 99.862% |
| border-top-style | 1450/1452 | 99.862% |
| overflow-wrap | 1450/1452 | 99.862% |
