# `[assembly: TesseraRoot]`

**What it does:** the same as putting [`[Tessera]`](tessera.md) on each listed type: generates their C# serializers
and C++ headers. You list the types in one place instead of annotating each class.

```csharp
[assembly: TesseraRoot(typeof(SaveGame), typeof(Settings))]
```

Put it in any `.cs` file of the project, as often as you like.

## When you need it

- **Your code saves objects through a generic method**, such as
  `Save<T>(T value) => TesseraSerializer.Serialize(value)`. The generator can't tell which types reach that call
  ([TESSERA010](../diagnostics/TESSERA010.md)), so it generates nothing for them. List them here.
- **The type is declared in another project** that you can't change, or that doesn't use Tessera. Listing it makes
  this project generate the type's serializer and C++ header.
- **You want to keep attributes out of your classes**, for example in a shared library that shouldn't depend on
  Tessera.

## Details

- Types used by a listed type (field types, list element types, derived classes) get generated code too.
- An abstract class or an interface can't be saved on its own; list its concrete classes.
- For a type from another project, the initial values of its fields may not be visible here: see
  [TESSERA011](../diagnostics/TESSERA011.md).
