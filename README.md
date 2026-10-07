<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/tessera-icon-dark.svg">
    <img src="docs/images/tessera-icon.svg" alt="Tessera" width="128">
  </picture>
</p>

<h1 align="center">Tessera</h1>

<p align="center">
  Binary buffers from plain .NET objects, read in place by C++20.<br>
  No IDL, no parsing, and no allocations on the read side.
</p>

## 1. Example

Your C# classes are the schema:

```csharp
namespace Game;

public class Monster
{
    public string? Name;
    public short Hp = 100;            // equal to its default: not stored
    public int? Mana;                 // nullable: C++ can tell whether it was set
    public Vec3 Position;             // plain struct: stored inline
    public List<Weapon>? Weapons;
}

public class Weapon { public string? Name; public int Damage; }

public record struct Vec3(float X, float Y, float Z);
```

Serialize one. That call is all the source generator needs: it writes a serializer for `Monster` and for every type
`Monster` uses.

```csharp
using Game;
using Tessera;

var orc = new Monster
{
    Name = "Orc",
    Mana = 25,
    Position = new Vec3(1, 2, 3),
    Weapons = [new() { Name = "Axe", Damage = 9 }, new() { Name = "Bow", Damage = 4 }],
};

byte[] buffer = TesseraSerializer.Serialize(orc);
File.WriteAllBytes("monster.bin", buffer);
Console.WriteLine($"{buffer.Length} bytes");
```

```text
328 bytes
```

The same build writes a C++ header for each class. From `game/Monster.tessera.hpp` (shortened):

```cpp
class Monster final : public tessera::Table<Monster> {
public:
    std::string_view            name() const noexcept;      // string? Name
    std::int16_t                hp() const noexcept;        // short Hp = 100
    std::optional<std::int32_t> mana() const noexcept;      // int? Mana
    const Vec3&                 position() const noexcept;  // Vec3 Position
    tessera::Vector<Weapon>     weapons() const noexcept;   // List<Weapon>? Weapons
    // has_name(), has_hp() ...: whether each member was stored
};
```

C++ verifies the buffer once, then reads it where it lies:

```cpp
#include "game/Monster.tessera.hpp"

// data, size: the 328 bytes of monster.bin, in 8-byte aligned memory
tessera::Reader<game::Monster> reader(data, size);  // verifies the buffer once
if (!reader) return 1;

game::Monster orc = reader.root();  // a view: reads in place, nothing is parsed or copied
const game::Vec3& p = orc.position();
std::cout << orc.name() << ": hp " << orc.hp() << ", mana " << orc.mana().value_or(0)
          << ", position " << p.x << ' ' << p.y << ' ' << p.z << '\n';
for (game::Weapon w : orc.weapons()) std::cout << "  " << w.name() << ": damage " << w.damage() << '\n';
```

```text
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
```

`hp` was not stored, because it equaled its default, so C++ returned the default compiled into its header. A view is
one pointer into the buffer: keep the buffer (and the `Reader`) alive while you use it. The whole example, including
loading the file, is in [samples/Quickstart](samples/Quickstart).

### What the 328 bytes hold

| Part | Bytes | Contents |
|---|---:|---|
| Header | 16 | root offset, magic, format version, layout fingerprint |
| Data | 88 | the orc (28), the weapon list (12), two weapons (12 each) and three strings (8 each) |
| Schema | 224 | the hashed member names and the layouts of `Monster`, `Weapon`, `Vec3` and the list |

