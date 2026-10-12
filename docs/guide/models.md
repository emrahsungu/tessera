# Which types are models

A model is a class, record or struct that Tessera writes and C++ reads. The source generator writes a serializer and
a C++ header for every model. You don't list them anywhere: a type is a model when its project does one of these.

| The project... | Example | Notes |
|---|---|---|
| serializes it | `TesseraSerializer.Serialize(monster)` or `TesseraSerializer.Write(writer, monster)` | the compiler must know the type of the value: see [Generic code](#generic-code) |
| uses it in a model | `public Weapon Equipped;`, `public List<Weapon> Weapons;`, `public Dictionary<string, Stock> Items;` | as a member, a collection's element type, or a dictionary's value type |
| derives it from a union member's type | `public sealed class Potion : Item` when a model has an `Item` member | every public or internal concrete class of the project, unless [`[TesseraUnion]`](../attributes/tessera-union.md) lists them |
| marks it | [`[Tessera]`](../attributes/tessera.md) on the type, or [`[assembly: TesseraRoot(typeof(Monster))]`](../attributes/tessera-root.md) | for types nothing in the project serializes, and types you cannot edit |

Enums and plain structs (only unmanaged fields) that models use are part of the schema too; they need nothing else.

## Is `[Tessera]` needed?

No. `[Tessera]` is optional, and on a type that is already a model in its project it changes nothing there: no byte
of any buffer and no line of the generated C# or C++ (the tests check this). It matters in four cases:

| | Without `[Tessera]` | With `[Tessera]` or `[assembly: TesseraRoot]` |
|---|---|---|
| Model | when its project serializes it or a model uses it | always: C++ gets its header even if no C# code writes it |
| Buffers and generated code | the same | the same |
| Union member of this project's abstract types | yes, when public or internal and not generic | yes |
| Union member of another project's abstract types | no | yes |
| Member initializers visible to other projects | when its own project treats it as a model | yes |
| Stable type name (the source of union tags) | the C# type name | the C# type name, or `[Tessera("Name")]` |
| Under strict mode (`TesseraRequireAttribute`) | error [TESSERA012](../diagnostics/TESSERA012.md) | accepted |

So mark a type when:

- **only C++ reads it**, for example a type written by another tool, or one you want a header for before any C# code
  writes it;
- **another project derives from your abstract types**, or uses your types and relies on their initializers (see
  [Models in several projects](multiple-projects.md));
- **you want a stable union tag** that survives renaming the class (`[Tessera("OldName")]`), or two union members
  share a C# name in different namespaces ([TESSERA004](../diagnostics/TESSERA004.md));
- **your team wants every model marked**: turn on [strict mode](#strict-mode).

## Every call has a writer

A `Serialize` or `Write` call whose type is known compiles only if a writer exists for that type, and the generator
makes sure one does. Calls that could never work are compile errors ([TESSERA014](../diagnostics/TESSERA014.md)):
serializing an abstract class or an interface (the root of a buffer has one concrete type), a collection, a plain
struct, or something that is not a model at all. A private or protected class cannot be a model, because the
generated serializers cannot see it ([TESSERA013](../diagnostics/TESSERA013.md)).

## Generic code

Only generic code hides the type from the generator:

```csharp
public static byte[] Save<T>(T value) => TesseraSerializer.Serialize(value);  // TESSERA010 (info)
```

The generator cannot tell which types reach this call, so it reports [TESSERA010](../diagnostics/TESSERA010.md) and
generates nothing for it. The types it writes must be models for another reason: serialized elsewhere with their
concrete type, used by another model, or marked:

```csharp
[assembly: TesseraRoot(typeof(Monster), typeof(World))]
```

When a type reaches such a call without a writer, `Serialize` throws a `TesseraException` naming the type.

## Strict mode

Set this property in a project to require a mark on every model class, record and struct with references, so that
no type joins the wire format unannounced:

```xml
<PropertyGroup>
  <TesseraRequireAttribute>true</TesseraRequireAttribute>
</PropertyGroup>
```

Unmarked models are then errors ([TESSERA012](../diagnostics/TESSERA012.md)). Enums, plain structs and abstract union
bases need no mark.

Next: [Types](types.md).
