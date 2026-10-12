using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Tessera;

/// <summary>
/// How to read a type written with another version of its class: for each of the reader's members, where the writer
/// stored it. <see cref="Binder"/> makes them; <see cref="Translator"/> uses them.
/// </summary>
internal sealed class Binding
{
    /// <summary>Marks a member whose writer layout cannot be read as the reader's type (it reads as absent).</summary>
    public static readonly Binding Incompatible = new();

    public int Words = 1;       // the writer's header words
    public int Fixed;           // the writer's bytes of fixed cells
    public FieldBinding[] Fields = Array.Empty<FieldBinding>();   // objects: per reader member, in wire order
    public List<Term> Terms = new();
    public Binding? Element;    // vectors
    public List<(uint Tag, Binding? Binding)> Members = new();     // unions

    public bool TryMember(uint tag, out Binding? binding)
    {
        foreach (var (t, b) in Members)
        {
            if (t != tag) continue;
            binding = b;
            return true;
        }

        binding = null;
        return false;
    }
}

internal struct FieldBinding
{
    public const int Missing = -1, FixedCell = -2;

    public int Bit;             // the writer's presence bit, Missing, or FixedCell
    public int FixedAt;         // FixedCell: the cell's position in the writer's object
    public int TermStart;       // the writer's cell offset: these terms, after its header and fixed cells
    public int TermCount;
    public Binding? Child;      // objects, vectors, unions: null when the layouts are the same
}

/// <summary>"Size x population count of (header word &amp; mask)": one part of a cell's offset.</summary>
internal struct Term
{
    public int Word;
    public int Size;
    public uint Mask;
}

/// <summary>
/// Matches the reader's compiled schema against the writer's schema section, member by member, by name hash and kind
/// (and struct shape). Whether two layouts are the same is decided by comparing them, never by trusting the fingerprints
/// stored in the buffer, so a forged schema cannot make the reader skip the checks.
/// </summary>
internal sealed class Binder
{
    private readonly ReadSchema _r;
    private readonly ReadSchema _w;
    private readonly Dictionary<(int, int), Memo> _memo = new();

    private sealed class Memo
    {
        public Binding? Binding;
        public bool InProgress;
        public bool Cyclic;
    }

    public Binder(ReadSchema reader, ReadSchema writer)
    {
        _r = reader;
        _w = writer;
    }

    /// <summary>
    /// The binding of reader entry <paramref name="r"/> to writer entry <paramref name="e"/>: null when the whole subtree has
    /// the same layout, <see cref="Binding.Incompatible"/> when it cannot be read.
    /// </summary>
    public Binding? Bind(int r, int e)
    {
        if (e < 0) return Binding.Incompatible;
        if (_memo.TryGetValue((r, e), out Memo? m))
        {
            if (m.InProgress) m.Cyclic = true;
            return m.Binding;
        }

        ReadEntry R = _r.Entries[r], E = _w.Entries[e];
        if (R.Category == ReadEntry.Object && E.Category == ReadEntry.Object) return BindObject(r, e, R, E);
        if (R.Category == ReadEntry.Union && E.Category == ReadEntry.Union) return BindUnion(R, E);
        if (R.Category == ReadEntry.Vector && E.Category == ReadEntry.Vector) return BindVector(R, E);
        return Binding.Incompatible;
    }

    /// <summary>Struct-valued members must agree on the struct's shape and size (the reader copies its bytes).</summary>
    private bool StructMatches(WireKind kind, int readerRef, int writerRef)
    {
        if (kind is not (WireKind.Struct or WireKind.SharedStruct)) return true;
        if (readerRef < 0 || writerRef < 0) return false;
        ReadEntry a = _r.Entries[readerRef], b = _w.Entries[writerRef];
        return a.Fingerprint == b.Fingerprint && a.StructSize == b.StructSize;
    }

    private static bool HasChild(WireKind k) => k is WireKind.Object or WireKind.Vector or WireKind.Union;

