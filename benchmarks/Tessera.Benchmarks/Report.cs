using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Bench;

/// <summary>Builds docs/BENCHMARKS.md-style tables from benchmarks/results/*.json.</summary>
public static class Report
{
    /// <summary>
    /// A library Tessera is compared with, under the names the results give it per measurement (null: not measured);
    /// <paramref name="Deserialize"/> creates the objects in .NET (Protobuf: parses into its own messages).
    /// </summary>
    internal sealed record Rival(string Name, string? Size, string? Write, string? Read, string? Deserialize);

    /// <summary>JSON is written by System.Text.Json and read by simdjson in C++; MemoryPack has no C++ reader.</summary>
    internal static readonly Rival[] Rivals =
    {
        new("FlatBuffers", "FlatBuffers", "FlatBuffers", "FlatBuffers", null),
        new("MessagePack", "MessagePack", "MessagePack", "MessagePack", "MessagePack"),
        new("Protobuf", "Protobuf", "Protobuf", "Protobuf", "Protobuf"),
        new("JSON", "JSON", "System.Text.Json", "JSON", "System.Text.Json"),
        new("MemoryPack", "MemoryPack", "MemoryPack", null, "MemoryPack"),
    };

    /// <summary>A .NET read: deserializing (Protobuf: parsing into its messages), or reading every field in place.</summary>
    internal static string DeserializeOp(string library) => library == "Protobuf" ? "parse" : "deserialize";

    /// <summary>The C++ readers that parse a buffer before reading it: their traversal and random access read a tree parsed beforehand.</summary>
    internal static readonly string[] Parsers = { "MessagePack", "Protobuf", "JSON" };

    private sealed record Size(string Workload, string Library, int Bytes, int Gzip);

    private sealed record Write(string Workload, string Library, string Variant, double Us, double Alloc);

    public static string Build(string resultsDir)
    {
        var sb = new StringBuilder();
        using var dotnet = JsonDocument.Parse(File.ReadAllText(Path.Combine(resultsDir, "dotnet.json")));
        var env = dotnet.RootElement.GetProperty("environment");
        var natives = Directory.GetFiles(resultsDir, "native-*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonDocument.Parse(File.ReadAllText(f))).ToList();
        string Env(string name) => env.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

        sb.AppendLine("# Benchmark results");
        sb.AppendLine();
        sb.AppendLine($"- CPU: {Env("cpu")}");
        sb.AppendLine($"- OS: {Env("os")}; .NET: {Env("dotnet")}");
        sb.AppendLine($"- C++ compilers: {string.Join(", ", natives.Select(n => n.RootElement.GetProperty("compiler").GetString()))}");
        sb.AppendLine($"- .NET libraries: Tessera {Env("tessera")}, FlatBuffers {Env("flatbuffers")}, MessagePack-CSharp {Env("messagepack")}, MemoryPack {Env("memorypack")}, Google.Protobuf {Env("protobuf")}, System.Text.Json {Env("system_text_json")}, Newtonsoft.Json {Env("newtonsoft_json")}; measured with {Env("benchmark_tool")}");
        sb.AppendLine("- C++ libraries: FlatBuffers 25.12.19, msgpack-cxx 9.0.0, libprotobuf 36.2 (with Abseil 20250512.1), simdjson 5.0.3, each compiled from source with the benchmark's flags");
        sb.AppendLine($"- Recorded {Env("timestamp_utc")}. Medians of interleaved rounds; ±MAD in the raw JSON.");
        sb.AppendLine();
        sb.AppendLine("Every library writes the same C# objects. Every C++ reader computes a checksum over every field (presence included), which must match the value computed from the C# objects before anything is timed. The C++ readers' small helper functions are force-inlined with MSVC, for every library: MSVC stopped inlining them into one large function when it read Tessera (`benchmarks/native/readers.hpp` explains).");
        sb.AppendLine();

        var sizes = dotnet.RootElement.GetProperty("sizes").EnumerateArray()
            .Select(s => new Size(s.GetProperty("workload").GetString()!, s.GetProperty("library").GetString()!, s.GetProperty("bytes").GetInt32(), s.GetProperty("gzip").GetInt32()))
            .ToList();
        string[] workloads = sizes.Select(s => s.Workload).Distinct().ToArray();
        var write = dotnet.RootElement.GetProperty("write").EnumerateArray()
            .Select(w => new Write(w.GetProperty("workload").GetString()!, w.GetProperty("library").GetString()!, w.GetProperty("variant").GetString()!,
                w.GetProperty("median_us").GetDouble(), w.GetProperty("alloc_bytes").GetDouble()))
            .ToList();
        double Bytes(string w, string lib) => sizes.FirstOrDefault(s => s.Workload == w && s.Library == lib)?.Bytes ?? 0;
        double WriteUs(string w, string lib, string variant = "to byte[]") => write.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == variant)?.Us ?? 0;
        var read = DotnetReads(resultsDir);
        double ReadUs(string w, string lib, string op) => read.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == op)?.Us ?? 0;

