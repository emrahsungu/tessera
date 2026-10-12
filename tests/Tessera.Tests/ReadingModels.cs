using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Tessera;

namespace TestModels.Reading;

/// <summary>Members that code outside the class sets in every way: Deserialize must set each one.</summary>
[Tessera]
public class Setters
{
    public int Field;
    public readonly int ReadOnlyField;
    public int Settable { get; set; }
    public int InitOnly { get; init; }
    public int PrivateSet { get; private set; }
    public int GetOnly { get; }
    public string? Text { get; protected set; }
    public required string Required { get; set; }
    [TesseraIgnore] public required string RequiredIgnored { get; set; }

    private int _backing;

    /// <summary>A setter with a body, which only the class can call.</summary>
    public int Custom
    {
        get => _backing;
        private set => _backing = value;
    }

    public Setters()
    {
    }

    [SetsRequiredMembers]
    public Setters(int readOnlyField, int getOnly, int privateSet, int custom, string text)
    {
        ReadOnlyField = readOnlyField;
        GetOnly = getOnly;
        PrivateSet = privateSet;
        Custom = custom;
        Text = text;
        Required = "required";
        RequiredIgnored = "ignored";
    }
}

/// <summary>No constructor without parameters: created through the one whose parameters are its members.</summary>
[Tessera]
public class Positional
{
    public Positional(string name, int count)
    {
        Name = name;
        Count = count;
        Constructed = true;
    }

    public string Name { get; }
    public int Count { get; }
    public double Extra { get; set; }
    [TesseraIgnore] public bool Constructed { get; }
}

/// <summary>A positional record, with one more property set after its primary constructor.</summary>
[Tessera]
public sealed record Point3(float X, float Y, float Z)
{
    public string? Label { get; init; }
}

/// <summary>A private constructor without parameters: run through an UnsafeAccessor.</summary>
[Tessera]
public class Hidden
{
    private Hidden()
    {
        Made = true;
    }

    public static Hidden Create(int value) => new() { Value = value };

    public int Value { get; private set; }
    [TesseraIgnore] public bool Made { get; }
}

/// <summary>No constructor that Deserialize can call: created without running one.</summary>
[Tessera]
public class Uncallable
{
    public Uncallable(Guid seed) => Seed = seed;

    [TesseraIgnore] public Guid Seed { get; }
    public int Value { get; set; }
    public string? Name { get; set; }
}

/// <summary>A struct holding a string (stored as an object), with a read-only field and a private setter.</summary>
[Tessera]
public struct Tagged
{
    public string? Tag;
    public readonly int Fixed;
    public int Count { get; private set; }

    public Tagged(string tag, int @fixed, int count)
    {
        Tag = tag;
        Fixed = @fixed;
        Count = count;
    }
}

[Tessera]
public class Boxes
{
    public Tagged One;
    public Tagged? Maybe;
    public Tagged? Nothing;
    public List<Tagged>? Many;
}

/// <summary>Lists and dictionaries declared as many types: each is created as its declared type.</summary>
[Tessera]
public class Collections
{
    public int[]? Array;
    public List<string>? List;
    public IReadOnlyList<int>? ReadOnlyList;
    public IList<string>? IList;
    public IEnumerable<long>? Enumerable;
    public ICollection<int>? Collection;
    public HashSet<string>? Set;
    public SortedSet<int>? Sorted;
    public Queue<int>? Queue;
    public Stack<int>? Stack;
    public LinkedList<int>? Linked;
    public ReadOnlyCollection<int>? ReadOnly;
    public ObservableCollection<string>? Observable;
    public int[][]? Jagged;
    public List<int[]>? ListOfArrays;
    public Vec3[]? Points;
    public Padded[]? Padded;
    public List<Vec3>? PointList;
    public Color[]? Colors;
    public bool[]? Flags;
    public Dictionary<string, int>? Dictionary;
    public IDictionary<int, string>? IDictionary;
    public IReadOnlyDictionary<string, string>? ReadOnlyDictionary;
    public SortedDictionary<int, int>? SortedDictionary;
    public SortedList<string, int>? SortedList;
    public ConcurrentDictionary<int, int>? Concurrent;
    public ReadOnlyDictionary<int, int>? Wrapped;

    // Stored alike, declared as different C# types: lists of lists, and dictionaries of lists.
    public List<List<int>>? ListOfLists;
    public Dictionary<string, int[]>? ArraysByName;
    public Dictionary<string, List<int>>? ListsByName;
    public List<Dictionary<string, int[]>>? Tables;
}

/// <summary>A base class whose property only it can set.</summary>
public abstract class Base
{
    public int BaseValue { get; private set; }

    public void SetBase(int value) => BaseValue = value;
}

[Tessera]
public sealed class Derived : Base
{
    public string? Own { get; init; }
}
