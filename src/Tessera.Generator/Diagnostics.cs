using Microsoft.CodeAnalysis;

namespace Tessera.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "Tessera";

        public static readonly DiagnosticDescriptor UnsupportedType = new DiagnosticDescriptor(
            "TESSERA001", "Unsupported member type",
            "Member '{0}' of '{1}' has type '{2}', which Tessera cannot serialize: {3}",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor NameCollision = new DiagnosticDescriptor(
            "TESSERA002", "Duplicate wire name",
            "Members '{0}' and '{1}' of '{2}' have the same wire name or name hash; give one of them a different [TesseraName]",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor NoUnionMembers = new DiagnosticDescriptor(
            "TESSERA003", "Polymorphic type without known members",
            "'{0}' is abstract or an interface, but no public or internal concrete type of this project derives from it, and no referenced project marks one with [Tessera] or lists one in [assembly: TesseraRoot]; mark the derived types in their project, or list them with [TesseraUnion(...)]",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor UnionTagCollision = new DiagnosticDescriptor(
            "TESSERA004", "Union tag collision",
            "Types '{0}' and '{1}' in union '{2}' have the same type id; give one of them a different [Tessera(\"name\")]",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor TooManyMembers = new DiagnosticDescriptor(
            "TESSERA005", "Too many members",
            "'{0}' has {1} serialized members; the limit is 4096",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor StructNotSupported = new DiagnosticDescriptor(
            "TESSERA006", "Unsupported struct layout",
            "Struct '{0}' cannot be stored inline: {1}",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor CppNameAdjusted = new DiagnosticDescriptor(
            "TESSERA007", "C++ name adjusted",
            "C++ accessor for member '{0}' of '{1}' was renamed to '{2}' to avoid a clash",
            Category, DiagnosticSeverity.Info, true);

        public static readonly DiagnosticDescriptor ZeroUnionTag = new DiagnosticDescriptor(
            "TESSERA009", "Union tag is zero",
            "Type '{0}' hashes to union tag 0, which means 'no value'; give it a different [Tessera(\"name\")]",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor GenericCall = new DiagnosticDescriptor(
            "TESSERA010", "Model type not visible at this call",
            "This call serializes the type parameter '{0}', so the generator cannot tell which models it writes; types used only through it get no writer unless they have [Tessera], are listed in [assembly: TesseraRoot(typeof(...))] or are serialized elsewhere with their concrete type",
            Category, DiagnosticSeverity.Info, true);

        public static readonly DiagnosticDescriptor DefaultsNotVisible = new DiagnosticDescriptor(
            "TESSERA011", "Member initializers of another project are not visible",
            "Model types from '{0}' that are not models there ({1}): initializers of their members (such as '= 100') are not visible here, so this project takes those members' defaults as zero; in '{0}', mark these types [Tessera] or list them in [assembly: TesseraRoot], or give the members [DefaultValue]",
            Category, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor ModelNotMarked = new DiagnosticDescriptor(
            "TESSERA012", "Model is not marked",
            "'{0}' is a model but has no [Tessera] and is not listed in [assembly: TesseraRoot]; TesseraRequireAttribute requires every model class, record and struct to be marked",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor NotAccessible = new DiagnosticDescriptor(
            "TESSERA013", "Model is not accessible",
            "'{0}' cannot be a model because the generated writers cannot see it; it and the types that contain it must be public or internal (not private, protected or file-local)",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor NotARoot = new DiagnosticDescriptor(
            "TESSERA014", "Type cannot be the root of a buffer",
            "This call always throws: '{0}' cannot be the root of a buffer, because {1}",
            Category, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor CppTypeNameAdjusted = new DiagnosticDescriptor(
            "TESSERA008", "C++ type name adjusted",
            "C++ type for '{0}' was renamed to '{1}' because another type in the same C++ namespace has that name",
            Category, DiagnosticSeverity.Info, true);
    }
}
