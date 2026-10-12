using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TestModels;
using TestModels.Behavior;
using TestModels.Reading;
using Xunit;
using V1 = TestModels.V1;
using V2 = TestModels.V2;
using V3 = TestModels.V3;

namespace Tessera.Tests;

/// <summary>TesseraSerializer.Deserialize: buffers read back as objects of the classes they were written from.</summary>
public class DeserializeTests
{
    private static readonly TesseraOptions ShareAll = new() { Sharing = Sharing.All };

    private static T RoundTrip<T>(T value, TesseraOptions? options = null) =>
        TesseraSerializer.Deserialize<T>(TesseraSerializer.Serialize(value, options));

    /// <summary>Written again, the objects give the same bytes: every member came back as it was.</summary>
    [Theory]
    [InlineData(Sharing.None)]
    [InlineData(Sharing.Strings)]
    [InlineData(Sharing.All)]
    public void EveryMemberReadsBack(Sharing sharing)
    {
        var options = new TesseraOptions { Sharing = sharing };
        void Same<T>(T value)
        {
            byte[] buffer = TesseraSerializer.Serialize(value, options);
            Assert.Equal(buffer, TesseraSerializer.Serialize(TesseraSerializer.Deserialize<T>(buffer), options));
        }

        Same(Samples.Full());
        Same(Samples.Sparse());
        Same(Samples.Tree());
        Same(Samples.Swarm());
        Same(Samples.Catalog());
        Same(Samples.Readings());
        Same(Samples.SharedTags());
        Same(Samples.WideDense());
        Same(Samples.WideSparse());
        Same(new Empty());
        Same(new Profile   // classes of another assembly
        {
            Name = "p",
            Badge = new SharedModels.Badge { Title = "b", Level = 7 },
            Badges = new() { new SharedModels.Badge { Title = "c" } },
            Reward = new SharedModels.Gold { Amount = 3 },
        });
        Same(new Defaults());
        Same(new Defaults { Plain = 1, Kept = 2, WithInitializer = 0, WithAttribute = 4, Ratio = 0, Flag = false });

        Monster m = TesseraSerializer.Deserialize<Monster>(TesseraSerializer.Serialize(Samples.Full(), options));
        Monster full = Samples.Full();
        Assert.Equal(full.Name, m.Name);
        Assert.Equal(full.Hp, m.Hp);
        Assert.Equal(full.Mana, m.Mana);
        Assert.Equal(full.Velocity, m.Velocity);
        Assert.Equal(full.Boss, m.Boss);
        Assert.Equal(full.Weapons!.Select(w => w.Name), m.Weapons!.Select(w => w.Name));
        Assert.Equal(full.Inventory, m.Inventory);
        Assert.Equal(full.Tags, m.Tags);
        Assert.IsType(full.Loot!.GetType(), m.Loot);
        Assert.Equal(full.Big, m.Big);   // an init-only property
    }

    [Fact]
    public void AbsentMembersGetTheirDefaults()
    {
        Monster m = TesseraSerializer.Deserialize<Monster>(TesseraSerializer.Serialize(new Monster { Hp = 100, Color = Color.Blue, Name = null }));
        Assert.Null(m.Name);
        Assert.Equal(100, m.Hp);
        Assert.Equal(Color.Blue, m.Color);
        Assert.Null(m.Weapons);
        Assert.Null(m.Mana);

        // A list created by an initializer is still null when null was written.
        var defaults = new Defaults { Plain = 0, WithInitializer = 7, Flag = true };
        Defaults d = RoundTrip(defaults);
        Assert.Equal(7, d.WithInitializer);
        Assert.Equal(0.5f, d.Ratio);
        Assert.True(d.Flag);
    }