    private Binding? BindObject(int r, int e, ReadEntry R, ReadEntry E)
    {
        var b = new Binding { Words = E.Words, Fixed = E.FixedBytes, Fields = new FieldBinding[R.Count] };
        var memo = new Memo { Binding = b, InProgress = true };
        _memo[(r, e)] = memo;
        bool identical = R.Count == E.Count && R.CellCount == E.CellCount && R.Words == E.Words && R.FixedBytes == E.FixedBytes;
        bool childrenIdentical = true;
        var byHash = new Dictionary<ulong, int>(E.Count);
        for (int j = 0; j < E.Count; j++) byHash.TryAdd(E.Hash[j], j);
        for (int i = 0; i < R.Count; i++)
        {
            ref FieldBinding fb = ref b.Fields[i];
            fb.Bit = FieldBinding.Missing;
            if (!byHash.TryGetValue(R.Hash[i], out int j))
            {
                identical = false;
                continue;
            }

            WireKind k = E.Kind[j];
            if (k != R.Kind[i] || !StructMatches(k, R.Ref[i], E.Ref[j]))
            {
                identical = false;
                continue;
            }

            bool writerFixed = j < E.FixedCount, readerFixed = i < R.FixedCount;
            int writerAt = writerFixed ? E.FixedOffset[j] : E.FieldBit(j);
            int readerAt = readerFixed ? R.FixedOffset[i] : R.FieldBit(i);
            if (j != i || writerFixed != readerFixed || writerAt != readerAt ||
                (k != WireKind.Bool && !writerFixed && E.CellSize[j - E.FixedCount] != (readerFixed ? -1 : R.CellSize[i - R.FixedCount])))
            {
                identical = false;
            }

            if (HasChild(k))
            {
                Binding? child = Bind(R.Ref[i], E.Ref[j]);
                if (child == Binding.Incompatible)
                {
                    identical = false;
                    continue;
                }

                fb.Child = child;
                if (child != null) childrenIdentical = false;
            }

            if (writerFixed)
            {
                fb.Bit = FieldBinding.FixedCell;
                fb.FixedAt = 4 * E.Words + E.FixedOffset[j];
                continue;
            }

            fb.Bit = E.FieldBit(j);
            if (k == WireKind.Bool) continue;
            // The writer's present cells before cell j: one term per cell size and header word.
            int bit = j - E.FixedCount;
            fb.TermStart = b.Terms.Count;
            for (int s = 0; s < E.Sizes.Length; s++)
            {
                for (int w = 0; w <= bit >> 5; w++)
                {
                    uint mask = E.SizeMasks[s * E.Words + w];
                    if (w == bit >> 5) mask &= (1u << (bit & 31)) - 1u;
                    if (mask != 0) b.Terms.Add(new Term { Word = w, Size = E.Sizes[s], Mask = mask });
                }
            }

            fb.TermCount = b.Terms.Count - fb.TermStart;
        }

        memo.InProgress = false;
        if (identical && childrenIdentical && !memo.Cyclic)
        {
            memo.Binding = null;   // nothing holds b yet: the whole subtree reads with the reader's own positions
            return null;
        }

        return b;
    }

    private Binding? BindUnion(ReadEntry R, ReadEntry E)
    {
        var members = new List<(uint, Binding?)>();
        bool identical = true;
        for (int k = 0; k < R.Count; k++)
        {
            for (int m = 0; m < E.Count; m++)
            {
                if (E.Tags[m] != R.Tags[k]) continue;
                Binding? mb = R.Members[k] < 0 ? Binding.Incompatible : Bind(R.Members[k], E.Members[m]);
                if (mb != null) identical = false;
                members.Add((R.Tags[k], mb));
                break;
            }
        }

        return identical ? null : new Binding { Members = members };
    }

    private Binding? BindVector(ReadEntry R, ReadEntry E)
    {
        WireKind k = E.ElemKind;
        if (k != R.ElemKind || E.ElemOptional != R.ElemOptional || !StructMatches(k, R.ElemRef, E.ElemRef)) return Binding.Incompatible;
        if (k != WireKind.SharedStruct && E.ElemSize != R.ElemSize) return Binding.Incompatible;
        if (!HasChild(k)) return null;
        Binding? child = Bind(R.ElemRef, E.ElemRef);
        if (child == Binding.Incompatible || child == null) return child;
        return new Binding { Element = child };
    }
}

/// <summary>Writes a buffer back to front, like the .NET writer: an item's position is its distance from the end.</summary>
internal sealed class Builder
{
    private byte[] _buf = new byte[1024];
    private int _head = 1024;

    public int Position => _buf.Length - _head;

    public Span<byte> Bytes => _buf;

    /// <summary>
    /// Reserves <paramref name="size"/> bytes in front of everything written so far, padded so that the point
    /// <paramref name="alignAt"/> bytes before the item's end lands on a multiple of <paramref name="align"/>. Returns the
    /// index of the item's first byte in <see cref="Bytes"/>; <see cref="Position"/> is then the item's position.
    /// </summary>
    public int Reserve(int size, int align, int alignAt)
    {
        int pad = (-(Position + alignAt)) & (align - 1);
        int need = pad + size;
        if (_head < need) Grow(need);
        _head -= need;
        _buf.AsSpan(_head + size, pad).Clear();
        return _head;
    }

