using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Tessera;

/// <summary>Writes one value of type <typeparamref name="T"/>; returns the position of the written object.</summary>
public delegate int TesseraWriteFunc<in T>(TesseraWriter writer, T value);

/// <summary>Registration slot filled by generated code for every model type.</summary>
public static class TesseraModel<T>
{
    /// <summary>Generated writer for <typeparamref name="T"/>.</summary>
    public static TesseraWriteFunc<T>? Write { get; private set; }

    /// <summary>Schema entry for <typeparamref name="T"/>.</summary>
    public static TesseraType? Type { get; private set; }

    /// <summary>Generated check of buffers holding a <typeparamref name="T"/>, written with these classes.</summary>
    public static TesseraVerifyFunc? Verify { get; private set; }

    /// <summary>Generated code that creates a <typeparamref name="T"/> from a buffer checked before.</summary>
    public static TesseraDeserializeFunc<T>? Read { get; private set; }

    /// <summary>Generated code that checks a buffer while it creates a <typeparamref name="T"/> from it.</summary>
    public static TesseraDeserializeFunc<T>? ReadChecked { get; private set; }

    /// <summary>Generated code that creates a <typeparamref name="T"/> from a buffer checked before, each stored item once.</summary>
    public static TesseraDeserializeFunc<T>? ReadShared { get; private set; }

    /// <summary>Called by generated module initializers.</summary>
    public static void Register(TesseraWriteFunc<T> write, TesseraType type)
    {
        Write = write;
        Type = type;
        TesseraSerializer.CountRegistration();
    }

    /// <summary>Called by generated module initializers.</summary>
    public static void RegisterRead(TesseraType type, TesseraVerifyFunc verify, TesseraDeserializeFunc<T> readChecked, TesseraDeserializeFunc<T> read,
        TesseraDeserializeFunc<T> readShared)
    {
        Type ??= type;
        Verify = verify;
        ReadChecked = readChecked;
        Read = read;
        ReadShared = readShared;
    }
}

/// <summary>Entry point for writing Tessera buffers.</summary>
public static class TesseraSerializer
{
    [ThreadStatic] private static TesseraWriter? t_writer;

    /// <summary>Serializes <paramref name="value"/> to a new byte array.</summary>
    public static byte[] Serialize<T>(T value, TesseraOptions? options = null)
    {
        TesseraWriter writer = Rent(options);
        Write(writer, value);
        return writer.ToArray();
    }