    [Fact]
    public void MembersAreSetEveryWay()
    {
        var setters = new Setters(readOnlyField: 1, getOnly: 2, privateSet: 3, custom: 4, text: "protected") { Field = 5, Settable = 6, InitOnly = 7 };
        Setters s = RoundTrip(setters);
        Assert.Equal(1, s.ReadOnlyField);
        Assert.Equal(2, s.GetOnly);
        Assert.Equal(3, s.PrivateSet);
        Assert.Equal(4, s.Custom);
        Assert.Equal("protected", s.Text);
        Assert.Equal(5, s.Field);
        Assert.Equal(6, s.Settable);
        Assert.Equal(7, s.InitOnly);
        Assert.Equal("required", s.Required);
        Assert.Null(s.RequiredIgnored);   // not stored

        Positional p = RoundTrip(new Positional("p", 3) { Extra = 1.5 });
        Assert.Equal("p", p.Name);
        Assert.Equal(3, p.Count);
        Assert.Equal(1.5, p.Extra);
        Assert.True(p.Constructed);

        var point = new Point3(1, 2, 3) { Label = "a" };
        Assert.Equal(point, RoundTrip(point));

        Hidden h = RoundTrip(Hidden.Create(9));
        Assert.Equal(9, h.Value);
        Assert.True(h.Made);   // its private constructor ran

        Uncallable u = RoundTrip(new Uncallable(Guid.NewGuid()) { Value = 4, Name = "u" });
        Assert.Equal(4, u.Value);
        Assert.Equal("u", u.Name);
        Assert.Equal(Guid.Empty, u.Seed);   // created without a constructor

        var derived = new Derived { Own = "own" };
        derived.SetBase(5);
        Derived dr = RoundTrip(derived);
        Assert.Equal(5, dr.BaseValue);   // a private setter of the base class
        Assert.Equal("own", dr.Own);

        Tagged root = RoundTrip(new Tagged("root", 8, 9));   // a struct at the top
        Assert.Equal(("root", 8, 9), (root.Tag, root.Fixed, root.Count));

        Boxes b = RoundTrip(new Boxes { One = new Tagged("a", 1, 2), Maybe = new Tagged("b", 3, 4), Many = new() { new Tagged("c", 5, 6) } });
        Assert.Equal(("a", 1, 2), (b.One.Tag, b.One.Fixed, b.One.Count));
        Assert.Equal(("b", 3, 4), (b.Maybe!.Value.Tag, b.Maybe.Value.Fixed, b.Maybe.Value.Count));
        Assert.Null(b.Nothing);
        Assert.Equal(("c", 5, 6), (b.Many![0].Tag, b.Many[0].Fixed, b.Many[0].Count));
    }

