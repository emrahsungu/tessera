using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Bench.Models;
using MessagePack;
using Tessera;

namespace Bench;

/// <summary>Size of one library's buffer for one workload.</summary>
public sealed record SizeRow(string Workload, string Library, int Bytes, int Gzip);

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
        new Workload<Prefab>("prefab", static () => DataGen.Prefab(), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<World>("monsters", static () => DataGen.World(), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<RecordSet>("records", static () => DataGen.Records(), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<Series>("series", static () => DataGen.Series(), static (fb, x) => fb.Write(x), Checksum.Of),
        // The series again, with its always-set values as fixed cells ([TesseraKeepDefault]); Tessera only.
        new TesseraOnly<FixedSeries>("series", "Tessera (fixed)", "tessera-fixed", static () => FixedSeries.From(DataGen.Series())),
        // Scene graphs of 1,024 nodes: dense, sparse, and 16 distinct nodes repeated.
        new Workload<Scene>("dense-unique", static () => DataGen.Scene("dense-unique"), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<Scene>("sparse-unique", static () => DataGen.Scene("sparse-unique"), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<Scene>("dense-shared", static () => DataGen.Scene("dense-shared"), static (fb, x) => fb.Write(x), Checksum.Of),
        new Workload<Lookup>("lookup", static () => DataGen.Lookup(), static (fb, x) => fb.Write(x), Checksum.Of),
        // Real data: canada.json, one GeoJSON polygon of 480 rings and 55,563 points (scripts/deps.ps1 fetches it).
        new Workload<FeatureCollection>("canada", static () => DataGen.Canada(Path.Combine(Deps, "downloads", "canada.json")),
            static (fb, x) => fb.Write(x), Checksum.Of),
    };

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
        Func<int> CreateOp(string library, string variant);
        void Export(string dataDir, List<string> expected, List<SizeRow> sizes);
    }

    /// <summary>A workload that every library writes, from the same objects.</summary>
    private sealed class Workload<T>(string name, Func<T> create, Action<FbWriters, T> fbWrite, Func<T, ulong> checksum) : IWorkload where T : class
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
                default:
                    throw new ArgumentException($"unknown case {name}/{library}/{variant}");
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
