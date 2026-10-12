using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TestModels;
using Xunit;

namespace Tessera.Tests;

/// <summary>Buffers written by the .NET writer, read back in .NET through the generated read types.</summary>
public class ReaderTests
{
    [Fact]
    public void EveryMemberReadsBack()
    {
        Monster m = Samples.Full();
        byte[] buffer = TesseraSerializer.Serialize(m);
        MonsterView v = MonsterView.Read(buffer);

        Assert.False(v.IsNull);
        Assert.Equal(m.Name, v.Name);
        Assert.Equal(m.Name, Encoding.UTF8.GetString(v.NameUtf8));
        Assert.Equal(m.Hp, v.Hp);
        Assert.Equal(m.Mana, v.Mana);
        Assert.True(v.HasMana);
        Assert.Equal(m.Pos, v.Pos);
        Assert.Equal(m.Velocity!.Value.X, v.Velocity!.Value.X);   // -0.0 kept
        Assert.True(float.IsNegative(v.Velocity!.Value.X));
        Assert.Equal(m.Velocity, v.Velocity);
        Assert.Equal(m.Color, v.Color);
        Assert.Equal(m.Friendly, v.Friendly);
        Assert.Equal(m.Boss, v.Boss);
        Assert.Equal(m.Weapons!.Count, v.Weapons.Count);
        for (int i = 0; i < m.Weapons.Count; i++)
        {
            Assert.Equal(m.Weapons[i].Name, v.Weapons[i].Name);
            Assert.Equal(m.Weapons[i].Damage, v.Weapons[i].Damage);
        }

        Assert.Equal(m.Inventory, v.Inventory.ToArray());
        Assert.Equal(m.Tags, ToList(v.Tags));
        Assert.True(v.Tags.IsNull(1));
        Assert.Equal(m.Equipped!.Name, v.Equipped.Name);
        Assert.Equal(m.Equipped.Damage, v.Equipped.Damage);
        Assert.True(double.IsNaN(v.Score));
        Assert.Equal(m.Id, v.Id);
        Assert.False(v.Loot.IsNull);
        Assert.True(v.Loot.IsKey);
        Assert.False(v.Loot.IsPotion);
        Assert.True(v.Loot.AsPotion.IsNull);
        Assert.Equal("gate", v.Loot.AsKey.Label);
        Assert.Equal(7u, v.Loot.AsKey.Door);
        Assert.True(v.Loot.AsKey.Golden);
        Assert.Equal(3, v.Bag.Count);
        Assert.Equal(25, v.Bag[0].AsPotion.Heal);
        Assert.True(v.Bag[1].IsNull);
        Assert.Equal(1u, v.Bag[2].AsKey.Door);
        Assert.Equal(m.Path, v.Path.ToArray());
        Assert.Equal(3, v.Grid.Count);
        Assert.Equal(new[] { 1, 2 }, v.Grid[0].ToArray());
        Assert.True(v.Grid.IsNull(1));
        Assert.False(v.Grid.IsNull(2));
        Assert.Equal(0, v.Grid[2].Length);
        Assert.Equal(m.Abilities, v.Abilities);
        Assert.Equal(m.Initial, v.Initial);
        Assert.Equal(m.Blob, v.Blob.ToArray());
        Assert.Equal(m.MoveSpeed, v.MoveSpeed);
        Assert.Equal(m.Tint, v.Tint);
        Assert.Equal(m.Ref, v.Ref);
        Assert.Equal(m.Odd, v.Odd);
        Assert.Equal(m.Samples, v.Samples.ToArray());
        Assert.Equal(m.Unique!.OrderBy(s => s, StringComparer.Ordinal), ToList(v.Unique).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(m.Big, v.Big);
        Assert.Equal(m.Tiny, v.Tiny);
        Assert.Equal(m.Switches, v.Switches.ToArray());
    }

    [Fact]
    public void AbsentMembersReadAsDefaults()
    {
        MonsterView v = MonsterView.Read(TesseraSerializer.Serialize(Samples.Sparse()));
        Assert.Null(v.Name);
        Assert.True(v.NameUtf8.IsEmpty);
        Assert.False(v.HasName);
        Assert.Equal(100, v.Hp);            // the declared default
        Assert.False(v.HasHp);
        Assert.Null(v.Mana);
        Assert.Equal(default, v.Pos);
        Assert.Null(v.Velocity);
        Assert.Equal(Color.Blue, v.Color);  // the declared default
        Assert.False(v.Friendly);
        Assert.Null(v.Boss);
        Assert.True(v.Weapons.IsNull);
        Assert.Equal(0, v.Weapons.Count);
        Assert.True(v.Inventory.IsEmpty);
        Assert.Equal(0, v.Tags.Count);
        Assert.True(v.Equipped.IsNull);
        Assert.Null(v.Equipped.Name);
        Assert.Equal(0, v.Equipped.Damage);
        Assert.True(v.Loot.IsNull);
        Assert.True(v.Loot.AsKey.IsNull);
        Assert.Equal(0, v.Bag.Count);
        Assert.Equal(0, v.Grid.Count);
    }

    [Fact]
    public void RecordsAndNestedListsReadBack()
    {
        NodeView root = NodeView.Read(TesseraSerializer.Serialize(Samples.Tree()));
        Assert.Equal("root", root.Name);
        Assert.Equal(3, root.Children.Count);
        Assert.True(root.Children[0].Children.IsNull);
        Assert.Equal("c", root.Children[1].Children[0].Name);
        Assert.False(root.Children[1].Children[0].Children.IsNull);
        Assert.Equal(0, root.Children[1].Children[0].Children.Count);
        var names = new List<string?>();
        foreach (NodeView child in root.Children) names.Add(child.Name);
        Assert.Equal(new[] { "a", "b", "a" }, names);
    }

    [Fact]
    public void FixedCellsReadBack()
    {
        Swarm s = Samples.Swarm();
        SwarmView v = SwarmView.Read(TesseraSerializer.Serialize(s));
        ParticleView p0 = v.Particles[0], p1 = v.Particles[1];
        Assert.Equal(long.MinValue, p0.Id);
        Assert.True(float.IsNegative(p0.X));
        Assert.Equal(new Vec3(1, 2, 3), p0.Velocity);
        Assert.Equal(-7, p0.Kind);
        Assert.Equal(0xFF, p0.Flags);
        Assert.Equal(Color.Red, p0.Tint);
        Assert.True(p0.Visible);
        Assert.Equal(2.5, p0.Mass);
        Assert.Equal("p0", p0.Name);
        Assert.Equal(0, p0.Life);
        Assert.Equal(1.5f, p1.X);            // a fixed cell holding its non-zero default
        Assert.Equal(Color.Blue, p1.Tint);
        Assert.Equal(10, p1.Life);
        Assert.Equal(42, v.Leader.Id);
        Assert.True(v.Missing.IsNull);
        Assert.Equal(1.5f, v.Missing.X);     // an absent object reads its declared defaults
        Assert.Equal(Color.Blue, v.Missing.Tint);
        Assert.False(v.Missing.HasId);
    }

    [Fact]
    public void DictionariesAreFoundByKey()
    {
        Catalog c = Samples.Catalog();
        CatalogView v = CatalogView.Read(TesseraSerializer.Serialize(c));
        foreach (var (key, value) in c.Stock!)
        {
            Assert.True(v.Stock.TryGetValue(key, out int found), key);
            Assert.Equal(value, found);
            Assert.Equal(value, v.Stock[key]);
        }

        Assert.False(v.Stock.ContainsKey("missing"));
        Assert.Equal(-1, v.Stock.IndexOf("aaa"));
        Assert.Equal(c.Stock.Count, v.Stock.Count);
        Assert.Equal(new[] { "", "apple", "pear", "zebra", "� replacement", "\U0001F600 smile" }, ToList(v.Stock.Keys));
        foreach (var (key, value) in c.Names!) Assert.Equal(value, v.Names[key]);
        Assert.False(v.Names.ContainsKey(1));
        Assert.Equal("Bow", v.ByColor[Color.Blue].Name);
        Assert.Equal(9, v.ByColor[Color.Red].Damage);
        Assert.False(v.ByColor.TryGetValue(Color.Green, out WeaponView none));
        Assert.True(none.IsNull);
        Assert.Equal(new[] { "a", "b" }, ToList(v.Tags[10]));
        Assert.Equal(0, v.Tags[5].Count);
        Assert.Equal(new Vec3(4, 5, 6), v.Points['a']);
        Assert.Equal(1, v.Nested["x"]["y"]);
        Assert.False(v.Empty.IsNull);
        Assert.Equal(0, v.Empty.Count);
        Assert.True(v.Missing.IsNull);
        Assert.Equal(0, v.Missing.Count);
        Assert.False(v.Missing.ContainsKey(1));
        Assert.Throws<KeyNotFoundException>(() => CatalogView.Read(TesseraSerializer.Serialize(c)).Stock["nope"]);
    }

    [Fact]
    public void NullableElementsReadBack()
    {
        Readings r = Samples.Readings();
        ReadingsView v = ReadingsView.Read(TesseraSerializer.Serialize(r));
        Assert.Equal(r.Ints!.Count, v.Ints.Count);
        for (int i = 0; i < r.Ints.Count; i++) Assert.Equal(r.Ints[i], v.Ints[i]);
        Assert.Equal(r.Doubles, ToArray(v.Doubles));
        Assert.True(double.IsNegative(v.Doubles[2]!.Value));
        Assert.Equal(r.Flags, ToArray(v.Flags));
        Assert.Equal(r.Colors, ToArray(v.Colors));
        Assert.Equal(r.Points, ToArray(v.Points));
        Assert.Equal(3, v.Nested.Count);
        Assert.Equal(new short?[] { 1, null }, ToArray(v.Nested[0]));
        Assert.Equal(0, v.Nested[1].Count);
        Assert.Equal(new short?[] { null }, ToArray(v.Nested[2]));
        Assert.Equal(1L, v.Optional["a"]);
        Assert.True(v.Optional.ContainsKey("b"));
        Assert.Null(v.Optional["b"]);
    }

    [Fact]
    public void WideObjectsReadBack()
    {
        foreach (Wide w in new[] { Samples.WideDense(), Samples.WideSparse() })
        {
            WideView v = WideView.Read(TesseraSerializer.Serialize(w));
            Assert.Equal(w.F00, v.F00);
            Assert.Equal(w.F07, v.F07);
            Assert.Equal(w.F31, v.F31);
            Assert.Equal(w.F32, v.F32);
            Assert.Equal(w.F39, v.F39);
            Assert.Equal(w.B00, v.B00);
            Assert.Equal(w.B19, v.B19);
            Assert.Equal(w.S0, v.S0);
            Assert.Equal(w.S3, v.S3);
            Assert.Equal(w.S7, v.S7);
            Assert.Equal(w.D0, v.D0);
            Assert.Equal(w.D2, v.D2);
            Assert.Equal(w.D3, v.D3);
        }
    }

    [Theory]
    [InlineData(Sharing.None)]
    [InlineData(Sharing.Strings)]
    [InlineData(Sharing.All)]
    public void SharedItemsReadBack(Sharing sharing)
    {
        var options = new TesseraOptions { Sharing = sharing };
        NodeView dag = NodeView.Read(TesseraSerializer.Serialize(Samples.Dag(10), options));
        int depth = 0;
        for (NodeView n = dag; n.Children.Count > 0; n = n.Children[1]) depth++;
        Assert.Equal(10, depth);

        TagCloudView tags = TagCloudView.Read(TesseraSerializer.Serialize(Samples.SharedTags(), options));
        Assert.Equal(2000, tags.Groups.Count);
        Assert.Equal("tag1999", tags.Groups[1999][1999]);

        NodeView text = NodeView.Read(TesseraSerializer.Serialize(Samples.SharedLongString(), options), new TesseraReadOptions { ValidateUtf8 = true });
        Assert.Equal(Samples.SharedLongString().Children![499].Name, text.Children[499].Name);
    }

    [Fact]
    public void ReadsFromAnyMemory()
    {
        byte[] buffer = TesseraSerializer.Serialize(Samples.Full());
        // At an odd address: .NET reads unaligned values.
        byte[] shifted = new byte[buffer.Length + 3];
        buffer.CopyTo(shifted, 3);
        Assert.Equal("Bow", MonsterView.Read(shifted.AsSpan(3)).Weapons[1].Name);
        Assert.Equal("Bow", MonsterView.Read(new ReadOnlyMemory<byte>(buffer).Span).Weapons[1].Name);
        Span<byte> stack = stackalloc byte[buffer.Length];
        buffer.CopyTo(stack);
        Assert.Equal("Bow", MonsterView.Read(stack).Weapons[1].Name);
    }

    [Fact]
    public void BrokenBuffersAreRejected()
    {
        byte[] good = TesseraSerializer.Serialize(Samples.Full());
        Assert.Equal(TesseraReadError.TooSmall, Error(good.AsSpan(0, 15)));
        Assert.Equal(TesseraReadError.OutOfBounds, Error(good.AsSpan(0, good.Length - 8)));
        byte[] magic = (byte[])good.Clone();
        magic[4] ^= 1;
        Assert.Equal(TesseraReadError.BadMagic, Error(magic));
        byte[] version = (byte[])good.Clone();
        version[6] = 99;
        Assert.Equal(TesseraReadError.BadVersion, Error(version));
        byte[] flags = (byte[])good.Clone();
        flags[7] |= 0x80;
        Assert.Equal(TesseraReadError.ReservedBits, Error(flags));
        byte[] root = (byte[])good.Clone();
        root[0] = 2;
        Assert.Equal(TesseraReadError.BadOffset, Error(root));

        // Another root type, with no schema to match it by: rejected.
        byte[] weapon = TesseraSerializer.Serialize(new Weapon { Name = "x" }, new TesseraOptions { IncludeSchema = false });
        Assert.Equal(TesseraReadError.SchemaMismatch, Error(weapon));
        var e = Assert.Throws<TesseraReadException>(() => MonsterView.Read(weapon));
        Assert.Equal(TesseraReadError.SchemaMismatch, e.Error);
    }

    [Fact]
    public void LimitsAreEnforced()
    {
        byte[] deep = TesseraSerializer.Serialize(Samples.Chain(40));   // 80 levels: the deepest item is at depth 79
        Assert.True(NodeView.TryRead(deep, out _, out _));
        Assert.False(NodeView.TryRead(deep, out _, out TesseraReadError error, new TesseraReadOptions { MaxDepth = 78 }));
        Assert.Equal(TesseraReadError.TooDeep, error);
        Assert.True(NodeView.TryRead(deep, out _, out _, new TesseraReadOptions { MaxDepth = 79 }));

        byte[] dag = TesseraSerializer.Serialize(Samples.Dag(20), new TesseraOptions { Sharing = Sharing.All });   // 2^22 paths, 64 items
        Assert.True(NodeView.TryRead(dag, out NodeView v, out _));
        Assert.Equal("x", v.Children[0].Children[1].Name);
        Assert.False(NodeView.TryRead(dag, out _, out error, new TesseraReadOptions { MaxItems = 20 }));
        Assert.Equal(TesseraReadError.TooManyItems, error);
    }

    /// <summary>Damaged buffers never make a read go outside the buffer: each one is rejected, or reads like a valid one.</summary>
    [Fact]
    public void DamagedBuffersAreSafe()
    {
        var random = new Random(12345);
        foreach (byte[] good in new[]
                 {
                     TesseraSerializer.Serialize(Samples.Full()),
                     TesseraSerializer.Serialize(Samples.Full(), new TesseraOptions { Sharing = Sharing.All }),
                 })
        {
            for (int round = 0; round < 3000; round++)
            {
                byte[] bad = (byte[])good.Clone();
                int flips = 1 + random.Next(4);
                for (int i = 0; i < flips; i++) bad[16 + random.Next(bad.Length - 16)] ^= (byte)(1 << random.Next(8));
                if (MonsterView.TryRead(bad, out MonsterView v, out _)) ReadEverything(v);
            }
        }
    }

    /// <summary>Touches every member, recursively, as a program reading the buffer would.</summary>
    private static void ReadEverything(MonsterView v)
    {
        _ = v.Name;
        _ = v.Hp;
        _ = v.Mana;
        _ = v.Pos;
        _ = v.Velocity;
        _ = v.Color;
        _ = v.Friendly;
        _ = v.Boss;
        foreach (WeaponView w in v.Weapons)
        {
            _ = w.Name;
            _ = w.Damage;
        }

        _ = v.Inventory.ToArray();
        foreach (string? s in v.Tags) _ = s;
        _ = v.Equipped.Name;
        _ = v.Score;
        _ = v.Id;
        _ = v.Loot.AsKey.Label;
        _ = v.Loot.AsPotion.Heal;
        foreach (ItemView item in v.Bag)
        {
            _ = item.AsKey.Door;
            _ = item.AsPotion.Label;
        }

        _ = v.Path.ToArray();
        foreach (ReadOnlySpan<int> row in v.Grid) _ = row.ToArray();
        _ = v.Abilities;
        _ = v.Initial;
        _ = v.Blob.ToArray();
        _ = v.Tint;
        _ = v.Ref;
        _ = v.Odd;
        _ = v.Samples.ToArray();
        foreach (string? s in v.Unique) _ = s;
        _ = v.Switches.ToArray();
    }

    private static TesseraReadError Error(ReadOnlySpan<byte> buffer)
    {
        MonsterView.TryRead(buffer, out _, out TesseraReadError error);
        return error;
    }

    private static List<string?> ToList(TesseraStringList list)
    {
        var result = new List<string?>();
        foreach (string? s in list) result.Add(s);
        return result;
    }

    private static T?[] ToArray<T>(TesseraNullableList<T> list) where T : unmanaged
    {
        var result = new T?[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = list[i];
        return result;
    }
}
