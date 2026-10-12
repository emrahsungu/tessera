# Benchmark results

- CPU: Intel(R) Core(TM) i7-6700K CPU @ 4.00GHz (8 logical cores)
- OS: Microsoft Windows 10.0.19045; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, gcc 15.2, msvc 19.42.34444
- .NET libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10, MemoryPack 1.21.4, Google.Protobuf 3.36.2, System.Text.Json 10.0.12, Newtonsoft.Json 13.0.4; measured with BenchmarkDotNet 0.15.8
- C++ libraries: FlatBuffers 25.12.19, msgpack-cxx 9.0.0, libprotobuf 36.2 (with Abseil 20250512.1), simdjson 5.0.3, each compiled from source with the benchmark's flags
- Recorded 2026-10-12 10:23:21Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every library writes the same C# objects. Every C++ reader computes a checksum over every field (presence included), which must match the value computed from the C# objects before anything is timed. The C++ readers' small helper functions are force-inlined with MSVC, for every library: MSVC stopped inlining them into one large function when it read Tessera (`benchmarks/native/readers.hpp` explains).

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 9 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack | vs Protobuf | vs JSON | vs MemoryPack |
|---|---:|---:|---:|---:|---:|
| Size | **1.27** (1.00–3.37) | 0.90 (0.59–2.01) | 0.92 (0.65–1.42) | **2.15** (1.30–3.04) | **1.28** (0.87–8.48) |
| .NET write | **6.36** (1.39–31.27) | **2.29** (1.15–6.67) | **4.58** (2.70–22.97) | **14.44** (4.30–185.48) | 0.66 (0.45–0.84) |
| C++ verify (clang 19.1) | **2.98** (1.69–9.21) | **18.68** (6.69–1459.30) | **14.43** (5.24–813.85) | **24.90** (10.74–2877.15) |  |
| C++ full traversal (clang 19.1) | **1.06** (1.00–1.20) |  |  |  |  |
| C++ verify + traversal (clang 19.1) | **1.68** (1.05–2.52) | **5.72** (3.99–14.53) | **4.53** (2.21–9.81) | **11.73** (5.95–27.97) |  |
| C++ random access (clang 19.1) | **1.27** (1.05–1.97) |  |  |  |  |
| C++ verify (gcc 15.2) | **3.97** (2.44–18.85) | **18.79** (7.01–1146.78) | **15.71** (6.23–638.89) | **33.99** (14.18–3122.13) |  |
| C++ full traversal (gcc 15.2) | **1.03** (0.99–1.20) |  |  |  |  |
| C++ verify + traversal (gcc 15.2) | **1.86** (1.02–2.80) | **4.71** (3.71–10.16) | **4.05** (2.31–9.49) | **10.65** (5.40–25.20) |  |
| C++ random access (gcc 15.2) | **1.20** (1.01–2.12) |  |  |  |  |
| C++ verify (msvc 19.42.34444) | **2.73** (1.66–12.41) | **22.33** (8.05–1687.54) | **17.29** (6.76–1083.31) | **27.39** (12.52–2863.14) |  |
| C++ full traversal (msvc 19.42.34444) | **1.12** (1.00–2.03) |  |  |  |  |
| C++ verify + traversal (msvc 19.42.34444) | **1.62** (1.03–3.32) | **6.39** (4.45–17.11) | **5.12** (2.23–11.04) | **18.33** (5.59–55.99) |  |
| C++ random access (msvc 19.42.34444) | **1.32** (1.13–1.79) |  |  |  |  |

