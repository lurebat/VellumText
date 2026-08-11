# A1 - BenchmarkDotNet results (corroborating harness)

Run: `dotnet run -c Release -- bench` (Release, net8.0, 36 benchmarks, 112.7s).

IMPORTANT: the `InvalidateFrom(offset)` arm below is FAST BECAUSE IT IS WRONG.
It returns the pre-edit layout - see a1-diagnostic.md. The ~0.25-0.37x ratio and
~0.5x allocation ratio are the cost of returning a stale answer, not a speedup.
This file is retained precisely because it shows what a naive benchmark concludes.

| Method                                      | Length | EditPercent | Mean      | Error      | StdDev     | Median    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------------------- |------- |------------ |----------:|-----------:|-----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| 'Fresh TextLayout, no cache'                | 100    | 10          | 110.18 us |  54.841 us |  42.816 us |  95.67 us |  1.14 |    0.59 |      - |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 10          |  48.49 us |  38.238 us |  25.292 us |  41.22 us |  0.50 |    0.31 |      - |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 10          | 140.68 us |  51.119 us |  39.910 us | 148.15 us |  1.45 |    0.65 |      - |      - |   9.01 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 10          | 121.22 us |  72.742 us |  56.792 us |  96.28 us |  1.25 |    0.73 |      - |      - |   9.07 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 100    | 50          | 146.51 us |  51.256 us |  40.017 us | 159.76 us |  1.08 |    0.44 |      - |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 50          |  45.00 us |  46.845 us |  30.985 us |  30.21 us |  0.33 |    0.25 |      - |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 50          | 126.29 us |  27.986 us |  21.849 us | 121.20 us |  0.93 |    0.32 |      - |      - |   9.01 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 50          |  97.62 us |  18.027 us |  14.074 us | 104.34 us |  0.72 |    0.24 |      - |      - |   9.06 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 100    | 90          | 136.95 us |  57.182 us |  44.644 us | 150.47 us |  1.17 |    0.69 |      - |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 90          |  42.88 us |  44.216 us |  29.246 us |  32.79 us |  0.37 |    0.32 |      - |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 90          | 110.07 us |  30.049 us |  23.460 us | 119.58 us |  0.94 |    0.50 |      - |      - |   9.01 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 90          | 100.21 us |  25.034 us |  18.101 us | 111.94 us |  0.85 |    0.44 |      - |      - |   9.06 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 10          | 246.86 us |  97.262 us |  75.936 us | 198.83 us |  1.08 |    0.43 |      - |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 10          |  61.07 us |   1.745 us |   1.362 us |  60.90 us |  0.27 |    0.07 |      - |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 10          | 188.02 us |   5.961 us |   3.943 us | 187.89 us |  0.82 |    0.21 |      - |      - |  33.45 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 400    | 10          | 238.73 us |  90.706 us |  70.817 us | 189.78 us |  1.05 |    0.41 |      - |      - |   33.5 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 50          | 270.05 us | 104.660 us |  81.712 us | 278.92 us |  1.09 |    0.46 |      - |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 50          |  62.81 us |   3.106 us |   2.246 us |  62.73 us |  0.25 |    0.08 |      - |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 50          | 265.04 us |  92.018 us |  71.841 us | 301.26 us |  1.07 |    0.43 |      - |      - |  33.44 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 400    | 50          | 269.23 us |  86.888 us |  67.837 us | 291.66 us |  1.09 |    0.42 |      - |      - |   33.5 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 90          | 285.86 us |  98.465 us |  76.875 us | 320.78 us |  1.08 |    0.44 |      - |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 90          |  91.53 us |  31.366 us |  24.488 us | 105.64 us |  0.35 |    0.14 |      - |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 90          | 192.21 us |  25.491 us |  16.860 us | 186.61 us |  0.73 |    0.23 |      - |      - |  33.44 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 400    | 90          | 254.54 us |  97.381 us |  76.029 us | 232.98 us |  0.97 |    0.41 |      - |      - |   33.5 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 10          | 458.04 us | 146.130 us | 114.089 us | 391.52 us |  1.05 |    0.33 |      - |      - |  62.36 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 10          | 162.97 us |  49.923 us |  38.976 us | 186.62 us |  0.37 |    0.12 |      - |      - |  31.93 KB |        0.51 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 10          | 477.06 us | 179.614 us | 140.231 us | 392.86 us |  1.09 |    0.39 |      - |      - |  62.64 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 10          | 434.75 us | 154.194 us | 120.385 us | 360.94 us |  1.00 |    0.34 |      - |      - |   62.7 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 50          | 365.40 us |   6.800 us |   4.917 us | 366.54 us |  1.00 |    0.02 |      - |      - |  63.54 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 50          | 111.71 us |   7.078 us |   4.682 us | 109.31 us |  0.31 |    0.01 |      - |      - |  31.93 KB |        0.50 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 50          | 504.99 us | 189.239 us | 147.745 us | 406.91 us |  1.38 |    0.39 | 3.9063 | 3.9063 |  63.83 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 50          | 470.30 us | 147.198 us | 114.922 us | 416.83 us |  1.29 |    0.30 | 3.9063 | 3.9063 |  63.89 KB |        1.01 |
|                                             |        |             |           |            |            |           |       |         |        |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 90          | 393.38 us |  89.257 us |  59.038 us | 368.30 us |  1.02 |    0.19 |      - |      - |  63.54 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 90          | 112.84 us |   1.217 us |   0.805 us | 112.91 us |  0.29 |    0.03 |      - |      - |  31.93 KB |        0.50 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 90          | 526.05 us | 162.502 us | 126.871 us | 545.62 us |  1.36 |    0.35 | 3.9063 | 3.9063 |  63.83 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 90          | 374.90 us |   8.638 us |   5.141 us | 373.78 us |  0.97 |    0.11 | 3.9063 | 3.9063 |  63.89 KB |        1.01 |

