using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace Tessera;

/// <summary>
/// Creates the object of type <typeparamref name="T"/> stored at <paramref name="item"/>, <paramref name="depth"/> levels
/// below the top-level object (for generated code).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate T TesseraDeserializeFunc<out T>(ref TesseraDeserializer deserializer, ref byte item, int depth);

/// <summary>
/// How generated deserializers read (for generated code): a type argument, so that each way is compiled on its own, with
/// the other ways' code left out.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ITesseraReadMode
{
    /// <summary>Whether to check the buffer while reading it.</summary>
    static abstract bool Check { get; }

    /// <summary>Whether each stored object and list is created once and shared by its references.</summary>
    static abstract bool Share { get; }
}

/// <summary>Generated deserializers that check the buffer as they read it (for generated code).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct TesseraChecked : ITesseraReadMode
{
    /// <inheritdoc/>
    public static bool Check => true;

    /// <inheritdoc/>
    public static bool Share => false;
}

/// <summary>Generated deserializers that read a buffer checked before, or trusted (for generated code).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct TesseraUnchecked : ITesseraReadMode
{
    /// <inheritdoc/>
    public static bool Check => false;

    /// <inheritdoc/>
    public static bool Share => false;
}

/// <summary>
/// Generated deserializers that read a buffer checked before, or trusted, creating each stored item once (for generated
/// code).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct TesseraShared : ITesseraReadMode
{
    /// <inheritdoc/>
    public static bool Check => false;

    /// <inheritdoc/>
    public static bool Share => true;
}

