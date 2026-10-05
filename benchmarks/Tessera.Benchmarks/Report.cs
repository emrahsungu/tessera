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
    public static string Build(string resultsDir)
    {
        var sb = new StringBuilder();
        using var dotnet = JsonDocument.Parse(File.ReadAllText(Path.Combine(resultsDir, "dotnet.json")));
        var env = dotnet.RootElement.GetProperty("environment");
        var natives = Directory.GetFiles(resultsDir, "native-*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonDocument.Parse(File.ReadAllText(f))).ToList();

        sb.AppendLine("# Benchmark results");
        sb.AppendLine();
        sb.AppendLine($"- CPU: {env.GetProperty("cpu").GetString()}");
        sb.AppendLine($"- OS: {env.GetProperty("os").GetString()}; .NET: {env.GetProperty("dotnet").GetString()}");
        sb.AppendLine($"- C++ compilers: {string.Join(", ", natives.Select(n => n.RootElement.GetProperty("compiler").GetString()))}");
        sb.AppendLine($"- Libraries: Tessera {env.GetProperty("tessera").GetString()}, FlatBuffers {env.GetProperty("flatbuffers").GetString()}, MessagePack-CSharp {env.GetProperty("messagepack").GetString()}, msgpack-cxx 9.0.0");
        sb.AppendLine($"- Recorded {env.GetProperty("timestamp_utc").GetString()}. Medians of interleaved rounds; ±MAD in the raw JSON.");
        sb.AppendLine();
        sb.AppendLine("Every reader computes a checksum over every field (presence included) and must match the value computed from the C# objects before anything is timed.");
        sb.AppendLine();

        var sizes = dotnet.RootElement.GetProperty("sizes").EnumerateArray()
            .Select(s => (Workload: s.GetProperty("workload").GetString()!, Library: s.GetProperty("library").GetString()!, Bytes: s.GetProperty("bytes").GetInt32(), Gzip: s.GetProperty("gzip").GetInt32()))
            .ToList();
        string[] workloads = sizes.Select(s => s.Workload).Distinct().ToArray();
        var write = dotnet.RootElement.GetProperty("write").EnumerateArray()
            .Select(w => (Workload: w.GetProperty("workload").GetString()!, Library: w.GetProperty("library").GetString()!, Variant: w.GetProperty("variant").GetString()!,
                Us: w.GetProperty("median_us").GetDouble(), Alloc: w.GetProperty("alloc_bytes").GetDouble()))
            .ToList();

        // ------------------------------------------------------------------ summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"How many times faster (or smaller) Tessera is: the other library's time or size divided by Tessera's, as the geometric mean over the {workloads.Length} workloads, with the per-workload range in parentheses. Above 1 means Tessera wins.");
        sb.AppendLine();
        sb.AppendLine("| | vs FlatBuffers | vs MessagePack |");
        sb.AppendLine("|---|---:|---:|");
        void Summary(string label, Func<string, string, double> metric, bool messagePack = true)
        {
            string Ratio(string other)
            {
                var r = workloads.Select(w => (Other: metric(w, other), Tessera: metric(w, "Tessera"))).Where(x => x.Other > 0 && x.Tessera > 0).Select(x => x.Other / x.Tessera).ToList();
                if (r.Count == 0) return "";
                double geo = Math.Exp(r.Average(Math.Log));
                string g = geo >= 1 ? $"**{geo:0.00}**" : $"{geo:0.00}";
                return $"{g} ({r.Min():0.00}–{r.Max():0.00})";
            }

            sb.AppendLine($"| {label} | {Ratio("FlatBuffers")} | {(messagePack ? Ratio("MessagePack") : "")} |");
        }

        Summary("Size", (w, lib) => sizes.FirstOrDefault(s => s.Workload == w && s.Library == lib).Bytes);
        Summary(".NET write", (w, lib) => write.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == "to byte[]").Us);
        foreach (var native in natives)
        {
            var root = native.RootElement;
            string compiler = root.GetProperty("compiler").GetString()!;
            var rows = ReadRows(root);
            foreach (var (label, prefix, mp) in new[] { ("verify", "verify", true), ("full traversal", "full traversal", false), ("verify + traversal", "verify + traversal", true), ("random access", "random access x1000", false) })
            {
                Summary($"C++ {label} ({compiler})", (w, lib) => Op(rows, w, lib, prefix), mp);
            }
        }

        sb.AppendLine();
        sb.AppendLine("MessagePack has no separate verify step: its \"verify\" is a full parse into a tree, which its traversal and random access then use (those two are left out of the MessagePack column because they exclude the parse).");
        sb.AppendLine();
        sb.AppendLine($"Where Tessera is slower in these results (by more than {(SlowerMargin - 1) * 100:0}% for the C++ reads), and why:");
        sb.AppendLine();
        foreach (string line in Slower(natives, workloads, write, sizes)) sb.AppendLine("- " + line);
        sb.AppendLine();

        // ------------------------------------------------------------------ sizes
        string[] sizeColumns = { "Tessera", "Tessera (full sharing)", "Tessera (no schema)", "FlatBuffers", "FlatBuffers (shared strings)", "MessagePack" };
        sb.AppendLine("## Size (bytes, smaller is better)");
        sb.AppendLine();
        Table(sb, "Workload", sizeColumns, workloads, (w, c) =>
        {
            var row = sizes.FirstOrDefault(s => s.Workload == w && s.Library == c);
            return row.Library == null ? "" : row.Bytes.ToString("N0", CultureInfo.InvariantCulture);
        }, (w, c) => sizes.Where(s => s.Workload == w && (s.Library == "Tessera" || s.Library == "FlatBuffers" || s.Library == "MessagePack")).Min(s => s.Bytes) == sizes.FirstOrDefault(s => s.Workload == w && s.Library == c).Bytes);
        sb.AppendLine();
        sb.AppendLine("gzip -9 of the same buffers (for transfer/storage comparisons):");
        sb.AppendLine();
        Table(sb, "Workload", new[] { "Tessera", "FlatBuffers", "MessagePack" }, workloads, (w, c) =>
        {
            var row = sizes.FirstOrDefault(s => s.Workload == w && s.Library == c);
            return row.Library == null ? "" : row.Gzip.ToString("N0", CultureInfo.InvariantCulture);
        });
        sb.AppendLine();

        // ------------------------------------------------------------------ .NET write
        string[] writeColumns = { "Tessera", "Tessera (no sharing)", "Tessera (full sharing)", "FlatBuffers", "MessagePack" };
        sb.AppendLine("## .NET write (µs per buffer, lower is better)");
        sb.AppendLine();
        sb.AppendLine("Object graph to `byte[]` with a reused writer/builder (each library's normal path). Tessera's default shares equal strings.");
        sb.AppendLine();
        Table(sb, "Workload", writeColumns, workloads, (w, c) => Fmt(write.FirstOrDefault(x => x.Workload == w && x.Library == c && x.Variant == "to byte[]").Us),
            (w, c) => Best(write.Where(x => x.Workload == w && x.Variant == "to byte[]" && (x.Library == "Tessera" || x.Library == "FlatBuffers" || x.Library == "MessagePack" || x.Library == "Tessera (no sharing)")).Select(x => (x.Library, x.Us)), c));
        sb.AppendLine();
        sb.AppendLine("Allocated bytes per write without the final copy (reused writer, builder or buffer):");
        sb.AppendLine();
        var noCopy = new Dictionary<string, string> { ["Tessera"] = "reused writer, no copy", ["FlatBuffers"] = "reused builder, no copy", ["MessagePack"] = "reused buffer, no copy" };
        Table(sb, "Workload", noCopy.Keys.ToArray(), workloads, (w, c) =>
        {
            var row = write.FirstOrDefault(x => x.Workload == w && x.Library == c && x.Variant == noCopy[c]);
            return row.Library == null ? "" : $"{Fmt(row.Us)} µs, {row.Alloc.ToString("N0", CultureInfo.InvariantCulture)} B";
        });
        sb.AppendLine();

        // ------------------------------------------------------------------ C++ read
        foreach (var native in natives)
        {
            var root = native.RootElement;
            var rows = ReadRows(root);
            sb.AppendLine($"## C++ read — {root.GetProperty("compiler").GetString()} (µs, lower is better)");
            sb.AppendLine();
            foreach (var (title, prefix) in new[]
                     {
                         ("Verify (MessagePack: parse into a tree)", "verify"),
                         ("Full traversal of every field (MessagePack: over the already parsed tree)", "full traversal"),
                         ("Verify + full traversal (safe end-to-end read)", "verify + traversal"),
                         ("1000 random element reads (MessagePack: on the parsed tree)", "random access x1000"),
                         ("Open + read one field", "open + read one field"),
                     })
            {
                sb.AppendLine($"**{title}**");
                sb.AppendLine();
                string Cell(string w, string lib) => Fmt(Op(rows, w, lib, prefix));
                var libs = new[] { "Tessera", "FlatBuffers", "MessagePack" };
                Table(sb, "Workload", libs.Concat(new[] { "Tessera vs FlatBuffers" }).ToArray(), workloads, (w, c) =>
                {
                    if (c != "Tessera vs FlatBuffers") return Cell(w, c);
                    double a = Op(rows, w, "Tessera", prefix), b = Op(rows, w, "FlatBuffers", prefix);
                    if (a <= 0 || b <= 0) return "";
                    return a <= b ? $"{b / a:0.00}× faster" : $"{a / b:0.00}× slower";
                }, (w, c) => c != "Tessera vs FlatBuffers" && Best(libs.Select(l => (l, Parse(Cell(w, l)))), c));
                sb.AppendLine();
            }
        }

        // ------------------------------------------------------------------ dictionary lookups
        if (workloads.Contains("lookup"))
        {
            sb.AppendLine("## Dictionary lookups (µs per 1,000 lookups, lower is better)");
            sb.AppendLine();
            sb.AppendLine("The lookup workload: a `Dictionary<string, Stock>` and a `Dictionary<int, double>` of 5,000 entries each, read with 1,000 keys that are present. Tessera and FlatBuffers store the keys sorted and binary-search them (`find`, `LookupByKey`). MessagePack's parsed tree has no index, so it is scanned.");
            sb.AppendLine();
            var lookupColumns = new[] { "Tessera", "FlatBuffers", "MessagePack" };
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
                ("Size (bytes)", sizes.First(s => s.Workload == w && s.Library == "Tessera").Bytes, sizes.First(s => s.Workload == w && s.Library == "Tessera (fixed)").Bytes, true),
                (".NET write (µs)", write.FirstOrDefault(x => x.Workload == w && x.Library == "Tessera" && x.Variant == "to byte[]").Us,
                    write.FirstOrDefault(x => x.Workload == w && x.Library == "Tessera (fixed)" && x.Variant == "to byte[]").Us, false),
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

    /// <summary>Closer than this is within run-to-run noise.</summary>
    private const double SlowerMargin = 1.05;

    /// <summary>The cases in which Tessera loses, computed from the results, each with its cause.</summary>
    private static List<string> Slower(List<JsonDocument> natives, string[] workloads,
        List<(string Workload, string Library, string Variant, double Us, double Alloc)> write, List<(string Workload, string Library, int Bytes, int Gzip)> sizes)
    {
        var lines = new List<string>();
        string X(double r) => r.ToString("0.00", CultureInfo.InvariantCulture) + "×";

        // C++ reads against FlatBuffers: per operation, the workloads where Tessera is slower, per compiler.
        var reads = new (string Prefix, string Title, string Why)[]
        {
            ("verify", "Verify", ""),
            ("full traversal", "Reading every field", "Every member's presence bit is tested, and compilers differ in how many branches they make of these checks: MSVC makes more than Clang on union-heavy objects such as prefab's, for the same source."),
            ("verify + traversal", "Verify, then read every field", ""),
            ("random access x1000", "Random reads", "A member's position is a popcount over the presence bits of the members before it, so in wide objects (prefab, monsters) a late member takes a few more instructions than FlatBuffers' vtable lookup. On lookup these are dictionary entries read by index: keys and values are two vectors, so an entry takes one or two more loads than FlatBuffers' vector of key-value tables (lookups by key are faster)."),
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

            if (parts.Count > 0) lines.Add($"**{title}** (FlatBuffers faster): {string.Join("; ", parts)}.{(why.Length > 0 ? " " + why : "")}");
        }

        // .NET write: the workloads where FlatBuffers or MessagePack write faster.
        foreach (string other in new[] { "FlatBuffers", "MessagePack" })
        {
            double Us(string w, string lib) => write.FirstOrDefault(x => x.Workload == w && x.Library == lib && x.Variant == "to byte[]").Us;
            var slow = workloads.Select(w => (w, Ratio: Us(w, "Tessera") / Us(w, other))).Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio)).ToList();
            if (slow.Count == 0) continue;
            string why = slow.Any(x => x.w == "lookup")
                ? " The lookup workload writes dictionaries, whose keys Tessera sorts so that C++ can binary-search them; MessagePack writes the entries as they come and can then only scan them."
                : "";
            lines.Add($"**.NET write** ({other} faster): {string.Join(", ", slow.Select(x => $"{x.w} {X(x.Ratio)}"))}.{why}");
        }

        // Size: the workloads where FlatBuffers or MessagePack buffers are smaller.
        foreach (string other in new[] { "FlatBuffers", "MessagePack" })
        {
            double Bytes(string w, string lib) => sizes.FirstOrDefault(s => s.Workload == w && s.Library == lib).Bytes;
            var smaller = workloads.Select(w => (w, Ratio: Bytes(w, "Tessera") / Bytes(w, other))).Where(x => x.Ratio > 1 && !double.IsInfinity(x.Ratio)).ToList();
            if (smaller.Count == 0) continue;
            string why = other == "MessagePack" ? " MessagePack stores small integers in one or two bytes; Tessera keeps values fixed-width so they can be read in place." : "";
            lines.Add($"**Size** ({other} smaller): {string.Join(", ", smaller.Select(x => $"{x.w} {X(x.Ratio)}"))}.{why}");
        }

        return lines;
    }

    /// <summary>"Both take 2–4 ns.": the range of Tessera's and FlatBuffers' open + read one field over all runs.</summary>
    private static string OpenRange(List<JsonDocument> natives)
    {
        var ns = natives.SelectMany(n => ReadRows(n.RootElement)).Where(r => r.Op == "open + read one field" && (r.Library == "Tessera" || r.Library == "FlatBuffers"))
            .Select(r => r.Us * 1000).ToList();
        return ns.Count == 0 ? "" : $"Both take {ns.Min().ToString("0", CultureInfo.InvariantCulture)}–{ns.Max().ToString("0", CultureInfo.InvariantCulture)} ns.";
    }

    private static List<(string Workload, string Library, string Op, double Us)> ReadRows(JsonElement root) =>
        root.GetProperty("read").EnumerateArray()
            .Select(r => (r.GetProperty("workload").GetString()!, r.GetProperty("library").GetString()!, r.GetProperty("operation").GetString()!, r.GetProperty("median_us").GetDouble()))
            .ToList();

    /// <summary>Median µs of the operation whose name starts with <paramref name="prefix"/> ("verify" excludes "verify + traversal").</summary>
    private static double Op(List<(string Workload, string Library, string Op, double Us)> rows, string workload, string library, string prefix) =>
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

    private static double Parse(string s) => double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}