The schema lets readers built from older or newer versions of these classes read the buffer: add, delete or reorder
members whenever you like (see [Schema evolution](#schema-evolution)). Its size depends on the types, not on the data,
so it only matters for small buffers. When the writer and the reader are always built from the same classes, leave it
out:

```csharp
byte[] small = TesseraSerializer.Serialize(orc, new TesseraOptions { IncludeSchema = false });  // 104 bytes
```

## 2. Getting started

Tessera is not on nuget.org yet, so build the package once:

```shell
dotnet pack src/Tessera -c Release -o artifacts
```

Add `artifacts` as a package source in your `nuget.config`, reference the package, and choose where the C++ headers
go:

```xml
<ItemGroup>
  <PackageReference Include="Tessera" Version="0.1.0-preview" />
</ItemGroup>
<PropertyGroup>
  <!-- After each build, the generated headers and the C++ runtime headers are written here. -->
  <TesseraCppOutputDir>cpp/generated</TesseraCppOutputDir>
</PropertyGroup>
```

In C++, add that folder to the include path. `game/Monster.tessera.hpp` includes what `Monster` needs, and the header
named after the assembly (`<AssemblyName>.tessera.hpp`) includes every model type. The runtime is header-only C++20.
Buffers must start at an 8-byte aligned address, as memory from `new`, `malloc` or a `std::vector<std::uint64_t>` does.

NuGet caches packages by version: after repacking the same version, delete `~/.nuget/packages/tessera`.

## 3. Why Tessera

Tessera was built for game tooling: UI prefabs, records, scene graphs and time series authored in .NET and loaded by a
C++ engine. Both sides are generated from the same C# source, so they cannot drift apart.

- **Your classes are the schema.** No IDL and no required attributes: classes, records, structs, enums, nullable
  values, lists, dictionaries, and abstract classes or interfaces as unions. Unsupported types are reported at compile
  time with a suggested replacement.
- **Zero-copy reads.** After one verification pass, every access is a plain load: no parsing, no allocations.
- **Readable C++.** One header per type, `std::optional` for nullable members, `std::string_view` for strings,
  `enum class` with your names, and `const S&` straight into the buffer for plain structs.
- **Fast.** Writes are 6.3× faster than FlatBuffers and 2.4× faster than MessagePack-CSharp, with no allocations
  besides the resulting array (with a reused writer, none at all). Verifying a buffer is 2.7–4.1× faster than
  FlatBuffers' verifier; verifying and then reading every field is 1.6–1.9× faster. Geometric means over the
  benchmark workloads (per compiler for C++); see [Performance](#4-performance).
- **Compact.** Absent members and default values take no space, and equal strings are stored once: buffers are up to
  70% smaller than FlatBuffers' (1.3× on the geometric mean).
- **Schema evolution by name.** Add, delete, reorder and rename members anywhere, with no field ids or deprecated
  slots; old and new readers keep working.
- **Hardened.** About 450,000 fuzzed buffers per test run under GCC's AddressSanitizer and UBSan, MSVC and Clang, and
  every change to the format or the reader is benchmarked against the commit before it.
- **Apache-2.0** licensed.

## 4. Performance

Environment: Intel Core i7-6700K, Windows 10, .NET 10.0.12. C++ with clang-cl 19.1, MSVC 19.42 and GCC 15.2 (in WSL),
each compiler with the same flags for every library (optimized, AVX2). Against FlatBuffers 25.12.19,
MessagePack-CSharp 3.1.10 and msgpack-cxx 9.0.0. Medians of interleaved rounds; in .NET, every case starts on a
collected heap, and result arrays are allocated as short-lived objects ([docs/BENCHMARKS.md](docs/BENCHMARKS.md)
explains why).

There are nine workloads: a UI prefab (400 objects with polymorphic components), a game world (1,000 monsters),
2,000 sparse records (40 optional fields each), a 20,000-sample time series, three scene graphs of 1,024 nodes (dense,
sparse, and 16 distinct nodes repeated), two dictionaries of 5,000 entries, and canada.json, the contour of Canada from
the common JSON benchmarks: one GeoJSON polygon of 480 rings and 55,563 points. Every library writes the same C#
objects, and every C++ reader computes a checksum over every field, which must match the C# objects before anything
is timed.

Summary: how many times faster (or smaller) Tessera is than FlatBuffers. A safe read verifies the buffer, then reads
every field.

| Workload | .NET write | Size | Safe read, Clang | Safe read, GCC | Safe read, MSVC |
|---|---:|---:|---:|---:|---:|
| prefab | 4.11× | 1.28× | 1.53× | 2.18× | 1.72× |
| monsters | 4.68× | 1.18× | 1.50× | 1.69× | 1.43× |
| records | 30.51× | 3.37× | 2.53× | 2.73× | 3.32× |
| series | 5.34× | 1.00× | 1.89× | 1.97× | 1.80× |
| dense-unique | 4.08× | 1.06× | 1.56× | 1.99× | 1.33× |
| sparse-unique | 5.86× | 1.04× | 1.78× | 2.11× | 1.71× |
| dense-shared | 5.26× | 1.32× | 1.46× | 2.03× | 1.33× |
| lookup | 31.21× | 1.16× | 2.31× | 2.19× | 1.59× |
| canada | 1.25× | 1.00× | 1.05× | 1.03× | 1.02× |

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/summary-dark.svg">
  <img alt="Tessera against FlatBuffers and MessagePack, geometric means per metric and compiler" src="docs/images/benchmarks/summary-light.svg">
</picture>

These numbers describe these workloads on this machine; they are not guarantees. Measure with your own data.

### .NET write

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/write-dark.svg">
  <img alt=".NET write time per workload for Tessera, FlatBuffers and MessagePack" src="docs/images/benchmarks/write-light.svg">
</picture>

### Buffer size

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/size-dark.svg">
  <img alt="Buffer size per workload for Tessera, FlatBuffers and MessagePack" src="docs/images/benchmarks/size-light.svg">
</picture>

MessagePack stores small integers in one or two bytes, so it is smaller on six of the nine workloads; Tessera is
smaller on the prefab, the sparse records and canada, whose doubles take nine bytes each in MessagePack. Tessera keeps
values fixed-width so that they can be read in place.

### C++: verify, then read every field

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/safe-read-dark.svg">
  <img alt="Verify then read every field, Tessera against FlatBuffers per workload and compiler" src="docs/images/benchmarks/safe-read-light.svg">
</picture>

### C++: 1,000 random reads

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/random-access-dark.svg">
  <img alt="Random reads, Tessera against FlatBuffers per workload and compiler" src="docs/images/benchmarks/random-access-light.svg">
</picture>

### Dictionary lookups

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/benchmarks/lookups-dark.svg">
  <img alt="Dictionary lookups by string and by int, Tessera against FlatBuffers per compiler" src="docs/images/benchmarks/lookups-light.svg">
</picture>

### Where Tessera is slower

- **Size, against MessagePack**, on six of the nine workloads (1.11–1.70×): MessagePack stores small integers in one
  or two bytes; Tessera keeps values fixed-width so that they can be read in place.
- **Time: nowhere by more than 1%.** Two GCC rows are within 1% of FlatBuffers: reading every field of the sparse
  scene and of canada. There, all three libraries run at the speed of the benchmark's checksum, which mixes every
  value into one running hash. MessagePack's C++ traversals and random reads run over a tree parsed beforehand, so
  they are not compared.

With MSVC, the C++ readers' small helper functions are force-inlined, for all three libraries: MSVC stopped inlining
them into one large function when it read Tessera, which made reading every field of the prefab 24% slower.
FlatBuffers' and MessagePack's code does not change ([benchmarks/native/readers.hpp](benchmarks/native/readers.hpp)).

The full list, computed from the results, is in [docs/BENCHMARKS.md](docs/BENCHMARKS.md).

Reproduce everything (it also fetches FlatBuffers, msgpack-cxx and canada.json, SHA-256 pinned, into `.deps/`):

```shell
powershell scripts/bench.ps1
```

It writes every table to [docs/BENCHMARKS.md](docs/BENCHMARKS.md), the raw samples to `docs/benchmarks/*.json` and
these charts to `docs/images/benchmarks`. Every change to the format or the reader is also measured against the
commit before it with `scripts/ab.ps1` (see [How to build](#how-to-build)).

## 5. How it works

A Roslyn source generator turns ordinary C# classes, records and structs into allocation-free writers, and writes a
readable C++20 header of view classes for the same types. C++ verifies a buffer once and then reads it where it lies:
every accessor is a plain load.

```text
C# models: plain classes, records and structs
        │
        │  source generator, at build time
        ▼
.NET writer ───► buffer: presence bits, inline values, forward offsets, optional schema
                    │
                    ▼
          C++20 tessera::Reader<T>: verifies once
                    │
                    ▼
          generated views: plain loads, no parse, no allocation
```

### Presence bits instead of a vtable

An object is a presence bitmap followed by the cells of its present members. Members that are absent, null or equal
to their default take no space at all, and bools live in the bitmap itself (two bits: present, value).

```text
   0          4W              4W+F
   ┌──────────┬───────────────┬───────┬───────┬─────────┬────────┬───
   │ presence │ fixed cells   │ cell  │ cell  │ offset  │ offset │ …     only present cells are stored,
   │ bitmap   │ (opt-in)      │ 8 B   │ 4 B   │ string  │ vector │       sorted by alignment, then size
   └──────────┴───────────────┴───────┴───────┴─────────┴────────┴───

   position of a cell = 4W + F + Σ size × popcount(presence & mask)
                        one popcount per run of equal-size cells, or one load
                        per byte of presence bits from a table of cell sizes
```

The generator knows every type's layout, so the masks and sizes are compile-time constants in the C++ header: reading
a member is a bit test, one or a few popcounts and a load, with no vtable and no hash. Where the cells before a member
have different sizes, a byte of presence bits indexes a 256-entry table of their sizes instead (types with the same
cell sizes share it): one load replaces several popcounts, and a load waits for its data outside the CPU's scheduler,
so more independent reads overlap. Values of 8 bytes or less and plain structs (C layout) are stored inline; strings,
vectors, objects and union members sit behind forward offsets.

### Fixed cells for members that are always set

Members marked `[TesseraKeepDefault]` (scalars, enums and plain structs) are always stored, so they need no presence
bit: they come first, at constant positions, and reading one is a single load. On the time series workload, that makes
random reads 20–33% faster and verification up to 7% faster, at the same size.

### Written back to front

The writer builds a buffer from the end, so every offset points forward and cycles are impossible. With
`Sharing.Strings` (the default), equal strings are written once; with `Sharing.All`, equal strings, vectors, shared
structs and objects are written once, and the buffer becomes a DAG. Writers are generated per type: no reflection, and
with a reused `TesseraWriter` no allocations (except a dictionary other than `Dictionary<TKey, TValue>`, such as a
`SortedDictionary`, whose enumerator is allocated: about 230 bytes).

### Verify once, then plain loads

`tessera::Reader<T>` checks the whole buffer before the first access: the header, every object's bitmap and cells,
every offset (non-null where required, forward, in bounds and aligned), string and vector lengths, union tags, and
nesting depth, plus UTF-8 if you ask. The verifier is generated per type. Shared items in a DAG
would be visited once per path, which grows exponentially with nesting; when the walk outgrows the buffer, the
verifier switches to checking each shared item once (the only case in which it allocates, for its memo). After
verification, accessors are bounds-free loads.

### Same models: compile-time positions; other models: translate once

```text
buffer fingerprint == compiled fingerprint  ──►  compile-time positions, no lookups
                   !=                       ──►  parse the buffer's schema once, match members by the
                                                 xxHash64 of their names, translate the buffer into
                                                 the reader's layout, then compile-time positions
```

Every buffer records a fingerprint of its root type's layout. A reader built from the same models takes the fast
path. A reader built from other models matches members by name once, when the `Reader` is opened, and translates the
buffer into a copy in its own layout, so every read afterwards is the same plain load as on the fast path; members
it does not find read as absent. Translation costs one copy of the buffer at open (items shared in the buffer stay
shared), and the copy lives as long as the `Reader`. The binder compares layouts structurally and never trusts the
buffer's fingerprints for safety. See [docs/FORMAT.md](docs/FORMAT.md) for the complete wire format.

### Generated code

| Generated | Where | Contents |
|---|---|---|
| C# writers | compiled into your assembly | one writer per type, presence bits computed while writing, no reflection |
| C++ headers | `TesseraCppOutputDir` after each build | one header per type (by C++ namespace) and one per assembly that includes them all |
| C++ runtime | copied next to the headers | header-only: views, `Reader`, verifier, schema translation, JSON dump |

## 6. Types

| C# | C++ accessor returns |
|---|---|
| `bool`, `sbyte` … `ulong`, `float`, `double` | the same scalar type (`std::int32_t` …) |
| `char` | `char16_t` |
| enum (any integer base, `[Flags]` too) | `enum class` with the same values (flags get `\|`, `&`, `~`) |
| `T?` for value types | `std::optional<T>` |
| `string` | `std::string_view` (UTF-8; null → `data() == nullptr`) |
| struct with only unmanaged fields | a C++ struct of the same layout, returned as `const S&` (stored inline) |
| class, record, struct with references | a view class with one accessor per member |
| `T[]`, `List<T>`, `IReadOnlyList<T>`, any `IEnumerable<T>` | `tessera::Vector<T>` (random access, range-for); `List<int?>` gives `tessera::Vector<std::optional<std::int32_t>>` |
| `Dictionary<K, V>`, `IReadOnlyDictionary<K, V>`, any `IDictionary<K, V>` (keys: integers, chars, enums, strings) | `tessera::Map<K, V>`: `find(key)` and `contains(key)` (binary search), `keys()`, `values()` |
| abstract class or interface | a union view: `type()`, `as_potion()` … |

Public fields and properties are serialized, as are internal ones declared in the same assembly. Properties need a
setter or must be auto-properties. Leave a member out with `[TesseraIgnore]`, `[IgnoreDataMember]` or `[NonSerialized]`.
`DateTime`, `Guid`, `decimal` and similar types have no portable C++ form; they are reported at compile time with a
suggested replacement (for example `DateTime.Ticks` as a `long`).

### Unions

A member whose type is an abstract class or an interface holds any of the classes that derive from it or implement it:

```csharp
public abstract class Item { }
public sealed class Potion : Item { public int Heal; }
public sealed class Key : Item { public uint Door; }

public class Chest { public Item? Loot; }
```

```cpp
if (game::Potion potion = chest.loot().as_potion()) use(potion.heal());
else if (game::Key key = chest.loot().as_key()) use(key.door());
```

### Which types are models

A class, record or struct is a model when its project does one of these:

- **serializes it:** `TesseraSerializer.Serialize(value)` or `Write(writer, value)`, where the compiler knows the type
  of `value`;
- **uses it in a model:** as a member, as the element or value type of a collection, or as a concrete class deriving
  from the type of an abstract or interface member (every public or internal one in the project, unless
  `[TesseraUnion]` lists them);
- **marks it:** `[Tessera]` on the type, or `[assembly: TesseraRoot(typeof(Monster))]` in the project, for types you
  cannot or would rather not edit.

`[Tessera]` is optional. With or without it:

- **Every call has a writer.** A `Serialize` or `Write` call whose type is known compiles only if a writer exists
  for that type. Calls that could never work, for example with an abstract type, a collection or a private class,
  are compile errors. Only generic code hides the type: a call that serializes a type parameter `T` is reported
  (TESSERA010, info), and the types it writes must be models for another reason.
- **A bare `[Tessera]` changes nothing in its own project.** On a type that is already a model there, it changes no
  byte of any buffer and no line of the generated C# or C++ (the tests check this). It changes how other projects
  see the type, as the table below shows.

What the attribute adds:

| | Without `[Tessera]` | With `[Tessera]` or `[assembly: TesseraRoot]` |
|---|---|---|
| Model | when its project serializes it or a model uses it | always: C++ gets its header even if no C# code writes it |
| Buffers and generated code | the same | the same |
| Union member of this project's abstract types | yes, when public or internal and not generic | yes |
| Union member of another project's abstract types | no | yes |
| Member initializers visible to other projects | when its own project treats it as a model | yes |
| Stable type name (the source of union tags) | the C# type name | the C# type name, or `[Tessera("Name")]` |
| Under `TesseraRequireAttribute` | error TESSERA012 | accepted |

Member initializers are compiled into constructors, which the generator cannot read in a referenced assembly. So
each project's generator records the initializers of its own models in its assembly, and other projects read them
from there. When a type's project does not treat it as a model, nothing is recorded: another project that uses the
type takes zero as the default of its members and warns (TESSERA011). Mark those types in their project, list them in
`TesseraRoot` there, or give the members `[DefaultValue]`.

**Strict mode.** Set `<TesseraRequireAttribute>true</TesseraRequireAttribute>` to require a mark on every model
class, record and struct with references (TESSERA012), so that no type joins the wire format unannounced. Enums,
plain structs and abstract union bases need no mark.

### Attributes

| Attribute | Effect |
|---|---|
| `[TesseraName("old")]` | wire name of a member, so it can be renamed in C# |
| `[DefaultValue(x)]`, initializer (`= 100`) | the default that is omitted from buffers and returned by C++ when absent |
| `[TesseraKeepDefault]` | always store the member; scalars, enums and plain structs then sit at a fixed position with no presence bit |
| `[TesseraShared]` | store a struct once behind an offset (useful for large repeated structs with `Sharing.All`) |
| `[TesseraUnion(typeof(A), typeof(B))]` | the types allowed in an abstract or interface member (default: see [Which types are models](#which-types-are-models)) |
| `[Tessera]`, `[Tessera("Name")]` | optional: makes the type a model; the name fixes its union tag (needed when two union members share a C# name) |
| `[assembly: TesseraRoot(typeof(A), ...)]` | makes types models without editing them |

## 7. Advanced features

### The wire contract

There is no schema file: the C# models are the schema, so the contract between writers and readers is implicit in
them. A buffer depends on exactly these parts of the models:

| Part of a model | Role on the wire | Changing it |
|---|---|---|
| Member name: the C# name, or `[TesseraName]` | identifies the member (`xxHash64` of the name) | makes a new member; the old one reads as absent. Keep the old name with `[TesseraName("OldName")]` |
| Member kind: scalar type, string, object, vector and element kind, union, struct layout | must match for the member to be read | makes a new member. Some changes keep the kind: `int` ↔ `int?`, `int` ↔ an enum based on `int`, `List<T>` ↔ `T[]`, one class ↔ another (members match by name) |
| Member default: initializer or `[DefaultValue]` | not stored: writers omit members equal to it, readers return their own | changes what old buffers read: a member left out because it equaled the old default now reads as the new one |
| Names of union member types (without namespace), or `[Tessera("Name")]` | the union tag (`xxHash32` of the name) | old buffers' values of that type read as an unknown union member. Keep the old name with `[Tessera("OldName")]` |
| Everything else: namespaces, the names of other types, member order, fields or properties, class or record | nothing | is free |

Defaults are the easy part to miss. If a member's default may change, mark it `[TesseraKeepDefault]` (always stored)
or write with `WriteDefaults = true`. A default can also change without an edit to the member: when a type from
another project starts or stops being a model there, its initializers become visible or invisible to your project
(see [Which types are models](#which-types-are-models)).

### Schema evolution

Members are identified by name, not by position. In FlatBuffers a field's slot is its identity: new fields go at the
end (or every field needs an explicit `id`), deleted fields stay in the schema as `deprecated`, and moving a field
breaks old buffers. In Tessera:

- **Reorder members freely**, and turn fields into properties or back. The layout does not depend on declaration
  order (cells are sorted by alignment, size and name hash), so the layout and the fingerprint stay the same, and
  readers built from either version keep using compile-time positions.
- **Add and delete members anywhere.** Each buffer carries a compact schema, 12 bytes per member and about 32 per type
  (224 bytes in the [example](#1-example)), which you can turn off. A reader built from other models matches members
  by name through it, once per buffer, and translates the buffer into its own layout. Members missing from a buffer
  read as absent, with the reader's default (a `[TesseraKeepDefault]` member is always stored, so it reads as present,
  with its default).
- **Rename** a member by keeping its wire name: `[TesseraName("OldName")]`.
- **Changing a member's kind** makes it a different member: readers of the other version see it as absent. A
  dictionary whose value type changes keeps its keys and has no values there; one whose key type changes is empty.
- **Unions:** new member types can be added. Old readers see an unknown `type()`, and every `as_x()` is empty.
- Without the schema (`IncludeSchema = false`), a reader accepts only buffers whose fingerprint matches its own
  (`Error::SchemaMismatch` otherwise): adding or deleting members then breaks old buffers, reordering does not.

The C++ tests read old buffers with new readers and new buffers with old readers, with MSVC, GCC and Clang: the
model versions are in [tests/Tessera.Tests/Evolution.cs](tests/Tessera.Tests/Evolution.cs), the checks in
[tests/cpp/interop_test.cpp](tests/cpp/interop_test.cpp).

### Write options

`TesseraSerializer.Serialize(value, new TesseraOptions { ... })`:

| Option | Default | |
|---|---|---|
| `Sharing` | `Strings` | `None`: fastest writes. `Strings`: equal strings stored once. `All`: equal strings, vectors and objects stored once (the buffer becomes a DAG) |
| `IncludeSchema` | `true` | embed the schema so other model versions can read the buffer |
| `WriteDefaults` | `false` | also store members equal to their default |
| `MaxDepth` | `128` | nesting limit (objects and vectors); matches C++ `Options::max_depth` |

To write without allocating, reuse a writer. The span points into the writer and is valid until its next use:

```csharp
var writer = new TesseraWriter();                          // or new TesseraWriter(options)
ReadOnlySpan<byte> span = TesseraSerializer.Write(writer, orc);
```

### Dictionaries

Dictionaries are written with their keys sorted (strings by code point, the order of their UTF-8 bytes), so C++ finds
a key by binary search:

```cpp
if (auto stock = shop.items().find("item-0042")) use(stock->count());
bool known = shop.prices().contains(7);
```

### Verification options and trusted buffers

```cpp
tessera::Options strict;
strict.utf8 = true;                                   // also validate UTF-8 (default: false)
strict.max_depth = 64;                                // nesting limit (default: 128)
strict.max_items = 100'000;                           // objects and vectors verified (default: 2^24)
tessera::Reader<game::Monster> checked(data, size, strict);

// Buffers you wrote yourself, with exactly these models: no verification.
tessera::Reader<game::Monster> trusted(data, size, tessera::trusted);
```

### Models across assemblies

Models can span several assemblies. An assembly's headers also cover the model types it uses from other assemblies,
and the headers for one type are identical whichever assembly wrote them, so including several assemblies' headers in
one file works. A type that two different model versions define stops the build with an `#error`. The initializers of
another project's types are visible only when that project treats them as models (see
[Which types are models](#which-types-are-models)).

### C++ naming and output

MSBuild properties: `TesseraCppOutputDir`; `TesseraCppHeaderName` (default `<AssemblyName>.tessera.hpp`);
`TesseraCppNamespace` (default: the C# namespace in lower case); `TesseraCppNaming` (`snake_case` (default), `camelCase`,
`PascalCase` or `original`); `TesseraCppCopyRuntime` (default `true`).

### Debugging

`tessera::to_json(view)` prints any view, nested objects and vectors included.

## Requirements

- Using the package: .NET 8 or later, and any C# compiler from the .NET 8 SDK on.
- C++: a C++20 compiler and CMake 3.20 or later. Tested with MSVC 19.42, clang-cl 19, GCC 15 and Clang 21.
- Building this repository: the .NET 10 SDK, CMake, and Visual Studio 2022 or later with "Desktop development with
  C++" (add "C++ Clang tools for Windows" for clang-cl; Visual Studio 2026 needs CMake 4.2 or later); WSL with GCC and
  Clang for the Linux tests and the GCC benchmark.

## How to build

### 1. Build

```shell
dotnet build Tessera.slnx -c Release
```

### 2. Run tests

```shell
powershell scripts/test.ps1             # .NET tests, then C++ interop, fuzz and UTF-8 tests with MSVC
powershell scripts/test.ps1 -Asan       # also an MSVC AddressSanitizer build
powershell scripts/test.ps1 -Linux      # also GCC (AddressSanitizer + UBSan) and Clang in WSL
powershell scripts/test.ps1 -Package    # also packs Tessera and builds samples/Quickstart from the package
```

### 3. Run benchmarks

```shell
powershell scripts/bench.ps1            # writes docs/BENCHMARKS.md and docs/images/benchmarks
powershell scripts/bench.ps1 -NoGcc     # without WSL; -NoClang without clang-cl; -Quick for a short smoke run
```

### 4. Compare two commits

```shell
powershell scripts/ab.ps1 -Baseline <commit> -Candidate <commit>
powershell scripts/ab.ps1 -Candidate HEAD -Jcc   # with the JCC-erratum mitigation, to rule out code placement
```

Both commits are built in their own worktrees and run in alternating order; FlatBuffers and MessagePack run on both
sides as the noise control.

## Repository layout

| Folder | Contents |
|---|---|
| `src/Tessera` | runtime (writer, options, attributes) and the package project |
| `src/Tessera.Generator` | source generator: model analysis, wire layout, C# writers, C++ headers |
| `src/Tessera.Cpp` | build step that writes the generated headers to disk |
| `cpp/include/tessera` | C++20 header-only runtime: views, verifier, schema translation, JSON dump |
| `tests` | .NET tests, generator tests, C++ interop, fuzz and UTF-8 tests |
| `benchmarks` | the Tessera / FlatBuffers / MessagePack benchmark (C# writers, C++ readers) |
| `samples/Quickstart` | the example at the top of this page, end to end |
| `docs` | [wire format](docs/FORMAT.md), [benchmark results](docs/BENCHMARKS.md) and their raw data, the README's charts |

## Third-party components

The Tessera package bundles no third-party code. The following projects are used to build, test and benchmark it; each
is distributed under its own license.

| Project | Used for | License |
|---|---|---|
| [FlatBuffers](https://github.com/google/flatbuffers) 25.12.19 | benchmarks: `flatc`, C++ headers, the C# runtime (built from source) | Apache-2.0 |
| [msgpack-c](https://github.com/msgpack/msgpack-c) (C++) 9.0.0 | benchmarks: the C++ MessagePack reader | BSL-1.0 |
| [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) 3.1.10 | benchmarks: the .NET MessagePack writer | MIT |
| [nativejson-benchmark](https://github.com/miloyip/nativejson-benchmark) | benchmarks: `canada.json`, the canada workload's data (fetched, not included) | MIT |
| [xUnit.net](https://github.com/xunit/xunit) v3 | tests | Apache-2.0 |
| [Roslyn](https://github.com/dotnet/roslyn) (Microsoft.CodeAnalysis.CSharp 4.8) | building the source generator (not shipped) | MIT |
| [xxHash](https://github.com/Cyan4973/xxHash) | the hash algorithm of member names, union tags and fingerprints, implemented in `src/Shared/XxHash.cs` | BSD-2-Clause |

## Contributing

Bug reports and pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). Please report security problems
privately, as described in [SECURITY.md](SECURITY.md).

## License

Tessera is released under the [Apache License 2.0](LICENSE).

## Status

Tessera is a preview (0.1.0-preview, wire format version 2). The API and the wire format may change before 1.0; readers
reject buffers of other format versions. The package is not published to nuget.org yet.
