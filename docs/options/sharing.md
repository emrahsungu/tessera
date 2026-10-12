# `Sharing`

**What it does:** decides which repeated values are stored only once. Every other place that holds the same value
then stores a 4-byte reference to that one copy.

```csharp
byte[] data = TesseraSerializer.Serialize(scene, new TesseraOptions { Sharing = Sharing.All });
```

| Value | Stored once | |
|---|---|---|
| `Sharing.None` | nothing | fastest saving |
| `Sharing.Strings` | equal strings | **the default** |
| `Sharing.All` | equal strings, lists, objects, and structs marked [`[TesseraShared]`](../attributes/tessera-shared.md) | smallest buffers |

Sharing changes only the buffer's size and the time it takes to save. C++ reads every buffer the same way, with the
same code, and needs no setting.

## How it works

Without sharing, the buffer is a tree: each object, list and string is stored where it is used. With sharing, the
writer keeps a hash of everything it has stored, and when the same contents come again, it stores a reference to the
earlier copy instead of writing them again.

Values are compared **by content**, not by reference: two different `string` instances with the same text are stored
once, and so are two different objects (with `Sharing.All`) whose fields are all equal.

## The numbers

Buffer sizes from the [benchmarks](../BENCHMARKS.md), in bytes:

| Data | `None` | `Strings` (default) | `All` |
|---|---:|---:|---:|
| UI prefab: 400 objects, many repeated names and sprite paths | 89,800 | 75,800 | 74,232 |
| 1,000 game monsters, repeated weapon names | 179,768 | 155,096 | 147,896 |
| 2,000 records with optional string fields | 96,000 | 70,928 | 70,912 |
| scene of 1,024 nodes, copies of 16 distinct nodes | 65,080 | 52,984 | 5,352 |
| 20,000 time-series samples, no repeated strings | 581,288 | 581,288 | 581,288 |

Saving time, against the default: `None` was up to 31% faster, most where every string is unique (it was 7% slower
on the records, whose buffer sharing makes 26% smaller), and `All` took 1.7 to 4.1 times as long. The [benchmark results](../BENCHMARKS.md) have the current times for every case
(the "Tessera (no sharing)" and "Tessera (full sharing)" columns).

## `Sharing.None`

Each string, list and object is written where it occurs; the writer keeps no hash table.

**Use it when:**

- **saving speed matters most**, for example game state sent over the network every frame, or a log written
  continuously;
- **strings rarely repeat**: unique names, free text, numeric data. Then the default only spends time hashing.

**Costs:** every repeated string is stored again: 16–26% larger buffers in the data above.

## `Sharing.Strings` (default)

Equal strings are stored once.

**Use it:** almost always. Real data repeats strings all the time: names, tags, asset paths, enum-like strings,
dictionary keys. Nearly all the size gain of sharing comes from strings, at a small cost in saving time.

## `Sharing.All`

Equal strings, lists, objects and `[TesseraShared]` structs are stored once.

**Use it when:**

- **whole objects or lists repeat**: prefab instances that copy a template, scene nodes cloned from a few distinct
  ones, the same list of tags on thousands of items, large repeated structs such as materials;
- **size matters more than saving time**: content shipped with a game, data downloaded by many users, data saved once
  and read many times.

**Costs:**

- **Saving takes longer**, because every object and list is hashed and compared with what was already written.
- **Validation in C++ adapts.** An object referenced from many places would be checked once per reference; when that
  adds up to more work than the buffer's size justifies, the validator switches to checking each shared item once,
  and allocates memory to remember which ones it checked. Plain reading is unaffected.

**What changes in the buffer:** it becomes a graph without cycles (a DAG) instead of a tree: one stored object can be
the value of many fields. C++ can't tell, and doesn't need to: all data is read-only.

## Details

- Cycles are never possible: a buffer is written from the end, and every reference points forward.
- Sharing doesn't reach across buffers: each buffer is self-contained.
- To store a large struct value once per buffer, mark it [`[TesseraShared]`](../attributes/tessera-shared.md) and save
  with `Sharing.All`; plain structs are otherwise always stored inline.
