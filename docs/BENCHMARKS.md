# Benchmark results

- CPU: Intel(R) Core(TM) i9-14900 (32 logical cores)
- OS: Microsoft Windows 10.0.26200; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, msvc 19.43.34810
- Libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10.0, msgpack-cxx 9.0.0
- Recorded 2026-10-05 08:26:02Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every reader computes a checksum over every field (presence included) and must match the value computed from the C# objects before anything is timed.

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 8 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack |
|---|---:|---:|
| Size | **1.31** (1.00–3.37) | 0.87 (0.59–2.01) |
| .NET write | **6.17** (3.24–27.74) | **1.72** (0.30–3.25) |
| C++ verify (clang 19.1) | **3.13** (1.59–13.59) | **11.84** (8.26–28.69) |
| C++ full traversal (clang 19.1) | **1.02** (0.97–1.18) |  |
| C++ verify + traversal (clang 19.1) | **1.70** (1.29–3.27) | **4.42** (3.56–5.80) |
| C++ random access (clang 19.1) | **1.01** (0.58–1.30) |  |
| C++ verify (msvc 19.43.34810) | **3.53** (2.31–23.38) | **17.00** (11.88–44.83) |
| C++ full traversal (msvc 19.43.34810) | 0.94 (0.62–1.02) |  |
| C++ verify + traversal (msvc 19.43.34810) | **1.53** (1.14–1.83) | **4.70** (1.84–6.13) |
| C++ random access (msvc 19.43.34810) | **1.11** (0.92–1.30) |  |

MessagePack has no separate verify step: its "verify" is a full parse into a tree, which its traversal and random access then use (those two are left out of the MessagePack column because they exclude the parse).

Where Tessera is slower in these results (by more than 5% for the C++ reads), and why:

- **Reading every field** (FlatBuffers faster): msvc 19.43.34810: prefab 1.63×. Every member's presence bit is tested, and compilers differ in how many branches they make of these checks: MSVC makes more than Clang on union-heavy objects such as prefab's, for the same source.
- **Random reads** (FlatBuffers faster): clang 19.1: prefab 1.24×, monsters 1.07×, lookup 1.71×; msvc 19.43.34810: lookup 1.09×. A member's position is a popcount over the presence bits of the members before it, so in wide objects (prefab, monsters) a late member takes a few more instructions than FlatBuffers' vtable lookup. On lookup these are dictionary entries read by index: keys and values are two vectors, so an entry takes one or two more loads than FlatBuffers' vector of key-value tables (lookups by key are faster).
- **Opening a buffer and reading one field** (FlatBuffers faster): clang 19.1: lookup 1.22×; msvc 19.43.34810: records 1.19×, series 1.20×, lookup 1.35×. Both take 2–5 ns.
- **.NET write** (MessagePack faster): lookup 3.36×. The lookup workload writes dictionaries, whose keys Tessera sorts so that C++ can binary-search them; MessagePack writes the entries as they come and can then only scan them.
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
| lookup | 112,366 | 146,563 | 93,180 |

## .NET write (µs per buffer, lower is better)

