using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Bench.Models;
using Google.FlatBuffers;
using Google.Protobuf;
using MessagePack;
using Tessera;
using Fb = Bench.Fb;
using Pb = Bench.Pb;

namespace Bench;

/// <summary>Size of one library's buffer for one workload.</summary>
public sealed record SizeRow(string Workload, string Library, int Bytes, int Gzip);

/// <summary>
/// How a workload is read in place: through Tessera's read types (checked, unless the options say otherwise) and
/// FlatBuffers' accessors, every field into <see cref="Traversals"/>' checksum; and Protobuf's parser, into its own
/// message classes. FlatBuffers' C# verifier (25.12.19) cannot check buffers over 32 KB (it converts positions to 16-bit
/// numbers, then misreads the fields as absent), so in .NET FlatBuffers is read without it.
/// </summary>
public sealed record Readers(
    Func<byte[], TesseraReadOptions, ulong> Tessera,
    Func<ByteBuffer, ulong> FlatBuffers,
    MessageParser Protobuf);

/// <summary>
/// The benchmark workloads: their data, every library's buffers (exported for the C++ benchmark) and the write
/// operations that BenchmarkDotNet times. BenchmarkDotNet runs every case in a process of its own, so a case is
/// rebuilt from its key ("workload/library/variant") there.
/// </summary>
public static class Catalog
{
    public static string Root { get; } = FindRoot();