        // ------------------------------------------------------------------ summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the {workloads.Length} workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.");
        sb.AppendLine();
        sb.AppendLine($"| | {string.Join(" | ", Rivals.Select(r => "vs " + r.Name))} |");
        sb.AppendLine($"|---|{string.Join("|", Rivals.Select(_ => "---:"))}|");
        void Summary(string label, Func<string, string, double> metric, Func<Rival, string?> nameOf)
        {
            string Ratio(string? other)
            {
                if (other == null) return "";
                var r = workloads.Select(w => (Other: metric(w, other), Tessera: metric(w, "Tessera"))).Where(x => x.Other > 0 && x.Tessera > 0).Select(x => x.Other / x.Tessera).ToList();
                if (r.Count == 0) return "";
                double geo = Math.Exp(r.Average(Math.Log));
                string g = geo >= 1 ? $"**{geo:0.00}**" : $"{geo:0.00}";
                return $"{g} ({r.Min():0.00}–{r.Max():0.00})";
            }

            sb.AppendLine($"| {label} | {string.Join(" | ", Rivals.Select(r => Ratio(nameOf(r))))} |");
        }

        Summary("Size", Bytes, r => r.Size);
        Summary(".NET write", (w, lib) => WriteUs(w, lib), r => r.Write);
        if (read.Count > 0)
        {
            Summary(".NET deserialize", (w, lib) => ReadUs(w, lib, DeserializeOp(lib)), r => r.Deserialize);
            Summary(".NET read every field in place, unchecked", (w, lib) => ReadUs(w, lib, "traversal"), r => r.Name == "FlatBuffers" ? "FlatBuffers" : null);
        }
        foreach (var native in natives)
        {
            var root = native.RootElement;
            string compiler = root.GetProperty("compiler").GetString()!;
            var rows = ReadRows(root);
            foreach (var (label, prefix, parsersToo) in SummaryReads)
            {
                Summary($"C++ {label} ({compiler})", (w, lib) => Op(rows, w, lib, prefix), r => r.Read is { } name && (parsersToo || !Parsers.Contains(name)) ? name : null);
            }
        }

        sb.AppendLine();
        if (read.Count > 0)
        {
            sb.AppendLine("In .NET, \"deserialize\" checks the buffer and creates the objects (Tessera's `Deserialize`, Protobuf's parse into its own message classes, the others' deserializers into the same classes); \"read every field in place\" reads a buffer without creating objects, which FlatBuffers does without checking it (its C# verifier cannot check buffers over 32 KB), so Tessera's is unchecked too. The .NET read section below has both, checked and unchecked.");
            sb.AppendLine();
        }

        sb.AppendLine("MessagePack, Protobuf and JSON are parsed before they are read: their \"verify\" is the parse (into a tree for MessagePack and Protobuf, into simdjson's DOM for JSON), and their traversal and random access read a tree parsed beforehand, so those two are left out of their columns. JSON is written by System.Text.Json and read by simdjson. MemoryPack has no C++ reader. How each parser is set up is in the C++ section below.");
        sb.AppendLine();
        sb.AppendLine($"Where Tessera is slower or larger in these results (by more than {(SlowerMargin - 1) * 100:0}% for the C++ reads), and why:");
        sb.AppendLine();
        var slower = Slower(natives, workloads, write, sizes, read);
        if (slower.Count == 0) sb.AppendLine("- Nowhere.");
        foreach (string line in slower) sb.AppendLine("- " + line);
        sb.AppendLine();

