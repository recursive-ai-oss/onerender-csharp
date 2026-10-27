# Interactive gate summary (gate)

## Bar: each change at most 1 ms and 0 B allocated

| Benchmark | Mean (us) | Allocated (B) | Verdict |
|---|---:|---:|---|
| IncrementalBenchmarks.IdleFrame | 0.001 | 0 | PASS |
| IncrementalBenchmarks.HoverRestyle | 3.793 | 0 | PASS |
| IncrementalBenchmarks.TextChange | 42.782 | 0 | PASS |
| IncrementalBenchmarks.SubtreeRelayout | 352.027 | 0 | PASS |
| MotionBenchmarks.CompositorTick100 | 29.031 | 0 | PASS |
| MotionBenchmarks.MixedTick100Compositor4Layout | 243.006 | 0 | PASS |
| MotionBenchmarks.IdleAdvance | 0.007 | 0 | PASS |

## OneGame.UI and the engine on the same changes

| Change | OneGame.UI mean (us) | OneGame.UI B | engine mean (us) | engine B | engine no slower |
|---|---:|---:|---:|---:|---|
| IdleFrame | 1.900 | 0 | 0.001 | 0 | yes |
| HoverRestyle | 61.812 | 4424 | 3.401 | 0 | yes |
| TextChange | 57.442 | 4264 | 38.973 | 0 | yes |
| SubtreeRelayout | 79.286 | 4304 | 334.315 | 0 | NO |

## Like for like: OneGame.UI's own flex-only page through the engine's builder front end

Scene check (both trees built, compared node for node, border boxes within 0.5 px):

    initial: 763 elements (262 labels, 1025 styled nodes) on both sides, worst border-box difference 0.096 px
    after text change and resize: worst border-box difference 0.096 px

| Change | OneGame.UI mean (us) | OneGame.UI B | engine mean (us) | engine B | engine / OneGame.UI |
|---|---:|---:|---:|---:|---:|
| IdleFrame | 1.900 | 0 | 0.001 | 0 | 0.00x |
| HoverRestyle | 61.812 | 4424 | 3.562 | 0 | 0.06x |
| HoverWithHitTest (vs OneGame.UI hover) | 61.812 | 4424 | 10.662 | 0 | 0.17x |
| TextChange | 57.442 | 4264 | 42.929 | 0 | 0.75x |
| SubtreeRelayout | 79.286 | 4304 | 307.789 | 0 | 3.88x |

Interactive bar: PASS
Engine no slower than OneGame.UI on every change: NO
