# C++ API

Everything is in the `tessera` namespace, in `tessera/tessera.hpp` and `tessera/tessera_reader.hpp`, which every
generated header includes. Your classes' accessor classes are in the namespace of your C# classes
(`game::Monster` for `Game.Monster`, see [MSBuild settings](../guide/msbuild.md)).

The buffer must start at an 8-byte aligned address, as memory from `new`, `malloc` or a `std::vector<std::uint64_t>`
does. All reads are `const` and `noexcept`.

## Opening a buffer

### `tessera::Reader<T>`

Validates a buffer and gives you its top-level object.

```cpp
tessera::Reader<game::Save> reader(data, size);             // validate everything (the default)
tessera::Reader<game::Save> strict(data, size, options);    // with tessera::Options
tessera::Reader<game::Save> mine(data, size, tessera::trusted);  // header only: your own data
if (!reader) { log(tessera::to_string(reader.error())); return; }
game::Save save = reader.root();
```

| Member | What it does |
|---|---|
| `Reader(const void* data, std::size_t size, const Options& = {})` | opens and validates the buffer |
| `explicit operator bool()` | true when the buffer opened without an error |
| `error()` | the [`tessera::Error`](#tesseraerror) |
| `root()` | the top-level object (an empty accessor object if opening failed) |
| `exact_schema()` | true when the buffer was written with exactly the classes of this header; false when it came from another version and was converted |

A buffer written with another version of your classes is converted once, while opening, into memory the `Reader` owns
([Changing your classes](../guide/versioning.md)). **Keep the `Reader` alive as long as you use objects from it**,
and the buffer too.

### `tessera::verify<T>(data, size, Options = {})` → `Error`

Validates without keeping a `Reader`, and without converting a buffer of another version. Use it to check data you
don't read now: an upload on a server, an import tool, a test.

### `tessera::root_unchecked<T>(data)` → `T`

The top-level object, with no checks at all. Only for data your own program wrote with exactly these classes:
see [C++ read options](../options/cpp-read-options.md#tesseraroot_uncheckedtdata).

### `tessera::Options`, `tessera::trusted`

What to check when opening: see [C++ read options](../options/cpp-read-options.md).

### `tessera::Error`

| Value | Meaning |
|---|---|
| `None` | no error |
| `TooSmall` | shorter than the 16-byte header |
| `BadMagic` | not a Tessera buffer |
| `BadVersion` | written by a format version this code doesn't support |
| `SchemaMismatch` | written with other classes, and the buffer has no description of them (`IncludeSchema = false`), or the two can't be matched |
| `BadSchema` | the description of the classes in the buffer is malformed |
| `OutOfBounds` | something extends past the end of the buffer |
| `BadOffset` | a reference is zero, points backward or is misaligned, or a dictionary lacks its keys or values |
| `BadString` | a string has no terminating zero, or isn't valid UTF-8 (when checked) |
| `BadUnionTag` | a polymorphic field holds a type ID the buffer doesn't declare |
| `ReservedBits` | header bits that must be zero are set: written by a newer version of Tessera |
| `TooDeep` | nested deeper than `Options::max_depth` |
| `TooManyItems` | more objects and lists than `Options::max_items` |
| `Misaligned` | the buffer doesn't start at an 8-byte aligned address |

`tessera::to_string(error)` returns a short English description.

## Reading objects

### Accessor objects

For each of your classes, the generated header has an accessor class with one method per field. An accessor object
is a pointer into the buffer: copying it costs nothing, and reading a field is a memory load.

```cpp
game::Monster m = save.monsters()[0];
std::string_view name = m.name();             // strings: UTF-8, read in place
std::int16_t hp = m.hp();                     // numbers: the default if the field wasn't saved
std::optional<std::int32_t> mana = m.mana();  // nullable C# fields: std::optional
const game::Vec3& pos = m.position();         // plain structs: a reference into the buffer
bool saved = m.has_mana();                    // was the field saved?
```

| Member | What it does |
|---|---|
| `x()` | the value of field `X` ([what each C# type becomes](../guide/supported-types.md#what-each-c-type-becomes)) |
| `has_x()` | whether field `X` was saved: false when it was null, missing, or equal to its default |
| `explicit operator bool()` | false for an empty accessor object: a null object field, or the `root()` of a failed `Reader` |
| `tessera_data()` | the object's address in the buffer, for debugging or hashing |

Only nullable value types (`int?`, `bool?`, `Vec3?`, nullable enums) return `std::optional`. Strings, lists,
dictionaries and objects are never wrapped in it, nullable or not: a missing one reads as empty and converts to `false`
([Missing values](../guide/supported-types.md#missing-values-stdoptional-only-for-value-types)).

Reading a field of an empty accessor object returns the field's default, so a chain such as
`save.player().weapon().damage()` never crashes on a null in between.

Strings are `std::string_view`. A null C# string reads as a `std::string_view` with `data() == nullptr`; an empty
string has a non-null `data()`. Strings are followed by a zero byte in the buffer, so `data()` is also a C string.

### `tessera::Vector<T>`: lists and arrays

| Member | What it does |
|---|---|
| `size()`, `empty()` | the number of elements |
| `operator[](i)` | element `i`, without a range check |
| `at(i)` | element `i`, or a default value when `i` is out of range |
| `begin()`, `end()` | random-access iterators, for range-for and the standard algorithms |
| `explicit operator bool()` | false when the C# list was null; true for an empty list |

Elements are returned like fields: numbers by value, strings as `std::string_view`, plain structs as `const S&`,
objects as accessor objects. A list of nullable values (`List<int?>`) gives `std::optional` elements.

### `tessera::Map<K, V>`: dictionaries

Keys are stored sorted, so lookups are binary searches.

| Member | What it does |
|---|---|
| `find(key)` | `std::optional` of the value, empty when the key isn't there |
| `contains(key)` | whether the key is there |
| `keys()`, `values()` | the keys in sorted order, and the values in the same order: entry `i` is `keys()[i]`, `values()[i]` |
| `size()`, `empty()` | the number of entries |
| `explicit operator bool()` | false when the C# dictionary was null |

```cpp
if (auto stock = shop.items().find("sword")) buy(stock->price());
```

### Polymorphic fields

A field declared as a base class or an interface gets an accessor class with the base class's name. It tells which
derived class the value is, and gives you that class's accessor object:

| Member | What it does |
|---|---|
| `type()` | a generated `enum class Tag`: `Tag::None` when the field was null, otherwise the class's name (`Tag::Potion`) |
| `as_potion()`, `as_key()`, ... | the value as that class, or an empty accessor object if it is another class |
| `explicit operator bool()` | false when the field was null |
| `tag()` | the raw 32-bit type ID |

```cpp
switch (chest.loot().type()) {
    case game::Item::Tag::Potion: drink(chest.loot().as_potion()); break;
    case game::Item::Tag::Key: open(chest.loot().as_key()); break;
    case game::Item::Tag::None: break;   // the field was null
    default: break;                      // a class added in a newer version
}
```

## Debugging

### `tessera::to_json(value)` → `std::string`

Any accessor object, list or dictionary as JSON text, nested values included.

```cpp
std::cout << tessera::to_json(reader.root()) << '\n';
```

## Used by the generated code

`tessera::Table`, `tessera::TableBase`, `tessera::UnionBase`, `tessera::schema<T>`, `tessera::shared<S>`, the methods
`tessera_get`, `tessera_has` and `tessera_as`, the `tessera::Kind` enum and the `TESSERA_*` macros are what the
generated headers are built from. You don't need them, and they may change between versions.