    [Fact]
    public void ListsAndDictionariesKeepTheirTypes()
    {
        var c = new Collections
        {
            Array = new[] { 1, 2, 3 },
            List = new() { "a", "b" },
            ReadOnlyList = new List<int> { 4, 5 },
            IList = new List<string> { "x" },
            Enumerable = new long[] { 6, 7 },
            Collection = new List<int> { 8 },
            Set = new() { "s1", "s2" },
            Sorted = new() { 3, 1, 2 },
            Queue = new(new[] { 1, 2, 3 }),
            Stack = new(new[] { 1, 2, 3 }),
            Linked = new(new[] { 9, 8 }),
            ReadOnly = new(new[] { 7, 6 }),
            Observable = new() { "o" },
            Jagged = new[] { new[] { 1 }, new[] { 2, 3 } },
            ListOfArrays = new() { new[] { 4, 5 } },
            Points = new[] { new Vec3(1, 2, 3) },
            Padded = new[] { new Padded { Tag = 1, Value = 2.5, Flag = true } },
            PointList = new() { new Vec3(4, 5, 6) },
            Colors = new[] { Color.Green, Color.Red },
            Flags = new[] { true, false, true },
            Dictionary = new() { ["a"] = 1 },
            IDictionary = new Dictionary<int, string> { [1] = "one" },
            ReadOnlyDictionary = new Dictionary<string, string> { ["k"] = "v" },
            SortedDictionary = new() { [2] = 20, [1] = 10 },
            SortedList = new() { ["b"] = 2, ["a"] = 1 },
            Concurrent = new(new[] { KeyValuePair.Create(1, 2) }),
            Wrapped = new(new Dictionary<int, int> { [5] = 50 }),
            ListOfLists = new() { new() { 1, 2 } },
            ArraysByName = new() { ["x"] = new[] { 1, 2 } },
            ListsByName = new() { ["y"] = new() { 3 } },
            Tables = new() { new() { ["z"] = new[] { 4 } } },
        };
        Collections r = RoundTrip(c);
        Assert.Equal(c.Array, r.Array);
        Assert.Equal(c.List, r.List);
        Assert.Equal(c.ReadOnlyList, r.ReadOnlyList);
        Assert.Equal(c.IList, r.IList);
        Assert.Equal(c.Enumerable, r.Enumerable);
        Assert.Equal(c.Collection, r.Collection);
        Assert.IsType<HashSet<string>>(r.Set);
        Assert.True(c.Set.SetEquals(r.Set!));
        Assert.IsType<SortedSet<int>>(r.Sorted);
        Assert.Equal(new[] { 1, 2, 3 }, r.Sorted);
        Assert.Equal(c.Queue, r.Queue);
        Assert.Equal(c.Stack, r.Stack);   // top first, as before
        Assert.Equal(3, r.Stack!.Peek());
        Assert.Equal(c.Linked, r.Linked);
        Assert.Equal(c.ReadOnly, r.ReadOnly);
        Assert.Equal(c.Observable, r.Observable);
        Assert.Equal(c.Jagged, r.Jagged);
        Assert.Equal(c.ListOfArrays, r.ListOfArrays);
        Assert.Equal(c.Points, r.Points);
        Assert.Equal((byte)1, r.Padded![0].Tag);
        Assert.Equal(2.5, r.Padded[0].Value);
        Assert.True(r.Padded[0].Flag);
        Assert.Equal(c.PointList, r.PointList);
        Assert.Equal(c.Colors, r.Colors);
        Assert.Equal(c.Flags, r.Flags);
        Assert.Equal(c.Dictionary, r.Dictionary);
        Assert.Equal(c.IDictionary, r.IDictionary);
        Assert.Equal(c.ReadOnlyDictionary, r.ReadOnlyDictionary);
        Assert.IsType<SortedDictionary<int, int>>(r.SortedDictionary);
        Assert.Equal(c.SortedDictionary, r.SortedDictionary);
        Assert.IsType<SortedList<string, int>>(r.SortedList);
        Assert.Equal(c.SortedList, r.SortedList);
        Assert.IsType<ConcurrentDictionary<int, int>>(r.Concurrent);
        Assert.Equal(2, r.Concurrent![1]);
        Assert.IsType<ReadOnlyDictionary<int, int>>(r.Wrapped);
        Assert.Equal(50, r.Wrapped![5]);
        Assert.Equal(c.ListOfLists, r.ListOfLists);
        Assert.Equal(c.ArraysByName!["x"], r.ArraysByName!["x"]);
        Assert.Equal(c.ListsByName!["y"], r.ListsByName!["y"]);
        Assert.Equal(c.Tables![0]["z"], r.Tables![0]["z"]);
    }

    [Fact]
    public void OtherVersionsAreReadByName()
    {
        V2.Player p = TesseraSerializer.Deserialize<V2.Player>(TesseraSerializer.Serialize(Samples.PlayerV1()));
        Assert.Equal("Ann", p.DisplayName);
        Assert.Equal(12, p.Level);
        Assert.Equal(0.75f, p.Health);
        Assert.True(p.Online);
        Assert.Equal(50, p.Gold);           // missing from the writer: its declared default
        Assert.Equal(0, p.KindChanged);     // short -> int cannot be read
        Assert.Null(p.Title);
        Assert.Equal(new[] { "potion", "key" }, p.Items!.Select(i => i.Id));
        Assert.Equal(new[] { 3, 1 }, p.Items!.Select(i => i.Count));
        Assert.Equal(7, p.Stats!.Dex);
        Assert.Equal("key", p.Favorite!.Id);
        Assert.Empty(p.Scores!);            // int -> long values cannot be read

        V1.Player old = TesseraSerializer.Deserialize<V1.Player>(TesseraSerializer.Serialize(Samples.PlayerV2()));
        Assert.Equal("Bob", old.Name);
        Assert.Equal(3, old.Level);
        Assert.Null(old.Favorite);
        Assert.Equal("gem", old.Items![0].Id);

        V3.Player moved = TesseraSerializer.Deserialize<V3.Player>(TesseraSerializer.Serialize(Samples.PlayerV1()));
        Assert.Equal("Ann", moved.Name);
        Assert.Equal(99, moved.Removed);
        Assert.Equal(2, moved.Scores!["b"]);
    }

