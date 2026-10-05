# Benchmark results

- CPU: Intel(R) Core(TM) i7-6700K CPU @ 4.00GHz (8 logical cores)
- OS: Microsoft Windows 10.0.19045; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, gcc 15.2, msvc 19.42.34444
- Libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10.0, msgpack-cxx 9.0.0
- Recorded 2026-10-05 19:23:02Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every reader computes a checksum over every field (presence included) and must match the value computed from the C# objects before anything is timed.

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 8 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack |
|---|---:|---:|
| Size | **1.31** (1.00–3.37) | 0.87 (0.59–2.01) |
| .NET write | **6.77** (3.64–30.71) | **1.89** (0.66–3.55) |
| C++ verify (clang 19.1) | **2.72** (1.57–8.98) | **10.53** (6.88–24.54) |
| C++ full traversal (clang 19.1) | **1.05** (1.00–1.14) |  |
| C++ verify + traversal (clang 19.1) | **1.74** (1.44–2.43) | **4.94** (3.87–6.28) |
| C++ random access (clang 19.1) | **1.16** (0.86–1.75) |  |
| C++ verify (gcc 15.2) | **3.99** (2.37–13.11) | **13.82** (7.29–66.47) |
| C++ full traversal (gcc 15.2) | **1.03** (1.00–1.17) |  |
| C++ verify + traversal (gcc 15.2) | **2.02** (1.64–2.65) | **5.47** (3.74–12.62) |
| C++ random access (gcc 15.2) | **1.06** (0.81–1.20) |  |
| C++ verify (msvc 19.42.34444) | **2.55** (1.68–10.64) | **13.63** (8.95–29.19) |
| C++ full traversal (msvc 19.42.34444) | **1.07** (0.79–1.97) |  |
| C++ verify + traversal (msvc 19.42.34444) | **1.63** (1.33–3.17) | **5.85** (4.80–6.71) |
| C++ random access (msvc 19.42.34444) | **1.23** (0.94–1.71) |  |

MessagePack has no separate verify step: its "verify" is a full parse into a tree, which its traversal and random access then use (those two are left out of the MessagePack column because they exclude the parse).

Where Tessera is slower in these results (by more than 5% for the C++ reads), and why:

- **Reading every field** (FlatBuffers faster): msvc 19.42.34444: prefab 1.27×. Every member's presence bit is tested, and compilers differ in how many branches they make of these checks: MSVC makes more than Clang on union-heavy objects such as prefab's, for the same source.
- **Random reads** (FlatBuffers faster): clang 19.1: prefab 1.16×, monsters 1.07×; gcc 15.2: prefab 1.24×; msvc 19.42.34444: dense-shared 1.07×. A member's position is a popcount over the presence bits of the members before it, so in wide objects (prefab, monsters) a late member takes a few more instructions than FlatBuffers' vtable lookup.
- **Opening a buffer and reading one field** (FlatBuffers faster): gcc 15.2: prefab 1.08×. Both take 4–9 ns.
- **.NET write** (MessagePack faster): lookup 1.51×. The lookup workload writes dictionaries, which Tessera stores as sorted keys (so that C++ can binary-search them) and a separate vector of values; MessagePack writes the entries as they come, inline, and can then only scan them.
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
| prefab | **81.0** | 85.4 | 133 | 323 | 187 |
| monsters | **176** | 190 | 337 | 818 | 300 |
| records | **267** | 298 | 360 | 8,197 | 948 |
| series | 380 | **374** | 959 | 1,737 | 616 |
| dense-unique | 54.2 | **41.3** | 111 | 197 | 85.2 |
| sparse-unique | 24.6 | **21.0** | 56.9 | 133 | 74.9 |
| dense-shared | **36.5** | 41.3 | 99.5 | 195 | 83.8 |
| lookup | 377 | 280 | 519 | 6,073 | **250** |

Allocated bytes per write without the final copy (reused writer, builder or buffer):

