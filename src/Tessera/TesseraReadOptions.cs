namespace Tessera;

/// <summary>Settings for reading a buffer.</summary>
public sealed record TesseraReadOptions
{
    /// <summary>The defaults: the whole buffer is checked before anything is read.</summary>
    public static TesseraReadOptions Default { get; } = new();

    /// <summary>
    /// Settings that skip the check. Only for buffers this program wrote itself and kept in memory: without the check, a
    /// damaged or hostile buffer makes reads go outside it.
    /// </summary>
    public static TesseraReadOptions Trusted { get; } = new() { Verify = false };

    /// <summary>
    /// Whether the whole buffer is checked once, before anything is read: every offset, size and nesting level. After
    /// that, reading a field is a plain memory load. Default: true.
    /// </summary>
    public bool Verify { get; init; } = true;

    /// <summary>
    /// Whether the check also rejects strings that are not valid UTF-8. Without it, invalid bytes read as U+FFFD
    /// (the replacement character). Default: false.
    /// </summary>
    public bool ValidateUtf8 { get; init; }

    /// <summary>
    /// The deepest nesting accepted: every object and every list is one level, as when writing. Default: 128, the same
    /// as <see cref="TesseraOptions.MaxDepth"/>.
    /// </summary>
    public int MaxDepth { get; init; } = 128;

    /// <summary>
    /// The most objects and lists the check visits. An item stored once and used from many places
    /// (<see cref="Sharing.All"/>) is checked once. Default: 16,777,216.
    /// </summary>
    public int MaxItems { get; init; } = 1 << 24;
}

/// <summary>Why a buffer cannot be read.</summary>
public enum TesseraReadError
{
    /// <summary>The buffer can be read.</summary>
    None = 0,

    /// <summary>Shorter than the 16-byte header.</summary>
    TooSmall,

    /// <summary>Not a Tessera buffer: the header's marker is wrong.</summary>
    BadMagic,

    /// <summary>Written in a format version this reader does not support.</summary>
    BadVersion,

    /// <summary>
    /// Written with different classes, and without the description of them that would let this program match their fields
    /// (see <see cref="TesseraOptions.IncludeSchema"/>); or its top-level class cannot be read as the one requested.
    /// </summary>
    SchemaMismatch,

    /// <summary>The description of the classes stored in the buffer is damaged.</summary>
    BadSchema,

    /// <summary>Something in the buffer extends past its end.</summary>
    OutOfBounds,

    /// <summary>An offset is zero where a value is required, points backward or is misaligned, or a dictionary lacks its keys or values.</summary>
    BadOffset,

    /// <summary>A string lacks its terminating zero, or is not valid UTF-8 (when <see cref="TesseraReadOptions.ValidateUtf8"/> is on).</summary>
    BadString,

    /// <summary>A field declared as a base class or interface holds a type that is not one of its known derived types.</summary>
    BadUnionTag,

    /// <summary>Bits that must be zero are set.</summary>
    ReservedBits,

    /// <summary>Nested deeper than <see cref="TesseraReadOptions.MaxDepth"/>.</summary>
    TooDeep,

    /// <summary>More objects and lists than <see cref="TesseraReadOptions.MaxItems"/>.</summary>
    TooManyItems,
}
