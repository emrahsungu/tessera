using System.Collections.Generic;

namespace Game;

// Plain classes: Program.cs serializes a Monster, which makes Monster a model, and every type it uses with it.

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