| Workload | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| prefab | 79.0 µs, 0 B | 306 µs, 0 B | 176 µs, 0 B |
| monsters | 161 µs, 0 B | 781 µs, 0 B | 273 µs, 0 B |
| records | 263 µs, 0 B | 8,145 µs, 0 B | 917 µs, 0 B |
| series | 267 µs, 0 B | 1,613 µs, 0 B | 516 µs, 0 B |
| dense-unique | 48.5 µs, 0 B | 191 µs, 0 B | 81.7 µs, 0 B |
| sparse-unique | 22.2 µs, 0 B | 130 µs, 0 B | 71.3 µs, 0 B |
| dense-shared | 32.1 µs, 0 B | 189 µs, 0 B | 79.9 µs, 0 B |
| lookup | 334 µs, 0 B | 5,994 µs, 4,791,936 B | 214 µs, 0 B |

## C++ read — clang 19.1 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **17.2** | 34.0 | 210 | 1.98× faster |
| monsters | **44.1** | 69.1 | 347 | 1.57× faster |
| records | **24.8** | 223 | 609 | 8.98× faster |
| series | **79.9** | 212 | 783 | 2.66× faster |
| dense-unique | **11.8** | 22.3 | 90.3 | 1.89× faster |
| sparse-unique | **6.44** | 17.4 | 68.9 | 2.70× faster |
| dense-shared | **11.7** | 22.3 | 80.5 | 1.91× faster |
| lookup | **20.9** | 86.0 | 241 | 4.13× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.2** | 19.9 | 21.7 | 1.04× faster |
| monsters | **37.8** | 43.1 | 38.8 | 1.14× faster |
| records | **116** | 128 | 117 | 1.10× faster |
| series | **96.8** | 104 | 102 | 1.07× faster |
| dense-unique | **11.9** | **11.9** | **11.9** | 1.00× faster |
| sparse-unique | **8.50** | 8.55 | 8.67 | 1.01× faster |
| dense-shared | **11.8** | 11.9 | 11.9 | 1.01× faster |
| lookup | **28.8** | 30.9 | 29.0 | 1.07× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **37.4** | 57.3 | 235 | 1.53× faster |
| monsters | **84.2** | 121 | 395 | 1.44× faster |
| records | **148** | 360 | 745 | 2.43× faster |
| series | **177** | 318 | 899 | 1.79× faster |
| dense-unique | **23.6** | 34.4 | 103 | 1.46× faster |
| sparse-unique | **15.0** | 25.9 | 76.4 | 1.73× faster |
| dense-shared | **23.7** | 34.2 | 91.7 | 1.44× faster |
| lookup | **49.8** | 116 | 273 | 2.34× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 5.77 | 4.95 | **4.93** | 1.16× slower |
| monsters | 5.52 | **5.17** | 6.46 | 1.07× slower |
| records | **3.03** | 3.50 | 6.01 | 1.16× faster |
| series | 3.41 | 4.01 | **3.25** | 1.18× faster |
| dense-unique | 3.57 | 4.28 | **3.20** | 1.20× faster |
| sparse-unique | **2.57** | 4.50 | 2.77 | 1.75× faster |
| dense-shared | 3.42 | 3.97 | **3.18** | 1.16× faster |
| lookup | 1.99 | 2.42 | **1.66** | 1.22× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 0.0078 | **0.0076** | 211 | 1.02× slower |
| monsters | **0.0073** | 0.0079 | 346 | 1.09× faster |
| records | **0.0039** | 0.0049 | 610 | 1.25× faster |
| series | **0.0038** | 0.0056 | 785 | 1.46× faster |
| dense-unique | **0.0052** | 0.0061 | 89.7 | 1.17× faster |
| sparse-unique | **0.0052** | 0.0060 | 67.6 | 1.16× faster |
| dense-shared | **0.0047** | 0.0061 | 81.2 | 1.28× faster |
| lookup | **0.0042** | 0.0046 | 242 | 1.10× faster |

