# Types

## Members

Public fields and properties are serialized, and so are internal ones declared in the same assembly. A property needs
a setter or must be an auto-property. Leave a member out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md),
`[IgnoreDataMember]` or `[NonSerialized]`.

Member order does not matter: the layout sorts members by alignment, size and name hash, so you can reorder members,
or turn fields into properties and back, without changing a byte. A type can have up to 4,096 serialized members
([TESSERA005](../diagnostics/TESSERA005.md)).

## What each C# type becomes

| C# | C++ accessor returns |
|---|---|
| `bool`, `sbyte` … `ulong`, `float`, `double` | the same scalar type (`bool`, `std::int8_t` … `std::uint64_t`, `float`, `double`) |
| `char` | `char16_t` |
| enum (any integer base, `[Flags]` too) | an `enum class` with the same names and values; flags enums get `\|`, `&` and `~` |
| `T?` for a value type | `std::optional<T>` |
| `string` | `std::string_view` of UTF-8 bytes; a null string has `data() == nullptr`, an empty one does not |
| struct with only unmanaged fields ("plain struct") | a C++ struct with the same layout, stored inline and returned as `const S&` (by value if it has a `bool` field) |
| class, record, or struct with references | a view class with one accessor per member |
| `T[]`, `List<T>`, `IReadOnlyList<T>`, any `IEnumerable<T>` | `tessera::Vector<T>`: `size()`, `operator[]`, range-for |
| `List<int?>` and other lists of nullable values | `tessera::Vector<std::optional<std::int32_t>>` |
| `List<List<T>>` and other nested collections | `tessera::Vector<tessera::Vector<T>>` |
| `Dictionary<K, V>`, `IReadOnlyDictionary<K, V>`, any `IDictionary<K, V>` | `tessera::Map<K, V>`: see [Dictionaries](#dictionaries) |
| abstract class or interface | a union view: see [Unions](#unions) |

Every member also gets `has_x()`, which tells whether a value was stored. A member that equals its default is not
stored ([`[TesseraKeepDefault]`](../attributes/tessera-keep-default.md) changes that), and reads as the default
compiled into the header.

## Plain structs

A struct whose fields are all unmanaged (numbers, enums, `bool`, `char`, other plain structs) is stored inline, in
its C# layout, and C++ reads it as a struct of the same layout: `const Vec3& p = orc.position();` is one load, with
nothing to parse. Use plain structs for vectors, colors, ids and other small values.

They cannot have an explicit layout (`[StructLayout(LayoutKind.Explicit)]`) or fixed-size buffers
([TESSERA006](../diagnostics/TESSERA006.md)), and a plain struct from another assembly must have public fields.

A large struct that repeats often can be stored once and referenced instead: see
[`[TesseraShared]`](../attributes/tessera-shared.md).

## Strings

Strings are written as UTF-8 with a length and a terminating zero, so `name().data()` is also a valid C string. With
the default `Sharing.Strings`, equal strings are stored once ([Writing in .NET](writing.md#sharing)). The verifier
checks UTF-8 only when you ask ([Reading in C++](reading-cpp.md#verification-options)).

## Collections

Arrays, lists and any other `IEnumerable<T>` become vectors. Vectors of numbers and of plain structs are stored as
one contiguous array and read in place; vectors of strings, objects and unions hold offsets to them. Only
single-dimensional arrays are supported; use nested lists (`List<List<T>>`) or a flat array with your own indexing
instead of `T[,]`.

A null collection is absent (`has_x()` is false); an empty one is present with `size() == 0`.

## Dictionaries

Dictionary keys can be integers, chars, enums or strings. The writer stores keys and values as two vectors, with the
keys sorted (strings by code point, which is the order of their UTF-8 bytes), so C++ finds a key by binary search:

```cpp
if (auto stock = shop.items().find("item-0042")) use(stock->count());
bool known = shop.prices().contains(7);
for (std::string_view key : shop.items().keys()) list(key);
```

`keys()` and `values()` give the two vectors, in key order: entry `i` is `keys()[i]` and `values()[i]`.

## Unions

A member whose type is an abstract class or an interface holds any of the concrete types that derive from it or
implement it:

```csharp
public abstract class Item { }
public sealed class Potion : Item { public int Heal; }
public sealed class Key : Item { public uint Door; }

public class Chest { public Item? Loot; }
```

```cpp
if (game::Potion potion = chest.loot().as_potion()) use(potion.heal());
else if (game::Key key = chest.loot().as_key()) use(key.door());

switch (chest.loot().type()) {
    case game::Item::Tag::Potion: ...
    case game::Item::Tag::None: ...  // the member was null
    default: ...                     // a type this reader does not know
}
```

Which types a union can hold:

- every public or internal, non-generic concrete class of the project that derives from the member's type;
- classes of other projects only when their project marks them with [`[Tessera]`](../attributes/tessera.md) or lists
  them in [`[assembly: TesseraRoot]`](../attributes/tessera-root.md);
- exactly the listed types, when [`[TesseraUnion(typeof(A), typeof(B))]`](../attributes/tessera-union.md) is on the
  member or on the base type.

Each member type has a tag: a 32-bit hash of its type name, without the namespace. Renaming a class therefore changes
its tag, so old buffers would read it as an unknown type; keep the old tag with `[Tessera("OldName")]`. Two types with
the same name in different namespaces collide ([TESSERA004](../diagnostics/TESSERA004.md)), and a name whose hash is 0
is reserved for "no value" ([TESSERA009](../diagnostics/TESSERA009.md)); give one of them a `[Tessera("Name")]`.

## Types Tessera does not support

These are reported at compile time ([TESSERA001](../diagnostics/TESSERA001.md)) with a suggested replacement:

| C# type | Why | Store instead |
|---|---|---|
| `DateTime` | no C++ equivalent | `DateTime.Ticks` (or `ToBinary()`) as a `long` |
| `TimeSpan` | no C++ equivalent | `TimeSpan.Ticks` as a `long` |
| `DateTimeOffset` | no C++ equivalent | `UtcTicks` as a `long`, and the offset separately |
| `Guid` | no C++ equivalent | a string, or your own 16-byte plain struct |
| `decimal` | no C++ equivalent | a `double`, a scaled `long`, or a string |
| `nint`, `nuint` | no fixed size on the wire | `int` or `long` |
| `Half`, `Int128`, `UInt128` | no portable C++ equivalent | `float`, two `long`s |
| multi-dimensional arrays (`T[,]`) | only single-dimensional arrays | nested lists, or a flat array |
| delegates, `object`, `dynamic` | not data | a model type |

Next: [Writing in .NET](writing.md).
