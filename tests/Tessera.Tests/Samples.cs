using System.Collections.Generic;
using TestModels;

namespace Tessera.Tests;

/// <summary>Sample values shared by the .NET tests and the C++ interop tests.</summary>
public static class Samples
{
    public static Monster Full() => new()
    {
        Name = "Orc \"Grunt\" ünïcødé ✓",
        Hp = 300,
        Mana = 0,
        Pos = new Vec3(1, 2, 3),
        Velocity = new Vec3(-0f, float.MaxValue, float.Epsilon),
        Color = Color.Red,
        Friendly = true,
        Boss = false,
        Weapons = new List<Weapon> { new() { Name = "Axe", Damage = 9 }, new() { Name = "Bow", Damage = -3 }, new() { Name = "Axe", Damage = 9 } },
        Inventory = new[] { 1, 2, 3, int.MinValue, int.MaxValue },
        Tags = new List<string?> { "a", null, "", "a" },
        Equipped = new Weapon { Name = "Sword", Damage = 12 },
        Score = double.NaN,
        Id = long.MinValue,
        Loot = new Key { Label = "gate", Door = 7, Golden = true },
        Bag = new List<Item?> { new Potion { Label = "heal", Heal = 25 }, null, new Key { Label = "back", Door = 1 } },
        Path = new List<Vec3> { new(0, 0, 0), new(1, 1, 1) },
        Grid = new List<List<int>?> { new() { 1, 2 }, null, new() },
        Abilities = Abilities.Fly | Abilities.Burrow,
        Initial = 'Ж',
        Blob = new byte[] { 0, 255, 128 },
        MoveSpeed = 4.5f,
        Tint = new Rgba { R = 1, G = 2, B = 3, A = 4 },
        Ref = new ObjectRef { ComponentName = 0xDEADBEEF, Index = -1, ObjectRefId = 5 },
        Odd = new Padded { Tag = 7, Value = -2.5, Flag = true },
        Samples = new List<double> { 0.1, -0.0 },
        Unique = new HashSet<string> { "x" },
        Big = ulong.MaxValue,
        Tiny = -128,
        Switches = new List<bool> { true, false, true },
    };

    /// <summary>Everything at its default: only defaults-that-differ and non-null references are stored.</summary>
    public static Monster Sparse() => new() { Hp = 100, Color = Color.Blue };

    public static Node Tree() => new("root", new List<Node>
    {
        new("a", null),
        new("b", new List<Node> { new("c", new List<Node>()) }),
        new("a", null),
    });

    /// <summary>
    /// <paramref name="n"/> nodes, each the only child of the previous one; the last has an empty child list. Every node
    /// and every child list is a nesting level, so this is 2n levels deep.
    /// </summary>
    public static Node Chain(int n)
    {
        var node = new Node("leaf", new List<Node>());
        for (int i = 1; i < n; i++) node = new Node("n" + i, new List<Node> { node });
        return node;
    }

    /// <summary>
    /// A node whose two children are the same node, <paramref name="levels"/> times over. With Sharing.All the buffer
    /// stores each level once; walked as a tree it has about 2^(levels + 2) items. 2 * levels + 2 nesting levels.
    /// </summary>
    public static Node Dag(int levels)
    {
        var node = new Node("x", new List<Node>());
        for (int i = 0; i < levels; i++) node = new Node("x", new List<Node> { node, node });
        return node;
    }

    /// <summary>
    /// Dag(8) reached twice: right below the root (depth 2) and at the end of a 5-node chain (depth 12), so the deepest
    /// item is at depth 29 on the second path only. <paramref name="deepFirst"/> puts the chain first.
    /// </summary>
    public static Node DagTwice(bool deepFirst)
    {
        var dag = Dag(8);
        var chain = dag;
        for (int i = 0; i < 5; i++) chain = new Node("c" + i, new List<Node> { chain });
        return new Node("r", deepFirst ? new List<Node> { chain, dag } : new List<Node> { dag, chain });
    }

    /// <summary>One list of 2,000 strings in 2,000 places: with Sharing.All, stored once, but 4 million strings deep.</summary>
    public static TagCloud SharedTags()
    {
        var tags = new List<string>();
        for (int i = 0; i < 2000; i++) tags.Add("tag" + i);
        var groups = new List<List<string>>();
        for (int i = 0; i < 2000; i++) groups.Add(tags);
        return new TagCloud { Groups = groups };
    }

    /// <summary>Particles with fixed cells: every value set, all defaults, a leader, and no "missing" one.</summary>
    public static Swarm Swarm() => new()
    {
        Particles = new List<Particle>
        {
            new() { Id = long.MinValue, X = -0.0f, Velocity = new Vec3(1, 2, 3), Kind = -7, Flags = 0xFF, Tint = Color.Red, Visible = true, Mass = 2.5, Name = "p0", Life = 0 },
            new(),
        },
        Leader = new Particle { Id = 42, Name = "lead" },
    };

