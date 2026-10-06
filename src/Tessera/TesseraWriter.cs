using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tessera;

/// <summary>
/// Builds a Tessera buffer back to front (children before parents), so every reference is a forward offset.
/// Positions handed to and returned from this API are distances from the end of the buffer; they stay valid while the
/// buffer grows. A writer is reusable but not thread-safe.
/// </summary>
/// <remarks>Most members are called by generated code; application code normally uses <see cref="TesseraSerializer"/>.</remarks>
public sealed partial class TesseraWriter
{
    private byte[] _buf;
    private int _head;
    private int _savedPos;
    private int _depth;
    private TesseraOptions _options = TesseraOptions.Default;
    private bool _shareStrings;
    private bool _shareAll;
    private bool _finished;
    private int[] _refStack = new int[64];
    private int _refTop;

    /// <summary>Creates a writer with default options.</summary>
    public TesseraWriter() : this(TesseraOptions.Default) { }

    /// <summary>Creates a writer.</summary>
    public TesseraWriter(TesseraOptions options, int initialCapacity = 4096)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Tessera requires a little-endian platform.");
        _buf = GC.AllocateUninitializedArray<byte>(Math.Max(64, initialCapacity));
        _head = _buf.Length;
        Options = options;
    }

    /// <summary>Writer options. Changing them resets the writer.</summary>
    public TesseraOptions Options
    {
        get => _options;
        set
        {
            _options = value ?? throw new ArgumentNullException(nameof(value));
            _shareStrings = value.Sharing != Sharing.None;
            _shareAll = value.Sharing == Sharing.All;
            Reset();
        }
    }

    /// <summary>True when members equal to their default must still be written.</summary>
    public bool WriteDefaults => _options.WriteDefaults;

    /// <summary>True when objects, vectors and shared structs are deduplicated.</summary>
    public bool ShareAll => _shareAll;

    /// <summary>Number of bytes written so far (also the position of the most recently written item).</summary>
    public int Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buf.Length - _head;
    }

    /// <summary>The finished buffer. Valid until the next write or <see cref="Reset"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _finished ? _buf.AsSpan(_head) : throw new InvalidOperationException("Call Finish first.");

    /// <summary>The finished buffer as a new array.</summary>
    public byte[] ToArray()
    {
        ReadOnlySpan<byte> written = WrittenSpan;
        byte[] copy = GC.AllocateUninitializedArray<byte>(written.Length);  // every byte is overwritten: no zeroing
        written.CopyTo(copy);
        return copy;
    }

    /// <summary>Clears the writer for a new buffer, keeping its memory.</summary>
    public void Reset()
    {
        _head = _buf.Length;
        _depth = 0;
        _refTop = 0;
        _finished = false;
        ResetSharing();
        ResetSchema();
    }

    // ------------------------------------------------------------------ space management

    /// <summary>
    /// Reserves <paramref name="size"/> bytes in front of everything written so far. Padding is inserted so that the
    /// point <paramref name="alignAt"/> bytes before the end of the item lands on a multiple of
    /// <paramref name="align"/> in the final buffer. Returns a reference to the first reserved byte; the item's
    /// position is <see cref="Position"/> afterwards. The caller must write every reserved byte.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Reserve(int size, int align, int alignAt)
    {
        _savedPos = Position;
        int pad = (-(Position + alignAt)) & (align - 1);
        int need = pad + size;
        if (_head < need) Grow(need);
        int start = _head - need;
        ref byte first = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_buf), start);
        if (pad != 0) Unsafe.InitBlockUnaligned(ref Unsafe.Add(ref first, size), 0, (uint)pad);
        _head = start;
        return ref first;
    }

    private void Grow(int need)
    {
        int used = _buf.Length - _head;
        long wanted = Math.Max((long)_buf.Length * 2, (long)used + need + 256);
        if (wanted > Array.MaxLength)
        {
            if ((long)used + need > Array.MaxLength) throw new TesseraException("Buffer exceeds the 2 GB limit.");
            wanted = Array.MaxLength;
        }

        var next = GC.AllocateUninitializedArray<byte>((int)wanted);
        Buffer.BlockCopy(_buf, _head, next, next.Length - used, used);
        _head = next.Length - used;
        _buf = next;
    }

    /// <summary>Discards the item reserved by the last Reserve/Begin call (used when an equal item already exists).</summary>
    private void Rollback() => _head = _buf.Length - _savedPos;

    private Span<byte> ItemSpan(int pos, int size) => _buf.AsSpan(_buf.Length - pos, size);

    // ------------------------------------------------------------------ depth

    // Every object and every vector is one level, exactly as the C++ verifier counts (its max_depth has the same
    // default), so any buffer this writer produces passes verification with default options.

    /// <summary>Enters one nesting level (an object or a vector of references); throws past <see cref="TesseraOptions.MaxDepth"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter()
    {
        if (++_depth > _options.MaxDepth) ThrowTooDeep();
    }

    /// <summary>Checks that a leaf level (a vector of scalars or structs) still fits under <see cref="TesseraOptions.MaxDepth"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnterLeaf()
    {
        if (_depth >= _options.MaxDepth) ThrowTooDeep();
    }

    /// <summary>Leaves one nesting level.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit() => _depth--;

    private void ThrowTooDeep() =>
        throw new TesseraException($"Object graph is deeper than MaxDepth ({_options.MaxDepth} nested objects and vectors); it may contain a cycle.");

    // ------------------------------------------------------------------ references

    /// <summary>Writes the forward offset from the 4-byte slot at <paramref name="slotPos"/> to <paramref name="target"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteOffset(ref byte slot, int slotPos, int target) =>
        Unsafe.WriteUnaligned(ref slot, (uint)(slotPos - target));

    // ------------------------------------------------------------------ strings

    /// <summary>Writes a UTF-8 string ([u32 length][bytes][0]) and returns its position.</summary>
    public int WriteString(string value)
    {
        if (!_shareStrings) return WriteStringBytes(value);
        if (_pendingCount != 0) AddPendingStrings();
        // One lookup: the entry is added here and set once the string is written. (A write that throws abandons the
        // buffer, and the next one starts with Reset.)
        ref int known = ref CollectionsMarshal.GetValueRefOrAddDefault(_strings, value, out bool exists);
        if (exists) return known;
        return known = WriteStringBytes(value);
    }

    /// <summary>
    /// Writes a dictionary's string keys as a vector in code point order (the order C++ binary-searches) and returns
    /// its position; keys out of that order are first sorted, together with <paramref name="values"/>. The order is
    /// checked while the keys are written, so each key is read once (after an inversion, the keys written so far are
    /// taken back). The keys of a dictionary are distinct, so they are looked up only among the strings written before
    /// them; new ones are added to the sharing lookup when a later string is written, so equal strings are still
    /// stored once.
    /// </summary>
    public int WriteMapKeys<TValue>(string[] keys, TValue[] values, int count)
    {
        if (_pendingCount != 0) AddPendingStrings();  // an earlier dictionary's keys: the lookups below must find them
        int start = Position, refTop = _refTop;
        if (WriteKeys(keys, count, checkOrder: true, out int position)) return position;
        _head = _buf.Length - start;
        _refTop = refTop;
        Array.Clear(_pendingStrings, 0, _pendingCount);
        _pendingCount = 0;
        keys.AsSpan(0, count).Sort(values.AsSpan(0, count), TesseraMapKeys.Utf8Order);
        WriteKeys(keys, count, checkOrder: false, out position);
        return position;
    }

    private bool WriteKeys(string[] keys, int count, bool checkOrder, out int position)
    {
        Enter();
        int slot = PushRefs(count);
        bool lookup = _shareStrings && _strings.Count != 0;
        if (_shareStrings && _pendingStrings.Length < count) _pendingStrings = new (string, int)[Math.Max(count, 2 * _pendingStrings.Length)];
        for (int i = count - 1; i >= 0; i--)
        {
            string key = keys[i];
            if (checkOrder && i + 1 < count && TesseraMapKeys.CompareCodePoints(key, keys[i + 1]) >= 0)
            {
                Exit();
                position = 0;
                return false;
            }

            int pos;
            if (key is null) pos = 0;
            else if (lookup && _strings.TryGetValue(key, out int known)) pos = known;
            else
            {
                pos = WriteStringBytes(key);
                if (_shareStrings) _pendingStrings[_pendingCount++] = (key, pos);
            }

            SetRef(slot + i, pos);
        }

        Exit();
        position = WriteRefVector(slot, count);
        return true;
    }

    private void AddPendingStrings()
    {
        for (int i = 0; i < _pendingCount; i++) _strings.TryAdd(_pendingStrings[i].Value, _pendingStrings[i].Pos);
        Array.Clear(_pendingStrings, 0, _pendingCount);
        _pendingCount = 0;
    }

    private int WriteStringBytes(string value)
    {
        // ASCII, the common case, in one pass: room for one byte per char, then a narrowing copy that stops at the
        // first other char (that string is then written again, exactly sized).
        int length = value.Length;
        ref byte a = ref Reserve(4 + length + 1, 4, 4 + length + 1);
        if (Ascii.FromUtf16(value, MemoryMarshal.CreateSpan(ref Unsafe.Add(ref a, 4), length), out _) == OperationStatus.Done)
        {
            Unsafe.WriteUnaligned(ref a, (uint)length);
            Unsafe.Add(ref a, 4 + length) = 0;
            return Position;
        }

        Rollback();
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int size = 4 + byteCount + 1;
        ref byte p = ref Reserve(size, 4, size);
        Unsafe.WriteUnaligned(ref p, (uint)byteCount);
        Encoding.UTF8.GetBytes(value, MemoryMarshal.CreateSpan(ref Unsafe.Add(ref p, 4), byteCount));
        Unsafe.Add(ref p, 4 + byteCount) = 0;
        return Position;
    }

    // ------------------------------------------------------------------ vectors

    /// <summary>Writes a vector of little-endian primitives (no bool) and returns its position.</summary>
    public int WriteScalars<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        int elemSize = Unsafe.SizeOf<T>();
        int bytes = checked(values.Length * elemSize);
        int align = Math.Max(4, elemSize);
        ref byte p = ref Reserve(4 + bytes, align, bytes);
        Unsafe.WriteUnaligned(ref p, (uint)values.Length);
        MemoryMarshal.AsBytes(values).CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref p, 4), bytes));
        return _shareAll ? InternBytes(Position, 4 + bytes, align) : Position;
    }

    /// <summary>Writes a vector of bools (one byte each, 0 or 1) and returns its position.</summary>
    public int WriteBools(ReadOnlySpan<bool> values)
    {
        ref byte p = ref Reserve(4 + values.Length, 4, values.Length);
        Unsafe.WriteUnaligned(ref p, (uint)values.Length);
        for (int i = 0; i < values.Length; i++) Unsafe.Add(ref p, 4 + i) = values[i] ? (byte)1 : (byte)0;
        return _shareAll ? InternBytes(Position, 4 + values.Length, 4) : Position;
    }

    /// <summary>Starts a vector of fixed-size elements written by the caller; finish with <see cref="EndVector"/>.</summary>
    public ref byte BeginVector(int count, int elemSize, int elemAlign)
    {
        int bytes = checked(count * elemSize);
        ref byte p = ref Reserve(4 + bytes, Math.Max(4, elemAlign), bytes);
        Unsafe.WriteUnaligned(ref p, (uint)count);
        return ref Unsafe.Add(ref p, 4);
    }

    /// <summary>
    /// Starts a vector of optional values: a presence bit per element (zeroed here), then every element's value (also
    /// zeroed, so absent ones stay zero). Returns the first presence word; values start <c>4 * ((count + 31) / 32)</c>
    /// bytes later. Finish with <see cref="EndOptionalVector"/>.
    /// </summary>
    public ref byte BeginOptionalVector(int count, int elemSize, int elemAlign)
    {
        int words = (count + 31) >> 5;
        int bytes = checked(count * elemSize);
        ref byte p = ref Reserve(4 + 4 * words + bytes, Math.Max(4, elemAlign), bytes);
        Unsafe.WriteUnaligned(ref p, (uint)count);
        Unsafe.InitBlockUnaligned(ref Unsafe.Add(ref p, 4), 0, (uint)(4 * words + bytes));
        return ref Unsafe.Add(ref p, 4);
    }

    /// <summary>Completes a vector started with <see cref="BeginOptionalVector"/> and returns its position.</summary>
    public int EndOptionalVector(int count, int elemSize, int elemAlign) =>
        _shareAll ? InternBytes(Position, 4 + 4 * ((count + 31) >> 5) + count * elemSize, Math.Max(4, elemAlign)) : Position;

    /// <summary>Completes a vector started with <see cref="BeginVector"/> and returns its position.</summary>
    public int EndVector(int count, int elemSize, int elemAlign) =>
        _shareAll ? InternBytes(Position, 4 + count * elemSize, Math.Max(4, elemAlign)) : Position;

    /// <summary>Reserves <paramref name="count"/> slots for child positions of a reference vector.</summary>
    public int PushRefs(int count)
    {
        int start = _refTop;
        int end = checked(start + count);
        if (end > _refStack.Length) Array.Resize(ref _refStack, Math.Max(end, _refStack.Length * 2));
        _refTop = end;
        return start;
    }

    /// <summary>Stores a child position (0 = null) into a slot reserved by <see cref="PushRefs"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRef(int slot, int position) => _refStack[slot] = position;

    /// <summary>Writes a vector of offsets to previously written items (slots from <see cref="PushRefs"/>) and pops them.</summary>
    public int WriteRefVector(int start, int count)
    {
        int bytes = checked(count * 4);
        ref byte p = ref Reserve(4 + bytes, 4, bytes);
        int pos = Position;
        Unsafe.WriteUnaligned(ref p, (uint)count);
        ReadOnlySpan<int> children = _refStack.AsSpan(start, count);
        for (int i = 0; i < children.Length; i++)
        {
            int child = children[i];
            int slotPos = pos - 4 - 4 * i;
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 4 + 4 * i), child == 0 ? 0u : (uint)(slotPos - child));
        }

        _refTop = start;
        return _shareAll ? InternRefVector(pos, count, 4) : pos;
    }

    /// <summary>
    /// Writes a vector of tagged references (u32 tag, u32 offset per element) used for polymorphic elements.
    /// <paramref name="tags"/> holds one tag per slot from <see cref="PushRefs"/>.
    /// </summary>
    public int WriteUnionVector(int start, int count, ReadOnlySpan<uint> tags)
    {
        int bytes = checked(count * 8);
        ref byte p = ref Reserve(4 + bytes, 4, bytes);
        int pos = Position;
        Unsafe.WriteUnaligned(ref p, (uint)count);
        ReadOnlySpan<int> children = _refStack.AsSpan(start, count);
        for (int i = 0; i < children.Length; i++)
        {
            int child = children[i];
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 4 + 8 * i), child == 0 ? 0u : tags[i]);
            int slotPos = pos - 8 - 8 * i;
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 8 + 8 * i), child == 0 ? 0u : (uint)(slotPos - child));
        }

        _refTop = start;
        return _shareAll ? InternRefVector(pos, count, 8) : pos;
    }

    // ------------------------------------------------------------------ objects and shared structs

    /// <summary>
    /// Starts an object of <paramref name="size"/> bytes (header words plus cells). The cell area (the last
    /// <paramref name="cellBytes"/> bytes) is aligned to <paramref name="cellAlign"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte BeginObject(int size, int cellBytes, int cellAlign) => ref Reserve(size, cellAlign < 4 ? 4 : cellAlign, cellBytes);

    /// <summary>
    /// Completes an object started with <see cref="BeginObject"/>. <paramref name="refOffsets"/> lists the byte offsets
    /// (from the object start, ascending) of every 4-byte offset slot it contains; they are needed for deduplication.
    /// Returns the object's position (an earlier equal object's position when sharing is on).
    /// </summary>
    public int EndObject(TesseraType type, int size, ReadOnlySpan<int> refOffsets) =>
        _shareAll ? InternCanonical(Position, size, type.Id + 0x10000, refOffsets) : Position;

    /// <summary>Starts a shared (out-of-line) struct value; finish with <see cref="EndShared"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte BeginShared(int size, int align) => ref Reserve(size, align < 4 ? 4 : align, size);

    /// <summary>Completes a shared struct value and returns its position.</summary>
    public int EndShared(int size, int align) => _shareAll ? InternBytes(Position, size, Math.Max(4, align)) : Position;

    // ------------------------------------------------------------------ finishing

    /// <summary>
    /// Writes the schema section (optional) and the 16-byte header for the root object at <paramref name="root"/>.
    /// Returns the finished buffer, valid until the writer is reused.
    /// </summary>
    public ReadOnlySpan<byte> Finish(int root, TesseraType rootType)
    {
        if (_finished) throw new InvalidOperationException("Writer already finished; call Reset.");
        if (root <= 0) throw new ArgumentOutOfRangeException(nameof(root));
        bool schema = _options.IncludeSchema;
        if (schema) WriteSchema(rootType);
        ref byte h = ref Reserve(WireFormat.HeaderSize, 8, WireFormat.HeaderSize);
        int total = Position;
        Unsafe.WriteUnaligned(ref h, (uint)(total - root));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref h, 4), WireFormat.Magic);
        Unsafe.Add(ref h, 6) = WireFormat.Version;
        Unsafe.Add(ref h, 7) = schema ? WireFormat.FlagSchema : (byte)0;
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref h, 8), rootType.DeepFingerprint);
        _finished = true;
        return _buf.AsSpan(_head);
    }

    // ------------------------------------------------------------------ helpers for generated code

    /// <summary>Returns the list's backing span without copying.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> AsSpan<T>(List<T> list) => CollectionsMarshal.AsSpan(list);

    /// <summary>Little-endian helper for generated code.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(ref byte destination, T value) where T : unmanaged => Unsafe.WriteUnaligned(ref destination, value);

    /// <summary>Reads back a u32 at <paramref name="pos"/> (test and tooling helper).</summary>
    internal uint ReadUInt32(int pos) => BinaryPrimitives.ReadUInt32LittleEndian(_buf.AsSpan(_buf.Length - pos, 4));
}
