# `[assembly: TesseraRoot]`

```csharp
[assembly: TesseraRoot(typeof(Monster), typeof(World))]
```

On the assembly, as often as you like. Makes the listed types models of this project, without editing them. For a type
declared in this project, it has the same effect as [`[Tessera]`](tessera.md) on the type.

## Use it when

- **Your code serializes through generic methods.** In `Save<T>(T value) => TesseraSerializer.Serialize(value)` the
  generator cannot see which types reach the call ([TESSERA010](../diagnostics/TESSERA010.md)). List them here:

  ```csharp
  [assembly: TesseraRoot(typeof(SaveGame), typeof(Settings))]
  ```

- **Only C++ reads a type**, and you would rather list your C++-facing types in one place than mark each one.
- **The type lives in another project** that you cannot change, or that does not use Tessera itself. Listing it makes
  it a model of your project: your project generates its serializer and its C++ header.
- **You keep attributes out of your model classes**, for example in a shared library that should not depend on
  Tessera. The list can live in the project that does the writing.

## Details

- A type of another project listed here is a model of your project, but its initializers are visible only if its own
  project recorded them: see [TESSERA011](../diagnostics/TESSERA011.md) and
  [Models in several projects](../guide/multiple-projects.md).
- The types used by a listed type (members, elements, union members) become models too, as with any model.
- Abstract classes and interfaces cannot be roots of a buffer; list their concrete types instead.

See also: [Which types are models](../guide/models.md).