    private void Grow(int need)
    {
        int used = Position;
        long wanted = Math.Max(2L * _buf.Length, (long)used + need + 1024);
        if (wanted > Array.MaxLength) throw new TesseraException("A translated buffer would exceed 2 GB.");
        var next = new byte[wanted];
        _buf.AsSpan(_head, used).CopyTo(next.AsSpan(next.Length - used));
        _head = next.Length - used;
        _buf = next;
    }

    /// <summary>The bytes in an array whose length is a multiple of 8, ending with them, so positions keep their alignment.</summary>
    public byte[] Finish()
    {
        int used = Position;
        var result = new byte[(used + 7) & ~7];
        _buf.AsSpan(_head, used).CopyTo(result.AsSpan(result.Length - used));
        return result;
    }
}

/// <summary>
/// Rewrites a checked buffer written with another version of the classes in the reader's layout, so that the read types
/// read every buffer with their own constant positions. The source is read through its bindings (null: a subtree whose
/// layout is the reader's). Members the writer lacks are absent (fixed cells get their declared default); members the
/// reader lacks are dropped; a value of a derived type the reader does not know keeps its type tag and loses its object.
/// Items referenced from several places are translated once, so sharing, and the buffer's size, are kept.
/// </summary>
internal ref struct Translator
{
    private readonly ReadOnlySpan<byte> S;
    private readonly ReadSchema _r;
    private readonly Builder _out;
    private readonly Dictionary<(int, int), int> _memo;
    private const int StringKey = -1, SharedKey = -2;

    private Translator(ReadOnlySpan<byte> source, ReadSchema reader)
    {
        S = source;
        _r = reader;
        _out = new Builder();
        _memo = new Dictionary<(int, int), int>();
    }

    /// <summary>The translated buffer, and its root object's index in it.</summary>
    public static byte[] Translate(ReadOnlySpan<byte> source, ReadSchema reader, int root, Binding? binding, out int rootIndex)
    {
        var t = new Translator(source, reader);
        int pos = t.Table(reader.Root, root, binding);
        byte[] result = t._out.Finish();
        rootIndex = result.Length - pos;
        return result;
    }

    private readonly uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(S.Slice(at));

    private readonly int Target(int slot) => slot + (int)U32(slot);

    private static void Store32(Span<byte> b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(at), v);

    private int Table(int r, int p, Binding? b)
    {
        if (_memo.TryGetValue((p, r), out int done)) return done;
        ReadEntry R = _r.Entries[r];
        int n = R.Count;
        var src = new int[n];          // source cell per member (-1: absent)
        var child = new int[n];        // position of the translated target (0: none)
        var extra = new uint[n];       // union tags; bools' two bits
        for (int i = 0; i < n; i++)
        {
            WireKind k = R.Kind[i];
            if (k == WireKind.Bool)
            {
                extra[i] = b != null ? SlowBool(p, b, i) : (U32(p + 4 * (R.FieldBit(i) >> 5)) >> (R.FieldBit(i) & 31)) & 3u;
                src[i] = -1;
                continue;
            }

            int cell = b != null ? SlowCell(p, b, i) : ExactCell(R, p, i);
            src[i] = cell;
            if (cell < 0) continue;
            Binding? cb = b?.Fields[i].Child;
            switch (k)
            {
                case WireKind.String: child[i] = String(Target(cell)); break;
                case WireKind.Object: child[i] = Table(R.Ref[i], Target(cell), cb); break;
                case WireKind.Vector: child[i] = Vector(R.Ref[i], Target(cell), cb); break;
                case WireKind.SharedStruct: child[i] = Bytes(Target(cell), _r.Entries[R.Ref[i]].StructSize, _r.Entries[R.Ref[i]].StructAlign); break;
                case WireKind.Union:
                    extra[i] = U32(cell);
                    child[i] = U32(cell + 4) == 0 ? 0 : Member(R.Ref[i], cb, extra[i], Target(cell + 4));
                    break;
            }
        }

        if (R.IsDictionary)
        {
            // A dictionary always stores both vectors (readers find them without the header bits): one the source lacks,
            // or holds in a layout the reader cannot read, becomes an empty vector.
            for (int i = 0; i < n; i++)
            {
                if (src[i] >= 0 || R.Kind[i] != WireKind.Vector) continue;
                child[i] = EmptyVector(R.Ref[i]);
                src[i] = int.MaxValue;   // present: only its offset is written
            }
        }

        int cells = 0;
        bool align8 = false;
        for (int f = 0; f < R.FixedCount; f++) align8 |= Align8(R, f);
        for (int j = 0; j < R.CellCount; j++)
        {
            if (src[R.FixedCount + j] < 0) continue;
            cells += R.CellSize[j];
            align8 |= R.CellAlign[j] >= 8;
        }

        int head = 4 * R.Words + R.FixedBytes;
        int o = _out.Reserve(head + cells, align8 ? 8 : 4, R.FixedBytes + cells);
        int pos = _out.Position;
        Span<byte> d = _out.Bytes;
        d.Slice(o, head).Clear();

        int c = head;
        for (int j = 0; j < R.CellCount; j++)
        {
            int i = R.FixedCount + j;
            if (src[i] < 0) continue;
            SetBit(d, o, j);
            switch (R.Kind[i])
            {
                case WireKind.String:
                case WireKind.Object:
                case WireKind.Vector:
                case WireKind.SharedStruct:
                    Store32(d, o + c, (uint)(pos - c - child[i]));
                    break;
                case WireKind.Union:
                    Store32(d, o + c, extra[i]);
                    Store32(d, o + c + 4, child[i] != 0 ? (uint)(pos - (c + 4) - child[i]) : 0u);
                    break;
                default:
                    S.Slice(src[i], R.CellSize[j]).CopyTo(d.Slice(o + c));
                    break;
            }

            c += R.CellSize[j];
        }

        for (int i = R.FixedCount + R.CellCount; i < n; i++)
        {
            int bit = R.FieldBit(i);
            if ((extra[i] & 1u) != 0) SetBit(d, o, bit);
            if ((extra[i] & 2u) != 0) SetBit(d, o, bit + 1);
        }

        // Fixed cells: the source's, or the declared default where the writer lacks the member.
        if (R.FixedDefaults != null) R.FixedDefaults.CopyTo(d.Slice(o + 4 * R.Words));
        for (int f = 0; f < R.FixedCount; f++)
        {
            if (src[f] < 0) continue;
            int size = R.Kind[f] == WireKind.Struct ? _r.Entries[R.Ref[f]].StructSize : ReadSchema.KindCellSize(R.Kind[f]);
            S.Slice(src[f], size).CopyTo(d.Slice(o + 4 * R.Words + R.FixedOffset[f]));
        }

        _memo[(p, r)] = pos;
        return pos;
    }

    private static void SetBit(Span<byte> d, int o, int bit)
    {
        int at = o + 4 * (bit >> 5);
        Store32(d, at, BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(at)) | (1u << (bit & 31)));
    }

    private readonly bool Align8(ReadEntry R, int f) => R.Kind[f] switch
    {
        WireKind.Struct => _r.Entries[R.Ref[f]].StructAlign >= 8,
        WireKind.Int64 or WireKind.UInt64 or WireKind.Float64 => true,
        _ => false,
    };

    /// <summary>The source cell of reader member <paramref name="i"/> in an object laid out as the reader's type.</summary>
    private readonly int ExactCell(ReadEntry R, int p, int i)
    {
        if (i < R.FixedCount) return p + 4 * R.Words + R.FixedOffset[i];
        int bit = i - R.FixedCount;
        if (((U32(p + 4 * (bit >> 5)) >> (bit & 31)) & 1u) == 0) return -1;
        return p + 4 * R.Words + R.FixedBytes + R.CellsBefore(S.Slice(p, 4 * R.Words), bit);
    }

    private readonly int SlowCell(int p, Binding b, int i)
    {
        FieldBinding f = b.Fields[i];
        if (f.Bit == FieldBinding.FixedCell) return p + f.FixedAt;
        if (f.Bit == FieldBinding.Missing || ((U32(p + 4 * (f.Bit >> 5)) >> (f.Bit & 31)) & 1u) == 0) return -1;
        int off = 4 * b.Words + b.Fixed;
        for (int k = 0; k < f.TermCount; k++)
        {
            Term t = b.Terms[f.TermStart + k];
            off += t.Size * BitOperations.PopCount(U32(p + 4 * t.Word) & t.Mask);
        }

        return p + off;
    }

    private readonly uint SlowBool(int p, Binding b, int i)
    {
        FieldBinding f = b.Fields[i];
        if (f.Bit < 0) return 0;
        return (U32(p + 4 * (f.Bit >> 5)) >> (f.Bit & 31)) & 3u;
    }

    private int String(int s)
    {
        if (_memo.TryGetValue((s, StringKey), out int done)) return done;
        int size = 4 + (int)U32(s) + 1;
        int at = _out.Reserve(size, 4, size);
        S.Slice(s, size).CopyTo(_out.Bytes.Slice(at));
        int pos = _out.Position;
        _memo[(s, StringKey)] = pos;
        return pos;
    }

    /// <summary>A shared struct's bytes.</summary>
    private int Bytes(int s, int size, int align)
    {
        if (_memo.TryGetValue((s, SharedKey), out int done)) return done;
        int at = _out.Reserve(size, Math.Max(4, align), size);
        S.Slice(s, size).CopyTo(_out.Bytes.Slice(at));
        int pos = _out.Position;
        _memo[(s, SharedKey)] = pos;
        return pos;
    }

    /// <summary>The object of a value of union <paramref name="u"/> with <paramref name="tag"/>, or 0 when the reader does not know (or cannot read) its type.</summary>
    private int Member(int u, Binding? ub, uint tag, int target)
    {
        ReadEntry U = _r.Entries[u];
        for (int k = 0; k < U.Count; k++)
        {
            if (U.Tags[k] != tag || U.Members[k] < 0) continue;
            Binding? mb = null;
            if (ub != null && (!ub.TryMember(tag, out mb) || mb == Binding.Incompatible)) return 0;
            return Table(U.Members[k], target, mb);
        }

        return 0;
    }

    /// <summary>An empty vector of reader entry <paramref name="r"/>, aligned as <see cref="Vector"/> aligns one.</summary>
    private int EmptyVector(int r)
    {
        ReadEntry V = _r.Entries[r];
        int align = V.ElemOptional || !ReadSchema.IsReference(V.ElemKind) ? Math.Max(4, V.ElemAlign) : 4;
        int at = _out.Reserve(4, align, 0);
        Store32(_out.Bytes, at, 0);
        return _out.Position;
    }

    private int Vector(int r, int t, Binding? b)
    {
        if (_memo.TryGetValue((t, r), out int done)) return done;
        ReadEntry V = _r.Entries[r];
        int n = (int)U32(t);
        WireKind k = V.ElemKind;
        Binding? eb = b?.Element;
        int pos;
        if (V.ElemOptional || !ReadSchema.IsReference(k))
        {
            // Numbers, bools, inline structs and optional values: the same bytes in both layouts.
            int presence = V.ElemOptional ? 4 * ((n + 31) >> 5) : 0;
            int values = n * V.ElemSize;
            int at = _out.Reserve(4 + presence + values, Math.Max(4, V.ElemAlign), values);
            S.Slice(t, 4 + presence + values).CopyTo(_out.Bytes.Slice(at));
            pos = _out.Position;
        }
        else if (k == WireKind.Union)
        {
            var tags = new uint[n];
            var kids = new int[n];
            for (int i = 0; i < n; i++)
            {
                int slot = t + 4 + 8 * i;
                if (U32(slot + 4) == 0) continue;
                tags[i] = U32(slot);
                kids[i] = Member(V.ElemRef, eb, tags[i], Target(slot + 4));
            }

            int at = _out.Reserve(4 + 8 * n, 4, 8 * n);
            pos = _out.Position;
            Span<byte> d = _out.Bytes;
            Store32(d, at, (uint)n);
            for (int i = 0; i < n; i++)
            {
                int slot = pos - 8 - 8 * i;
                Store32(d, at + 4 + 8 * i, tags[i]);
                Store32(d, at + 8 + 8 * i, kids[i] != 0 ? (uint)(slot - kids[i]) : 0u);
            }
        }
        else
        {
            var kids = new int[n];
            for (int i = 0; i < n; i++)
            {
                int slot = t + 4 + 4 * i;
                if (U32(slot) == 0) continue;
                int target = Target(slot);
                kids[i] = k switch
                {
                    WireKind.String => String(target),
                    WireKind.Object => Table(V.ElemRef, target, eb),
                    WireKind.Vector => Vector(V.ElemRef, target, eb),
                    _ => Bytes(target, _r.Entries[V.ElemRef].StructSize, _r.Entries[V.ElemRef].StructAlign),   // shared structs
                };
            }

            int at = _out.Reserve(4 + 4 * n, 4, 4 * n);
            pos = _out.Position;
            Span<byte> d = _out.Bytes;
            Store32(d, at, (uint)n);
            for (int i = 0; i < n; i++)
            {
                int slot = pos - 4 - 4 * i;
                Store32(d, at + 4 + 4 * i, kids[i] != 0 ? (uint)(slot - kids[i]) : 0u);
            }
        }

        _memo[(t, r)] = pos;
        return pos;
    }
}
