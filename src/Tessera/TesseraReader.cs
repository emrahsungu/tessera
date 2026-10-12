using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tessera;

/// <summary>
/// Opens Tessera buffers for reading. The classes' generated read types (<c>MonsterView.Read(buffer)</c> for a class
/// <c>Monster</c>) call it; its members are the building blocks of that generated code.
/// </summary>
public static class TesseraReader
{
    // Read in place of an object, list or string that is absent, so reading one never needs a test for null: its header
    // says every member is absent, and a list's count is 0. Large enough for the header words and fixed cells of any
    // object (WireFormat.MaxFixedBytes, and 4,096 members at two bits each).
    private static readonly byte[] s_zeros = new byte[2048];

    /// <summary>Zero bytes read in place of an absent object or list (for generated code).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ref readonly byte Zeros => ref MemoryMarshal.GetArrayDataReference(s_zeros);

    /// <summary>Whether <paramref name="p"/> is <see cref="Zeros"/>, that is, an absent object or list.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAbsent(ref readonly byte p) => Unsafe.AreSame(ref Unsafe.AsRef(in p), ref MemoryMarshal.GetArrayDataReference(s_zeros));

    /// <summary>
    /// Checks <paramref name="buffer"/> (unless <see cref="TesseraReadOptions.Verify"/> is off) and returns a reference to
    /// its top-level object, which is of type <paramref name="root"/>; on failure, <see cref="Zeros"/> and the reason in
    /// <paramref name="error"/>. A buffer written with these classes is checked by <paramref name="verify"/>, the generated
    /// check of <paramref name="root"/>. Called by the generated read types.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ref readonly byte Open(ReadOnlySpan<byte> buffer, TesseraType root, TesseraVerifyFunc verify, TesseraReadOptions? options,
        out TesseraReadError error)
    {
        error = Open(buffer, root, verify, options ?? TesseraReadOptions.Default, out ReadOnlySpan<byte> data, out int rootAt, out _);
        return ref error == TesseraReadError.None ? ref Unsafe.Add(ref MemoryMarshal.GetReference(data), rootAt) : ref Zeros;
    }

    /// <summary>
    /// Checks <paramref name="buffer"/> as <see cref="Open(ReadOnlySpan{byte}, TesseraType, TesseraVerifyFunc, TesseraReadOptions?, out TesseraReadError)"/>
    /// does. <paramref name="data"/> is the buffer to read (a translated copy for other versions of the classes), with
    /// the top-level object at <paramref name="rootAt"/>; <paramref name="shared"/> says whether the check found that the
    /// buffer shares items so often that it checked each once.
    /// </summary>
    internal static TesseraReadError Open(ReadOnlySpan<byte> buffer, TesseraType root, TesseraVerifyFunc verify, TesseraReadOptions options,
        out ReadOnlySpan<byte> data, out int rootAt, out bool shared)
    {
        data = buffer;
        shared = false;
        TesseraReadError error = CheckHeader(buffer, out rootAt);
        if (error != TesseraReadError.None) return error;
        if (BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8)) == root.DeepFingerprint)
        {
            return options.Verify ? TesseraVerifier.Check(buffer, rootAt, options, verify, out shared) : TesseraReadError.None;
        }

        return OpenOther(buffer, root, options, ref data, ref rootAt, out shared);
    }

    /// <summary>A buffer written with other versions of the classes.</summary>
    private static TesseraReadError OpenOther(ReadOnlySpan<byte> buffer, TesseraType root, TesseraReadOptions options, ref ReadOnlySpan<byte> data,
        ref int rootAt, out bool shared)
    {
        shared = false;

        // Written with other versions of the classes: matched member by member, by name, through the schema section the
        // buffer carries, checked against that schema, and translated once into this program's layout.
        if ((buffer[7] & WireFormat.FlagSchema) == 0) return TesseraReadError.SchemaMismatch;
        if (!ReadSchema.TryParseSection(buffer, out ReadSchema writer)) return TesseraReadError.BadSchema;
        ReadSchema reader = root.ReadSchema ??= ReadSchema.FromTypes(root);
        Binding? binding = new Binder(reader, writer).Bind(reader.Root, writer.Root);
        if (binding == Binding.Incompatible) return TesseraReadError.SchemaMismatch;
        if (options.Verify)
        {
            TesseraReadError error = TesseraVerifier.Check(buffer, writer, rootAt, options, out shared);
            if (error != TesseraReadError.None) return error;
        }

        // The same layout throughout, though the fingerprints differ: read in place.
        if (binding == null) return TesseraReadError.None;
        data = Translator.Translate(buffer, reader, rootAt, binding, out rootAt);
        return TesseraReadError.None;
    }

    /// <summary>Checks the 16-byte header; <paramref name="root"/> is the top-level object's position.</summary>
    private static TesseraReadError CheckHeader(ReadOnlySpan<byte> b, out int root)
    {
        root = 0;
        if (b.Length < WireFormat.HeaderSize) return TesseraReadError.TooSmall;
        if (BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(4)) != WireFormat.Magic) return TesseraReadError.BadMagic;
        if (b[6] != WireFormat.Version) return TesseraReadError.BadVersion;
        if ((b[7] & ~WireFormat.FlagSchema) != 0) return TesseraReadError.ReservedBits;   // flags of a newer writer
        uint at = BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (at < WireFormat.HeaderSize || (at & 3u) != 0 || at >= (uint)b.Length) return TesseraReadError.BadOffset;
        root = (int)at;
        return TesseraReadError.None;
    }

    // ---------------------------------------------------------------- helpers for generated code

    /// <summary>The target of the 4-byte offset at <paramref name="slot"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref byte Target(ref byte slot) => ref Unsafe.Add(ref slot, (nint)Unsafe.ReadUnaligned<uint>(ref slot));

    /// <summary>The target of the 4-byte offset at <paramref name="slot"/>, or <see cref="Zeros"/> when the offset is 0.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref byte TargetOrAbsent(ref byte slot)
    {
        uint off = Unsafe.ReadUnaligned<uint>(ref slot);
        return ref off == 0 ? ref Unsafe.AsRef(in Zeros) : ref Unsafe.Add(ref slot, (nint)off);
    }

    /// <summary>The UTF-8 bytes of the string at <paramref name="s"/> (its length, then its bytes).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> Utf8(ref byte s) =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref s, 4), (int)Unsafe.ReadUnaligned<uint>(ref s));

    /// <summary>The string at <paramref name="s"/>, decoded (invalid UTF-8 reads as U+FFFD).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string String(ref byte s) => Encoding.UTF8.GetString(Utf8(ref s));

    /// <summary>The values of the list of numbers, enums or plain structs at <paramref name="v"/> (its count, then its values).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> Span<T>(ref byte v) where T : unmanaged =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, T>(ref Unsafe.Add(ref v, 4)), (int)Unsafe.ReadUnaligned<uint>(ref v));
}

