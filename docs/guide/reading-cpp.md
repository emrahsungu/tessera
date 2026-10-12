# Reading in C++

## The headers

After each build, the folder in `TesseraCppOutputDir` holds:

| File | Contents |
|---|---|
| `game/Monster.tessera.hpp` | one header per class, in a folder per C++ namespace; it includes everything the class needs |
| `<AssemblyName>.tessera.hpp` | one header that includes every class of the assembly |
| `tessera/tessera.hpp`, `tessera/tessera_reader.hpp` | the C++ library: header-only C++20, no other dependencies |

Add the folder to your include path, and include what you read.

## Opening a buffer

```cpp
tessera::Reader<game::Monster> reader(data, size);  // validates the buffer once
if (!reader) {
    std::cerr << tessera::to_string(reader.error()) << '\n';
    return;
}
game::Monster orc = reader.root();
```

The buffer must start at an 8-byte aligned address, as memory from `new`, `malloc` or a `std::vector<std::uint64_t>`
does. Load files into such memory, not into a `std::vector<char>`.

When a `Reader` opens a buffer, it:

1. **checks the 16-byte header**: size, format marker and version, alignment;
2. **compares the hash of the classes' layout** stored in the buffer with the one in the header. If they match, it
   reads the buffer as it is. If not (the buffer was written with another version of your classes), it matches the
   fields by name and converts the buffer once into its own layout, in memory the `Reader` owns
   ([Changing your classes](versioning.md));
3. **validates the whole buffer**, unless you ask it not to: every reference, length, string and type ID, and the
   nesting depth. After that, reading a field is a plain memory load without checks.

Choose how much to check, for example skipping validation for data your own program wrote:
[C++ read options](../options/cpp-read-options.md).

**Keep the buffer, and the `Reader`, alive** while you use objects read from them: those objects point into them.
Reading never modifies anything, so any number of threads can read one buffer at the same time.

## Reading fields

`reader.root()` returns an accessor object: a small class wrapping a pointer into the buffer, with one method per C#
field. Copying it costs nothing.

```cpp
std::string_view name = orc.name();            // UTF-8; a null C# string has data() == nullptr
std::int16_t hp = orc.hp();                    // the default, if the field wasn't saved
std::optional<std::int32_t> mana = orc.mana(); // nullable C# fields are std::optional
const game::Vec3& p = orc.position();          // plain structs: a reference into the buffer
bool saved = orc.has_mana();                   // has_x() for every field
```

Reading a field of a missing object returns the field's default, so `save.player().weapon().damage()` never crashes on
a null along the way.

### Lists and arrays

`tessera::Vector<T>` has `size()`, `empty()`, `operator[]` (no range check), `at(i)` (a default value when `i` is out
of range), and iterators for range-for and the standard algorithms. A null C# list converts to `false`; an empty list
to `true`.

```cpp
for (game::Weapon w : orc.weapons()) use(w.name(), w.damage());
if (auto weapons = orc.weapons(); weapons && !weapons.empty()) best = weapons[0];
```

### Dictionaries

`tessera::Map<K, V>` stores its keys sorted, so `find` and `contains` are binary searches:

| Method | Returns |
|---|---|
| `find(key)` | `std::optional` of the value |
| `contains(key)` | whether the key is there |
| `keys()`, `values()` | the keys in sorted order, and the values in the same order |
| `size()`, `empty()` | the number of entries |

String keys are looked up with a `std::string_view`, so a string literal or a `std::string` works.

### Fields declared as a base class or interface

`type()` says which class the value is, as a generated `enum class Tag`; `as_x()` returns the value as class `x`, or
an empty accessor (converting to `false`) if it is another class:

```cpp
if (game::Potion potion = chest.loot().as_potion()) use(potion.heal());
if (chest.loot().type() == game::Item::Tag::None) { /* the field was null */ }
```

A buffer written by a newer version of your classes can hold a class this program doesn't know: `type()` then returns
a value that isn't in the enum, and every `as_x()` is empty.

## Errors

| `tessera::Error` | Meaning |
|---|---|
| `TooSmall`, `BadMagic` | not a Tessera buffer, or truncated before the end of the header |
| `BadVersion` | written by a format version this code doesn't support |
| `SchemaMismatch` | written with other classes, and the buffer has no description of them (saved with `IncludeSchema = false`) |
| `OutOfBounds`, `BadOffset`, `BadString`, `BadUnionTag`, `BadSchema` | corrupted (or forged) data |
| `ReservedBits` | written by a newer version of Tessera |
| `TooDeep`, `TooManyItems` | over the limits in `tessera::Options` |
| `Misaligned` | the buffer doesn't start at an 8-byte aligned address |

The [C++ API](../api/cpp.md#tesseraerror) describes each one.

## Debugging

`tessera::to_json(value)` returns any object, list or dictionary as JSON text:

```cpp
std::cout << tessera::to_json(orc) << '\n';
```

Next: [Changing your classes](versioning.md).
