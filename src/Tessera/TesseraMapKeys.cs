using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Tessera;

/// <summary>Key order of dictionaries in buffers. Used by the generated writers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TesseraMapKeys
{
    /// <summary>
    /// Strings in code point order, which is the order of their UTF-8 bytes: C++ readers binary-search the keys with
    /// <c>std::string_view</c> comparisons. Ordinal UTF-16 order differs for characters from U+10000 on.
    /// </summary>
    public static IComparer<string> Utf8Order { get; } = new CodePointComparer();

    /// <summary>
    /// Sorts the first <paramref name="count"/> string keys in code point order, moving their values with them. Keys
    /// already in order (a sorted dictionary, or one filled in key order) are left as they are.
    /// </summary>
    public static void SortUtf8<TValue>(string[] keys, TValue[] values, int count)
    {
        // Dictionary keys are distinct, so keys in order are each below the next.
        for (int i = 1; i < count; i++)
        {
            if (CompareCodePoints(keys[i - 1], keys[i]) >= 0)
            {
                keys.AsSpan(0, count).Sort(values.AsSpan(0, count), Utf8Order);
                return;
            }
        }
    }

    /// <summary>
    /// Sorts the first <paramref name="count"/> keys (integers, chars or enums) ascending, moving their values with them.
    /// Keys already in order are left as they are.
    /// </summary>
    public static void Sort<TKey, TValue>(TKey[] keys, TValue[] values, int count)
    {
        for (int i = 1; i < count; i++)
        {
            if (Comparer<TKey>.Default.Compare(keys[i - 1], keys[i]) >= 0)
            {
                keys.AsSpan(0, count).Sort(values.AsSpan(0, count));
                return;
            }
        }
    }

    private sealed class CodePointComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => CompareCodePoints(x, y);
    }

    private static int CompareCodePoints(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = x.AsSpan().CommonPrefixLength(y);
        if (i == x.Length || i == y.Length) return x.Length - y.Length;
        return CodePointRank(x[i]) - CodePointRank(y[i]);
    }

    // Surrogates (code points from U+10000) sort after U+E000..U+FFFF in code point order but before them in UTF-16.
    private static int CodePointRank(char c) => c < 0xD800 ? c : c >= 0xE000 ? c - 0x800 : c + 0x2000;
}
