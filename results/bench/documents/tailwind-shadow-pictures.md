# Tailwind page: blurred shadow pictures

The Tailwind marketing page (5 pages) to tagged PDF, before and after computing blurred box shadow pictures directly and sharing equal ones
(`pdf: compute blurred box shadow pictures directly and share equal ones`). One job, onerender pinned to cores 8-15, 3 warm-ups and 2 × 15 measured
iterations (BenchmarkDotNet, one call per iteration), 7 fresh native processes, Chrome unpinned; two rounds, each tree measured once per round.

Host: AMD Ryzen 9 7950X3D, 32 logical cores, governor `powersave`, load average 6.6-7.0 before, during and after (other light work on the host, nothing
heavy). .NET SDK 10.0.402, runtime 10.0.13, Chrome for Testing 156.0.8078.47.

| | warm in-process, median of rounds (ms) | allocated per call | fresh native process: wall (ms) | peak PSS | peak RSS |
|---|---|---|---|---|---|
| before (main at the time) | 126.6 and 136.3 | 87.8 MB | 153.9 and 152.0 | 119 MB | 137 MB |
| after | 97.1 and 93.5 | 35.3 MB | 73.7 and 74.1 | 28 MB | 69 MB |
| Chrome print-to-PDF | 159.9 warm (n=7) | | 295.4 cold (n=5) | 408 MB cold, 494 MB warm | 1.27 GB cold |

The same page at the commit where the document benchmark landed measured 122.2 and 122.7 ms warm, 151.3 and 134.5 ms as a fresh process: the earlier
129 ms and 180 ms readings differ by the host's load, not by a code change. Warm times of the same build vary by ±20 ms from run to run on this host
(per-merge medians 115-176 ms across two rounds), because blurred shadows were rasterized by the general image library on its worker threads.

Output: the other three documents (invoice, 50-page report, 10,000-row table) are byte-identical before and after; the Tailwind PDF has the same page
count and extracted text, 54,535 bytes instead of 73,657 because equal shadows share one image.
