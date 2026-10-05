using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tessera.Generator
{
    /// <summary>Turns the root types and everything they reference into generator models.</summary>
    internal sealed class ModelBuilder
    {
        private const string TesseraAttribute = "Tessera.TesseraAttribute";

        private readonly Compilation _compilation;
        private readonly CppNaming _naming;
        private readonly HashSet<INamedTypeSymbol> _explicit;   // [Tessera] or [assembly: TesseraRoot] in this compilation
        private readonly bool _requireAttribute;
        private readonly Dictionary<string, TypeModel> _byKey = new Dictionary<string, TypeModel>();
        private readonly Dictionary<string, EnumModel> _enums = new Dictionary<string, EnumModel>();
        private readonly HashSet<string> _inProgressStructs = new HashSet<string>();
        private readonly Dictionary<IAssemblySymbol, AssemblyRecords> _records = new Dictionary<IAssemblySymbol, AssemblyRecords>(SymbolEqualityComparer.Default);
        private readonly Dictionary<IAssemblySymbol, SortedSet<string>> _hiddenDefaults = new Dictionary<IAssemblySymbol, SortedSet<string>>(SymbolEqualityComparer.Default);

        public ModelBuilder(Compilation compilation, CppNaming naming, List<INamedTypeSymbol> explicitRoots, bool requireAttribute = false)
        {
            _compilation = compilation;
            _naming = naming;
            _explicit = new HashSet<INamedTypeSymbol>(explicitRoots, SymbolEqualityComparer.Default);
            _requireAttribute = requireAttribute;
        }

        public List<TypeModel> Types { get; } = new List<TypeModel>();
        public List<EnumModel> Enums { get; } = new List<EnumModel>();
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

        /// <summary>The types an assembly lists in [assembly: TesseraRoot(typeof(...), ...)].</summary>
        public static IEnumerable<INamedTypeSymbol> ListedRoots(IAssemblySymbol assembly)
        {
            foreach (var a in assembly.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() != "Tessera.TesseraRootAttribute") continue;
                foreach (var arg in a.ConstructorArguments)
                {
                    IEnumerable<TypedConstant> values = arg.Kind == TypedConstantKind.Array ? arg.Values : new[] { arg };
                    foreach (var v in values)
                    {
                        if (v.Value is INamedTypeSymbol t && !t.IsUnboundGenericType) yield return t;
                    }
                }
            }
        }

        /// <summary>
        /// Why a <c>TesseraSerializer.Serialize&lt;T&gt;</c> call with this T would always throw (no writer can exist for
        /// it), or null if T can be the root of a buffer.
        /// </summary>
        public static string? WhyNotARoot(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.SpecialType == SpecialType.None &&
                named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface &&
                named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            {
                if (named.IsAbstract || named.TypeKind == TypeKind.Interface)
                {
                    return "it is abstract or an interface, but the root of a buffer has one concrete type; serialize the concrete type, or a model with a member of this type";
                }

                bool inline = named.TypeKind == TypeKind.Struct && named.IsUnmanagedType;
                if (!inline && !IsDictionary(named) && CollectionElement(named, out _) == null) return null;
            }
            else if (type.SpecialType == SpecialType.System_Object || type.TypeKind is TypeKind.Delegate or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic)
            {
                return "it is not a model type; serialize a class, record or struct";
            }

            return "values of this type are stored inside models, not as the root of a buffer; serialize a model with a member of this type";
        }

        /// <param name="symbol">The root type.</param>
        /// <param name="call">Where the project serializes it, for roots found from calls (diagnostics point there).</param>
        public void AddRoot(INamedTypeSymbol symbol, Location? call = null)
        {
            if (symbol.IsAbstract || symbol.TypeKind == TypeKind.Interface)
            {
                // Abstract roots only matter as union bases: their members are found where a model uses them.
                return;
            }

            if (!IsAccessible(symbol))
            {
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.NotAccessible,
                    call ?? symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None, symbol.ToDisplayString()));
                return;
            }

            if (symbol.TypeKind == TypeKind.Struct && symbol.IsUnmanagedType)
            {
                GetStruct(symbol, symbol.Locations.FirstOrDefault());
                return;
            }

            GetObject(symbol);
        }

        /// <summary>
        /// Called after the last root. Puts the types in key order, so the generated code depends neither on which types
        /// are roots nor on the order in which they were found, and reports the types whose initializers were not visible.
        /// </summary>
        public void Complete()
        {
            Types.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            for (int i = 0; i < Types.Count; i++) Types[i].Index = i;

            foreach (var pair in _hiddenDefaults.OrderBy(p => p.Key.Name, StringComparer.Ordinal))
            {
                var names = pair.Value.ToList();
                string list = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + " and " + (names.Count - 3) + " more";
                Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.DefaultsNotVisible, Location.None, pair.Key.Name, list));
            }
        }

        // ------------------------------------------------------------------ objects

        private ObjectModel GetObject(INamedTypeSymbol symbol)
        {
            string key = "obj:" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (_byKey.TryGetValue(key, out var existing)) return (ObjectModel)existing;

            var model = new ObjectModel
            {
                Key = key,
                Symbol = symbol,
                CsName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                WireName = StableName(symbol),
                DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                IsValueType = symbol.IsValueType,
            };
            model.CppName = CppTypeName(symbol);
            model.TypeId = XxHash.Hash32(model.WireName);
            Register(model);
            if (_requireAttribute && !IsMarked(symbol)) Report(Generator.Diagnostics.ModelNotMarked, symbol, symbol.ToDisplayString());

            int order = 0;
            var seen = new HashSet<string>();
            for (INamedTypeSymbol? t = symbol; t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
            {
                foreach (ISymbol member in t.GetMembers())
                {
                    if (!seen.Add(member.Name) && (member is IFieldSymbol || member is IPropertySymbol)) continue;
                    var m = TryCreateMember(model, t, member);
                    if (m == null) continue;
                    m.DeclarationOrder = order++;
                    model.Members.Add(m);
                }
            }

            // Base-class members first, then derived, each in declaration order.
            model.Members.Sort((a, b) =>
            {
                int da = Depth(a.Symbol.ContainingType), db = Depth(b.Symbol.ContainingType);
                return da != db ? da.CompareTo(db) : a.DeclarationOrder.CompareTo(b.DeclarationOrder);
            });

            if (model.Members.Count > 4096)
            {
                Report(Generator.Diagnostics.TooManyMembers, symbol, symbol.Name, model.Members.Count);
            }

            CheckNameCollisions(model);
            AssignCppNames(model);
            return model;
        }

        private MemberModel? TryCreateMember(ObjectModel owner, INamedTypeSymbol declaringType, ISymbol member)
        {
            if (member.IsStatic || member.IsImplicitlyDeclared) return null;
            ITypeSymbol type;
            bool isProperty;
            switch (member)
            {
                case IFieldSymbol field:
                    if (field.IsConst || field.AssociatedSymbol != null || !IsAccessible(field)) return null;
                    type = field.Type;
                    isProperty = false;
                    break;
                case IPropertySymbol property:
                    if (property.IsIndexer || property.GetMethod == null || !IsAccessible(property.GetMethod)) return null;
                    if (property.SetMethod == null && !IsAutoProperty(property)) return null;
                    type = property.Type;
                    isProperty = true;
                    break;
                default:
                    return null;
            }

            if (HasAttribute(member, "Tessera.TesseraIgnoreAttribute") ||
                HasAttribute(member, "System.Runtime.Serialization.IgnoreDataMemberAttribute") ||
                HasAttribute(member, "System.NonSerializedAttribute"))
            {
                return null;
            }

            string wireName = member.Name;
            var nameAttr = FindAttribute(member, "Tessera.TesseraNameAttribute");
            if (nameAttr != null && nameAttr.ConstructorArguments.Length == 1 && nameAttr.ConstructorArguments[0].Value is string s && s.Length > 0)
            {
                wireName = s;
            }

            bool forceShared = HasAttribute(member, "Tessera.TesseraSharedAttribute");
            var unionTypes = UnionTypesFromAttributes(member);
            var value = Classify(type, member, owner.DisplayName, forceShared, unionTypes);
            if (value == null) return null;

            var model = new MemberModel
            {
                CsName = member.Name,
                WireName = wireName,
                NameHash = XxHash.Hash64(wireName),
                IsProperty = isProperty,
                Symbol = member,
                Value = value,
                KeepDefault = HasAttribute(member, "Tessera.TesseraKeepDefaultAttribute"),
                CsTypeDisplay = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            };
            ResolveDefault(model, member);
            return model;
        }

        private bool IsAccessible(ISymbol symbol)
        {
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return true;
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    return SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly);
                default:
                    return false;
            }
        }

        private static bool IsAutoProperty(IPropertySymbol property) =>
            property.ContainingType.GetMembers().OfType<IFieldSymbol>()
                .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));

        private static int Depth(INamedTypeSymbol? t)
        {
            int d = 0;
            for (; t != null; t = t.BaseType) d++;
            return d;
        }

        private void CheckNameCollisions(ObjectModel model)
        {
            var byHash = new Dictionary<ulong, MemberModel>();
            foreach (var m in model.Members)
            {
                if (byHash.TryGetValue(m.NameHash, out var other))
                {
                    Report(Generator.Diagnostics.NameCollision, m.Symbol, other.CsName, m.CsName, model.DisplayName);
                    continue;
                }

                byHash[m.NameHash] = m;
            }
        }

        private void AssignCppNames(ObjectModel model)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in model.Members)
            {
                string name = Naming.Member(m.CsName, _naming);
                string candidate = name;
                for (int i = 2; used.Contains(candidate) || used.Contains("has_" + candidate) || candidate.StartsWith("has_", StringComparison.Ordinal) && used.Contains(candidate.Substring(4)); i++)
                {
                    candidate = name + "_" + i;
                }

                if (candidate != name) Report(Generator.Diagnostics.CppNameAdjusted, m.Symbol, m.CsName, model.DisplayName, candidate);
                m.CppName = candidate;
                used.Add(candidate);
            }
        }

        // ------------------------------------------------------------------ classification

        private ValueModel? Classify(ITypeSymbol type, ISymbol member, string ownerName, bool forceShared, List<INamedTypeSymbol>? unionTypes, bool isElement = false)
        {
            if (type.IsReferenceType) type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var value = new ValueModel { Type = type, IsReferenceType = type.IsReferenceType };
            if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                value.IsNullableValueType = true;
                type = nullable.TypeArguments[0];
                value.Type = type;
            }

            value.CsType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            WireKind primitive = Primitive(type.SpecialType);
            if (primitive != WireKind.Invalid)
            {
                value.Kind = primitive;
                return value;
            }

            if (NonPortable(type) is string why)
            {
                Unsupported(member, ownerName, type, why);
                return null;
            }

            if (type.TypeKind == TypeKind.Delegate)
            {
                Unsupported(member, ownerName, type, "delegates cannot be serialized");
                return null;
            }

            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
            {
                var e = GetEnum(enumType);
                if (e == null)
                {
                    Unsupported(member, ownerName, type, "enum has an unsupported underlying type");
                    return null;
                }

                value.Kind = e.Underlying;
                value.Enum = e;
                return value;
            }

            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1)
                {
                    Unsupported(member, ownerName, type, "only single-dimensional arrays are supported");
                    return null;
                }

                return MakeVector(value, array.ElementType, CollectionShape.Array, member, ownerName, unionTypes);
            }

            if (type is INamedTypeSymbol named)
            {
                if (IsDictionary(named)) return MakeMap(value, named, member, ownerName, unionTypes);

                var collection = CollectionElement(named, out var shape);
                if (collection != null) return MakeVector(value, collection, shape, member, ownerName, unionTypes);

                if (named.TypeKind == TypeKind.Struct)
                {
                    if (named.IsUnmanagedType)
                    {
                        var st = GetStruct(named, member.Locations.FirstOrDefault());
                        if (st == null) return null;
                        value.Struct = st;
                        value.Kind = forceShared || st.SharedByDefault ? WireKind.SharedStruct : WireKind.Struct;
                        return value;
                    }

                    value.Kind = WireKind.Object;
                    value.Object = GetObject(named);
                    return value;
                }

                if (named.TypeKind == TypeKind.Class || named.TypeKind == TypeKind.Interface)
                {
                    if (named.SpecialType == SpecialType.System_Object || IsDelegate(named) || named.IsGenericType && named.IsUnboundGenericType)
                    {
                        Unsupported(member, ownerName, type, "use a concrete model type");
                        return null;
                    }

                    bool polymorphic = named.IsAbstract || named.TypeKind == TypeKind.Interface ||
                                       HasAttribute(named, "Tessera.TesseraUnionAttribute") || unionTypes != null;
                    if (polymorphic)
                    {
                        var union = GetUnion(named, unionTypes, member);
                        if (union == null) return null;
                        value.Kind = WireKind.Union;
                        value.Union = union;
                        return value;
                    }

                    value.Kind = WireKind.Object;
                    value.Object = GetObject(named);
                    return value;
                }
            }

            Unsupported(member, ownerName, type, "this kind of type is not supported");
            return null;
        }

        private ValueModel? MakeVector(ValueModel value, ITypeSymbol elementType, CollectionShape shape, ISymbol member, string ownerName, List<INamedTypeSymbol>? unionTypes)
        {
            // A member-level [TesseraUnion] applies to the elements of a collection member.
            var element = Classify(elementType, member, ownerName, false, unionTypes, isElement: true);
            if (element == null) return null;
            if (element.IsNullableValueType && !WireFormat.IsOptionalValueKind(element.Kind))
            {
                Unsupported(member, ownerName, elementType, "nullable elements must be scalars, enums or structs with only unmanaged fields");
                return null;
            }

            value.Kind = WireKind.Vector;
            value.Vector = VectorOf(element);
            value.Shape = shape;
            return value;
        }

        private VectorModel VectorOf(ValueModel element)
        {
            string key = "vec:" + ElementKey(element);
            if (_byKey.TryGetValue(key, out var existing)) return (VectorModel)existing;
            var vector = new VectorModel { Key = key, Element = element };
            Register(vector);
            return vector;
        }

        /// <summary>
        /// A dictionary is written as an object with two vectors: "Keys", ascending (strings by code point, the order of
        /// their UTF-8 bytes), and "Values" in the same order, so readers binary-search the keys.
        /// </summary>
        private ValueModel? MakeMap(ValueModel value, INamedTypeSymbol named, ISymbol member, string ownerName, List<INamedTypeSymbol>? unionTypes)
        {
            var dictionary = named.AllInterfaces.Concat(new[] { named }).First(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" ||
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");
            var key = Classify(dictionary.TypeArguments[0], member, ownerName, false, null, isElement: true);
            if (key == null) return null;
            bool integer = key.Kind >= WireKind.Int8 && key.Kind <= WireKind.UInt64;
            if (!(integer || key.Kind == WireKind.Char16 || key.Kind == WireKind.String))
            {
                Unsupported(member, ownerName, named, "dictionary keys must be integers, chars, enums or strings");
                return null;
            }

            var element = Classify(dictionary.TypeArguments[1], member, ownerName, false, unionTypes, isElement: true);
            if (element == null) return null;
            if (element.IsNullableValueType && !WireFormat.IsOptionalValueKind(element.Kind))
            {
                Unsupported(member, ownerName, named, "nullable values must be scalars, enums or structs with only unmanaged fields");
                return null;
            }
            string mapKey = "map:" + ElementKey(key) + "|" + ElementKey(element);
            if (!_byKey.TryGetValue(mapKey, out var existing))
            {
                var map = new ObjectModel
                {
                    Key = mapKey,
                    CsName = "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<" + key.CsTypeWithNullable() + ", " + element.CsTypeWithNullable() + ">>",
                    WireName = "Map",
                    DisplayName = "Dictionary<" + key.Type.ToDisplayString() + ", " + element.Type.ToDisplayString() + ">",
                    CppName = "Map",
                    Map = new MapInfo { Key = key, Value = element, CsKeyType = key.CsTypeWithNullable(), CsValueType = element.CsTypeWithNullable() },
                };
                map.TypeId = XxHash.Hash32(map.WireName);
                Register(map);
                map.Members.Add(MapMember("Keys", key, 0));
                map.Members.Add(MapMember("Values", element, 1));
                existing = map;
            }

            value.Kind = WireKind.Object;
            value.Object = (ObjectModel)existing;
            return value;
        }

        private MemberModel MapMember(string name, ValueModel element, int order) => new MemberModel
        {
            CsName = name,
            WireName = name,
            NameHash = XxHash.Hash64(name),
            CppName = name == "Keys" ? "keys" : "values",
            DeclarationOrder = order,
            CsTypeDisplay = name,
            Value = new ValueModel
            {
                Kind = WireKind.Vector,
                Vector = VectorOf(element),
                Shape = CollectionShape.Span,
                CsType = "global::System.ReadOnlySpan<" + element.CsTypeWithNullable() + ">",
                Type = element.Type,
                IsReferenceType = true,
            },
        };

        private static string ElementKey(ValueModel e)
        {
            switch (e.Kind)
            {
                case WireKind.Struct:
                case WireKind.SharedStruct:
                case WireKind.Object:
                case WireKind.Vector:
                case WireKind.Union:
                    return ((int)e.Kind).ToString() + ":" + e.Entry!.Key + (e.IsNullableValueType ? "?" : "");
                default:
                    return ((int)e.Kind).ToString() + (e.Enum != null ? ":" + e.Enum.CsName : "") + (e.IsNullableValueType ? "?" : "");
            }
        }

        private static ITypeSymbol? CollectionElement(INamedTypeSymbol named, out CollectionShape shape)
        {
            shape = CollectionShape.Enumerable;
            if (named.SpecialType == SpecialType.System_String) return null;
            string def = named.OriginalDefinition.ToDisplayString();
            switch (def)
            {
                case "System.Collections.Generic.List<T>":
                    shape = CollectionShape.List;
                    return named.TypeArguments[0];
                case "System.Collections.Generic.IList<T>":
                case "System.Collections.Generic.IReadOnlyList<T>":
                    shape = CollectionShape.IndexedList;
                    return named.TypeArguments[0];
            }

            if (named.TypeKind == TypeKind.Struct) return null; // e.g. ImmutableArray<T>: not yet
            foreach (var iface in named.AllInterfaces.Concat(named.TypeKind == TypeKind.Interface ? new[] { named } : Array.Empty<INamedTypeSymbol>()))
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    bool indexed = named.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>" ||
                                                                 i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IList<T>");
                    shape = indexed ? CollectionShape.IndexedList : CollectionShape.Enumerable;
                    return iface.TypeArguments[0];
                }
            }

            return null;
        }

        private static bool IsDictionary(INamedTypeSymbol named)
        {
            foreach (var i in named.AllInterfaces.Concat(new[] { named }))
            {
                string def = i.OriginalDefinition.ToDisplayString();
                if (def == "System.Collections.Generic.IDictionary<TKey, TValue>" || def == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDelegate(INamedTypeSymbol t) => t.TypeKind == TypeKind.Delegate;

        /// <summary>Framework value types with no portable C++ form (and whose private layout is not visible).</summary>
        private static string? NonPortable(ITypeSymbol t)
        {
            switch (t.SpecialType)
            {
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return "native-sized integers have no fixed size on the wire; use int or long";
                case SpecialType.System_Decimal:
                    return "decimal has no C++ equivalent; store it as double, a scaled long or a string";
                case SpecialType.System_DateTime:
                    return "DateTime has no C++ equivalent; store DateTime.Ticks (or ToBinary()) as a long";
            }

            switch (t.ToDisplayString())
            {
                case "System.TimeSpan": return "TimeSpan has no C++ equivalent; store TimeSpan.Ticks as a long";
                case "System.DateTimeOffset": return "DateTimeOffset has no C++ equivalent; store UtcTicks as a long and the offset separately";
                case "System.Guid": return "Guid has no C++ equivalent; store it as a string or in your own 16-byte struct";
                case "System.Half":
                case "System.Int128":
                case "System.UInt128":
                    return t.Name + " has no portable C++ equivalent";
            }

            return null;
        }

        internal static WireKind Primitive(SpecialType t)
        {
            switch (t)
            {
                case SpecialType.System_Boolean: return WireKind.Bool;
                case SpecialType.System_SByte: return WireKind.Int8;
                case SpecialType.System_Byte: return WireKind.UInt8;
                case SpecialType.System_Int16: return WireKind.Int16;
                case SpecialType.System_UInt16: return WireKind.UInt16;
                case SpecialType.System_Char: return WireKind.Char16;
                case SpecialType.System_Int32: return WireKind.Int32;
                case SpecialType.System_UInt32: return WireKind.UInt32;
                case SpecialType.System_Int64: return WireKind.Int64;
                case SpecialType.System_UInt64: return WireKind.UInt64;
                case SpecialType.System_Single: return WireKind.Float32;
                case SpecialType.System_Double: return WireKind.Float64;
                case SpecialType.System_String: return WireKind.String;
                default: return WireKind.Invalid;
            }
        }

        // ------------------------------------------------------------------ enums

        private EnumModel? GetEnum(INamedTypeSymbol symbol)
        {
            string key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (_enums.TryGetValue(key, out var existing)) return existing;
            var underlying = symbol.EnumUnderlyingType;
            WireKind kind = underlying == null ? WireKind.Invalid : Primitive(underlying.SpecialType);
            if (kind == WireKind.Invalid || kind == WireKind.Bool || kind == WireKind.Char16) return null;
            var model = new EnumModel
            {
                Symbol = symbol,
                CsName = key,
                CppName = CppTypeName(symbol),
                DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                Underlying = kind,
                UnderlyingCs = underlying!.ToDisplayString(),
                IsFlags = symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"),
            };
            foreach (var f in symbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (f.HasConstantValue && f.ConstantValue != null) model.Values.Add(new KeyValuePair<string, object>(f.Name, f.ConstantValue));
            }

            _enums[key] = model;
            Enums.Add(model);
            return model;
        }

        // ------------------------------------------------------------------ structs

        private StructModel? GetStruct(INamedTypeSymbol symbol, Location? location)
        {
            string key = "struct:" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (_byKey.TryGetValue(key, out var existing)) return (StructModel)existing;
            if (!_inProgressStructs.Add(key)) return null;

            var model = new StructModel
            {
                Key = key,
                Symbol = symbol,
                CsName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                WireName = StableName(symbol),
                DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                SharedByDefault = HasAttribute(symbol, "Tessera.TesseraSharedAttribute"),
                External = !SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, _compilation.Assembly),
            };
            model.CppName = CppTypeName(symbol);
            model.TypeId = XxHash.Hash32(model.WireName);

            var layoutAttr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
            int pack = 0;
            if (layoutAttr != null)
            {
                if (layoutAttr.ConstructorArguments.Length > 0 && layoutAttr.ConstructorArguments[0].Value is int kind && kind == 2)
                {
                    Report(Generator.Diagnostics.StructNotSupported, symbol, model.DisplayName, "explicit layout is not supported");
                    _inProgressStructs.Remove(key);
                    return null;
                }

                foreach (var named in layoutAttr.NamedArguments)
                {
                    if (named.Key == "Pack" && named.Value.Value is int p) pack = p;
                }
            }

            int offset = 0, maxAlign = 1;
            var usedCpp = new HashSet<string>();
            foreach (var f in symbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (f.IsStatic || f.IsConst) continue;
                if (f.IsFixedSizeBuffer)
                {
                    Report(Generator.Diagnostics.StructNotSupported, symbol, model.DisplayName, "fixed-size buffers are not supported");
                    _inProgressStructs.Remove(key);
                    return null;
                }

                var sf = new StructField { Name = f.AssociatedSymbol?.Name ?? f.Name };
                ITypeSymbol ft = f.Type;
                WireKind k = Primitive(ft.SpecialType);
                if (k == WireKind.String) k = WireKind.Invalid;
                if (k != WireKind.Invalid)
                {
                    sf.Kind = k;
                    sf.Size = k == WireKind.Bool ? 1 : WireFormat.CellSize(k);
                    sf.Align = sf.Size;
                }
                else if (ft.TypeKind == TypeKind.Enum && ft is INamedTypeSymbol et && GetEnum(et) is EnumModel em)
                {
                    sf.Kind = em.Underlying;
                    sf.Enum = em;
                    sf.Size = WireFormat.CellSize(em.Underlying);
                    sf.Align = sf.Size;
                }
                else if (NonPortable(ft) is string why)
                {
                    Report(Generator.Diagnostics.StructNotSupported, symbol, model.DisplayName, $"field '{f.Name}': {why}");
                    _inProgressStructs.Remove(key);
                    return null;
                }
                else if (ft.TypeKind == TypeKind.Struct && ft is INamedTypeSymbol nst && nst.IsUnmanagedType)
                {
                    var nested = GetStruct(nst, location);
                    if (nested == null)
                    {
                        _inProgressStructs.Remove(key); // the nested struct reported why
                        return null;
                    }

                    sf.Kind = WireKind.Struct;
                    sf.Struct = nested;
                    sf.Size = nested.Size;
                    sf.Align = nested.Align;
                }
                else
                {
                    Report(Generator.Diagnostics.StructNotSupported, symbol, model.DisplayName, $"field '{f.Name}' has unsupported type '{ft.ToDisplayString()}'");
                    _inProgressStructs.Remove(key);
                    return null;
                }

                if (pack > 0 && sf.Align > pack) sf.Align = pack;
                sf.CsType = ft.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (f.AssociatedSymbol is IPropertySymbol prop && prop.GetMethod != null && IsAccessible(prop.GetMethod))
                {
                    sf.Access = "." + prop.Name;
                }
                else if (IsAccessible(f))
                {
                    sf.Access = "." + f.Name;
                }
                else
                {
                    sf.PrivateAccessor = "__TesseraField_" + Naming.CsIdentifier(model.WireName) + "_" + Naming.CsIdentifier(f.Name);
                    sf.Access = f.Name; // raw field name for UnsafeAccessor
                }

                offset = AlignUp(offset, sf.Align);
                sf.Offset = offset;
                offset += sf.Size;
                maxAlign = Math.Max(maxAlign, sf.Align);
                string cpp = Naming.Member(sf.Name, _naming);
                while (!usedCpp.Add(cpp)) cpp += "_";
                sf.CppName = cpp;
                model.Fields.Add(sf);
            }

            if (model.Fields.Count == 0)
            {
                Report(Generator.Diagnostics.StructNotSupported, symbol, model.DisplayName, model.External
                    ? "none of its fields are visible; structs from other assemblies must have public fields"
                    : "it has no instance fields");
                _inProgressStructs.Remove(key);
                return null;
            }

            model.Align = maxAlign;
            model.Size = AlignUp(offset, maxAlign);
            _inProgressStructs.Remove(key);
            Register(model);
            return model;
        }

        private static int AlignUp(int v, int a) => (v + a - 1) / a * a;

        // ------------------------------------------------------------------ unions

        private List<INamedTypeSymbol>? UnionTypesFromAttributes(ISymbol symbol)
        {
            List<INamedTypeSymbol>? list = null;
            foreach (var a in symbol.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() != "Tessera.TesseraUnionAttribute") continue;
                foreach (var arg in a.ConstructorArguments)
                {
                    IEnumerable<TypedConstant> values = arg.Kind == TypedConstantKind.Array ? arg.Values : new[] { arg };
                    foreach (var v in values)
                    {
                        if (v.Value is INamedTypeSymbol t) (list ?? (list = new List<INamedTypeSymbol>())).Add(t);
                    }
                }
            }

            return list;
        }

        private UnionModel? GetUnion(INamedTypeSymbol baseType, List<INamedTypeSymbol>? explicitMembers, ISymbol member)
        {
            var candidates = new List<INamedTypeSymbol>();
            if (explicitMembers != null) candidates.AddRange(explicitMembers);
            var fromBase = UnionTypesFromAttributes(baseType);
            if (fromBase != null) candidates.AddRange(fromBase);
            if (candidates.Count == 0)
            {
                // Every usable subclass of this project, whether marked or not, and the marked ones of referenced projects.
                foreach (var t in SourceTypes().Concat(ReferencedModels()))
                {
                    if (!t.IsAbstract && t.TypeKind != TypeKind.Interface && !IsGenericDefinition(t) && IsAccessible(t) && DerivesFrom(t, baseType))
                    {
                        candidates.Add(t);
                    }
                }

                if (!baseType.IsAbstract && baseType.TypeKind != TypeKind.Interface) candidates.Add(baseType);
            }

            candidates = candidates.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToList();
            string key = "union:" + baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
                         string.Join(",", candidates.Select(c => c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).OrderBy(x => x, StringComparer.Ordinal));
            if (_byKey.TryGetValue(key, out var existing)) return (UnionModel)existing;
            if (candidates.Count == 0)
            {
                Report(Generator.Diagnostics.NoUnionMembers, member, baseType.ToDisplayString());
                return null;
            }

            var model = new UnionModel
            {
                Key = key,
                Symbol = baseType,
                CsName = baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DisplayName = baseType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                TypeId = XxHash.Hash32(StableName(baseType)),
            };
            // A member-level [TesseraUnion(...)] names its view after the allowed types, e.g. Item_Key_Potion.
            model.CppName = CppTypeName(baseType) + (explicitMembers == null ? "" :
                "_" + string.Join("_", candidates.Select(CppTypeName).OrderBy(x => x, StringComparer.Ordinal)));
            Register(model);
            var tags = new Dictionary<uint, ObjectModel>();
            foreach (var c in candidates)
            {
                var obj = GetObject(c);
                if (obj.TypeId == 0)
                {
                    Report(Generator.Diagnostics.ZeroUnionTag, member, obj.DisplayName);
                    continue;
                }

                if (tags.TryGetValue(obj.TypeId, out var clash))
                {
                    Report(Generator.Diagnostics.UnionTagCollision, member, clash.DisplayName, obj.DisplayName, model.DisplayName);
                    continue;
                }

                tags[obj.TypeId] = obj;
                model.Members.Add(new UnionMember { Tag = obj.TypeId, Type = obj, Depth = Depth(c) });
            }

            model.Members.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            return model;
        }

        private static bool DerivesFrom(INamedTypeSymbol t, INamedTypeSymbol baseType)
        {
            if (baseType.TypeKind == TypeKind.Interface) return t.AllInterfaces.Contains(baseType, SymbolEqualityComparer.Default);
            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(b, baseType)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ defaults

        private void ResolveDefault(MemberModel m, ISymbol member)
        {
            var v = m.Value;
            if (v.IsNullableValueType) return;
            bool scalar = v.Kind == WireKind.Bool || (v.Kind >= WireKind.Int8 && v.Kind <= WireKind.Char16);
            if (!scalar) return;

            var dv = FindAttribute(member, "System.ComponentModel.DefaultValueAttribute");
            if (dv != null && dv.ConstructorArguments.Length == 1 && !dv.ConstructorArguments[0].IsNull)
            {
                m.DefaultValue = dv.ConstructorArguments[0].Value;
                m.HasCustomDefault = true;
                return;
            }

            if (!SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, _compilation.Assembly))
            {
                // Declared in another assembly, whose initializers are not visible here. If that assembly's generator treated
                // the declaring type as a model, it recorded them as [assembly: TesseraDefault(typeof(T), "Member", value)].
                var records = Records(member.ContainingAssembly);
                if (records.Defaults.TryGetValue((member.ContainingType, member.Name), out var value))
                {
                    m.DefaultValue = value;
                    m.HasCustomDefault = true;
                }
                else if (!records.Declared.Contains(member.ContainingType))
                {
                    if (!_hiddenDefaults.TryGetValue(member.ContainingAssembly, out var types))
                    {
                        _hiddenDefaults[member.ContainingAssembly] = types = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    types.Add(member.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
                }

                return;
            }

            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                ExpressionSyntax? init = null;
                if (syntax is VariableDeclaratorSyntax vd) init = vd.Initializer?.Value;
                else if (syntax is PropertyDeclarationSyntax pd) init = pd.Initializer?.Value;
                if (init == null) continue;
                var semantic = _compilation.GetSemanticModel(syntax.SyntaxTree);
                var constant = semantic.GetConstantValue(init);
                if (constant.HasValue && constant.Value != null)
                {
                    m.DefaultValue = constant.Value;
                    m.HasCustomDefault = true;
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private List<INamedTypeSymbol>? _referencedModels;
        private List<INamedTypeSymbol>? _sourceTypes;

        /// <summary>
        /// Types that referenced model assemblies (those that use Tessera themselves) mark with [Tessera] or list in
        /// [assembly: TesseraRoot], so a union whose base type comes from another assembly includes the subclasses there.
        /// Unmarked types of other assemblies are never union members: only their own project could vouch for them.
        /// </summary>
        private List<INamedTypeSymbol> ReferencedModels()
        {
            if (_referencedModels != null) return _referencedModels;
            var found = new List<INamedTypeSymbol>();
            foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (!assembly.Modules.Any(m => m.ReferencedAssemblySymbols.Any(r => r.Name == "Tessera"))) continue;
                found.AddRange(AllTypes(assembly.GlobalNamespace).Where(t => HasAttribute(t, TesseraAttribute)));
                found.AddRange(ListedRoots(assembly));
            }

            return _referencedModels = found;
        }

        /// <summary>Every type declared in this compilation (nested ones included).</summary>
        private List<INamedTypeSymbol> SourceTypes() => _sourceTypes ??= AllTypes(_compilation.Assembly.GlobalNamespace).ToList();

        private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    foreach (var t in AllTypes(ns)) yield return t;
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var t in AllTypes(type)) yield return t;
                }
            }
        }

        /// <summary>Whether the generated writers (in this compilation) can name the type.</summary>
        private bool IsAccessible(INamedTypeSymbol type)
        {
            bool sameAssembly = SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly);
            for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
            {
                if (t.IsFileLocal) return false;
                switch (t.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                        break;
                    case Accessibility.Internal:
                    case Accessibility.ProtectedOrInternal:
                        if (!sameAssembly) return false;
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        /// <summary>A generic type definition, or a type nested in one: not a concrete type a union can hold.</summary>
        private static bool IsGenericDefinition(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
            {
                if (t.TypeParameters.Length > 0) return true;
            }

            return false;
        }

        /// <summary>[Tessera], or listed in [assembly: TesseraRoot] here or in the type's own assembly.</summary>
        private bool IsMarked(INamedTypeSymbol symbol) =>
            _explicit.Contains(symbol) || _explicit.Contains(symbol.OriginalDefinition) || HasAttribute(symbol, TesseraAttribute) ||
            ListedRoots(symbol.ContainingAssembly).Contains(symbol, SymbolEqualityComparer.Default);

        /// <summary>The member defaults another assembly's generator recorded, and the types it recorded them for.</summary>
        private sealed class AssemblyRecords
        {
            public readonly HashSet<ITypeSymbol> Declared = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            public readonly Dictionary<(ITypeSymbol Type, string Member), object> Defaults = new Dictionary<(ITypeSymbol, string), object>(new RecordKeyComparer());
        }

        private sealed class RecordKeyComparer : IEqualityComparer<(ITypeSymbol Type, string Member)>
        {
            public bool Equals((ITypeSymbol Type, string Member) x, (ITypeSymbol Type, string Member) y) =>
                x.Member == y.Member && SymbolEqualityComparer.Default.Equals(x.Type, y.Type);

            public int GetHashCode((ITypeSymbol Type, string Member) k) => SymbolEqualityComparer.Default.GetHashCode(k.Type) * 31 + k.Member.GetHashCode();
        }

        private AssemblyRecords Records(IAssemblySymbol assembly)
        {
            if (_records.TryGetValue(assembly, out var records)) return records;
            records = new AssemblyRecords();
            foreach (var a in assembly.GetAttributes())
            {
                var args = a.ConstructorArguments;
                switch (a.AttributeClass?.ToDisplayString())
                {
                    case "Tessera.TesseraDeclaredAttribute" when args.Length == 1 && args[0].Value is ITypeSymbol declared:
                        records.Declared.Add(declared);
                        break;
                    case "Tessera.TesseraDefaultAttribute" when args.Length == 3 && args[0].Value is ITypeSymbol type && args[1].Value is string member && args[2].Value != null:
                        records.Declared.Add(type);
                        records.Defaults[(type, member)] = args[2].Value!;
                        break;
                }
            }

            return _records[assembly] = records;
        }

        private void Register(TypeModel model)
        {
            _byKey[model.Key] = model;
            model.Index = Types.Count;
            Types.Add(model);
        }

        private static string StableName(INamedTypeSymbol symbol)
        {
            var attr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TesseraAttribute);
            if (attr != null && attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s && s.Length > 0) return s;
            string name = symbol.Name;
            for (var outer = symbol.ContainingType; outer != null; outer = outer.ContainingType) name = outer.Name + "." + name;
            if (symbol.TypeArguments.Length > 0)
            {
                name += "<" + string.Join(",", symbol.TypeArguments.Select(a => a is INamedTypeSymbol n ? StableName(n) : a.Name)) + ">";
            }

            return name;
        }

        private static string CppTypeName(INamedTypeSymbol symbol)
        {
            string name = symbol.Name;
            for (var outer = symbol.ContainingType; outer != null; outer = outer.ContainingType) name = outer.Name + "_" + name;
            foreach (var arg in symbol.TypeArguments) name += "_" + (arg is INamedTypeSymbol n ? CppTypeName(n) : arg.Name);
            return Naming.Sanitize(name);
        }

        private static bool HasAttribute(ISymbol symbol, string fullName) => FindAttribute(symbol, fullName) != null;

        private static AttributeData? FindAttribute(ISymbol symbol, string fullName) =>
            symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

        private void Unsupported(ISymbol member, string owner, ITypeSymbol type, string reason) =>
            Report(Generator.Diagnostics.UnsupportedType, member, member.Name, owner, type.ToDisplayString(), reason);

        private void Report(DiagnosticDescriptor descriptor, ISymbol symbol, params object[] args)
        {
            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
            Diagnostics.Add(Diagnostic.Create(descriptor, location, args));
        }
    }
}
