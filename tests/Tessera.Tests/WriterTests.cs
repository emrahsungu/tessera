using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestModels;
using TestModels.Behavior;
using Xunit;
using static Tessera.Tests.BufferReader;

namespace Tessera.Tests;

public class WriterTests
{
    private static Dictionary<ulong, object?> Read(byte[] bytes) => new BufferReader(bytes).Root();

    [Fact]
    public void HeaderIsWellFormed()
    {
        byte[] b = TesseraSerializer.Serialize(Samples.Full());
        Assert.Equal(WireFormat.Magic, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)));
        Assert.Equal(WireFormat.Version, b[6]);
        Assert.Equal(WireFormat.FlagSchema, b[7]);
        Assert.Equal(0, b.Length % 8);
        uint root = BinaryPrimitives.ReadUInt32LittleEndian(b);
        Assert.True(root >= 16 && root < b.Length && root % 4 == 0);
        Assert.Equal(TesseraModel<Monster>.Type!.DeepFingerprint, BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(8)));
    }

    [Fact]
    public void ValuesRoundTripThroughTheEmbeddedSchema()
    {
        var m = Samples.Full();
        var r = Read(TesseraSerializer.Serialize(m));
        Assert.Equal(m.Name, r[Hash("Name")]);
        Assert.Equal((short)300, r[Hash("Hp")]);
        Assert.Equal((short)0, r[Hash("Mana")]);
        Assert.Equal(true, r[Hash("Friendly")]);
        Assert.Equal(false, r[Hash("Boss")]);
        Assert.Equal(long.MinValue, r[Hash("Id")]);
        Assert.Equal(ulong.MaxValue, r[Hash("Big")]);
        Assert.Equal((sbyte)-128, r[Hash("Tiny")]);
        Assert.Equal('Ж', r[Hash("Initial")]);
        Assert.Equal(4.5f, r[Hash("Speed")]);            // [TesseraName("Speed")]
        Assert.False(r.ContainsKey(Hash("MoveSpeed")));
        Assert.True(double.IsNaN((double)r[Hash("Score")]!));
        var weapons = (List<object?>)r[Hash("Weapons")]!;
        Assert.Equal(3, weapons.Count);
        Assert.Equal("Bow", ((Dictionary<ulong, object?>)weapons[1]!)[Hash("Name")]);
        var tags = (List<object?>)r[Hash("Tags")]!;
        Assert.Equal(new object?[] { "a", null, "", "a" }, tags);
        var (tag, loot) = ((uint, Dictionary<ulong, object?>))r[Hash("Loot")]!;
        Assert.Equal(XxHash.Hash32("Key"), tag);
        Assert.Equal(7u, loot[Hash("Door")]);
        var grid = (List<object?>)r[Hash("Grid")]!;
        Assert.Null(grid[1]);
        Assert.Empty((List<object?>)grid[2]!);
    }

    [Fact]
    public void FixedCellsHaveConstantPositions()
    {
        // [TesseraKeepDefault] values sit right after the header, in wire order (Id, X, Velocity, Kind, then Flags and
        // Tint by name hash), padded with zeros to 32 bytes for the 8-aligned Mass, whatever the other members hold.
        var bare = new Particle { Id = 7, X = 2, Velocity = new Vec3(1, 2, 3), Kind = 3, Flags = 4 };
        var full = new Particle { Id = 7, X = 2, Velocity = new Vec3(1, 2, 3), Kind = 3, Flags = 4, Mass = 1, Name = "n", Life = 5, Visible = true };
        byte[] a = TesseraSerializer.Serialize(bare), b = TesseraSerializer.Serialize(full);
        int ra = (int)BinaryPrimitives.ReadUInt32LittleEndian(a), rb = (int)BinaryPrimitives.ReadUInt32LittleEndian(b);
        Assert.Equal(a.AsSpan(ra + 4, 32).ToArray(), b.AsSpan(rb + 4, 32).ToArray());
        Assert.Equal(7L, BinaryPrimitives.ReadInt64LittleEndian(a.AsSpan(ra + 4)));
        Assert.Equal(2f, BinaryPrimitives.ReadSingleLittleEndian(a.AsSpan(ra + 12)));
        Assert.Equal(new byte[4], a.AsSpan(ra + 4 + 28, 4).ToArray());
        Assert.Equal(0, (rb + 4 + 32) % 8);   // Mass, the first other cell, is 8-aligned

        var r = Read(b);
        Assert.Equal(7L, r[Hash("Id")]);
        Assert.Equal((short)3, r[Hash("Kind")]);
        Assert.Equal(1.0, r[Hash("Mass")]);
        Assert.Equal("n", r[Hash("Name")]);
        Assert.Equal(5, r[Hash("Life")]);
        Assert.False(Read(a).ContainsKey(Hash("Mass")));
        Assert.Equal(1.5f, Read(TesseraSerializer.Serialize(new Particle()))[Hash("X")]);   // stored although it is the default
    }

    [Fact]
    public void DictionariesAreSortedByKey()
    {
        var r = Read(TesseraSerializer.Serialize(Samples.Catalog()));
        var stock = (Dictionary<ulong, object?>)r[Hash("Stock")]!;
        // Strings by code point (UTF-8 byte order): U+FFFD before U+1F600, unlike ordinal UTF-16 order.
        Assert.Equal(new object?[] { "", "apple", "pear", "zebra", "� replacement", "\U0001F600 smile" }, (List<object?>)stock[Hash("Keys")]!);
        Assert.Equal(new object?[] { 7, 5, 3, 0, 1, 9 }, (List<object?>)stock[Hash("Values")]!);
        var names = (Dictionary<ulong, object?>)r[Hash("Names")]!;
        Assert.Equal(new object?[] { -1, 0, 3, int.MaxValue }, (List<object?>)names[Hash("Keys")]!);
        Assert.Equal(new object?[] { "minus one", "zero", "three", "max" }, (List<object?>)names[Hash("Values")]!);
        Assert.Empty((List<object?>)((Dictionary<ulong, object?>)r[Hash("Empty")]!)[Hash("Keys")]!);
        Assert.False(r.ContainsKey(Hash("Missing")));
    }

    [Fact]
    public void NegativeZeroAndNaNSurvive()
    {
        var m = new Monster { Velocity = new Vec3(-0f, 0f, float.NaN), Score = -0.0 };
        var r = Read(TesseraSerializer.Serialize(m));
        byte[] v = (byte[])r[Hash("Velocity")]!;
        Assert.Equal(0x80000000u, BinaryPrimitives.ReadUInt32LittleEndian(v));
        Assert.True(float.IsNaN(BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(8))));
        Assert.True(double.IsNegative((double)r[Hash("Score")]!), "-0.0 differs from the default 0.0, so it is written");
    }

    [Fact]
    public void DefaultsAreOmittedUnlessRequested()
    {
        var sparse = Read(TesseraSerializer.Serialize(new Defaults()));
        Assert.Equal(new[] { Hash("Kept") }, sparse.Keys.ToArray());   // [TesseraKeepDefault] only

        var changed = Read(TesseraSerializer.Serialize(new Defaults { Plain = 1, WithInitializer = 0, WithAttribute = 4, Ratio = 0, Flag = false }));
        Assert.Equal(1, changed[Hash("Plain")]);
        Assert.Equal(0, changed[Hash("WithInitializer")]);   // 0 differs from the declared default 7
        Assert.Equal(4, changed[Hash("WithAttribute")]);
        Assert.Equal(0f, changed[Hash("Ratio")]);
        Assert.Equal(false, changed[Hash("Flag")]);

        var all = Read(TesseraSerializer.Serialize(new Defaults(), new TesseraOptions { WriteDefaults = true }));
        Assert.Equal(6, all.Count);
        Assert.Equal(7, all[Hash("WithInitializer")]);
    }

    [Fact]
    public void AbsenceIsDistinctFromEmpty()
    {
        var empty = Read(TesseraSerializer.Serialize(new Holder { Names = new List<string>(), Text = "", Bytes = Array.Empty<byte>() }));
        Assert.Empty((List<object?>)empty[Hash("Names")]!);
        Assert.Equal("", empty[Hash("Text")]);
        Assert.Empty((List<object?>)empty[Hash("Bytes")]!);
        var none = Read(TesseraSerializer.Serialize(new Holder()));
        Assert.Empty(none);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryKeysShareWithOtherStrings(bool keysFirst)
    {
        // A key equal to a string written before it, and one equal to a string written after it, are stored once.
        var catalog = keysFirst
            ? new Catalog { Nested = new() { ["apple"] = new() { ["apple"] = 1 } } }   // the inner dictionary is written first
            : new Catalog { Stock = new() { ["apple"] = 1, ["pear"] = 2 }, Names = new() { [1] = "apple", [2] = "pear" } };
        byte[] shared = TesseraSerializer.Serialize(catalog);
        byte[] separate = TesseraSerializer.Serialize(catalog, new TesseraOptions { Sharing = Sharing.None });
        Assert.Equal(1, Count(shared, "apple"));
        Assert.Equal(2, Count(separate, "apple"));
        Assert.Equal(TesseraSerializer.Serialize(catalog), shared);   // the reused writer starts clean

        static int Count(byte[] buffer, string s)
        {
            byte[] item = BitConverter.GetBytes(s.Length).Concat(System.Text.Encoding.UTF8.GetBytes(s)).Append((byte)0).ToArray();
            int n = 0;
            for (int i = buffer.AsSpan().IndexOf(item); i >= 0; i = buffer.AsSpan(i + 1).IndexOf(item) is int j && j >= 0 ? i + 1 + j : -1) n++;
            return n;
        }
    }

    [Fact]
    public void SharingLevelsTradeSizeForSpeed()
    {
        var leaf = new Leaf { Value = 1, Label = "same label" };
        var holder = new Holder
        {
            Names = Enumerable.Repeat("repeated string", 50).ToList(),
            Leaves = Enumerable.Range(0, 50).Select(_ => new Leaf { Value = 1, Label = "same label" }).ToList(),
            First = leaf,
            Second = leaf,
        };
        int none = TesseraSerializer.Serialize(holder, new TesseraOptions { Sharing = Sharing.None }).Length;
        int strings = TesseraSerializer.Serialize(holder, new TesseraOptions { Sharing = Sharing.Strings }).Length;
        int all = TesseraSerializer.Serialize(holder, new TesseraOptions { Sharing = Sharing.All }).Length;
        Assert.True(none > strings && strings > all, $"none {none}, strings {strings}, all {all}");
        // All 52 equal leaves and the 50-element vector of them collapse into a few items.
        Assert.True(all < 900, $"all {all}");
        var r = Read(TesseraSerializer.Serialize(holder, new TesseraOptions { Sharing = Sharing.All }));
        Assert.Equal(50, ((List<object?>)r[Hash("Leaves")]!).Count);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        var writer = new TesseraWriter();
        byte[] a = TesseraSerializer.Serialize(Samples.Full());
        byte[] b = TesseraSerializer.Write(writer, Samples.Full()).ToArray();
        byte[] c = TesseraSerializer.Write(writer, Samples.Full()).ToArray();
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void CyclesAreRejected()
    {
        var a = new CycleNode { Name = "a" };
        a.Next = a;
        var e = Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(a));
        Assert.Contains("MaxDepth", e.Message);
        var chain = new CycleNode { Name = "x" };
        for (int i = 0; i < 100; i++) chain = new CycleNode { Next = chain };
        Assert.NotEmpty(TesseraSerializer.Serialize(chain));
        Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(chain, new TesseraOptions { MaxDepth = 50 }));
    }

    [Fact]
    public void DepthCountsObjectsAndVectors()
    {
        Assert.NotEmpty(TesseraSerializer.Serialize(Samples.Chain(64)));                        // 128 levels: the default limit
        Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(Samples.Chain(65)));     // 130 levels
        Assert.NotEmpty(TesseraSerializer.Serialize(Samples.Chain(65), new TesseraOptions { MaxDepth = 130 }));
        // A leaf vector (here byte[]) is a level too.
        var holder = new Holder { Bytes = new byte[1] };
        Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(holder, new TesseraOptions { MaxDepth = 1 }));
        Assert.NotEmpty(TesseraSerializer.Serialize(holder, new TesseraOptions { MaxDepth = 2 }));
    }

    [Fact]
    public void UnknownUnionMemberIsRejected()
    {
        var m = new Monster { Loot = new NotAModel() };
        var e = Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(m));
        Assert.Contains("not a member of union", e.Message);
    }

    /// <summary>Derives from a union base, but the generated writers cannot see a private type, so it is not a union member.</summary>
    private sealed class NotAModel : Item
    {
    }

    [Fact]
    public void ExplicitUnionsAcceptOnlyTheirListedTypes()
    {
        var ok = new Chest { Any = new Potion { Heal = 3 }, OnlyKey = new Key { Door = 9 }, Keys = new List<Item> { new Key { Door = 1 }, new Key() } };
        var r = Read(TesseraSerializer.Serialize(ok));
        var (tag, key) = ((uint, Dictionary<ulong, object?>))r[Hash("OnlyKey")]!;
        Assert.Equal(XxHash.Hash32("Key"), tag);
        Assert.Equal(9u, key[Hash("Door")]);
        Assert.Equal(2, ((List<object?>)r[Hash("Keys")]!).Count);

        var e = Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(new Chest { OnlyKey = new Potion() }));
        Assert.Contains("not a member of union", e.Message);
        Assert.Throws<TesseraException>(() => TesseraSerializer.Serialize(new Chest { Keys = new List<Item> { new Key(), new Potion() } }));
    }

    [Fact]
    public void ReorderedMembersKeepTheLayout()
    {
        // V3 is V1 with every member moved and some fields turned into properties: the same layout, so the same
        // fingerprints, and buffers of either version read the same.
        var v1 = TesseraModel<TestModels.V1.Player>.Type!;
        var v3 = TesseraModel<TestModels.V3.Player>.Type!;
        Assert.Equal(v1.Fingerprint, v3.Fingerprint);
        Assert.Equal(v1.DeepFingerprint, v3.DeepFingerprint);

        var a = Read(TesseraSerializer.Serialize(Samples.PlayerV1()));
        var b = Read(TesseraSerializer.Serialize(Samples.PlayerV3()));
        Assert.Equal(a.Keys.Order(), b.Keys.Order());
        foreach (string name in new[] { "Name", "Level", "Health", "Online", "Removed", "KindChanged" }) Assert.Equal(a[Hash(name)], b[Hash(name)]);
        var items = (List<object?>)b[Hash("Items")]!;
        Assert.Equal("key", ((Dictionary<ulong, object?>)items[1]!)[Hash("Id")]);
        Assert.Equal(7, ((Dictionary<ulong, object?>)b[Hash("Stats")]!)[Hash("Dex")]);
    }

    [Fact]
    public void ModelsNeedNoAttribute()
    {
        // Unmarked has no [Tessera]: this call makes it a model, and UnmarkedChild with it.
        var r = Read(TesseraSerializer.Serialize(new Unmarked { Name = "plain", Child = new UnmarkedChild { Value = 1.5f } }));
        Assert.Equal("plain", r[Hash("Name")]);
        Assert.False(r.ContainsKey(Hash("Count")));   // equal to its initializer: not stored
        Assert.Equal(1.5f, ((Dictionary<ulong, object?>)r[Hash("Child")]!)[Hash("Value")]);
    }

    [Fact]
    public void NonModelTypesAreRejected()
    {
        // Serialize(new object()) does not compile (TESSERA014); generic code, which hides the type, reaches the runtime check.
        Assert.Throws<TesseraException>(() => SerializeAny(new object()));
        Assert.Throws<ArgumentNullException>(() => TesseraSerializer.Serialize<Monster>(null!));
    }

    private static byte[] SerializeAny<T>(T value) => TesseraSerializer.Serialize(value);

    [Fact]
    public void LargeBuffersGrow()
    {
        var holder = new Holder { Bytes = new byte[3_000_000], Names = Enumerable.Range(0, 20_000).Select(i => "name " + i).ToList() };
        new Random(5).NextBytes(holder.Bytes);
        byte[] b = TesseraSerializer.Serialize(holder, new TesseraOptions { Sharing = Sharing.None });
        var r = Read(b);
        Assert.Equal(holder.Bytes.Length, ((List<object?>)r[Hash("Bytes")]!).Count);
        Assert.Equal("name 19999", ((List<object?>)r[Hash("Names")]!)[19999]);
    }

    [Fact]
    public void ConcurrentWritersAgree()
    {
        byte[] expected = TesseraSerializer.Serialize(Samples.Full());
        Parallel.For(0, 64, _ => Assert.Equal(expected, TesseraSerializer.Serialize(Samples.Full())));
    }

    [Fact]
    public void SchemaSectionCanBeOmitted()
    {
        byte[] with = TesseraSerializer.Serialize(Samples.Full());
        byte[] without = TesseraSerializer.Serialize(Samples.Full(), new TesseraOptions { IncludeSchema = false });
        Assert.Equal(0, without[7] & WireFormat.FlagSchema);
        Assert.True(without.Length < with.Length);
        Assert.Throws<FormatException>(() => new BufferReader(without));
    }

    [Fact]
    public void UnionMembersAreEmbeddedOnlyWhenWritten()
    {
        int withKey = new BufferReader(TesseraSerializer.Serialize(new Monster { Loot = new Key() })).EntryCount;
        int withNone = new BufferReader(TesseraSerializer.Serialize(new Monster())).EntryCount;
        Assert.Equal(withNone + 1, withKey);
    }
}
