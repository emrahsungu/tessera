# Which types get generated code

For some of your types, the source generator writes a C# serializer and a C++ header. You don't list those types
anywhere: the generator finds them in your code. A class, record or struct gets generated code when its project:

| The project... | Example |
|---|---|
| **saves it** | `TesseraSerializer.Serialize(monster)` or `TesseraSerializer.Write(writer, monster)`, where the compiler knows the type of `monster` (see [Generic code](#generic-code)) |
| **uses it as a field type** of a class that gets generated code | `public Weapon Equipped;`, a list element `List<Weapon>`, a dictionary value `Dictionary<string, Stock>` |
| **derives it from a field's declared type** | `class Potion : Item`, when a class with generated code has a field `Item Loot` (every public or internal derived class of the project, unless [`[TesseraUnion]`](../attributes/tessera-union.md) lists them) |
| **marks it** | [`[Tessera]`](../attributes/tessera.md) on the class, or [`[assembly: TesseraRoot(typeof(Monster))]`](../attributes/tessera-root.md) |

Enums and plain structs (structs with only numbers, enums and other plain structs inside) used by those types are
included automatically.

So in most projects you write no attributes at all: the classes you save, and everything they contain, get code.

## When do you need `[Tessera]`?

On a class that already gets generated code, `[Tessera]` changes nothing in its project: the same code is generated,
and the same bytes are saved (the tests check this). It matters when:

- **you want the C++ header before any C# code saves the class**, or the saving code is in another project;
- **the class derives from a base class (or implements an interface) that another project uses as a field type**:
  that project only includes derived classes of other projects when they are marked;
- **another project saves the class, and its fields have initial values** (`= 100`): the other project can only see
  those values when this project marks the class ([Classes in several projects](multiple-projects.md));
- **you want a fixed name for the class in saved data**, so you can rename it later (`[Tessera("OldName")]`);
- **your project requires every saved class to be marked** ([strict mode](#strict-mode)).

| | Without `[Tessera]` | With `[Tessera]` or `[assembly: TesseraRoot]` |
|---|---|---|
| Code generated | when the project saves it, or uses it as a field type | always |
| Generated code and saved bytes | the same | the same |
| Allowed in its own project's polymorphic fields | yes, if public or internal and not generic | yes |
| Allowed in other projects' polymorphic fields | no | yes |
| Field initial values visible to other projects | only if its project generates code for it | yes |
| Name stored for it in polymorphic fields | the class name | the class name, or the name in `[Tessera("Name")]` |
| With strict mode on | error [TESSERA012](../diagnostics/TESSERA012.md) | fine |

## Every `Serialize` call is checked at compile time

When the compiler knows the type of the value you serialize, the generator makes sure a serializer exists for it. If
none can exist, the call is a compile error ([TESSERA014](../diagnostics/TESSERA014.md)), instead of an exception at
run time: for example serializing an abstract class or an interface, a list or a plain struct on its own, or an
`object`. A private or protected class can't get generated code, because the generated serializers couldn't access it
([TESSERA013](../diagnostics/TESSERA013.md)).

## Generic code

Only generic code hides the type from the generator:

```csharp
public static byte[] Save<T>(T value) => TesseraSerializer.Serialize(value);  // TESSERA010 (info)
```

This method could receive any type, so the generator learns nothing from it, and says so
([TESSERA010](../diagnostics/TESSERA010.md)). The types that reach it must get generated code for another reason:
saved elsewhere with their real type, used as a field type, or listed:

```csharp
[assembly: TesseraRoot(typeof(SaveGame), typeof(Settings))]
```

A type that reaches such a call without generated code throws a `TesseraException` naming it.

## Strict mode

To require `[Tessera]` (or a `TesseraRoot` entry) on every saved class, record and struct, set this property in the
project:

```xml
<PropertyGroup>
  <TesseraRequireAttribute>true</TesseraRequireAttribute>
</PropertyGroup>
```

Then nothing enters your saved data unannounced: an unmarked class that would get generated code is an error
([TESSERA012](../diagnostics/TESSERA012.md)). Enums, plain structs and abstract base classes need no mark.

Next: [Supported types](supported-types.md).
