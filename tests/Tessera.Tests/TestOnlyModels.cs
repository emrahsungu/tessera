using System.Collections.Generic;
using System.ComponentModel;
using Tessera;

namespace TestModels.Behavior;

[Tessera]
public class CycleNode
{
    public string? Name;
    public CycleNode? Next;
}

[Tessera]
public class Defaults
{
    public int Plain;
    [TesseraKeepDefault] public int Kept;
    public int WithInitializer = 7;
    [DefaultValue(3)] public int WithAttribute = 3;
    public float Ratio = 0.5f;
    public bool Flag = true;
}

[Tessera]
public class Holder
{
    public List<string>? Names;
    public string? Text;
    public List<Leaf>? Leaves;
    public Leaf? First;
    public Leaf? Second;
    public byte[]? Bytes;
}

[Tessera]
public class Leaf
{
    public int Value;
    public string? Label;
}

[Tessera]
public class Chest
{
    public TestModels.Item? Any;
    [TesseraUnion(typeof(TestModels.Key))] public TestModels.Item? OnlyKey;
    [TesseraUnion(typeof(TestModels.Key))] public List<TestModels.Item>? Keys;
}

/// <summary>No attribute: a model because <c>WriterTests.ModelsNeedNoAttribute</c> serializes it.</summary>
public class Unmarked
{
    public string? Name;
    public int Count = 3;
    public UnmarkedChild? Child;
}

/// <summary>No attribute: a model because <see cref="Unmarked"/> uses it.</summary>
public class UnmarkedChild
{
    public float Value;
}

/// <summary>Uses model types of another assembly (tests/Tessera.Tests.Shared).</summary>
[Tessera]
public class Profile
{
    public string? Name;
    public SharedModels.Badge? Badge;
    public System.Collections.Generic.List<SharedModels.Badge>? Badges;
    public SharedModels.Reward? Reward;
}