    private static readonly IWorkload[] s_workloads =
    {
        new Workload<Prefab>("prefab", static () => DataGen.Prefab(), static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
            static (b, o) => Traversals.Tessera(PrefabView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.Prefab.GetRootAsPrefab(bb)), Pb.Prefab.Parser)),
        new Workload<World>("monsters", static () => DataGen.World(), static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
            static (b, o) => Traversals.Tessera(WorldView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.World.GetRootAsWorld(bb)), Pb.World.Parser)),
        new Workload<RecordSet>("records", static () => DataGen.Records(), static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
            static (b, o) => Traversals.Tessera(RecordSetView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.RecordSet.GetRootAsRecordSet(bb)), Pb.RecordSet.Parser)),
        new Workload<Series>("series", static () => DataGen.Series(), static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
            static (b, o) => Traversals.Tessera(SeriesView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.Series.GetRootAsSeries(bb)), Pb.Series.Parser)),
        // The series again, with its always-set values as fixed cells ([TesseraKeepDefault]); Tessera only.
        new TesseraOnly<FixedSeries>("series", "Tessera (fixed)", "tessera-fixed", static () => FixedSeries.From(DataGen.Series())),
        // Scene graphs of 1,024 nodes: dense, sparse, and 16 distinct nodes repeated.
        new Workload<Scene>("dense-unique", static () => DataGen.Scene("dense-unique"), static (fb, x) => fb.Write(x), Checksum.Of, SceneReaders),
        new Workload<Scene>("sparse-unique", static () => DataGen.Scene("sparse-unique"), static (fb, x) => fb.Write(x), Checksum.Of, SceneReaders),
        new Workload<Scene>("dense-shared", static () => DataGen.Scene("dense-shared"), static (fb, x) => fb.Write(x), Checksum.Of, SceneReaders),
        new Workload<Lookup>("lookup", static () => DataGen.Lookup(), static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
            static (b, o) => Traversals.Tessera(LookupView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.Lookup.GetRootAsLookup(bb)), Pb.Lookup.Parser)),
        // Real data: canada.json, one GeoJSON polygon of 480 rings and 55,563 points (scripts/deps.ps1 fetches it).
        new Workload<FeatureCollection>("canada", static () => DataGen.Canada(Path.Combine(Deps, "downloads", "canada.json")),
            static (fb, x) => fb.Write(x), Checksum.Of, new Readers(
                static (b, o) => Traversals.Tessera(FeatureCollectionView.Read(b, o)),
                static bb => Traversals.FlatBuffers(Fb.FeatureCollection.GetRootAsFeatureCollection(bb)), Pb.FeatureCollection.Parser)),
    };

    private static Readers SceneReaders => new(
        static (b, o) => Traversals.Tessera(SceneView.Read(b, o)), static bb => Traversals.FlatBuffers(Fb.Scene.GetRootAsScene(bb)), Pb.Scene.Parser);

    /// <summary>scripts/deps.ps1's folder: the repository's .deps, or TESSERA_DEPS (scripts/ab.ps1 sets it for its worktrees).</summary>
    public static string Deps => Environment.GetEnvironmentVariable("TESSERA_DEPS") is { Length: > 0 } deps ? deps : Path.Combine(Root, ".deps");

    /// <summary>Every write case, as "workload/library/variant".</summary>
    public static IEnumerable<string> Keys => s_workloads.SelectMany(w => w.Cases.Select(c => $"{w.Name}/{c.Library}/{c.Variant}"));

    /// <summary>Builds the case's data and returns the operation to time (the written length, so nothing is dead code).</summary>
    public static Func<int> CreateOp(string key)
    {
        string[] parts = key.Split('/', 3);
        foreach (var w in s_workloads)
            if (w.Name == parts[0] && w.Cases.Contains((parts[1], parts[2]))) return w.CreateOp(parts[1], parts[2]);
        throw new ArgumentException("unknown benchmark case: " + key);
    }

    /// <summary>Every read case, as "workload/library/operation".</summary>
    public static IEnumerable<string> ReadKeys => s_workloads.SelectMany(w => w.ReadCases.Select(c => $"{w.Name}/{c.Library}/{c.Operation}"));

    /// <summary>
    /// Writes the case's buffer, checks that its read gives back every field (the checksum of the objects it was
    /// written from), and returns the read to time.
    /// </summary>
    public static Func<int> CreateReadOp(string key)
    {
        string[] parts = key.Split('/', 3);
        foreach (var w in s_workloads)
            if (w.Name == parts[0] && w.ReadCases.Contains((parts[1], parts[2]))) return w.CreateReadOp(parts[1], parts[2]);
        throw new ArgumentException("unknown benchmark case: " + key);
    }

    /// <summary>Writes every library's buffer and the expected checksums for the C++ benchmark; returns the sizes.</summary>
    public static List<SizeRow> Export(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var sizes = new List<SizeRow>();
        var expected = new List<string>();
        foreach (var w in s_workloads) w.Export(dataDir, expected, sizes);
        File.WriteAllText(Path.Combine(dataDir, "expected.txt"), string.Join("\n", expected) + "\n");
        return sizes;
    }

    private static int Gzip(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var z = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(bytes);
        return (int)ms.Length;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private interface IWorkload
    {
        string Name { get; }
        IReadOnlyList<(string Library, string Variant)> Cases { get; }
        IReadOnlyList<(string Library, string Operation)> ReadCases { get; }
        Func<int> CreateOp(string library, string variant);
        Func<int> CreateReadOp(string library, string operation);
        void Export(string dataDir, List<string> expected, List<SizeRow> sizes);
    }

    /// <summary>A workload that every library writes, from the same objects.</summary>
    private sealed class Workload<T>(string name, Func<T> create, Action<FbWriters, T> fbWrite, Func<T, ulong> checksum, Readers readers) : IWorkload
        where T : class
    {
        private static readonly TesseraOptions s_noShare = new() { Sharing = Sharing.None };
        private static readonly TesseraOptions s_noSchema = new() { IncludeSchema = false };
        private static readonly TesseraOptions s_all = new() { Sharing = Sharing.All };

        public string Name => name;

        public IReadOnlyList<(string Library, string Variant)> Cases { get; } = new[]
        {
            ("Tessera", "to byte[]"), ("Tessera", "reused writer, no copy"),
            ("Tessera (no sharing)", "to byte[]"), ("Tessera (full sharing)", "to byte[]"),
            ("FlatBuffers", "to byte[]"), ("FlatBuffers", "reused builder, no copy"), ("FlatBuffers (shared strings)", "to byte[]"),
            ("MessagePack", "to byte[]"), ("MessagePack", "reused buffer, no copy"),
            ("System.Text.Json", "to byte[]"), ("System.Text.Json", "reused buffer, no copy"),
            ("Newtonsoft.Json", "to byte[]"), ("Newtonsoft.Json", "reused buffer, no copy"),
            ("MemoryPack", "to byte[]"), ("MemoryPack", "reused buffer, no copy"),
            ("Protobuf", "to byte[]"), ("Protobuf", "reused buffer, no copy"), ("Protobuf (prebuilt messages)", "to byte[]"),
        };

        public Func<int> CreateOp(string library, string variant)
        {
            T data = create();
            switch (library, variant)
            {
                case ("Tessera", "to byte[]"):
                    return () => TesseraSerializer.Serialize(data).Length;
                case ("Tessera", "reused writer, no copy"):
                {
                    var writer = new TesseraWriter();
                    return () => TesseraSerializer.Write(writer, data).Length;
                }
                case ("Tessera (no sharing)", "to byte[]"):
                    return () => TesseraSerializer.Serialize(data, s_noShare).Length;
                case ("Tessera (full sharing)", "to byte[]"):
                    return () => TesseraSerializer.Serialize(data, s_all).Length;
                case ("FlatBuffers", "to byte[]"):
                {
                    var fb = new FbWriters(sharedStrings: false);
                    return () =>
                    {
                        fbWrite(fb, data);
                        return fb.ToArray().Length;
                    };
                }
                case ("FlatBuffers", "reused builder, no copy"):
                {
                    var fb = new FbWriters(sharedStrings: false);
                    return () =>
                    {
                        fbWrite(fb, data);
                        return fb.Written.Count;
                    };
                }
                case ("FlatBuffers (shared strings)", "to byte[]"):
                {
                    var fb = new FbWriters(sharedStrings: true);
                    return () =>
                    {
                        fbWrite(fb, data);
                        return fb.ToArray().Length;
                    };
                }
                case ("MessagePack", "to byte[]"):
                    return () => MessagePackSerializer.Serialize(data).Length;
                case ("MessagePack", "reused buffer, no copy"):
                {
                    var output = new ArrayBufferWriter<byte>(1 << 20);
                    return () =>
                    {
                        output.ResetWrittenCount();
                        MessagePackSerializer.Serialize(output, data);
                        return output.WrittenCount;
                    };
                }
                case ("System.Text.Json", "to byte[]"):
                {
                    var info = JsonFormats.Stj<T>();
                    return () => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, info).Length;
                }
                case ("System.Text.Json", "reused buffer, no copy"):
                {
                    var info = JsonFormats.Stj<T>();
                    var output = new ArrayBufferWriter<byte>(1 << 20);
                    var json = new System.Text.Json.Utf8JsonWriter(output);
                    return () =>
                    {
                        output.ResetWrittenCount();
                        json.Reset(output);
                        System.Text.Json.JsonSerializer.Serialize(json, data, info);
                        json.Flush();
                        return output.WrittenCount;
                    };
                }
                case ("Newtonsoft.Json", "to byte[]"):
                {
                    var writer = new JsonFormats.NewtonsoftWriter();
                    return () => writer.Write(data).ToArray().Length;
                }
                case ("Newtonsoft.Json", "reused buffer, no copy"):
                {
                    var writer = new JsonFormats.NewtonsoftWriter();
                    return () => (int)writer.Write(data).Length;
                }
                case ("MemoryPack", "to byte[]"):
                    return () => MemoryPack.MemoryPackSerializer.Serialize(data).Length;
                case ("MemoryPack", "reused buffer, no copy"):
                {
                    var output = new ArrayBufferWriter<byte>(1 << 20);
                    return () =>
                    {
                        output.ResetWrittenCount();
                        MemoryPack.MemoryPackSerializer.Serialize(output, data);
                        return output.WrittenCount;
                    };
                }
                // Protobuf writes its own message classes: the objects are converted on every write (as FlatBuffers
                // builds from them), except with prebuilt messages, as when a program keeps its data in them.
                case ("Protobuf", "to byte[]"):
                    return () => ProtobufFormats.Convert(data).ToByteArray().Length;
                case ("Protobuf", "reused buffer, no copy"):
                {
                    var output = new ArrayBufferWriter<byte>(1 << 20);
                    return () =>
                    {
                        output.ResetWrittenCount();
                        ProtobufFormats.Convert(data).WriteTo(output);
                        return output.WrittenCount;
                    };
                }
                case ("Protobuf (prebuilt messages)", "to byte[]"):
                {
                    IMessage message = ProtobufFormats.Convert(data);
                    return () => message.ToByteArray().Length;
                }
                default:
                    throw new ArgumentException($"unknown case {name}/{library}/{variant}");
            }
        }

        public IReadOnlyList<(string Library, string Operation)> ReadCases { get; } = new[]
        {
            ("Tessera", "deserialize"), ("Tessera", "deserialize, unchecked"), ("Tessera", "verify + traversal"), ("Tessera", "traversal"),
            ("FlatBuffers", "traversal"),
            ("MemoryPack", "deserialize"), ("MessagePack", "deserialize"), ("System.Text.Json", "deserialize"), ("Newtonsoft.Json", "deserialize"),
            ("Protobuf", "parse"),
        };

        public Func<int> CreateReadOp(string library, string operation)
        {
            T data = create();
            ulong expected = checksum(data);
            Func<int> Checked(Func<T> read) => Same(() => checksum(read()), () => read() != null ? 1 : 0);
            Func<int> Same(Func<ulong> check, Func<int> op)
            {
                ulong got = check();
                if (got != expected) throw new InvalidOperationException($"{name}/{library}/{operation} read {got:X16}, expected {expected:X16}");
                return op;
            }

            switch (library, operation)
            {
                case ("Tessera", "deserialize"):
                {
                    byte[] b = TesseraSerializer.Serialize(data);
                    return Checked(() => TesseraSerializer.Deserialize<T>(b));
                }
                case ("Tessera", "deserialize, unchecked"):
                {
                    byte[] b = TesseraSerializer.Serialize(data);
                    return Checked(() => TesseraSerializer.Deserialize<T>(b, TesseraReadOptions.Trusted));
                }
                case ("Tessera", "verify + traversal"):
                {
                    byte[] b = TesseraSerializer.Serialize(data);
                    return Same(() => readers.Tessera(b, TesseraReadOptions.Default), () => (int)readers.Tessera(b, TesseraReadOptions.Default));
                }
                case ("Tessera", "traversal"):
                {
                    byte[] b = TesseraSerializer.Serialize(data);
                    return Same(() => readers.Tessera(b, TesseraReadOptions.Trusted), () => (int)readers.Tessera(b, TesseraReadOptions.Trusted));
                }
                case ("FlatBuffers", "traversal"):
                {
                    var fb = new FbWriters(sharedStrings: false);
                    fbWrite(fb, data);
                    var bb = new ByteBuffer(fb.ToArray());
                    return Same(() => readers.FlatBuffers(bb), () => (int)readers.FlatBuffers(bb));
                }
                case ("MemoryPack", "deserialize"):
                {
                    byte[] b = MemoryPack.MemoryPackSerializer.Serialize(data);
                    return Checked(() => MemoryPack.MemoryPackSerializer.Deserialize<T>(b)!);
                }
                case ("MessagePack", "deserialize"):
                {
                    byte[] b = MessagePackSerializer.Serialize(data);
                    return Checked(() => MessagePackSerializer.Deserialize<T>(b));
                }
                case ("System.Text.Json", "deserialize"):
                {
                    var info = JsonFormats.Stj<T>();
                    byte[] b = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, info);
                    return Checked(() => System.Text.Json.JsonSerializer.Deserialize(b, info)!);
                }
                case ("Newtonsoft.Json", "deserialize"):
                {
                    byte[] b = new JsonFormats.NewtonsoftWriter().Write(data).ToArray();
                    var reader = new JsonFormats.NewtonsoftReader();
                    return Checked(() => reader.Read<T>(b));
                }
                // Protobuf parses into its own message classes, which a program then uses as they are.
                case ("Protobuf", "parse"):
                {
                    byte[] b = ProtobufFormats.Convert(data).ToByteArray();
                    IMessage parsed = readers.Protobuf.ParseFrom(b);
                    if (parsed.CalculateSize() != b.Length || !parsed.Equals(ProtobufFormats.Convert(data)))
                        throw new InvalidOperationException($"{name}/Protobuf parsed a different message");
                    return () => readers.Protobuf.ParseFrom(b).CalculateSize();
                }
                default:
                    throw new ArgumentException($"unknown read case {name}/{library}/{operation}");
            }
        }

        public void Export(string dataDir, List<string> expected, List<SizeRow> sizes)
        {
            T data = create();
            var fb = new FbWriters(sharedStrings: false);
            var fbShared = new FbWriters(sharedStrings: true);
            fbWrite(fb, data);
            byte[] fbBytes = fb.ToArray();
            fbWrite(fbShared, data);
            byte[] fbSharedBytes = fbShared.ToArray();
            var buffers = new List<(string Key, string Label, byte[] Bytes)>
            {
                ("tessera", "Tessera", TesseraSerializer.Serialize(data)),
                ("tessera-noshare", "Tessera (no sharing)", TesseraSerializer.Serialize(data, s_noShare)),
                ("tessera-noschema", "Tessera (no schema)", TesseraSerializer.Serialize(data, s_noSchema)),
                ("tessera-all", "Tessera (full sharing)", TesseraSerializer.Serialize(data, s_all)),
                ("fb", "FlatBuffers", fbBytes),
                ("fb-shared", "FlatBuffers (shared strings)", fbSharedBytes),
                ("msgpack", "MessagePack", MessagePackSerializer.Serialize(data)),
                ("json", "JSON", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, JsonFormats.Stj<T>())),
                ("json-newtonsoft", "JSON (Newtonsoft.Json)", new JsonFormats.NewtonsoftWriter().Write(data).ToArray()),
                // MemoryPack has no C++ reader, so its buffer only counts for size.
                ("memorypack", "MemoryPack", MemoryPack.MemoryPackSerializer.Serialize(data)),
                ("protobuf", "Protobuf", ProtobufFormats.Convert(data).ToByteArray()),
            };
            foreach (var (key, label, bytes) in buffers)
            {
                File.WriteAllBytes(Path.Combine(dataDir, $"{name}.{key}.bin"), bytes);
                sizes.Add(new SizeRow(name, label, bytes.Length, Gzip(bytes)));
            }

            expected.Add($"{name} {checksum(data).ToString(CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>Tessera only: a workload's data in another model (the fixed-cell series).</summary>
    private sealed class TesseraOnly<T>(string name, string library, string fileKey, Func<T> create) : IWorkload where T : class
    {
        public string Name => name;

        public IReadOnlyList<(string Library, string Variant)> Cases { get; } = new[] { (library, "to byte[]"), (library, "reused writer, no copy") };

        public IReadOnlyList<(string Library, string Operation)> ReadCases { get; } = Array.Empty<(string, string)>();

        public Func<int> CreateReadOp(string lib, string operation) => throw new ArgumentException("no read cases: " + name);

        public Func<int> CreateOp(string lib, string variant)
        {
            T data = create();
            if (variant == "to byte[]") return () => TesseraSerializer.Serialize(data).Length;
            var writer = new TesseraWriter();
            return () => TesseraSerializer.Write(writer, data).Length;
        }

        public void Export(string dataDir, List<string> expected, List<SizeRow> sizes)
        {
            byte[] bytes = TesseraSerializer.Serialize(create());
            File.WriteAllBytes(Path.Combine(dataDir, $"{name}.{fileKey}.bin"), bytes);
            sizes.Add(new SizeRow(name, library, bytes.Length, Gzip(bytes)));
        }
    }
}
