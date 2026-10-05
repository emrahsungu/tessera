using System;

namespace Tessera;

/// <summary>Thrown when a value cannot be written.</summary>
public sealed class TesseraException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TesseraException(string message) : base(message) { }
}
