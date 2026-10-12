# Options

## Saving in C#

Pass a `TesseraOptions` to `TesseraSerializer.Serialize`, or to a `TesseraWriter`:

```csharp
var options = new TesseraOptions { Sharing = Sharing.All, IncludeSchema = false };
byte[] data = TesseraSerializer.Serialize(scene, options);
```

| Option | Default | What it does |
|---|---|---|
| [`Sharing`](sharing.md) | `Sharing.Strings` | Which repeated values are stored only once: nothing, equal strings, or equal strings, lists and objects. More sharing gives smaller buffers and slower saving. |
| [`IncludeSchema`](include-schema.md) | `true` | Stores a description of your classes (field names and types) in the buffer, so a program built from an older or newer version of the classes can still read it. |
| [`WriteDefaults`](write-defaults.md) | `false` | Writes fields even when they equal their default value. |
| [`MaxDepth`](max-depth.md) | `128` | How deeply objects and lists may nest. Deeper data, or a cycle of references, throws an exception instead of saving. |

The C++ reader needs no matching setting: each buffer records how it was written.

`TesseraOptions` is an immutable record. `TesseraOptions.Default` holds the defaults; make variants with `with`:

```csharp
TesseraOptions fast = TesseraOptions.Default with { Sharing = Sharing.None };
```

## Reading in C++

[C++ read options](cpp-read-options.md): whether to validate a buffer before reading it, whether to check that strings
are valid UTF-8, limits on nesting and object count, and how to skip validation for data your own program wrote.

## Reading in .NET

[.NET read options](dotnet-read-options.md): the same choices for the generated read types (`MonsterView.Read`) and
`TesseraSerializer.Deserialize`, through a `TesseraReadOptions`.
