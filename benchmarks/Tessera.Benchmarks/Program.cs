using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Bench;
using Bench.Models;
using Tessera;
using MessagePack;

// Usage: Tessera.Benchmarks [--export-only] [--quick] [--rounds N] [--round-ms N] [--warmup-ms N]  |  Tessera.Benchmarks --report <results dir> <out.md>
// 1. builds the four workloads, 2. writes every library's buffer and the expected checksums for the C++ benchmark,
// 3. times serialization (unless --export-only) and writes benchmarks/results/dotnet.json.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 3 && args[0] == "--report")
{
    // --report <results dir> <output .md>: tables from the JSON written by this program and benchmarks/native, and the
    // README's bar charts in images/benchmarks next to the report.
    File.WriteAllText(args[2], Report.Build(args[1]));
    string charts = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "images", "benchmarks");
    Charts.Write(args[1], charts);
    Console.WriteLine("Wrote " + args[2] + " and the charts in " + charts);
    return 0;
}

bool exportOnly = args.Contains("--export-only");
bool quick = args.Contains("--quick");
int rounds = ArgInt("--rounds", quick ? 3 : 11);
int roundMs = ArgInt("--round-ms", quick ? 30 : 100);
int warmupMs = ArgInt("--warmup-ms", quick ? 200 : 1000);

string root = FindRoot();
string generated = Path.Combine(root, "benchmarks", "generated");
string dataDir = Path.Combine(generated, "data");
Directory.CreateDirectory(dataDir);

var cases = new List<Case>();
var sizes = new List<Dictionary<string, object>>();
var expected = new Dictionary<string, string>();

Add("prefab", DataGen.Prefab(), static (fb, x) => fb.Write(x), Checksum.Of);
Add("monsters", DataGen.World(), static (fb, x) => fb.Write(x), Checksum.Of);
Add("records", DataGen.Records(), static (fb, x) => fb.Write(x), Checksum.Of);
var series = DataGen.Series();
Add("series", series, static (fb, x) => fb.Write(x), Checksum.Of);
AddFixed("series", FixedSeries.From(series));
// Scene graphs of 1,024 nodes: dense, sparse, and 16 distinct nodes repeated.
foreach (string scene in new[] { "dense-unique", "sparse-unique", "dense-shared" }) Add(scene, DataGen.Scene(scene), static (fb, x) => fb.Write(x), Checksum.Of);
Add("lookup", DataGen.Lookup(), static (fb, x) => fb.Write(x), Checksum.Of);

File.WriteAllText(Path.Combine(dataDir, "expected.txt"), string.Join("\n", expected.Select(kv => $"{kv.Key} {kv.Value}")) + "\n");
Console.WriteLine($"Exported buffers to {dataDir}");
PrintSizes();
if (exportOnly) return 0;

// Run with one core and high priority to reduce noise.
try
{
    using var p = Process.GetCurrentProcess();
    p.PriorityClass = ProcessPriorityClass.High;
    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) p.ProcessorAffinity = (IntPtr)(1 << 2);
}
catch (Exception e)
{
    Console.WriteLine("note: could not set priority/affinity: " + e.Message);
}

Harness.Run(cases, rounds, roundMs, warmupMs, s => Console.Error.WriteLine(s));
WriteResults();
return 0;

// --------------------------------------------------------------------------------------------------------------------

void Add<T>(string name, T data, Action<FbWriters, T> fbWrite, Func<T, ulong> checksum) where T : class
{
    var noShare = new TesseraOptions { Sharing = Sharing.None };
    var noSchema = new TesseraOptions { IncludeSchema = false };
    var all = new TesseraOptions { Sharing = Sharing.All };
    var writer = new TesseraWriter();
    var fb = new FbWriters(sharedStrings: false);
    var fbShared = new FbWriters(sharedStrings: true);
    var output = new ArrayBufferWriter<byte>(1 << 20);

    var buffers = new List<(string Key, string Label, byte[] Bytes)>
    {
        ("tessera", "Tessera", TesseraSerializer.Serialize(data)),
        ("tessera-noshare", "Tessera (no sharing)", TesseraSerializer.Serialize(data, noShare)),
        ("tessera-noschema", "Tessera (no schema)", TesseraSerializer.Serialize(data, noSchema)),
        ("tessera-all", "Tessera (full sharing)", TesseraSerializer.Serialize(data, all)),
    };
    fbWrite(fb, data);
    buffers.Add(("fb", "FlatBuffers", fb.ToArray()));
    fbWrite(fbShared, data);
    buffers.Add(("fb-shared", "FlatBuffers (shared strings)", fbShared.ToArray()));
    buffers.Add(("msgpack", "MessagePack", MessagePackSerializer.Serialize(data)));
    foreach (var (key, _, bytes) in buffers) File.WriteAllBytes(Path.Combine(dataDir, $"{name}.{key}.bin"), bytes);
    expected[name] = checksum(data).ToString(CultureInfo.InvariantCulture);
    foreach (var (key, label, bytes) in buffers)
    {
        sizes.Add(new() { ["workload"] = name, ["library"] = label, ["bytes"] = bytes.Length, ["gzip"] = Gzip(bytes) });
    }

    cases.Add(new Case { Workload = name, Library = "Tessera", Variant = "to byte[]", Op = () => TesseraSerializer.Serialize(data).Length });
    cases.Add(new Case { Workload = name, Library = "Tessera", Variant = "reused writer, no copy", Op = () => TesseraSerializer.Write(writer, data).Length });
    cases.Add(new Case { Workload = name, Library = "Tessera (no sharing)", Variant = "to byte[]", Op = () => TesseraSerializer.Serialize(data, noShare).Length });
    cases.Add(new Case { Workload = name, Library = "Tessera (full sharing)", Variant = "to byte[]", Op = () => TesseraSerializer.Serialize(data, all).Length });
    cases.Add(new Case { Workload = name, Library = "FlatBuffers", Variant = "to byte[]", Op = () => { fbWrite(fb, data); return fb.ToArray().Length; } });
    cases.Add(new Case { Workload = name, Library = "FlatBuffers", Variant = "reused builder, no copy", Op = () => { fbWrite(fb, data); return fb.Written.Count; } });
    cases.Add(new Case { Workload = name, Library = "FlatBuffers (shared strings)", Variant = "to byte[]", Op = () => { fbWrite(fbShared, data); return fbShared.ToArray().Length; } });
    cases.Add(new Case { Workload = name, Library = "MessagePack", Variant = "to byte[]", Op = () => MessagePackSerializer.Serialize(data).Length });
    cases.Add(new Case
    {
        Workload = name, Library = "MessagePack", Variant = "reused buffer, no copy",
        Op = () =>
        {
            output.ResetWrittenCount();
            MessagePackSerializer.Serialize(output, data);
            return output.WrittenCount;
        },
    });
}