## C++ read — gcc 15.2 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **10.5** | 43.5 | 148 | 4.13× faster |
| monsters | **35.4** | 83.8 | 258 | 2.37× faster |
| records | **19.5** | 255 | 1,295 | 13.11× faster |
| series | **67.7** | 185 | 1,910 | 2.73× faster |
| dense-unique | **8.20** | 27.5 | 73.9 | 3.35× faster |
| sparse-unique | **4.68** | 17.9 | 50.2 | 3.83× faster |
| dense-shared | **8.16** | 27.5 | 63.9 | 3.37× faster |
| lookup | **21.6** | 91.1 | 197 | 4.22× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **19.6** | 19.7 | 19.7 | 1.00× faster |
| monsters | **39.1** | 42.1 | 40.0 | 1.08× faster |
| records | **112** | 131 | 120 | 1.17× faster |
| series | **94.9** | 95.6 | 106 | 1.01× faster |
| dense-unique | **12.1** | **12.1** | **12.1** | 1.00× faster |
| sparse-unique | **8.74** | 8.75 | 8.82 | 1.00× faster |
| dense-shared | **12.1** | **12.1** | **12.1** | 1.00× slower |
| lookup | **29.5** | 30.0 | 29.7 | 1.02× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **31.0** | 66.4 | 168 | 2.14× faster |
| monsters | **79.3** | 130 | 302 | 1.64× faster |
| records | **144** | 382 | 1,446 | 2.65× faster |
| series | **163** | 279 | 2,056 | 1.72× faster |
| dense-unique | **20.4** | 39.7 | 86.5 | 1.95× faster |
| sparse-unique | **13.5** | 26.6 | 59.2 | 1.97× faster |
| dense-shared | **20.4** | 39.7 | 76.3 | 1.95× faster |
| lookup | **51.3** | 119 | 227 | 2.33× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 5.31 | 4.28 | **3.97** | 1.24× slower |
| monsters | **4.97** | 5.19 | 5.02 | 1.04× faster |
| records | **4.00** | 4.14 | 5.50 | 1.04× faster |
| series | 3.28 | 3.90 | **2.28** | 1.19× faster |
| dense-unique | 3.99 | 3.95 | **3.30** | 1.01× slower |
| sparse-unique | **2.22** | 2.59 | 2.77 | 1.17× faster |
| dense-shared | 3.87 | 4.32 | **3.43** | 1.12× faster |
| lookup | 2.38 | 2.86 | **1.93** | 1.20× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 0.0066 | **0.0061** | 147 | 1.08× slower |
| monsters | **0.0067** | 0.0070 | 260 | 1.04× faster |
| records | **0.0042** | 0.0049 | 1,292 | 1.18× faster |
| series | **0.0043** | 0.0048 | 1,923 | 1.11× faster |
| dense-unique | **0.0058** | 0.0069 | 74.4 | 1.20× faster |
| sparse-unique | **0.0058** | 0.0069 | 50.0 | 1.19× faster |
| dense-shared | **0.0050** | 0.0069 | 64.1 | 1.39× faster |
| lookup | **0.0037** | 0.0050 | 196 | 1.36× faster |

## C++ read — msvc 19.42.34444 (µs, lower is better)

