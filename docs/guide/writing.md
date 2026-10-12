# Writing in .NET

Everything goes through `TesseraSerializer`. The generated serializers register themselves with it, so there is no
setup.

## Three ways to write

```csharp
// 1. To a new array: the simplest.
byte[] buffer = TesseraSerializer.Serialize(orc);

// 2. Into an IBufferWriter<byte>: a pipe, a pooled buffer, an ArrayBufferWriter.
var output = new ArrayBufferWriter<byte>();
TesseraSerializer.Serialize(orc, output);

// 3. With your own writer: no allocations at all.
var writer = new TesseraWriter();
ReadOnlySpan<byte> span = TesseraSerializer.Write(writer, orc);
stream.Write(span);
```

| Method | Allocates | Notes |
|---|---|---|
| `Serialize(value, options)` | the result array | uses a writer kept per thread, so it is safe to call from several threads |
| `Serialize(value, output, options)` | nothing besides what `output` does | builds the buffer in the per-thread writer, then copies it into `output` |
| `Write(writer, value)` | nothing | the span points into `writer` and is valid until the writer is used again |

The serializers are generated per type: no reflection runs, and with a reused writer nothing is allocated. The one
exception is a dictionary of a type other than `Dictionary<TKey, TValue>` (a `SortedDictionary`, for example), whose
enumerator is allocated: about 230 bytes per dictionary.

A `TesseraWriter` grows as needed and keeps its memory for the next buffer. One writer must not be used by two threads
at once; give each thread its own.

```csharp
var writer = new TesseraWriter(new TesseraOptions { Sharing = Sharing.None }, initialCapacity: 1 << 20);
```

`writer.ToArray()` copies the last buffer into a new array; `writer.Reset()` clears it, keeping the memory.

## Options

Pass a `TesseraOptions` to `Serialize`, or to the `TesseraWriter` constructor. It is a record, so derive variants with
`with`:

```csharp
var compact = TesseraOptions.Default with { IncludeSchema = false };
byte[] buffer = TesseraSerializer.Serialize(orc, compact);
```

| Option | Default | What it does |
|---|---|---|
| `Sharing` | `Strings` | which equal values are stored once: see [Sharing](#sharing) |
| `IncludeSchema` | `true` | embeds the schema, so readers built from other versions of the models can read the buffer |
| `WriteDefaults` | `false` | also stores members equal to their default |
| `MaxDepth` | `128` | nesting limit, counting every object and vector; matches the C++ reader's `Options::max_depth` |

### Sharing

| Value | Stores once | Use it when |
|---|---|---|
| `None` | nothing; the buffer is a tree | write speed matters most and few strings repeat |
| `Strings` (default) | equal strings | almost always: nearly all of the size benefit, at little cost |
| `All` | equal strings, vectors, objects and [shared structs](../attributes/tessera-shared.md), compared by content | data repeats whole objects (prefab instances, scenes built from a few distinct nodes); the buffer becomes a DAG |

In the benchmark's scene of 1,024 nodes built from 16 distinct ones, `Sharing.All` writes 5,352 bytes instead of
52,984. It costs write time, because every object is hashed and compared. C++ reads a shared buffer exactly like
any other.

### IncludeSchema

The schema holds every member's hashed name and kind: 12 bytes per member and about 32 per type, whatever the data.
Readers built from a different version of the models use it to match members by name
([Schema evolution](schema-evolution.md)). Leave it out (`IncludeSchema = false`) when the writer and every reader are
always built from the same models, such as a game and its own save files, or when buffers are small and many. A
reader then accepts only buffers whose layout fingerprint matches its own.

### WriteDefaults

Members equal to their default are normally not stored, and readers return their own default. That is compact, but it
makes the default part of the wire contract: if you change a default, old buffers read as the new one. Set
`WriteDefaults = true` to store every non-nullable member, or mark the members whose defaults may change with
[`[TesseraKeepDefault]`](../attributes/tessera-keep-default.md).

### MaxDepth

The writer counts nesting as it goes: every object and every vector is a level, the root object is level 1. A deeper
graph throws a `TesseraException`, and so does a cycle, which would otherwise never end. The C++ reader has the same
limit (`Options::max_depth`, default 128); raise both together.

## Errors

| Exception | When |
|---|---|
| `ArgumentNullException` | the value to serialize is null |
| `TesseraException`: no writer was generated | the type is not a model: see [Which types are models](models.md#generic-code); the message names the fix |
| `TesseraException`: deeper than MaxDepth | the graph is deeper than `MaxDepth`, or has a cycle |
| `TesseraException`: exceeds the 2 GB limit | the buffer would be larger than a .NET array can be |
| `TesseraException`: too many types | one buffer would hold more than 65,534 distinct types |

Next: [Reading in C++](reading-cpp.md).
