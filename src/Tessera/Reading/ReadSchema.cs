using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Tessera;

/// <summary>
/// A schema prepared for reading: one entry per type, with what checking and translating a buffer need worked out once.
/// Built from a buffer's schema section (the classes it was written with) or from the compiled types reachable from a
/// root (the classes this program reads it as). Both use the entry encoding of docs/FORMAT.md.
/// </summary>
internal sealed class ReadSchema
{
    public ReadEntry[] Entries = Array.Empty<ReadEntry>();
    public int Root;

    /// <summary>The compiled types reachable from <paramref name="root"/>, every member of every union included.</summary>
    public static ReadSchema FromTypes(TesseraType root)
    {
        var index = new Dictionary<TesseraType, int>(ReferenceEqualityComparer.Instance);
        var order = new List<TesseraType>();
        var pending = new Stack<TesseraType>();
        index[root] = 0;
        order.Add(root);
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (TesseraType r in pending.Pop().References)
            {
                if (index.ContainsKey(r)) continue;
                index[r] = order.Count;
                order.Add(r);
                pending.Push(r);
            }
        }

        var entries = new ReadEntry[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            TesseraType t = order[i];
            if (!ReadEntry.TryParse(t.Entry, local => local == WireFormat.NoType || local >= t.References.Length ? -1 : index[t.References[local]],
                    out entries[i]))
            {
                throw new InvalidOperationException("The compiled schema entry of " + t.Name + " is malformed.");
            }

            entries[i].IsDictionary = t.IsDictionary;
        }

        var schema = new ReadSchema { Entries = entries, Root = 0 };
        if (!schema.Complete()) throw new InvalidOperationException("The compiled schema of " + root.Name + " is inconsistent.");
        return schema;
    }

    /// <summary>The schema section of <paramref name="buffer"/> (which has the flag), fully checked.</summary>
    public static bool TryParseSection(ReadOnlySpan<byte> buffer, out ReadSchema schema)
    {
        schema = null!;
        if (buffer.Length < WireFormat.HeaderSize + 8) return false;
        ReadOnlySpan<byte> section = buffer.Slice(WireFormat.HeaderSize);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(section);
        if (length < 8 || length > (uint)section.Length) return false;
        section = section.Slice(0, (int)length);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(section.Slice(4));
        int root = BinaryPrimitives.ReadUInt16LittleEndian(section.Slice(6));
        if (count == 0 || root >= count || 8L + 4L * count > length) return false;
        var entries = new ReadEntry[count];
        for (int i = 0; i < count; i++)
        {
            uint at = BinaryPrimitives.ReadUInt32LittleEndian(section.Slice(8 + 4 * i));
            if ((at & 3u) != 0 || at < 8u + 4u * (uint)count || at > length || length - at < 24) return false;
            if (!ReadEntry.TryParse(section.Slice((int)at), r => r == WireFormat.NoType ? -1 : r < count ? r : -2, out entries[i])) return false;
        }

        schema = new ReadSchema { Entries = entries, Root = root };
        return schema.Complete();
    }

    /// <summary>Checks the references between entries and works out each entry's derived layout.</summary>
    private bool Complete()
    {
        foreach (ReadEntry e in Entries)
        {
            switch (e.Category)
            {
                case ReadEntry.Object:
                    for (int f = 0; f < e.Count; f++)
                    {
                        if (!RefMatches(e.Kind[f], e.Ref[f])) return false;
                    }

                    break;
                case ReadEntry.Vector:
                    if (!RefMatches(e.ElemKind, e.ElemRef)) return false;
                    break;
                case ReadEntry.Union:
                    foreach (int m in e.Members)
                    {
                        if (m == -2 || (m >= 0 && Entries[m].Category != ReadEntry.Object)) return false;
                    }

                    break;
            }
        }

        foreach (ReadEntry e in Entries)
        {
            if (e.Category == ReadEntry.Object && !e.CompleteObject(this)) return false;
            if (e.Category == ReadEntry.Vector) e.CompleteVector(this);
        }

        return true;
    }

    private bool RefMatches(WireKind kind, int r)
    {
        if (r == -2) return false;  // out of range
        bool needs = kind is WireKind.Struct or WireKind.SharedStruct or WireKind.Object or WireKind.Vector or WireKind.Union;
        if (!needs) return r == -1;
        if (r == -1) return kind is not (WireKind.Struct or WireKind.SharedStruct);
        byte c = Entries[r].Category;
        return kind switch
        {
            WireKind.Struct or WireKind.SharedStruct => c == ReadEntry.Struct,
            WireKind.Object => c == ReadEntry.Object,
            WireKind.Vector => c == ReadEntry.Vector,
            _ => c == ReadEntry.Union,
        };
    }

    /// <summary>Cell size of a kind; 0 for bool, -1 for an inline struct (its entry has the size).</summary>
    public static int KindCellSize(WireKind k) => k switch
    {
        WireKind.Bool => 0,
        WireKind.Int8 or WireKind.UInt8 => 1,
        WireKind.Int16 or WireKind.UInt16 or WireKind.Char16 => 2,
        WireKind.Int32 or WireKind.UInt32 or WireKind.Float32 or WireKind.String or WireKind.Object or WireKind.Vector or WireKind.SharedStruct => 4,
        WireKind.Int64 or WireKind.UInt64 or WireKind.Float64 or WireKind.Union => 8,
        _ => -1,
    };

    public static bool IsReference(WireKind k) =>
        k is WireKind.String or WireKind.Object or WireKind.Vector or WireKind.Union or WireKind.SharedStruct;
}

