# Writing in C#

Saving goes through `TesseraSerializer`. The generated serializers register themselves with it when your assembly
loads, so there is nothing to set up.

## Three ways to save

```csharp
// 1. To a new array: the simplest.
byte[] buffer = TesseraSerializer.Serialize(orc);

// 2. Into memory you manage: an ArrayBufferWriter, a PipeWriter, a pooled buffer.
var output = new ArrayBufferWriter<byte>();
TesseraSerializer.Serialize(orc, output);

// 3. With a writer you keep: no memory allocated at all.
var writer = new TesseraWriter();
ReadOnlySpan<byte> span = TesseraSerializer.Write(writer, orc);
stream.Write(span);
```

| Method | Allocates | Use it when |
|---|---|---|
| `Serialize(value)` | the returned array | you just need the bytes: files, most code. Safe to call from several threads |
| `Serialize(value, output)` | nothing, besides what `output` does | the bytes go into memory you manage, such as a network pipe |
| `Write(writer, value)` | nothing | you save often, for example every frame. The span is valid until the writer is used again |

No reflection runs: the serializers are ordinary generated code. The one allocation they make is the enumerator of a
dictionary that is not a `Dictionary<TKey, TValue>` (a `SortedDictionary`, for example): about 230 bytes per such
dictionary.

A `TesseraWriter` grows as needed and keeps its memory for the next buffer. Don't use one writer from two threads at
once; give each thread its own. Every member is described in the [C# API](../api/csharp.md).

## Options

```csharp
var options = new TesseraOptions { Sharing = Sharing.All };
byte[] buffer = TesseraSerializer.Serialize(scene, options);
```

| Option | Default | What it does |
|---|---|---|
| [`Sharing`](../options/sharing.md) | `Strings` | stores repeated values only once: nothing, strings, or strings, lists and objects |
| [`IncludeSchema`](../options/include-schema.md) | `true` | stores a description of your classes, so other versions of them can read the buffer |
| [`WriteDefaults`](../options/write-defaults.md) | `false` | writes fields even when they equal their default value |
| [`MaxDepth`](../options/max-depth.md) | `128` | how deeply objects and lists may nest; also catches reference cycles |

Each option has its own page, with every case to use it in.

## Errors

| Exception | Cause |
|---|---|
| `ArgumentNullException` | the object to save is null |
| `TesseraException`: no writer was generated | no serializer exists for the type: see [Which types get generated code](generated-types.md#generic-code); the message names the fix |
| `TesseraException`: deeper than MaxDepth | the data nests deeper than [`MaxDepth`](../options/max-depth.md), or contains a reference cycle |
| `TesseraException`: exceeds the 2 GB limit | the buffer would be larger than a .NET array can be |
| `TesseraException`: too many types | the data uses more than 65,534 different types |

Next: [Reading in C++](reading-cpp.md).