/// <summary>
/// The state of one <see cref="TesseraSerializer.Deserialize{T}(ReadOnlySpan{byte}, TesseraReadOptions?)"/> call, for the
/// generated code that creates the objects of a buffer.
/// </summary>
/// <remarks>
/// <para>
/// A buffer written with the reader's own classes is checked while its objects are created: each object, list and
/// string right before it is read, with the same rules and limits as <see cref="TesseraVerifier"/>, in one pass over the
/// buffer. At anything unusual (a check fails, the buffer shares items heavily, a limit is reached), the objects created
/// so far are dropped, and the buffer is checked on its own first, which says why it cannot be read, or how to read it.
/// So both ways accept exactly the same buffers.
/// </para>
/// <para>
/// Objects and lists are created once per reference, as other serializers do: two references to one item stored once
/// (written with <see cref="Sharing.All"/>) give two equal objects. A buffer that shares items so often that this would
/// create far more objects than it holds (the check found that, or, without the check, more than 16 per 4 bytes are being
/// created) is read again with each stored item created once and shared by its references, which keeps the work in
/// proportion to the buffer's size.
/// </para>
/// <para>
/// A string stored once and referenced from several places (<see cref="Sharing.Strings"/>, the default) is decoded once
/// while it is among the recently decoded ones, and its references share it. Strings are immutable, so this changes
/// nothing but the work and the memory. Other references decode it again, up to 16 times the buffer's size in bytes in
/// all; past that, each is decoded once and shared, so that references to one long string cannot multiply the work.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct TesseraDeserializer
{
    private readonly ref byte _base;
    private readonly int _length;
    private readonly bool _shared;
    private readonly int _maxDepth;
    private readonly int _deep;          // levels from which Enter takes its slow path
    private readonly bool _validateUtf8;
    private long _itemsLeft;             // unchecked: before sharing the items; checked: the check's limit
    private long _refsLeft;              // checked: list references the check may follow
    private long _validateLeft;          // checked: bytes of long strings the check may validate
    private long _utf8Left;              // bytes to decode before every string is decoded once
    private Dictionary<long, object>? _items;
    private Dictionary<int, string>? _strings;
    private Recent[]? _recent;           // strings decoded lately, by position; null before RecentAfter strings, and when they rarely repeat
    private int _plain;                  // strings decoded without looking for them among the recent ones
    private int _hits;
    private int _misses;

    /// <summary>A string decoded lately, and its position (0, which no string has: none).</summary>
    private struct Recent
    {
        public int At;
        public string Text;
    }

    // Strings are looked for among the recent ones from the RecentAfter-th on (buffers with a few strings need no table),
    // and after Probation strings missing from them, only if at least a third were there.
    private const int RecentAfter = 16;
    private const int Probation = 128;
    private const int LongString = 256;

    private TesseraDeserializer(ReadOnlySpan<byte> data, bool shared, TesseraReadOptions? check)
    {
        _base = ref MemoryMarshal.GetReference(data);
        _length = data.Length;
        _shared = shared;
        long quarter = data.Length / 4;
        _itemsLeft = shared ? long.MaxValue : 16 * quarter + 16;
        _utf8Left = 16L * data.Length + 65536;
        if (check != null)
        {
            // The limits of TesseraVerifier's walk of the tree.
            _maxDepth = check.MaxDepth;
            _deep = Math.Min(check.MaxDepth, 62) + 1;
            _validateUtf8 = check.ValidateUtf8;
            _itemsLeft = Math.Min(check.MaxItems, 16 * quarter);
            _refsLeft = 16 * quarter;
            _validateLeft = 16L * data.Length + 65536;
        }

    }

    /// <summary>
    /// Checks the buffer of the reader's own classes while creating the object at <paramref name="root"/>; false at
    /// anything unusual (see the remarks).
    /// </summary>
    internal static bool TryChecked<T>(ReadOnlySpan<byte> data, int root, TesseraReadOptions options, TesseraDeserializeFunc<T> read, out T value)
    {
        // Every offset points to a multiple of 4 (from the buffer's start), so in a buffer whose length is a multiple of 4
        // too, the 4 bytes at a position that passed the offset check are in the buffer: objects with one header word,
        // lists and strings start with no range check of their own.
        if ((data.Length & 3) != 0)
        {
            value = default!;
            return false;
        }

        var d = new TesseraDeserializer(data, shared: false, options);
        try
        {
            value = read(ref d, ref Unsafe.Add(ref MemoryMarshal.GetReference(data), root), 0);
            return true;
        }
        catch (Unusual)
        {
            value = default!;
            return false;
        }
    }

    /// <summary>
    /// Creates the object at <paramref name="root"/> of a buffer checked before (or trusted), with <paramref name="read"/>,
    /// or, when the buffer shares items heavily, with <paramref name="readShared"/>.
    /// </summary>
    internal static T Run<T>(ReadOnlySpan<byte> data, int root, bool shared, TesseraDeserializeFunc<T> read, TesseraDeserializeFunc<T> readShared)
    {
        if (!shared)
        {
            var tree = new TesseraDeserializer(data, shared: false, check: null);
            try
            {
                return read(ref tree, ref Unsafe.Add(ref MemoryMarshal.GetReference(data), root), 0);
            }
            catch (TooManyCopies)
            {
                // Created once per reference, the items would be far more than the buffer holds: share them instead.
            }
        }

        var once = new TesseraDeserializer(data, shared: true, check: null);
        return readShared(ref once, ref Unsafe.Add(ref MemoryMarshal.GetReference(data), root), 0);
    }

    /// <summary>Counts an object or list about to be created, without checks.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Count()
    {
        if (--_itemsLeft < 0) TooMany();
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TooMany() => throw new TooManyCopies();

    /// <summary>When items are shared: the item of type <paramref name="type"/> already created from <paramref name="item"/>, or null.</summary>
    public readonly object? Find(ref byte item, int type) => _items != null && _items.TryGetValue(Key(ref item, type), out object? value) ? value : null;

    /// <summary>When items are shared: records the item of type <paramref name="type"/> created from <paramref name="item"/>.</summary>
    public void Remember(ref byte item, int type, object value) => (_items ??= new Dictionary<long, object>())[Key(ref item, type)] = value;

    /// <summary>Returns <paramref name="value"/>, created from <paramref name="item"/>; when items are shared, records it first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Keep<TC, T>(ref byte item, int type, T value) where TC : struct, ITesseraReadMode where T : class
    {
        if (TC.Share) Remember(ref item, type, value);
        return value;
    }

    private readonly long Key(ref byte item, int type) => (long)type << 32 | (uint)Unsafe.ByteOffset(ref _base, ref item);

    // ---------------------------------------------------------------- checks (TesseraVerifier's, at the position read)

    /// <summary>Gives up on the one-pass check: the buffer is checked on its own instead.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fail() => throw new Unusual();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly long At(ref byte p) => Unsafe.ByteOffset(ref _base, ref p);

    /// <summary>Counts an object or list <paramref name="depth"/> levels below the top-level object against the limits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter(int depth)
    {
        if (--_itemsLeft < 0 || depth >= _deep) EnterSlow(depth);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnterSlow(int depth)
    {
        if (_itemsLeft < 0 || depth > _maxDepth) Fail();
        // A MaxDepth set very high must not let a deep buffer exhaust the stack: that would end the process.
        if ((depth & 63) == 63 && !RuntimeHelpers.TryEnsureSufficientExecutionStack()) Fail();
    }

    /// <summary>Checks that the <paramref name="n"/> bytes at <paramref name="p"/> (a position in the buffer) are in the buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Range(ref byte p, long n)
    {
        if (n > _length - At(ref p)) Fail();
    }

    /// <summary>
    /// The target of the 4-byte offset at <paramref name="slot"/>; checked, the offset is non-zero, a multiple of 4, and
    /// points into the buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref byte Target<TC>(ref byte slot) where TC : struct, ITesseraReadMode
    {
        uint off = Unsafe.ReadUnaligned<uint>(ref slot);
        if (TC.Check && (off == 0 || (off & 3u) != 0 || At(ref slot) + off >= _length)) Fail();
        return ref Unsafe.Add(ref slot, (nint)off);
    }

    /// <summary>The struct that the offset at <paramref name="slot"/> points to, <paramref name="size"/> bytes; checked as such.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref byte SharedStruct<TC>(ref byte slot, int size) where TC : struct, ITesseraReadMode
    {
        ref byte target = ref Target<TC>(ref slot);
        if (TC.Check) Range(ref target, size);
        return ref target;
    }

    /// <summary>A list's <paramref name="n"/> references, about to be followed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void References(uint n)
    {
        if (n > _refsLeft) Fail();
        _refsLeft -= n;
    }

    /// <summary>Checks that <paramref name="p"/> is a multiple of 8 from the buffer's start (8-byte numbers in lists).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Aligned8(ref byte p)
    {
        if ((At(ref p) & 7) != 0) Fail();
    }

    /// <summary>Checks that the <paramref name="n"/> bools at <paramref name="p"/> are each 0 or 1.</summary>
    public readonly void Booleans(ref byte p, uint n)
    {
        if (MemoryMarshal.CreateReadOnlySpan(ref p, (int)n).IndexOfAnyExcept((byte)0, (byte)1) >= 0) Fail();
    }

    /// <summary>
    /// Checks the list of <paramref name="n"/> optional values of <paramref name="size"/> bytes at <paramref name="v"/>: its
    /// presence bits and values in the buffer, no presence bit past the last value, bools 0 or 1, 8-byte numbers at a
    /// multiple of 8.
    /// </summary>
    public readonly void OptionalValues(ref byte v, uint n, int size, bool bools, bool align8)
    {
        int words = (int)((n + 31L) / 32);
        Range(ref v, 4 + 4L * words + (long)n * size);
        if (n % 32 != 0 && (Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref v, 4 * words)) >> (int)(n % 32)) != 0) Fail();
        if (bools) Booleans(ref Unsafe.Add(ref v, 4 + 4 * words), n);
        if (align8) Aligned8(ref Unsafe.Add(ref v, 4 + 4 * words));
    }

    // ---------------------------------------------------------------- strings

    /// <summary>
    /// The string at <paramref name="s"/> (its length, then its UTF-8 bytes), decoded; checked, it is in the buffer,
    /// followed by a zero byte, and valid UTF-8 if the options ask.
    /// </summary>
    public string String<TC>(ref byte s) where TC : struct, ITesseraReadMode
    {
        Recent[]? recent = _recent;
        if (recent == null)
        {
            if (++_plain == RecentAfter && _length >= 1024)
            {
                _recent = new Recent[Math.Min(512, (int)BitOperations.RoundUpToPowerOf2((uint)_length / 64))];
            }

            return Read<TC>(ref s);
        }

        int at = (int)At(ref s);
        ref Recent slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(recent), (at >> 2) & (recent.Length - 1));
        if (slot.At == at)
        {
            // Read (and checked) already.
            _hits++;
            return slot.Text;
        }

        string text = Read<TC>(ref s);
        slot.At = at;
        slot.Text = text;
        if (++_misses == Probation && _hits < Probation / 2) _recent = null;
        return text;
    }

    private string Read<TC>(ref byte s) where TC : struct, ITesseraReadMode
    {
        if (TC.Check) Check(ref s);
        int n = (int)Unsafe.ReadUnaligned<uint>(ref s);
        if (n > _utf8Left) return Cached(ref s, n);
        _utf8Left -= n;
        return Decode(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref s, 4), n));
    }

    private void Check(ref byte s)
    {
        uint n = Unsafe.ReadUnaligned<uint>(ref s);   // in the buffer: see TryChecked
        Range(ref s, (long)n + 5);
        if (Unsafe.Add(ref s, 4 + (int)n) != 0) Fail();
        if (!_validateUtf8) return;
        // As TesseraVerifier: long strings count against a budget (references to one long string are many in a buffer
        // that shares items heavily, which is then checked on its own).
        if (n >= LongString)
        {
            if (n > _validateLeft) Fail();
            _validateLeft -= n;
        }

        if (!Utf8.IsValid(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref s, 4), (int)n))) Fail();
    }

    /// <summary>UTF-8 decoded; ASCII, the most common, is Latin-1 too, whose decoder only widens the bytes.</summary>
    private static string Decode(ReadOnlySpan<byte> utf8) => Ascii.IsValid(utf8) ? Encoding.Latin1.GetString(utf8) : Encoding.UTF8.GetString(utf8);

    private string Cached(ref byte s, int n)
    {
        int at = (int)At(ref s);
        _strings ??= new Dictionary<int, string>();
        if (!_strings.TryGetValue(at, out string? text))
        {
            text = Decode(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref s, 4), n));
            _strings[at] = text;
        }

        return text;
    }

    /// <summary>Thrown inside <see cref="Run{T}"/> to read the buffer again with its items shared.</summary>
    private sealed class TooManyCopies : Exception
    {
    }

    /// <summary>Thrown inside <see cref="TryChecked{T}"/>: the buffer is checked on its own instead.</summary>
    private sealed class Unusual : Exception
    {
    }
}
