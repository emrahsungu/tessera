using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;

namespace Bench;

/// <summary>
/// .NET read benchmark: every case ("workload/library/operation", see <see cref="Catalog"/>) is one BenchmarkDotNet
/// parameter value, so it runs in a process of its own, with a heap that no other case has used.
/// </summary>
public class ReadBenchmarks
{
    /// <summary>The cases to run; Program sets them before BenchmarkDotNet enumerates the parameter values.</summary>
    public static IReadOnlyList<string> Selected { get; set; } = Array.Empty<string>();

    public static IEnumerable<string> Cases() => Selected;

    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "";

    private Func<int> _op = null!;

    [GlobalSetup]
    public void Setup() => _op = Catalog.CreateReadOp(Case);

    [Benchmark]
    public int Read() => _op();
}
