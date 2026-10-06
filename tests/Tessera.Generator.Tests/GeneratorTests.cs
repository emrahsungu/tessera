using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Tessera.Generator.Tests;

public class GeneratorTests
{
    private const string Usings = "using System; using System.Collections.Generic; using System.ComponentModel; using Tessera;\n";

    private static GeneratorHarness.Result Run(string source, IReadOnlyDictionary<string, string>? properties = null, params GeneratorHarness.Library[] libraries) =>
        GeneratorHarness.Run(Usings + source, properties, libraries);

    /// <summary>No errors anywhere and no warnings in generated code (users may build with warnings as errors).</summary>
    private static void AssertCompilesCleanly(GeneratorHarness.Result r)
    {
        var problems = r.Output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error ||
                        d.Severity == DiagnosticSeverity.Warning && (d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) ?? false))
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void ValidModelGeneratesCompilingWritersAndHeader()
    {
        var r = Run("""
            namespace Game.Data;
            public enum Kind : byte { A, B }
            public struct Vec2 { public float X, Y; }
            [Tessera] public class Thing
            {
                public string? Name;
                public int? Count;
                public Kind Kind = Kind.B;
                public Vec2 Pos { get; set; }
                public List<Thing>? Children;
                public bool Visible = true;
                internal long Secret;
                private int _hidden;
                [TesseraIgnore] public int Skipped;
                public int Computed => Count ?? 0;
            }
            """);
        Assert.Empty(r.Diagnostics);
        // Tessera.Defaults.g.cs records the initializer defaults (Kind.B, true) for generators of other assemblies.
        Assert.Equal(new[] { "Tessera.CppHeader.g.cs", "Tessera.Defaults.g.cs", "Tessera.Writers.g.cs" }, r.Sources.Select(s => s.HintName).OrderBy(x => x, StringComparer.Ordinal));
        AssertCompilesCleanly(r);
        Assert.Equal("Models.tessera.hpp", r.HeaderName);
        string h = r.Header;
        Assert.Contains("namespace game::data {", h);
        Assert.Contains("class Thing final : public tessera::Table<Thing>", h);
        Assert.Contains("enum class Kind : std::uint8_t", h);
        Assert.Contains("struct Vec2 {", h);
        Assert.Contains("std::optional<std::int32_t> Thing::count() const noexcept", h);
        Assert.Contains("secret() const noexcept;", h);           // internal members of the same assembly are included
        Assert.DoesNotContain("hidden", h);
        Assert.DoesNotContain("skipped", h);
        Assert.DoesNotContain("computed", h);                     // get-only computed properties are not data
        Assert.Contains("tessera::Vector<Thing>", h);
        Assert.DoesNotContain("Unsafe.SizeOf", r.Writers);        // structs of this assembly need no layout check
    }

    [Fact]
    public void BuildPropertiesControlTheHeader()
    {
        var r = Run("""
            namespace Game;
            [Tessera] public class Thing { public int MaxHP; public string? DisplayName; }
            """, new Dictionary<string, string>
        {
            ["TesseraCppNaming"] = "camelCase",
            ["TesseraCppNamespace"] = "my::models",
            ["TesseraCppHeaderName"] = "things.hpp",
        });
        Assert.Empty(r.Diagnostics);
        Assert.Equal("things.hpp", r.HeaderName);
        Assert.Contains("namespace my::models {", r.Header);
        Assert.Contains("maxHP() const noexcept;", r.Header);
        Assert.Contains("displayName() const noexcept;", r.Header);
        Assert.Contains("hasMaxHP() const noexcept;", r.Header);
    }

    [Fact]
    public void SnakeCaseIsTheDefault()
    {
        var r = Run("[Tessera] public class Thing { public int MaxHP; public string? URLPath; }");
        Assert.Contains("max_hp() const noexcept;", r.Header);
        Assert.Contains("url_path() const noexcept;", r.Header);
        Assert.Contains("has_max_hp() const noexcept;", r.Header);
        Assert.Contains("namespace models {", r.Header);   // global namespace: named after the assembly
    }

    [Theory]
    [InlineData("public Dictionary<double, int>? Map;", "dictionary keys must be")]
    [InlineData("public struct S { public string? A; } public List<S?>? Values;", "nullable elements must be")]
    [InlineData("public int[,]? Grid;", "single-dimensional")]
    [InlineData("public object? Anything;", "concrete model type")]
    [InlineData("public Action? Callback;", "delegates")]
    [InlineData("public IntPtr Handle;", "native-sized")]
    [InlineData("public nuint Size;", "native-sized")]
    [InlineData("public DateTime When;", "DateTime.Ticks")]
    [InlineData("public DateTime? When;", "DateTime.Ticks")]
    [InlineData("public List<DateTime>? Times;", "DateTime.Ticks")]
    [InlineData("public DateTimeOffset When;", "UtcTicks")]
    [InlineData("public TimeSpan Span;", "TimeSpan.Ticks")]
    [InlineData("public Guid Id;", "16-byte struct")]
    [InlineData("public decimal Price;", "decimal")]
    [InlineData("public Half Small;", "Half")]
    [InlineData("public Int128 Huge;", "Int128")]
    public void UnsupportedMemberTypesAreErrors(string member, string reason)
    {
        var r = Run("[Tessera] public class Thing { " + member + " }");
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA001", d.Id);
        Assert.Contains(reason, d.GetMessage());
        Assert.Empty(r.Sources);
    }

    [Fact]
    public void DuplicateWireNamesAreErrors()
    {
        var r = Run("[Tessera] public class Thing { public int Speed; [TesseraName(\"Speed\")] public float MoveSpeed; }");
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA002", d.Id);
        Assert.Contains("MoveSpeed", d.GetMessage());
    }

    [Fact]
    public void PolymorphicMemberWithoutConcreteTypesIsAnError()
    {
        var r = Run("""
            public abstract class Shape { }
            public abstract class Polygon : Shape { }
            public class Outer { private sealed class Circle : Shape { } }   // private: the writers cannot name it
            [Tessera] public class Scene { public Shape? Main; }
            """);
        Assert.Equal("TESSERA003", Assert.Single(r.Errors).Id);
    }

    [Fact]
    public void SubclassesInTheProjectAreUnionMembers()
    {
        var r = Run("""
            namespace Game;
            public abstract class Shape { }
            public sealed class Circle : Shape { public float R; }
            internal sealed class Square : Shape { public float Side; }
            public abstract class Polygon : Shape { }
            public sealed class Triangle : Polygon { public float A; }
            public class Outer { private sealed class Hidden : Shape { } }   // private: the writers cannot name it
            public sealed class Boxed<T> : Shape { public T? Value; }        // generic: not one concrete type
            [Tessera] public class Scene { public Shape? Main; }
            """);
        Assert.Empty(r.Diagnostics);
        AssertCompilesCleanly(r);
        string shape = r.Files.Single(f => f.Path == "game/Shape.tessera.hpp").Content;
        Assert.Equal(new[] { "as_circle", "as_square", "as_triangle" },
            System.Text.RegularExpressions.Regex.Matches(shape, @"\bas_\w+(?=\(\) const)").Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ which types are models

    private const string Unmarked = """
        namespace Game;
        public enum Faction : byte { Neutral, Red }
        public struct Vec3 { public float X, Y, Z; }
        public class Monster { public string? Name; public short Hp = 100; public Vec3 Position; public Faction Faction; public List<Weapon>? Weapons; public Item? Loot; }
        public class Weapon { public string? Name; public int Damage; }
        public abstract class Item { }
        public sealed class Potion : Item { public int Heal; }
        """;

    private const string SerializesMonster = "\npublic static class Save { public static byte[] Write(Monster m) => TesseraSerializer.Serialize(m); }";

    /// <summary><see cref="Unmarked"/> with a bare [Tessera] on every class and struct.</summary>
    private static string Marked => Unmarked.Replace("public class ", "[Tessera] public class ").Replace("public abstract class ", "[Tessera] public abstract class ")
        .Replace("public sealed class ", "[Tessera] public sealed class ").Replace("public struct ", "[Tessera] public struct ");

    [Fact]
    public void SerializedTypesAreModelsWithoutTheAttribute()
    {
        var r = Run(Unmarked + SerializesMonster);
        Assert.Empty(r.Diagnostics);
        AssertCompilesCleanly(r);
        Assert.Equal(new[] { "Models.tessera.hpp", "game/Faction.tessera.hpp", "game/Item.tessera.hpp", "game/Monster.tessera.hpp", "game/Potion.tessera.hpp", "game/Vec3.tessera.hpp", "game/Weapon.tessera.hpp" },
            r.Files.Select(f => f.Path).Where(p => !p.StartsWith("tessera_", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Contains("s_Hp != (100)", r.Writers);   // the initializer is the default

        var models = r.Load();
        var monster = models.GetType("Game.Monster")!;
        var serialize = typeof(TesseraSerializer).GetMethods().Single(m => m.Name == "Serialize" && m.ReturnType == typeof(byte[])).MakeGenericMethod(monster);
        Assert.NotEmpty((byte[])serialize.Invoke(null, new[] { Activator.CreateInstance(monster), null })!);
    }

    [Fact]
    public void BareAttributeChangesNothing()
    {
        // The same models found from the Serialize call, from [Tessera] on every type, or both: identical output.
        var found = Run(Unmarked + SerializesMonster);
        var marked = Run(Marked);
        var both = Run(Marked + SerializesMonster);
        Assert.Empty(marked.Diagnostics);
        foreach (var other in new[] { marked, both })
        {
            Assert.Equal(found.Sources.Select(s => s.HintName), other.Sources.Select(s => s.HintName));
            Assert.Equal(found.Sources.Select(s => s.SourceText.ToString()), other.Sources.Select(s => s.SourceText.ToString()));
        }
    }

    [Fact]
    public void AssemblyRootsAreModels()
    {
        var r = Run("[assembly: TesseraRoot(typeof(Game.Monster), typeof(Game.Settings))]\n" + Unmarked + "public class Settings { public int Volume = 80; }");
        Assert.Empty(r.Diagnostics);
        Assert.Contains("class Monster final", r.Header);
        Assert.Contains("class Settings final", r.Header);
        Assert.Equal(Run(Unmarked + SerializesMonster).Writers, Run("[assembly: TesseraRoot(typeof(Game.Monster))]\n" + Unmarked).Writers);
    }

    [Fact]
    public void GenericCallsAreReported()
    {
        var r = Run(Unmarked + "\npublic static class Save { public static byte[] Write<T>(T value) => TesseraSerializer.Serialize(value); }");
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal("TESSERA010", d.Id);
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Empty(r.Sources);   // nothing else makes Monster a model
    }

    [Theory]
    [InlineData("Item", "abstract or an interface")]
    [InlineData("List<Weapon>", "stored inside models")]
    [InlineData("Dictionary<string, Weapon>", "stored inside models")]
    [InlineData("Weapon[]", "stored inside models")]
    [InlineData("Vec3", "stored inside models")]
    [InlineData("Faction", "stored inside models")]
    [InlineData("string", "stored inside models")]
    [InlineData("int?", "stored inside models")]
    [InlineData("object", "not a model type")]
    public void CallsThatAlwaysThrowAreErrors(string type, string reason)
    {
        var r = Run(Unmarked + $"\npublic static class Save {{ public static byte[] Write({type} value) => TesseraSerializer.Serialize(value); }}");
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA014", d.Id);
        Assert.Contains(reason, d.GetMessage());
    }

    [Fact]
    public void InaccessibleTypesAreErrors()
    {
        var r = Run("public class Outer { private class Secret { public int X; } public static byte[] Write() => TesseraSerializer.Serialize(new Secret()); }");
        Assert.Equal("TESSERA013", Assert.Single(r.Errors).Id);
    }

    [Fact]
    public void RequireAttributeRejectsUnmarkedModels()
    {
        var strict = new Dictionary<string, string> { ["TesseraRequireAttribute"] = "true" };
        var r = Run(Unmarked + SerializesMonster, strict);
        // Every class is reported; the enum, the plain struct and the abstract union base need no mark.
        Assert.All(r.Errors, d => Assert.Equal("TESSERA012", d.Id));
        Assert.Equal(new[] { "'Game.Monster'", "'Game.Potion'", "'Game.Weapon'" }, r.Errors.Select(d => d.GetMessage().Split(' ')[0]).OrderBy(x => x, StringComparer.Ordinal));

        string listed = "[assembly: TesseraRoot(typeof(Game.Weapon))]\n" + Unmarked.Replace("public class Monster", "[Tessera] public class Monster")
            .Replace("public sealed class Potion", "[Tessera] public sealed class Potion");
        Assert.Empty(Run(listed + SerializesMonster, strict).Diagnostics);
    }

    private const string Unit = "namespace Game; public class Unit { public Plain.Stats? Stats; public Plain.Tag? Tag; } public static class Save { public static byte[] Write(Unit u) => TesseraSerializer.Serialize(u); }";

    [Fact]
    public void HiddenInitializersOfOtherProjectsAreReported()
    {
        // A project that does not treat Stats as a model records nothing about it, so '= 100' is invisible here.
        const string plain = Usings + "namespace Plain; public class Stats { public short Hp = 100; public string? Note; } public class Tag { public string? Text; }";
        var r = Run(Unit, null, GeneratorHarness.CompileLibrary("Plain", plain));
        var d = Assert.Single(r.Diagnostics);
        Assert.Equal("TESSERA011", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("'Plain' that are not models there (Stats)", d.GetMessage());   // Tag has no scalar members, so no defaults
        Assert.Contains("s_Hp != 0", r.Writers);

        // When that project serializes Stats, its generator records the initializers.
        var models = GeneratorHarness.CompileLibrary("Plain", plain + " public static class Save { public static byte[] Write(Stats s) => TesseraSerializer.Serialize(s); }", generate: true);
        var ok = Run(Unit, null, models);
        Assert.Empty(ok.Diagnostics);
        Assert.Contains("s_Hp != (100)", ok.Writers);

        // [DefaultValue] is visible in metadata.
        var attributed = GeneratorHarness.CompileLibrary("Plain", plain.Replace("public short Hp = 100;", "[DefaultValue((short)100)] public short Hp = 100;"));
        Assert.Empty(Run(Unit, null, attributed).Diagnostics);

        // A project reference that carries the other project's source (as in an IDE) is still another assembly.
        var fromSource = GeneratorHarness.Run(Usings + Unit, GeneratorHarness.CompilationReference("Plain", plain));
        Assert.Equal("TESSERA011", Assert.Single(fromSource.Diagnostics).Id);
        Assert.Contains("s_Hp != 0", fromSource.Writers);
    }

    [Fact]
    public void UnionMembersFromOtherProjectsMustBeMarked()
    {
        var lib = GeneratorHarness.CompileLibrary("Shapes", Usings + """
            namespace Shapes;
            public abstract class Shape { }
            [Tessera] public sealed class Circle : Shape { public float R; }
            public sealed class Square : Shape { public float Side; }
            """, generate: true);
        var r = Run("namespace Game; public class Scene { public Shapes.Shape? Main; } public static class Save { public static byte[] Write(Scene s) => TesseraSerializer.Serialize(s); }", null, lib);
        Assert.Empty(r.Diagnostics);
        Assert.Contains("as_circle", r.Header);
        Assert.DoesNotContain("as_square", r.Header);
    }

    [Fact]
    public void UnionTagCollisionsAreErrors()
    {
        var r = Run("""
            public abstract class Shape { }
            [Tessera("Same")] public sealed class Circle : Shape { public float R; }
            [Tessera("Same")] public sealed class Square : Shape { public float Side; }
            [Tessera] public class Scene { public Shape? Main; }
            """);
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA004", d.Id);
        Assert.Contains("Circle", d.GetMessage());
        Assert.Contains("Square", d.GetMessage());
    }

    [Fact]
    public void MemberLimitIsEnforced()
    {
        var sb = new StringBuilder("[Tessera] public class Huge {");
        for (int i = 0; i < 4097; i++) sb.Append(" public int F").Append(i).Append(';');
        sb.Append(" }");
        var r = Run(sb.ToString());
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA005", d.Id);
        Assert.Contains("4097", d.GetMessage());
    }

    [Theory]
    [InlineData("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct S { [System.Runtime.InteropServices.FieldOffset(0)] public int A; }", "explicit layout")]
    [InlineData("public unsafe struct S { public fixed byte Data[16]; }", "fixed-size buffers")]
    [InlineData("public struct S { }", "no instance fields")]
    [InlineData("public struct S { public decimal Price; }", "field 'Price': decimal")]
    [InlineData("public struct S { public Guid Id; }", "field 'Id': Guid")]
    public void UnsupportedStructsAreErrors(string declaration, string reason)
    {
        var r = Run(declaration + " [Tessera] public class Thing { public S Value; }");
        var d = Assert.Single(r.Errors);
        Assert.Equal("TESSERA006", d.Id);
        Assert.Contains(reason, d.GetMessage());
    }

    [Fact]
    public void NestedStructErrorsAreReportedOnce()
    {
        var r = Run("public struct Inner { public decimal D; } public struct Outer { public Inner I; } [Tessera] public class Thing { public Outer Value; }");
        var d = Assert.Single(r.Errors);
        Assert.Contains("Inner", d.GetMessage());
    }

    [Fact]
    public void ManagedStructsBecomeObjects()
    {
        var r = Run("public struct Named { public string? Name; public int Id; } [Tessera] public class Thing { public Named Value; public Named? Maybe; }");
        Assert.Empty(r.Errors);
        Assert.Contains("class Named final : public tessera::Table<Named>", r.Header);
        AssertCompilesCleanly(r);
    }

    [Fact]
    public void PublicStructsFromOtherAssembliesWork()
    {
        var r = Run("[Tessera] public class Thing { public System.Numerics.Vector3 Position; public System.Numerics.Quaternion Rotation; }");
        Assert.Empty(r.Diagnostics);
        AssertCompilesCleanly(r);
        Assert.Contains("namespace system::numerics {", r.Header);
        Assert.Contains("struct Vector3 {", r.Header);
        Assert.Contains("Unsafe.SizeOf<global::System.Numerics.Vector3>() != 12", r.Writers);
    }

    private const string ShapesSource = """
        namespace Shapes;
        public struct Open { public int A; public int B; }
        public struct Mixed { public int A; private int _b; public Mixed(int a, int b) { A = a; _b = b; } public int B => _b; }
        """;

    private const string ShapesModels = """
        [Tessera] public class WithOpen { public Shapes.Open Value; }
        [Tessera] public class WithMixed { public Shapes.Mixed Value; }
        """;

    [Fact]
    public void PrivateFieldsOfProjectReferencedStructsAreWritten()
    {
        // Roslyn's reference assemblies keep private struct fields, so a struct from another project is complete.
        var r = Run(ShapesModels, null, GeneratorHarness.CompileLibrary("Shapes", ShapesSource));
        Assert.Empty(r.Errors);
        AssertCompilesCleanly(r);
        var (write, shapes) = Loader(r);
        byte[] bytes = write("WithMixed", Activator.CreateInstance(shapes.GetType("Shapes.Mixed")!, 0x11111111, 0x22222222)!);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x11, 0x11, 0x11, 0x11, 0x22, 0x22, 0x22, 0x22 }) > 0);
    }

    [Fact]
    public void StructsWithHiddenFieldsFailAtTheFirstWrite()
    {
        // A reference assembly that hides private fields (like the framework's) makes the struct look smaller.
        var lib = GeneratorHarness.CompileLibrary("Shapes", ShapesSource, referenceSource: """
            namespace Shapes;
            public struct Open { public int A; public int B; }
            public struct Mixed { public int A; public Mixed(int a, int b) { throw null!; } }
            """);
        var r = Run(ShapesModels, null, lib);
        Assert.Empty(r.Errors);
        AssertCompilesCleanly(r);
        var (write, shapes) = Loader(r);

        object open = Activator.CreateInstance(shapes.GetType("Shapes.Open")!)!;
        open.GetType().GetField("A")!.SetValue(open, 5);
        Assert.NotEmpty(write("WithOpen", open));

        object mixed = Activator.CreateInstance(shapes.GetType("Shapes.Mixed")!, 1, 2)!;
        var e = Assert.Throws<TesseraException>(() => write("WithMixed", mixed));
        Assert.Contains("fields Tessera cannot see", e.Message);
    }

    /// <summary>Loads the compiled models; returns a serializer for "model.Value = value" and the Shapes assembly.</summary>
    private static (Func<string, object, byte[]> Write, Assembly Shapes) Loader(GeneratorHarness.Result r)
    {
        var models = r.Load();
        var serialize = typeof(TesseraSerializer).GetMethods().Single(m => m.Name == "Serialize" && m.ReturnType == typeof(byte[]));
        var shapes = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(models)!.LoadFromAssemblyName(new AssemblyName("Shapes"));
        byte[] Write(string model, object value)
        {
            var type = models.GetType(model)!;
            object instance = Activator.CreateInstance(type)!;
            type.GetField("Value")!.SetValue(instance, value);
            try
            {
                return (byte[])serialize.MakeGenericMethod(type).Invoke(null, new[] { instance, null })!;
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        return (Write, shapes);
    }

    [Fact]
    public void ClashingCppMemberNamesAreAdjusted()
    {
        var r = Run("[Tessera] public class Thing { public int FooBar; public int foo_bar; public int Count; public int has_count; }");
        Assert.Empty(r.Errors);
        Assert.Equal(new[] { "TESSERA007", "TESSERA007" }, r.Ids);
        Assert.Contains("foo_bar_2() const noexcept;", r.Header);
        Assert.Contains("has_count_2() const noexcept;", r.Header);
        AssertCompilesCleanly(r);
    }

    [Fact]
    public void ClashingCppTypeNamesAreAdjusted()
    {
        var r = Run("""
            namespace A { [Tessera] public class Item { public int X; } }
            namespace B { [Tessera] public class Item { public int Y; } [Tessera] public class Holder { public A.Item? First; public B.Item? Second; } }
            """, new Dictionary<string, string> { ["TesseraCppNamespace"] = "game" });
        Assert.Empty(r.Errors);
        Assert.Equal("TESSERA008", Assert.Single(r.Diagnostics).Id);
        Assert.Contains("class Item final", r.Header);
        Assert.Contains("class Item_2 final", r.Header);
    }

    [Fact]
    public void ExplicitUnionsAreNamedAfterTheirMembers()
    {
        var r = Run("""
            namespace Game;
            [Tessera] public abstract class Item { public int Weight; }
            [Tessera] public sealed class Potion : Item { public int Heal; }
            [Tessera] public sealed class Key : Item { public uint Door; }
            [Tessera] public sealed class Coin : Item { public int Value; }
            [Tessera] public class Chest
            {
                public Item? Any;
                [TesseraUnion(typeof(Potion), typeof(Key))] public Item? Small;
                [TesseraUnion(typeof(Key), typeof(Potion))] public Item? SameSet;
                [TesseraUnion(typeof(Coin))] public List<Item>? Coins;
            }
            """);
        Assert.Empty(r.Diagnostics);
        AssertCompilesCleanly(r);
        string h = r.Header;
        Assert.Contains("class Item final : public tessera::UnionBase", h);
        Assert.Contains("class Item_Key_Potion final : public tessera::UnionBase", h);
        Assert.Contains("class Item_Coin final : public tessera::UnionBase", h);   // [TesseraUnion] on a list applies to its elements
        Assert.Contains("tessera::Vector<Item_Coin>", h);
        Assert.Equal(1, Count(h, "class Item_Key_Potion final"));
    }

    [Fact]
    public void MemberOrderDoesNotChangeTheLayout()
    {
        // Cells are sorted by alignment, size and name hash, so declaration order (and field or property) never reaches
        // the wire: the same schema entries and fingerprints.
        var a = Run("namespace Game; [Tessera] public class Unit { public string? Name; public int Hp; public bool Alive; public double X; public List<int>? Ids; [TesseraKeepDefault] public short Team; public Unit? Next; }");
        var b = Run("namespace Game; [Tessera] public class Unit { public Unit? Next; [TesseraKeepDefault] public short Team; public List<int>? Ids { get; set; } public double X; public bool Alive; public int Hp; public string? Name { get; set; } }");
        Assert.Empty(a.Diagnostics);
        Assert.Empty(b.Diagnostics);
        Assert.Equal(SchemaEntries(a), SchemaEntries(b));
        Assert.Equal(Fingerprints(a.Header), Fingerprints(b.Header));
        Assert.NotEmpty(Fingerprints(a.Header));
    }

    /// <summary>Every generated schema entry: name, fingerprints and encoded bytes.</summary>
    private static string[] SchemaEntries(GeneratorHarness.Result r) =>
        r.Writers.Split('\n').Where(l => l.Contains("new TesseraType(", StringComparison.Ordinal)).Select(l => l.Substring(l.IndexOf('=') + 1).Trim())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static string[] Fingerprints(string header) =>
        header.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("fingerprint = 0x", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void OutputIsIdenticalAcrossRuns()
    {
        const string source = """
            namespace Game;
            [Tessera] public abstract class Item { }
            [Tessera] public sealed class Potion : Item { public int Heal; }
            [Tessera] public sealed class Key : Item { public uint Door; }
            [Tessera] public class Chest { [TesseraUnion(typeof(Potion), typeof(Key))] public Item? Small; public List<Item>? All; public string? Name; }
            """;
        var a = Run(source);
        var b = Run(source);
        Assert.Equal(a.Header, b.Header);
        Assert.Equal(a.Sources.Select(s => s.SourceText.ToString()), b.Sources.Select(s => s.SourceText.ToString()));
    }

    [Fact]
    public void DictionariesBecomeMaps()
    {
        var r = Run("""
            namespace Game;
            public enum Kind : byte { A, B }
            [Tessera] public class Item { public int Id; }
            [Tessera] public class Inv
            {
                public Dictionary<string, Item>? Items;
                public SortedDictionary<Kind, int>? Counts;
                public IReadOnlyDictionary<long, List<string>>? Tags;
                public Dictionary<string, Item>? More;   // the same map type
            }
            """);
        Assert.Empty(r.Errors);
        string h = r.Header;
        Assert.Contains("::tessera::Map<std::string_view, ::game::Item>", h);
        Assert.Contains("::tessera::Map<::game::Kind, std::int32_t>", h);
        Assert.Contains("::tessera::Map<std::int64_t, tessera::Vector<std::string_view>>", h);
        Assert.Equal(3, r.Files.Count(f => f.Path.StartsWith("tessera_maps/", StringComparison.Ordinal)));
        Assert.Contains("static constexpr std::size_t keys = ", h);
        Assert.Contains("w.WriteMapKeys(keys, values, count)", r.Writers);   // string keys in code point order
        Assert.Contains("TesseraMapKeys.Sort(keys, values, count)", r.Writers);  // other keys ascending
        AssertCompilesCleanly(r);
    }

    [Fact]
    public void DictionaryKeysMustBeIntegersCharsEnumsOrStrings()
    {
        var r = Run("namespace Game; [Tessera] public class Bad { public Dictionary<double, int>? ByRatio; }");
        Assert.Contains("TESSERA001", r.Ids);
    }

    [Fact]
    public void KeepDefaultValuesBecomeFixedCells()
    {
        var r = Run("""
            namespace Game;
            public struct V2 { public float X, Y; }
            [Tessera] public class P
            {
                [TesseraKeepDefault] public long Id;
                [TesseraKeepDefault] public V2 Pos;
                [TesseraKeepDefault] public byte Tag;
                [TesseraKeepDefault] public int? Maybe;     // nullable: keeps its presence bit
                [TesseraKeepDefault] public string? Name;   // reference: keeps its presence bit
                [TesseraKeepDefault] public bool On;        // bool: stays in the header
                public double D;
            }
            """);
        Assert.Empty(r.Errors);
        string h = r.Header;
        Assert.Contains("static constexpr std::uint32_t fixed = 24;", h);   // Id 0, Pos 8, Tag 16, padded for D
        Assert.Contains("tessera::fixed_at(0)", h);
        Assert.Contains("tessera::fixed_at(8)", h);
        Assert.Contains("tessera::fixed_at(16)", h);
        Assert.Equal(3, Count(h, "tessera::fixed_at("));
        Assert.Contains("static constexpr std::uint16_t cells[] = {8, 4, 4, 0};", h);   // D, Maybe/Name
        AssertCompilesCleanly(r);
        Assert.Contains("Unsafe.InitBlockUnaligned(ref Unsafe.Add(ref o, 21), 0, 7);", r.Writers);   // padding zeroed
    }

    [Fact]
    public void EveryTypeGetsItsOwnHeader()
    {
        var r = Run("""
            namespace Game;
            public enum Kind : byte { A, B }
            public struct Vec2 { public float X, Y; }
            [Tessera] public abstract class Item { }
            [Tessera] public sealed class Potion : Item { public int Heal; }
            [Tessera] public class Thing { public Kind Kind; public Vec2 Pos; public Item? Loot; public List<Thing>? Children; }
            """);
        Assert.Empty(r.Errors);
        var paths = r.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Models.tessera.hpp", "game/Item.tessera.hpp", "game/Kind.tessera.hpp", "game/Potion.tessera.hpp", "game/Thing.tessera.hpp", "game/Vec2.tessera.hpp" }, paths);
        string umbrella = r.Files.Single(f => f.Path == "Models.tessera.hpp").Content;
        foreach (string p in paths.Where(p => p.Contains('/'))) Assert.Contains($"#include \"{p}\"", umbrella);
        string thing = r.Files.Single(f => f.Path == "game/Thing.tessera.hpp").Content;
        Assert.Contains("#include \"Kind.tessera.hpp\"", thing);        // complete types for the class itself
        Assert.Contains("class Item;", thing);                         // other views: forward-declared for the class...
        Assert.Contains("#include \"Item.tessera.hpp\"", thing);         // ...and included for the accessor definitions
        Assert.Matches("#ifndef TESSERA_DECL_game_Thing_[0-9A-F]{16}", thing);
        Assert.Matches("#ifndef TESSERA_DEFS_game_Thing_[0-9A-F]{16}", thing);
        Assert.Equal(1, Count(r.Header, "class Thing final"));
    }

    private const string SharedSource = Usings + """
        namespace Shared;
        public enum Rarity : byte { Common, Rare }
        [Tessera] public class Badge { public short Level = 5; public Rarity Rarity = Rarity.Rare; public string? Title; public float Scale = 1.5f; }
        [Tessera] public abstract class Reward { }
        [Tessera] public sealed class Gold : Reward { public int Amount = 10; }
        """;

    private const string UsesShared = "namespace Game; [Tessera] public class Profile { public Shared.Badge? Badge; public Shared.Reward? Reward; }";

    [Fact]
    public void TypesFromAnotherModelAssemblyGetIdenticalHeaders()
    {
        // The shared assembly's own headers, and the headers of an assembly that uses its types: the type headers
        // must be byte-identical (same guards), including defaults that only the shared assembly's source shows.
        var own = GeneratorHarness.Run(SharedSource);
        var lib = GeneratorHarness.CompileLibrary("Shared", SharedSource, generate: true);
        var user = Run(UsesShared, null, lib);
        Assert.Empty(user.Errors);
        foreach (string path in new[] { "shared/Badge.tessera.hpp", "shared/Rarity.tessera.hpp", "shared/Gold.tessera.hpp" })
        {
            Assert.Equal(own.Files.Single(f => f.Path == path).Content, user.Files.Single(f => f.Path == path).Content);
        }

        // A union gets a header where a member uses it: here only in the using assembly.
        Assert.DoesNotContain(own.Files, f => f.Path == "shared/Reward.tessera.hpp");
        Assert.Contains(user.Files, f => f.Path == "shared/Reward.tessera.hpp");

        string badge = user.Files.Single(f => f.Path == "shared/Badge.tessera.hpp").Content;
        Assert.Contains("short Level = 5", badge);
        Assert.Contains("Rarity Rarity = Rarity.Rare", badge);
        Assert.Contains("float Scale = 1.5", badge);
        Assert.Contains("as_gold", user.Header);                       // union member declared in the other assembly
        AssertCompilesCleanly(user);
    }

    [Fact]
    public void WritersUseDefaultsAndUnionMembersFromAnotherModelAssembly()
    {
        var lib = GeneratorHarness.CompileLibrary("Shared", SharedSource, generate: true);
        var r = Run(UsesShared, null, lib);
        // The writers compare against the initializer defaults, which only the other assembly's source shows.
        Assert.Contains("s_Level != (5)", r.Writers);
        Assert.Contains("(byte)s_Rarity != (1)", r.Writers);
        Assert.Contains($"SingleToInt32Bits(s_Scale) != {BitConverter.SingleToInt32Bits(1.5f)}", r.Writers);
        Assert.Contains("s_Amount != (10)", r.Writers);

        // Gold is a member of the Reward union although it is declared in the other assembly.
        var models = r.Load();
        var shared = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(models)!.LoadFromAssemblyName(new AssemblyName("Shared"));
        var profileType = models.GetType("Game.Profile")!;
        object profile = Activator.CreateInstance(profileType)!;
        profileType.GetField("Badge")!.SetValue(profile, Activator.CreateInstance(shared.GetType("Shared.Badge")!));
        profileType.GetField("Reward")!.SetValue(profile, Activator.CreateInstance(shared.GetType("Shared.Gold")!));
        var serialize = typeof(TesseraSerializer).GetMethods().Single(m => m.Name == "Serialize" && m.ReturnType == typeof(byte[])).MakeGenericMethod(profileType);
        Assert.NotEmpty((byte[])serialize.Invoke(null, new[] { profile, null })!);
    }

    [Fact]
    public void NoModelsMeansNoOutput()
    {
        var r = Run("public class Plain { public int X; }");
        Assert.Empty(r.Diagnostics);
        Assert.Empty(r.Sources);
    }

    private static int Count(string text, string fragment)
    {
        int n = 0;
        for (int i = text.IndexOf(fragment, StringComparison.Ordinal); i >= 0; i = text.IndexOf(fragment, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
