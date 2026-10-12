using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace Tessera;

/// <summary>
/// Checks a buffer against a schema before anything is read ("Verification" in docs/FORMAT.md): afterwards every read
/// stays inside the buffer. The same checks as the C++ reader's, plus one for .NET: bools in lists are 0 or 1, since
/// they are read as <see cref="bool"/>.
/// </summary>
/// <remarks>
/// A buffer written with sharing references an item from several places, so walking it as a tree checks the item once
/// per reference, which grows exponentially with nesting. No item is smaller than 4 bytes and references only point
/// forward, so visiting more than size / 4 items, or following more list references than that, proves the buffer shares
/// items. The tree walk still goes on up to 16 times that (walking a shared item again is cheaper than looking it up),
/// then, with sharing proven, starts over checking each (position, type) once, remembering the height of its subtree so
/// a later visit only checks that it fits under the depth limit.
/// </remarks>
internal ref struct Verifier
{
    private readonly ReadOnlySpan<byte> _b;
    private readonly ReadSchema _s;
    private readonly TesseraReadOptions _o;
    private long _items;
    private long _limit;        // tree walk: 16 x size / 4 (or MaxItems, if lower); checking once: MaxItems
    private long _refsLeft;     // list references the tree walk may follow
    private long _utf8Left;     // bytes of long strings the tree walk may validate
    private bool _refsOver;
    private bool _utf8Over;
    private Slot[]? _memo;      // the items checked once
    private int _mask;
    private int _used;
    private int _deepest;       // deepest level reached under the item being checked once
    private bool _own;

    /// <summary>Why the check failed (the first failure).</summary>
    public TesseraReadError Error;

    private struct Slot
    {
        public long Key;        // 0: empty; 1: a long string's UTF-8; 2e + 2: object of entry e; 2e + 3: list of entry e
        public long Pos;
        public int Height;
    }

    private const long StringKey = 1;
    private const int LongString = 256;

    public Verifier(ReadOnlySpan<byte> buffer, ReadSchema schema, TesseraReadOptions options)
    {
        _b = buffer;
        _s = schema;
        _o = options;
        long quarter = buffer.Length / 4;
        _limit = Math.Min(options.MaxItems, 16 * quarter);
        _refsLeft = 16 * quarter;
        _utf8Left = 16L * buffer.Length + 65536;
    }

    /// <summary>Checks the buffer's root object at <paramref name="root"/>.</summary>
    public static TesseraReadError Check(ReadOnlySpan<byte> buffer, ReadSchema schema, long root, TesseraReadOptions options)
    {
        var v = new Verifier(buffer, schema, options);
        bool ok = v.Object(schema.Root, root, 0);
        if (!ok && v.Shared) ok = v.StartOnce() && v.Object(schema.Root, root, 0);
        return ok ? TesseraReadError.None : v.Error;
    }

    /// <summary>After the tree walk failed: whether it stopped because the buffer shares items rather than at a limit.</summary>
    private readonly bool Shared =>
        _memo == null && Error == TesseraReadError.TooManyItems && (_items > _b.Length / 4 || _refsOver || _utf8Over);

    /// <summary>Starts over, checking each item once; MaxItems then counts the items checked.</summary>
    private bool StartOnce()
    {
        Error = TesseraReadError.None;
        _items = 0;
        _limit = _o.MaxItems;
        _refsLeft = long.MaxValue;   // each list is checked once
        _deepest = 0;
        _own = false;
        Grow(1024);
        return true;
    }

    private bool Fail(TesseraReadError e)
    {
        if (Error == TesseraReadError.None) Error = e;
        return false;
    }

    private bool Range(long pos, long n) => (pos <= _b.Length && n <= _b.Length - pos) || Fail(TesseraReadError.OutOfBounds);

    /// <summary>A u32 at a position already known to be in bounds.</summary>
    private readonly uint U32(long pos) => Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(_b), (nint)pos));

    /// <summary>The target of the 4-byte offset at <paramref name="slot"/> (non-zero, 4-aligned, in bounds), or -1.</summary>
    private long Follow(long slot)
    {
        uint off = U32(slot);
        if (off == 0 || (off & 3u) != 0)
        {
            Fail(TesseraReadError.BadOffset);
            return -1;
        }

        long pos = slot + off;
        if (pos >= _b.Length)
        {
            Fail(TesseraReadError.OutOfBounds);
            return -1;
        }

        return pos;
    }

    private bool Enter(int depth)
    {
        if (depth > _o.MaxDepth) return Fail(TesseraReadError.TooDeep);
        if (++_items > _limit) return Fail(TesseraReadError.TooManyItems);
        // A MaxDepth set very high must not let a deep buffer exhaust the stack: that would end the process.
        if ((depth & 63) == 63 && !RuntimeHelpers.TryEnsureSufficientExecutionStack()) return Fail(TesseraReadError.TooDeep);
        return true;
    }

    /// <summary>A list's <paramref name="n"/> references, about to be followed.</summary>
    private bool Refs(long n)
    {
        if (n <= _refsLeft)
        {
            _refsLeft -= n;
            return true;
        }

        _refsOver = true;
        return Fail(TesseraReadError.TooManyItems);
    }

    private bool Aligned(long pos, int align) => (pos & (align - 1)) == 0 || Fail(TesseraReadError.BadOffset);

    /// <summary>Whether this visit comes from <see cref="Once"/> (so check the item) rather than from a reference to it.</summary>
    private bool TakeOwn()
    {
        bool own = _own;
        _own = false;
        return own;
    }

    /// <summary>The item at <paramref name="pos"/> as <paramref name="key"/>, checked unless it already was.</summary>
    private bool Once(long pos, long key, int depth, int entry, bool vector)
    {
        int slot = Find(pos, key);
        if (_memo![slot].Key != 0)
        {
            int height = _memo[slot].Height;
            if ((long)depth + height > _o.MaxDepth) return Fail(TesseraReadError.TooDeep);
            if (depth + height > _deepest) _deepest = depth + height;
            return true;
        }

        int outer = _deepest;
        _deepest = depth;
        _own = true;
        if (!(vector ? Vector(entry, pos, depth) : Object(entry, pos, depth))) return false;
        if (!Remember(pos, key, _deepest - depth)) return false;
        if (outer > _deepest) _deepest = outer;
        return true;
    }

    public bool Object(int e, long p, int depth)
    {
        if (_memo != null && !TakeOwn()) return Once(p, 2L * e + 2, depth, e, vector: false);
        ReadEntry E = _s.Entries[e];
        if (!Enter(depth) || !Range(p, 4L * E.Words)) return false;
        for (int w = 0; w < E.Words; w++)
        {
            uint word = U32(p + 4 * w);
            if ((word & ~E.Used[w]) != 0 || ((word >> 1) & ~word & E.BoolPresent[w]) != 0) return Fail(TesseraReadError.ReservedBits);
        }

        // A dictionary stores both of its vectors (its only two cells): readers locate them without the presence bits.
        if (E.IsDictionary && (E.CellCount != 2 || (U32(p) & 3u) != 3u)) return Fail(TesseraReadError.BadOffset);
        ReadOnlySpan<byte> header = _b.Slice((int)p, 4 * E.Words);
        if (!Range(p, 4L * E.Words + E.FixedBytes + E.CellsBefore(header, E.CellCount))) return false;

        // The offsets, in cell order: each cell's place is a count of the present cells before it, per cell size.
        long before = 4L * E.Words + E.FixedBytes;
        for (int w = 0; w < E.Words; w++)
        {
            uint h = U32(p + 4 * w);
            for (uint refs = h & E.RefMask[w]; refs != 0; refs &= refs - 1)
            {
                int bit = BitOperations.TrailingZeroCount(refs);
                uint below = h & ((1u << bit) - 1u);
                long at = before;
                for (int s = 0; s < E.Sizes.Length; s++) at += (long)E.Sizes[s] * BitOperations.PopCount(below & E.SizeMasks[s * E.Words + w]);
                long cell = p + at;
                int f = E.FixedCount + 32 * w + bit;
                WireKind kind = E.Kind[f];
                if (kind == WireKind.Union)
                {
                    long target = Follow(cell + 4);
                    if (target < 0 || !UnionMember(E.Ref[f], U32(cell), target, depth + 1)) return false;
                }
                else
                {
                    long target = Follow(cell);
                    if (target < 0 || !Value(kind, E.Ref[f], target, depth + 1)) return false;
                }
            }

            for (int s = 0; s < E.Sizes.Length; s++) before += (long)E.Sizes[s] * BitOperations.PopCount(h & E.SizeMasks[s * E.Words + w]);
        }

        return true;
    }

    private bool UnionMember(int u, uint tag, long target, int depth)
    {
        if (u < 0) return Fail(TesseraReadError.BadSchema);
        ReadEntry U = _s.Entries[u];
        for (int m = 0; m < U.Count; m++)
        {
            if (U.Tags[m] != tag) continue;
            if (U.Members[m] < 0) return Fail(TesseraReadError.BadUnionTag);
            return Object(U.Members[m], target, depth);
        }

        return Fail(TesseraReadError.BadUnionTag);
    }

    private bool Value(WireKind kind, int r, long target, int depth) => kind switch
    {
        WireKind.String => String(target),
        WireKind.SharedStruct => Range(target, _s.Entries[r].StructSize),
        WireKind.Object => r >= 0 ? Object(r, target, depth) : Fail(TesseraReadError.BadSchema),
        WireKind.Vector => r >= 0 ? Vector(r, target, depth) : Fail(TesseraReadError.BadSchema),
        _ => Fail(TesseraReadError.BadSchema),
    };

    private bool Vector(int e, long t, int depth)
    {
        if (_memo != null && !TakeOwn()) return Once(t, 2L * e + 3, depth, e, vector: true);
        ReadEntry V = _s.Entries[e];
        if (!Enter(depth) || !Range(t, 4)) return false;
        uint n = U32(t);
        long es = V.ElemSize;
        WireKind k = V.ElemKind;
        if (V.ElemOptional)
        {
            long words = (n + 31L) / 32;
            if (!Range(t, 4 + 4 * words + n * es)) return false;
            if (n % 32 != 0 && (U32(t + 4 + 4 * (words - 1)) >> (int)(n % 32)) != 0) return Fail(TesseraReadError.ReservedBits);
            if (k == WireKind.Bool && !Booleans(t + 4 + 4 * words, n)) return false;
            return es < 8 || k == WireKind.Struct || Aligned(t + 4 + 4 * words, 8);
        }

        if (!Range(t, 4 + n * es)) return false;
        if (!ReadSchema.IsReference(k))
        {
            if (k == WireKind.Bool && !Booleans(t + 4, n)) return false;
            return es < 8 || k == WireKind.Struct || Aligned(t + 4, 8);
        }

        if (!Refs(n)) return false;
        for (uint i = 0; i < n; i++)
        {
            if (k == WireKind.Union)
            {
                long slot = t + 4 + 8L * i;
                if (U32(slot + 4) == 0) continue;
                long target = Follow(slot + 4);
                if (target < 0 || !UnionMember(V.ElemRef, U32(slot), target, depth + 1)) return false;
            }
            else
            {
                long slot = t + 4 + 4L * i;
                if (U32(slot) == 0) continue;
                long target = Follow(slot);
                if (target < 0 || !Value(k, V.ElemRef, target, depth + 1)) return false;
            }
        }

        return true;
    }

    /// <summary>The bytes of a list of bools are each 0 or 1: they are read as <see cref="bool"/> values.</summary>
    private bool Booleans(long pos, uint n) =>
        _b.Slice((int)pos, (int)n).IndexOfAnyExcept((byte)0, (byte)1) < 0 || Fail(TesseraReadError.ReservedBits);

    private bool String(long s)
    {
        if (!Range(s, 4)) return false;
        uint n = U32(s);
        if (!Range(s, (long)n + 5)) return false;
        if (_b[(int)(s + 4 + n)] != 0) return Fail(TesseraReadError.BadString);
        return !_o.ValidateUtf8 || Utf8Text(s, n);
    }

    private bool Utf8Text(long s, uint n)
    {
        ReadOnlySpan<byte> text = _b.Slice((int)s + 4, (int)n);
        // Validating a short string once per reference costs at most LongString bytes per reference, and references are
        // counted; long strings count against a budget, or are validated once.
        if (n < LongString) return Utf8.IsValid(text) || Fail(TesseraReadError.BadString);
        if (_memo == null)
        {
            if (n > _utf8Left)
            {
                _utf8Over = true;
                return Fail(TesseraReadError.TooManyItems);
            }

            _utf8Left -= n;
            return Utf8.IsValid(text) || Fail(TesseraReadError.BadString);
        }

        if (_memo[Find(s, StringKey)].Key != 0) return true;
        if (!Utf8.IsValid(text)) return Fail(TesseraReadError.BadString);
        return Remember(s, StringKey, 0);
    }

    // Open addressing with linear probing; at most half full.
    private readonly int Find(long pos, long key)
    {
        ulong h = ((ulong)pos + (ulong)key * 0xC2B2AE3D27D4EB4FUL) * 0x9E3779B97F4A7C15UL;
        for (int i = (int)(h >> 32) & _mask; ; i = (i + 1) & _mask)
        {
            ref Slot s = ref _memo![i];
            if (s.Key == 0 || (s.Key == key && s.Pos == pos)) return i;
        }
    }

    private bool Remember(long pos, long key, int height)
    {
        if (2 * (_used + 1) > _mask + 1)
        {
            if (_mask >= 1 << 29) return Fail(TesseraReadError.TooManyItems);
            Grow(2 * (_mask + 1));
        }

        _memo![Find(pos, key)] = new Slot { Key = key, Pos = pos, Height = height };
        _used++;
        return true;
    }

    private void Grow(int capacity)
    {
        Slot[]? old = _memo;
        int oldCapacity = old == null ? 0 : _mask + 1;
        _memo = new Slot[capacity];
        _mask = capacity - 1;
        for (int i = 0; i < oldCapacity; i++)
        {
            if (old![i].Key != 0) _memo[Find(old[i].Pos, old[i].Key)] = old[i];
        }
    }
}
