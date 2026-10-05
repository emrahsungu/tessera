using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tessera.Generator.Tests;

/// <summary>Runs the generator over source text, the same way the compiler does during a build.</summary>
internal static class GeneratorHarness
{
    private static readonly MetadataReference[] References = BuildReferences();

    /// <summary>A separately compiled assembly that models can reference (to test types from other assemblies).</summary>
    public sealed record Library(string Name, byte[] Image, byte[] ReferenceImage)
    {
        /// <summary>Models compile against the reference assembly, as with a project reference in a real build.</summary>
        public MetadataReference Reference => MetadataReference.CreateFromImage(ReferenceImage);
    }

    public sealed record Result(ImmutableArray<Diagnostic> Diagnostics, Compilation Output, ImmutableArray<GeneratedSourceResult> Sources, Library[] Libraries)
    {
        public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

        public IEnumerable<string> Ids => Diagnostics.Select(d => d.Id);

        public string Writers => Sources.Single(s => s.HintName == "Tessera.Writers.g.cs").SourceText.ToString();

        /// <summary>Every C++ header, decoded from the generated [assembly: TesseraCppHeader(path, content)] attributes.</summary>
        public IReadOnlyList<(string Path, string Content)> Files =>
            Output.Assembly.GetAttributes().Where(a => a.AttributeClass?.Name == "TesseraCppHeaderAttribute")
                .Select(a => ((string)a.ConstructorArguments[0].Value!, (string)a.ConstructorArguments[1].Value!)).ToList();

        /// <summary>All headers' text, concatenated (for content checks).</summary>
        public string Header => string.Concat(Files.Select(f => f.Content + "\n"));

        /// <summary>The umbrella header: the only one not in a namespace folder.</summary>
        public string HeaderName => Files.Single(f => !f.Path.Contains('/')).Path;

        /// <summary>Emits the compiled models and loads them (with their libraries) into a fresh load context.</summary>
        public Assembly Load()
        {
            using var stream = new MemoryStream();
            var emit = Output.Emit(stream);
            if (!emit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
            var context = new AssemblyLoadContext("models", isCollectible: true);
            var loaded = Libraries.ToDictionary(l => l.Name, l => context.LoadFromStream(new MemoryStream(l.Image)));
            context.Resolving += (_, name) => name.Name != null && loaded.TryGetValue(name.Name, out var a) ? a : null;
            stream.Position = 0;
            return context.LoadFromStream(stream);
        }
    }

    /// <summary>
    /// Compiles a library and its reference assembly. Roslyn's reference assemblies keep private struct fields; pass
    /// <paramref name="referenceSource"/> to imitate one that hides them (like the framework's reference packs).
    /// </summary>
    public static Library CompileLibrary(string name, string source, string? referenceSource = null, bool generate = false)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        Compilation compilation = CSharpCompilation.Create(name, new[] { Parse(source) }, References, options);
        if (generate)
        {
            // A model library built with the generator, like a referenced project that uses Tessera itself.
            CSharpGeneratorDriver.Create(new TesseraGenerator().AsSourceGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out compilation, out _);
        }

        using var stream = new MemoryStream();
        using var refStream = new MemoryStream();
        var emit = compilation.Emit(stream, metadataPEStream: refStream);
        if (!emit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        if (referenceSource != null)
        {
            refStream.SetLength(0);
            emit = CSharpCompilation.Create(name, new[] { Parse(referenceSource) }, References, options)
                .Emit(refStream, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: true));
            if (!emit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        return new Library(name, stream.ToArray(), refStream.ToArray());
    }

    /// <summary>
    /// A reference to another project's compilation rather than its assembly, as an IDE may pass a project reference:
    /// its symbols come with syntax, but they still belong to another assembly.
    /// </summary>
    public static MetadataReference CompilationReference(string name, string source) =>
        CSharpCompilation.Create(name, new[] { Parse(source) }, References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)).ToMetadataReference();

    public static Result Run(string source, IReadOnlyDictionary<string, string>? buildProperties = null, params Library[] libraries) =>
        Run(source, buildProperties, libraries, libraries.Select(l => l.Reference));

    public static Result Run(string source, MetadataReference reference) => Run(source, null, Array.Empty<Library>(), new[] { reference });

    private static Result Run(string source, IReadOnlyDictionary<string, string>? buildProperties, Library[] libraries, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create("Models", new[] { Parse(source) }, References.Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        var options = new Options(buildProperties?.ToDictionary(kv => "build_property." + kv.Key, kv => kv.Value) ?? new Dictionary<string, string>());
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new TesseraGenerator().AsSourceGenerator() }, optionsProvider: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var run = driver.GetRunResult().Results.Single();
        return new Result(diagnostics, output, run.GeneratedSources, libraries);
    }

    private static SyntaxTree Parse(string source) => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));

    private static MetadataReference[] BuildReferences()
    {
        // Compile against the framework reference pack like a real build does (its structs carry placeholder private
        // fields, which the generator must not mistake for the real layout). Fall back to the runtime's own assemblies.
        string frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var version = new DirectoryInfo(frameworkDir).Name;
        string refDir = Path.GetFullPath(Path.Combine(frameworkDir, "..", "..", "..", "packs", "Microsoft.NETCore.App.Ref", version, "ref",
            "net" + Environment.Version.ToString(2)));
        var files = Directory.Exists(refDir)
            ? Directory.GetFiles(refDir, "*.dll")
            : ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Where(p => string.Equals(Path.GetDirectoryName(p), frameworkDir, StringComparison.OrdinalIgnoreCase)).ToArray();
        var refs = files.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(TesseraAttribute).Assembly.Location));
        return refs.ToArray();
    }

    private sealed class Options(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        private readonly ConfigOptions _global = new(global);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => ConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => ConfigOptions.Empty;
    }

    private sealed class ConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly ConfigOptions Empty = new(new Dictionary<string, string>());

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
