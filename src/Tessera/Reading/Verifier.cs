using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace Tessera;

/// <summary>
/// Checks a buffer before anything is read ("Verification" in docs/FORMAT.md): afterwards every read stays inside the
/// buffer. The same checks as the C++ reader's, plus one for .NET: bools in lists are 0 or 1, since they are read as
/// <see cref="bool"/>. For generated code.
/// </summary>
/// <remarks>
/// <para>
/// A buffer written with the reader's own version of the classes is checked by generated code, one method per type with
/// its positions and masks as constants (the C++ reader does the same at compile time), calling the public members
/// below. A buffer written with other versions is checked against the schema it carries, which this type walks itself.
/// </para>
/// <para>
/// A buffer written with sharing references an item from several places, so walking it as a tree checks the item once
/// per reference, which grows exponentially with nesting. No item is smaller than 4 bytes and references only point
/// forward, so visiting more than size / 4 items, or following more list references than that, proves the buffer shares
/// items. The tree walk still goes on up to 16 times that (walking a shared item again is cheaper than looking it up),
/// then, with sharing proven, starts over checking each (position, type) once, remembering the height of its subtree so
/// a later visit only checks that it fits under the depth limit.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct TesseraVerifier
{
    private readonly ReadOnlySpan<byte> _b;
    private readonly ReadSchema? _s;    // the schema walked (null: generated code checks)
    private readonly TesseraReadOptions _o;
    private readonly int _maxDepth;
    private readonly int _deep;          // levels from which Enter takes its slow path
    private readonly bool _utf8;
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
    private TesseraReadError _error;

    private struct Slot
    {
        public long Key;        // 0: empty; 1: a long string's UTF-8; else the type's key (see Once)
        public int Pos;
        public int Height;
    }

    private const long StringKey = 1;
    private const int LongString = 256;

    private TesseraVerifier(ReadOnlySpan<byte> buffer, ReadSchema? schema, TesseraReadOptions options)
    {
        _b = buffer;
        _s = schema;
        _o = options;
        _maxDepth = options.MaxDepth;
        _deep = Math.Min(options.MaxDepth, 62) + 1;
        _utf8 = options.ValidateUtf8;
        long quarter = buffer.Length / 4;
        _limit = Math.Min(options.MaxItems, 16 * quarter);
        _refsLeft = 16 * quarter;
        _utf8Left = 16L * buffer.Length + 65536;
    }

    /// <summary>
    /// Checks the buffer's top-level object, at <paramref name="root"/>, with its type's generated check;
    /// <paramref name="shared"/> says whether the buffer shares items so often that it was checked once per item.
    /// </summary>
    internal static TesseraReadError Check(ReadOnlySpan<byte> buffer, int root, TesseraReadOptions options, TesseraVerifyFunc verify, out bool shared)
    {
        var v = new TesseraVerifier(buffer, null, options);
        bool ok = verify(ref v, root, 0);
        if (!ok && v.Shared) ok = v.StartOnce() && verify(ref v, root, 0);
        shared = v._memo != null;
        return ok ? TesseraReadError.None : v._error;
    }

    /// <summary>Checks the buffer's top-level object, at <paramref name="root"/>, against <paramref name="schema"/>.</summary>
    internal static TesseraReadError Check(ReadOnlySpan<byte> buffer, ReadSchema schema, int root, TesseraReadOptions options, out bool shared)
    {
        var v = new TesseraVerifier(buffer, schema, options);
        bool ok = v.Object(schema.Root, root, 0);
        if (!ok && v.Shared) ok = v.StartOnce() && v.Object(schema.Root, root, 0);
        shared = v._memo != null;
        return ok ? TesseraReadError.None : v._error;
    }

    /// <summary>After the tree walk failed: whether it stopped because the buffer shares items rather than at a limit.</summary>
    private readonly bool Shared =>
        _memo == null && _error == TesseraReadError.TooManyItems && (_items > _b.Length / 4 || _refsOver || _utf8Over);

    /// <summary>Starts over, checking each item once; MaxItems then counts the items checked.</summary>
    private bool StartOnce()
    {
        _error = TesseraReadError.None;
        _items = 0;
        _limit = _o.MaxItems;
        _refsLeft = long.MaxValue;   // each list is checked once
        _deepest = 0;
        _own = false;
        Grow(1024);
        return true;
    }

    // ---------------------------------------------------------------- the building blocks of the generated checks

    /// <summary>Whether items are being checked once each (the buffer shares them): see <see cref="Once"/>.</summary>
    public readonly bool IsOnce
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _memo != null;
    }

    /// <summary>
    /// While checking once: whether this visit comes from <see cref="Once"/> (so check the item) rather than from a
    /// reference to it (so call <see cref="Once"/>).
    /// </summary>
    public bool TakeOwn()
    {
        bool own = _own;
        _own = false;
        return own;
    }

    /// <summary>
    /// Checks the item at <paramref name="pos"/> once: calls <paramref name="verify"/> (the item's own check, which then
    /// gets true from <see cref="TakeOwn"/>) unless an item of the same <paramref name="key"/> (its type) was checked there.
    /// </summary>
    public bool Once(int pos, long key, int depth, TesseraVerifyFunc verify)
    {
        if (Checked(pos, key, depth, out bool ok)) return ok;
        int outer = BeginOnce(depth);
        return verify(ref this, pos, depth) && EndOnce(pos, key, depth, outer);
    }

    /// <summary>Records <paramref name="error"/>, unless an earlier failure was recorded; returns false.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Fail(TesseraReadError error)
    {
        if (_error == TesseraReadError.None) _error = error;
        return false;
    }

    /// <summary>Whether the <paramref name="n"/> bytes at <paramref name="pos"/> (not negative) are in the buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Range(int pos, long n) => n <= _b.Length - pos || Fail(TesseraReadError.OutOfBounds);

    /// <summary>The u32 at <paramref name="pos"/>, a position already known to be in the buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint U32(int pos) => Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(_b), pos));

    /// <summary>
    /// Whether the 4-byte offset at <paramref name="slot"/> is non-zero, a multiple of 4, and points into the buffer, at
    /// <paramref name="target"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Follow(int slot, out int target)
    {
        uint off = U32(slot);
        ulong pos = (ulong)(uint)slot + off;
        target = (int)pos;
        return (off != 0 && (off & 3u) == 0 && pos < (ulong)_b.Length) || Unfollowed(off);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Unfollowed(uint off) => Fail(off == 0 || (off & 3u) != 0 ? TesseraReadError.BadOffset : TesseraReadError.OutOfBounds);

    /// <summary>Counts an object or list, <paramref name="depth"/> levels below the top-level object, against the limits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Enter(int depth)
    {
        if (++_items > _limit || depth >= _deep) return EnterSlow(depth);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool EnterSlow(int depth)
    {
        if (depth > _maxDepth) return Fail(TesseraReadError.TooDeep);
        if (_items > _limit) return Fail(TesseraReadError.TooManyItems);
        // A MaxDepth set very high must not let a deep buffer exhaust the stack: that would end the process.
        return (depth & 63) != 63 || RuntimeHelpers.TryEnsureSufficientExecutionStack() || Fail(TesseraReadError.TooDeep);
    }

    /// <summary>A list's <paramref name="n"/> references, about to be followed.</summary>
    public bool References(uint n)
    {
        if (n <= _refsLeft)
        {
            _refsLeft -= n;
            return true;
        }

        _refsOver = true;
        return Fail(TesseraReadError.TooManyItems);
    }

    /// <summary>Whether <paramref name="pos"/> is a multiple of 8 (8-byte numbers in lists are read in place).</summary>
    public bool Aligned8(int pos) => (pos & 7) == 0 || Fail(TesseraReadError.BadOffset);

    /// <summary>The <paramref name="n"/> bytes of bools at <paramref name="pos"/> are each 0 or 1: they are read as <see cref="bool"/>.</summary>
    public bool Booleans(int pos, uint n) =>
        _b.Slice(pos, (int)n).IndexOfAnyExcept((byte)0, (byte)1) < 0 || Fail(TesseraReadError.ReservedBits);

    /// <summary>
    /// The list of <paramref name="n"/> optional values of <paramref name="size"/> bytes at <paramref name="t"/>: its
    /// presence bits and values in the buffer, no presence bit past the last value, bools 0 or 1, and 8-byte numbers at a
    /// multiple of 8.
    /// </summary>
    public bool OptionalValues(int t, uint n, int size, bool bools, bool align8)
    {
        int words = (int)((n + 31L) / 32);
        if (!Range(t, 4 + 4L * words + (long)n * size)) return false;
        if (n % 32 != 0 && (U32(t + 4 * words) >> (int)(n % 32)) != 0) return Fail(TesseraReadError.ReservedBits);
        if (bools && !Booleans(t + 4 + 4 * words, n)) return false;
        return !align8 || Aligned8(t + 4 + 4 * words);
    }

    /// <summary>The string at <paramref name="s"/>: in the buffer, followed by a zero byte, and valid UTF-8 if requested.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool String(int s)
    {
        if (!Range(s, 4)) return false;
        uint n = U32(s);
        if (!Range(s, (long)n + 5)) return false;
        if (Unsafe.Add(ref MemoryMarshal.GetReference(_b), s + 4 + (int)n) != 0) return Fail(TesseraReadError.BadString);
        return !_utf8 || Utf8Text(s, n);
    }

    // ---------------------------------------------------------------- checking once

    /// <summary>Whether the item was checked already; if so, <paramref name="ok"/> says whether it fits under the depth limit here.</summary>
    private bool Checked(int pos, long key, int depth, out bool ok)
    {
        ref Slot s = ref _memo![Find(pos, key)];
        if (s.Key == 0)
        {
            ok = false;
            return false;
        }

        int height = s.Height;
        if ((long)depth + height > _maxDepth)
        {
            ok = Fail(TesseraReadError.TooDeep);
            return true;
        }

        if (depth + height > _deepest) _deepest = depth + height;
        ok = true;
        return true;
    }

    private int BeginOnce(int depth)
    {
        int outer = _deepest;
        _deepest = depth;
        _own = true;
        return outer;
    }

    private bool EndOnce(int pos, long key, int depth, int outer)
    {
        if (!Remember(pos, key, _deepest - depth)) return false;
        if (outer > _deepest) _deepest = outer;
        return true;
    }

    // ---------------------------------------------------------------- walking a schema

    // Keys of the items checked once: 2e + 2 for objects of entry e, 2e + 3 for lists.
    private bool OnceEntry(int pos, long key, int depth, int entry, bool vector)
    {
        if (Checked(pos, key, depth, out bool ok)) return ok;
        int outer = BeginOnce(depth);
        return (vector ? Vector(entry, pos, depth) : Object(entry, pos, depth)) && EndOnce(pos, key, depth, outer);
    }

    private bool Object(int e, int p, int depth)
    {
        if (_memo != null && !TakeOwn()) return OnceEntry(p, 2L * e + 2, depth, e, vector: false);
        ReadEntry E = _s!.Entries[e];
        if (!Enter(depth) || !Range(p, 4L * E.Words)) return false;
        for (int w = 0; w < E.Words; w++)
        {
            uint word = U32(p + 4 * w);
            if ((word & ~E.Used[w]) != 0 || ((word >> 1) & ~word & E.BoolPresent[w]) != 0) return Fail(TesseraReadError.ReservedBits);
        }

        // A dictionary stores both of its vectors (its only two cells): readers locate them without the presence bits.
        if (E.IsDictionary && (E.CellCount != 2 || (U32(p) & 3u) != 3u)) return Fail(TesseraReadError.BadOffset);
        ReadOnlySpan<byte> header = _b.Slice(p, 4 * E.Words);
        if (!Range(p, 4L * E.Words + E.FixedBytes + E.CellsBefore(header, E.CellCount))) return false;

        // The offsets, in cell order: each cell's place is a count of the present cells before it, per cell size.
        int before = 4 * E.Words + E.FixedBytes;
        for (int w = 0; w < E.Words; w++)
        {
            uint h = U32(p + 4 * w);
            for (uint refs = h & E.RefMask[w]; refs != 0; refs &= refs - 1)
            {
                int bit = BitOperations.TrailingZeroCount(refs);
                uint below = h & ((1u << bit) - 1u);
                int at = before;
                for (int s = 0; s < E.Sizes.Length; s++) at += E.Sizes[s] * BitOperations.PopCount(below & E.SizeMasks[s * E.Words + w]);
                int cell = p + at;
                int f = E.FixedCount + 32 * w + bit;
                WireKind kind = E.Kind[f];
                if (kind == WireKind.Union)
                {
                    if (!Follow(cell + 4, out int target) || !UnionMember(E.Ref[f], U32(cell), target, depth + 1)) return false;
                }
                else
                {
                    if (!Follow(cell, out int target) || !Value(kind, E.Ref[f], target, depth + 1)) return false;
                }
            }

            for (int s = 0; s < E.Sizes.Length; s++) before += E.Sizes[s] * BitOperations.PopCount(h & E.SizeMasks[s * E.Words + w]);
        }

        return true;
    }

    private bool UnionMember(int u, uint tag, int target, int depth)
    {
        if (u < 0) return Fail(TesseraReadError.BadSchema);
        ReadEntry U = _s!.Entries[u];
        for (int m = 0; m < U.Count; m++)
        {
            if (U.Tags[m] != tag) continue;
            if (U.Members[m] < 0) return Fail(TesseraReadError.BadUnionTag);
            return Object(U.Members[m], target, depth);
        }

        return Fail(TesseraReadError.BadUnionTag);
    }

    private bool Value(WireKind kind, int r, int target, int depth) => kind switch
    {
        WireKind.String => String(target),
        WireKind.SharedStruct => Range(target, _s!.Entries[r].StructSize),
        WireKind.Object => r >= 0 ? Object(r, target, depth) : Fail(TesseraReadError.BadSchema),
        WireKind.Vector => r >= 0 ? Vector(r, target, depth) : Fail(TesseraReadError.BadSchema),
        _ => Fail(TesseraReadError.BadSchema),
    };

    private bool Vector(int e, int t, int depth)
    {
        if (_memo != null && !TakeOwn()) return OnceEntry(t, 2L * e + 3, depth, e, vector: true);
        ReadEntry V = _s!.Entries[e];
        if (!Enter(depth) || !Range(t, 4)) return false;
        uint n = U32(t);
        int es = V.ElemSize;
        WireKind k = V.ElemKind;
        bool align8 = es >= 8 && k != WireKind.Struct;
        if (V.ElemOptional) return OptionalValues(t, n, es, k == WireKind.Bool, align8);
        if (!Range(t, 4 + (long)n * es)) return false;
        if (!ReadSchema.IsReference(k))
        {
            if (k == WireKind.Bool && !Booleans(t + 4, n)) return false;
            return !align8 || Aligned8(t + 4);
        }

        if (!References(n)) return false;
        for (int i = 0; i < (int)n; i++)
        {
            if (k == WireKind.Union)
            {
                int slot = t + 4 + 8 * i;
                if (U32(slot + 4) == 0) continue;
                if (!Follow(slot + 4, out int target) || !UnionMember(V.ElemRef, U32(slot), target, depth + 1)) return false;
            }
            else
            {
                int slot = t + 4 + 4 * i;
                if (U32(slot) == 0) continue;
                if (!Follow(slot, out int target) || !Value(k, V.ElemRef, target, depth + 1)) return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- UTF-8 and the table of items checked once

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Utf8Text(int s, uint n)
    {
        ReadOnlySpan<byte> text = _b.Slice(s + 4, (int)n);
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
    private readonly int Find(int pos, long key)
    {
        ulong h = ((ulong)pos + (ulong)key * 0xC2B2AE3D27D4EB4FUL) * 0x9E3779B97F4A7C15UL;
        for (int i = (int)(h >> 32) & _mask; ; i = (i + 1) & _mask)
        {
            ref Slot s = ref _memo![i];
            if (s.Key == 0 || (s.Key == key && s.Pos == pos)) return i;
        }
    }

    private bool Remember(int pos, long key, int height)
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

/// <summary>A generated check of the items of one type (for generated code): see <see cref="TesseraVerifier"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate bool TesseraVerifyFunc(ref TesseraVerifier verifier, int position, int depth);
