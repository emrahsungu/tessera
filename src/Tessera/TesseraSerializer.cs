using System;
using System.Buffers;
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

    /// <summary>Called by generated module initializers.</summary>
    public static void Register(TesseraWriteFunc<T> write, TesseraType type)
    {
        Write = write;
        Type = type;
        TesseraSerializer.CountRegistration();
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

    private static TesseraWriter Rent(TesseraOptions? options)
    {
        options ??= TesseraOptions.Default;
        TesseraWriter? writer = t_writer;
        if (writer is null) t_writer = writer = new TesseraWriter(options);
        else if (!ReferenceEquals(writer.Options, options)) writer.Options = options;
        return writer;
    }
}
