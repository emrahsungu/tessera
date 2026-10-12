using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tessera;

/// <summary>Creates the object of type <typeparamref name="T"/> stored at <paramref name="item"/> (for generated code).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate T TesseraDeserializeFunc<out T>(ref TesseraDeserializer deserializer, ref byte item);

/// <summary>
/// The state of one <see cref="TesseraSerializer.Deserialize{T}(ReadOnlySpan{byte}, TesseraReadOptions?)"/> call, for the
/// generated code that creates the objects of a checked buffer.
/// </summary>
/// <remarks>
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
    private readonly bool _shared;
    private long _itemsLeft;
    private long _utf8Left;
    private Dictionary<long, object>? _items;
    private Dictionary<int, string>? _strings;
    private Recent[]? _recent;   // strings decoded lately, by position; null when strings rarely repeat
    private int _hits;
    private int _misses;

    /// <summary>A string decoded lately, and its position (0, which no string has: none).</summary>
    private struct Recent
    {
        public int At;
        public string Text;
    }

    // After this many strings missing from the recent ones, they are only looked for if at least a third were there.
    private const int Probation = 512;

    private TesseraDeserializer(ReadOnlySpan<byte> data, bool shared)
    {
        _base = ref MemoryMarshal.GetReference(data);
        _shared = shared;
        _itemsLeft = shared ? long.MaxValue : 16L * (data.Length / 4) + 16;
        _utf8Left = 16L * data.Length + 65536;
        if (data.Length >= 1024) _recent = new Recent[Math.Min(512, (int)BitOperations.RoundUpToPowerOf2((uint)data.Length / 64))];
    }

    /// <summary>Creates the object at <paramref name="root"/> of a checked buffer.</summary>
    internal static T Run<T>(ReadOnlySpan<byte> data, int root, bool shared, TesseraDeserializeFunc<T> read)
    {
        if (!shared)
        {
            var tree = new TesseraDeserializer(data, shared: false);
            try
            {
                return read(ref tree, ref Unsafe.Add(ref MemoryMarshal.GetReference(data), root));
            }
            catch (TooManyCopies)
            {
                // Created once per reference, the items would be far more than the buffer holds: share them instead.
            }
        }

        var once = new TesseraDeserializer(data, shared: true);
        return read(ref once, ref Unsafe.Add(ref MemoryMarshal.GetReference(data), root));
    }

    /// <summary>Whether each stored object and list is created once and shared by its references.</summary>
    public readonly bool Shared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shared;
    }

    /// <summary>Counts an object or list about to be created.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Count()
    {
        if (--_itemsLeft < 0) TooMany();
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TooMany() => throw new TooManyCopies();

    /// <summary>While <see cref="Shared"/>: the item of type <paramref name="type"/> already created from <paramref name="item"/>, or null.</summary>
    public readonly object? Find(ref byte item, int type) => _items != null && _items.TryGetValue(Key(ref item, type), out object? value) ? value : null;

    /// <summary>While <see cref="Shared"/>: records the item of type <paramref name="type"/> created from <paramref name="item"/>.</summary>
    public void Remember(ref byte item, int type, object value) => (_items ??= new Dictionary<long, object>())[Key(ref item, type)] = value;

    /// <summary>Returns <paramref name="value"/>, created from <paramref name="item"/>; while <see cref="Shared"/>, records it first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Keep<T>(ref byte item, int type, T value) where T : class
    {
        if (_shared) Remember(ref item, type, value);
        return value;
    }

    private readonly long Key(ref byte item, int type) => (long)type << 32 | (uint)Unsafe.ByteOffset(ref _base, ref item);

    /// <summary>The string at <paramref name="s"/> (its length, then its UTF-8 bytes), decoded.</summary>
    public string String(ref byte s)
    {
        Recent[]? recent = _recent;
        if (recent == null) return Decode(ref s);
        int at = (int)Unsafe.ByteOffset(ref _base, ref s);
        ref Recent slot = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(recent), (at >> 2) & (recent.Length - 1));
        if (slot.At == at)
        {
            _hits++;
            return slot.Text;
        }

        string text = Decode(ref s);
        slot.At = at;
        slot.Text = text;
        if (++_misses == Probation && _hits < Probation / 2) _recent = null;
        return text;
    }

    private string Decode(ref byte s)
    {
        int n = (int)Unsafe.ReadUnaligned<uint>(ref s);
        if (n > _utf8Left) return Cached(ref s, n);
        _utf8Left -= n;
        return Decode(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref s, 4), n));
    }

    /// <summary>UTF-8 decoded; ASCII, the most common, widened directly into the new string.</summary>
    private static unsafe string Decode(ReadOnlySpan<byte> utf8)
    {
        if (!Ascii.IsValid(utf8)) return Encoding.UTF8.GetString(utf8);
        fixed (byte* p = utf8)
        {
            return string.Create(utf8.Length, (nint)p, static (chars, bytes) => Ascii.ToUtf16(new ReadOnlySpan<byte>((void*)bytes, chars.Length), chars, out _));
        }
    }

    private string Cached(ref byte s, int n)
    {
        int at = (int)Unsafe.ByteOffset(ref _base, ref s);
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
}
