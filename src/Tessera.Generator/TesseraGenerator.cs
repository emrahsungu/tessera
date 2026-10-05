using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tessera.Generator
{
    /// <summary>
    /// Finds the models of a project, computes their wire layout and emits (1) allocation-free C# writers and (2) a C++
    /// header with zero-copy view classes, embedded in the assembly as <c>TesseraCppHeaderAttribute</c>. Roots are the
    /// types marked [Tessera], the types listed in [assembly: TesseraRoot(...)], and the T of every
    /// <c>TesseraSerializer.Serialize&lt;T&gt;</c> / <c>Write&lt;T&gt;</c> call with a known T; everything they reference is included.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class TesseraGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var annotated = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Tessera.TesseraAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

            var calls = context.SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => IsSerializerCall(node),
                    static (ctx, ct) => CallSite.From(ctx, ct))
                .Where(static c => c != null)
                .Select(static (c, _) => c!);

            var options = context.AnalyzerConfigOptionsProvider.Select(static (p, _) => Settings.From(p.GlobalOptions));
            var input = annotated.Collect().Combine(calls.Collect()).Combine(context.CompilationProvider).Combine(options);
            context.RegisterSourceOutput(input, static (spc, data) => Execute(spc, data.Left.Left.Left, data.Left.Left.Right, data.Left.Right, data.Right));
        }

        /// <summary>Syntactic filter: an invocation of a method named Serialize or Write (checked semantically later).</summary>
        private static bool IsSerializerCall(SyntaxNode node)
        {
            if (node is not InvocationExpressionSyntax invocation) return false;
            string? name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                SimpleNameSyntax simple => simple.Identifier.ValueText,   // using static Tessera.TesseraSerializer
                _ => null,
            };
            return name == "Serialize" || name == "Write";
        }

        /// <summary>A TesseraSerializer.Serialize/Write call and the type it serializes (possibly a type parameter).</summary>
        private sealed class CallSite
        {
            public ITypeSymbol Type = null!;
            public Location Location = Location.None;

            public static CallSite? From(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
            {
                var invocation = (InvocationExpressionSyntax)ctx.Node;
                if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method || !method.IsGenericMethod) return null;
                if (method.ContainingType?.ToDisplayString() != "Tessera.TesseraSerializer") return null;
                var type = method.TypeArguments[0];
                return type.TypeKind == TypeKind.Error ? null : new CallSite { Type = type, Location = invocation.GetLocation() };
            }
        }

        private static void Execute(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> annotated, ImmutableArray<CallSite> calls,
            Compilation compilation, Settings settings)
        {
            // Explicit roots: [Tessera] types and [assembly: TesseraRoot(...)] types. Implicit roots: the serialized types.
            var explicitRoots = annotated.Concat(ModelBuilder.ListedRoots(compilation.Assembly))
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
            var roots = new List<INamedTypeSymbol>(explicitRoots);
            var firstCall = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
            foreach (var call in calls)
            {
                if (call.Type is ITypeParameterSymbol parameter)
                {
                    // Generic code hides its model types from the generator: say so where it happens.
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.GenericCall, call.Location, parameter.Name));
                }
                else if (ModelBuilder.WhyNotARoot(call.Type) is string why)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NotARoot, call.Location, call.Type.ToDisplayString(), why));
                }
                else if (call.Type is INamedTypeSymbol model && !firstCall.ContainsKey(model))
                {
                    firstCall[model] = call.Location;
                    roots.Add(model);
                }
            }

            roots = roots.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal).ToList();
            if (roots.Count == 0) return;

            var builder = new ModelBuilder(compilation, settings.Naming, explicitRoots, settings.RequireAttribute);
            foreach (var root in roots) builder.AddRoot(root, firstCall.TryGetValue(root, out var location) ? location : null);
            builder.Complete();

            foreach (var d in builder.Diagnostics) context.ReportDiagnostic(d);
            if (builder.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return;

            string asm = compilation.AssemblyName ?? "Models";
            LayoutBuilder.Build(builder.Types);
            foreach (var d in AssignTypeNames(builder.Types, builder.Enums, settings, asm)) context.ReportDiagnostic(d);

            string ns = "Tessera.Generated." + Naming.CsIdentifier(asm);
            var csharp = new CSharpEmitter(builder.Types, ns).Emit();
            context.AddSource("Tessera.Writers.g.cs", csharp);

            string headerName = settings.HeaderName ?? asm + ".tessera.hpp";
            var headers = new System.Text.StringBuilder("// <auto-generated/>\n");
            foreach (var file in new CppEmitter(builder.Types, builder.Enums, settings, asm).EmitFiles(headerName))
            {
                headers.Append("[assembly: global::Tessera.TesseraCppHeaderAttribute(").Append(Naming.CsString(file.Key)).Append(", ")
                    .Append(Naming.CsString(file.Value)).Append(")]\n");
            }

            context.AddSource("Tessera.CppHeader.g.cs", headers.ToString());

            // Initializer defaults of members declared here, for generators of other model assemblies that use these types,
            // and the types whose members they cover (so those generators know that the recorded defaults are complete).
            var defaults = new System.Text.StringBuilder();
            var covered = builder.Types.OfType<ObjectModel>().Where(o => o.Map == null).SelectMany(o => o.Members).Select(m => m.Symbol.ContainingType)
                .Where(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly))
                .Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Distinct().OrderBy(t => t, StringComparer.Ordinal);
            foreach (var type in covered) defaults.Append("[assembly: global::Tessera.TesseraDeclaredAttribute(typeof(").Append(type).Append("))]\n");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in builder.Types.OfType<ObjectModel>().Where(o => o.Map == null).SelectMany(o => o.Members))
            {
                var declaring = m.Symbol.ContainingType;
                if (!m.HasCustomDefault || m.DefaultValue == null || !SymbolEqualityComparer.Default.Equals(declaring.ContainingAssembly, compilation.Assembly)) continue;
                string type = declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                string? literal = CsConstant(m);
                if (literal == null || !seen.Add(type + "." + m.CsName)) continue;
                defaults.Append("[assembly: global::Tessera.TesseraDefaultAttribute(typeof(").Append(type).Append("), ").Append(Naming.CsString(m.CsName))
                    .Append(", ").Append(literal).Append(")]\n");
            }

            if (defaults.Length > 0) context.AddSource("Tessera.Defaults.g.cs", "// <auto-generated/>\n" + defaults);
        }

        /// <summary>A C# constant of the member's exact type (attribute arguments keep their type).</summary>
        private static string? CsConstant(MemberModel m)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            object d = m.DefaultValue!;
            var v = m.Value;
            if (v.Enum != null) return "(" + v.Enum.CsName + ")(" + Convert.ToString(d, ic) + ")";
            switch (v.Kind)
            {
                case WireKind.Bool: return d is bool b && b ? "true" : "false";
                case WireKind.Int8: return "(sbyte)(" + Convert.ToSByte(d, ic).ToString(ic) + ")";
                case WireKind.UInt8: return "(byte)" + Convert.ToByte(d, ic).ToString(ic);
                case WireKind.Int16: return "(short)(" + Convert.ToInt16(d, ic).ToString(ic) + ")";
                case WireKind.UInt16: return "(ushort)" + Convert.ToUInt16(d, ic).ToString(ic);
                case WireKind.Char16: return "(char)" + (d is char c ? (int)c : Convert.ToInt32(d, ic)).ToString(ic);
                case WireKind.Int32: return "(int)(" + Convert.ToInt32(d, ic).ToString(ic) + ")";
                case WireKind.UInt32: return "(uint)" + Convert.ToUInt32(d, ic).ToString(ic) + "u";
                case WireKind.Int64: return "(long)(" + Convert.ToInt64(d, ic).ToString(ic) + "L)";
                case WireKind.UInt64: return "(ulong)" + Convert.ToUInt64(d, ic).ToString(ic) + "UL";
                case WireKind.Float32:
                {
                    float f = Convert.ToSingle(d, ic);
                    if (float.IsNaN(f)) return "float.NaN";
                    if (float.IsInfinity(f)) return f > 0 ? "float.PositiveInfinity" : "float.NegativeInfinity";
                    return "(float)(" + f.ToString("R", ic) + "F)";
                }
                case WireKind.Float64:
                {
                    double f = Convert.ToDouble(d, ic);
                    if (double.IsNaN(f)) return "double.NaN";
                    if (double.IsInfinity(f)) return f > 0 ? "double.PositiveInfinity" : "double.NegativeInfinity";
                    return "(double)(" + f.ToString("R", ic) + "D)";
                }
                default: return null;
            }
        }

        /// <summary>
        /// Makes C++ type names unique per C++ namespace (two C# namespaces can map to one, e.g. with TesseraCppNamespace).
        /// Types later in a deterministic order get a numeric suffix.
        /// </summary>
        private static List<Diagnostic> AssignTypeNames(List<TypeModel> types, List<EnumModel> enums, Settings settings, string assembly)
        {
            var named = new List<(ISymbol Symbol, string Display, Func<string> Get, Action<string> Set)>();
            foreach (var e in enums.OrderBy(x => x.CsName, StringComparer.Ordinal)) named.Add((e.Symbol, e.DisplayName, () => e.CppName, v => e.CppName = v));
            foreach (var t in types)
            {
                switch (t)
                {
                    case StructModel s: named.Add((s.Symbol, s.DisplayName, () => s.CppName, v => s.CppName = v)); break;
                    case ObjectModel o when o.Map == null: named.Add((o.Symbol, o.DisplayName, () => o.CppName, v => o.CppName = v)); break;
                    case UnionModel u: named.Add((u.Symbol, u.DisplayName, () => u.CppName, v => u.CppName = v)); break;
                }
            }

            var diagnostics = new List<Diagnostic>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in named)
            {
                string ns = CppEmitter.NamespaceOf(n.Symbol, settings, assembly) + "::";
                string name = n.Get(), candidate = name;
                for (int i = 2; !used.Add(ns + candidate); i++) candidate = name + "_" + i;
                if (candidate == name) continue;
                n.Set(candidate);
                var location = n.Symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
                diagnostics.Add(Diagnostic.Create(Diagnostics.CppTypeNameAdjusted, location, n.Display, candidate));
            }

            return diagnostics;
        }
    }

    internal sealed class Settings : IEquatable<Settings>
    {
        public CppNaming Naming = CppNaming.SnakeCase;
        public string? Namespace;
        public string? HeaderName;
        public bool RequireAttribute;   // TesseraRequireAttribute: every model needs [Tessera] or [assembly: TesseraRoot]

        public static Settings From(AnalyzerConfigOptions o)
        {
            var s = new Settings();
            if (o.TryGetValue("build_property.TesseraCppNaming", out var naming) && !string.IsNullOrWhiteSpace(naming))
            {
                switch (naming.Trim().ToLowerInvariant())
                {
                    case "camel": case "camelcase": s.Naming = CppNaming.CamelCase; break;
                    case "pascal": case "pascalcase": s.Naming = CppNaming.PascalCase; break;
                    case "original": case "none": s.Naming = CppNaming.Original; break;
                    default: s.Naming = CppNaming.SnakeCase; break;
                }
            }

            if (o.TryGetValue("build_property.TesseraCppNamespace", out var ns) && !string.IsNullOrWhiteSpace(ns)) s.Namespace = ns.Trim();
            if (o.TryGetValue("build_property.TesseraCppHeaderName", out var hn) && !string.IsNullOrWhiteSpace(hn)) s.HeaderName = hn.Trim();
            if (o.TryGetValue("build_property.TesseraRequireAttribute", out var ra)) s.RequireAttribute = string.Equals(ra?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            return s;
        }

        public bool Equals(Settings? other) => other != null && other.Naming == Naming && other.Namespace == Namespace && other.HeaderName == HeaderName &&
                                               other.RequireAttribute == RequireAttribute;

        public override bool Equals(object? obj) => Equals(obj as Settings);

        public override int GetHashCode() => (int)Naming ^ (Namespace?.GetHashCode() ?? 0) ^ (HeaderName?.GetHashCode() ?? 0) ^ (RequireAttribute ? 0x5A5A : 0);
    }
}
