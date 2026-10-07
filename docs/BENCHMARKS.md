# Benchmark results

- CPU: Intel(R) Core(TM) i7-6700K CPU @ 4.00GHz (8 logical cores)
- OS: Microsoft Windows 10.0.19045; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, gcc 15.2, msvc 19.42.34444
- Libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10.0, msgpack-cxx 9.0.0
- Recorded 2026-10-07 01:51:39Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every reader computes a checksum over every field (presence included) and must match the value computed from the C# objects before anything is timed. The C++ readers' small helper functions are force-inlined with MSVC, for all three libraries: MSVC stopped inlining them into one large function when it read Tessera (`benchmarks/native/readers.hpp` explains).

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 9 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack |
|---|---:|---:|
| Size | **1.27** (1.00–3.37) | 0.90 (0.59–2.01) |
| .NET write | **6.29** (1.25–31.21) | **2.37** (1.13–5.85) |
| C++ verify (clang 19.1) | **3.02** (1.69–9.20) | **18.53** (6.77–1556.07) |
| C++ full traversal (clang 19.1) | **1.04** (1.00–1.13) |  |
| C++ verify + traversal (clang 19.1) | **1.68** (1.05–2.53) | **5.57** (3.88–14.75) |
| C++ random access (clang 19.1) | **1.25** (1.04–1.85) |  |
| C++ verify (gcc 15.2) | **4.13** (2.48–13.25) | **28.05** (7.68–3365.84) |
| C++ full traversal (gcc 15.2) | **1.03** (0.99–1.18) |  |
| C++ verify + traversal (gcc 15.2) | **1.93** (1.03–2.73) | **6.87** (3.90–28.56) |
| C++ random access (gcc 15.2) | **1.27** (1.04–2.11) |  |
| C++ verify (msvc 19.42.34444) | **2.72** (1.69–12.40) | **23.72** (9.02–1703.97) |
| C++ full traversal (msvc 19.42.34444) | **1.10** (1.00–2.04) |  |
| C++ verify + traversal (msvc 19.42.34444) | **1.61** (1.02–3.32) | **6.70** (4.84–17.89) |
| C++ random access (msvc 19.42.34444) | **1.28** (1.13–1.64) |  |

MessagePack has no separate verify step: its "verify" is a full parse into a tree, which its traversal and random access then use (those two are left out of the MessagePack column because they exclude the parse).

Where Tessera is slower in these results (by more than 5% for the C++ reads), and why:

- **Size** (MessagePack smaller): monsters 1.19×, series 1.11×, dense-unique 1.69×, sparse-unique 1.26×, dense-shared 1.70×, lookup 1.39×. MessagePack stores small integers in one or two bytes; Tessera keeps values fixed-width so they can be read in place.

## Size (bytes, smaller is better)

| Workload | Tessera | Tessera (full sharing) | Tessera (no schema) | FlatBuffers | FlatBuffers (shared strings) | MessagePack |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **75,800** | 74,232 | 74,384 | 97,332 | 83,420 | 83,330 |
| monsters | 155,096 | 147,896 | 154,680 | 183,424 | 158,752 | **130,187** |
| records | **70,928** | 70,912 | 70,336 | 239,336 | 215,400 | 142,887 |
| series | 581,288 | 581,288 | 581,112 | 582,776 | 582,776 | **523,153** |
| dense-unique | 69,528 | 69,528 | 69,272 | 73,396 | 73,396 | **41,029** |
| sparse-unique | 33,480 | 33,480 | 33,224 | 34,724 | 34,724 | **26,634** |
| dense-shared | 52,984 | 5,352 | 52,728 | 69,700 | 57,604 | **31,121** |
| lookup | 240,416 | 240,368 | 240,056 | 280,064 | 280,064 | **173,020** |
| canada | **895,344** | **895,344** | 894,936 | 898,792 | 898,792 | 1,057,063 |

gzip -9 of the same buffers (for transfer/storage comparisons):

| Workload | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| prefab | 20,730 | 22,422 | 15,575 |
| monsters | 98,996 | 106,353 | 94,599 |
| records | 59,667 | 77,163 | 63,848 |
| series | 240,987 | 287,823 | 193,227 |
| dense-unique | 20,126 | 25,449 | 20,862 |
| sparse-unique | 15,890 | 18,337 | 13,298 |
| dense-shared | 7,462 | 6,245 | 628 |
| lookup | 112,921 | 146,563 | 93,180 |
| canada | 471,467 | 473,596 | 470,468 |

## .NET write (µs per buffer, lower is better)

