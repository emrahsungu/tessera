using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Tessera.Generator
{
    /// <summary>How a C# collection member is read when writing a vector.</summary>
    internal enum CollectionShape
    {
        Array,
        List,
        IndexedList, // IList<T> / IReadOnlyList<T>
        Enumerable,  // anything else: materialized first
        Span,        // ReadOnlySpan<T> (a dictionary's sorted keys or values)
    }

    /// <summary>A dictionary: an object with two vector members, "Keys" (ascending) and "Values" (in key order).</summary>
    internal sealed class MapInfo
    {
        public ValueModel Key = null!;    // classified as a vector element
        public ValueModel Value = null!;
        public string CsKeyType = "";
        public string CsValueType = "";
    }

    /// <summary>A schema entry: object, struct, vector or union.</summary>
    internal abstract class TypeModel
    {
        /// <summary>Unique key inside one generator run.</summary>
        public string Key = "";

        /// <summary>Dense index in key order (see <see cref="ModelBuilder.Complete"/>); also the suffix of generated member names.</summary>
        public int Index;

        public abstract EntryCategory Category { get; }

        public uint TypeId;
        public ulong Fingerprint;
        public ulong DeepFingerprint;
        public byte[] Entry = new byte[0];
        public List<ushort> RefSlots = new List<ushort>();
        public List<TypeModel> References = new List<TypeModel>();

        /// <summary>C++ identifier of the type (for objects, structs and unions).</summary>
        public string CppName = "";
    }

    internal sealed class ObjectModel : TypeModel
    {
        public override EntryCategory Category => EntryCategory.Object;

        public INamedTypeSymbol Symbol = null!;
        public string CsName = "";   // global::-qualified
        public string WireName = ""; // stable name (type id source)
        public string DisplayName = "";
        public bool IsValueType;
        public MapInfo? Map;        // a dictionary (no C# type of its own: Symbol is null)
        public List<MemberModel> Members = new List<MemberModel>(); // declaration order

        // Layout
        public List<MemberModel> Fixed = new List<MemberModel>(); // wire order: always stored, constant positions
        public List<MemberModel> Cells = new List<MemberModel>(); // wire order: stored when present
        public List<MemberModel> Bools = new List<MemberModel>(); // wire order
        public int FixedSize;  // bytes of fixed cells after the header, padded to the cells' alignment
        public int HeaderWords;
        public int MaxCellAlign;
        public int RefCellCount;
    }

    internal sealed class StructField
    {
        public string Name = "";        // C# member name
        public string CppName = "";
        public string Access = "";      // C# expression suffix, e.g. ".X"
        public string? PrivateAccessor; // UnsafeAccessor method for private fields
        public WireKind Kind;
        public EnumModel? Enum;
        public StructModel? Struct;     // nested struct
        public int Offset;
        public int Size;
        public int Align;
        public string CsType = "";
    }

    internal sealed class StructModel : TypeModel
    {
        public override EntryCategory Category => EntryCategory.Struct;

        public INamedTypeSymbol Symbol = null!;
        public string CsName = "";
        public string WireName = "";
        public string DisplayName = "";
        public bool SharedByDefault;
        public List<StructField> Fields = new List<StructField>();
        public int Size;
        public int Align;

        /// <summary>Declared in another assembly: its non-public fields are invisible, so the writer checks the size.</summary>
        public bool External;
    }

    internal sealed class VectorModel : TypeModel
    {
        public override EntryCategory Category => EntryCategory.Vector;

        public ValueModel Element = null!;
    }

    internal sealed class UnionMember
    {
        public uint Tag;
        public ObjectModel Type = null!;
        public int Depth; // inheritance depth, deeper types are tested first
    }

    internal sealed class UnionModel : TypeModel
    {
        public override EntryCategory Category => EntryCategory.Union;

        public INamedTypeSymbol Symbol = null!;
        public string CsName = "";
        public string DisplayName = "";
        public List<UnionMember> Members = new List<UnionMember>();
    }

    internal sealed class EnumModel
    {
        public INamedTypeSymbol Symbol = null!;
        public string CsName = "";
        public string CppName = "";
        public string DisplayName = "";
        public WireKind Underlying;
        public string UnderlyingCs = "";
        public List<KeyValuePair<string, object>> Values = new List<KeyValuePair<string, object>>();
        public bool IsFlags;
    }

    /// <summary>Type of a member or vector element.</summary>
    internal sealed class ValueModel
    {
        public WireKind Kind;
        public ITypeSymbol Type = null!;     // declared C# type (Nullable<T> unwrapped for value types)
        public string CsType = "";           // global::-qualified, unwrapped
        public bool IsNullableValueType;     // Nullable<T>
        public bool IsReferenceType;
        public EnumModel? Enum;
        public StructModel? Struct;          // Struct / SharedStruct
        public ObjectModel? Object;
        public VectorModel? Vector;
        public CollectionShape Shape;
        public UnionModel? Union;

        public TypeModel? Entry => (TypeModel?)Struct ?? (TypeModel?)Object ?? (TypeModel?)Vector ?? Union;
    }

    internal sealed class MemberModel
    {
        public string CsName = "";
        public string WireName = "";
        public ulong NameHash;   // member identity: xxHash64 of the wire name
        public bool IsProperty;
        public ISymbol Symbol = null!;
        public ValueModel Value = null!;
        public bool KeepDefault;
        public object? DefaultValue;     // constant default for scalars/enums/bools (null = zero)
        public bool HasCustomDefault;
        public string CppName = "";
        public int DeclarationOrder;
        public string CsTypeDisplay = "";

        // Layout
        public int Bit;       // presence bit (bools: present bit, value bit is Bit + 1); -1 for fixed cells
        public int CellSize;
        public int CellAlign;
        public bool IsFixed;
        public int FixedOffset; // fixed cells: position after the header

        public bool IsBool => Value.Kind == WireKind.Bool;
    }
}
