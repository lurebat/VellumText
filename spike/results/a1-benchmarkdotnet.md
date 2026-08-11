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
