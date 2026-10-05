using System;
using System.Collections.Generic;
using Tessera;

namespace TestModels;

public enum Color : byte { Red, Green, Blue }

[Flags]
public enum Abilities : uint { None = 0, Fly = 1, Swim = 2, Burrow = 4 }

public struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

[TesseraShared]
public struct Rgba
{
    public byte R, G, B, A;
}

/// <summary>A reference to another object: component name hash, index and id.</summary>
public struct ObjectRef
{
    public uint ComponentName;
    public short Index;
    public short ObjectRefId;
}

public struct Padded
{
    public byte Tag;
    public double Value;
    public bool Flag;
}

[Tessera]
public class Monster
{
    public string? Name;
    public short Hp = 100;
    public short? Mana;
    public Vec3 Pos;
    public Vec3? Velocity;
    public Color Color = Color.Blue;
    public bool Friendly;
    public bool? Boss;
    public List<Weapon>? Weapons;
    public int[]? Inventory;
    public List<string?>? Tags;
    public Weapon? Equipped;
    public double Score;
    public long Id;
    public Item? Loot;
    public List<Item?>? Bag;
    public List<Vec3>? Path;
    public List<List<int>?>? Grid;
    public Abilities Abilities;
    public char Initial;
    public byte[]? Blob;
    [TesseraName("Speed")] public float MoveSpeed;
    public Rgba Tint;
    public ObjectRef Ref;
    public Padded Odd;
    public IReadOnlyList<double>? Samples { get; set; }
    public HashSet<string>? Unique { get; set; }
    public ulong Big { get; init; }
    public sbyte Tiny { get; set; }
    public List<bool>? Switches;
}

[Tessera]
public class Weapon
{
    public string? Name { get; set; }
    public short Damage { get; set; }
}

[Tessera]
public abstract class Item
{
    public string? Label;
}

[Tessera]
public sealed class Potion : Item
{
    public int Heal;
}

[Tessera]
public sealed class Key : Item
{
    public uint Door;
    public bool Golden;
}

[Tessera]
public sealed record Node(string Name, List<Node>? Children);

[Tessera]
public class Empty
{
}

/// <summary>Lists of strings; with Sharing.All, equal lists are stored once.</summary>
[Tessera]
public class TagCloud
{
    public List<List<string>>? Groups;
}

/// <summary>Fixed cells ([TesseraKeepDefault] values) of every alignment, padding before an 8-aligned cell, and a
/// non-zero default (an absent particle must still read X as 1.5).</summary>
[Tessera]
public class Particle
{
    [TesseraKeepDefault] public long Id;
    [TesseraKeepDefault] public float X = 1.5f;
    [TesseraKeepDefault] public Vec3 Velocity;
    [TesseraKeepDefault] public short Kind;
    [TesseraKeepDefault] public byte Flags;
    [TesseraKeepDefault] public Color Tint = Color.Blue;
    [TesseraKeepDefault] public bool Visible;   // bools stay in the header
    public double? Mass;
    public string? Name;
    public int Life = 10;
}

[Tessera]
public class Swarm
{
    public List<Particle>? Particles;
    public Particle? Leader;
    public Particle? Missing;
}

/// <summary>Dictionaries with every kind of key, and values of every kind of element.</summary>
[Tessera]
public class Catalog
{
    public Dictionary<string, int>? Stock;
    public Dictionary<int, string>? Names;
    public SortedDictionary<Color, Weapon>? ByColor;
    public IReadOnlyDictionary<long, List<string>>? Tags;
    public Dictionary<char, Vec3>? Points;
    public Dictionary<string, Dictionary<string, int>>? Nested;
    public Dictionary<int, int>? Empty;
    public Dictionary<int, int>? Missing;
}

/// <summary>Vectors of nullable values: presence bits, then every value.</summary>
[Tessera]
public class Readings
{
    public List<int?>? Ints;
    public double?[]? Doubles;
    public List<bool?>? Flags;
    public List<Color?>? Colors;
    public List<Vec3?>? Points;
    public List<List<short?>>? Nested;
    public Dictionary<string, long?>? Optional;
}