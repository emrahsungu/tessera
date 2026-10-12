# `[Tessera]`

```csharp
[Tessera]
public class Monster { ... }

[Tessera("Sword")]          // a fixed type name: the source of the union tag
public sealed class Blade : Weapon { ... }
```

On a class, record or struct. Optional: makes the type a model even when nothing in its project serializes it or uses
it. On a type that is already a model in its project, it changes nothing there: no byte of any buffer and no line of
the generated C# or C++.

## Use it when

- **Only C++ reads the type.** Nothing in C# writes it yet, but C++ needs the header: mark it, and the header is
  generated.
- **Another project derives from your abstract types or interfaces.** A union member's possible types include
  classes of other projects only when their project marks them ([Unions](../guide/types.md#unions)).
- **Another project uses your types and relies on their initializers** (`public short Hp = 100;`). Marked types have
  their initializers recorded in the assembly, so other projects read the same defaults
  ([Models in several projects](../guide/multiple-projects.md)).
- **You need a stable union tag.** A union member's tag is a hash of its type name, without the namespace. Renaming
  the class changes the tag, so old buffers would read it as an unknown type. `[Tessera("OldName")]` keeps the old
  tag. It also resolves two union members with the same name in different namespaces
  ([TESSERA004](../diagnostics/TESSERA004.md)) and a name whose hash is 0 ([TESSERA009](../diagnostics/TESSERA009.md)).
- **Your project uses strict mode** (`<TesseraRequireAttribute>true</TesseraRequireAttribute>`), which requires every
  model to be marked ([TESSERA012](../diagnostics/TESSERA012.md)).

## Details

- The name only affects union tags. Member names and the C++ type name do not depend on it.
- It is not inherited: each derived class is a model in its own right, and is marked separately if needed.
- For types you cannot or would rather not edit, use [`[assembly: TesseraRoot]`](tessera-root.md), which has the same
  effect.

See also: [Which types are models](../guide/models.md).
