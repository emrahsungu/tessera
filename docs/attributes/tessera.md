# `[Tessera]`

**What it does:** generates the C# serializer and the C++ header for the type, even if no code in the project
serializes it.

```csharp
[Tessera]
public class SpawnPoint
{
    public Vec3 Position;
    public int Team;
}
```

You usually don't need it. Tessera already generates code for every class your project serializes
(`TesseraSerializer.Serialize(x)`), and for the types of their fields. On such a class, `[Tessera]` changes nothing:
the generated code and the saved bytes stay exactly the same.

## When you need it

- **You want the C++ header before any C# code saves the type**, or the code that saves it lives in another project.
  Without a `Serialize` call, nothing would be generated.
- **The class derives from a base class (or implements an interface) that another project uses as a field type.**
  That project's generator includes derived classes from other projects only when they are marked
  ([Polymorphic fields](../guide/supported-types.md#fields-declared-as-a-base-class-or-interface)).
- **Another project saves this type, and its fields have initial values** (`public short Hp = 100;`). The other
  project can only read those values if this project marks the type
  ([Classes in several projects](../guide/multiple-projects.md)).
- **Your project requires every saved class to be marked** (strict mode: [TESSERA012](../diagnostics/TESSERA012.md)).

## `[Tessera("Name")]`: a fixed type name

A field declared as a base class or an interface can hold any derived class, so the buffer records which class each
value is. It stores a 32-bit ID: a hash of the class name, without the namespace.

Renaming the class therefore changes its ID, and data saved before the rename would no longer load as that class.
Keep the old name:

```csharp
[Tessera("Sword")]                  // the class used to be called Sword
public sealed class Blade : Weapon { ... }
```

You also need a name when two derived classes of one base have the same name in different namespaces
([TESSERA004](../diagnostics/TESSERA004.md)), or, very rarely, when a name's hash is 0
([TESSERA009](../diagnostics/TESSERA009.md)).

The name changes only this ID: field names and the C++ class name stay the same.

## Details

- The attribute is not inherited: a derived class needs its own `[Tessera]`, if it needs one at all.
- To mark types without editing them, list them in [`[assembly: TesseraRoot]`](tessera-root.md).