        // ------------------------------------------------------------------ sizes
        string[] sizeColumns = { "Tessera", "Tessera (full sharing)", "Tessera (no schema)", "FlatBuffers", "FlatBuffers (shared strings)", "MessagePack", "Protobuf", "MemoryPack", "JSON" };
        string[] sizeBest = { "Tessera", "FlatBuffers", "MessagePack", "Protobuf", "MemoryPack", "JSON" };
        sb.AppendLine("## Size (bytes, smaller is better)");
        sb.AppendLine();
        sb.AppendLine("JSON is System.Text.Json's; Newtonsoft.Json writes the same JSON, apart from how it formats some numbers. The smallest of the libraries' default outputs is in bold.");
        sb.AppendLine();
        Table(sb, "Workload", sizeColumns, workloads, (w, c) => Bytes(w, c) > 0 ? Bytes(w, c).ToString("N0", CultureInfo.InvariantCulture) : "",
            (w, c) => Best(sizeBest.Select(l => (l, Bytes(w, l))), c));
        sb.AppendLine();
        sb.AppendLine("gzip -9 of the same buffers (for transfer/storage comparisons):");
        sb.AppendLine();
        Table(sb, "Workload", sizeBest, workloads, (w, c) =>
        {
            var row = sizes.FirstOrDefault(s => s.Workload == w && s.Library == c);
            return row == null ? "" : row.Gzip.ToString("N0", CultureInfo.InvariantCulture);
        });
        sb.AppendLine();