---

## Re-run on net10.0

Same binary retargeted to `net10.0`. Timings on this VM are noisier than the net8.0 run above,
so individual ratios there should not be over-read. The **allocation ratio is the stable signal**
(memory is far less sensitive to machine load than time), and it reproduces exactly:
`InvalidateFrom(offset)` allocates 0.39x at 100 chars and ~0.49-0.51x at 400/800 - identical to
net8.0, and consistent with it skipping the shaping work entirely rather than doing it faster.

| Method                                      | Length | EditPercent | Mean     | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------------------- |------- |------------ |---------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| 'Fresh TextLayout, no cache'                | 100    | 10          | 147.3 us |  86.58 us |  67.60 us | 126.23 us |  1.23 |    0.80 |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 10          | 137.7 us |  18.02 us |  14.07 us | 142.89 us |  1.15 |    0.52 |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 10          | 108.0 us |  69.25 us |  45.80 us |  92.64 us |  0.90 |    0.56 |      - |   9.01 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 10          | 147.8 us |  77.10 us |  60.19 us | 143.49 us |  1.23 |    0.75 |      - |   9.06 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 100    | 50          | 153.1 us | 135.91 us | 106.11 us |  97.93 us |  1.42 |    1.24 |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 50          | 129.2 us |  15.31 us |  10.12 us | 132.48 us |  1.20 |    0.58 |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 50          | 127.2 us | 107.76 us |  77.92 us |  97.62 us |  1.18 |    0.95 |      - |      9 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 50          | 114.5 us |  67.62 us |  44.73 us |  97.74 us |  1.06 |    0.67 |      - |   9.06 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 100    | 90          | 181.0 us | 157.50 us | 122.96 us | 115.41 us |  1.47 |    1.33 |      - |   8.94 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 100    | 90          | 115.0 us |  30.65 us |  23.93 us | 114.19 us |  0.94 |    0.54 |      - |    3.5 KB |        0.39 |
| 'Persistent cache + Invalidate() (all)'     | 100    | 90          | 107.6 us |  80.86 us |  53.48 us |  89.40 us |  0.88 |    0.66 |      - |   9.01 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 100    | 90          | 105.1 us |  68.75 us |  45.48 us |  89.36 us |  0.86 |    0.60 |      - |   9.06 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 10          | 174.6 us |  32.99 us |  25.75 us | 167.72 us |  1.02 |    0.20 |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 10          | 106.1 us |  39.89 us |  31.15 us | 125.14 us |  0.62 |    0.19 |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 10          | 171.1 us |  29.37 us |  22.93 us | 170.39 us |  1.00 |    0.18 |      - |  33.44 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 400    | 10          | 171.5 us |  30.80 us |  24.05 us | 168.89 us |  1.00 |    0.19 |      - |  33.49 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 50          | 168.2 us |  24.31 us |  18.98 us | 166.91 us |  1.01 |    0.16 |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 50          | 112.5 us |  47.31 us |  36.94 us | 130.95 us |  0.68 |    0.23 |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 50          | 170.7 us |  31.41 us |  24.52 us | 168.47 us |  1.03 |    0.18 |      - |  33.44 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 400    | 50          | 173.9 us |  33.55 us |  26.19 us | 165.14 us |  1.05 |    0.19 |      - |   33.5 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 400    | 90          | 173.5 us |  38.62 us |  30.15 us | 157.55 us |  1.03 |    0.23 |      - |  33.28 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 400    | 90          | 104.8 us |  38.76 us |  30.26 us | 122.79 us |  0.62 |    0.20 |      - |  16.34 KB |        0.49 |
| 'Persistent cache + Invalidate() (all)'     | 400    | 90          | 171.5 us |  24.60 us |  19.20 us | 171.20 us |  1.01 |    0.19 |      - |  33.44 KB |        1.01 |
| 'Fresh cache each edit (control)'           | 400    | 90          | 171.1 us |  23.23 us |  18.14 us | 165.10 us |  1.01 |    0.19 |      - |   33.5 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 10          | 292.4 us |   2.78 us |   2.17 us | 292.40 us |  1.00 |    0.01 |      - |  62.36 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 10          | 103.1 us |  17.33 us |  13.53 us | 112.44 us |  0.35 |    0.04 |      - |  31.93 KB |        0.51 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 10          | 295.4 us |   4.48 us |   3.24 us | 294.71 us |  1.01 |    0.01 |      - |  62.65 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 10          | 311.0 us |   6.42 us |   4.64 us | 308.96 us |  1.06 |    0.02 |      - |   62.7 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 50          | 316.3 us |   3.73 us |   2.69 us | 316.16 us |  1.00 |    0.01 |      - |  63.54 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 50          | 102.7 us |  12.80 us |  10.00 us | 108.86 us |  0.32 |    0.03 |      - |  31.93 KB |        0.50 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 50          | 307.8 us |   4.63 us |   3.62 us | 306.05 us |  0.97 |    0.01 | 3.9063 |  63.83 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 50          | 310.3 us |   2.48 us |   1.79 us | 310.17 us |  0.98 |    0.01 | 3.9063 |  63.89 KB |        1.01 |
|                                             |        |             |          |           |           |           |       |         |        |           |             |
| 'Fresh TextLayout, no cache'                | 800    | 90          | 299.8 us |   3.26 us |   2.36 us | 299.39 us |  1.00 |    0.01 |      - |  63.54 KB |        1.00 |
| 'Persistent cache + InvalidateFrom(offset)' | 800    | 90          | 104.8 us |  19.69 us |  15.37 us | 113.08 us |  0.35 |    0.05 |      - |  31.93 KB |        0.50 |
| 'Persistent cache + Invalidate() (all)'     | 800    | 90          | 310.6 us |   2.02 us |   1.46 us | 310.06 us |  1.04 |    0.01 | 3.9063 |  63.83 KB |        1.00 |
| 'Fresh cache each edit (control)'           | 800    | 90          | 310.0 us |   7.05 us |   5.51 us | 308.70 us |  1.03 |    0.02 | 3.9063 |  63.89 KB |        1.01 |