Object graph to `byte[]` with a reused writer/builder (each library's normal path). Tessera's default shares equal strings. Every case starts on a collected heap, and the large-object threshold is raised to 4 MB, so result arrays die young like any other object (at the default 85 KB, the full collections that free them took time that varied erratically with the result's exact size). The table after this one leaves the final copy out.

| Workload | Tessera | Tessera (no sharing) | Tessera (full sharing) | FlatBuffers | MessagePack |
|---|---:|---:|---:|---:|---:|
| prefab | 77.7 | **67.5** | 132 | 319 | 182 |
| monsters | 172 | **152** | 333 | 804 | 298 |
| records | **269** | 269 | 363 | 8,213 | 911 |
| series | 314 | **308** | 910 | 1,679 | 601 |
| dense-unique | 48.6 | **35.3** | 108 | 199 | 86.4 |
| sparse-unique | 22.3 | **19.1** | 55.1 | 131 | 74.8 |
| dense-shared | 37.5 | **34.9** | 103 | 198 | 85.1 |
| lookup | 208 | **200** | 353 | 6,494 | 235 |
| canada | 108 | **106** | 377 | 135 | 632 |

Allocated bytes per write without the final copy (reused writer, builder or buffer):

| Workload | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| prefab | 75.4 µs, 0 B | 311 µs, 0 B | 171 µs, 0 B |
| monsters | 160 µs, 0 B | 787 µs, 0 B | 286 µs, 0 B |
| records | 265 µs, 0 B | 8,181 µs, 0 B | 897 µs, 0 B |
| series | 268 µs, 0 B | 1,622 µs, 0 B | 551 µs, 0 B |
| dense-unique | 45.2 µs, 0 B | 194 µs, 0 B | 82.0 µs, 0 B |
| sparse-unique | 20.4 µs, 0 B | 128 µs, 0 B | 71.3 µs, 0 B |
| dense-shared | 33.6 µs, 0 B | 191 µs, 0 B | 80.3 µs, 0 B |
| lookup | 187 µs, 0 B | 6,457 µs, 4,791,936 B | 217 µs, 0 B |
| canada | 53.5 µs, 0 B | 68.2 µs, 120 B | 528 µs, 0 B |

## C++ read — clang 19.1 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **16.5** | 32.8 | 204 | 1.99× faster |
| monsters | **40.2** | 68.1 | 336 | 1.69× faster |
| records | **24.5** | 225 | 593 | 9.20× faster |
| series | **69.4** | 209 | 760 | 3.02× faster |
| dense-unique | **11.7** | 24.7 | 87.8 | 2.12× faster |
| sparse-unique | **6.33** | 17.8 | 65.0 | 2.81× faster |
| dense-shared | **11.7** | 22.4 | 79.0 | 1.92× faster |
| lookup | **20.5** | 84.1 | 236 | 4.10× faster |
| canada | **0.94** | 4.54 | 1,468 | 4.81× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **18.8** | 19.3 | 21.3 | 1.02× faster |
| monsters | **37.2** | 42.0 | 38.1 | 1.13× faster |
| records | **112** | 127 | 113 | 1.13× faster |
| series | **95.8** | 102 | 97.2 | 1.06× faster |
| dense-unique | **11.8** | **11.8** | **11.8** | 1.00× faster |
| sparse-unique | **8.44** | 8.48 | 8.57 | 1.00× faster |
| dense-shared | **11.8** | **11.8** | **11.8** | 1.00× faster |
| lookup | **28.7** | 30.2 | 28.9 | 1.05× faster |
| canada | **107** | **107** | 109 | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **35.8** | 54.9 | 227 | 1.53× faster |
| monsters | **79.3** | 119 | 381 | 1.50× faster |
| records | **143** | 362 | 716 | 2.53× faster |
| series | **165** | 313 | 863 | 1.89× faster |
| dense-unique | **23.4** | 36.5 | 99.6 | 1.56× faster |
| sparse-unique | **14.8** | 26.3 | 73.5 | 1.78× faster |
| dense-shared | **23.4** | 34.2 | 91.0 | 1.46× faster |
| lookup | **49.4** | 114 | 265 | 2.31× faster |
| canada | **108** | 113 | 1,594 | 1.05× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **4.67** | 4.89 | 5.04 | 1.05× faster |
| monsters | **4.89** | 5.08 | 6.35 | 1.04× faster |
| records | **2.99** | 3.58 | 5.88 | 1.20× faster |
| series | 3.32 | 3.91 | **3.18** | 1.18× faster |
| dense-unique | 3.56 | 4.14 | **3.12** | 1.16× faster |
| sparse-unique | **2.37** | 4.40 | 2.70 | 1.85× faster |
| dense-shared | 3.32 | 4.22 | **3.20** | 1.27× faster |
| lookup | 1.94 | 2.33 | **1.62** | 1.21× faster |
| canada | **1.65** | 2.45 | 1.86 | 1.48× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0063** | 0.0075 | 205 | 1.19× faster |
| monsters | **0.0064** | 0.0077 | 336 | 1.19× faster |
| records | **0.0039** | 0.0048 | 593 | 1.22× faster |
| series | **0.0038** | 0.0055 | 760 | 1.46× faster |
| dense-unique | **0.0051** | 0.0060 | 88.0 | 1.17× faster |
| sparse-unique | **0.0051** | 0.0060 | 64.9 | 1.17× faster |
| dense-shared | **0.0047** | 0.0060 | 79.2 | 1.28× faster |
| lookup | **0.0041** | 0.0046 | 236 | 1.11× faster |
| canada | **0.0067** | 0.0080 | 1,459 | 1.19× faster |

## C++ read — gcc 15.2 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **9.69** | 44.2 | 153 | 4.56× faster |
| monsters | **34.3** | 84.9 | 263 | 2.48× faster |
| records | **19.3** | 256 | 1,310 | 13.25× faster |
| series | **43.3** | 181 | 1,954 | 4.18× faster |
| dense-unique | **7.86** | 27.6 | 75.4 | 3.51× faster |
| sparse-unique | **4.60** | 18.3 | 51.5 | 3.97× faster |
| dense-shared | **7.77** | 27.5 | 65.1 | 3.54× faster |
| lookup | **21.1** | 84.2 | 201 | 3.99× faster |
| canada | **0.89** | 2.55 | 2,998 | 2.87× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.7** | 19.8 | 19.9 | 1.00× faster |
| monsters | **39.3** | 42.0 | 39.9 | 1.07× faster |
| records | **113** | 133 | 120 | 1.18× faster |
| series | **95.0** | 96.9 | 106 | 1.02× faster |
| dense-unique | 12.2 | 12.2 | **12.1** | 1.00× faster |
| sparse-unique | 8.80 | **8.73** | 8.82 | 1.01× slower |
| dense-shared | **12.2** | **12.2** | **12.2** | 1.00× faster |
| lookup | **29.5** | 30.5 | 30.0 | 1.03× faster |
| canada | **111** | **111** | 125 | 1.00× slower |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **30.2** | 65.8 | 172 | 2.18× faster |
| monsters | **78.1** | 132 | 312 | 1.69× faster |
| records | **140** | 382 | 1,454 | 2.73× faster |
| series | **139** | 273 | 2,075 | 1.97× faster |
| dense-unique | **20.3** | 40.5 | 87.3 | 1.99× faster |
| sparse-unique | **13.4** | 28.3 | 60.1 | 2.11× faster |
| dense-shared | **19.9** | 40.4 | 77.4 | 2.03× faster |
| lookup | **51.6** | 113 | 230 | 2.19× faster |
| canada | **111** | 114 | 3,181 | 1.03× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **4.06** | 4.42 | 4.29 | 1.09× faster |
| monsters | **3.93** | 4.98 | 5.21 | 1.27× faster |
| records | **3.66** | 4.33 | 5.29 | 1.18× faster |
| series | 3.15 | 3.55 | **2.30** | 1.13× faster |
| dense-unique | 3.83 | 4.00 | **3.31** | 1.04× faster |
| sparse-unique | **2.10** | 2.63 | 2.78 | 1.25× faster |
| dense-shared | 3.58 | 4.11 | **3.57** | 1.15× faster |
| lookup | 1.97 | 2.84 | **1.95** | 1.44× faster |
| canada | 3.82 | 8.04 | **2.32** | 2.11× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0051** | 0.0062 | 153 | 1.22× faster |
| monsters | **0.0054** | 0.0071 | 262 | 1.30× faster |
| records | **0.0041** | 0.0054 | 1,307 | 1.32× faster |
| series | **0.0043** | 0.0048 | 1,942 | 1.11× faster |
| dense-unique | **0.0051** | 0.0076 | 76.0 | 1.49× faster |
| sparse-unique | **0.0051** | 0.0070 | 51.3 | 1.36× faster |
| dense-shared | **0.0047** | 0.0069 | 64.6 | 1.46× faster |
| lookup | **0.0037** | 0.0050 | 199 | 1.36× faster |
| canada | **0.0072** | 0.0107 | 2,981 | 1.48× faster |

## C++ read — msvc 19.42.34444 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **15.6** | 38.7 | 246 | 2.47× faster |
| monsters | **36.7** | 68.4 | 414 | 1.87× faster |
| records | **21.1** | 261 | 683 | 12.40× faster |
| series | **69.1** | 206 | 996 | 2.98× faster |
| dense-unique | **10.9** | 18.5 | 124 | 1.69× faster |
| sparse-unique | **6.54** | 17.3 | 84.8 | 2.64× faster |
| dense-shared | **10.9** | 18.4 | 98.7 | 1.69× faster |
| lookup | **25.1** | 56.5 | 319 | 2.25× faster |
| canada | **1.05** | 2.95 | 1,796 | 2.80× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.2** | 19.7 | 19.7 | 1.02× faster |
| monsters | 39.5 | 43.0 | **38.3** | 1.09× faster |
| records | 139 | 284 | **119** | 2.04× faster |
| series | 98.2 | 99.6 | **96.9** | 1.01× faster |
| dense-unique | **11.8** | 11.9 | **11.8** | 1.00× faster |
| sparse-unique | **8.53** | 8.66 | 8.56 | 1.01× faster |
| dense-shared | **11.8** | 11.9 | **11.8** | 1.00× faster |
| lookup | **28.7** | 29.0 | 28.8 | 1.01× faster |
| canada | **107** | **107** | 111 | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **36.1** | 61.9 | 267 | 1.72× faster |
| monsters | **82.1** | 118 | 458 | 1.43× faster |
| records | **167** | 556 | 810 | 3.32× faster |
| series | **167** | 302 | 1,102 | 1.80× faster |
| dense-unique | **22.8** | 30.3 | 135 | 1.33× faster |
| sparse-unique | **15.1** | 25.8 | 93.6 | 1.71× faster |
| dense-shared | **22.8** | 30.4 | 111 | 1.33× faster |
| lookup | **53.8** | 85.5 | 349 | 1.59× faster |
| canada | **108** | 111 | 1,933 | 1.02× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 5.45 | 7.51 | **4.93** | 1.38× faster |
| monsters | **5.81** | 9.50 | 7.18 | 1.64× faster |
| records | **4.87** | 5.94 | 5.47 | 1.22× faster |
| series | 3.63 | 4.11 | **2.39** | 1.13× faster |
| dense-unique | 4.79 | 5.49 | **3.10** | 1.15× faster |
| sparse-unique | **3.81** | 4.64 | 4.37 | 1.22× faster |
| dense-shared | 5.01 | 5.66 | **3.11** | 1.13× faster |
| lookup | 2.03 | 3.25 | **1.72** | 1.60× faster |
| canada | 6.65 | 7.96 | **2.07** | 1.20× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0057** | 0.0082 | 246 | 1.44× faster |
| monsters | **0.0057** | 0.0089 | 414 | 1.55× faster |
| records | **0.0056** | 0.0076 | 683 | 1.35× faster |
| series | **0.0036** | 0.0048 | 994 | 1.31× faster |
| dense-unique | **0.0059** | 0.0078 | 124 | 1.31× faster |
| sparse-unique | **0.0060** | 0.0078 | 84.8 | 1.31× faster |
| dense-shared | **0.0056** | 0.0078 | 98.6 | 1.40× faster |
| lookup | **0.0047** | 0.0064 | 319 | 1.37× faster |
| canada | **0.0075** | 0.0107 | 1,794 | 1.43× faster |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, so it is scanned.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **115** | 193 | 7,464 |
| gcc 15.2 | **134** | 166 | 8,251 |
| msvc 19.42.34444 | **133** | 185 | 8,646 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **43.5** | 109 | 802 |
| gcc 15.2 | **50.5** | 82.1 | 1,090 |
| msvc 19.42.34444 | **48.7** | 114 | 1,215 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 314 | 285 | −9.4% |
| C++ verify, clang 19.1 (µs) | 69.4 | 64.5 | −7.1% |
| C++ full traversal, clang 19.1 (µs) | 95.8 | 95.7 | −0.1% |
| C++ verify + traversal, clang 19.1 (µs) | 165 | 160 | −3.1% |
| C++ random access, clang 19.1 (µs) | 3.32 | 2.66 | −19.8% |
| C++ verify, gcc 15.2 (µs) | 43.3 | 42.9 | −1.1% |
| C++ full traversal, gcc 15.2 (µs) | 95.0 | 94.4 | −0.7% |
| C++ verify + traversal, gcc 15.2 (µs) | 139 | 137 | −1.0% |
| C++ random access, gcc 15.2 (µs) | 3.15 | 2.19 | −30.4% |
| C++ verify, msvc 19.42.34444 (µs) | 69.1 | 68.6 | −0.7% |
| C++ full traversal, msvc 19.42.34444 (µs) | 98.2 | 95.7 | −2.5% |
| C++ verify + traversal, msvc 19.42.34444 (µs) | 167 | 164 | −1.8% |
| C++ random access, msvc 19.42.34444 (µs) | 3.63 | 2.46 | −32.2% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
