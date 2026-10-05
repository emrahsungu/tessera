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

    private sealed class CodePointComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int n = Math.Min(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                char a = x[i], b = y[i];
                if (a != b) return CodePointRank(a) - CodePointRank(b);
            }

            return x.Length - y.Length;
        }

        // Surrogates (code points from U+10000) sort after U+E000..U+FFFF in code point order but before them in UTF-16.
        private static int CodePointRank(char c) => c < 0xD800 ? c : c >= 0xE000 ? c - 0x800 : c + 0x2000;
    }
}