Object graph to `byte[]` with a reused writer/builder (each library's normal path). Tessera's default shares equal strings.

| Workload | Tessera | Tessera (no sharing) | Tessera (full sharing) | FlatBuffers | MessagePack |
|---|---:|---:|---:|---:|---:|
| prefab | **29.1** | 35.5 | 54.0 | 165 | 93.0 |
| monsters | **85.9** | 93.1 | 158 | 392 | 152 |
| records | **162** | 183 | 211 | 4,487 | 525 |
| series | 204 | **200** | 504 | 883 | 320 |
| dense-unique | 29.1 | **20.7** | 60.0 | 94.2 | 41.0 |
| sparse-unique | 12.8 | **10.9** | 31.8 | 61.8 | 34.1 |
| dense-shared | **16.8** | 20.6 | 52.3 | 93.8 | 40.7 |
| lookup | 447 | 389 | 524 | 3,483 | **133** |

Allocated bytes per write without the final copy (reused writer, builder or buffer):

| Workload | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| prefab | 26.0 µs, 0 B | 152 µs, 0 B | 85.8 µs, 0 B |
| monsters | 76.3 µs, 0 B | 364 µs, 0 B | 133 µs, 0 B |
| records | 161 µs, 0 B | 4,448 µs, 0 B | 498 µs, 0 B |
| series | 141 µs, 0 B | 797 µs, 0 B | 257 µs, 0 B |
| dense-unique | 26.1 µs, 0 B | 89.3 µs, 0 B | 38.3 µs, 0 B |
| sparse-unique | 11.7 µs, 0 B | 59.5 µs, 0 B | 32.3 µs, 0 B |
| dense-shared | 14.5 µs, 0 B | 89.2 µs, 0 B | 38.6 µs, 0 B |
| lookup | 409 µs, 112 B | 3,354 µs, 4,791,936 B | 117 µs, 0 B |

## C++ read — clang 19.1 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **7.11** | 15.3 | 116 | 2.15× faster |
| monsters | **20.2** | 32.1 | 189 | 1.59× faster |
| records | **12.1** | 165 | 348 | 13.59× faster |
| series | **43.4** | 136 | 366 | 3.13× faster |
| dense-unique | **5.05** | 12.0 | 46.6 | 2.38× faster |
| sparse-unique | **3.19** | 9.38 | 41.1 | 2.94× faster |
| dense-shared | **5.13** | 12.0 | 42.4 | 2.33× faster |
| lookup | **11.0** | 43.0 | 116 | 3.92× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 15.1 | **14.6** | 14.9 | 1.04× slower |
| monsters | **29.1** | 29.3 | 29.5 | 1.01× faster |
| records | **61.1** | 72.2 | 62.6 | 1.18× faster |
| series | **74.9** | 76.0 | 76.1 | 1.02× faster |
| dense-unique | 9.21 | **9.20** | 9.28 | 1.00× slower |
| sparse-unique | **6.62** | 6.64 | 6.69 | 1.00× faster |
| dense-shared | 9.26 | 9.25 | **9.18** | 1.00× slower |
| lookup | **22.3** | 22.5 | 22.7 | 1.01× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **22.4** | 30.6 | 130 | 1.37× faster |
| monsters | **49.6** | 64.0 | 221 | 1.29× faster |
| records | **78.1** | 255 | 419 | 3.27× faster |
| series | **118** | 212 | 445 | 1.80× faster |
| dense-unique | **14.3** | 21.5 | 55.7 | 1.50× faster |
| sparse-unique | **10.0** | 16.0 | 48.0 | 1.60× faster |
| dense-shared | **14.4** | 21.1 | 51.1 | 1.47× faster |
| lookup | **33.3** | 64.8 | 139 | 1.95× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 2.89 | 2.33 | **2.23** | 1.24× slower |
| monsters | 2.73 | 2.56 | **2.51** | 1.07× slower |
| records | **1.45** | 1.81 | 1.87 | 1.25× faster |
| series | 1.61 | 1.75 | **1.31** | 1.09× faster |
| dense-unique | 1.77 | 2.20 | **1.45** | 1.25× faster |
| sparse-unique | **1.12** | 1.45 | 1.26 | 1.30× faster |
| dense-shared | 1.79 | 2.06 | **1.44** | 1.15× faster |
| lookup | 2.30 | 1.35 | **0.67** | 1.71× slower |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0033** | 0.0036 | 114 | 1.07× faster |
| monsters | **0.0034** | 0.0038 | 190 | 1.12× faster |
| records | **0.0020** | 0.0025 | 347 | 1.27× faster |
| series | **0.0018** | 0.0027 | 379 | 1.47× faster |
| dense-unique | **0.0025** | 0.0034 | 46.7 | 1.34× faster |
| sparse-unique | **0.0025** | 0.0034 | 41.3 | 1.35× faster |
| dense-shared | **0.0024** | 0.0034 | 42.2 | 1.42× faster |
| lookup | 0.0030 | **0.0024** | 117 | 1.22× slower |

## C++ read — msvc 19.43.34810 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **6.95** | 20.7 | 144 | 2.98× faster |
| monsters | **14.9** | 37.5 | 261 | 2.51× faster |
| records | **8.41** | 197 | 377 | 23.38× faster |
| series | **42.8** | 106 | 508 | 2.49× faster |
| dense-unique | **4.72** | 12.3 | 66.1 | 2.61× faster |
| sparse-unique | **3.20** | 11.3 | 50.5 | 3.53× faster |
| dense-shared | **4.68** | 12.3 | 62.1 | 2.63× faster |
| lookup | **13.1** | 30.2 | 161 | 2.31× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 24.4 | 15.0 | **14.9** | 1.63× slower |
| monsters | 29.8 | **28.8** | 29.6 | 1.03× slower |
| records | 230 | 228 | **65.4** | 1.01× slower |
| series | **75.3** | 76.8 | 76.4 | 1.02× faster |
| dense-unique | 9.24 | 9.28 | **9.23** | 1.00× faster |
| sparse-unique | 6.74 | **6.63** | 6.67 | 1.02× slower |
| dense-shared | 9.32 | 9.32 | **9.20** | 1.00× slower |
| lookup | 22.6 | 22.7 | **22.5** | 1.01× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **31.4** | 35.9 | 159 | 1.14× faster |
| monsters | **47.8** | 70.7 | 293 | 1.48× faster |
| records | **244** | 436 | 448 | 1.78× faster |
| series | **118** | 183 | 585 | 1.55× faster |
| dense-unique | **14.0** | 21.5 | 75.0 | 1.54× faster |
| sparse-unique | **9.84** | 18.0 | 57.7 | 1.83× faster |
| dense-shared | **14.0** | 21.4 | 72.7 | 1.52× faster |
| lookup | **35.7** | 52.5 | 185 | 1.47× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 3.45 | 3.84 | **1.80** | 1.11× faster |
| monsters | 3.25 | 3.85 | **2.23** | 1.18× faster |
| records | 2.37 | 2.89 | **1.85** | 1.22× faster |
| series | 1.50 | 1.96 | **0.97** | 1.30× faster |
| dense-unique | 2.57 | 2.86 | **1.46** | 1.11× faster |
| sparse-unique | 1.81 | 1.90 | **1.27** | 1.05× faster |
| dense-shared | 2.66 | 2.70 | **1.45** | 1.01× faster |
| lookup | 2.08 | 1.91 | **0.69** | 1.09× slower |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0041** | 0.0043 | 144 | 1.05× faster |
| monsters | **0.0038** | 0.0045 | 263 | 1.18× faster |
| records | 0.0041 | **0.0035** | 381 | 1.19× slower |
| series | 0.0032 | **0.0027** | 505 | 1.20× slower |
| dense-unique | **0.0036** | 0.0042 | 66.1 | 1.17× faster |
| sparse-unique | **0.0035** | 0.0042 | 50.9 | 1.17× faster |
| dense-shared | **0.0033** | 0.0042 | 62.8 | 1.26× faster |
| lookup | 0.0040 | **0.0030** | 160 | 1.35× slower |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, so it is scanned.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **101** | 130 | 4,313 |
| msvc 19.43.34810 | **114** | 115 | 4,840 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **25.6** | 65.5 | 485 |
| msvc 19.43.34810 | **30.6** | 71.9 | 759 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 204 | 184 | −9.5% |
| C++ verify, clang 19.1 (µs) | 43.4 | 35.6 | −18.1% |
| C++ full traversal, clang 19.1 (µs) | 74.9 | 75.4 | +0.7% |
| C++ verify + traversal, clang 19.1 (µs) | 118 | 111 | −5.4% |
| C++ random access, clang 19.1 (µs) | 1.61 | 1.31 | −18.7% |
| C++ verify, msvc 19.43.34810 (µs) | 42.8 | 38.8 | −9.3% |
| C++ full traversal, msvc 19.43.34810 (µs) | 75.3 | 74.8 | −0.8% |
| C++ verify + traversal, msvc 19.43.34810 (µs) | 118 | 113 | −4.3% |
| C++ random access, msvc 19.43.34810 (µs) | 1.50 | 1.07 | −28.7% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
