using System;

namespace Tessera;

/// <summary>Thrown when a value cannot be written, or a buffer cannot be read.</summary>
public class TesseraException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TesseraException(string message) : base(message) { }
}

/// <summary>Thrown when a buffer cannot be read; <see cref="Error"/> says why.</summary>
public sealed class TesseraReadException : TesseraException
{
    /// <summary>Creates the exception.</summary>
    public TesseraReadException(TesseraReadError error) : base(Describe(error)) => Error = error;

    /// <summary>Why the buffer cannot be read.</summary>
    public TesseraReadError Error { get; }

    /// <summary>A sentence describing <paramref name="error"/>.</summary>
    public static string Describe(TesseraReadError error) => error switch
    {
        TesseraReadError.None => "The buffer can be read.",
        TesseraReadError.TooSmall => "The buffer is shorter than a Tessera header (16 bytes).",
        TesseraReadError.BadMagic => "This is not a Tessera buffer: its header lacks the Tessera marker.",
        TesseraReadError.BadVersion => "The buffer was written in a format version this reader does not support.",
        TesseraReadError.SchemaMismatch => "The buffer was written with different classes and carries no description of them, or its top-level " +
                                           "class cannot be read as the one requested.",
        TesseraReadError.BadSchema => "The description of the classes stored in the buffer is damaged.",
        TesseraReadError.OutOfBounds => "Something in the buffer extends past its end.",
        TesseraReadError.BadOffset => "An offset in the buffer is zero where a value is required, points backward or is misaligned, or a " +
                                      "dictionary lacks its keys or values.",
        TesseraReadError.BadString => "A string in the buffer lacks its terminating zero, or is not valid UTF-8.",
        TesseraReadError.BadUnionTag => "A field declared as a base class or interface holds a type that is not one of its known derived types.",
        TesseraReadError.ReservedBits => "Bits that must be zero are set.",
        TesseraReadError.TooDeep => "The buffer nests deeper than MaxDepth.",
        TesseraReadError.TooManyItems => "The buffer holds more objects and lists than MaxItems.",
        _ => "The buffer cannot be read.",
    };
}