    /// <summary>
    /// Dictionaries, written with keys unsorted. "\U0001F600" (a surrogate pair) sorts after "�" by code point
    /// (and UTF-8 bytes) but before it in ordinal UTF-16 order.
    /// </summary>
    public static Catalog Catalog() => new()
    {
        Stock = new Dictionary<string, int> { ["pear"] = 3, ["apple"] = 5, ["\U0001F600 smile"] = 9, ["� replacement"] = 1, ["zebra"] = 0, [""] = 7 },
        Names = new Dictionary<int, string> { [3] = "three", [-1] = "minus one", [int.MaxValue] = "max", [0] = "zero" },
        ByColor = new SortedDictionary<Color, Weapon> { [Color.Blue] = new Weapon { Name = "Bow", Damage = 2 }, [Color.Red] = new Weapon { Name = "Axe", Damage = 9 } },
        Tags = new Dictionary<long, List<string>> { [10] = new List<string> { "a", "b" }, [5] = new List<string>() },
        Points = new Dictionary<char, Vec3> { ['z'] = new Vec3(1, 2, 3), ['a'] = new Vec3(4, 5, 6) },
        Nested = new Dictionary<string, Dictionary<string, int>> { ["x"] = new Dictionary<string, int> { ["y"] = 1 } },
        Empty = new Dictionary<int, int>(),
    };

    /// <summary>Nullable elements: 40 ints (two presence words), every third one null, and one of each other kind.</summary>
    public static Readings Readings()
    {
        var ints = new List<int?>();
        for (int i = 0; i < 40; i++) ints.Add(i % 3 == 0 ? null : i * 7);
        return new Readings
        {
            Ints = ints,
            Doubles = new double?[] { 1.5, null, -0.0 },
            Flags = new List<bool?> { true, null, false },
            Colors = new List<Color?> { Color.Red, null, Color.Blue },
            Points = new List<Vec3?> { new Vec3(1, 2, 3), null },
            Nested = new List<List<short?>> { new() { 1, null }, new(), new() { null } },
            Optional = new Dictionary<string, long?> { ["a"] = 1, ["b"] = null },
        };
    }

    /// <summary>500 nodes named by one 20 KB string (non-ASCII), which the default sharing stores once.</summary>
    public static Node SharedLongString()
    {
        string name = string.Concat(System.Linq.Enumerable.Repeat("héllo wörld ✓ ", 1300));
        var nodes = new List<Node>();
        for (int i = 0; i < 500; i++) nodes.Add(new Node(name, null));
        return new Node("root", nodes);
    }

    public static TestModels.V1.Player PlayerV1() => new()
    {
        Name = "Ann",
        Level = 12,
        Health = 0.75f,
        Online = true,
        Items = new List<TestModels.V1.Item> { new() { Id = "potion", Count = 3 }, new() { Id = "key", Count = 1 } },
        Stats = new TestModels.V1.Stats { Str = 5, Dex = 7 },
        Removed = 99,
        KindChanged = -2,
        Favorite = new TestModels.V1.Item { Id = "key", Count = 1 },
        Scores = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 },
    };

    /// <summary>The data of <see cref="PlayerV1"/> in the reordered V3 model.</summary>
    public static TestModels.V3.Player PlayerV3() => new()
    {
        Name = "Ann",
        Level = 12,
        Health = 0.75f,
        Online = true,
        Items = new List<TestModels.V3.Item> { new() { Id = "potion", Count = 3 }, new() { Id = "key", Count = 1 } },
        Stats = new TestModels.V3.Stats { Str = 5, Dex = 7 },
        Removed = 99,
        KindChanged = -2,
        Favorite = new TestModels.V3.Item { Id = "key", Count = 1 },
        Scores = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 },
    };

    public static TestModels.V2.Player PlayerV2() => new()
    {
        Online = false,
        DisplayName = "Bob",
        Gold = 1000,
        Level = 3,
        Health = 1,
        Items = new List<TestModels.V2.Item> { new() { Rarity = 2, Count = 5, Id = "gem" } },
        Stats = new TestModels.V2.Stats { Str = 1, Dex = 2 },
        KindChanged = 123456,
        Title = "Sir",
        Scores = new Dictionary<string, long> { ["x"] = 10 },
    };

    public static Wide WideDense()
    {
        var w = new Wide();
        var t = typeof(Wide);
        for (int i = 0; i < 40; i++) t.GetField($"F{i:D2}")!.SetValue(w, i * 1000 + 1);
        for (int i = 0; i < 20; i++) t.GetField($"B{i:D2}")!.SetValue(w, i % 3 == 0);
        for (int i = 0; i < 8; i++) t.GetField($"S{i}")!.SetValue(w, "s" + i);
        for (int i = 0; i < 4; i++) t.GetField($"D{i}")!.SetValue(w, i + 0.5);
        return w;
    }

    public static Wide WideSparse() => new() { F07 = 7, F39 = -39, B19 = true, S3 = "x", D2 = 2.5 };
}
