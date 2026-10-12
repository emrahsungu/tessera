using System;
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
        : this(name, fingerprint, deepFingerprint, entry, refSlots, false)
    {
    }

    /// <summary>Creates a type entry. Called by generated code.</summary>
    /// <param name="name">Display name (C# type name).</param>
    /// <param name="fingerprint">Fingerprint of the type's own layout.</param>
    /// <param name="deepFingerprint">Fingerprint of the type and everything reachable from it.</param>
    /// <param name="entry">Encoded schema entry; type references hold indices into <see cref="SetReferences"/>.</param>
    /// <param name="refSlots">Byte offsets of the 16-bit type references inside <paramref name="entry"/>.</param>
    /// <param name="dictionary">Whether the type is a dictionary, whose keys and values are both always stored.</param>
    public TesseraType(string name, ulong fingerprint, ulong deepFingerprint, byte[] entry, ushort[] refSlots, bool dictionary)
    {
        Id = Interlocked.Increment(ref s_nextId) - 1;
        Name = name;
        Fingerprint = fingerprint;
        DeepFingerprint = deepFingerprint;
        Entry = entry;
        RefSlots = refSlots;
        IsDictionary = dictionary;
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

    /// <summary>Whether the type is a dictionary: an object whose two vectors, keys and values, are always stored.</summary>
    internal bool IsDictionary { get; }

    /// <summary>The schema of the types reachable from this one, prepared for reading (built on first use).</summary>
    internal ReadSchema? ReadSchema;

    /// <summary>Sets the types referenced by <see cref="Entry"/> (separate step so cyclic models can be built).</summary>
    public void SetReferences(params TesseraType[] references) => _refs = references;
}
