# Reading in C++

## The headers

After each build, `TesseraCppOutputDir` holds:

| File | Contents |
|---|---|
| `game/Monster.tessera.hpp` | one header per model type, in a folder per C++ namespace; it includes what the type needs |
| `<AssemblyName>.tessera.hpp` | one header per assembly that includes every model type |
| `tessera/tessera.hpp`, `tessera/tessera_reader.hpp` | the C++ runtime: header-only C++20, no other dependencies |

Add the folder to your include path and include what you read. The headers for one type are identical whichever
assembly wrote them, so including several assemblies' headers in one file works
([Models in several projects](multiple-projects.md)).

## Opening a buffer

```cpp
tessera::Reader<game::Monster> reader(data, size);  // verifies the buffer once
if (!reader) {
    std::cerr << tessera::to_string(reader.error()) << '\n';
    return;
}
game::Monster orc = reader.root();
```

The buffer must start at an 8-byte aligned address, as memory from `new`, `malloc` or a `std::vector<std::uint64_t>`
does. Read files into such memory, not into a `std::vector<char>`.

What the `Reader` does when it opens a buffer:

1. **Checks the header**: size, magic, format version, alignment.
2. **Compares the layout fingerprint** with the one compiled into the header. Equal: the buffer is read in place.
   Different: the reader matches members by name through the buffer's schema, and translates the buffer once into
   its own layout, in memory the `Reader` owns ([Schema evolution](schema-evolution.md)).
3. **Verifies the whole buffer** (unless you pass `tessera::trusted`): every object, offset, string, vector, union tag
   and the nesting depth. After that, every accessor is a plain load with no bounds check.

| Member | |
|---|---|
| `explicit operator bool()` | true when the buffer opened without an error |
| `error()` | the `tessera::Error` |
| `root()` | the root view (an empty view if opening failed) |
| `exact_schema()` | true when the buffer was written with exactly these models, so nothing was translated |

**Lifetimes.** Views point into the buffer, or into the `Reader`'s translated copy. Keep both alive while you use
views. A `Reader` and its views only read: any number of threads can read one buffer at the same time.

## Views

A view is one pointer. Copying it is free, and every accessor is `const` and `noexcept`.

```cpp
std::string_view name = orc.name();            // UTF-8; a null C# string has data() == nullptr
std::int16_t hp = orc.hp();                    // the default when it was not stored
std::optional<std::int32_t> mana = orc.mana(); // nullable members are std::optional
const game::Vec3& p = orc.position();          // plain structs: a reference into the buffer
bool stored = orc.has_mana();                  // has_x() for every member
```

### Vectors

`tessera::Vector<T>` reads in place: `size()`, `empty()`, `operator[]` (unchecked), `at(i)` (returns a default value
when `i` is out of range), and random-access iterators for range-for and the standard algorithms. A vector that was
null in C# converts to `false`; an empty one to `true`.

```cpp
for (game::Weapon w : orc.weapons()) use(w.name(), w.damage());
if (auto weapons = orc.weapons(); weapons && !weapons.empty()) best = weapons[0];
```

### Dictionaries

`tessera::Map<K, V>` holds its keys sorted, so `find` and `contains` binary-search:

| Member | |
|---|---|
| `find(key)` | `std::optional` of the value |
| `contains(key)` | whether the key is present |
| `keys()`, `values()` | the two vectors, in key order: entry `i` is `keys()[i]`, `values()[i]` |
| `size()`, `empty()` | the number of entries |

String keys are looked up with a `std::string_view`, so a literal or a `std::string` works.

### Unions

A union view has `type()`, which returns the generated `Tag` enum, and one `as_x()` per member type, which returns
an empty view (converting to `false`) unless the value has that type:

```cpp
if (game::Potion potion = chest.loot().as_potion()) use(potion.heal());
if (chest.loot().type() == game::Item::Tag::None) { /* null in C# */ }
```

A buffer written by a newer version of the models can hold a type this reader does not know: `type()` then returns a
value not in the enum, and every `as_x()` is empty.

## Verification options

```cpp
tessera::Options strict;
strict.utf8 = true;            // also validate UTF-8 in every string (default: false)
strict.max_depth = 64;         // nesting limit for objects and vectors (default: 128)
strict.max_items = 100'000;    // objects and vectors verified (default: 2^24)
tessera::Reader<game::Monster> checked(data, size, strict);
```

Verify buffers that come from outside your program: files a user can edit, the network, mods. In a buffer with
shared items (written with `Sharing.All`), the verifier checks each shared item once when walking every path would
take longer; that is the only case in which verification allocates (translating a buffer of another model version
allocates its copy).

To check a buffer without reading it, for example in an import tool:

```cpp
tessera::Error e = tessera::verify<game::Monster>(data, size);
```

## Trusted buffers

For buffers your own program wrote with exactly these models, verification can be skipped:

```cpp
tessera::Reader<game::Monster> trusted(data, size, tessera::trusted);  // header and schema checks only
game::Monster orc = tessera::root_unchecked<game::Monster>(data);       // no checks at all
```

`root_unchecked` is as cheap as opening a FlatBuffers buffer with `GetRoot`. Never use either on data that could have
been modified: an invalid offset in an unverified buffer reads out of bounds.

## Errors

| `tessera::Error` | Meaning |
|---|---|
| `None` | no error |
| `TooSmall` | shorter than the 16-byte header |
| `BadMagic` | not a Tessera buffer |
| `BadVersion` | written by an unsupported format version |
| `SchemaMismatch` | the fingerprint differs and the buffer has no schema (written with `IncludeSchema = false`), or the schemas cannot be matched |
| `BadSchema` | the schema section is malformed |
| `OutOfBounds` | an item extends past the end of the buffer |
| `BadOffset` | an offset is zero, backward or misaligned, or a dictionary lacks its keys or values |
| `BadString` | a string lacks its terminator, or is not valid UTF-8 (with `utf8 = true`) |
| `BadUnionTag` | a union tag the buffer's schema does not declare |
| `ReservedBits` | header bits that must be zero are set (written by a newer writer) |
| `TooDeep` | nesting deeper than `max_depth` |
| `TooManyItems` | more objects and vectors than `max_items` |
| `Misaligned` | the buffer does not start at an 8-byte aligned address |

`tessera::to_string(error)` gives a short description.

## Debugging

`tessera::to_json(view)` returns any view as JSON text, nested objects and vectors included:

```cpp
std::cout << tessera::to_json(orc) << '\n';
```

Next: [Schema evolution](schema-evolution.md).
