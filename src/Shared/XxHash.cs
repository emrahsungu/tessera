// xxHash32 / xxHash64 (seed 0), used for member-name hashes and schema fingerprints.
// Shared between the runtime and the source generator; no dependencies so it builds for netstandard2.0.
using System;

namespace Tessera
{
    /// <summary>Reference implementations of xxHash32 and xxHash64.</summary>
    public static class XxHash
    {
        private const uint P32_1 = 2654435761U, P32_2 = 2246822519U, P32_3 = 3266489917U, P32_4 = 668265263U, P32_5 = 374761393U;
        private const ulong P64_1 = 11400714785074694791UL, P64_2 = 14029467366897019727UL, P64_3 = 1609587929392839161UL,
            P64_4 = 9650029242287828579UL, P64_5 = 2870177450012600261UL;

        /// <summary>xxHash32 (seed 0) of the UTF-8 bytes of <paramref name="text"/>: type ids and union tags.</summary>
        public static uint Hash32(string text) => Hash32(System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>xxHash64 (seed 0) of the UTF-8 bytes of <paramref name="text"/>: member-name hashes.</summary>
        public static ulong Hash64(string text) => Hash64(System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>xxHash32 with seed 0.</summary>
        public static uint Hash32(byte[] data)
        {
            int len = data.Length, i = 0;
            uint h;
            if (len >= 16)
            {
                uint v1 = unchecked(P32_1 + P32_2), v2 = P32_2, v3 = 0, v4 = unchecked(0 - P32_1);
                int limit = len - 16;
                do
                {
                    v1 = Round32(v1, Read32(data, i)); i += 4;
                    v2 = Round32(v2, Read32(data, i)); i += 4;
                    v3 = Round32(v3, Read32(data, i)); i += 4;
                    v4 = Round32(v4, Read32(data, i)); i += 4;
                } while (i <= limit);
                h = Rotl32(v1, 1) + Rotl32(v2, 7) + Rotl32(v3, 12) + Rotl32(v4, 18);
            }
            else
            {
                h = P32_5;
            }

            h = unchecked(h + (uint)len);
            while (i + 4 <= len)
            {
                h = unchecked(Rotl32(h + Read32(data, i) * P32_3, 17) * P32_4);
                i += 4;
            }

            while (i < len)
            {
                h = unchecked(Rotl32(h + data[i] * P32_5, 11) * P32_1);
                i++;
            }

            h ^= h >> 15;
            h = unchecked(h * P32_2);
            h ^= h >> 13;
            h = unchecked(h * P32_3);
            h ^= h >> 16;
            return h;
        }

        /// <summary>xxHash64 with seed 0.</summary>
        public static ulong Hash64(byte[] data) => Hash64(data, data.Length);

        /// <summary>xxHash64 with seed 0 over the first <paramref name="len"/> bytes.</summary>
        public static ulong Hash64(byte[] data, int len)
        {
            int i = 0;
            ulong h;
            if (len >= 32)
            {
                ulong v1 = unchecked(P64_1 + P64_2), v2 = P64_2, v3 = 0, v4 = unchecked(0 - P64_1);
                int limit = len - 32;
                do
                {
                    v1 = Round64(v1, Read64(data, i)); i += 8;
                    v2 = Round64(v2, Read64(data, i)); i += 8;
                    v3 = Round64(v3, Read64(data, i)); i += 8;
                    v4 = Round64(v4, Read64(data, i)); i += 8;
                } while (i <= limit);
                h = Rotl64(v1, 1) + Rotl64(v2, 7) + Rotl64(v3, 12) + Rotl64(v4, 18);
                h = Merge64(h, v1);
                h = Merge64(h, v2);
                h = Merge64(h, v3);
                h = Merge64(h, v4);
            }
            else
            {
                h = P64_5;
            }

            h = unchecked(h + (ulong)len);
            while (i + 8 <= len)
            {
                h ^= Round64(0, Read64(data, i));
                h = unchecked(Rotl64(h, 27) * P64_1 + P64_4);
                i += 8;
            }

            if (i + 4 <= len)
            {
                h ^= unchecked(Read32(data, i) * P64_1);
                h = unchecked(Rotl64(h, 23) * P64_2 + P64_3);
                i += 4;
            }

            while (i < len)
            {
                h ^= unchecked(data[i] * P64_5);
                h = unchecked(Rotl64(h, 11) * P64_1);
                i++;
            }

            h ^= h >> 33;
            h = unchecked(h * P64_2);
            h ^= h >> 29;
            h = unchecked(h * P64_3);
            h ^= h >> 32;
            return h;
        }

        private static uint Round32(uint acc, uint input) => unchecked(Rotl32(acc + input * P32_2, 13) * P32_1);

        private static ulong Round64(ulong acc, ulong input) => unchecked(Rotl64(acc + input * P64_2, 31) * P64_1);

        private static ulong Merge64(ulong acc, ulong val)
        {
            acc ^= Round64(0, val);
            return unchecked(acc * P64_1 + P64_4);
        }

        private static uint Rotl32(uint x, int r) => (x << r) | (x >> (32 - r));

        private static ulong Rotl64(ulong x, int r) => (x << r) | (x >> (64 - r));

        private static uint Read32(byte[] d, int i) => (uint)(d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24);

        private static ulong Read64(byte[] d, int i) => Read32(d, i) | (ulong)Read32(d, i + 4) << 32;
    }
}