// Tessera only: the workload's data in a model whose always-set values are fixed cells ([TesseraKeepDefault]).
void AddFixed<T>(string name, T data) where T : class
{
    var writer = new TesseraWriter();
    byte[] bytes = TesseraSerializer.Serialize(data);
    File.WriteAllBytes(Path.Combine(dataDir, $"{name}.tessera-fixed.bin"), bytes);
    sizes.Add(new() { ["workload"] = name, ["library"] = "Tessera (fixed)", ["bytes"] = bytes.Length, ["gzip"] = Gzip(bytes) });
    cases.Add(new Case { Workload = name, Library = "Tessera (fixed)", Variant = "to byte[]", Op = () => TesseraSerializer.Serialize(data).Length });
    cases.Add(new Case { Workload = name, Library = "Tessera (fixed)", Variant = "reused writer, no copy", Op = () => TesseraSerializer.Write(writer, data).Length });
}

static int Gzip(byte[] bytes)
{
    using var ms = new MemoryStream();
    using (var z = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(bytes);
    return (int)ms.Length;
}

void PrintSizes()
{
    Console.WriteLine();
    Console.WriteLine("| Workload | Library | Bytes | gzip |");
    Console.WriteLine("|---|---|---:|---:|");
    foreach (var s in sizes) Console.WriteLine($"| {s["workload"]} | {s["library"]} | {s["bytes"]:N0} | {s["gzip"]:N0} |");
}

void WriteResults()
{
    var outDir = Path.Combine(root, "benchmarks", "results");
    Directory.CreateDirectory(outDir);
    var rows = cases.Select(c =>
    {
        var st = Harness.Summarize(c.NsPerOp);
        return new Dictionary<string, object>
        {
            ["workload"] = c.Workload, ["library"] = c.Library, ["variant"] = c.Variant,
            ["median_us"] = st.Median / 1000, ["min_us"] = st.Min / 1000, ["max_us"] = st.Max / 1000, ["mad_percent"] = st.MadPercent,
            ["alloc_bytes"] = c.AllocatedBytesPerOp, ["output_bytes"] = c.OutputBytes, ["samples_us"] = c.NsPerOp.Select(x => x / 1000).ToArray(),
        };
    }).ToList();
    var doc = new Dictionary<string, object>
    {
        ["environment"] = Environment(),
        ["settings"] = new Dictionary<string, object> { ["rounds"] = rounds, ["round_ms"] = roundMs },
        ["write"] = rows,
        ["sizes"] = sizes,
    };
    File.WriteAllText(Path.Combine(outDir, "dotnet.json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine();
    Console.WriteLine("| Workload | Library | Variant | Median µs | ±MAD | Alloc bytes/op |");
    Console.WriteLine("|---|---|---|---:|---:|---:|");
    foreach (var r in rows)
    {
        Console.WriteLine($"| {r["workload"]} | {r["library"]} | {r["variant"]} | {(double)r["median_us"]:N1} | {(double)r["mad_percent"]:N1}% | {(double)r["alloc_bytes"]:N0} |");
    }
}

Dictionary<string, object> Environment() => new()
{
    ["dotnet"] = RuntimeInformation.FrameworkDescription,
    ["os"] = RuntimeInformation.OSDescription,
    ["cpu"] = CpuName(),
    ["tessera"] = typeof(TesseraWriter).Assembly.GetName().Version?.ToString() ?? "",
    ["messagepack"] = typeof(MessagePackSerializer).Assembly.GetName().Version?.ToString() ?? "",
    ["flatbuffers"] = "25.12.19 (C# runtime built from the release source)",
    ["timestamp_utc"] = DateTime.UtcNow.ToString("u"),
};

static string CpuName()
{
    try
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string s) return s.Trim() + $" ({System.Environment.ProcessorCount} logical cores)";
        }
        else if (File.Exists("/proc/cpuinfo"))
        {
            var line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
            if (line != null) return line.Split(':', 2)[1].Trim();
        }
    }
    catch (Exception)
    {
    }

    return RuntimeInformation.ProcessArchitecture.ToString();
}

int ArgInt(string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
}
