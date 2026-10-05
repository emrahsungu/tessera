using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Tessera.Tests;

public class MapKeyTests
{
    // Code point order is the order of the UTF-8 bytes.
    private static int Utf8Compare(string a, string b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b));

    [Theory]
    [InlineData("", "a")]
    [InlineData("a", "ab")]
    [InlineData("ab", "b")]
    [InlineData("item-00ff", "item-0100")]
    [InlineData("�", "\U0001F600")]        // U+FFFD before U+1F600, though UTF-16 order puts the surrogate first
    [InlineData("x", "x\U00010000")]
    [InlineData("\U0001F600", "\U0001F601")]
    public void StringKeysSortByCodePoint(string low, string high)
    {
        Assert.True(Utf8Compare(low, high) < 0);
        Assert.True(TesseraMapKeys.Utf8Order.Compare(low, high) < 0);
        Assert.True(TesseraMapKeys.Utf8Order.Compare(high, low) > 0);
        Assert.Equal(0, TesseraMapKeys.Utf8Order.Compare(low, new string(low.AsSpan())));
    }

    [Fact]
    public void SortsStringKeysWithTheirValues()
    {
        var rng = new Random(5);
        string[] pool = { "", "a", "ab", "b", "", "�", "\U00010000", "\U0001F600", "xy", "x\U00010000", "zz" };
        string[] keys = pool.Concat(Enumerable.Range(0, 200).Select(i => "k" + rng.Next(100000).ToString("x"))).Distinct().ToArray();
        string[] shuffled = keys.OrderBy(_ => rng.Next()).ToArray();
        int[] values = shuffled.Select(k => Array.IndexOf(keys, k)).ToArray();

        TesseraMapKeys.SortUtf8(shuffled, values, shuffled.Length);

        for (int i = 1; i < shuffled.Length; i++) Assert.True(Utf8Compare(shuffled[i - 1], shuffled[i]) < 0);
        for (int i = 0; i < shuffled.Length; i++) Assert.Equal(keys[values[i]], shuffled[i]);
    }

    [Fact]
    public void SortsOnlyTheCountedPrefix()
    {
        string[] keys = { "c", "a", "b", "0" };   // pooled arrays are longer than the dictionary
        int[] values = { 3, 1, 2, 9 };
        TesseraMapKeys.SortUtf8(keys, values, 3);
        Assert.Equal(new[] { "a", "b", "c", "0" }, keys);
        Assert.Equal(new[] { 1, 2, 3, 9 }, values);
    }

    [Fact]
    public void KeysInOrderStayAsTheyAre()
    {
        string[] keys = { "a", "b", "c" };
        object[] values = { new(), new(), new() };
        object[] before = values.ToArray();
        TesseraMapKeys.SortUtf8(keys, values, 3);
        Assert.Equal(before, values);
    }

    private enum Kind : short { Low = -5, Mid = 0, High = 7 }

    [Fact]
    public void SortsIntegerCharAndEnumKeys()
    {
        int[] ints = { 5, -3, 0, int.MaxValue, int.MinValue };
        string[] iv = ints.Select(i => i.ToString()).ToArray();
        TesseraMapKeys.Sort(ints, iv, ints.Length);
        Assert.Equal(new[] { int.MinValue, -3, 0, 5, int.MaxValue }, ints);
        Assert.Equal(ints.Select(i => i.ToString()), iv);

        char[] chars = { 'z', 'a', '￿', '0' };
        int[] cv = { 1, 2, 3, 4 };
        TesseraMapKeys.Sort(chars, cv, chars.Length);
        Assert.Equal(new[] { '0', 'a', 'z', '￿' }, chars);
        Assert.Equal(new[] { 4, 2, 1, 3 }, cv);

        Kind[] kinds = { Kind.High, Kind.Low, Kind.Mid };
        int[] kv = { 7, -5, 0 };
        TesseraMapKeys.Sort(kinds, kv, kinds.Length);
        Assert.Equal(new[] { Kind.Low, Kind.Mid, Kind.High }, kinds);
        Assert.Equal(new[] { -5, 0, 7 }, kv);
    }
}
