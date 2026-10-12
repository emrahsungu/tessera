using Microsoft.CodeAnalysis;

namespace Tessera.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "Tessera";

        /// <summary>Each diagnostic's page in docs/diagnostics, which IDEs open from the error list.</summary>
        private static string Help(string id) => "https://github.com/emrahsungu/tessera/blob/main/docs/diagnostics/" + id + ".md";

        public static readonly DiagnosticDescriptor UnsupportedType = new DiagnosticDescriptor(
            "TESSERA001", "A field's type can't be saved",
            "Field '{0}' of '{1}' has type '{2}', which Tessera cannot save: {3}",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA001"));

        public static readonly DiagnosticDescriptor NameCollision = new DiagnosticDescriptor(
            "TESSERA002", "Two fields would be saved under the same name",
            "Fields '{0}' and '{1}' of '{2}' would be saved under the same name (or names with the same hash); give one of them a different [TesseraName]",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA002"));

        public static readonly DiagnosticDescriptor NoUnionMembers = new DiagnosticDescriptor(
            "TESSERA003", "No class can be saved in a field of this type",
            "Fields of type '{0}' can hold only classes derived from it, and none can be saved: no public or internal class of this project derives from it, and no referenced project marks one with [Tessera] or lists one in [assembly: TesseraRoot]; mark the derived classes in their project, or list them with [TesseraUnion(...)]",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA003"));

        public static readonly DiagnosticDescriptor UnionTagCollision = new DiagnosticDescriptor(
            "TESSERA004", "Two derived classes would get the same type ID",
            "Classes '{0}' and '{1}', both saved in fields of type '{2}', would get the same type ID (a hash of the class name); give one of them a different [Tessera(\"name\")]",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA004"));

        public static readonly DiagnosticDescriptor TooManyMembers = new DiagnosticDescriptor(
            "TESSERA005", "Too many fields in one class",
            "'{0}' has {1} saved fields and properties; the limit is 4096",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA005"));

        public static readonly DiagnosticDescriptor StructNotSupported = new DiagnosticDescriptor(
            "TESSERA006", "A struct's memory layout can't be saved as it is",
            "Struct '{0}' cannot be saved byte for byte: {1}",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA006"));

        public static readonly DiagnosticDescriptor CppNameAdjusted = new DiagnosticDescriptor(
            "TESSERA007", "A C++ method was renamed to avoid a clash",
            "The C++ method for field '{0}' of '{1}' was renamed to '{2}' to avoid a clash",
            Category, DiagnosticSeverity.Info, true, helpLinkUri: Help("TESSERA007"));

        public static readonly DiagnosticDescriptor ZeroUnionTag = new DiagnosticDescriptor(
            "TESSERA009", "A class name hashes to the reserved type ID 0",
            "The name of class '{0}' hashes to type ID 0, which is reserved for null; give it a different [Tessera(\"name\")]",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA009"));

        public static readonly DiagnosticDescriptor GenericCall = new DiagnosticDescriptor(
            "TESSERA010", "A generic method hides which types it saves",
            "This call saves the type parameter '{0}', so the generator cannot tell which classes it saves; a class saved only through it gets no serializer unless it has [Tessera], is listed in [assembly: TesseraRoot(typeof(...))], or is saved elsewhere with its real type",
            Category, DiagnosticSeverity.Info, true, helpLinkUri: Help("TESSERA010"));

        public static readonly DiagnosticDescriptor DefaultsNotVisible = new DiagnosticDescriptor(
            "TESSERA011", "Another project's initial field values aren't visible",
            "Classes from '{0}' that '{0}' generates no code for ({1}): the initial values of their fields (such as '= 100') are not visible here, so this project takes those fields' defaults as zero; in '{0}', mark these classes [Tessera] or list them in [assembly: TesseraRoot], or give the fields [DefaultValue]",
            Category, DiagnosticSeverity.Warning, true, helpLinkUri: Help("TESSERA011"));

        public static readonly DiagnosticDescriptor ModelNotMarked = new DiagnosticDescriptor(
            "TESSERA012", "Strict mode: a saved class isn't marked",
            "'{0}' is saved but has no [Tessera] and is not listed in [assembly: TesseraRoot]; TesseraRequireAttribute requires every saved class, record and struct to be marked",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA012"));

        public static readonly DiagnosticDescriptor NotAccessible = new DiagnosticDescriptor(
            "TESSERA013", "A private or protected class can't be saved",
            "'{0}' cannot be saved because the generated serializers cannot access it; it and the classes it is nested in must be public or internal (not private, protected or file-local)",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA013"));

        public static readonly DiagnosticDescriptor NotARoot = new DiagnosticDescriptor(
            "TESSERA014", "This Serialize call could never succeed",
            "This call always throws: '{0}' cannot be the top-level object of a buffer, because {1}",
            Category, DiagnosticSeverity.Error, true, helpLinkUri: Help("TESSERA014"));

        public static readonly DiagnosticDescriptor CppTypeNameAdjusted = new DiagnosticDescriptor(
            "TESSERA008", "A C++ class was renamed to avoid a clash",
            "The C++ class for '{0}' was renamed to '{1}' because another class in the same C++ namespace has that name",
            Category, DiagnosticSeverity.Info, true, helpLinkUri: Help("TESSERA008"));
    }
}
