using System.Collections.Generic;

namespace Game;

// Plain classes: Program.cs serializes a Monster, which makes Monster a model, and every type it uses with it.

public enum Faction : byte { Neutral, Red, Blue }

public struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z) => (X, Y, Z) = (x, y, z);
}

public class Monster
{
    public string? Name;
    public short Hp = 100;                // equal to its default: not stored
    public int? Mana;                     // nullable: absence is visible in C++
    public Vec3 Position;                 // plain struct: stored inline
    public Faction Faction;
    public List<Weapon>? Weapons;
    public Item? Loot;                    // abstract: a union of the classes deriving from it
}

public class Weapon
{
    public string? Name;
    public int Damage;
}

public abstract class Item
{
}

public sealed class Potion : Item
{
    public int Heal;
}

public sealed class Key : Item
{
    public uint Door;
}
