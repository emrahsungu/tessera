using System;
using System.IO;
using Game;
using Tessera;

// Writes monster.bin; cpp/main.cpp reads it through the generated header.
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
