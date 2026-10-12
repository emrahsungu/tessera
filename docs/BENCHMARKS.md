# Benchmark results

- CPU: Intel(R) Core(TM) i7-6700K CPU @ 4.00GHz (8 logical cores)
- OS: Microsoft Windows 10.0.19045; .NET: .NET 10.0.12
- C++ compilers: clang 19.1, gcc 15.2, msvc 19.42.34444
- .NET libraries: Tessera 0.1.0.0, FlatBuffers 25.12.19 (C# runtime built from the release source), MessagePack-CSharp 3.1.10, MemoryPack 1.21.4, Google.Protobuf 3.36.2, System.Text.Json 10.0.12, Newtonsoft.Json 13.0.4; measured with BenchmarkDotNet 0.15.8
- C++ libraries: FlatBuffers 25.12.19, msgpack-cxx 9.0.0, libprotobuf 36.2 (with Abseil 20250512.1), simdjson 5.0.3, each compiled from source with the benchmark's flags
- Recorded 2026-10-12 17:19:36Z. Medians of interleaved rounds; ±MAD in the raw JSON.

Every library writes the same C# objects. Every C++ reader computes a checksum over every field (presence included), which must match the value computed from the C# objects before anything is timed. The C++ readers' small helper functions are force-inlined with MSVC, for every library: MSVC stopped inlining them into one large function when it read Tessera (`benchmarks/native/readers.hpp` explains).

## Summary

How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the 9 workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.

| | vs FlatBuffers | vs MessagePack | vs Protobuf | vs JSON | vs MemoryPack |
|---|---:|---:|---:|---:|---:|
| Size | **1.27** (1.00–3.37) | 0.90 (0.59–2.01) | 0.92 (0.65–1.42) | **2.15** (1.30–3.04) | **1.28** (0.87–8.48) |
| .NET write | **9.41** (1.65–99.96) | **3.32** (1.30–10.79) | **6.32** (3.36–27.79) | **20.63** (7.57–206.41) | 0.99 (0.65–1.60) |
| .NET deserialize |  | **4.55** (1.88–13.91) | **4.20** (2.51–18.59) | **22.13** (7.48–173.14) | **1.14** (0.99–1.44) |
| .NET read every field in place, unchecked | **5.94** (4.70–10.20) |  |  |  |  |
| C++ verify (clang 19.1) | **2.99** (1.68–9.25) | **17.77** (6.46–1459.04) | **12.81** (4.80–706.79) | **24.89** (10.80–2868.29) |  |
| C++ full traversal (clang 19.1) | **1.05** (1.00–1.18) |  |  |  |  |
| C++ verify + traversal (clang 19.1) | **1.68** (1.05–2.51) | **5.38** (3.84–13.93) | **4.08** (2.13–9.08) | **11.26** (5.39–27.66) |  |
| C++ random access (clang 19.1) | **1.27** (1.04–2.00) |  |  |  |  |
| C++ verify (gcc 15.2) | **3.97** (2.44–18.39) | **18.77** (7.08–1154.17) | **15.95** (6.28–686.92) | **34.13** (14.31–3206.53) |  |
| C++ full traversal (gcc 15.2) | **1.04** (0.99–1.19) |  |  |  |  |
| C++ verify + traversal (gcc 15.2) | **1.85** (1.04–2.78) | **4.77** (3.75–10.84) | **4.11** (2.30–9.25) | **10.72** (5.44–25.68) |  |
| C++ random access (gcc 15.2) | **1.19** (1.03–2.02) |  |  |  |  |
| C++ verify (msvc 19.42.34444) | **2.72** (1.68–12.40) | **23.91** (8.60–1684.82) | **20.10** (7.74–1187.84) | **27.35** (12.70–2832.85) |  |
| C++ full traversal (msvc 19.42.34444) | **1.13** (1.00–2.03) |  |  |  |  |
| C++ verify + traversal (msvc 19.42.34444) | **1.62** (1.03–3.33) | **6.83** (4.59–17.62) | **5.95** (2.33–12.47) | **18.44** (6.05–56.20) |  |
| C++ random access (msvc 19.42.34444) | **1.33** (1.14–1.79) |  |  |  |  |

In .NET, "deserialize" checks the buffer and creates the objects (Tessera's `Deserialize`, Protobuf's parse into its own message classes, the others' deserializers into the same classes); "read every field in place" reads a buffer without creating objects, which FlatBuffers does without checking it (its C# verifier cannot check buffers over 32 KB), so Tessera's is unchecked too. The .NET read section below has both, checked and unchecked.

MessagePack, Protobuf and JSON are parsed before they are read: their "verify" is the parse (into a tree for MessagePack and Protobuf, into simdjson's DOM for JSON), and their traversal and random access read a tree parsed beforehand, so those two are left out of their columns. JSON is written by System.Text.Json and read by simdjson. MemoryPack has no C++ reader. How each parser is set up is in the C++ section below.

Where Tessera is slower or larger in these results (by more than 5% for the C++ reads), and why:

- **.NET write** (MemoryPack faster): monsters 1.11×, dense-unique 1.53×, dense-shared 1.21×, lookup 1.01×, canada 1.02×. MemoryPack writes the members one after another, with nothing that lets a reader find one without reading those before it, and copies arrays of plain structs as they lie in memory; it can only be read from the start, and only by .NET.
- **.NET deserialize** (MemoryPack faster): canada 1.01×. On canada, both create the same objects (920,376 bytes allocated) and copy the same values.
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
| prefab | 55.8 | **45.4** | 112 | 318 | 181 | 59.7 | 397 | 299 | 1,128 | 3,022 |
| monsters | 123 | **111** | 288 | 827 | 307 | 111 | 753 | 541 | 2,306 | 5,452 |
| records | **82.1** | 88.2 | 157 | 8,207 | 886 | 132 | 742 | 564 | 1,147 | 2,699 |
| series | 195 | **191** | 767 | 1,818 | 553 | 208 | 981 | 602 | 4,321 | 10,017 |
| dense-unique | 36.1 | 24.9 | 85.7 | 181 | 74.8 | **23.6** | 170 | 120 | 465 | 1,077 |
| sparse-unique | 16.5 | **13.7** | 41.5 | 117 | 58.4 | 17.1 | 57.3 | 42.7 | 333 | 692 |
| dense-shared | 28.7 | 25.5 | 87.3 | 179 | 76.2 | **23.7** | 153 | 103 | 405 | 993 |
| lookup | 180 | **165** | 301 | 7,928 | 234 | 177 | 604 | 406 | 1,362 | 2,849 |
| canada | 83.0 | 82.7 | 344 | 137 | 642 | **81.2** | 2,306 | 1,214 | 17,132 | 27,353 |

Without the final copy (into a reused writer, builder or buffer), µs:

| Workload | Tessera | FlatBuffers | MessagePack | MemoryPack | Protobuf | System.Text.Json | Newtonsoft.Json |
|---|---:|---:|---:|---:|---:|---:|---:|
| prefab | **49.2** | 310 | 169 | 53.0 | 330 | 1,088 | 3,221 |
| monsters | 111 | 840 | 299 | **106** | 612 | 2,350 | 5,362 |
| records | **92.7** | 8,011 | 872 | 94.9 | 577 | 1,145 | 2,654 |
| series | **169** | 1,759 | 511 | 180 | 831 | 4,215 | 10,010 |
| dense-unique | 33.0 | 177 | 73.7 | **20.3** | 210 | 474 | 1,057 |
| sparse-unique | **15.2** | 119 | 54.9 | 15.3 | 51.1 | 332 | 696 |
| dense-shared | 25.1 | 177 | 69.3 | **20.3** | 133 | 412 | 958 |
| lookup | 164 | 6,575 | 218 | **155** | 509 | 1,347 | 2,794 |
| canada | 34.3 | 68.6 | 1,013 | **31.4** | 2,028 | 16,692 | 27,224 |

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

## .NET read (µs per buffer, lower is better)

Deserializing: a buffer (`byte[]`) to the objects it holds. Tessera checks the buffer as it creates the objects (`TesseraSerializer.Deserialize<T>`); "Tessera (unchecked)" skips the check (`TesseraReadOptions.Trusted`), as for data a program wrote itself. MemoryPack, MessagePack and both JSON libraries create the same classes; Protobuf parses into its own generated message classes, which a program then uses as they are. Every result is checked against the checksum of the objects it was written from before anything is timed. The fastest of the checked reads is in bold.

Measured with BenchmarkDotNet, as the .NET write: every case in a process of its own. The lookup cases were measured again at 2026-10-12 22:55:16Z: in the first run, other work on the machine disturbed Tessera’s deserialize case (20% MAD).

| Workload | Tessera | Tessera (unchecked) | MemoryPack | MessagePack | System.Text.Json | Newtonsoft.Json | Protobuf (parse) |
|---|---:|---:|---:|---:|---:|---:|---:|
| prefab | **58.6** | 54.3 | 66.1 | 295 | 1,800 | 3,788 | 329 |
| monsters | **155** | 139 | 190 | 584 | 3,052 | 5,861 | 611 |
| records | **184** | 180 | 235 | 1,285 | 1,823 | 2,586 | 578 |
| series | **321** | 290 | 337 | 1,011 | 6,783 | 14,491 | 924 |
| dense-unique | **41.3** | 35.6 | 41.9 | 145 | 710 | 1,608 | 145 |
| sparse-unique | **22.7** | 19.3 | 24.3 | 106 | 441 | 932 | 56.8 |
| dense-shared | **29.8** | 25.0 | 42.9 | 139 | 691 | 1,581 | 143 |
| lookup | **253** | 245 | 284 | 475 | 1,895 | 3,636 | 658 |
| canada | 94.2 | 94.6 | **93.5** | 1,311 | 16,308 | 31,522 | 1,751 |

Bytes allocated per read:

| Workload | Tessera | Tessera (unchecked) | MemoryPack | MessagePack | System.Text.Json | Newtonsoft.Json | Protobuf (parse) |
|---|---:|---:|---:|---:|---:|---:|---:|
| prefab | 165,840 | 165,840 | 190,216 | 190,216 | 332,096 | 1,521,120 | 397,968 |
| monsters | 394,176 | 394,176 | 454,304 | 454,640 | 684,776 | 2,516,484 | 809,488 |
| records | 671,040 | 671,040 | 722,376 | 722,376 | 739,800 | 1,566,984 | 499,448 |
| series | 1,140,504 | 1,140,504 | 1,132,288 | 1,132,288 | 1,497,328 | 5,094,328 | 1,656,976 |
| dense-unique | 163,968 | 163,968 | 155,752 | 155,752 | 246,552 | 872,856 | 254,368 |
| sparse-unique | 97,408 | 97,408 | 89,192 | 89,192 | 115,480 | 624,640 | 116,128 |
| dense-shared | 124,288 | 124,288 | 155,752 | 155,752 | 246,552 | 797,184 | 254,368 |
| lookup | 695,496 | 695,496 | 687,280 | 687,280 | 1,492,632 | 2,643,248 | 1,062,816 |
| canada | 920,376 | 920,376 | 920,376 | 920,376 | 4,446,304 | 13,821,224 | 3,581,880 |

Reading in place: every field of a buffer, without creating objects, into the same checksum as the C++ readers (strings by their UTF-8 bytes, not decoded). Tessera reads through its generated read types (`MonsterView.Read`), with its check and without; FlatBuffers through its generated accessors, without its verifier: FlatBuffers' C# verifier (25.12.19) cannot check buffers over 32 KB, which it rejects or misreads as missing fields.

| Workload | Tessera: check + every field | Tessera: every field | FlatBuffers: every field |
|---|---:|---:|---:|
| prefab | 43.9 | **33.3** | 217 |
| monsters | 87.5 | **57.4** | 388 |
| records | 206 | **186** | 911 |
| series | 233 | **176** | 830 |
| dense-unique | 28.7 | **20.2** | 102 |
| sparse-unique | 19.8 | **15.7** | 78.4 |
| dense-shared | 28.6 | **20.2** | 102 |
| lookup | 70.6 | **48.1** | 337 |
| canada | 149 | **147** | 1,499 |

## C++ read

Tessera and FlatBuffers read the buffer where it lies. The parsers are each set up their fastest way, measured, and reuse their memory from one parse to the next:

- msgpack-cxx parses into a reused zone, with strings and binary data referenced in the buffer (its zero-copy option) rather than copied. Its tree for the traversal, random access and lookups is parsed once with the strings copied: that reads as fast and scans keys twice as fast.
- libprotobuf parses into a reused message object, as Google advises (a message keeps the memory it allocated when it is cleared). Its parsed tree for the traversal and random access is on an arena, which reads a few percent faster. A new arena per parse, the other fast way, is faster on prefab (clang 19.1 195 µs instead of 297; gcc 15.2 191 µs instead of 267; msvc 19.42.34444 294 µs instead of 383): there every element replaces a oneof's member, which a reused message allocates anew.
- simdjson reuses its parser. "Verify" parses into its DOM, which validates the whole document; the other operations stream through the document with its On-Demand API, its fastest way, computing the checksum without building a tree.

### clang 19.1 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **16.5** | 32.8 | 194 | 297 | 281 | 1.99× faster |
| monsters | **39.5** | 66.4 | 310 | 289 | 574 | 1.68× faster |
| records | **23.8** | 220 | 566 | 215 | 264 | 9.25× faster |
| series | **69.0** | 210 | 722 | 331 | 1,389 | 3.04× faster |
| dense-unique | **11.5** | 22.0 | 85.1 | 66.3 | 130 | 1.91× faster |
| sparse-unique | **6.27** | 17.4 | 64.1 | 31.5 | 80.7 | 2.77× faster |
| dense-shared | **11.5** | 21.9 | 74.1 | 69.0 | 124 | 1.91× faster |
| lookup | **20.4** | 83.8 | 219 | 269 | 299 | 4.10× faster |
| canada | **0.95** | 4.58 | 1,382 | 670 | 2,717 | 4.83× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **18.2** | 19.4 | 21.1 | 19.1 |  | 1.06× faster |
| monsters | **35.2** | 41.6 | 36.2 | 35.7 |  | 1.18× faster |
| records | **110** | 126 | 113 | 77.6 |  | 1.14× faster |
| series | **95.7** | 102 | 96.7 | 95.6 |  | 1.06× faster |
| dense-unique | **10.8** | 10.8 | 10.8 | 10.8 |  | 1.00× faster |
| sparse-unique | **8.31** | 8.31 | 8.44 | 8.30 |  | 1.00× faster |
| dense-shared | **10.8** | 10.8 | 10.9 | 10.8 |  | 1.00× faster |
| lookup | **28.6** | 29.4 | 28.8 | 28.7 |  | 1.03× faster |
| canada | **106** | 106 | 108 | 107 |  | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **35.3** | 55.8 | 216 | 320 | 417 | 1.58× faster |
| monsters | **78.1** | 118 | 349 | 333 | 844 | 1.52× faster |
| records | **142** | 355 | 684 | 301 | 764 | 2.51× faster |
| series | **165** | 312 | 820 | 427 | 2,610 | 1.89× faster |
| dense-unique | **22.3** | 32.7 | 96.0 | 77.0 | 247 | 1.47× faster |
| sparse-unique | **14.6** | 25.7 | 72.4 | 39.7 | 156 | 1.76× faster |
| dense-shared | **22.2** | 32.7 | 85.3 | 79.5 | 241 | 1.47× faster |
| lookup | **49.1** | 113 | 247 | 299 | 367 | 2.31× faster |
| canada | **107** | 113 | 1,497 | 782 | 2,973 | 1.05× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **4.63** | 4.86 | 4.94 | 6.10 |  | 1.05× faster |
| monsters | **4.85** | 5.07 | 6.36 | 7.85 |  | 1.04× faster |
| records | **2.92** | 3.44 | 5.87 | 3.51 |  | 1.18× faster |
| series | **3.38** | 3.91 | 3.17 | 3.42 |  | 1.15× faster |
| dense-unique | **3.30** | 4.12 | 3.09 | 3.75 |  | 1.25× faster |
| sparse-unique | **2.36** | 4.72 | 2.57 | 2.71 |  | 2.00× faster |
| dense-shared | **3.32** | 4.30 | 3.18 | 3.79 |  | 1.30× faster |
| lookup | **1.90** | 2.19 | 1.63 | 4.26 |  | 1.15× faster |
| canada | **1.61** | 2.48 | 1.89 | 6.11 |  | 1.54× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0062** | 0.0091 | 194 | 297 | 98.9 | 1.46× faster |
| monsters | **0.0064** | 0.0076 | 309 | 287 | 147 | 1.19× faster |
| records | **0.0039** | 0.0048 | 566 | 215 | 71.1 | 1.25× faster |
| series | **0.0038** | 0.0055 | 723 | 331 | 488 | 1.46× faster |
| dense-unique | **0.0051** | 0.0060 | 84.9 | 66.0 | 41.2 | 1.18× faster |
| sparse-unique | **0.0051** | 0.0060 | 63.6 | 31.4 | 23.3 | 1.19× faster |
| dense-shared | **0.0047** | 0.0060 | 74.5 | 68.6 | 40.2 | 1.30× faster |
| lookup | **0.0041** | 0.0045 | 219 | 269 | 81.2 | 1.10× faster |
| canada | **0.0067** | 0.0079 | 1,382 | 672 | 552 | 1.18× faster |

### gcc 15.2 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **9.70** | 41.7 | 137 | 267 | 270 | 4.30× faster |
| monsters | **33.8** | 83.2 | 240 | 272 | 581 | 2.46× faster |
| records | **13.7** | 251 | 376 | 215 | 277 | 18.39× faster |
| series | **42.8** | 156 | 592 | 305 | 1,413 | 3.64× faster |
| dense-unique | **7.76** | 27.1 | 70.2 | 55.3 | 125 | 3.50× faster |
| sparse-unique | **4.55** | 18.1 | 47.2 | 28.5 | 79.8 | 3.98× faster |
| dense-shared | **7.74** | 27.7 | 60.7 | 55.9 | 118 | 3.59× faster |
| lookup | **20.9** | 50.9 | 186 | 253 | 299 | 2.44× faster |
| canada | **0.89** | 2.52 | 1,022 | 609 | 2,841 | 2.84× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | 20.4 | **20.1** | 19.2 | 18.7 |  | 1.01× slower |
| monsters | **36.8** | 41.3 | 37.3 | 36.6 |  | 1.12× faster |
| records | **110** | 131 | 118 | 86.2 |  | 1.19× faster |
| series | **94.2** | 95.5 | 106 | 99.1 |  | 1.01× faster |
| dense-unique | **11.0** | 11.2 | 11.2 | 11.1 |  | 1.01× faster |
| sparse-unique | 8.62 | **8.53** | 8.66 | 8.54 |  | 1.01× slower |
| dense-shared | 11.1 | **11.1** | 11.1 | 11.1 |  | 1.00× slower |
| lookup | **29.3** | 29.9 | 29.7 | 29.5 |  | 1.02× faster |
| canada | 110 | **110** | 125 | 116 |  | 1.00× slower |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **31.0** | 64.1 | 163 | 287 | 420 | 2.07× faster |
| monsters | **75.8** | 129 | 288 | 312 | 857 | 1.70× faster |
| records | **134** | 373 | 504 | 309 | 731 | 2.78× faster |
| series | **138** | 253 | 709 | 402 | 1,934 | 1.84× faster |
| dense-unique | **18.9** | 38.6 | 81.2 | 67.5 | 189 | 2.04× faster |
| sparse-unique | **13.2** | 27.2 | 56.8 | 37.3 | 124 | 2.07× faster |
| dense-shared | **18.9** | 38.9 | 72.5 | 66.9 | 179 | 2.05× faster |
| lookup | **50.4** | 81.3 | 216 | 279 | 352 | 1.61× faster |
| canada | **111** | 115 | 1,203 | 732 | 2,850 | 1.04× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **4.03** | 4.25 | 3.91 | 5.38 |  | 1.06× faster |
| monsters | **4.02** | 4.85 | 5.20 | 5.99 |  | 1.21× faster |
| records | **3.61** | 3.72 | 5.25 | 3.49 |  | 1.03× faster |
| series | **3.27** | 3.83 | 2.28 | 2.93 |  | 1.17× faster |
| dense-unique | **3.84** | 4.30 | 3.40 | 3.78 |  | 1.12× faster |
| sparse-unique | **2.20** | 2.55 | 2.51 | 3.18 |  | 1.16× faster |
| dense-shared | **3.60** | 3.70 | 3.49 | 3.46 |  | 1.03× faster |
| lookup | **2.43** | 2.81 | 2.03 | 4.40 |  | 1.16× faster |
| canada | **3.96** | 7.98 | 2.30 | 5.24 |  | 2.02× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0050** | 0.0061 | 140 | 269 | 132 | 1.21× faster |
| monsters | **0.0052** | 0.0070 | 241 | 272 | 198 | 1.35× faster |
| records | **0.0040** | 0.0051 | 379 | 215 | 90.5 | 1.26× faster |
| series | **0.0034** | 0.0048 | 589 | 308 | 601 | 1.43× faster |
| dense-unique | **0.0050** | 0.0068 | 70.6 | 56.5 | 55.6 | 1.38× faster |
| sparse-unique | **0.0050** | 0.0069 | 47.7 | 28.4 | 31.8 | 1.38× faster |
| dense-shared | **0.0046** | 0.0068 | 60.8 | 56.5 | 54.1 | 1.48× faster |
| lookup | **0.0036** | 0.0049 | 186 | 251 | 104 | 1.35× faster |
| canada | **0.0071** | 0.0105 | 1,028 | 606 | 698 | 1.48× faster |

### msvc 19.42.34444 (µs, lower is better)

**Verify (MessagePack, Protobuf and JSON: the parse)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **15.8** | 39.0 | 240 | 383 | 307 | 2.47× faster |
| monsters | **36.8** | 70.1 | 392 | 425 | 627 | 1.91× faster |
| records | **21.2** | 263 | 639 | 271 | 302 | 12.40× faster |
| series | **69.8** | 195 | 1,117 | 643 | 1,525 | 2.80× faster |
| dense-unique | **11.0** | 18.5 | 129 | 108 | 146 | 1.68× faster |
| sparse-unique | **6.60** | 17.3 | 88.3 | 51.0 | 89.4 | 2.62× faster |
| dense-shared | **11.0** | 18.5 | 94.8 | 110 | 140 | 1.68× faster |
| lookup | **25.1** | 57.3 | 361 | 451 | 322 | 2.28× faster |
| canada | **1.03** | 3.02 | 1,735 | 1,223 | 2,917 | 2.94× faster |

**Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **18.9** | 20.3 | 19.5 | 18.9 |  | 1.07× faster |
| monsters | **39.5** | 45.2 | 37.7 | 37.1 |  | 1.14× faster |
| records | **141** | 287 | 121 | 107 |  | 2.03× faster |
| series | **98.7** | 99.6 | 101 | 96.6 |  | 1.01× faster |
| dense-unique | **11.0** | 11.4 | 10.9 | 10.9 |  | 1.04× faster |
| sparse-unique | **8.57** | 8.98 | 8.54 | 8.39 |  | 1.05× faster |
| dense-shared | **11.0** | 11.4 | 10.9 | 10.9 |  | 1.04× faster |
| lookup | **28.9** | 29.3 | 28.9 | 28.8 |  | 1.02× faster |
| canada | **107** | 107 | 119 | 112 |  | 1.00× faster |

**Verify + full traversal (safe end-to-end read)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **35.7** | 63.1 | 261 | 409 | 830 | 1.77× faster |
| monsters | **82.4** | 120 | 437 | 475 | 1,507 | 1.46× faster |
| records | **168** | 560 | 772 | 392 | 1,018 | 3.33× faster |
| series | **169** | 295 | 1,252 | 752 | 3,746 | 1.75× faster |
| dense-unique | **22.0** | 30.0 | 139 | 121 | 409 | 1.36× faster |
| sparse-unique | **15.2** | 26.2 | 96.6 | 60.4 | 248 | 1.72× faster |
| dense-shared | **22.0** | 29.9 | 106 | 120 | 412 | 1.36× faster |
| lookup | **54.1** | 86.6 | 392 | 494 | 738 | 1.60× faster |
| canada | **108** | 111 | 1,906 | 1,348 | 6,079 | 1.03× faster |

**1000 random element reads (MessagePack and Protobuf: on the parsed tree)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **5.45** | 7.62 | 4.91 | 6.01 |  | 1.40× faster |
| monsters | **5.29** | 9.46 | 7.34 | 7.43 |  | 1.79× faster |
| records | **5.01** | 6.28 | 5.46 | 5.88 |  | 1.25× faster |
| series | **3.68** | 4.21 | 2.43 | 3.10 |  | 1.15× faster |
| dense-unique | **4.71** | 5.57 | 3.13 | 4.32 |  | 1.18× faster |
| sparse-unique | **3.62** | 4.93 | 4.23 | 3.76 |  | 1.36× faster |
| dense-shared | **5.04** | 5.75 | 3.16 | 4.43 |  | 1.14× faster |
| lookup | **2.06** | 3.31 | 1.82 | 4.56 |  | 1.61× faster |
| canada | **6.69** | 8.07 | 2.13 | 7.02 |  | 1.21× faster |

**Open + read one field (the parsers parse first)**

| Workload | Tessera | FlatBuffers | MessagePack | Protobuf | JSON | Tessera vs FlatBuffers |
|---|---:|---:|---:|---:|---:|---:|
| prefab | **0.0057** | 0.0082 | 240 | 388 | 307 | 1.43× faster |
| monsters | **0.0058** | 0.0090 | 392 | 427 | 193 | 1.55× faster |
| records | **0.0057** | 0.0080 | 640 | 271 | 90.3 | 1.40× faster |
| series | **0.0037** | 0.0048 | 1,117 | 649 | 598 | 1.31× faster |
| dense-unique | **0.0059** | 0.0079 | 130 | 107 | 56.8 | 1.32× faster |
| sparse-unique | **0.0060** | 0.0079 | 88.3 | 51.0 | 36.3 | 1.32× faster |
| dense-shared | **0.0065** | 0.0079 | 94.8 | 110 | 56.4 | 1.21× faster |
| lookup | **0.0047** | 0.0052 | 357 | 459 | 129 | 1.11× faster |
| canada | **0.0076** | 0.0109 | 1,731 | 1,220 | 1,069 | 1.43× faster |

## Dictionary lookups (µs per 1,000 lookups, lower is better)

The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, and neither have Protobuf's entries, so both are scanned: the benchmark's Protobuf schema has repeated key-value entries, because a map field would parse into a hash map, whose order is unspecified.

**By string**

| Compiler | Tessera | FlatBuffers | MessagePack | Protobuf |
|---|---:|---:|---:|---:|
| clang 19.1 | **117** | 190 | 8,003 | 7,773 |
| gcc 15.2 | **134** | 163 | 8,152 | 8,826 |
| msvc 19.42.34444 | **152** | 203 | 10,864 | 14,205 |

**By int**

| Compiler | Tessera | FlatBuffers | MessagePack | Protobuf |
|---|---:|---:|---:|---:|
| clang 19.1 | **39.1** | 109 | 828 | 1,298 |
| gcc 15.2 | **49.7** | 80.4 | 1,140 | 1,406 |
| msvc 19.42.34444 | **49.6** | 132 | 1,248 | 1,344 |

## Fixed cells: the series workload with `[TesseraKeepDefault]`

The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.

|  | Tessera | Tessera (fixed) | Change |
|---|---:|---:|---:|
| Size (bytes) | 581,288 | 581,296 | +0.0% |
| .NET write (µs) | 195 | 174 | −10.7% |
| C++ verify, clang 19.1 (µs) | 69.0 | 64.3 | −6.8% |
| C++ full traversal, clang 19.1 (µs) | 95.7 | 95.6 | −0.1% |
| C++ verify + traversal, clang 19.1 (µs) | 165 | 160 | −2.8% |
| C++ random access, clang 19.1 (µs) | 3.38 | 2.70 | −20.2% |
| C++ verify, gcc 15.2 (µs) | 42.8 | 42.7 | −0.4% |
| C++ full traversal, gcc 15.2 (µs) | 94.2 | 93.5 | −0.7% |
| C++ verify + traversal, gcc 15.2 (µs) | 138 | 136 | −1.2% |
| C++ random access, gcc 15.2 (µs) | 3.27 | 2.27 | −30.6% |
| C++ verify, msvc 19.42.34444 (µs) | 69.8 | 69.0 | −1.1% |
| C++ full traversal, msvc 19.42.34444 (µs) | 98.7 | 96.1 | −2.7% |
| C++ verify + traversal, msvc 19.42.34444 (µs) | 169 | 165 | −1.9% |
| C++ random access, msvc 19.42.34444 (µs) | 3.68 | 2.44 | −33.7% |

Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).