        // ------------------------------------------------------------------ .NET write
        string[] writeColumns =
        {
            "Tessera", "Tessera (no sharing)", "Tessera (full sharing)", "FlatBuffers", "MessagePack", "MemoryPack", "Protobuf", "Protobuf (prebuilt messages)",
            "System.Text.Json", "Newtonsoft.Json",
        };
        // Full sharing searches for equal objects and prebuilt messages skip the conversion: both do other work than the rest.
        string[] writeBest = writeColumns.Where(c => c is not ("Tessera (full sharing)" or "Protobuf (prebuilt messages)")).ToArray();
        sb.AppendLine("## .NET write (µs per buffer, lower is better)");
        sb.AppendLine();
        sb.AppendLine("Object graph to `byte[]` with a reused writer, builder or buffer (each library's normal path). Tessera's default shares equal strings. FlatBuffers is written with its builder API from the objects, and so is Protobuf: it writes its own generated message classes, which the objects are converted to on every write; \"Protobuf (prebuilt messages)\" leaves the conversion out, as when a program keeps its data in the messages. Both JSON libraries write the same JSON (System.Text.Json with its source-generated serializers). The fastest of the libraries that write from the objects is in bold.");
        sb.AppendLine();
        sb.AppendLine("Measured with BenchmarkDotNet: every case in a process of its own, with collections forced between iterations, and the large-object threshold raised to 4 MB, so result arrays die young like any other object (at the default 85 KB, the full collections that free them took time that varied erratically with the result's exact size). The tables after this one leave the final copy out.");
        sb.AppendLine();
        Table(sb, "Workload", writeColumns, workloads, (w, c) => Fmt(WriteUs(w, c)), (w, c) => Best(writeBest.Select(l => (l, WriteUs(w, l))), c));
        sb.AppendLine();
        var noCopy = new (string Library, string Variant)[]
        {
            ("Tessera", "reused writer, no copy"), ("FlatBuffers", "reused builder, no copy"), ("MessagePack", "reused buffer, no copy"), ("MemoryPack", "reused buffer, no copy"),
            ("Protobuf", "reused buffer, no copy"), ("System.Text.Json", "reused buffer, no copy"), ("Newtonsoft.Json", "reused buffer, no copy"),
        };
        string[] noCopyColumns = noCopy.Select(x => x.Library).ToArray();
        Write? NoCopy(string w, string lib) => write.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == noCopy.First(n => n.Library == lib).Variant);
        sb.AppendLine("Without the final copy (into a reused writer, builder or buffer), µs:");
        sb.AppendLine();
        Table(sb, "Workload", noCopyColumns, workloads, (w, c) => NoCopy(w, c) is { } row ? Fmt(row.Us) : "",
            (w, c) => Best(noCopyColumns.Select(l => (l, NoCopy(w, l)?.Us ?? 0)), c));
        sb.AppendLine();
        sb.AppendLine("Bytes allocated per write without the final copy:");
        sb.AppendLine();
        Table(sb, "Workload", noCopyColumns, workloads, (w, c) => NoCopy(w, c) is { } row ? row.Alloc.ToString("N0", CultureInfo.InvariantCulture) : "");
        sb.AppendLine();

        // ------------------------------------------------------------------ .NET read
        if (read.Count > 0)
        {
            var deserializeColumns = new (string Title, string Library, string Op)[]
            {
                ("Tessera", "Tessera", "deserialize"), ("Tessera (unchecked)", "Tessera", "deserialize, unchecked"), ("MemoryPack", "MemoryPack", "deserialize"),
                ("MessagePack", "MessagePack", "deserialize"), ("System.Text.Json", "System.Text.Json", "deserialize"),
                ("Newtonsoft.Json", "Newtonsoft.Json", "deserialize"), ("Protobuf (parse)", "Protobuf", "parse"),
            };
            double Col((string Title, string Library, string Op)[] columns, string w, string title) =>
                columns.First(c => c.Title == title) is var c ? ReadUs(w, c.Library, c.Op) : 0;
            double ColAlloc((string Title, string Library, string Op)[] columns, string w, string title) =>
                columns.First(c => c.Title == title) is var c ? read.FirstOrDefault(x => x.Workload == w && x.Library == c.Library && x.Variant == c.Op)?.Alloc ?? 0 : 0;
            string[] checkedColumns = deserializeColumns.Where(c => c.Title != "Tessera (unchecked)").Select(c => c.Title).ToArray();
            sb.AppendLine("## .NET read (µs per buffer, lower is better)");
            sb.AppendLine();
            sb.AppendLine("Deserializing: a buffer (`byte[]`) to the objects it holds. Tessera checks the buffer, then creates the objects (`TesseraSerializer.Deserialize<T>`); \"Tessera (unchecked)\" skips the check (`TesseraReadOptions.Trusted`), as for data a program wrote itself. MemoryPack, MessagePack and both JSON libraries create the same classes; Protobuf parses into its own generated message classes, which a program then uses as they are. Every result is checked against the checksum of the objects it was written from before anything is timed. The fastest of the checked reads is in bold.");
            sb.AppendLine();
            sb.AppendLine("Measured with BenchmarkDotNet, as the .NET write: every case in a process of its own.");
            sb.AppendLine();
            Table(sb, "Workload", deserializeColumns.Select(c => c.Title).ToArray(), workloads, (w, c) => Fmt(Col(deserializeColumns, w, c)),
                (w, c) => Best(checkedColumns.Select(l => (l, Col(deserializeColumns, w, l))), c));
            sb.AppendLine();
            sb.AppendLine("Bytes allocated per read:");
            sb.AppendLine();
            Table(sb, "Workload", deserializeColumns.Select(c => c.Title).ToArray(), workloads, (w, c) =>
                ColAlloc(deserializeColumns, w, c) is var a && a > 0 ? a.ToString("N0", CultureInfo.InvariantCulture) : "");
            sb.AppendLine();
            var placeColumns = new (string Title, string Library, string Op)[]
            {
                ("Tessera: check + every field", "Tessera", "verify + traversal"), ("Tessera: every field", "Tessera", "traversal"),
                ("FlatBuffers: every field", "FlatBuffers", "traversal"),
            };
            sb.AppendLine("Reading in place: every field of a buffer, without creating objects, into the same checksum as the C++ readers (strings by their UTF-8 bytes, not decoded). Tessera reads through its generated read types (`MonsterView.Read`), with its check and without; FlatBuffers through its generated accessors, without its verifier: FlatBuffers' C# verifier (25.12.19) cannot check buffers over 32 KB, which it rejects or misreads as missing fields.");
            sb.AppendLine();
            Table(sb, "Workload", placeColumns.Select(c => c.Title).ToArray(), workloads, (w, c) => Fmt(Col(placeColumns, w, c)),
                (w, c) => c != placeColumns[0].Title && Best(placeColumns.Skip(1).Select(x => (x.Title, Col(placeColumns, w, x.Title))), c));
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ C++ read
        sb.AppendLine("## C++ read");
        sb.AppendLine();
        sb.AppendLine("Tessera and FlatBuffers read the buffer where it lies. The parsers are each set up their fastest way, measured, and reuse their memory from one parse to the next:");
        sb.AppendLine();
        sb.AppendLine("- msgpack-cxx parses into a reused zone, with strings and binary data referenced in the buffer (its zero-copy option) rather than copied. Its tree for the traversal, random access and lookups is parsed once with the strings copied: that reads as fast and scans keys twice as fast.");
        sb.AppendLine("- libprotobuf parses into a reused message object, as Google advises (a message keeps the memory it allocated when it is cleared). Its parsed tree for the traversal and random access is on an arena, which reads a few percent faster." + ArenaNote(natives, workloads));
        sb.AppendLine("- simdjson reuses its parser. \"Verify\" parses into its DOM, which validates the whole document; the other operations stream through the document with its On-Demand API, its fastest way, computing the checksum without building a tree.");
        sb.AppendLine();
        foreach (var native in natives)
        {
            var root = native.RootElement;
            var rows = ReadRows(root);
            sb.AppendLine($"### {root.GetProperty("compiler").GetString()} (µs, lower is better)");
            sb.AppendLine();
            foreach (var (title, prefix, parsed) in new[]
                     {
                         ("Verify (MessagePack, Protobuf and JSON: the parse)", "verify", false),
                         ("Full traversal of every field (MessagePack and Protobuf: over the already parsed tree)", "full traversal", true),
                         ("Verify + full traversal (safe end-to-end read)", "verify + traversal", false),
                         ("1000 random element reads (MessagePack and Protobuf: on the parsed tree)", "random access x1000", true),
                         ("Open + read one field (the parsers parse first)", "open + read one field", false),
                     })
            {
                sb.AppendLine($"**{title}**");
                sb.AppendLine();
                var libs = new[] { "Tessera", "FlatBuffers", "MessagePack", "Protobuf", "JSON" };
                // Over a tree parsed beforehand, the parsers do less work than the zero-copy readers: only those two compete.
                var compete = parsed ? new[] { "Tessera", "FlatBuffers" } : libs;
                Table(sb, "Workload", libs.Append("Tessera vs FlatBuffers").ToArray(), workloads, (w, c) =>
                {
                    if (c != "Tessera vs FlatBuffers") return Fmt(Op(rows, w, c, prefix));
                    double a = Op(rows, w, "Tessera", prefix), b = Op(rows, w, "FlatBuffers", prefix);
                    if (a <= 0 || b <= 0) return "";
                    return a <= b ? $"{b / a:0.00}× faster" : $"{a / b:0.00}× slower";
                }, (w, c) => compete.Contains(c) && Best(compete.Select(l => (l, Op(rows, w, l, prefix))), c));
                sb.AppendLine();
            }
        }

        // ------------------------------------------------------------------ dictionary lookups
        if (workloads.Contains("lookup"))
        {
            sb.AppendLine("## Dictionary lookups (µs per 1,000 lookups, lower is better)");
            sb.AppendLine();
            sb.AppendLine("The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, and neither have Protobuf's entries, so both are scanned: the benchmark's Protobuf schema has repeated key-value entries, because a map field would parse into a hash map, whose order is unspecified.");
            sb.AppendLine();
            var lookupColumns = new[] { "Tessera", "FlatBuffers", "MessagePack", "Protobuf" };
            foreach (var (title, prefix) in new[] { ("By string", "1000 lookups by string"), ("By int", "1000 lookups by int") })
            {
                sb.AppendLine($"**{title}**");
                sb.AppendLine();
                var compilers = natives.Select(n => n.RootElement.GetProperty("compiler").GetString()!).ToArray();
                var rowsOf = natives.ToDictionary(n => n.RootElement.GetProperty("compiler").GetString()!, n => ReadRows(n.RootElement));
                Table(sb, "Compiler", lookupColumns, compilers, (c, lib) => Fmt(Op(rowsOf[c], "lookup", lib, prefix)),
                    (c, lib) => Best(lookupColumns.Select(l => (l, Op(rowsOf[c], "lookup", l, prefix))), lib));
                sb.AppendLine();
            }
        }

        // ------------------------------------------------------------------ fixed cells (opt-in)
        foreach (string w in sizes.Where(s => s.Library == "Tessera (fixed)").Select(s => s.Workload).Distinct())
        {
            sb.AppendLine($"## Fixed cells: the {w} workload with `[TesseraKeepDefault]`");
            sb.AppendLine();
            sb.AppendLine("The same data in a model whose always-set members are marked `[TesseraKeepDefault]`, so they are stored at constant positions without presence bits. This is opt-in, so the tables above use the plain model.");
            sb.AppendLine();
            var metrics = new List<(string Label, double Plain, double Fixed, bool Bytes)>
            {
                ("Size (bytes)", Bytes(w, "Tessera"), Bytes(w, "Tessera (fixed)"), true),
                (".NET write (µs)", WriteUs(w, "Tessera"), WriteUs(w, "Tessera (fixed)"), false),
            };
            foreach (var native in natives)
            {
                string compiler = native.RootElement.GetProperty("compiler").GetString()!;
                var rows = ReadRows(native.RootElement);
                foreach (var (label, prefix) in new[] { ("verify", "verify"), ("full traversal", "full traversal"), ("verify + traversal", "verify + traversal"), ("random access", "random access x1000") })
                {
                    metrics.Add(($"C++ {label}, {compiler} (µs)", Op(rows, w, "Tessera", prefix), Op(rows, w, "Tessera (fixed)", prefix), false));
                }
            }

            Table(sb, "", new[] { "Tessera", "Tessera (fixed)", "Change" }, metrics.Select(m => m.Label).ToArray(), (label, c) =>
            {
                var m = metrics.First(x => x.Label == label);
                if (m.Plain <= 0 || m.Fixed <= 0) return "";
                string Value(double v) => m.Bytes ? v.ToString("N0", CultureInfo.InvariantCulture) : Fmt(v);
                double change = (m.Fixed / m.Plain - 1) * 100;
                return c == "Tessera" ? Value(m.Plain) : c == "Tessera (fixed)" ? Value(m.Fixed) : (change < 0 ? "−" : "+") + Math.Abs(change).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            });
            sb.AppendLine();
        }

        sb.AppendLine("Raw samples, MAD and the exact settings are in [benchmarks/](benchmarks/) (`*.json`, copied from `benchmarks/results` by `scripts/bench.ps1`, which reproduces everything).");
        return sb.ToString();
    }

    /// <summary>The C++ rows of the summary: label, operation, and whether the parsers take part (only where their parse is included).</summary>
    internal static readonly (string Label, string Prefix, bool Parsers)[] SummaryReads =
    {
        ("verify", "verify", true), ("full traversal", "full traversal", false), ("verify + traversal", "verify + traversal", true), ("random access", "random access x1000", false),
    };

    /// <summary>Closer than this is within run-to-run noise.</summary>
    private const double SlowerMargin = 1.05;

    /// <summary>Where a new arena per parse beats the reused message for Protobuf, per compiler.</summary>
    private static string ArenaNote(List<JsonDocument> natives, string[] workloads)
    {
        var parts = new List<string>();
        foreach (string w in workloads)
        {
            var faster = new List<string>();
            foreach (var native in natives)
            {
                var rows = ReadRows(native.RootElement);
                double reused = Op(rows, w, "Protobuf", "verify"), arena = Op(rows, w, "Protobuf", "parse with a new arena");
                if (reused > 0 && arena > 0 && arena < reused)
                    faster.Add($"{native.RootElement.GetProperty("compiler").GetString()} {Fmt(arena)} µs instead of {Fmt(reused)}");
            }

            if (faster.Count > 0) parts.Add($"{w} ({string.Join("; ", faster)})");
        }

        return parts.Count == 0 ? "" : $" A new arena per parse, the other fast way, is faster on {string.Join(", ", parts)}: there every element replaces a oneof's member, which a reused message allocates anew.";
    }

    /// <summary>The cases in which Tessera loses, computed from the results, each with its cause.</summary>
    private static List<string> Slower(List<JsonDocument> natives, string[] workloads, List<Write> write, List<Size> sizes, List<Write> read)
    {
        var lines = new List<string>();
        string X(double r) => r.ToString("0.00", CultureInfo.InvariantCulture) + "×";

        // C++ reads against FlatBuffers: per operation, the workloads where Tessera is slower, per compiler.
        var reads = new (string Prefix, string Title, string Why)[]
        {
            ("verify", "Verify", ""),
            ("full traversal", "Reading every field", "Tessera tests each member's presence bit and finds a present member's cell with popcounts or table loads; FlatBuffers reads each member's position from the object's vtable."),
            ("verify + traversal", "Verify, then read every field", ""),
            ("random access x1000", "Random reads", "A member's position is computed from the presence bits of the members before it (a popcount per run of equal-size cells, or a table load per byte of presence bits), which can take a few more instructions than FlatBuffers' vtable lookup."),
            ("open + read one field", "Opening a buffer and reading one field", OpenRange(natives)),
            ("1000 lookups by string", "Dictionary lookups by string", ""),
            ("1000 lookups by int", "Dictionary lookups by int", ""),
        };
        foreach (var (prefix, title, why) in reads)
        {
            var parts = new List<string>();
            foreach (var native in natives)
            {
                var rows = ReadRows(native.RootElement);
                var slow = workloads.Select(w => (w, Ratio: Op(rows, w, "Tessera", prefix) / Op(rows, w, "FlatBuffers", prefix)))
                    .Where(x => !double.IsNaN(x.Ratio) && !double.IsInfinity(x.Ratio) && x.Ratio > SlowerMargin).ToList();
                if (slow.Count > 0) parts.Add($"{native.RootElement.GetProperty("compiler").GetString()}: {string.Join(", ", slow.Select(x => $"{x.w} {X(x.Ratio)}"))}");
            }

            string because = why;
            if (prefix == "random access x1000" && parts.Any(p => p.Contains("lookup ", StringComparison.Ordinal)))
                because += " On lookup these are dictionary entries read by index: keys and values are two vectors, so an entry takes one or two more loads than FlatBuffers' vector of key-value tables (lookups by key are faster).";
            if (parts.Count > 0) lines.Add($"**{title}** (FlatBuffers faster): {string.Join("; ", parts)}.{(because.Length > 0 ? " " + because : "")}");
        }

        // C++ reads against the parsers, where their parse is included.
        foreach (string parser in Parsers)
        {
            foreach (var (prefix, title) in new[] { ("verify", "Verify"), ("verify + traversal", "Verify, then read every field"), ("open + read one field", "Opening a buffer and reading one field"), ("1000 lookups", "Dictionary lookups") })
            {
                var parts = new List<string>();
                foreach (var native in natives)
                {
                    var rows = ReadRows(native.RootElement);
                    var slow = workloads.Select(w => (w, Ratio: Op(rows, w, "Tessera", prefix) / Op(rows, w, parser, prefix)))
                        .Where(x => !double.IsNaN(x.Ratio) && !double.IsInfinity(x.Ratio) && x.Ratio > SlowerMargin).ToList();
                    if (slow.Count > 0) parts.Add($"{native.RootElement.GetProperty("compiler").GetString()}: {string.Join(", ", slow.Select(x => $"{x.w} {X(x.Ratio)}"))}");
                }

                if (parts.Count > 0) lines.Add($"**{title}** ({parser} faster): {string.Join("; ", parts)}.");
            }
        }

        // .NET write: the workloads where another library writes faster.
        foreach (string other in new[] { "FlatBuffers", "MessagePack", "MemoryPack", "Protobuf", "System.Text.Json", "Newtonsoft.Json" })
        {
            double Us(string w, string lib) => write.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == "to byte[]")?.Us ?? 0;
            var slow = workloads.Select(w => (w, Ratio: Us(w, "Tessera") / Us(w, other))).Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio)).ToList();
            if (slow.Count == 0) continue;
            string why = other switch
            {
                "MessagePack" when slow.Any(x => x.w == "lookup") =>
                    " The lookup workload writes dictionaries, which Tessera stores as sorted keys (so that C++ can binary-search them) and a separate vector of values; MessagePack writes the entries as they come, inline, and can then only scan them.",
                "MemoryPack" => " MemoryPack writes the members one after another, with nothing that lets a reader find one without reading those before it, and copies arrays of plain structs as they lie in memory; it can only be read from the start, and only by .NET.",
                _ => "",
            };
            lines.Add($"**.NET write** ({other} faster): {string.Join(", ", slow.Select(x => $"{x.w} {X(x.Ratio)}"))}.{why}");
        }

        // .NET read: deserializing against the others, reading in place against FlatBuffers (both unchecked).
        double ReadUs(string w, string lib, string op) => read.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == op)?.Us ?? 0;
        foreach (string other in new[] { "MemoryPack", "MessagePack", "Protobuf", "System.Text.Json", "Newtonsoft.Json" })
        {
            var slow = workloads.Select(w => (w, Ratio: ReadUs(w, "Tessera", "deserialize") / ReadUs(w, other, DeserializeOp(other))))
                .Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio) && !double.IsNaN(x.Ratio)).ToList();
            if (slow.Count == 0) continue;
            string why = other == "MemoryPack"
                ? " Tessera's time includes its check of the buffer, which MemoryPack does as it reads; without the check (unchecked), see the table. MemoryPack stores strings as UTF-16 by default, so its strings are a copy; Tessera's are UTF-8, decoded."
                : "";
            lines.Add($"**.NET deserialize** ({other} faster): {string.Join(", ", slow.Select(x => $"{x.w} {X(x.Ratio)}"))}.{why}");
        }

        var slowPlace = workloads.Select(w => (w, Ratio: ReadUs(w, "Tessera", "traversal") / ReadUs(w, "FlatBuffers", "traversal")))
            .Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio) && !double.IsNaN(x.Ratio)).ToList();
        if (slowPlace.Count > 0) lines.Add($"**.NET read every field in place** (FlatBuffers faster): {string.Join(", ", slowPlace.Select(x => $"{x.w} {X(x.Ratio)}"))}.");

        // Size: the workloads where another library's buffer is smaller.
        foreach (string other in new[] { "FlatBuffers", "MessagePack", "Protobuf", "MemoryPack", "JSON" })
        {
            double Bytes(string w, string lib) => sizes.FirstOrDefault(s => s.Workload == w && s.Library == lib)?.Bytes ?? 0;
            var smaller = workloads.Select(w => (w, Ratio: Bytes(w, "Tessera") / Bytes(w, other))).Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio)).ToList();
            if (smaller.Count == 0) continue;
            string why = other switch
            {
                "MessagePack" => " MessagePack stores small integers in one or two bytes; Tessera keeps values fixed-width so they can be read in place.",
                "Protobuf" => " Protobuf stores integers as varints (one byte for small values) and leaves out zeros; Tessera keeps values fixed-width so they can be read in place.",
                "MemoryPack" => " MemoryPack writes the members one after another, with nothing that lets a reader find one without reading those before it; Tessera stores the offsets that let C++ read any member directly.",
                _ => "",
            };
            lines.Add($"**Size** ({other} smaller): {string.Join(", ", smaller.Select(x => $"{x.w} {X(x.Ratio)}"))}.{why}");
        }

        return lines;
    }

    /// <summary>The .NET reads in benchmarks/results/dotnet-read.json (operation in Variant), or none.</summary>
    private static List<Write> DotnetReads(string resultsDir)
    {
        string path = Path.Combine(resultsDir, "dotnet-read.json");
        if (!File.Exists(path)) return new List<Write>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("read").EnumerateArray()
            .Select(r => new Write(r.GetProperty("workload").GetString()!, r.GetProperty("library").GetString()!, r.GetProperty("operation").GetString()!,
                r.GetProperty("median_us").GetDouble(), r.GetProperty("alloc_bytes").GetDouble()))
            .ToList();
    }

    /// <summary>"Both take 2–4 ns.": the range of Tessera's and FlatBuffers' open + read one field over all runs.</summary>
    private static string OpenRange(List<JsonDocument> natives)
    {
        var ns = natives.SelectMany(n => ReadRows(n.RootElement)).Where(r => r.Op == "open + read one field" && (r.Library == "Tessera" || r.Library == "FlatBuffers"))
            .Select(r => r.Us * 1000).ToList();
        return ns.Count == 0 ? "" : $"Both take {ns.Min().ToString("0", CultureInfo.InvariantCulture)}–{ns.Max().ToString("0", CultureInfo.InvariantCulture)} ns.";
    }

    internal static List<(string Workload, string Library, string Op, double Us)> ReadRows(JsonElement root) =>
        root.GetProperty("read").EnumerateArray()
            .Select(r => (r.GetProperty("workload").GetString()!, r.GetProperty("library").GetString()!, r.GetProperty("operation").GetString()!, r.GetProperty("median_us").GetDouble()))
            .ToList();

    /// <summary>Median µs of the operation whose name starts with <paramref name="prefix"/> ("verify" excludes "verify + traversal").</summary>
    internal static double Op(List<(string Workload, string Library, string Op, double Us)> rows, string workload, string library, string prefix) =>
        rows.FirstOrDefault(r => r.Workload == workload && r.Library == library && r.Op.StartsWith(prefix, StringComparison.Ordinal) &&
                                 (prefix != "verify" || !r.Op.Contains("traversal", StringComparison.Ordinal))).Us;

    private static void Table(StringBuilder sb, string first, string[] columns, string[] rows, Func<string, string, string> cell, Func<string, string, bool>? bold = null)
    {
        sb.AppendLine($"| {first} | {string.Join(" | ", columns)} |");
        sb.AppendLine($"|---|{string.Join("|", columns.Select(_ => "---:"))}|");
        foreach (var r in rows)
        {
            var cells = columns.Select(c =>
            {
                string v = cell(r, c);
                return v.Length > 0 && bold != null && bold(r, c) ? $"**{v}**" : v;
            });
            sb.AppendLine($"| {r} | {string.Join(" | ", cells)} |");
        }
    }

    private static bool Best(IEnumerable<(string Library, double Value)> values, string column)
    {
        var list = values.Where(v => v.Value > 0).ToList();
        if (list.Count == 0) return false;
        double min = list.Min(v => v.Value);
        return list.Any(v => v.Library == column && v.Value == min);
    }

    private static string Fmt(double us) => us <= 0 ? "" : us >= 100 ? us.ToString("N0", CultureInfo.InvariantCulture) : us >= 10 ? us.ToString("0.0", CultureInfo.InvariantCulture) : us >= 0.1 ? us.ToString("0.00", CultureInfo.InvariantCulture) : us.ToString("0.0000", CultureInfo.InvariantCulture);
}
