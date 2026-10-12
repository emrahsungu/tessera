# Your first model

This page writes a buffer in .NET and reads it in C++. The finished project is
[samples/Quickstart](../../samples/Quickstart); run it to follow along.

## 1. The models

Your C# classes are the schema. There is no IDL file and no attribute to add:

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

## 2. Write a buffer

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

The `Serialize` call is all the source generator needs. Because the compiler knows that `orc` is a `Monster`, the
generator writes a serializer for `Monster` and for every type `Monster` uses: `Vec3`, `Weapon` and the list.
[Which types are models](models.md) lists the other ways a type becomes a model.

## 3. The generated C++ header

The same build writes one C++ header per type into `TesseraCppOutputDir` (see [Installation](installation.md)).
From `game/Monster.tessera.hpp`, shortened:

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

The namespace `Game` became `game`, and the members became `snake_case` accessors.
[MSBuild settings](msbuild.md) changes both.

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

    tessera::Reader<game::Monster> reader(data.data(), size);  // verifies the buffer once
    if (!reader) {
        std::cerr << "invalid buffer: " << tessera::to_string(reader.error()) << '\n';
        return 1;
    }

    game::Monster orc = reader.root();  // a view: reads in place, nothing is parsed or copied
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

Add `TesseraCppOutputDir` to the include path; the sample's `cpp/CMakeLists.txt` does exactly that.

Three things to notice:

- **`hp` was not stored**, because it equaled its default. C++ returned the default compiled into its header.
  `orc.has_hp()` tells you whether a value was stored.
- **A view is one pointer into the buffer.** Keep the buffer, and the `Reader`, alive while you use views.
- **Buffers must start at an 8-byte aligned address**, as memory from `new`, `malloc` or a
  `std::vector<std::uint64_t>` does. The `Reader` reports `Error::Misaligned` otherwise.

## 5. What the 328 bytes hold

| Part | Bytes | Contents |
|---|---:|---|
| Header | 16 | root offset, magic, format version, layout fingerprint |
| Data | 88 | the orc (28), the weapon list (12), two weapons (12 each) and three strings (8 each) |
| Schema | 224 | the hashed member names and the layouts of `Monster`, `Weapon`, `Vec3` and the list |

The schema lets readers built from older or newer versions of these classes read the buffer
([Schema evolution](schema-evolution.md)). Its size depends on the types, not on the data, so it only matters for
small buffers. When the writer and the reader are always built from the same classes, leave it out:

```csharp
byte[] small = TesseraSerializer.Serialize(orc, new TesseraOptions { IncludeSchema = false });  // 104 bytes
```

Next: [Which types are models](models.md).
