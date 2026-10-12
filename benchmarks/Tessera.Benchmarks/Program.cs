using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bench;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using MessagePack;
using Perfolizer.Horology;
using Tessera;

// Usage: Tessera.Benchmarks [--export-only] [--quick] [--launches N] [--rounds N] [--round-ms N] [--warmup-ms N] [--filter text]
//        Tessera.Benchmarks --report <results dir> <out.md>
// 1. builds the workloads, writes every library's buffer and the expected checksums for the C++ benchmark,
// 2. times serialization with BenchmarkDotNet (unless --export-only) and writes benchmarks/results/dotnet.json:
//    every case in --launches processes of its own (default 1), each N iterations of about --round-ms after warmup.
//    Several launches matter: dynamic PGO does not settle on the same code in every process.
// --filter keeps only cases whose "workload/library/variant" contains the text (for focused tuning).
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
int roundMs = ArgInt("--round-ms", 100);
// A second of warmup, quick or not: after less, some processes had not settled on their final optimized code yet
// (Tessera's prefab write measured 83-373 µs in some processes after 180-500 ms, 78-80 µs in every one after 1 s).
int warmupMs = ArgInt("--warmup-ms", 1000);
int launches = ArgInt("--launches", 1);

string root = Catalog.Root;
string dataDir = Path.Combine(root, "benchmarks", "generated", "data");
List<SizeRow> sizes = Catalog.Export(dataDir);
Console.WriteLine($"Exported buffers to {dataDir}");
Console.WriteLine();
Console.WriteLine("| Workload | Library | Bytes | gzip |");
Console.WriteLine("|---|---|---:|---:|");
foreach (var s in sizes) Console.WriteLine($"| {s.Workload} | {s.Library} | {s.Bytes:N0} | {s.Gzip:N0} |");
if (exportOnly) return 0;

int filterAt = Array.IndexOf(args, "--filter");
string filter = filterAt >= 0 && filterAt + 1 < args.Length ? args[filterAt + 1] : "";
WriteBenchmarks.Selected = Catalog.Keys.Where(k => k.Contains(filter, StringComparison.Ordinal)).ToList();

// One process per case and collections forced between iterations (both BenchmarkDotNet's defaults). Result arrays up
// to 4 MB are allocated as ordinary objects that die young: with the default large-object threshold (85 KB), the full
// collections that free them took time that varied erratically with the exact size (155 KB cost twice as much as
// 240 KB), so equal work by two libraries could time differently. Not pinned to one core: .NET compiles the optimized
// code on a background thread, which a pinned benchmark thread at BenchmarkDotNet's high priority starves, and the
// cases then measured unoptimized code (4-6 times slower).
var job = Job.Default
    .WithLaunchCount(launches)
    .WithWarmupCount(Math.Clamp((warmupMs + roundMs - 1) / Math.Max(1, roundMs), 2, 50))
    .WithIterationCount(rounds)
    .WithIterationTime(TimeInterval.FromMilliseconds(roundMs))
    .WithUnrollFactor(1)
    .WithEvaluateOverhead(false)
    .WithGcServer(false)
    .WithGcConcurrent(false)
    .WithGcForce(true)
    .WithEnvironmentVariables(new EnvironmentVariable("DOTNET_GCLOHThreshold", "0x400000"))
    .WithId("write");
var config = ManualConfig.CreateEmpty()
    .AddJob(job)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumnProvider(DefaultColumnProviders.Instance)
    .AddLogger(new ProgressLogger())
    .WithArtifactsPath(Path.Combine(root, "benchmarks", "results", "bdn"));
Summary summary = BenchmarkRunner.Run<WriteBenchmarks>(config);

var rows = new List<Dictionary<string, object>>();
var failed = new List<string>();
foreach (BenchmarkReport report in summary.Reports)
{
    string key = (string)report.BenchmarkCase.Parameters["Case"];
    double[] us = report.AllMeasurements
        .Where(m => m.IterationMode == IterationMode.Workload && m.IterationStage == IterationStage.Actual)
        .Select(m => m.Nanoseconds / m.Operations / 1000).ToArray();
    if (us.Length == 0)
    {
        failed.Add(key);
        continue;
    }

    string[] parts = key.Split('/', 3);
    var st = Stats.Of(us);
    rows.Add(new Dictionary<string, object>
    {
        ["workload"] = parts[0], ["library"] = parts[1], ["variant"] = parts[2],
        ["median_us"] = st.Median, ["min_us"] = st.Min, ["max_us"] = st.Max, ["mad_percent"] = st.MadPercent,
        ["alloc_bytes"] = (double)(report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0), ["samples_us"] = us,
    });
}

