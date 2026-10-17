# Tessera.Serialization

Binary buffers from plain .NET objects, read in place by C++20 and .NET: no schema file, no parsing, and no
allocations on the read side. This package holds the .NET library, the source generator that writes a serializer for
each of your classes, the tool that writes their C++ headers after each build, and the header-only C++20 library those
headers use.

## Example

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

Save one. That call is all the source generator needs: it writes a serializer for `Monster` and for every type
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

```
328 bytes
```

The same build writes a C++ header for each class. C++ verifies the buffer once, then reads it where it lies:

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

```
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
```

.NET reads the buffer in place too, through a read type generated for each class (`MonsterView` for `Monster`), or
creates the objects again:

```csharp
MonsterView view = MonsterView.Read(buffer);   // checks the buffer once; copies nothing
Vec3 p = view.Position;
Console.WriteLine($"{view.Name}: hp {view.Hp}, mana {view.Mana}, position {p.X} {p.Y} {p.Z}");
foreach (WeaponView w in view.Weapons) Console.WriteLine($"  {w.Name}: damage {w.Damage}");

Monster copy = TesseraSerializer.Deserialize<Monster>(buffer);
Console.WriteLine($"{copy.Name} again, with {copy.Weapons!.Count} weapons");
```

```
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
Orc again, with 2 weapons
```

## Setup

```shell
dotnet add package Tessera.Serialization --prerelease
```

Then choose where the C++ headers go, in your project file:

```xml
<PropertyGroup>
  <TesseraCppOutputDir>cpp/generated</TesseraCppOutputDir>
</PropertyGroup>
```

After each build, that folder holds a header for each class and the C++ runtime headers: add it to your C++ include
path. Buffers must start at an 8-byte aligned address, as memory from `new`, `malloc` or a `std::vector<std::uint64_t>`
does. Without `TesseraCppOutputDir`, no headers are written to disk.

## Why Tessera

- **Your classes are the schema.** No separate schema file and no required attributes: classes, records, structs,
  enums, nullable values, lists, dictionaries, and fields declared as an abstract class or interface, which can hold
  any derived class. Types that can't be saved are reported at compile time, with a suggested replacement.
- **No parsing.** C++ and .NET validate a buffer once; after that, every field read is a plain memory load: nothing
  is parsed, copied or allocated.
- **Versioning by name.** Add, remove, reorder and rename fields, with no field numbers to manage: older and newer
  programs read each other's data.
- **Compact.** Fields that are null or equal to their default take no space, and equal strings are stored once.
- **Measured.** Benchmarked against FlatBuffers, MessagePack, Protocol Buffers, JSON and MemoryPack, in .NET and C++:
  see the [results](https://github.com/emrahsungu/tessera#4-performance).

## Documentation

- [README](https://github.com/emrahsungu/tessera#readme) and the
  [documentation](https://github.com/emrahsungu/tessera/blob/main/docs/README.md)
- [Installation](https://github.com/emrahsungu/tessera/blob/main/docs/guide/installation.md) and
  [Getting started](https://github.com/emrahsungu/tessera/blob/main/docs/guide/getting-started.md)
- [Supported types](https://github.com/emrahsungu/tessera/blob/main/docs/guide/supported-types.md),
  [attributes](https://github.com/emrahsungu/tessera/blob/main/docs/attributes/README.md) and
  [options](https://github.com/emrahsungu/tessera/blob/main/docs/options/README.md)
- [Reading in C++](https://github.com/emrahsungu/tessera/blob/main/docs/guide/reading-cpp.md) and
  [reading in .NET](https://github.com/emrahsungu/tessera/blob/main/docs/guide/reading-dotnet.md)
- [Changing your classes](https://github.com/emrahsungu/tessera/blob/main/docs/guide/versioning.md): which changes
  keep data saved earlier readable
- [Compiler messages](https://github.com/emrahsungu/tessera/blob/main/docs/diagnostics/README.md): every `TESSERA`
  error and warning, and how to fix it
- [Wire format](https://github.com/emrahsungu/tessera/blob/main/docs/FORMAT.md) and
  [benchmarks](https://github.com/emrahsungu/tessera/blob/main/docs/BENCHMARKS.md)

Tessera is a preview: the API and the wire format may change before 1.0. It is released under the
[Apache License 2.0](https://github.com/emrahsungu/tessera/blob/main/LICENSE).
