using System.Collections.Generic;
using System.Text;

namespace Tessera.Generator
{
    internal enum CppNaming
    {
        SnakeCase,
        CamelCase,
        PascalCase,
        Original,
    }

    internal static class Naming
    {
        private static readonly HashSet<string> CppKeywords = new HashSet<string>
        {
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch",
            "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept", "const", "consteval", "constexpr",
            "constinit", "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype", "default", "delete",
            "do", "double", "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for",
            "friend", "goto", "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
            "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register", "reinterpret_cast",
            "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct",
            "switch", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename",
            "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
            "final", "override", "import", "module",
            // names reserved by the tessera::Table base class and common macros
            "tessera", "std", "NULL", "assert", "errno", "min", "max",
        };

        /// <summary>Converts a C# member name to the configured C++ style.</summary>
        public static string Member(string name, CppNaming style)
        {
            string trimmed = name.Trim('_');
            if (trimmed.Length == 0) trimmed = "value";
            string result;
            switch (style)
            {
                case CppNaming.SnakeCase: result = Snake(trimmed); break;
                case CppNaming.CamelCase: result = char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1); break;
                case CppNaming.PascalCase: result = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1); break;
                default: result = name; break;
            }

            return Sanitize(result);
        }

        /// <summary>Makes a valid, non-keyword C++ identifier.</summary>
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 128 && (char.IsLetterOrDigit(c) || c == '_')) sb.Append(c);
                else if (c >= 128) sb.Append("_u").Append(((int)c).ToString("X4"));
                else sb.Append('_');
            }

            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            string s = sb.ToString();
            // Identifiers with a double underscore or a leading underscore + capital are reserved in C++.
            while (s.Contains("__")) s = s.Replace("__", "_");
            if (s.Length > 1 && s[0] == '_' && char.IsUpper(s[1])) s = "v" + s;
            if (CppKeywords.Contains(s)) s += "_";
            return s;
        }

        /// <summary>PascalCase/camelCase to snake_case, keeping acronyms together ("MaxHP" -> "max_hp").</summary>
        public static string Snake(string name)
        {
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    bool prevLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                    bool acronymEnd = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if ((prevLowerOrDigit || acronymEnd) && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>Identifier for a C# type name inside generated C# code.</summary>
        public static string CsIdentifier(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>C# string literal.</summary>
        public static string CsString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
