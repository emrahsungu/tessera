# Benchmark results

- CPU: Intel(R) Core(TM) i7-6700K CPU @ 4.00GHz (8 logical cores)
- OS: Microsoft Windows 10.0.19045; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, gcc 15.2, msvc 19.42.34444
- Libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10.0, msgpack-cxx 9.0.0
- Recorded 2026-10-06 09:06:30Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every reader computes a checksum over every field (presence included) and must match the value computed from the C# objects before anything is timed. The C++ readers' small helper functions are force-inlined with MSVC, for all three libraries: MSVC stopped inlining them into one large function when it read Tessera (`benchmarks/native/readers.hpp` explains).

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 8 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack |
|---|---:|---:|
| Size | **1.31** (1.00–3.37) | 0.87 (0.59–2.01) |
| .NET write | **7.58** (4.33–30.48) | **2.14** (1.10–3.56) |
| C++ verify (clang 19.1) | **2.80** (1.72–8.76) | **10.70** (6.84–23.56) |
| C++ full traversal (clang 19.1) | **1.05** (1.00–1.13) |  |
| C++ verify + traversal (clang 19.1) | **1.76** (1.45–2.51) | **4.95** (3.88–6.34) |
| C++ random access (clang 19.1) | **1.24** (1.03–2.08) |  |
| C++ verify (gcc 15.2) | **4.37** (2.48–13.53) | **15.06** (7.69–65.71) |
| C++ full traversal (gcc 15.2) | **1.03** (0.99–1.19) |  |
| C++ verify + traversal (gcc 15.2) | **2.14** (1.68–2.76) | **5.64** (3.90–14.24) |
| C++ random access (gcc 15.2) | **1.13** (1.00–1.45) |  |
| C++ verify (msvc 19.42.34444) | **2.66** (1.65–11.95) | **13.75** (8.73–29.65) |
| C++ full traversal (msvc 19.42.34444) | **1.11** (1.00–2.06) |  |
| C++ verify + traversal (msvc 19.42.34444) | **1.70** (1.32–3.37) | **5.88** (4.70–7.47) |
| C++ random access (msvc 19.42.34444) | **1.33** (1.05–1.73) |  |

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

## .NET write (µs per buffer, lower is better)

