using System;
using System.Text;
using Xunit;
using V1 = TestModels.V1;
using V2 = TestModels.V2;
using V3 = TestModels.V3;

namespace Tessera.Tests;

/// <summary>Buffers written with one version of the classes, read in .NET with another (the C++ interop test's cases).</summary>
public class ReaderVersionTests
{
    [Fact]
    public void ReorderedMembersReadInPlace()
    {
        // V3 is V1 with every member moved: the same layout.
        V1.PlayerView p = V1.PlayerView.Read(TesseraSerializer.Serialize(Samples.PlayerV3()));
        Assert.Equal("Ann", p.Name);
        Assert.Equal(12, p.Level);
        Assert.Equal(0.75f, p.Health);
        Assert.True(p.Online);
        Assert.Equal(99, p.Removed);
        Assert.Equal(-2, p.KindChanged);
        Assert.Equal(2, p.Items.Count);
        Assert.Equal("potion", p.Items[0].Id);
        Assert.Equal(3, p.Items[0].Count);
        Assert.Equal("key", p.Items[1].Id);
        Assert.Equal(5, p.Stats.Str);
        Assert.Equal(7, p.Stats.Dex);
        Assert.Equal("key", p.Favorite.Id);
        Assert.Equal(1, p.Favorite.Count);
        Assert.Equal(2, p.Scores.Count);
        Assert.Equal(1, p.Scores["a"]);
        Assert.Equal(2, p.Scores["b"]);

        V3.PlayerView p3 = V3.PlayerView.Read(TesseraSerializer.Serialize(Samples.PlayerV1()));
        Assert.Equal("Ann", p3.Name);
        Assert.Equal(99, p3.Removed);
        Assert.Equal(7, p3.Stats.Dex);
        Assert.Equal(1, p3.Items[1].Count);
    }

    [Fact]
    public void OldBufferNewReader()
    {
        V2.PlayerView p = V2.PlayerView.Read(TesseraSerializer.Serialize(Samples.PlayerV1()));
        Assert.Equal("Ann", p.DisplayName);              // renamed in C#, the same name in the buffer
        Assert.Equal(12, p.Level);                        // a cell then, a fixed cell now
        Assert.Equal(0.75f, p.Health);
        Assert.True(p.Online);
        Assert.Equal(50, p.Gold);                         // missing from the writer: its declared default
        Assert.True(p.HasGold);                           // fixed cells are always stored
        Assert.False(p.HasKindChanged);                   // short -> int: cannot be read, so absent
        Assert.Equal(0, p.KindChanged);
        Assert.False(p.HasTitle);
        Assert.Equal(2, p.Items.Count);
        Assert.Equal("potion", p.Items[0].Id);
        Assert.Equal(3, p.Items[0].Count);
        Assert.Equal(0, p.Items[0].Rarity);              // added, as a fixed cell
        Assert.Equal("key", p.Items[1].Id);
        Assert.Equal(1, p.Items[1].Count);
        Assert.Equal(5, p.Stats.Str);
        Assert.Equal(7, p.Stats.Dex);
        Assert.Equal("key", p.Favorite.Id);
        // The values' type changed (int -> long): the dictionary keeps its keys and has no values.
        Assert.Equal(2, p.Scores.Count);
        Assert.Equal("a", p.Scores.Keys[0]);
        Assert.Equal(0, p.Scores.Values.Length);
        Assert.True(p.Scores.ContainsKey("a"));
        Assert.False(p.Scores.TryGetValue("a", out _));
    }

    [Fact]
    public void NewBufferOldReader()
    {
        V1.PlayerView p = V1.PlayerView.Read(TesseraSerializer.Serialize(Samples.PlayerV2()));
        Assert.Equal("Bob", p.Name);
        Assert.Equal(3, p.Level);
        Assert.Equal(1.0f, p.Health);
        Assert.False(p.Online);
        Assert.False(p.HasOnline);
        Assert.Equal(0, p.Removed);                       // a fixed cell the writer lacks: its default
        Assert.False(p.HasKindChanged);
        Assert.Equal(1, p.Items.Count);
        Assert.Equal("gem", p.Items[0].Id);
        Assert.Equal(5, p.Items[0].Count);
        Assert.Equal(1, p.Stats.Str);
        Assert.Equal(2, p.Stats.Dex);
        Assert.True(p.Favorite.IsNull);
        Assert.Equal(1, p.Scores.Count);
        Assert.Equal("x", p.Scores.Keys[0]);
        Assert.Equal(0, p.Scores.Values.Length);
        Assert.False(p.Scores.TryGetValue("x", out _));
    }

    [Fact]
    public void SameVersionReadsInPlace()
    {
        V2.PlayerView p = V2.PlayerView.Read(TesseraSerializer.Serialize(Samples.PlayerV2()));
        Assert.Equal(1000, p.Gold);
        Assert.Equal("Sir", p.Title);
        Assert.Equal(123456, p.KindChanged);
        Assert.Equal(10, p.Scores["x"]);
    }

    [Fact]
    public void WithoutSchemaOtherVersionsCannotBeRead()
    {
        byte[] b = TesseraSerializer.Serialize(Samples.PlayerV1(), new TesseraOptions { IncludeSchema = false });
        Assert.False(V2.PlayerView.TryRead(b, out _, out TesseraReadError error));
        Assert.Equal(TesseraReadError.SchemaMismatch, error);
        Assert.Equal(99, V1.PlayerView.Read(b).Removed);
    }

    [Fact]
    public void TranslationKeepsSharing()
    {
        // Two references to one string and one item: translated once each.
        var v1 = Samples.PlayerV1();
        v1.Favorite = v1.Items![1];
        byte[] b = TesseraSerializer.Serialize(v1, new TesseraOptions { Sharing = Sharing.All });
        V2.PlayerView p = V2.PlayerView.Read(b);
        Assert.Equal("key", p.Favorite.Id);
        Assert.Equal(1, p.Favorite.Count);
        Assert.Equal("key", p.Items[1].Id);
    }

    [Fact]
    public void DamagedBuffersOfOtherVersionsAreSafe()
    {
        var random = new Random(777);
        byte[] good = TesseraSerializer.Serialize(Samples.PlayerV1());
        for (int round = 0; round < 3000; round++)
        {
            byte[] bad = (byte[])good.Clone();
            int flips = 1 + random.Next(4);
            for (int i = 0; i < flips; i++) bad[random.Next(8, bad.Length)] ^= (byte)(1 << random.Next(8));
            if (!V2.PlayerView.TryRead(bad, out V2.PlayerView p, out _)) continue;
            _ = p.DisplayName;
            _ = p.Level;
            _ = p.Gold;
            foreach (V2.ItemView item in p.Items)
            {
                _ = item.Id;
                _ = item.Count;
                _ = item.Rarity;
            }

            _ = p.Stats.Dex;
            _ = p.Favorite.Id;
            foreach (string? key in p.Scores.Keys) _ = key;
            _ = p.Scores.Values.ToArray();
            _ = Encoding.UTF8.GetString(p.TitleUtf8);
        }
    }
}
