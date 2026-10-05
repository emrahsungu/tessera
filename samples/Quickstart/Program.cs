using System;
using System.Collections.Generic;
using System.IO;
using Game;
using Tessera;

// Writes monster.bin; cpp/main.cpp reads it through the generated header.
var monster = new Monster
{
    Name = "Orc",
    Mana = 25,
    Position = new Vec3(1, 2, 3),
    Faction = Faction.Red,
    Weapons = new List<Weapon> { new() { Name = "Axe", Damage = 9 }, new() { Name = "Bow", Damage = 4 } },
    Loot = new Potion { Heal = 50 },
};

byte[] bytes = TesseraSerializer.Serialize(monster);
string path = Path.GetFullPath(args.Length > 0 ? args[0] : "monster.bin");
File.WriteAllBytes(path, bytes);
Console.WriteLine($"wrote {bytes.Length} bytes to {path}");
