using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Bench;

/// <summary>
/// SVG bar charts of benchmarks/results/*.json for the README, in a light and a dark variant (GitHub picks one with
/// &lt;picture&gt;). Times-better charts use a log scale centred on 1×, so 2× better and 2× worse are the same length.
/// </summary>
public static class Charts
{
    private sealed record Theme(string Name, string Fg, string Muted, string Grid, string Tessera, string FlatBuffers, string MessagePack, string[] Compilers);

    private static readonly Theme[] Themes =
    {
        // Tessera in the brand colors: deep teal #075366 and turquoise #15CFC5, cream #FFF5E5 on dark backgrounds.
        new("light", "#1f2328", "#59636e", "#d1d9e0", "#075366", "#8c959f", "#8a63d2", new[] { "#075366", "#0e8f96", "#15cfc5" }),
        new("dark", "#e6edf3", "#9198a1", "#3d444d", "#15cfc5", "#848d97", "#b392f0", new[] { "#fff5e5", "#15cfc5", "#0e8f96" }),
    };

    private const int Width = 860, LabelX = 16, PlotLeft = 210, PlotRight = Width - 90;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Read(string Workload, string Library, string Op, double Us);

    public static void Write(string resultsDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        using var dotnet = JsonDocument.Parse(File.ReadAllText(Path.Combine(resultsDir, "dotnet.json")));
        var sizes = dotnet.RootElement.GetProperty("sizes").EnumerateArray()
            .Select(s => (Workload: s.GetProperty("workload").GetString()!, Library: s.GetProperty("library").GetString()!, Bytes: (double)s.GetProperty("bytes").GetInt32()))
            .ToList();
        var write = dotnet.RootElement.GetProperty("write").EnumerateArray()
            .Where(w => w.GetProperty("variant").GetString() == "to byte[]")
            .Select(w => (Workload: w.GetProperty("workload").GetString()!, Library: w.GetProperty("library").GetString()!, Us: w.GetProperty("median_us").GetDouble()))
            .ToList();
        var natives = Directory.GetFiles(resultsDir, "native-*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            string compiler = Pretty(doc.RootElement.GetProperty("compiler").GetString()!);
            var reads = doc.RootElement.GetProperty("read").EnumerateArray()
                .Select(r => new Read(r.GetProperty("workload").GetString()!, r.GetProperty("library").GetString()!, r.GetProperty("operation").GetString()!, r.GetProperty("median_us").GetDouble()))
                .ToList();
            return (Compiler: compiler, Reads: reads);
        }).ToList();
        string[] workloads = sizes.Select(s => s.Workload).Distinct().ToArray();

        double Size(string w, string lib) => sizes.FirstOrDefault(s => s.Workload == w && s.Library == lib).Bytes;
        double WriteUs(string w, string lib) => write.FirstOrDefault(x => x.Workload == w && x.Library == lib).Us;
        static double Op(List<Read> reads, string w, string lib, string prefix) =>
            reads.FirstOrDefault(r => r.Workload == w && r.Library == lib && r.Op.StartsWith(prefix, StringComparison.Ordinal) &&
                                      (prefix != "verify" || !r.Op.Contains("traversal", StringComparison.Ordinal)))?.Us ?? 0;
        double Geo(Func<string, double> other, Func<string, double> tessera)
        {
            var r = workloads.Select(w => (O: other(w), I: tessera(w))).Where(x => x.O > 0 && x.I > 0).Select(x => Math.Log(x.O / x.I)).ToList();
            return r.Count == 0 ? 0 : Math.Exp(r.Average());
        }

        // 1. Summary: times better than FlatBuffers and MessagePack (geometric means over the workloads).
        var summary = new List<(string Group, string Label, double[] Values)>
        {
            ("Size", "", new[] { Geo(w => Size(w, "FlatBuffers"), w => Size(w, "Tessera")), Geo(w => Size(w, "MessagePack"), w => Size(w, "Tessera")) }),
            (".NET write", "", new[] { Geo(w => WriteUs(w, "FlatBuffers"), w => WriteUs(w, "Tessera")), Geo(w => WriteUs(w, "MessagePack"), w => WriteUs(w, "Tessera")) }),
        };
        foreach (var (title, prefix, mp) in new[] { ("Verify", "verify", true), ("Verify + read every field", "verify + traversal", true), ("Read every field (trusted)", "full traversal", false), ("1,000 random reads", "random access x1000", false) })
        {
            foreach (var (compiler, reads) in natives)
            {
                double fb = Geo(w => Op(reads, w, "FlatBuffers", prefix), w => Op(reads, w, "Tessera", prefix));
                double m = mp ? Geo(w => Op(reads, w, "MessagePack", prefix), w => Op(reads, w, "Tessera", prefix)) : double.NaN;
                summary.Add((title, compiler, new[] { fb, m }));
            }
        }