Object graph to `byte[]` with a reused writer/builder (each library's normal path). Tessera's default shares equal strings.

| Workload | Tessera | Tessera (no sharing) | Tessera (full sharing) | FlatBuffers | MessagePack |
|---|---:|---:|---:|---:|---:|
| prefab | 73.8 | **73.6** | 125 | 320 | 177 |
| monsters | 182 | **161** | 335 | 795 | 301 |
| records | **260** | 271 | 350 | 7,911 | 917 |
| series | 359 | **355** | 915 | 1,684 | 613 |
| dense-unique | 43.9 | **31.6** | 102 | 196 | 85.0 |
| sparse-unique | 20.5 | **17.7** | 52.3 | 129 | 72.8 |
| dense-shared | 34.9 | **31.0** | 96.6 | 194 | 84.3 |
| lookup | 227 | **213** | 361 | 5,842 | 249 |

Allocated bytes per write without the final copy (reused writer, builder or buffer):

| Workload | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| prefab | 73.0 µs, 0 B | 302 µs, 0 B | 168 µs, 0 B |
| monsters | 155 µs, 0 B | 766 µs, 0 B | 277 µs, 0 B |
| records | 258 µs, 0 B | 7,885 µs, 0 B | 885 µs, 0 B |
| series | 260 µs, 0 B | 1,584 µs, 0 B | 509 µs, 0 B |
| dense-unique | 40.9 µs, 0 B | 191 µs, 0 B | 81.9 µs, 0 B |
| sparse-unique | 20.9 µs, 0 B | 126 µs, 0 B | 70.2 µs, 0 B |
| dense-shared | 32.6 µs, 0 B | 189 µs, 0 B | 81.3 µs, 0 B |
| lookup | 185 µs, 0 B | 5,775 µs, 4,791,936 B | 215 µs, 0 B |

## C++ read — clang 19.1 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **16.7** | 33.2 | 206 | 1.98× faster |
| monsters | **39.6** | 68.1 | 340 | 1.72× faster |
| records | **25.4** | 222 | 599 | 8.76× faster |
| series | **69.2** | 210 | 753 | 3.03× faster |
| dense-unique | **11.5** | 21.9 | 88.1 | 1.90× faster |
| sparse-unique | **6.28** | 17.3 | 66.5 | 2.75× faster |
| dense-shared | **11.5** | 21.9 | 78.7 | 1.91× faster |
| lookup | **20.5** | 84.7 | 235 | 4.13× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **18.9** | 19.3 | 21.3 | 1.03× faster |
| monsters | **37.2** | 42.1 | 38.1 | 1.13× faster |
| records | **112** | 127 | 113 | 1.13× faster |
| series | **95.8** | 102 | 97.1 | 1.06× faster |
| dense-unique | **11.8** | **11.8** | **11.8** | 1.00× faster |
| sparse-unique | **8.44** | 8.47 | 8.57 | 1.00× faster |
| dense-shared | **11.8** | **11.8** | **11.8** | 1.00× faster |
| lookup | **28.7** | 30.2 | 28.9 | 1.05× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **36.0** | 55.6 | 229 | 1.54× faster |
| monsters | **79.6** | 119 | 384 | 1.49× faster |
| records | **144** | 360 | 721 | 2.51× faster |
| series | **165** | 312 | 854 | 1.89× faster |
| dense-unique | **23.3** | 33.7 | 100 | 1.45× faster |
| sparse-unique | **14.7** | 25.7 | 74.7 | 1.75× faster |
| dense-shared | **23.3** | 33.7 | 90.4 | 1.45× faster |
| lookup | **49.2** | 115 | 264 | 2.34× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **4.65** | 4.90 | 4.94 | 1.05× faster |
| monsters | **4.90** | 5.07 | 6.40 | 1.03× faster |
| records | **2.95** | 3.36 | 5.91 | 1.14× faster |
| series | 3.34 | 3.89 | **3.21** | 1.16× faster |
| dense-unique | 3.49 | 4.18 | **3.14** | 1.20× faster |
| sparse-unique | **2.27** | 4.72 | 2.67 | 2.08× faster |
| dense-shared | 3.36 | 4.21 | **3.17** | 1.25× faster |
| lookup | 1.94 | 2.34 | **1.61** | 1.21× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0063** | 0.0075 | 205 | 1.20× faster |
| monsters | **0.0064** | 0.0078 | 339 | 1.21× faster |
| records | **0.0039** | 0.0049 | 598 | 1.25× faster |
| series | **0.0039** | 0.0055 | 760 | 1.40× faster |
| dense-unique | **0.0051** | 0.0060 | 87.8 | 1.17× faster |
| sparse-unique | **0.0051** | 0.0060 | 66.3 | 1.17× faster |
| dense-shared | **0.0047** | 0.0060 | 79.1 | 1.28× faster |
| lookup | **0.0041** | 0.0046 | 236 | 1.10× faster |

## C++ read — gcc 15.2 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **9.36** | 41.8 | 145 | 4.47× faster |
| monsters | **33.2** | 82.1 | 255 | 2.48× faster |
| records | **18.8** | 254 | 1,234 | 13.53× faster |
| series | **41.9** | 179 | 1,824 | 4.27× faster |
| dense-unique | **7.61** | 27.0 | 72.3 | 3.55× faster |
| sparse-unique | **4.46** | 18.4 | 49.9 | 4.12× faster |
| dense-shared | **7.60** | 26.9 | 64.3 | 3.54× faster |
| lookup | **22.2** | 89.3 | 191 | 4.02× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.2** | **19.2** | 19.4 | 1.00× faster |
| monsters | **38.3** | 40.3 | 38.6 | 1.05× faster |
| records | **108** | 129 | 114 | 1.19× faster |
| series | **93.0** | 93.9 | 99.5 | 1.01× faster |
| dense-unique | 12.0 | **11.9** | 12.0 | 1.00× slower |
| sparse-unique | 8.63 | **8.58** | 8.68 | 1.01× slower |
| dense-shared | **11.9** | **11.9** | 12.0 | 1.00× faster |
| lookup | **29.2** | 29.3 | 29.3 | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **28.9** | 63.3 | 164 | 2.19× faster |
| monsters | **75.0** | 126 | 296 | 1.68× faster |
| records | **136** | 375 | 1,364 | 2.76× faster |
| series | **135** | 266 | 1,929 | 1.96× faster |
| dense-unique | **19.6** | 41.2 | 84.1 | 2.10× faster |
| sparse-unique | **13.1** | 28.6 | 58.4 | 2.19× faster |
| dense-shared | **19.5** | 41.1 | 76.1 | 2.10× faster |
| lookup | **51.4** | 118 | 220 | 2.29× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 3.93 | 4.15 | **3.83** | 1.06× faster |
| monsters | **3.83** | 4.52 | 4.96 | 1.18× faster |
| records | 3.65 | **3.63** | 5.15 | 1.00× slower |
| series | 2.87 | 3.40 | **2.22** | 1.18× faster |
| dense-unique | 3.64 | 3.84 | **3.50** | 1.05× faster |
| sparse-unique | **2.42** | 2.53 | 2.62 | 1.04× faster |
| dense-shared | 3.44 | 3.87 | **3.38** | 1.13× faster |
| lookup | 1.91 | 2.77 | **1.84** | 1.45× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0049** | 0.0060 | 145 | 1.23× faster |
| monsters | **0.0052** | 0.0069 | 254 | 1.32× faster |
| records | **0.0039** | 0.0051 | 1,231 | 1.31× faster |
| series | **0.0042** | 0.0046 | 1,820 | 1.11× faster |
| dense-unique | **0.0049** | 0.0067 | 72.3 | 1.37× faster |
| sparse-unique | **0.0049** | 0.0067 | 49.8 | 1.37× faster |
| dense-shared | **0.0045** | 0.0067 | 64.2 | 1.49× faster |
| lookup | **0.0036** | 0.0049 | 192 | 1.37× faster |

## C++ read — msvc 19.42.34444 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **15.5** | 38.5 | 248 | 2.49× faster |
| monsters | **36.2** | 70.1 | 421 | 1.94× faster |
| records | **21.9** | 262 | 650 | 11.95× faster |
| series | **69.1** | 186 | 981 | 2.70× faster |
| dense-unique | **11.1** | 18.3 | 124 | 1.66× faster |
| sparse-unique | **6.52** | 17.2 | 84.5 | 2.64× faster |
| dense-shared | **11.1** | 18.3 | 96.6 | 1.65× faster |
| lookup | **25.0** | 56.6 | 320 | 2.26× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.2** | 19.4 | 19.7 | 1.01× faster |
| monsters | 39.8 | 43.2 | **38.5** | 1.09× faster |
| records | 138 | 284 | **119** | 2.06× faster |
| series | 97.8 | 98.5 | **96.8** | 1.01× faster |
| dense-unique | **11.8** | 11.9 | **11.8** | 1.00× faster |
| sparse-unique | **8.54** | 8.61 | 8.56 | 1.01× faster |
| dense-shared | **11.8** | 11.9 | **11.8** | 1.00× faster |
| lookup | **28.7** | 28.9 | 28.8 | 1.01× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **35.7** | 61.4 | 267 | 1.72× faster |
| monsters | **82.4** | 120 | 461 | 1.45× faster |
| records | **165** | 558 | 776 | 3.37× faster |
| series | **167** | 283 | 1,087 | 1.69× faster |
| dense-unique | **22.9** | 30.3 | 136 | 1.32× faster |
| sparse-unique | **15.1** | 26.1 | 93.3 | 1.73× faster |
| dense-shared | **22.9** | 30.3 | 109 | 1.32× faster |
| lookup | **53.8** | 85.7 | 348 | 1.59× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 5.32 | 7.62 | **4.95** | 1.43× faster |
| monsters | **5.28** | 9.12 | 7.16 | 1.73× faster |
| records | **4.89** | 6.51 | 5.50 | 1.33× faster |
| series | 3.60 | 4.11 | **2.41** | 1.14× faster |
| dense-unique | 4.79 | 5.51 | **3.10** | 1.15× faster |
| sparse-unique | **3.55** | 4.53 | 4.36 | 1.27× faster |
| dense-shared | 5.03 | 5.29 | **3.08** | 1.05× faster |
| lookup | 2.09 | 3.45 | **1.72** | 1.65× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0057** | 0.0082 | 248 | 1.44× faster |
| monsters | **0.0057** | 0.0087 | 417 | 1.52× faster |
| records | **0.0056** | 0.0080 | 651 | 1.41× faster |
| series | **0.0036** | 0.0048 | 982 | 1.31× faster |
| dense-unique | **0.0059** | 0.0078 | 124 | 1.32× faster |
| sparse-unique | **0.0059** | 0.0079 | 84.5 | 1.33× faster |
| dense-shared | **0.0056** | 0.0078 | 96.6 | 1.39× faster |
| lookup | **0.0047** | 0.0052 | 319 | 1.11× faster |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, so it is scanned.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **118** | 194 | 8,036 |
| gcc 15.2 | **132** | 162 | 7,924 |
| msvc 19.42.34444 | **127** | 191 | 8,710 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **39.8** | 108 | 790 |
| gcc 15.2 | **48.9** | 79.1 | 1,220 |
| msvc 19.42.34444 | **48.0** | 114 | 1,230 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 359 | 326 | −9.2% |
| C++ verify, clang 19.1 (µs) | 69.2 | 64.4 | −7.0% |
| C++ full traversal, clang 19.1 (µs) | 95.8 | 95.7 | −0.1% |
| C++ verify + traversal, clang 19.1 (µs) | 165 | 160 | −2.9% |
| C++ random access, clang 19.1 (µs) | 3.34 | 2.67 | −20.2% |
| C++ verify, gcc 15.2 (µs) | 41.9 | 39.2 | −6.4% |
| C++ full traversal, gcc 15.2 (µs) | 93.0 | 93.1 | +0.1% |
| C++ verify + traversal, gcc 15.2 (µs) | 135 | 132 | −2.4% |
| C++ random access, gcc 15.2 (µs) | 2.87 | 2.26 | −21.2% |
| C++ verify, msvc 19.42.34444 (µs) | 69.1 | 68.5 | −0.8% |
| C++ full traversal, msvc 19.42.34444 (µs) | 97.8 | 95.7 | −2.2% |
| C++ verify + traversal, msvc 19.42.34444 (µs) | 167 | 164 | −1.7% |
| C++ random access, msvc 19.42.34444 (µs) | 3.60 | 2.42 | −32.7% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
