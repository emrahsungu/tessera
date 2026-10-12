# C# API

Everything is in the `Tessera` namespace, in the `Tessera` package.

## `TesseraSerializer`: saving objects

### `Serialize<T>(T value, TesseraOptions? options = null)` → `byte[]`

Saves an object to a new byte array.

```csharp
byte[] data = TesseraSerializer.Serialize(monster);
File.WriteAllBytes("monster.bin", data);
```

**Use it** in most code: it is the simplest way, and fast. It reuses an internal writer per thread, so it allocates
only the returned array, and it is safe to call from several threads at once.

### `Serialize<T>(T value, IBufferWriter<byte> output, TesseraOptions? options = null)`

Saves an object into an `IBufferWriter<byte>`: an `ArrayBufferWriter<byte>`, a `PipeWriter`, or a pooled buffer of
your own.

```csharp
var output = new ArrayBufferWriter<byte>();
TesseraSerializer.Serialize(monster, output);
socket.Send(output.WrittenSpan);
```

**Use it** when the bytes go into memory you manage, for example a network pipe or a buffer pool. The buffer is built
in the per-thread writer, then copied into `output` once.

### `Write<T>(TesseraWriter writer, T value)` → `ReadOnlySpan<byte>`

Saves an object with a writer you own, and returns the bytes inside that writer. Nothing is allocated.

```csharp
var writer = new TesseraWriter();
foreach (var frame in frames)
{
    ReadOnlySpan<byte> bytes = TesseraSerializer.Write(writer, frame);
    stream.Write(bytes);              // use the bytes before the next Write
}
```

**Use it** in hot loops, or wherever allocations matter (a game's frame loop). The span is valid until the writer is
used again: copy or send it before the next `Write`.

All three throw `ArgumentNullException` for a null value, and `TesseraException` when no serializer was generated for
`T`, the data nests deeper than `MaxDepth` (or has a cycle), or the buffer would exceed 2 GB.

## `TesseraWriter`: a reusable writer

A writer holds the memory a buffer is built in. It grows as needed, and keeps its memory for the next buffer.

| Member | What it does |
|---|---|
| `new TesseraWriter()` | a writer with the default options |
| `new TesseraWriter(TesseraOptions options, int initialCapacity = 4096)` | a writer with these options, starting with `initialCapacity` bytes (set it near your typical buffer size to avoid growing) |
| `Options` | the writer's options; setting them clears the writer |
| `WrittenSpan` | the last buffer written, until the writer is used again |
| `ToArray()` | a copy of the last buffer, as a new array |
| `Reset()` | clears the writer, keeping its memory |

```csharp
var writer = new TesseraWriter(new TesseraOptions { Sharing = Sharing.None }, initialCapacity: 1 << 20);
```

A writer must not be used by two threads at once: give each thread its own. The other public members (`Reserve`,
`WriteString`, `BeginObject`, `Finish` and so on) are the building blocks of the generated serializers; don't call them
yourself, they may change between versions.

## `TesseraOptions` and `Sharing`

The write options: see [Options](../options/README.md). `TesseraOptions.Default` holds the defaults.

## `TesseraException`

Thrown when saving can't succeed:

| Message | Cause |
|---|---|
| No writer was generated for `T` ... | the source generator didn't run, or the type isn't one it generates code for: the message names the fix ([Which types get generated code](../guide/generated-types.md)) |
| Object graph is deeper than MaxDepth ... | the data nests deeper than [`MaxDepth`](../options/max-depth.md), or contains a cycle |
| Buffer exceeds the 2 GB limit. | the buffer would be larger than a .NET array can be |
| Too many types in one buffer. | the data uses more than 65,534 different types |

## `TesseraCppHeaders`: the C++ headers, from code

The source generator stores each C++ header inside your compiled assembly. Normally the build writes them to
`TesseraCppOutputDir` ([MSBuild settings](../guide/msbuild.md)); these two methods do the same from code.

| Member | What it does |
|---|---|
| `Read(Assembly assembly)` | returns `(FileName, Content)` for every header in the assembly |
| `Export(Assembly assembly, string directory)` | writes them under `directory`, skipping files whose content is unchanged; returns the paths it wrote |

**Use them** when your build doesn't run MSBuild targets, for example in a custom asset pipeline, a game engine's
build step, or a test that checks the headers:

```csharp
TesseraCppHeaders.Export(typeof(Monster).Assembly, "Engine/Generated");
```

## `TesseraModel<T>`: is there a serializer?

`TesseraModel<T>.Write` is the generated serializer for `T`, or null if none was generated. Check it at startup to
fail early, for example in code that serializes through generics:

```csharp
if (TesseraModel<SaveGame>.Write is null) throw new InvalidOperationException("SaveGame has no serializer");
```

`TesseraModel<T>.Register` is called by the generated code; don't call it yourself.

## `TesseraMapKeys`: dictionary key order

Dictionaries are saved with their keys sorted, so that C++ can binary-search them. String keys are sorted by the
bytes of their UTF-8 encoding, which differs from `string.CompareOrdinal` for characters outside the Basic
Multilingual Plane (emoji, for example).

`TesseraMapKeys.Utf8Order` is an `IComparer<string>` that sorts the same way. **Use it** to produce or check the order
C++ sees in `keys()`. `SortUtf8` and `Sort` are used by the generated code.

## Types for the generated code

`TesseraWriteFunc<T>`, `TesseraType`, and the attributes `TesseraDefault`, `TesseraDeclared` and `TesseraCppHeader`
([generated attributes](../attributes/generated.md)) are public because the generated code uses them. You don't need
them.
