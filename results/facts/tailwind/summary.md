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
| box.borderBox | 602/737 81.682% | 546/715 76.364% | 1148/1452 79.063% |
| box.exists.element | 737/744 99.059% | 715/722 99.030% | 1452/1466 99.045% |
| box.exists.text | 417/427 97.658% | 401/411 97.567% | 818/838 97.613% |
| form.inputChecked | 4/4 100.000% | 4/4 100.000% | 8/8 100.000% |
| form.inputValue | 14/14 100.000% | 14/14 100.000% | 28/28 100.000% |
| form.optionSelected | 3/3 100.000% | 3/3 100.000% | 6/6 100.000% |
| form.textValue | 1/1 100.000% | 1/1 100.000% | 2/2 100.000% |
| generated.bounds | 18/25 72.000% | 16/25 64.000% | 34/50 68.000% |
| generated.text | 18/25 72.000% | 18/25 72.000% | 36/50 72.000% |
| generated.types | 66/66 100.000% | 66/66 100.000% | 132/132 100.000% |
| paint.order.exact | 521/737 70.692% | 499/715 69.790% | 1020/1452 70.248% |
| paint.order.relative | 728/730 99.726% | 706/708 99.718% | 1434/1438 99.722% |
| rect.client | 700/737 94.980% | 647/715 90.490% | 1347/1452 92.769% |
| rect.client.within1 | 713/737 96.744% | 655/715 91.608% | 1368/1452 94.215% |
| rect.offset | 548/737 74.355% | 498/715 69.650% | 1046/1452 72.039% |
| rect.offset.within1 | 610/737 82.768% | 560/715 78.322% | 1170/1452 80.579% |
| rect.scroll | 693/737 94.030% | 639/715 89.371% | 1332/1452 91.736% |
| rect.scroll.within1 | 707/737 95.929% | 648/715 90.629% | 1355/1452 93.320% |
| stacking.context | 737/737 100.000% | 715/715 100.000% | 1452/1452 100.000% |
| stacking.root | 737/737 100.000% | 715/715 100.000% | 1452/1452 100.000% |
| style.all | 51318/58223 88.140% | 49823/56485 88.206% | 101141/114708 88.173% |
| style.uncomputed | 0/12529 0.000% | 0/12155 0.000% | 0/24684 0.000% |
| text.bounds | 350/417 83.933% | 335/401 83.541% | 685/818 83.741% |
| text.boxes.bounds | 350/417 83.933% | 335/401 83.541% | 685/818 83.741% |
| text.boxes.count | 417/417 100.000% | 401/401 100.000% | 818/818 100.000% |
| text.boxes.ranges | 417/417 100.000% | 401/401 100.000% | 818/818 100.000% |
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
| width | 837/1452 | 57.645% |
| text-align | 1174/1452 | 80.854% |
| background-color | 1195/1452 | 82.300% |
| display | 1201/1452 | 82.713% |
| height | 1322/1452 | 91.047% |
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
| margin-left | 1438/1452 | 99.036% |
| background-image | 1442/1452 | 99.311% |
| line-height | 1446/1452 | 99.587% |
| margin-right | 1448/1452 | 99.725% |
| min-height | 1448/1452 | 99.725% |
| min-width | 1448/1452 | 99.725% |
| overflow-x | 1448/1452 | 99.725% |
| overflow-y | 1448/1452 | 99.725% |
| border-bottom-style | 1450/1452 | 99.862% |
| border-left-style | 1450/1452 | 99.862% |
| border-right-style | 1450/1452 | 99.862% |
| border-top-style | 1450/1452 | 99.862% |
| overflow-wrap | 1450/1452 | 99.862% |
