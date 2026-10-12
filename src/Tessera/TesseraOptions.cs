namespace Tessera;

/// <summary>Which repeated values are stored only once, and referenced from every other place they occur.</summary>
public enum Sharing
{
    /// <summary>Nothing: every value is written where it occurs. Fastest saving.</summary>
    None = 0,

    /// <summary>Equal strings are stored once.</summary>
    Strings = 1,

    /// <summary>Equal strings, lists, objects and [TesseraShared] structs, compared by content. Smallest buffers, slowest saving.</summary>
    All = 2,
}

/// <summary>Options for saving. Documentation: docs/options.</summary>
public sealed record TesseraOptions
{
    /// <summary>Default settings: shared strings and an embedded schema.</summary>
    public static TesseraOptions Default { get; } = new();

    /// <summary>Which repeated values are stored only once. Default <see cref="Sharing.Strings"/>: nearly all of the size benefit, at little cost.</summary>
    public Sharing Sharing { get; init; } = Sharing.Strings;

    /// <summary>
    /// Stores a description of the classes in the buffer (each field's name hash and type), so programs built from an
    /// older or newer version of the classes can still read it. Without it, a reader accepts only buffers written with
    /// exactly its own classes. Default true.
    /// </summary>
    public bool IncludeSchema { get; init; } = true;

    /// <summary>Saves non-nullable fields even when they equal their default value. Default false.</summary>
    public bool WriteDefaults { get; init; }

    /// <summary>
    /// How deeply objects and lists may nest; the object passed to Serialize is level 1. Deeper data, or a reference
    /// cycle, throws <see cref="TesseraException"/>. Default 128, the same as the C++ reader's <c>Options::max_depth</c>;
    /// raise both together.
    /// </summary>
    public int MaxDepth { get; init; } = 128;
}