    /// <summary>Serializes <paramref name="value"/> into <paramref name="output"/>.</summary>
    public static void Serialize<T>(T value, IBufferWriter<byte> output, TesseraOptions? options = null)
    {
        TesseraWriter writer = Rent(options);
        ReadOnlySpan<byte> bytes = Write(writer, value);
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> with a caller-owned writer. The returned span points into the writer and is
    /// valid until the writer is used again. This is the allocation-free path.
    /// </summary>
    public static ReadOnlySpan<byte> Write<T>(TesseraWriter writer, T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        TesseraWriteFunc<T> write = TesseraModel<T>.Write ?? throw NoWriter(typeof(T));
        writer.Reset();
        int root = write(writer, value);
        return writer.Finish(root, TesseraModel<T>.Type!);
    }

    /// <summary>
    /// Creates the <typeparamref name="T"/> that <paramref name="buffer"/> holds, checking the buffer as it reads it (unless
    /// <see cref="TesseraReadOptions.Verify"/> is off). A buffer written with other versions of the classes is read by
    /// matching members by name. To read a buffer without creating objects, use the generated read type
    /// (<c>MonsterView.Read(buffer)</c> for a class <c>Monster</c>).
    /// </summary>
    /// <remarks>
    /// Members are set as they were written: through a constructor whose parameters have the members' names when the
    /// class has no constructor without parameters, and otherwise directly, including members with private or init-only
    /// setters and read-only fields. A member that was not stored gets its declared default (null for references).
    /// Lists and dictionaries are created as their declared types.
    /// </remarks>
    /// <exception cref="TesseraReadException">The buffer cannot be read; its <c>Error</c> says why.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> buffer, TesseraReadOptions? options = null)
    {
        if (!TryDeserialize(buffer, out T? value, out TesseraReadError error, options)) throw new TesseraReadException(error);
        return value!;
    }

    /// <summary>
    /// Like <see cref="Deserialize{T}(ReadOnlySpan{byte}, TesseraReadOptions?)"/>, but returns false and the reason
    /// instead of throwing when the buffer cannot be read.
    /// </summary>
    public static bool TryDeserialize<T>(ReadOnlySpan<byte> buffer, [MaybeNullWhen(false)] out T value, out TesseraReadError error,
        TesseraReadOptions? options = null)
    {
        TesseraDeserializeFunc<T> read = TesseraModel<T>.Read ?? throw NoReader(typeof(T));
        options ??= TesseraReadOptions.Default;
        error = TesseraReader.Open(buffer, TesseraModel<T>.Type!, null, options, out ReadOnlySpan<byte> data, out int root, out bool shared, out bool @unchecked);
        if (error != TesseraReadError.None)
        {
            value = default;
            return false;
        }

        if (@unchecked)
        {
            // Written with these classes: checked while the objects are created, in one pass. At anything unusual (a
            // damaged buffer, items shared heavily, a limit reached), the buffer is checked on its own, which says why it
            // cannot be read or how to read it.
            if (TesseraDeserializer.TryChecked(data, root, options, TesseraModel<T>.ReadChecked!, out value)) return true;
            error = TesseraVerifier.Check(data, root, options, TesseraModel<T>.Verify!, out shared);
            if (error != TesseraReadError.None)
            {
                value = default;
                return false;
            }
        }

        value = TesseraDeserializer.Run(data, root, shared, read, TesseraModel<T>.ReadShared!);
        return true;
    }

    /// <summary>Writers registered by generated code so far, from every loaded assembly.</summary>
    private static int s_registered;

    internal static void CountRegistration() => Interlocked.Increment(ref s_registered);

    private const string AddGenerator = "reference the Tessera package; inside the Tessera repository, import src/Tessera.ProjectReference.targets " +
        "instead of referencing src/Tessera. The generator runs only as an analyzer, which a project reference does not pass on, so such a " +
        "project gets the library alone.";

    private static TesseraException NoWriter(Type type) => new(Volatile.Read(ref s_registered) == 0
        ? $"No serializer was generated for {type}, and no Tessera serializers are loaded at all: the source generator (Tessera.Generator) did " +
          "not run. To run it, " + AddGenerator
        : $"No serializer was generated for {type}. The source generator (Tessera.Generator) writes one for a class when it has [Tessera], is " +
          "listed in [assembly: TesseraRoot(typeof(...))], or is saved with its real type somewhere in its project; it cannot see calls through " +
          "generic code (Serialize<T> with a type parameter). If the project that saves it does not run the generator, " + AddGenerator);

    private static TesseraException NoReader(Type type) => new(Volatile.Read(ref s_registered) == 0
        ? $"No deserializer was generated for {type}, and no Tessera serializers are loaded at all: the source generator (Tessera.Generator) " +
          "did not run. To run it, " + AddGenerator
        : $"No deserializer was generated for {type}. The source generator (Tessera.Generator) writes one for a class when it has [Tessera], " +
          "is listed in [assembly: TesseraRoot(typeof(...))], or is saved or read with its real type somewhere in its project; it cannot see " +
          "calls through generic code (Deserialize<T> with a type parameter), and abstract classes and interfaces are read through the class " +
          "that holds them. If the project that reads it does not run the generator, " + AddGenerator);

    private static TesseraWriter Rent(TesseraOptions? options)
    {
        options ??= TesseraOptions.Default;
        TesseraWriter? writer = t_writer;
        if (writer is null) t_writer = writer = new TesseraWriter(options);
        else if (!ReferenceEquals(writer.Options, options)) writer.Options = options;
        return writer;
    }
}