/// <summary>One type of a <see cref="ReadSchema"/>.</summary>
internal sealed class ReadEntry
{
    public const byte Object = 1, Struct = 2, Vector = 3, Union = 4;

    public byte Category;
    public byte Flags;
    public int Count;
    public uint TypeId;
    public ulong Fingerprint;
    public ulong DeepFingerprint;

    // ---------------------------------------------------------------- objects
    public int Words;
    public int CellCount;
    public int FixedCount;
    public int FixedBytes;
    public bool IsDictionary;   // compiled schemas only: both vectors are always stored

    /// <summary>Per member, in wire order (fixed cells, cells, bools).</summary>
    public ulong[] Hash = Array.Empty<ulong>();
    public WireKind[] Kind = Array.Empty<WireKind>();
    public byte[] FieldFlags = Array.Empty<byte>();
    public int[] Ref = Array.Empty<int>();

    /// <summary>Per cell with a presence bit (bit j is member FixedCount + j): its size and alignment.</summary>
    public int[] CellSize = Array.Empty<int>();
    public int[] CellAlign = Array.Empty<int>();

    /// <summary>Per fixed cell: its position after the header words.</summary>
    public int[] FixedOffset = Array.Empty<int>();

    /// <summary>Per header word: the bits in use, the bools' present bits, and the cells that hold an offset.</summary>
    public uint[] Used = Array.Empty<uint>();
    public uint[] BoolPresent = Array.Empty<uint>();
    public uint[] RefMask = Array.Empty<uint>();

    /// <summary>The distinct cell sizes, and per size and header word the cells of that size ([size * Words + word]).</summary>
    public int[] Sizes = Array.Empty<int>();
    public uint[] SizeMasks = Array.Empty<uint>();

    public int BoolStart => (CellCount + 1) & ~1;

    // ---------------------------------------------------------------- structs
    public int StructSize;
    public int StructAlign;

    // ---------------------------------------------------------------- vectors
    public WireKind ElemKind;
    public byte ElemFlags;
    public int ElemRef;
    public int ElemSize;
    public int ElemAlign;

    public bool ElemOptional => (ElemFlags & WireFormat.ElementOptional) != 0;

    // ---------------------------------------------------------------- unions
    public uint[] Tags = Array.Empty<uint>();
    public int[] Members = Array.Empty<int>();

