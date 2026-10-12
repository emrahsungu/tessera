# Changing your classes

Classes change. A game ships, players keep their save files, and the next version adds, removes and renames fields.
Tessera finds fields **by name**, not by position, so data saved by an older version still loads in a newer one, and
the other way round.

## How fields are matched

Each buffer stores, along with the data, a short description of its classes: for each field, a 64-bit hash of its
name, and its type ([`IncludeSchema`](../options/include-schema.md)). A program opening a buffer written by another
version of the classes matches fields by those name hashes, once, and converts the buffer to its own layout. Then it
reads at full speed.

- A field the reader doesn't know is skipped.
- A field the buffer doesn't contain reads as missing: `has_x()` in C++ (`HasX` in .NET) is false, and its value is
  the default compiled into the reader; `Deserialize` sets the field to its declared default.

C++ and .NET readers work the same way.

## What saved data depends on

| Part of your class | Role in saved data | When you change it |
|---|---|---|
| a field's name (or its [`[TesseraName]`](../attributes/tessera-name.md)) | identifies the field | it becomes a new field, and the old one reads as missing. Keep the old name with `[TesseraName("OldName")]` |
| a field's type | must match for the field to be read | it becomes a new field. These changes keep the type: `int` ↔ `int?`, `int` ↔ an enum based on `int`, `List<T>` ↔ `T[]`, one class ↔ another (their fields are matched by name) |
| a field's default value (initializer or `[DefaultValue]`) | not stored: fields equal to it are left out, and readers fill in their own default | data saved earlier reads the new default wherever the field was left out |
| the name of a class held in a field declared as a base class or interface (or its `[Tessera("Name")]`) | identifies the class (a 32-bit hash of its name) | data saved earlier reads values of that class as an unknown class. Keep the old name with `[Tessera("OldName")]` |
| anything else: namespaces, other class names, field order, field vs. property, class vs. record | nothing | change it freely |

The default value is the one that is easy to miss. If a field's default may change, mark the field
[`[TesseraKeepDefault]`](../attributes/tessera-keep-default.md), which always writes it, or save with
[`WriteDefaults = true`](../options/write-defaults.md).

## What you can change

- **Reorder fields**, or turn fields into properties and back: nothing changes in the saved data, and even the layout
  hash stays the same.
- **Add and remove fields anywhere.** Older and newer programs read each other's data; missing fields read as their
  default.
- **Rename a field**, keeping its saved name: `[TesseraName("OldName")]`.
- **Rename a class**, keeping its saved name: `[Tessera("OldName")]` (only matters for classes stored in fields
  declared as a base class or interface).
- **Add derived classes.** Older programs see an unknown class: `type()` (`Tag` in .NET) returns a value outside their
  enum, and every `as_x()` (`AsX`) is empty; `Deserialize` sets such a field to null.
- **Change a field's type** only knowingly: it becomes a different field, and the other version reads it as missing.
  A dictionary whose value type changes keeps its keys but has no values in the other version; one whose key type
  changes is empty there.

## Without the description

With [`IncludeSchema = false`](../options/include-schema.md), buffers carry no description of their classes. A reader
then accepts only buffers whose layout hash matches its own, and refuses others with `Error::SchemaMismatch`
(`TesseraReadError.SchemaMismatch` in .NET).
Reordering fields still works (it doesn't change the layout); adding or removing fields breaks older data.

## What it costs

A reader built from the same classes as the buffer reads it directly. A reader built from other versions pays once,
when opening: it parses the description, matches fields by name, and copies the buffer into its own layout (values
stored once in the buffer stay stored once). Every read after that is as fast as usual. The matching compares the
actual layouts; it never trusts the hashes in the buffer for safety.

## Tested both ways

The tests read old data with new code and new data with old code, with MSVC, GCC and Clang, and in .NET: the class
versions are in [tests/Tessera.Tests/Evolution.cs](../../tests/Tessera.Tests/Evolution.cs), the checks in
[tests/cpp/interop_test.cpp](../../tests/cpp/interop_test.cpp),
[tests/Tessera.Tests/ReaderVersionTests.cs](../../tests/Tessera.Tests/ReaderVersionTests.cs) and
[tests/Tessera.Tests/DeserializeTests.cs](../../tests/Tessera.Tests/DeserializeTests.cs).

Next: [Classes in several projects](multiple-projects.md).
