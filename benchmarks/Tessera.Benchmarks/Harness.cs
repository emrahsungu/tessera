using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Bench;

/// <summary>One measured operation.</summary>
public sealed class Case
{
    public required string Workload { get; init; }
    public required string Library { get; init; }
    public required string Variant { get; init; }
    public required Func<int> Op { get; init; }
    public List<double> NsPerOp { get; } = new();
    public double AllocatedBytesPerOp { get; set; }
    public int OutputBytes { get; set; }
}

/// <summary>Summary statistics of one case.</summary>
public sealed record Stats(double Median, double Min, double Max, double MadPercent);

/// <summary>
/// Timing loop: warm up every case, then run interleaved rounds (each case runs ~RoundMs per round) so slow drifts
/// (turbo, thermal, background work) affect all cases alike. Reports the median of the per-round results.
/// </summary>
public static class Harness
{
    public static int Sink;

    public static void Run(IReadOnlyList<Case> cases, int rounds, int roundMs, int warmupMs, Action<string>? log = null)
    {
        foreach (var c in cases)
        {
            log?.Invoke($"warm up {c.Workload} / {c.Library} {c.Variant}");
            c.OutputBytes = c.Op();
            Spin(c, warmupMs);
            // Allocations per operation (measured on this thread only).
            int n = Calibrate(c, 50);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < n; i++) Sink ^= c.Op();
            c.AllocatedBytesPerOp = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)n;
        }

        var order = Enumerable.Range(0, cases.Count).ToArray();
        var rng = new Random(42);
        for (int r = 0; r < rounds; r++)
        {
            log?.Invoke($"round {r + 1}/{rounds}");
            rng.Shuffle(order);
            foreach (int i in order)
            {
                var c = cases[i];
                int n = Calibrate(c, roundMs);
                long t0 = Stopwatch.GetTimestamp();
                for (int k = 0; k < n; k++) Sink ^= c.Op();
                long t1 = Stopwatch.GetTimestamp();
                c.NsPerOp.Add((t1 - t0) * 1e9 / Stopwatch.Frequency / n);
            }
        }
    }

    /// <summary>Iterations that take about <paramref name="ms"/> milliseconds.</summary>
    private static int Calibrate(Case c, int ms)
    {
        int n = 1;
        while (true)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Sink ^= c.Op();
            double elapsed = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            if (elapsed >= ms / 4.0 || n >= 1 << 24) return Math.Max(1, (int)(n * ms / Math.Max(elapsed, 0.001)));
            n *= 2;
        }
    }

    private static void Spin(Case c, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) Sink ^= c.Op();
    }

    public static Stats Summarize(IReadOnlyList<double> samples)
    {
        var s = samples.OrderBy(x => x).ToArray();
        double median = s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
        var dev = s.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToArray();
        double mad = dev.Length % 2 == 1 ? dev[dev.Length / 2] : (dev[dev.Length / 2 - 1] + dev[dev.Length / 2]) / 2;
        return new Stats(median, s[0], s[^1], median > 0 ? 100 * mad / median : 0);
    }
}