    [Fact]
    public void SharedItems()
    {
        // Stored once, referenced twice: two equal objects, as other serializers create.
        Node small = TesseraSerializer.Deserialize<Node>(TesseraSerializer.Serialize(Samples.Dag(3), ShareAll));
        Assert.NotSame(small.Children![0], small.Children[1]);
        Assert.Equal("x", small.Children[1].Children![0].Name);

        // So shared that copies would be 2^22 objects: each stored node is created once, and shared.
        byte[] dag = TesseraSerializer.Serialize(Samples.Dag(20), ShareAll);
        Node big = TesseraSerializer.Deserialize<Node>(dag);
        Assert.Same(big.Children![0], big.Children[1]);
        int depth = 0;
        for (Node n = big; n.Children!.Count > 0; n = n.Children[1]) depth++;
        Assert.Equal(20, depth);

        // Without the check, found while creating the copies.
        Node trusted = TesseraSerializer.Deserialize<Node>(dag, TesseraReadOptions.Trusted);
        Assert.Same(trusted.Children![0], trusted.Children[1]);
    }

    [Fact]
    public void LongStringsReferencedOftenAreDecodedOnce()
    {
        string text = new string('x', 50_000);
        var holder = new Holder { Names = Enumerable.Repeat(text, 2000).ToList() };
        Holder h = RoundTrip(holder);
        Assert.Equal(2000, h.Names!.Count);
        Assert.All(h.Names, n => Assert.Equal(text, n));
        Assert.Same(h.Names[1998], h.Names[1999]);
    }

    [Fact]
    public void BrokenBuffersAreRejected()
    {
        byte[] good = TesseraSerializer.Serialize(Samples.Full());
        Assert.False(TesseraSerializer.TryDeserialize(good.AsSpan(0, 15), out Monster? m, out TesseraReadError error));
        Assert.Null(m);
        Assert.Equal(TesseraReadError.TooSmall, error);
        Assert.False(TesseraSerializer.TryDeserialize<Monster>(good.AsSpan(0, good.Length - 8), out _, out error));
        Assert.Equal(TesseraReadError.OutOfBounds, error);
        var e = Assert.Throws<TesseraReadException>(() => TesseraSerializer.Deserialize<Monster>(new byte[16]));
        Assert.Equal(TesseraReadError.BadMagic, e.Error);
        byte[] weapon = TesseraSerializer.Serialize(new Weapon { Name = "x" }, new TesseraOptions { IncludeSchema = false });
        Assert.Equal(TesseraReadError.SchemaMismatch, Assert.Throws<TesseraReadException>(() => TesseraSerializer.Deserialize<Monster>(weapon)).Error);
    }

    /// <summary>A damaged buffer is rejected, or reads as a valid one: nothing else is thrown.</summary>
    [Fact]
    public void DamagedBuffersAreSafe()
    {
        var random = new Random(9876);
        foreach (byte[] good in new[]
                 {
                     TesseraSerializer.Serialize(Samples.Full()),
                     TesseraSerializer.Serialize(Samples.Full(), ShareAll),
                 })
        {
            for (int round = 0; round < 3000; round++)
            {
                byte[] bad = (byte[])good.Clone();
                int flips = 1 + random.Next(4);
                for (int i = 0; i < flips; i++) bad[16 + random.Next(bad.Length - 16)] ^= (byte)(1 << random.Next(8));
                TesseraSerializer.TryDeserialize<Monster>(bad, out _, out _);
            }
        }

        byte[] catalog = TesseraSerializer.Serialize(Samples.Catalog());
        for (int round = 0; round < 3000; round++)
        {
            byte[] bad = (byte[])catalog.Clone();
            bad[16 + random.Next(bad.Length - 16)] ^= (byte)(1 << random.Next(8));
            TesseraSerializer.TryDeserialize<Catalog>(bad, out _, out _);
        }
    }
}
