# Schema evolution

Models change. A game ships, players keep their save files, and the next version adds, removes and renames members.
Tessera identifies members by name, not by position, so old buffers keep working with new readers and new buffers
with old readers.

## The wire contract

There is no schema file: the C# models are the schema, so the contract between writers and readers lives in them. A
buffer depends on exactly these parts of the models:

| Part of a model | Role on the wire | Changing it |
|---|---|---|
| Member name: the C# name, or [`[TesseraName]`](../attributes/tessera-name.md) | identifies the member (the `xxHash64` of the name) | makes a new member; the old one reads as absent. Keep the old name with `[TesseraName("OldName")]` |
| Member kind: scalar type, string, object, vector and element kind, union, struct layout | must match for the member to be read | makes a new member. Some changes keep the kind: `int` ↔ `int?`, `int` ↔ an enum based on `int`, `List<T>` ↔ `T[]`, one class ↔ another (members match by name) |
| Member default: initializer or `[DefaultValue]` | not stored: writers omit members equal to it, readers return their own | changes what old buffers read: a member left out because it equaled the old default now reads as the new one |
| Names of union member types (without namespace), or `[Tessera("Name")]` | the union tag (the `xxHash32` of the name) | old buffers' values of that type read as an unknown union member. Keep the old name with `[Tessera("OldName")]` |
| Everything else: namespaces, the names of other types, member order, fields or properties, class or record | nothing | is free |

Defaults are the easy part to miss. If a member's default may change, mark it
[`[TesseraKeepDefault]`](../attributes/tessera-keep-default.md) (always stored) or write with
`WriteDefaults = true`. A default can also change without an edit to the member: when a type from another project
starts or stops being a model there, its initializers become visible or invisible to your project
([Models in several projects](multiple-projects.md)).

## What you can change

- **Reorder members freely**, and turn fields into properties or back. The layout does not depend on declaration
  order (members are sorted by alignment, size and name hash), so the layout and its fingerprint stay the same, and
  readers built from either version keep reading in place.
- **Add and delete members anywhere.** Each buffer carries a compact schema (12 bytes per member, about 32 per type).
  A reader built from other models matches members by name through it, once per buffer, and translates the buffer
  into its own layout. Members missing from a buffer read as absent, with the reader's default. A
  `[TesseraKeepDefault]` member is always stored, so in buffers from a version that has it, it reads as present.
- **Rename a member** by keeping its wire name: `[TesseraName("OldName")]`.
- **Rename a union member type** by keeping its tag: `[Tessera("OldName")]`.
- **Add union member types.** Old readers see an unknown `type()`, and every `as_x()` is empty.
- **Change a member's kind** only knowingly: it becomes a different member, which readers of the other version see as
  absent. A dictionary whose value type changes keeps its keys and has no values there; one whose key type changes is
  empty.

## Without the schema

With `IncludeSchema = false`, buffers carry no schema, and a reader accepts only buffers whose layout fingerprint
matches its own; any other buffer fails with `Error::SchemaMismatch`. Reordering members still works (the layout does
not change); adding or deleting members breaks old buffers. Leave the schema out only when the writer and the reader
are always built together.

## Cost

A reader built from the same models as the buffer reads in place, with positions known at compile time. A reader built
from other models pays once, when it opens the buffer: it parses the schema, matches members by name, and copies the
buffer into its own layout (items shared in the buffer stay shared). Every read after that is the same plain load as
on the fast path. The binder compares layouts structurally and never trusts a buffer's fingerprints for safety.

## Tested both ways

The C++ tests read old buffers with new readers and new buffers with old readers, with MSVC, GCC and Clang: the model
versions are in [tests/Tessera.Tests/Evolution.cs](../../tests/Tessera.Tests/Evolution.cs), the checks in
[tests/cpp/interop_test.cpp](../../tests/cpp/interop_test.cpp).

Next: [Models in several projects](multiple-projects.md).
