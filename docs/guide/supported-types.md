# Supported types

## Which fields are saved

Public fields and properties are saved, and internal ones declared in the same assembly. A property needs a setter,
or must be an auto-property. To leave a field out, mark it [`[TesseraIgnore]`](../attributes/tessera-ignore.md)
(`[IgnoreDataMember]` and `[NonSerialized]` work too).

The order of fields doesn't matter: you can reorder them, or turn fields into properties and back, without changing a
single byte. A class can have up to 4,096 saved fields ([TESSERA005](../diagnostics/TESSERA005.md)).

## What each C# type becomes

| C# | In C++, the field's method returns |
|---|---|
| `bool`, `sbyte` … `ulong`, `float`, `double` | the same type (`bool`, `std::int8_t` … `std::uint64_t`, `float`, `double`) |
| `char` | `char16_t` |
| an enum (any integer base type, `[Flags]` too) | an `enum class` with the same names and values; flags enums also get `\|`, `&` and `~` |
| a nullable value, such as `int?` | `std::optional<std::int32_t>` |
| `string` | `std::string_view` (UTF-8); a null string has `data() == nullptr`, an empty one doesn't |
| a plain struct: only numbers, enums and other plain structs inside | a C++ struct with the same memory layout, as `const S&` into the buffer (a copy if it has a `bool` field) |
| a class, record, or a struct with references inside | an accessor class with one method per field |
| `T[]`, `List<T>`, `IReadOnlyList<T>`, any `IEnumerable<T>` | `tessera::Vector<T>`: `size()`, `operator[]`, range-for |
| `List<int?>` and other lists of nullable values | `tessera::Vector<std::optional<std::int32_t>>` |
| `List<List<T>>` and other nested lists | `tessera::Vector<tessera::Vector<T>>` |
| `Dictionary<K, V>`, `IReadOnlyDictionary<K, V>`, any `IDictionary<K, V>` | `tessera::Map<K, V>`: see [Dictionaries](#dictionaries) |
| an abstract class or an interface | an accessor that tells which derived class it holds: see [below](#fields-declared-as-a-base-class-or-interface) |

Every field also gets `has_x()`, which tells whether the field was saved. A field equal to its default value isn't
saved ([Default values](../attributes/default-value.md)), and reads as that default.

## Plain structs

A struct with only numbers, enums, `bool`, `char` and other plain structs inside is stored inside the object that
holds it, in its C# memory layout, and C++ reads it as a struct with the same layout: `const Vec3& p = orc.position();`
is one memory access, with nothing to decode. Use plain structs for vectors, colors, IDs and other small values.

They can't have an explicit layout (`[StructLayout(LayoutKind.Explicit)]`) or fixed-size buffers
([TESSERA006](../diagnostics/TESSERA006.md)), and one from another assembly must have public fields.

A large struct that repeats in many objects can be stored once and referenced: see
[`[TesseraShared]`](../attributes/tessera-shared.md).

## Strings

Strings are saved as UTF-8, with their length and a terminating zero byte, so `name().data()` is also a valid C
string. By default, equal strings are stored only once ([`Sharing`](../options/sharing.md)). C++ checks that strings
are valid UTF-8 only if you ask for it ([C++ read options](../options/cpp-read-options.md#utf8-default-false)).

## Lists and arrays

Arrays, lists and any other `IEnumerable<T>` become `tessera::Vector<T>` in C++. Lists of numbers and plain structs
are stored as one contiguous array and read in place; lists of strings, objects and polymorphic values store
references to them.

A null list reads as missing (`has_x()` is false, and the vector converts to `false`); an empty list is saved, with
`size() == 0`.

Only one-dimensional arrays are supported: instead of `T[,]`, use nested lists (`List<List<T>>`), or a flat array
indexed as `y * width + x`.

## Dictionaries

Keys can be integers, chars, enums or strings. A dictionary is saved as two arrays: the keys, sorted, and the values
in the same order. So C++ finds a key by binary search:

```cpp
if (auto stock = shop.items().find("item-0042")) use(stock->count());
bool known = shop.prices().contains(7);
for (std::string_view key : shop.items().keys()) list(key);
```

String keys are sorted by their UTF-8 bytes. Entry `i` is `keys()[i]` and `values()[i]`.

## Fields declared as a base class or interface

A field declared as an abstract class or an interface can hold any class that derives from it (or implements it):

```csharp
public abstract class Item { }
public sealed class Potion : Item { public int Heal; }
public sealed class Key : Item { public uint Door; }

public class Chest { public Item? Loot; }
```

In C++, `type()` tells which class the value is, and `as_potion()`, `as_key()` give you that class's accessor (an
empty one, converting to `false`, if the value is another class):

```cpp
if (game::Potion potion = chest.loot().as_potion()) use(potion.heal());
else if (game::Key key = chest.loot().as_key()) use(key.door());

switch (chest.loot().type()) {
    case game::Item::Tag::Potion: ...
    case game::Item::Tag::None: ...  // the field was null
    default: ...                     // a class this program doesn't know (added in a newer version)
}
```

**Which classes a field may hold:**

- every public or internal, non-generic class of the project that derives from the field's declared type;
- classes of other projects only when their project marks them [`[Tessera]`](../attributes/tessera.md) or lists them
  in [`[assembly: TesseraRoot]`](../attributes/tessera-root.md);
- or exactly the classes listed in [`[TesseraUnion(...)]`](../attributes/tessera-union.md).

**How the class is recorded:** the buffer stores a 32-bit ID for it, a hash of the class name without its namespace.
Renaming a class changes its ID, so data saved before would no longer load as that class: keep the old name with
`[Tessera("OldName")]`. Two classes with the same name in different namespaces get the same ID
([TESSERA004](../diagnostics/TESSERA004.md)); give one of them another name the same way.

## Types that can't be saved

These are reported at compile time ([TESSERA001](../diagnostics/TESSERA001.md)), with a suggested replacement:

| C# type | Why | Save instead |
|---|---|---|
| `DateTime` | C++ has no equivalent | `DateTime.Ticks` (or `ToBinary()`) as a `long` |
| `TimeSpan` | C++ has no equivalent | `TimeSpan.Ticks` as a `long` |
| `DateTimeOffset` | C++ has no equivalent | `UtcTicks` as a `long`, and the offset separately |
| `Guid` | C++ has no equivalent | a string, or your own 16-byte plain struct |
| `decimal` | C++ has no equivalent | a `double`, a scaled `long`, or a string |
| `nint`, `nuint` | their size differs between machines | `int` or `long` |
| `Half`, `Int128`, `UInt128` | no portable C++ equivalent | `float`, two `long`s |
| multi-dimensional arrays (`T[,]`) | only one-dimensional arrays are supported | nested lists, or a flat array |
| delegates, `object`, `dynamic` | not data | a class |

Next: [Writing in C#](writing.md).