    /// <summary>
    /// Reads one entry. <paramref name="resolve"/> maps a 16-bit type reference to an entry index: -1 for none, -2 for
    /// one out of range.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> p, Func<int, int> resolve, out ReadEntry entry)
    {
        entry = new ReadEntry();
        if (p.Length < 24) return false;
        entry.Category = p[0];
        entry.Flags = p[1];
        entry.Count = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(2));
        entry.TypeId = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4));
        entry.Fingerprint = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(8));
        entry.DeepFingerprint = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(16));
        switch (entry.Category)
        {
            case Object: return entry.ParseObject(p, resolve);
            case Struct:
                if (p.Length < 28 || entry.Flags != 0) return false;
                entry.StructSize = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(24));
                entry.StructAlign = p[26];
                return entry.StructSize != 0 && entry.StructAlign is 1 or 2 or 4 or 8;
            case Vector:
            {
                if (p.Length < 28 || entry.Flags != 0) return false;
                entry.ElemKind = (WireKind)p[24];
                entry.ElemFlags = p[25];
                entry.ElemRef = resolve(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(26)));
                if (entry.ElemKind == WireKind.Invalid || (byte)entry.ElemKind > (byte)WireKind.SharedStruct) return false;
                if ((entry.ElemFlags & ~3) != 0) return false;
                return !entry.ElemOptional || entry.ElemKind == WireKind.Struct || (entry.ElemKind >= WireKind.Bool && entry.ElemKind <= WireKind.Char16);
            }
            case Union:
            {
                if (entry.Flags != 0 || p.Length < 24 + 8L * entry.Count) return false;
                entry.Tags = new uint[entry.Count];
                entry.Members = new int[entry.Count];
                for (int m = 0; m < entry.Count; m++)
                {
                    entry.Tags[m] = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(24 + 8 * m));
                    entry.Members[m] = resolve(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(28 + 8 * m)));
                }

                return true;
            }
            default:
                return false;
        }
    }

    private bool ParseObject(ReadOnlySpan<byte> p, Func<int, int> resolve)
    {
        if ((Flags & ~WireFormat.EntryFixedCells) != 0) return false;
        bool hasFixed = (Flags & WireFormat.EntryFixedCells) != 0;
        int first = hasFixed ? 32 : 28;
        if (p.Length < first + 12L * Count) return false;
        Words = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(24));
        CellCount = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(26));
        FixedCount = hasFixed ? BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(28)) : 0;
        FixedBytes = hasFixed ? BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(30)) : 0;
        if (FixedCount + CellCount > Count || FixedBytes > WireFormat.MaxFixedBytes || (hasFixed && FixedCount == 0)) return false;
        int bits = BoolStart + 2 * (Count - FixedCount - CellCount);
        if (Words != Math.Max(1, (bits + 31) / 32)) return false;
        Hash = new ulong[Count];
        Kind = new WireKind[Count];
        FieldFlags = new byte[Count];
        Ref = new int[Count];
        for (int f = 0; f < Count; f++)
        {
            ReadOnlySpan<byte> field = p.Slice(first + 12 * f);
            Hash[f] = BinaryPrimitives.ReadUInt64LittleEndian(field);
            WireKind k = Kind[f] = (WireKind)field[8];
            FieldFlags[f] = field[9];
            Ref[f] = resolve(BinaryPrimitives.ReadUInt16LittleEndian(field.Slice(10)));
            if (k == WireKind.Invalid || (byte)k > (byte)WireKind.SharedStruct) return false;
            if ((k == WireKind.Bool) != (f >= FixedCount + CellCount)) return false;
            // Fixed cells: exactly the first FixedCount members, all numbers, enums or inline structs.
            bool isFixed = (FieldFlags[f] & WireFormat.FieldFixed) != 0;
            if (isFixed != (f < FixedCount) || (isFixed && !(k == WireKind.Struct || (k >= WireKind.Int8 && k <= WireKind.Char16)))) return false;
        }

        return true;
    }

    /// <summary>Cell sizes (structs from their entries), fixed cell positions, and the masks of each header word.</summary>
    public bool CompleteObject(ReadSchema s)
    {
        CellSize = new int[CellCount];
        CellAlign = new int[CellCount];
        FixedOffset = new int[FixedCount];
        int end = 0;
        for (int f = 0; f < FixedCount; f++)
        {
            FixedOffset[f] = end;
            end += FieldCellSize(s, f);
        }

        int align = 1;
        for (int j = 0; j < CellCount; j++)
        {
            int f = FixedCount + j;
            CellSize[j] = FieldCellSize(s, f);
            CellAlign[j] = Kind[f] switch
            {
                WireKind.Struct => s.Entries[Ref[f]].StructAlign,
                WireKind.Union => 4,
                _ => Math.Max(1, ReadSchema.KindCellSize(Kind[f])),
            };
            align = Math.Max(align, CellAlign[j]);
        }

        // The fixed cells, back to back, padded to the other cells' largest alignment.
        if (FixedBytes != (end + align - 1) / align * align) return false;

        Used = new uint[Words];
        BoolPresent = new uint[Words];
        RefMask = new uint[Words];
        var sizes = new List<int>();
        for (int j = 0; j < CellCount; j++)
        {
            Used[j >> 5] |= 1u << (j & 31);
            if (ReadSchema.IsReference(Kind[FixedCount + j])) RefMask[j >> 5] |= 1u << (j & 31);
            if (!sizes.Contains(CellSize[j])) sizes.Add(CellSize[j]);
        }

        for (int b = 0; b < Count - FixedCount - CellCount; b++)
        {
            int bit = BoolStart + 2 * b;
            Used[bit >> 5] |= 3u << (bit & 31);
            BoolPresent[bit >> 5] |= 1u << (bit & 31);
        }

        Sizes = sizes.ToArray();
        SizeMasks = new uint[Sizes.Length * Words];
        for (int j = 0; j < CellCount; j++) SizeMasks[Array.IndexOf(Sizes, CellSize[j]) * Words + (j >> 5)] |= 1u << (j & 31);
        return true;
    }

    public void CompleteVector(ReadSchema s)
    {
        ElemSize = ElemKind switch
        {
            WireKind.Bool => 1,
            WireKind.Struct => s.Entries[ElemRef].StructSize,
            _ => ReadSchema.KindCellSize(ElemKind),
        };
        ElemAlign = ElemKind switch
        {
            WireKind.Bool => 1,
            WireKind.Struct => s.Entries[ElemRef].StructAlign,
            WireKind.Union => 4,
            _ => ElemSize,
        };
    }

    private int FieldCellSize(ReadSchema s, int f) =>
        Kind[f] == WireKind.Struct ? s.Entries[Ref[f]].StructSize : ReadSchema.KindCellSize(Kind[f]);

    /// <summary>The presence bit of member <paramref name="f"/> (a cell or a bool; fixed cells have none).</summary>
    public int FieldBit(int f)
    {
        int c = f - FixedCount;
        return c < CellCount ? c : BoolStart + 2 * (c - CellCount);
    }

    /// <summary>
    /// The bytes of the present cells before cell <paramref name="bit"/> (all of them for <see cref="CellCount"/>), given
    /// the header words at <paramref name="header"/>.
    /// </summary>
    public int CellsBefore(ReadOnlySpan<byte> header, int bit)
    {
        int bytes = 0;
        int lastWord = bit >> 5;
        for (int w = 0; w <= lastWord && w < Words; w++)
        {
            uint h = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4 * w));
            if (w == lastWord) h &= (1u << (bit & 31)) - 1u;
            for (int s = 0; s < Sizes.Length; s++) bytes += Sizes[s] * BitOperations.PopCount(h & SizeMasks[s * Words + w]);
        }

        return bytes;
    }
}