/// <summary>A list of strings in a buffer. Absent and empty lists both have no items; <c>null</c> items read as null.</summary>
public readonly ref struct TesseraStringList
{
    private readonly ref byte _v;   // the count, then an offset per item (0: null)

    /// <summary>The list at <paramref name="v"/> (for generated code).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TesseraStringList(ref byte v) => _v = ref v;

    /// <summary>How many items the list has.</summary>
    public int Count => (int)Unsafe.ReadUnaligned<uint>(ref _v);

    /// <summary>Item <paramref name="index"/>, decoded; null for a null item.</summary>
    public string? this[int index]
    {
        get
        {
            ref byte slot = ref Slot(index);
            return Unsafe.ReadUnaligned<uint>(ref slot) == 0 ? null : TesseraReader.String(ref TesseraReader.Target(ref slot));
        }
    }

    /// <summary>The UTF-8 bytes of item <paramref name="index"/>, without decoding them; empty for a null item.</summary>
    public ReadOnlySpan<byte> GetUtf8(int index)
    {
        ref byte slot = ref Slot(index);
        return Unsafe.ReadUnaligned<uint>(ref slot) == 0 ? default : TesseraReader.Utf8(ref TesseraReader.Target(ref slot));
    }

    /// <summary>Whether item <paramref name="index"/> is null.</summary>
    public bool IsNull(int index) => Unsafe.ReadUnaligned<uint>(ref Slot(index)) == 0;

    private ref byte Slot(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return ref Unsafe.Add(ref _v, 4 + 4 * index);
    }

    /// <summary>Enumerates the items.</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Enumerates the items of a <see cref="TesseraStringList"/>.</summary>
    public ref struct Enumerator
    {
        private readonly TesseraStringList _list;
        private int _index;

        internal Enumerator(TesseraStringList list)
        {
            _list = list;
            _index = -1;
        }

        /// <summary>The current item.</summary>
        public readonly string? Current => _list[_index];

        /// <summary>Moves to the next item.</summary>
        public bool MoveNext() => ++_index < _list.Count;
    }
}