**Verify (MessagePack: parse into a tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **16.7** | 40.0 | 253 | 2.40× faster |
| monsters | **41.4** | 71.7 | 425 | 1.73× faster |
| records | **23.8** | 254 | 696 | 10.64× faster |
| series | **75.6** | 185 | 1,046 | 2.45× faster |
| dense-unique | **11.1** | 18.7 | 128 | 1.69× faster |
| sparse-unique | **6.56** | 16.7 | 89.9 | 2.54× faster |
| dense-shared | **11.0** | 18.5 | 98.9 | 1.68× faster |
| lookup | **25.5** | 58.0 | 340 | 2.27× faster |

**Full traversal of every field (MessagePack: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 25.5 | **20.0** | 20.1 | 1.27× slower |
| monsters | 41.6 | 44.5 | **39.3** | 1.07× faster |
| records | 146 | 287 | **123** | 1.97× faster |
| series | 103 | **99.7** | 102 | 1.03× slower |
| dense-unique | 12.0 | 12.1 | **11.9** | 1.01× faster |
| sparse-unique | 8.70 | 8.79 | **8.65** | 1.01× faster |
| dense-shared | 12.0 | 12.1 | **11.9** | 1.01× faster |
| lookup | 29.0 | 30.1 | **28.9** | 1.04× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **43.6** | 64.3 | 276 | 1.47× faster |
| monsters | **87.3** | 123 | 473 | 1.41× faster |
| records | **173** | 549 | 839 | 3.17× faster |
| series | **178** | 286 | 1,177 | 1.60× faster |
| dense-unique | **23.1** | 30.9 | 140 | 1.34× faster |
| sparse-unique | **15.3** | 25.6 | 98.0 | 1.67× faster |
| dense-shared | **23.1** | 30.8 | 111 | 1.33× faster |
| lookup | **54.8** | 87.5 | 368 | 1.60× faster |

**1000 random element reads (MessagePack: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | 6.54 | 7.86 | **4.89** | 1.20× faster |
| monsters | **6.25** | 9.46 | 7.25 | 1.51× faster |
| records | **5.21** | 7.14 | 5.72 | 1.37× faster |
| series | 3.74 | 4.23 | **2.38** | 1.13× faster |
| dense-unique | 5.70 | 5.69 | **3.18** | 1.00× slower |
| sparse-unique | **3.97** | 4.61 | 4.45 | 1.16× faster |
| dense-shared | 5.81 | 5.44 | **3.21** | 1.07× slower |
| lookup | 2.10 | 3.58 | **1.73** | 1.71× faster |

**Open + read one field**

| Workload | Tessera | FlatBuffers | MessagePack | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|
| prefab | **0.0067** | 0.0084 | 252 | 1.24× faster |
| monsters | **0.0068** | 0.0089 | 432 | 1.31× faster |
| records | **0.0059** | 0.0081 | 694 | 1.38× faster |
| series | **0.0037** | 0.0049 | 1,046 | 1.31× faster |
| dense-unique | **0.0066** | 0.0079 | 128 | 1.21× faster |
| sparse-unique | **0.0066** | 0.0080 | 89.7 | 1.21× faster |
| dense-shared | **0.0064** | 0.0079 | 99.2 | 1.24× faster |
| lookup | **0.0048** | 0.0053 | 338 | 1.11× faster |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, so it is scanned.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **155** | 203 | 7,884 |
| gcc 15.2 | **140** | 167 | 8,262 |
| msvc 19.42.34444 | **172** | 197 | 8,658 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack |
|---|---:|---:|---:|
| clang 19.1 | **44.5** | 111 | 829 |
| gcc 15.2 | **49.6** | 81.7 | 1,092 |
| msvc 19.42.34444 | **48.9** | 120 | 1,244 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 380 | 342 | −10.1% |
| C++ verify, clang 19.1 (µs) | 79.9 | 65.4 | −18.1% |
| C++ full traversal, clang 19.1 (µs) | 96.8 | 96.1 | −0.8% |
| C++ verify + traversal, clang 19.1 (µs) | 177 | 162 | −8.7% |
| C++ random access, clang 19.1 (µs) | 3.41 | 2.78 | −18.3% |
| C++ verify, gcc 15.2 (µs) | 67.7 | 40.4 | −40.3% |
| C++ full traversal, gcc 15.2 (µs) | 94.9 | 93.8 | −1.2% |
| C++ verify + traversal, gcc 15.2 (µs) | 163 | 135 | −17.3% |
| C++ random access, gcc 15.2 (µs) | 3.28 | 2.28 | −30.4% |
| C++ verify, msvc 19.42.34444 (µs) | 75.6 | 70.3 | −7.0% |
| C++ full traversal, msvc 19.42.34444 (µs) | 103 | 96.4 | −6.5% |
| C++ verify + traversal, msvc 19.42.34444 (µs) | 178 | 167 | −6.4% |
| C++ random access, msvc 19.42.34444 (µs) | 3.74 | 2.44 | −34.8% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
