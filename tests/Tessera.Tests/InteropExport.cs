using System;
using System.IO;
using TestModels;
using Xunit;

namespace Tessera.Tests;

/// <summary>Writes the generated C++ header and sample buffers for the C++ interop tests (tests/cpp).</summary>
public class InteropExport
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }
    }

    public static string InteropDir => Path.Combine(RepoRoot, "tests", "cpp", "generated");

    public static string InteropSharedDir => Path.Combine(RepoRoot, "tests", "cpp", "generated-shared");

    [Fact]
    public void ExportsHeaderAndBuffers()
    {
        var written = TesseraCppHeaders.Export(typeof(Monster).Assembly, InteropDir);
        Assert.True(File.Exists(Path.Combine(InteropDir, "Tessera.Tests.tessera.hpp")));

        Write("monster_full", TesseraSerializer.Serialize(Samples.Full()));
        Write("monster_full_noshare", TesseraSerializer.Serialize(Samples.Full(), new TesseraOptions { Sharing = Sharing.None }));
        Write("monster_full_shareall", TesseraSerializer.Serialize(Samples.Full(), new TesseraOptions { Sharing = Sharing.All }));
        Write("monster_full_noschema", TesseraSerializer.Serialize(Samples.Full(), new TesseraOptions { IncludeSchema = false }));
        Write("monster_sparse", TesseraSerializer.Serialize(Samples.Sparse()));
        Write("monster_defaults", TesseraSerializer.Serialize(Samples.Sparse(), new TesseraOptions { WriteDefaults = true }));
        Write("tree", TesseraSerializer.Serialize(Samples.Tree(), new TesseraOptions { Sharing = Sharing.All }));
        Write("empty", TesseraSerializer.Serialize(new Empty()));
        Write("deep_default", TesseraSerializer.Serialize(Samples.Chain(64)));                                 // deepest the writer allows by default
        Write("deep_200", TesseraSerializer.Serialize(Samples.Chain(100), new TesseraOptions { MaxDepth = 200 }));
        var all = new TesseraOptions { Sharing = Sharing.All };
        Write("dag", TesseraSerializer.Serialize(Samples.Dag(14), all));
        Write("dag_small", TesseraSerializer.Serialize(Samples.Dag(4), all));
        Write("dag_twice", TesseraSerializer.Serialize(Samples.DagTwice(deepFirst: false), all));
        Write("dag_twice_deep_first", TesseraSerializer.Serialize(Samples.DagTwice(deepFirst: true), all));
        Write("shared_long_string", TesseraSerializer.Serialize(Samples.SharedLongString()));
        Write("shared_tags", TesseraSerializer.Serialize(Samples.SharedTags(), all));
        Write("swarm", TesseraSerializer.Serialize(Samples.Swarm()));
        Write("catalog", TesseraSerializer.Serialize(Samples.Catalog()));
        Write("readings", TesseraSerializer.Serialize(Samples.Readings()));

        // Two model assemblies: Tessera.Tests' headers also define the Tessera.Tests.Shared types it uses. The C++ test
        // includes both sets of headers (identical type headers must merge) and reads a buffer that relies on defaults
        // declared by initializers in the other assembly.
        TesseraCppHeaders.Export(typeof(SharedModels.Badge).Assembly, InteropSharedDir);
        Write("profile", TesseraSerializer.Serialize(new TestModels.Behavior.Profile
        {
            Name = "Ann",
            Badge = new SharedModels.Badge { Title = "Champion" },                     // Level 5 and Rarity Rare are defaults
            Badges = new() { new SharedModels.Badge { Level = 9, Rarity = SharedModels.Rarity.Epic, Where = new SharedModels.Point { X = 1, Y = 2 } } },
            Reward = new SharedModels.Gold(),                                         // Amount 10 is a default
        }));
        Write("player_v1", TesseraSerializer.Serialize(Samples.PlayerV1()));
        Write("player_v1_noschema", TesseraSerializer.Serialize(Samples.PlayerV1(), new TesseraOptions { IncludeSchema = false }));
        Write("player_v2", TesseraSerializer.Serialize(Samples.PlayerV2()));
        Write("player_v3", TesseraSerializer.Serialize(Samples.PlayerV3()));
        Write("wide_dense", TesseraSerializer.Serialize(Samples.WideDense()));
        Write("wide_sparse", TesseraSerializer.Serialize(Samples.WideSparse()));
    }

    private static void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(InteropDir, name + ".bin"), bytes);
}
