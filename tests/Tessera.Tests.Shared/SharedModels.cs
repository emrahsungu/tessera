using Tessera;

namespace SharedModels;

public enum Rarity : byte
{
    Common,
    Rare,
    Epic,
}

public struct Point
{
    public float X;
    public float Y;
}

/// <summary>Defaults come from initializers, which other assemblies' generators only see through [assembly: TesseraDefault].</summary>
[Tessera]
public class Badge
{
    public string? Title;
    public short Level = 5;
    public Rarity Rarity = Rarity.Rare;
    public Point Where;
}

/// <summary>A union whose members are declared here; other assemblies' generators must find them.</summary>
[Tessera]
public abstract class Reward
{
}

[Tessera]
public sealed class Gold : Reward
{
    public int Amount = 10;
}

[Tessera]
public sealed class Gem : Reward
{
    public Rarity Kind;
}
