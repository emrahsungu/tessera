using System;
using System.IO;
using Game;
using Tessera;

// Writes monster.bin, which cpp/main.cpp reads through the generated header, then reads it back in .NET.
var orc = new Monster
{
    Name = "Orc",
    Mana = 25,
    Position = new Vec3(1, 2, 3),
    Weapons = [new() { Name = "Axe", Damage = 9 }, new() { Name = "Bow", Damage = 4 }],
};

byte[] buffer = TesseraSerializer.Serialize(orc);
File.WriteAllBytes(args.Length > 0 ? args[0] : "monster.bin", buffer);
Console.WriteLine($"{buffer.Length} bytes");

// Read it in place: a view of the buffer, nothing copied.
MonsterView view = MonsterView.Read(buffer);
Vec3 p = view.Position;
Console.WriteLine($"{view.Name}: hp {view.Hp}, mana {view.Mana}, position {p.X} {p.Y} {p.Z}");
foreach (WeaponView w in view.Weapons) Console.WriteLine($"  {w.Name}: damage {w.Damage}");

// Or create the objects again.
Monster copy = TesseraSerializer.Deserialize<Monster>(buffer);
Console.WriteLine($"{copy.Name} again, with {copy.Weapons!.Count} weapons");
