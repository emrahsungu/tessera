using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Tessera.Tests;

/// <summary>
/// Minimal schema-driven reader used by the .NET tests: decodes a buffer through its embedded schema section into
/// dictionaries keyed by member name hash. Independent of the C++ runtime, so the two implementations check each other.
/// </summary>
public sealed class BufferReader
{
    private readonly byte[] _b;
    private readonly int _schema;
    private readonly int _count;

    public BufferReader(byte[] buffer)
    {
        _b = buffer;
        if (U16(4) != WireFormat.Magic || _b[6] != WireFormat.Version) throw new FormatException("not a Tessera buffer");
        if ((_b[7] & WireFormat.FlagSchema) == 0) throw new FormatException("buffer has no schema section");
        _schema = 16;
        _count = U16(_schema + 4);
        RootEntry = U16(_schema + 6);
        RootOffset = (int)U32(0);
        Fingerprint = BinaryPrimitives.ReadUInt64LittleEndian(_b.AsSpan(8));
    }

    public int RootEntry { get; }
    public int RootOffset { get; }
    public ulong Fingerprint { get; }
    public int EntryCount => _count;

    public Dictionary<ulong, object?> Root() => Object(RootEntry, RootOffset);

    public static ulong Hash(string memberName) => XxHash.Hash64(memberName);

    private int Entry(int index) => _schema + (int)U32(_schema + 8 + 4 * index);

    private Dictionary<ulong, object?> Object(int entry, int at)
    {
        int e = Entry(entry);
        bool hasFixed = (_b[e + 1] & WireFormat.EntryFixedCells) != 0;
        int count = U16(e + 2), words = U16(e + 24), cells = U16(e + 26);
        int fixedCount = hasFixed ? U16(e + 28) : 0, fixedSize = hasFixed ? U16(e + 30) : 0, fields = hasFixed ? 32 : 28;
        int boolStart = (cells + 1) & ~1;
        var result = new Dictionary<ulong, object?>();
        int fixedAt = 4 * words;      // fixed cells: always stored, back to back
        int off = 4 * words + fixedSize;
        for (int j = 0; j < count; j++)
        {
            int f = e + fields + 12 * j;
            ulong hash = U64(f);
            var kind = (WireKind)_b[f + 8];
            int typeRef = U16(f + 10);
            if (j < fixedCount)
            {
                Assert.True((_b[f + 9] & WireFormat.FieldFixed) != 0);
                result[hash] = Value(kind, typeRef, at + fixedAt);
                fixedAt += kind == WireKind.Struct ? U16(Entry(typeRef) + 24) : WireFormat.CellSize(kind);
                continue;
            }

            int c = j - fixedCount;
            int bit = c < cells ? c : boolStart + 2 * (c - cells);
            bool present = ((U32(at + 4 * (bit / 32)) >> (bit % 32)) & 1) != 0;
            if (kind == WireKind.Bool)
            {
                if (present) result[hash] = ((U32(at + 4 * ((bit + 1) / 32)) >> ((bit + 1) % 32)) & 1) != 0;
                continue;
            }

            int size = kind == WireKind.Struct ? U16(Entry(typeRef) + 24) : WireFormat.CellSize(kind);
            if (present)
            {
                result[hash] = Value(kind, typeRef, at + off);
                off += size;
            }
        }

        return result;
    }

    private object? Value(WireKind kind, int typeRef, int cell)
    {
        switch (kind)
        {
            case WireKind.Int8: return (sbyte)_b[cell];
            case WireKind.UInt8: return _b[cell];
            case WireKind.Int16: return (short)U16(cell);
            case WireKind.UInt16: return U16(cell);
            case WireKind.Char16: return (char)U16(cell);
            case WireKind.Int32: return (int)U32(cell);
            case WireKind.UInt32: return U32(cell);
            case WireKind.Int64: return BinaryPrimitives.ReadInt64LittleEndian(_b.AsSpan(cell));
            case WireKind.UInt64: return BinaryPrimitives.ReadUInt64LittleEndian(_b.AsSpan(cell));
            case WireKind.Float32: return BinaryPrimitives.ReadSingleLittleEndian(_b.AsSpan(cell));
            case WireKind.Float64: return BinaryPrimitives.ReadDoubleLittleEndian(_b.AsSpan(cell));
            case WireKind.Struct: return _b.AsSpan(cell, U16(Entry(typeRef) + 24)).ToArray();
            case WireKind.SharedStruct: return _b.AsSpan(Follow(cell), U16(Entry(typeRef) + 24)).ToArray();
            case WireKind.String: return String(Follow(cell));
            case WireKind.Object: return Object(typeRef, Follow(cell));
            case WireKind.Vector: return Vector(typeRef, Follow(cell));
            case WireKind.Union: return Union(typeRef, U32(cell), Follow(cell + 4));
            default: throw new FormatException("unknown kind " + kind);
        }
    }

    private (uint Tag, Dictionary<ulong, object?> Value) Union(int entry, uint tag, int at)
    {
        int e = Entry(entry);
        for (int m = 0; m < U16(e + 2); m++)
        {
            if (U32(e + 24 + 8 * m) == tag) return (tag, Object(U16(e + 28 + 8 * m), at));
        }

        throw new FormatException("unknown union tag");
    }

    private List<object?> Vector(int entry, int at)
    {
        int e = Entry(entry);
        var kind = (WireKind)_b[e + 24];
        int elemRef = U16(e + 26);
        int n = (int)U32(at);
        var list = new List<object?>(n);
        int size = kind == WireKind.Bool ? 1 : kind == WireKind.Struct ? U16(Entry(elemRef) + 24) : WireFormat.CellSize(kind);
        if ((_b[e + 25] & WireFormat.ElementOptional) != 0)
        {
            // Presence bits, then every element's value.
            int values = at + 4 + 4 * ((n + 31) / 32);
            for (int i = 0; i < n; i++)
            {
                bool present = ((U32(at + 4 + 4 * (i / 32)) >> (i % 32)) & 1) != 0;
                list.Add(!present ? null : kind == WireKind.Bool ? _b[values + i] != 0 : Value(kind, elemRef, values + i * size));
            }

            return list;
        }

        for (int i = 0; i < n; i++)
        {
            int slot = at + 4 + i * size;
            if (kind == WireKind.Bool) list.Add(_b[slot] != 0);
            else if (WireFormat.IsReference(kind) && kind != WireKind.Union && U32(slot) == 0) list.Add(null);
            else if (kind == WireKind.Union && U32(slot + 4) == 0) list.Add(null);
            else list.Add(Value(kind, elemRef, slot));
        }

        return list;
    }

    private string String(int at) => Encoding.UTF8.GetString(_b, at + 4, (int)U32(at));

    private int Follow(int slot) => slot + (int)U32(slot);

    private ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_b.AsSpan(at));

    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(at));

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_b.AsSpan(at));
}
