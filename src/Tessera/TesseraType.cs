using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tessera;

/// <summary>
/// Schema entry of one generated type (object, struct, vector or union). Created by generated code; it carries the
/// pre-encoded schema entry that the writer copies into buffers when <see cref="TesseraOptions.IncludeSchema"/> is on.
/// </summary>
public sealed class TesseraType
{
    private static int s_nextId;

    private TesseraType[] _refs = Array.Empty<TesseraType>();

    /// <summary>Creates a type entry. Called by generated code.</summary>
    /// <param name="name">Display name (C# type name).</param>
    /// <param name="fingerprint">Fingerprint of the type's own layout.</param>
    /// <param name="deepFingerprint">Fingerprint of the type and everything reachable from it.</param>
    /// <param name="entry">Encoded schema entry; type references hold indices into <see cref="SetReferences"/>.</param>
    /// <param name="refSlots">Byte offsets of the 16-bit type references inside <paramref name="entry"/>.</param>
    public TesseraType(string name, ulong fingerprint, ulong deepFingerprint, byte[] entry, ushort[] refSlots)
    {
        Id = Interlocked.Increment(ref s_nextId) - 1;
        Name = name;
        Fingerprint = fingerprint;
        DeepFingerprint = deepFingerprint;
        Entry = entry;
        RefSlots = refSlots;
    }

    /// <summary>Process-wide dense id used by writers to track which entries a buffer needs.</summary>
    internal int Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Fingerprint of this type's own layout (member hashes, kinds and order).</summary>
    public ulong Fingerprint { get; }

    /// <summary>Fingerprint of this type's whole reachable schema; stored in buffer headers for root types.</summary>
    public ulong DeepFingerprint { get; }

    /// <summary>Category of the entry.</summary>
    public EntryCategory Category => (EntryCategory)Entry[0];

    internal byte[] Entry { get; }

    internal ushort[] RefSlots { get; }

    internal TesseraType[] References => _refs;

    /// <summary>Sets the types referenced by <see cref="Entry"/> (separate step so cyclic models can be built).</summary>
    public void SetReferences(params TesseraType[] references) => _refs = references;

    private ObjectCells? _cells;

    /// <summary>The most offsets an object of this type can hold.</summary>
    internal int MaxReferenceOffsets => (_cells ??= ObjectCells.From(this)).RefCount;

    /// <summary>
    /// Where an object of this type, whose bytes are <paramref name="item"/>, holds offsets to other items: worked out
    /// from its header bits and this type's layout. Writes them, ascending, into <paramref name="offsets"/> and returns
    /// how many there are. Only deduplication (<see cref="Sharing.All"/>) needs them.
    /// </summary>
    internal int ReferenceOffsets(ReadOnlySpan<byte> item, Span<int> offsets)
    {
        ObjectCells cells = _cells ??= ObjectCells.From(this);
        int n = 0, at = cells.Start;
        for (int w = 0; w < cells.Masks.Length; w++)
        {
            uint bits = MemoryMarshal.Read<uint>(item.Slice(4 * w)) & cells.Masks[w];
            while (bits != 0)
            {
                int i = 32 * w + BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                if (cells.RefAt[i] >= 0) offsets[n++] = at + cells.RefAt[i];
                at += cells.Size[i];
            }
        }

        return n;
    }

    /// <summary>An object's optional cells, read from its schema entry: each one's size and where in it an offset is.</summary>
    private sealed class ObjectCells
    {
        public int Start;
        public int RefCount;
        public uint[] Masks = Array.Empty<uint>();
        public int[] Size = Array.Empty<int>();
        public int[] RefAt = Array.Empty<int>();   // -1: no offset

        public static ObjectCells From(TesseraType type)
        {
            // Object entry: the common 24-byte header, u16 header words, u16 cells, [u16 fixed cells, u16 fixed bytes],
            // then 12 bytes per member in wire order (fixed cells, cells, bools): u64 name hash, u8 kind, u8 flags,
            // u16 type reference.
            ReadOnlySpan<byte> e = type.Entry;
            bool hasFixed = (e[1] & WireFormat.EntryFixedCells) != 0;
            int words = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(24));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(26));
            int fixedCount = hasFixed ? BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(28)) : 0;
            int fixedBytes = hasFixed ? BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(30)) : 0;
            int first = (hasFixed ? 32 : 28) + 12 * fixedCount;
            var cells = new ObjectCells { Start = 4 * words + fixedBytes, Masks = new uint[words], Size = new int[count], RefAt = new int[count] };
            for (int i = 0; i < count; i++)
            {
                int f = first + 12 * i;
                var kind = (WireKind)e[f + 8];
                cells.Masks[i >> 5] |= 1u << (i & 31);
                cells.RefAt[i] = -1;
                switch (kind)
                {
                    case WireKind.Struct:
                        // A struct entry holds its size right after the common header.
                        int reference = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(f + 10));
                        cells.Size[i] = BinaryPrimitives.ReadUInt16LittleEndian(type.References[reference].Entry.AsSpan(24));
                        break;
                    case WireKind.Union:
                        cells.Size[i] = 8;   // a tag, then the offset
                        cells.RefAt[i] = 4;
                        cells.RefCount++;
                        break;
                    case WireKind.String:
                    case WireKind.Object:
                    case WireKind.Vector:
                    case WireKind.SharedStruct:
                        cells.Size[i] = 4;
                        cells.RefAt[i] = 0;
                        cells.RefCount++;
                        break;
                    default:
                        cells.Size[i] = WireFormat.CellSize(kind);
                        break;
                }
            }

            return cells;
        }
    }
}