/// <summary>
/// A list of nullable numbers, enums or plain structs in a buffer (C# <c>List&lt;int?&gt;</c>): a bit per item says whether it
/// has a value.
/// </summary>
public readonly ref struct TesseraNullableList<T> where T : unmanaged
{
    private readonly ref byte _v;   // the count, a presence bit per item, then every item's value (zero when absent)

    /// <summary>The list at <paramref name="v"/> (for generated code).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TesseraNullableList(ref byte v) => _v = ref v;

    /// <summary>How many items the list has.</summary>
    public int Count => (int)Unsafe.ReadUnaligned<uint>(ref _v);

    /// <summary>Item <paramref name="index"/>, or null.</summary>
    public T? this[int index]
    {
        get
        {
            int count = Count;
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
            uint bits = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref _v, 4 + 4 * (index >> 5)));
            if (((bits >> (index & 31)) & 1u) == 0) return null;
            int values = 4 + 4 * ((count + 31) >> 5);
            return Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref _v, values + index * Unsafe.SizeOf<T>()));
        }
    }

    /// <summary>Enumerates the items.</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Enumerates the items of a <see cref="TesseraNullableList{T}"/>.</summary>
    public ref struct Enumerator
    {
        private readonly TesseraNullableList<T> _list;
        private int _index;

        internal Enumerator(TesseraNullableList<T> list)
        {
            _list = list;
            _index = -1;
        }

        /// <summary>The current item.</summary>
        public readonly T? Current => _list[_index];

        /// <summary>Moves to the next item.</summary>
        public bool MoveNext() => ++_index < _list.Count;
    }
}

/// <summary>A list of lists of numbers, enums or plain structs in a buffer (C# <c>List&lt;int[]&gt;</c>).</summary>
public readonly ref struct TesseraSpanList<T> where T : unmanaged
{
    private readonly ref byte _v;   // the count, then an offset per item (0: null)

    /// <summary>The list at <paramref name="v"/> (for generated code).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TesseraSpanList(ref byte v) => _v = ref v;

    /// <summary>How many items the list has.</summary>
    public int Count => (int)Unsafe.ReadUnaligned<uint>(ref _v);

    /// <summary>The values of item <paramref name="index"/>; empty for a null item.</summary>
    public ReadOnlySpan<T> this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            ref byte slot = ref Unsafe.Add(ref _v, 4 + 4 * index);
            return Unsafe.ReadUnaligned<uint>(ref slot) == 0 ? default : TesseraReader.Span<T>(ref TesseraReader.Target(ref slot));
        }
    }

    /// <summary>Whether item <paramref name="index"/> is null.</summary>
    public bool IsNull(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref _v, 4 + 4 * index)) == 0;
    }

    /// <summary>Enumerates the items.</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Enumerates the items of a <see cref="TesseraSpanList{T}"/>.</summary>
    public ref struct Enumerator
    {
        private readonly TesseraSpanList<T> _list;
        private int _index;

        internal Enumerator(TesseraSpanList<T> list)
        {
            _list = list;
            _index = -1;
        }

        /// <summary>The current item.</summary>
        public readonly ReadOnlySpan<T> Current => _list[_index];

        /// <summary>Moves to the next item.</summary>
        public bool MoveNext() => ++_index < _list.Count;
    }
}