MessagePack, Protobuf and JSON are parsed before they are read: their "verify" is the parse (into a tree for MessagePack and Protobuf, into simdjson's DOM for JSON), and their traversal and random access read a tree parsed beforehand, so those two are left out of their columns. JSON is written by System.Text.Json and read by simdjson. MemoryPack has no C++ reader. How each parser is set up is in the C++ section below.

Where Tessera is slower or larger in these results (by more than 5% for the C++ reads), and why:

- **.NET write** (MemoryPack faster): prefab 1.32×, monsters 1.55×, records 2.22×, series 1.40×, dense-unique 1.97×, sparse-unique 1.29×, dense-shared 1.60×, lookup 1.20×, canada 1.30×. MemoryPack writes the members one after another, with nothing that lets a reader find one without reading those before it, and copies arrays of plain structs as they lie in memory; it can only be read from the start, and only by .NET.
- **Size** (MessagePack smaller): monsters 1.19×, series 1.11×, dense-unique 1.69×, sparse-unique 1.26×, dense-shared 1.70×, lookup 1.39×. MessagePack stores small integers in one or two bytes; Tessera keeps values fixed-width so they can be read in place.
- **Size** (Protobuf smaller): monsters 1.07×, series 1.24×, dense-unique 1.53×, sparse-unique 1.31×, dense-shared 1.33×, lookup 1.09×. Protobuf stores integers as varints (one byte for small values) and leaves out zeros; Tessera keeps values fixed-width so they can be read in place.
- **Size** (MemoryPack smaller): monsters 1.08×, dense-unique 1.15×, lookup 1.09×, canada 1.00×. MemoryPack writes the members one after another, with nothing that lets a reader find one without reading those before it; Tessera stores the offsets that let C++ read any member directly.

## Size (bytes, smaller is better)

JSON is System.Text.Json's; Newtonsoft.Json writes the same JSON, apart from how it formats some numbers. The smallest of the libraries' default outputs is in bold.

| Workload | Tessera | Tessera (full sharing) | Tessera (no schema) | FlatBuffers | FlatBuffers (shared strings) | MessagePack | Protobuf | MemoryPack | JSON |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| prefab | **75,800** | 74,232 | 74,384 | 97,332 | 83,420 | 83,330 | 76,625 | 83,725 | 230,444 |
| monsters | 155,096 | 147,896 | 154,680 | 183,424 | 158,752 | **130,187** | 145,439 | 143,273 | 399,587 |
| records | **70,928** | 70,912 | 70,336 | 239,336 | 215,400 | 142,887 | 100,434 | 601,156 | 197,892 |
| series | 581,288 | 581,288 | 581,112 | 582,776 | 582,776 | 523,153 | **467,487** | 584,086 | 1,367,338 |
| dense-unique | 69,528 | 69,528 | 69,272 | 73,396 | 73,396 | **41,029** | 45,380 | 60,355 | 113,193 |
| sparse-unique | 33,480 | 33,480 | 33,224 | 34,724 | 34,724 | 26,634 | **25,563** | 40,452 | 57,432 |
| dense-shared | 52,984 | 5,352 | 52,728 | 69,700 | 57,604 | **31,121** | 39,758 | 58,777 | 99,489 |
| lookup | 240,416 | 240,368 | 240,056 | 280,064 | 280,064 | **173,020** | 221,089 | 220,009 | 311,545 |
| canada | 895,344 | 895,344 | 894,936 | 898,792 | 898,792 | 1,057,063 | 1,112,770 | **891,024** | 2,534,738 |

gzip -9 of the same buffers (for transfer/storage comparisons):

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | MemoryPack | JSON |
|---|---:|---:|---:|---:|---:|---:|
| prefab | 20,730 | 22,422 | 15,575 | 17,843 | 15,773 | 20,002 |
| monsters | 98,996 | 106,353 | 94,599 | 100,462 | 87,493 | 126,691 |
| records | 59,667 | 77,163 | 63,848 | 64,460 | 77,481 | 72,463 |
| series | 240,987 | 287,823 | 193,227 | 186,714 | 183,592 | 206,997 |
| dense-unique | 20,126 | 25,449 | 20,862 | 16,704 | 17,982 | 21,972 |
| sparse-unique | 15,890 | 18,337 | 13,298 | 11,764 | 13,593 | 13,914 |
| dense-shared | 7,462 | 6,245 | 628 | 744 | 906 | 1,352 |
| lookup | 112,921 | 146,563 | 93,180 | 96,906 | 91,435 | 92,527 |
| canada | 471,467 | 473,596 | 470,468 | 476,287 | 468,683 | 580,661 |

## .NET write (µs per buffer, lower is better)

Object graph to `byte[]` with a reused writer, builder or buffer (each library's normal path). Tessera's default shares equal strings. FlatBuffers is written with its builder API from the objects, and so is Protobuf: it writes its own generated message classes, which the objects are converted to on every write; "Protobuf (prebuilt messages)" leaves the conversion out, as when a program keeps its data in the messages. Both JSON libraries write the same JSON (System.Text.Json with its source-generated serializers). The fastest of the libraries that write from the objects is in bold.

Measured with BenchmarkDotNet: every case in a process of its own, with collections forced between iterations, and the large-object threshold raised to 4 MB, so result arrays die young like any other object (at the default 85 KB, the full collections that free them took time that varied erratically with the result's exact size). The tables after this one leave the final copy out.

| Workload | Tessera | Tessera (no sharing) | Tessera (full sharing) | FlatBuffers | MessagePack | MemoryPack | Protobuf | Protobuf (prebuilt messages) | System.Text.Json | Newtonsoft.Json |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| prefab | 76.8 | 62.1 | 128 | 309 | 180 | **58.3** | 397 | 289 | 1,110 | 3,176 |
| monsters | 162 | 134 | 311 | 836 | 290 | **104** | 729 | 516 | 2,281 | 4,695 |
| records | 266 | 263 | 355 | 7,887 | 882 | **120** | 729 | 564 | 1,146 | 2,686 |
| series | 304 | 307 | 879 | 1,845 | 571 | **217** | 1,015 | 606 | 4,461 | 10,231 |
| dense-unique | 42.8 | 27.9 | 90.2 | 177 | 70.1 | **21.8** | 223 | 115 | 450 | 1,007 |
| sparse-unique | 23.3 | 19.1 | 45.9 | 118 | 57.2 | **18.1** | 62.9 | 43.6 | 335 | 708 |
| dense-shared | 35.1 | 27.7 | 90.0 | 176 | 75.4 | **22.0** | 150 | 97.8 | 397 | 924 |
| lookup | 196 | 183 | 311 | 6,128 | 225 | **164** | 590 | 403 | 1,311 | 2,730 |
| canada | 90.0 | 94.9 | 338 | 125 | 601 | **69.4** | 2,068 | 1,125 | 16,701 | 26,159 |

Without the final copy (into a reused writer, builder or buffer), µs:

| Workload | Tessera | FlatBuffers | MessagePack | MemoryPack | Protobuf | System.Text.Json | Newtonsoft.Json |
|---|---:|---:|---:|---:|---:|---:|---:|
| prefab | 73.5 | 309 | 169 | **50.8** | 326 | 1,061 | 3,146 |
| monsters | 157 | 825 | 282 | **100** | 596 | 2,313 | 5,070 |
| records | 261 | 7,830 | 935 | **93.3** | 583 | 1,158 | 2,651 |
| series | 265 | 1,953 | 521 | **180** | 842 | 4,355 | 10,323 |
| dense-unique | 39.2 | 238 | 68.1 | **19.5** | 132 | 457 | 1,013 |
| sparse-unique | 21.3 | 113 | 61.3 | **15.5** | 54.1 | 339 | 713 |
| dense-shared | 33.0 | 171 | 68.5 | **19.6** | 124 | 406 | 927 |
| lookup | 184 | 6,113 | 211 | **154** | 473 | 1,317 | 2,709 |
| canada | 52.2 | 66.5 | 995 | **30.9** | 1,889 | 16,277 | 26,248 |

Bytes allocated per write without the final copy:

| Workload | Tessera | FlatBuffers | MessagePack | MemoryPack | Protobuf | System.Text.Json | Newtonsoft.Json |
|---|---:|---:|---:|---:|---:|---:|---:|
| prefab | 0 | 0 | 0 | 0 | 342,952 | 176,904 | 1,152,240 |
| monsters | 0 | 0 | 0 | 0 | 665,224 | 0 | 1,379,120 |
| records | 0 | 0 | 0 | 0 | 432,984 | 0 | 427,456 |
| series | 0 | 0 | 0 | 0 | 1,644,600 | 0 | 3,782,024 |
| dense-unique | 0 | 0 | 0 | 0 | 213,192 | 0 | 392,032 |
| sparse-unique | 0 | 0 | 0 | 0 | 105,672 | 0 | 264,048 |
| dense-shared | 0 | 0 | 0 | 0 | 213,192 | 0 | 376,416 |
| lookup | 0 | 4,791,936 | 0 | 0 | 822,648 | 0 | 1,065,600 |
| canada | 0 | 120 | 0 | 0 | 3,581,504 | 0 | 10,980,280 |

## C++ read

Tessera and FlatBuffers read the buffer where it lies. The parsers are each set up their fastest way, measured, and reuse their memory from one parse to the next:

- msgpack-cxx parses into a reused zone, with strings and binary data referenced in the buffer (its zero-copy option) rather than copied. Its tree for the traversal, random access and lookups is parsed once with the strings copied: that reads as fast and scans keys twice as fast.
- libprotobuf parses into a reused message object, as Google advises (a message keeps the memory it allocated when it is cleared). Its parsed tree for the traversal and random access is on an arena, which reads a few percent faster. A new arena per parse, the other fast way, is faster on prefab (clang 19.1 227 µs instead of 330; gcc 15.2 184 µs instead of 259; msvc 19.42.34444 241 µs instead of 328): there every element replaces a oneof's member, which a reused message allocates anew.
- simdjson reuses its parser. "Verify" parses into its DOM, which validates the whole document; the other operations stream through the document with its On-Demand API, its fastest way, computing the checksum without building a tree.

### clang 19.1 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **16.8** | 33.6 | 197 | 330 | 286 | 2.01× faster |
| monsters | **40.2** | 67.8 | 321 | 340 | 578 | 1.69× faster |
| records | **24.1** | 223 | 578 | 229 | 267 | 9.21× faster |
| series | **69.5** | 211 | 829 | 365 | 1,436 | 3.04× faster |
| dense-unique | **11.7** | 22.2 | 92.1 | 80.5 | 132 | 1.90× faster |
| sparse-unique | **6.33** | 17.5 | 68.3 | 36.1 | 81.8 | 2.76× faster |
| dense-shared | **11.7** | 22.3 | 78.1 | 83.9 | 125 | 1.91× faster |
| lookup | **20.7** | 85.2 | 260 | 297 | 302 | 4.11× faster |
| canada | **0.96** | 4.65 | 1,407 | 784 | 2,773 | 4.83× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **18.5** | 19.9 | 21.5 | 19.3 |  | 1.08× faster |
| monsters | **35.5** | 42.5 | 36.6 | 36.1 |  | 1.20× faster |
| records | **112** | 128 | 117 | 78.3 |  | 1.14× faster |
| series | **96.1** | 102 | 103 | 96.6 |  | 1.06× faster |
| dense-unique | 10.9 | **10.9** | 11.0 | 10.9 |  | 1.00× slower |
| sparse-unique | **8.38** | 8.39 | 8.58 | 8.34 |  | 1.00× faster |
| dense-shared | **10.9** | 11.0 | 10.9 | 10.9 |  | 1.01× faster |
| lookup | **28.8** | 30.0 | 28.9 | 28.8 |  | 1.04× faster |
| canada | **107** | 107 | 117 | 112 |  | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **35.8** | 57.1 | 223 | 352 | 433 | 1.59× faster |
| monsters | **79.3** | 121 | 363 | 380 | 888 | 1.52× faster |
| records | **143** | 360 | 709 | 316 | 852 | 2.52× faster |
| series | **166** | 315 | 960 | 462 | 2,680 | 1.90× faster |
| dense-unique | **22.5** | 33.1 | 102 | 91.2 | 264 | 1.47× faster |
| sparse-unique | **14.8** | 26.2 | 77.4 | 44.9 | 166 | 1.77× faster |
| dense-shared | **22.5** | 33.3 | 89.6 | 95.5 | 260 | 1.48× faster |
| lookup | **49.6** | 115 | 289 | 325 | 375 | 2.32× faster |
| canada | **108** | 113 | 1,571 | 874 | 3,023 | 1.05× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **4.71** | 4.95 | 5.01 | 7.41 |  | 1.05× faster |
| monsters | **4.96** | 5.23 | 6.43 | 8.11 |  | 1.05× faster |
| records | **3.04** | 3.55 | 5.93 | 3.57 |  | 1.17× faster |
| series | **3.41** | 4.01 | 3.23 | 4.70 |  | 1.18× faster |
| dense-unique | **3.35** | 4.22 | 3.16 | 3.78 |  | 1.26× faster |
| sparse-unique | **2.42** | 4.76 | 2.70 | 2.71 |  | 1.97× faster |
| dense-shared | **3.39** | 4.41 | 3.12 | 4.06 |  | 1.30× faster |
| lookup | **1.97** | 2.25 | 1.66 | 4.38 |  | 1.14× faster |
| canada | **1.64** | 2.54 | 1.89 | 6.26 |  | 1.55× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0063** | 0.0075 | 199 | 325 | 112 | 1.19× faster |
| monsters | **0.0065** | 0.0077 | 321 | 341 | 166 | 1.20× faster |
| records | **0.0039** | 0.0049 | 578 | 229 | 75.9 | 1.25× faster |
| series | **0.0038** | 0.0056 | 837 | 368 | 499 | 1.46× faster |
| dense-unique | **0.0051** | 0.0061 | 91.2 | 80.6 | 46.7 | 1.19× faster |
| sparse-unique | **0.0051** | 0.0061 | 68.3 | 35.7 | 25.2 | 1.19× faster |
| dense-shared | **0.0047** | 0.0061 | 78.1 | 83.7 | 45.9 | 1.30× faster |
| lookup | **0.0042** | 0.0046 | 262 | 295 | 97.6 | 1.11× faster |
| canada | **0.0068** | 0.0080 | 1,399 | 759 | 599 | 1.17× faster |

### gcc 15.2 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **9.36** | 40.7 | 135 | 259 | 263 | 4.35× faster |
| monsters | **33.1** | 81.7 | 232 | 264 | 564 | 2.47× faster |
| records | **13.0** | 245 | 369 | 210 | 273 | 18.85× faster |
| series | **41.5** | 151 | 569 | 290 | 1,374 | 3.65× faster |
| dense-unique | **7.66** | 26.7 | 68.3 | 53.5 | 121 | 3.48× faster |
| sparse-unique | **4.45** | 17.5 | 46.9 | 27.7 | 76.7 | 3.94× faster |
| dense-shared | **7.63** | 26.7 | 58.9 | 54.2 | 115 | 3.50× faster |
| lookup | **20.5** | 50.0 | 182 | 242 | 290 | 2.44× faster |
| canada | **0.86** | 2.46 | 987 | 550 | 2,688 | 2.86× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | 20.0 | **19.7** | 18.9 | 18.2 |  | 1.01× slower |
| monsters | **36.1** | 40.0 | 36.5 | 36.1 |  | 1.11× faster |
| records | **108** | 130 | 114 | 84.9 |  | 1.20× faster |
| series | **92.8** | 93.5 | 97.8 | 97.9 |  | 1.01× faster |
| dense-unique | 11.0 | **11.0** | 11.0 | 10.9 |  | 1.00× slower |
| sparse-unique | **8.43** | 8.48 | 8.52 | 8.40 |  | 1.01× faster |
| dense-shared | **10.9** | 10.9 | 11.0 | 10.9 |  | 1.00× faster |
| lookup | **29.2** | 29.4 | 29.4 | 29.1 |  | 1.01× faster |
| canada | 108 | **108** | 113 | 109 |  | 1.00× slower |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **29.9** | 63.3 | 157 | 284 | 411 | 2.12× faster |
| monsters | **73.8** | 126 | 274 | 306 | 842 | 1.70× faster |
| records | **131** | 367 | 492 | 302 | 707 | 2.80× faster |
| series | **134** | 250 | 679 | 389 | 1,881 | 1.86× faster |
| dense-unique | **18.5** | 37.6 | 79.6 | 63.9 | 182 | 2.04× faster |
| sparse-unique | **13.0** | 26.5 | 55.8 | 36.0 | 120 | 2.04× faster |
| dense-shared | **18.5** | 37.8 | 69.9 | 65.1 | 172 | 2.04× faster |
| lookup | **49.3** | 79.8 | 213 | 271 | 345 | 1.62× faster |
| canada | **110** | 112 | 1,115 | 658 | 2,765 | 1.02× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **3.95** | 4.13 | 3.67 | 4.92 |  | 1.05× faster |
| monsters | **3.86** | 4.96 | 5.04 | 5.87 |  | 1.28× faster |
| records | **3.70** | 4.04 | 5.22 | 3.30 |  | 1.09× faster |
| series | **3.16** | 3.74 | 2.22 | 2.86 |  | 1.19× faster |
| dense-unique | **3.73** | 3.81 | 3.17 | 3.96 |  | 1.02× faster |
| sparse-unique | **2.11** | 2.52 | 2.72 | 3.33 |  | 1.19× faster |
| dense-shared | **3.57** | 3.61 | 3.21 | 3.54 |  | 1.01× faster |
| lookup | **2.37** | 2.76 | 1.84 | 4.42 |  | 1.16× faster |
| canada | **3.66** | 7.74 | 2.44 | 4.99 |  | 2.12× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0049** | 0.0060 | 136 | 261 | 130 | 1.23× faster |
| monsters | **0.0051** | 0.0068 | 232 | 267 | 193 | 1.33× faster |
| records | **0.0039** | 0.0050 | 371 | 209 | 87.5 | 1.26× faster |
| series | **0.0042** | 0.0046 | 576 | 292 | 583 | 1.10× faster |
| dense-unique | **0.0049** | 0.0067 | 68.7 | 53.4 | 54.6 | 1.37× faster |
| sparse-unique | **0.0049** | 0.0067 | 47.4 | 27.8 | 31.3 | 1.37× faster |
| dense-shared | **0.0045** | 0.0067 | 58.7 | 54.2 | 53.2 | 1.49× faster |
| lookup | **0.0035** | 0.0048 | 182 | 242 | 102 | 1.36× faster |
| canada | **0.0070** | 0.0103 | 987 | 550 | 657 | 1.46× faster |

### msvc 19.42.34444 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **15.5** | 38.5 | 229 | 328 | 304 | 2.48× faster |
| monsters | **36.3** | 69.0 | 368 | 357 | 618 | 1.90× faster |
| records | **20.9** | 259 | 618 | 252 | 301 | 12.41× faster |
| series | **68.8** | 196 | 962 | 535 | 1,502 | 2.85× faster |
| dense-unique | **10.9** | 18.2 | 119 | 87.5 | 144 | 1.67× faster |
| sparse-unique | **6.53** | 17.1 | 80.6 | 44.2 | 88.7 | 2.62× faster |
| dense-shared | **11.0** | 18.2 | 88.2 | 89.0 | 137 | 1.66× faster |
| lookup | **24.6** | 56.3 | 299 | 364 | 317 | 2.29× faster |
| canada | **1.00** | 2.99 | 1,692 | 1,086 | 2,870 | 2.98× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **18.6** | 19.8 | 19.4 | 18.5 |  | 1.07× faster |
| monsters | **39.2** | 44.3 | 37.1 | 36.7 |  | 1.13× faster |
| records | **140** | 285 | 117 | 106 |  | 2.03× faster |
| series | **98.0** | 98.7 | 96.4 | 95.6 |  | 1.01× faster |
| dense-unique | **10.8** | 11.3 | 10.8 | 10.8 |  | 1.04× faster |
| sparse-unique | **8.43** | 8.90 | 8.43 | 8.30 |  | 1.06× faster |
| dense-shared | **10.9** | 11.3 | 10.8 | 10.8 |  | 1.04× faster |
| lookup | **28.7** | 28.9 | 28.8 | 28.7 |  | 1.01× faster |
| canada | **106** | 106 | 108 | 109 |  | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **35.1** | 61.8 | 249 | 351 | 822 | 1.76× faster |
| monsters | **81.2** | 117 | 413 | 405 | 1,485 | 1.45× faster |
| records | **167** | 555 | 743 | 372 | 933 | 3.32× faster |
| series | **167** | 294 | 1,068 | 616 | 3,700 | 1.76× faster |
| dense-unique | **21.8** | 29.6 | 130 | 98.2 | 405 | 1.36× faster |
| sparse-unique | **14.9** | 26.0 | 89.5 | 52.4 | 247 | 1.74× faster |
| dense-shared | **21.8** | 29.7 | 101 | 100 | 409 | 1.36× faster |
| lookup | **53.3** | 85.8 | 328 | 395 | 731 | 1.61× faster |
| canada | **107** | 110 | 1,838 | 1,186 | 6,014 | 1.03× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **5.33** | 7.47 | 4.85 | 5.86 |  | 1.40× faster |
| monsters | **5.20** | 9.34 | 7.18 | 7.28 |  | 1.79× faster |
| records | **4.88** | 6.02 | 5.35 | 5.86 |  | 1.23× faster |
| series | **3.61** | 4.07 | 2.33 | 3.15 |  | 1.13× faster |
| dense-unique | **4.61** | 5.49 | 3.24 | 4.14 |  | 1.19× faster |
| sparse-unique | **3.57** | 4.87 | 4.27 | 3.65 |  | 1.37× faster |
| dense-shared | **4.98** | 5.66 | 3.14 | 4.37 |  | 1.14× faster |
| lookup | **2.14** | 3.44 | 1.76 | 4.47 |  | 1.61× faster |
| canada | **6.67** | 7.95 | 2.07 | 6.91 |  | 1.19× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0057** | 0.0082 | 228 | 332 | 308 | 1.45× faster |
| monsters | **0.0057** | 0.0089 | 367 | 357 | 191 | 1.55× faster |
| records | **0.0056** | 0.0079 | 617 | 252 | 90.2 | 1.41× faster |
| series | **0.0036** | 0.0047 | 956 | 530 | 587 | 1.30× faster |
| dense-unique | **0.0059** | 0.0078 | 119 | 86.9 | 56.0 | 1.32× faster |
| sparse-unique | **0.0059** | 0.0078 | 80.9 | 44.1 | 36.3 | 1.32× faster |
| dense-shared | **0.0055** | 0.0078 | 87.9 | 88.7 | 55.1 | 1.40× faster |
| lookup | **0.0047** | 0.0052 | 300 | 364 | 128 | 1.10× faster |
| canada | **0.0075** | 0.0107 | 1,696 | 1,135 | 1,057 | 1.43× faster |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, and neither have Protobuf's entries, so both are scanned: the benchmark's Protobuf schema has repeated key-value entries, because a map field would parse into a hash map, whose order is unspecified.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack | Protobuf |
|---|---:|---:|---:|---:|
| clang 19.1 | **138** | 219 | 10,618 | 10,335 |
| gcc 15.2 | **131** | 160 | 7,906 | 8,661 |
| msvc 19.42.34444 | **134** | 190 | 8,031 | 10,863 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack | Protobuf |
|---|---:|---:|---:|---:|
| clang 19.1 | **39.7** | 128 | 828 | 1,339 |
| gcc 15.2 | **49.0** | 78.4 | 1,114 | 1,377 |
| msvc 19.42.34444 | **49.1** | 113 | 1,200 | 1,306 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 304 | 268 | −11.9% |
| C++ verify, clang 19.1 (µs) | 69.5 | 65.5 | −5.8% |
| C++ full traversal, clang 19.1 (µs) | 96.1 | 96.0 | −0.1% |
| C++ verify + traversal, clang 19.1 (µs) | 166 | 161 | −2.7% |
| C++ random access, clang 19.1 (µs) | 3.41 | 2.74 | −19.5% |
| C++ verify, gcc 15.2 (µs) | 41.5 | 41.2 | −0.8% |
| C++ full traversal, gcc 15.2 (µs) | 92.8 | 92.7 | −0.1% |
| C++ verify + traversal, gcc 15.2 (µs) | 134 | 134 | −0.3% |
| C++ random access, gcc 15.2 (µs) | 3.16 | 2.11 | −33.3% |
| C++ verify, msvc 19.42.34444 (µs) | 68.8 | 68.2 | −0.9% |
| C++ full traversal, msvc 19.42.34444 (µs) | 98.0 | 95.7 | −2.3% |
| C++ verify + traversal, msvc 19.42.34444 (µs) | 167 | 164 | −1.8% |
| C++ random access, msvc 19.42.34444 (µs) | 3.61 | 2.38 | −34.1% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
