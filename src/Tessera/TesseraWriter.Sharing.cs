using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tessera;

public sealed partial class TesseraWriter
{
    // Strings written so far, by content: open addressing with linear probing, at most half full; a slot is free when
    // its Value is null. Faster than a Dictionary<string, int> for this one job (find, or add after writing).
    private StringSlot[] _stringSlots = new StringSlot[64];
    private int _stringCount;
    private (string Value, int Pos)[] _pendingStrings = Array.Empty<(string, int)>();  // written, not yet in the table
    private int _pendingCount;
    private Entry[] _entries = new Entry[256];
    private int _entryCount;
    private int[] _slots = new int[512];

    private struct StringSlot
    {
        public string? Value;
        public int Pos;
        public int Hash;
    }

    private struct Entry
    {
        public ulong Hash;
        public int Pos;
        public int Size;
        public int Domain;
        public int Next;
    }

    private void ResetSharing()
    {
        if (_stringCount != 0)
        {
            // Clearing costs the table's size: a table grown by an earlier, larger buffer is replaced by a smaller one.
            if (_stringSlots.Length > 1024 && _stringCount < _stringSlots.Length / 8)
                _stringSlots = new StringSlot[Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)_stringCount * 4))];
            else
                Array.Clear(_stringSlots);
            _stringCount = 0;
        }

        if (_pendingCount != 0)
        {
            Array.Clear(_pendingStrings, 0, _pendingCount);
            _pendingCount = 0;
        }
        if (_entryCount != 0)
        {
            Array.Clear(_slots);
            _entryCount = 0;
        }
    }

    /// <summary>
    /// The position of a string equal to <paramref name="value"/> written before, or 0 (no item is at 0); either way
    /// <paramref name="slot"/> is where it is, or where to add it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindString(string value, int hash, out int slot)
    {
        StringSlot[] slots = _stringSlots;
        int mask = slots.Length - 1;
        ref StringSlot first = ref MemoryMarshal.GetArrayDataReference(slots);
        for (int i = hash & mask; ; i = (i + 1) & mask)
        {
            ref StringSlot s = ref Unsafe.Add(ref first, i);
            string? known = s.Value;
            if (known is null || (s.Hash == hash && string.Equals(known, value)))
            {
                slot = i;
                return known is null ? 0 : s.Pos;
            }
        }
    }

    /// <summary>Adds a string at the free slot <see cref="FindString"/> returned (the table must not have changed since).</summary>
    private void AddString(int slot, string value, int hash, int pos)
    {
        _stringSlots[slot] = new StringSlot { Value = value, Pos = pos, Hash = hash };
        if (++_stringCount * 2 > _stringSlots.Length) GrowStrings();
    }

    private void GrowStrings()
    {
        var slots = new StringSlot[_stringSlots.Length * 2];
        int mask = slots.Length - 1;
        foreach (StringSlot s in _stringSlots)
        {
            if (s.Value is null) continue;
            int i = s.Hash & mask;
            while (slots[i].Value is not null) i = (i + 1) & mask;
            slots[i] = s;
        }

        _stringSlots = slots;
    }

    // Seeded per process: equal strings still find each other, and the table's layout (never the buffer) cannot be
    // predicted, so chosen strings cannot pile up in one place.
    private static readonly ulong s_stringSeed = (ulong)Random.Shared.NextInt64();

    /// <summary>
    /// A hash of the string's UTF-16 code units. Up to 8 chars, the usual case: its first and last 8 bytes (overlapping
    /// when it is shorter) multiplied once. Longer strings take 16 bytes per step first.
    /// </summary>
    private static int HashString(string value)
    {
        const ulong k1 = 0xE7037ED1A0B428DBUL, k2 = 0x8EBC6AF09C88C6E3UL;
        ref byte p = ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(value.AsSpan()));
        int n = value.Length * 2;
        ulong seed = s_stringSeed ^ (ulong)n;
        ulong a, b;
        if (n >= 8)
        {
            if (n > 16)
            {
                for (int i = 0; i < n - 16; i += 16)
                    seed = Mix(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref p, i)) ^ seed, Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref p, i + 8)) ^ k2);
                a = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref p, n - 16));
            }
            else
            {
                a = Unsafe.ReadUnaligned<ulong>(ref p);
            }

            b = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref p, n - 8));
        }
        else if (n >= 4)
        {
            a = Unsafe.ReadUnaligned<uint>(ref p);
            b = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref p, n - 4));
        }
        else
        {
            a = n != 0 ? Unsafe.ReadUnaligned<ushort>(ref p) : 0UL;
            b = 0;
        }

        ulong h = Mix(a ^ seed, b ^ k1);
        return (int)(h ^ (h >> 32));
    }

    // Items without inner offsets: equal bytes means equal value. Domain separates alignments.
    private int InternBytes(int pos, int size, int align)
    {
        ReadOnlySpan<byte> item = ItemSpan(pos, size);
        ulong hash = HashBytes(item, (ulong)align);
        int domain = align;
        for (int e = _slots[(int)hash & (_slots.Length - 1)] - 1; e >= 0; e = _entries[e].Next)
        {
            ref Entry entry = ref _entries[e];
            if (entry.Hash == hash && entry.Domain == domain && entry.Size == size &&
                ItemSpan(entry.Pos, size).SequenceEqual(item))
            {
                Rollback();
                return entry.Pos;
            }
        }

        Add(hash, pos, size, domain);
        return pos;
    }

    // Reference vectors: compare targets (and union tags), not the relative offsets.
    private int InternRefVector(int pos, int count, int stride)
    {
        int size = 4 + count * stride;
        int domain = stride == 4 ? 1 : 2;
        ReadOnlySpan<byte> item = ItemSpan(pos, size);
        ulong hash = (ulong)size * 0x9E3779B97F4A7C15UL ^ (ulong)domain;
        for (int i = 0; i < count; i++)
        {
            int slot = 4 + i * stride + (stride - 4);
            ulong tag = stride == 8 ? MemoryMarshal.Read<uint>(item.Slice(slot - 4)) : 0;
            hash = Mix(hash ^ Target(pos, slot, item) ^ tag << 32, 0xE7037ED1A0B428DBUL);
        }

        for (int e = _slots[(int)hash & (_slots.Length - 1)] - 1; e >= 0; e = _entries[e].Next)
        {
            ref Entry entry = ref _entries[e];
            if (entry.Hash == hash && entry.Domain == domain && entry.Size == size && RefVectorEqual(pos, entry.Pos, count, stride))
            {
                Rollback();
                return entry.Pos;
            }
        }

        Add(hash, pos, size, domain);
        return pos;
    }

    private bool RefVectorEqual(int a, int b, int count, int stride)
    {
        int size = 4 + count * stride;
        ReadOnlySpan<byte> x = ItemSpan(a, size), y = ItemSpan(b, size);
        for (int i = 0; i < count; i++)
        {
            int slot = 4 + i * stride + (stride - 4);
            if (stride == 8 && MemoryMarshal.Read<uint>(x.Slice(slot - 4)) != MemoryMarshal.Read<uint>(y.Slice(slot - 4))) return false;
            if (Target(a, slot, x) != Target(b, slot, y)) return false;
        }

        return true;
    }

    // Items with inner offsets: canonical form replaces each offset by its absolute target position.
    private int InternCanonical(int pos, int size, int domain, ReadOnlySpan<int> refOffsets)
    {
        ulong hash = HashCanonical(pos, size, refOffsets) ^ (ulong)domain * 0x9E3779B97F4A7C15UL;
        for (int e = _slots[(int)hash & (_slots.Length - 1)] - 1; e >= 0; e = _entries[e].Next)
        {
            ref Entry entry = ref _entries[e];
            if (entry.Hash == hash && entry.Domain == domain && entry.Size == size &&
                CanonicalEqual(pos, entry.Pos, size, refOffsets))
            {
                Rollback();
                return entry.Pos;
            }
        }

        Add(hash, pos, size, domain);
        return pos;
    }

    private void Add(ulong hash, int pos, int size, int domain)
    {
        if (_entryCount == _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);
        if (_entryCount * 2 >= _slots.Length) Rehash(_slots.Length * 2);
        int bucket = (int)hash & (_slots.Length - 1);
        _entries[_entryCount] = new Entry { Hash = hash, Pos = pos, Size = size, Domain = domain, Next = _slots[bucket] - 1 };
        _slots[bucket] = ++_entryCount;
    }

    private void Rehash(int slotCount)
    {
        _slots = new int[slotCount];
        for (int i = 0; i < _entryCount; i++)
        {
            int bucket = (int)_entries[i].Hash & (slotCount - 1);
            _entries[i].Next = _slots[bucket] - 1;
            _slots[bucket] = i + 1;
        }
    }

    private uint Target(int itemPos, int offset, ReadOnlySpan<byte> item)
    {
        uint rel = MemoryMarshal.Read<uint>(item.Slice(offset));
        return rel == 0 ? 0u : (uint)(itemPos - offset) - rel;
    }

    private ulong HashCanonical(int pos, int size, ReadOnlySpan<int> refOffsets)
    {
        ReadOnlySpan<byte> item = ItemSpan(pos, size);
        ulong h = (ulong)size;
        int prev = 0;
        foreach (int o in refOffsets)
        {
            h = HashBytes(item.Slice(prev, o - prev), h);
            h = Mix(h ^ Target(pos, o, item), 0xA0761D6478BD642FUL);
            prev = o + 4;
        }

        return HashBytes(item.Slice(prev), h);
    }

    private bool CanonicalEqual(int a, int b, int size, ReadOnlySpan<int> refOffsets)
    {
        ReadOnlySpan<byte> x = ItemSpan(a, size), y = ItemSpan(b, size);
        int prev = 0;
        foreach (int o in refOffsets)
        {
            if (!x.Slice(prev, o - prev).SequenceEqual(y.Slice(prev, o - prev))) return false;
            if (Target(a, o, x) != Target(b, o, y)) return false;
            prev = o + 4;
        }

        return x.Slice(prev).SequenceEqual(y.Slice(prev));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong a, ulong b)
    {
        ulong high = Math.BigMul(a, b, out ulong low);
        return high ^ low;
    }

    private static ulong HashBytes(ReadOnlySpan<byte> data, ulong seed)
    {
        const ulong k0 = 0xA0761D6478BD642FUL, k1 = 0xE7037ED1A0B428DBUL;
        ulong h = seed ^ k0;
        int i = 0;
        for (; i + 8 <= data.Length; i += 8) h = Mix(h ^ MemoryMarshal.Read<ulong>(data.Slice(i)), k1);
        ulong tail = 0;
        for (int shift = 0; i < data.Length; i++, shift += 8) tail |= (ulong)data[i] << shift;
        return Mix(h ^ tail ^ (ulong)data.Length, k1);
    }
}
