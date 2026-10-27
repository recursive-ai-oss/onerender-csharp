# Interactive gate summary (gate)

## Bar: each change at most 1 ms and 0 B allocated

| Benchmark | Mean (us) | Allocated (B) | Verdict |
|---|---:|---:|---|
| IncrementalBenchmarks.IdleFrame | 0.001 | 0 | PASS |
| IncrementalBenchmarks.HoverRestyle | 3.146 | 0 | PASS |
| IncrementalBenchmarks.TextChange | 46.123 | 0 | PASS |
| IncrementalBenchmarks.SubtreeRelayout | 405.141 | 0 | PASS |
| MotionBenchmarks.CompositorTick100 | 29.655 | 0 | PASS |
| MotionBenchmarks.MixedTick100Compositor4Layout | 311.013 | 0 | PASS |
| MotionBenchmarks.IdleAdvance | 0.008 | 0 | PASS |
| MotionBenchmarks.WidthTransitionTick | 64.593 | 0 | PASS |
| StructuralBenchmarks.InsertSubtree | 550.700 | 0 | PASS |
| StructuralBenchmarks.RemoveSubtree | 400.700 | 0 | PASS |
| StructuralBenchmarks.MoveSubtree | 670.800 | 0 | PASS |
| StructuralBenchmarks.ChurnFrame | 713.600 | 0 | PASS |

## OneGame.UI and the engine on the same changes

| Change | OneGame.UI mean (us) | OneGame.UI B | engine mean (us) | engine B | engine no slower |
|---|---:|---:|---:|---:|---|
| IdleFrame | 2.136 | 0 | 0.001 | 0 | yes |
| HoverRestyle | 65.683 | 4424 | 3.780 | 0 | yes |
| TextChange | 64.016 | 4264 | 44.589 | 0 | yes |
| SubtreeRelayout | 86.003 | 4304 | 383.678 | 0 | NO |

## Like for like: OneGame.UI's own flex-only page through the engine's builder front end

Scene check (both trees built, compared node for node, border boxes within 0.5 px):

    initial: 763 elements (262 labels, 1025 styled nodes) on both sides, worst border-box difference 0.096 px
    after text change and resize: worst border-box difference 0.096 px

| Change | OneGame.UI mean (us) | OneGame.UI B | engine mean (us) | engine B | engine / OneGame.UI |
|---|---:|---:|---:|---:|---:|
| IdleFrame | 2.136 | 0 | 0.001 | 0 | 0.00x |
| HoverRestyle | 65.683 | 4424 | 3.874 | 0 | 0.06x |
| HoverWithHitTest (vs OneGame.UI hover) | 65.683 | 4424 | 12.590 | 0 | 0.19x |
| TextChange | 64.016 | 4264 | 46.059 | 0 | 0.72x |
| SubtreeRelayout | 86.003 | 4304 | 341.692 | 0 | 3.97x |

## An animated layout property: a CSS transition on one row's width, ticked every frame

| Benchmark | us per frame | B per frame | Within 1 ms and 0 B |
|---|---:|---:|---|
| WidthTransitionTick | 64.593 | 0 | yes |

Interactive bar: PASS
Engine no slower than OneGame.UI on every change: NO
