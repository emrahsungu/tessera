namespace Tessera;

/// <summary>Which values the writer stores once and references from every place they occur.</summary>
public enum Sharing
{
    /// <summary>No deduplication. Fastest writes; the output is a tree.</summary>
    None = 0,

    /// <summary>Equal strings are stored once.</summary>
    Strings = 1,

    /// <summary>Equal strings, vectors, shared structs and objects are stored once, compared by content (the output is a DAG).</summary>
    All = 2,
}

/// <summary>Writer settings.</summary>
public sealed record TesseraOptions
{
    /// <summary>Default settings: shared strings and an embedded schema.</summary>
    public static TesseraOptions Default { get; } = new();

    /// <summary>Deduplication level. Default <see cref="Sharing.Strings"/>: nearly all of the size benefit at almost no cost.</summary>
    public Sharing Sharing { get; init; } = Sharing.Strings;

    /// <summary>
    /// Embeds a compact schema (member hashes and kinds) so readers compiled from a different model version can still
    /// read the buffer. Without it a reader only accepts buffers whose schema fingerprint matches its own.
    /// Default true.
    /// </summary>
    public bool IncludeSchema { get; init; } = true;

    /// <summary>Writes non-nullable members even when they equal their default value. Default false.</summary>
    public bool WriteDefaults { get; init; }

    /// <summary>
    /// Maximum nesting depth, counting every object and every vector (the root object is level 1). Deeper (or cyclic)
    /// graphs throw <see cref="TesseraException"/>. Default 128, the same as the C++ reader's <c>Options::max_depth</c>;
    /// raise both together.
    /// </summary>
    public int MaxDepth { get; init; } = 128;
}