        // 2-3. Per-workload absolute numbers.
        string[] libs = { "Tessera", "FlatBuffers", "MessagePack" };
        var writeRows = workloads.Select(w => (w, libs.Select(l => WriteUs(w, l)).ToArray())).ToList();
        var sizeRows = workloads.Select(w => (w, libs.Select(l => Size(w, l)).ToArray())).ToList();

        // 4-5. Tessera against FlatBuffers per workload, per compiler.
        List<(string, double[])> PerWorkload(string prefix) =>
            workloads.Select(w => (w, natives.Select(n => Op(n.Reads, w, "FlatBuffers", prefix) / Op(n.Reads, w, "Tessera", prefix)).ToArray())).ToList();

        // 6. Dictionary lookups.
        var lookups = new List<(string Group, string Label, double[] Values)>();
        foreach (var (title, prefix) in new[] { ("By string", "1000 lookups by string"), ("By int", "1000 lookups by int") })
        {
            foreach (var (compiler, reads) in natives) lookups.Add((title, compiler, new[] { Op(reads, "lookup", "Tessera", prefix), Op(reads, "lookup", "FlatBuffers", prefix) }));
        }

        string[] compilers = natives.Select(n => n.Compiler).ToArray();
        foreach (var t in Themes)
        {
            Save(outDir, "summary", t, RatioChart(t, "Tessera, times better", "Geometric mean over the " + workloads.Length + " workloads. Right of 1× Tessera is better; log scale.",
                new[] { ("vs FlatBuffers", t.Tessera), ("vs MessagePack", t.MessagePack) }, summary, 0.25, 16));
            Save(outDir, "write", t, GroupedChart(t, ".NET write", "Microseconds per buffer, to byte[]; shorter is better. Each workload has its own scale.",
                libs.Select((l, i) => (l, new[] { t.Tessera, t.FlatBuffers, t.MessagePack }[i])).ToArray(), writeRows, Us));
            Save(outDir, "size", t, GroupedChart(t, "Buffer size", "Bytes; shorter is better. Each workload has its own scale.",
                libs.Select((l, i) => (l, new[] { t.Tessera, t.FlatBuffers, t.MessagePack }[i])).ToArray(), sizeRows, b => b.ToString("N0", Inv)));
            Save(outDir, "safe-read", t, RatioChart(t, "Verify, then read every field: Tessera vs FlatBuffers", "Times faster per workload. Right of 1× Tessera is faster; log scale.",
                compilers.Select((c, i) => (c, t.Compilers[i % t.Compilers.Length])).ToArray(), PerWorkload("verify + traversal").Select(r => (r.Item1, "", r.Item2)).ToList(), 0.25, 4));
            Save(outDir, "random-access", t, RatioChart(t, "1,000 random reads: Tessera vs FlatBuffers", "Times faster per workload. Left of 1× Tessera is slower; log scale.",
                compilers.Select((c, i) => (c, t.Compilers[i % t.Compilers.Length])).ToArray(), PerWorkload("random access x1000").Select(r => (r.Item1, "", r.Item2)).ToList(), 0.25, 4));
            Save(outDir, "lookups", t, LookupChart(t, lookups));
        }
    }

    private static string Pretty(string compiler)
    {
        var parts = compiler.Split(' ');
        string name = parts[0] switch { "clang" => "Clang", "gcc" => "GCC", "msvc" => "MSVC", var s => s };
        string version = parts.Length > 1 ? string.Join('.', parts[1].Split('.').Take(2)) : "";
        return (name + " " + version).Trim();
    }

    private static string Us(double us) => us >= 100 ? us.ToString("N0", Inv) : us >= 10 ? us.ToString("0.0", Inv) : us.ToString("0.00", Inv);

    private static void Save(string dir, string name, Theme t, string svg) => File.WriteAllText(Path.Combine(dir, $"{name}-{t.Name}.svg"), svg, new UTF8Encoding(false));

    // ------------------------------------------------------------------------------------------------ drawing helpers

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string F(double v) => v.ToString("0.#", Inv);

    private static void Text(StringBuilder sb, double x, double y, string text, string color, int size = 13, string anchor = "start", bool bold = false, bool mono = false) =>
        sb.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" fill=\"{color}\" font-size=\"{size}\" text-anchor=\"{anchor}\"{(bold ? " font-weight=\"600\"" : "")}{(mono ? " font-family=\"ui-monospace, SFMono-Regular, Consolas, 'Liberation Mono', monospace\"" : "")}>{Esc(text)}</text>");

    private static string Wrap(StringBuilder body, int height) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{height}\" viewBox=\"0 0 {Width} {height}\" font-family=\"-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif\">{body}</svg>\n";

    private static double Header(StringBuilder sb, Theme t, string title, string subtitle, (string Name, string Color)[] legend)
    {
        Text(sb, LabelX, 24, title, t.Fg, 16, bold: true);
        Text(sb, LabelX, 44, subtitle, t.Muted, 12);
        double x = LabelX;
        foreach (var (name, color) in legend)
        {
            sb.Append($"<rect x=\"{F(x)}\" y=\"56\" width=\"12\" height=\"12\" rx=\"2\" fill=\"{color}\"/>");
            Text(sb, x + 18, 66.5, name, t.Muted, 12);
            x += 30 + name.Length * 7;
        }

        return 86;
    }

    /// <summary>Diverging bars on a log scale from <paramref name="min"/>× to <paramref name="max"/>×, one bar per series and row.</summary>
    private static string RatioChart(Theme t, string title, string subtitle, (string Name, string Color)[] series, List<(string Group, string Label, double[] Values)> rows, double min, double max)
    {
        var sb = new StringBuilder();
        double top = Header(sb, t, title, subtitle, series);
        double X(double v) => PlotLeft + (PlotRight - PlotLeft) * (Math.Log2(Math.Clamp(v, min, max)) - Math.Log2(min)) / (Math.Log2(max) - Math.Log2(min));
        const double bar = 9, gap = 3, rowGap = 9, groupGap = 10;
        int n = series.Length;
        double rowH = n * bar + (n - 1) * gap;

        // Layout first, so the grid can span the plot.
        var y = new List<double>();
        double cy = top + 18;
        string? last = null;
        foreach (var r in rows)
        {
            if (r.Group != last && last != null) cy += groupGap;
            if (r.Label.Length > 0 && r.Group != last) cy += 16;  // group heading above labelled rows
            y.Add(cy);
            cy += rowH + rowGap;
            last = r.Group;
        }

        double bottom = cy - rowGap + 6;
        for (double v = min; v <= max * 1.0001; v *= 2)
        {
            sb.Append($"<line x1=\"{F(X(v))}\" y1=\"{F(top + 8)}\" x2=\"{F(X(v))}\" y2=\"{F(bottom)}\" stroke=\"{(Math.Abs(v - 1) < 1e-9 ? t.Muted : t.Grid)}\" stroke-width=\"{(Math.Abs(v - 1) < 1e-9 ? 1.5 : 1)}\"{(Math.Abs(v - 1) < 1e-9 ? " stroke-dasharray=\"4 3\"" : "")}/>");
            Text(sb, X(v), bottom + 16, v < 1 ? $"1/{F(1 / v)}×" : $"{F(v)}×", t.Muted, 11, "middle", mono: true);
        }

        last = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Label.Length > 0)
            {
                if (r.Group != last) Text(sb, LabelX, y[i] - 6, r.Group, t.Fg, 13, bold: true);
                Text(sb, LabelX + 12, y[i] + rowH / 2 + 4, r.Label, t.Muted, 12);
            }
            else
            {
                Text(sb, LabelX, y[i] + rowH / 2 + 4.5, r.Group, t.Fg, 13, bold: true);
            }

            last = r.Group;
            for (int s = 0; s < n; s++)
            {
                double v = r.Values[s];
                if (double.IsNaN(v) || v <= 0) continue;
                double by = y[i] + s * (bar + gap), x0 = X(1), x1 = X(v);
                double left = Math.Min(x0, x1), w = Math.Max(1.5, Math.Abs(x1 - x0));
                sb.Append($"<rect x=\"{F(left)}\" y=\"{F(by)}\" width=\"{F(w)}\" height=\"{F(bar)}\" rx=\"1.5\" fill=\"{series[s].Color}\"/>");
                string label = v.ToString(v >= 10 ? "0.0" : "0.00", Inv) + "×";
                if (v >= 1) Text(sb, x1 + 5, by + bar - 1, label, t.Fg, 11, mono: true);
                else Text(sb, x1 - 5, by + bar - 1, label, t.Fg, 11, "end", mono: true);
            }
        }

        return Wrap(sb, (int)Math.Ceiling(bottom + 28));
    }

    /// <summary>Per-workload bars of absolute values; each workload is scaled on its own.</summary>
    private static string GroupedChart(Theme t, string title, string subtitle, (string Name, string Color)[] series, List<(string Workload, double[] Values)> rows, Func<double, string> format)
    {
        var sb = new StringBuilder();
        double y = Header(sb, t, title, subtitle, series) + 6;
        const double bar = 10, gap = 3, groupGap = 14;
        int n = series.Length;
        foreach (var (workload, values) in rows)
        {
            double max = values.Where(v => v > 0).DefaultIfEmpty(1).Max(), best = values.Where(v => v > 0).DefaultIfEmpty(0).Min();
            double h = n * bar + (n - 1) * gap;
            Text(sb, LabelX, y + h / 2 + 4.5, workload, t.Fg, 13, bold: true);
            for (int s = 0; s < n; s++)
            {
                double v = values[s];
                double by = y + s * (bar + gap);
                if (v <= 0) continue;
                double w = Math.Max(1.5, (PlotRight - PlotLeft) * v / max);
                sb.Append($"<rect x=\"{PlotLeft}\" y=\"{F(by)}\" width=\"{F(w)}\" height=\"{F(bar)}\" rx=\"1.5\" fill=\"{series[s].Color}\"/>");
                Text(sb, PlotLeft + w + 5, by + bar - 1, format(v), v == best ? t.Fg : t.Muted, 11, bold: v == best, mono: true);
            }

            y += h + groupGap;
        }

        return Wrap(sb, (int)Math.Ceiling(y + 4));
    }

    /// <summary>Dictionary lookups: Tessera and FlatBuffers per compiler, shared scale per group.</summary>
    private static string LookupChart(Theme t, List<(string Group, string Label, double[] Values)> rows)
    {
        var sb = new StringBuilder();
        double y = Header(sb, t, "Dictionary lookups", "Microseconds per 1,000 lookups among 5,000 entries; shorter is better. MessagePack scans its parsed tree (8–9 ms by string, 0.8–1.2 ms by int) and is left out.",
            new[] { ("Tessera (find)", t.Tessera), ("FlatBuffers (LookupByKey)", t.FlatBuffers) }) + 6;
        const double bar = 10, gap = 3, rowGap = 9, groupGap = 12;
        foreach (var group in rows.GroupBy(r => r.Group))
        {
            double max = group.SelectMany(r => r.Values).Max();
            Text(sb, LabelX, y + 10, group.Key, t.Fg, 13, bold: true);
            y += 18;
            foreach (var r in group)
            {
                double h = 2 * bar + gap;
                Text(sb, LabelX + 12, y + h / 2 + 4, r.Label, t.Muted, 12);
                for (int s = 0; s < 2; s++)
                {
                    double v = r.Values[s], by = y + s * (bar + gap);
                    double w = Math.Max(1.5, (PlotRight - PlotLeft) * v / max);
                    sb.Append($"<rect x=\"{PlotLeft}\" y=\"{F(by)}\" width=\"{F(w)}\" height=\"{F(bar)}\" rx=\"1.5\" fill=\"{(s == 0 ? t.Tessera : t.FlatBuffers)}\"/>");
                    Text(sb, PlotLeft + w + 5, by + bar - 1, Us(v), s == 0 ? t.Fg : t.Muted, 11, mono: true);
                }

                y += h + rowGap;
            }

            y += groupGap;
        }

        return Wrap(sb, (int)Math.Ceiling(y));
    }
}
