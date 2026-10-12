# Getting started

This page saves an object in C# and reads it in C++. The finished project is
[samples/Quickstart](../../samples/Quickstart).

## 1. Your classes

You don't write a separate schema file: Tessera uses your C# classes as they are.

```csharp
namespace Game;

public class Monster
{
    public string? Name;
    public short Hp = 100;            // equal to its default value: not written
    public int? Mana;                 // nullable: C++ can tell whether it was set
    public Vec3 Position;             // a plain struct: stored inside the object
    public List<Weapon>? Weapons;
}

public class Weapon { public string? Name; public int Damage; }

public record struct Vec3(float X, float Y, float Z);
```

## 2. Save an object

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

You don't register the class anywhere. During compilation, the source generator sees the `Serialize` call, knows that
`orc` is a `Monster`, and writes a serializer for `Monster` and for the types its fields use: `Vec3`, `Weapon` and the
list. [Which types get generated code](generated-types.md) explains the rules.

## 3. The generated C++ header

The same build writes a C++ header for each class into `TesseraCppOutputDir` ([Installation](installation.md)). Part
of `game/Monster.tessera.hpp`:

```cpp
class Monster final : public tessera::Table<Monster> {
public:
    std::string_view            name() const noexcept;      // string? Name
    std::int16_t                hp() const noexcept;        // short Hp = 100
    std::optional<std::int32_t> mana() const noexcept;      // int? Mana
    const Vec3&                 position() const noexcept;  // Vec3 Position
    tessera::Vector<Weapon>     weapons() const noexcept;   // List<Weapon>? Weapons
    // has_name(), has_hp() ...: whether each field was saved
};
```

The C# namespace `Game` became the C++ namespace `game`, and the fields became methods in `snake_case`;
[MSBuild settings](msbuild.md) can change both.

## 4. Read it in C++

```cpp
#include "game/Monster.tessera.hpp"

#include <cstdint>
#include <fstream>
#include <iostream>
#include <vector>

int main() {
    // Load the file into 8-byte aligned memory: a std::vector<std::uint64_t> is.
    std::ifstream file("monster.bin", std::ios::binary | std::ios::ate);
    const auto size = static_cast<std::size_t>(file.tellg());
    std::vector<std::uint64_t> data((size + 7) / 8);
    file.seekg(0);
    file.read(reinterpret_cast<char*>(data.data()), static_cast<std::streamsize>(size));

    tessera::Reader<game::Monster> reader(data.data(), size);  // validates the buffer once
    if (!reader) {
        std::cerr << "invalid buffer: " << tessera::to_string(reader.error()) << '\n';
        return 1;
    }

    game::Monster orc = reader.root();  // reads the bytes in place: nothing is parsed or copied
    const game::Vec3& p = orc.position();
    std::cout << orc.name() << ": hp " << orc.hp() << ", mana " << orc.mana().value_or(0)
              << ", position " << p.x << ' ' << p.y << ' ' << p.z << '\n';
    for (game::Weapon w : orc.weapons()) std::cout << "  " << w.name() << ": damage " << w.damage() << '\n';
}
```

```text
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
```

Add `TesseraCppOutputDir` to your C++ include path; the sample's `cpp/CMakeLists.txt` shows how.

Three things to notice:

- **`hp` wasn't saved**, because it equaled its default value. C++ returned the default compiled into the header;
  `orc.has_hp()` would return false.
- **`orc` is a pointer into the buffer**, wrapped in a class. Keep the buffer, and the `Reader`, alive while you use it.
- **The buffer must start at an 8-byte aligned address**, as memory from `new`, `malloc` or a
  `std::vector<std::uint64_t>` does; the `Reader` reports `Error::Misaligned` otherwise.

## 5. What the 328 bytes hold

| Part | Bytes | Contents |
|---|---:|---|
| Header | 16 | where the top-level object starts, a format marker and version, and a hash of the classes' layout |
| Data | 88 | the orc (28), the weapon list (12), two weapons (12 each) and three strings (8 each) |
| Schema | 224 | a description of `Monster`, `Weapon`, `Vec3` and the list: each field's name (as a hash) and type |

The description lets a program built from an older or newer version of these classes read the buffer
([Changing your classes](versioning.md)). Its size depends on the classes, not the data, so it only matters for small
buffers. When the writer and the reader are always built from the same source, leave it out
([`IncludeSchema`](../options/include-schema.md)):

```csharp
byte[] small = TesseraSerializer.Serialize(orc, new TesseraOptions { IncludeSchema = false });  // 104 bytes
```

Next: [Which types get generated code](generated-types.md).
