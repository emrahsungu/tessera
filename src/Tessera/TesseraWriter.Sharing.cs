using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tessera;

public sealed partial class TesseraWriter
{
    private readonly Dictionary<string, int> _strings = new();
    private Entry[] _entries = new Entry[256];
    private int _entryCount;
    private int[] _slots = new int[512];

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
        _strings.Clear();
        if (_entryCount != 0)
        {
            Array.Clear(_slots);
            _entryCount = 0;
        }
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