foreach (string key in WriteBenchmarks.Selected.Except(rows.Select(r => $"{r["workload"]}/{r["library"]}/{r["variant"]}")).Except(failed)) failed.Add(key);
if (failed.Count > 0)
{
    Console.Error.WriteLine("BenchmarkDotNet produced no measurements for: " + string.Join(", ", failed) + " (logs in benchmarks/results/bdn)");
    return 1;
}

// Report order: the catalog's.
var order = Catalog.Keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
rows.Sort((a, b) => order[$"{a["workload"]}/{a["library"]}/{a["variant"]}"].CompareTo(order[$"{b["workload"]}/{b["library"]}/{b["variant"]}"]));
var outDir = Path.Combine(root, "benchmarks", "results");
Directory.CreateDirectory(outDir);
var doc = new Dictionary<string, object>
{
    ["environment"] = HostInfo(),
    ["settings"] = new Dictionary<string, object> { ["launches"] = launches, ["rounds"] = rounds, ["round_ms"] = roundMs, ["tool"] = "BenchmarkDotNet " + Version(typeof(BenchmarkRunner)) },
    ["write"] = rows,
    ["sizes"] = sizes.Select(s => new Dictionary<string, object> { ["workload"] = s.Workload, ["library"] = s.Library, ["bytes"] = s.Bytes, ["gzip"] = s.Gzip }).ToList(),
};
File.WriteAllText(Path.Combine(outDir, "dotnet.json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
Console.WriteLine("| Workload | Library | Variant | Median µs | ±MAD | Alloc bytes/op |");
Console.WriteLine("|---|---|---|---:|---:|---:|");
foreach (var r in rows)
{
    Console.WriteLine($"| {r["workload"]} | {r["library"]} | {r["variant"]} | {(double)r["median_us"]:N1} | {(double)r["mad_percent"]:N1}% | {(double)r["alloc_bytes"]:N0} |");
}

return 0;

// --------------------------------------------------------------------------------------------------------------------

Dictionary<string, object> HostInfo() => new()
{
    ["dotnet"] = RuntimeInformation.FrameworkDescription,
    ["os"] = RuntimeInformation.OSDescription,
    ["cpu"] = CpuName(),
    ["tessera"] = typeof(TesseraWriter).Assembly.GetName().Version?.ToString() ?? "",
    ["messagepack"] = Version(typeof(MessagePackSerializer)),
    ["flatbuffers"] = "25.12.19 (C# runtime built from the release source)",
    ["memorypack"] = Version(typeof(MemoryPack.MemoryPackSerializer)),
    ["protobuf"] = Version(typeof(Google.Protobuf.IMessage)),
    ["system_text_json"] = Version(typeof(JsonSerializer)),
    ["newtonsoft_json"] = Version(typeof(Newtonsoft.Json.JsonConvert)),
    ["benchmark_tool"] = "BenchmarkDotNet " + Version(typeof(BenchmarkRunner)),
    ["timestamp_utc"] = DateTime.UtcNow.ToString("u"),
};

// The package version (assembly versions are often fixed, such as 13.0.0.0 for every Newtonsoft.Json 13 release).
static string Version(Type type)
{
    string? v = type.Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
    return v == null ? type.Assembly.GetName().Version?.ToString() ?? "" : v.Split('+')[0];
}

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

/// <summary>Median, extremes and median absolute deviation of one case's iterations.</summary>
internal sealed record Stats(double Median, double Min, double Max, double MadPercent)
{
    public static Stats Of(IReadOnlyList<double> samples)
    {
        double[] s = samples.OrderBy(x => x).ToArray();
        double median = Middle(s);
        double mad = Middle(s.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToArray());
        return new Stats(median, s[0], s[^1], median > 0 ? 100 * mad / median : 0);
    }

    private static double Middle(double[] s) => s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
}

/// <summary>Console output: one line per case and BenchmarkDotNet's errors (the full log goes to benchmarks/results/bdn).</summary>
internal sealed class ProgressLogger : ILogger
{
    public string Id => nameof(ProgressLogger);

    public int Priority => 0;

    public void Write(LogKind logKind, string text)
    {
        if (logKind == LogKind.Error) Console.Error.Write(text);
    }

    public void WriteLine()
    {
    }

    public void WriteLine(LogKind logKind, string text)
    {
        if (logKind == LogKind.Error || text.StartsWith("// Benchmark: ", StringComparison.Ordinal) || text.StartsWith("// ** Remained", StringComparison.Ordinal))
            Console.Error.WriteLine(text);
    }

    public void Flush() => Console.Error.Flush();
}
