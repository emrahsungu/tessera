// Shared between the runtime (Tessera) and the source generator (Tessera.Generator).
// Keep in sync with cpp/include/tessera/tessera.hpp and docs/FORMAT.md.
namespace Tessera
{
    /// <summary>Wire kind of a member or vector element.</summary>
    public enum WireKind : byte
    {
        /// <summary>Not a valid kind.</summary>
        Invalid = 0,
        /// <summary>bool: stored as two header bits (present, value); no cell bytes.</summary>
        Bool = 1,
        /// <summary>sbyte.</summary>
        Int8 = 2,
        /// <summary>byte.</summary>
        UInt8 = 3,
        /// <summary>short.</summary>
        Int16 = 4,
        /// <summary>ushort.</summary>
        UInt16 = 5,
        /// <summary>int.</summary>
        Int32 = 6,
        /// <summary>uint.</summary>
        UInt32 = 7,
        /// <summary>long.</summary>
        Int64 = 8,
        /// <summary>ulong.</summary>
        UInt64 = 9,
        /// <summary>float.</summary>
        Float32 = 10,
        /// <summary>double.</summary>
        Float64 = 11,
        /// <summary>char (UTF-16 code unit).</summary>
        Char16 = 12,
        /// <summary>Fixed-size struct stored inline.</summary>
        Struct = 13,
        /// <summary>UTF-8 string behind an offset.</summary>
        String = 14,
        /// <summary>Object (table) behind an offset.</summary>
        Object = 15,
        /// <summary>Vector behind an offset.</summary>
        Vector = 16,
        /// <summary>Polymorphic object: 4-byte type tag followed by an offset.</summary>
        Union = 17,
        /// <summary>Fixed-size struct stored once behind an offset.</summary>
        SharedStruct = 18,
    }

    /// <summary>Category of a schema entry.</summary>
    public enum EntryCategory : byte
    {
        /// <summary>Object (table).</summary>
        Object = 1,
        /// <summary>Fixed-size struct.</summary>
        Struct = 2,
        /// <summary>Vector.</summary>
        Vector = 3,
        /// <summary>Union (set of tagged object types).</summary>
        Union = 4,
    }

    /// <summary>Format constants.</summary>
    public static class WireFormat
    {
        /// <summary>Buffer header size in bytes.</summary>
        public const int HeaderSize = 16;
        /// <summary>Magic stored at byte offset 4 ("TS").</summary>
        public const ushort Magic = 0x5354;
        /// <summary>Format version stored at byte offset 6.</summary>
        public const byte Version = 2;
        /// <summary>Header flag: a schema section follows the header.</summary>
        public const byte FlagSchema = 1;
        /// <summary>Type reference meaning "no entry".</summary>
        public const ushort NoType = 0xFFFF;
        /// <summary>Field flag: the C# member is nullable (informational).</summary>
        public const byte FieldNullable = 1;
        /// <summary>Field flag: a fixed cell (always stored, at a constant position, no presence bit).</summary>
        public const byte FieldFixed = 2;
        /// <summary>Object entry flag: the object has fixed cells (their count and size follow the cell count).</summary>
        public const byte EntryFixedCells = 1;
        /// <summary>Most bytes of fixed cells per object (absent objects read them from a 2 KB zero block).</summary>
        public const int MaxFixedBytes = 1024;
        /// <summary>Vector element flag: elements may be null.</summary>
        public const byte ElementNullable = 1;
        /// <summary>Vector element flag: optional values (presence bits, then every element's value).</summary>
        public const byte ElementOptional = 2;

        /// <summary>Size in bytes of a cell holding <paramref name="kind"/>; 0 for bool (header bits only).</summary>
        public static int CellSize(WireKind kind)
        {
            switch (kind)
            {
                case WireKind.Bool: return 0;
                case WireKind.Int8:
                case WireKind.UInt8: return 1;
                case WireKind.Int16:
                case WireKind.UInt16:
                case WireKind.Char16: return 2;
                case WireKind.Int32:
                case WireKind.UInt32:
                case WireKind.Float32:
                case WireKind.String:
                case WireKind.Object:
                case WireKind.Vector:
                case WireKind.SharedStruct: return 4;
                case WireKind.Int64:
                case WireKind.UInt64:
                case WireKind.Float64:
                case WireKind.Union: return 8;
                default: return -1; // Struct: size comes from the struct entry
            }
        }

        /// <summary>Alignment of a cell holding <paramref name="kind"/> (struct alignment comes from the struct entry).</summary>
        public static int CellAlign(WireKind kind)
        {
            switch (kind)
            {
                case WireKind.Union: return 4;
                case WireKind.Struct: return -1;
                default:
                    int size = CellSize(kind);
                    return size <= 0 ? 1 : size;
            }
        }

        /// <summary>True for nullable vector elements stored as optional values: scalars, bools, enums, inline structs.</summary>
        public static bool IsOptionalValueKind(WireKind kind) => kind == WireKind.Struct || (kind >= WireKind.Bool && kind <= WireKind.Char16);

        /// <summary>True for kinds whose cell holds an offset to data elsewhere in the buffer.</summary>
        public static bool IsReference(WireKind kind) =>
            kind == WireKind.String || kind == WireKind.Object || kind == WireKind.Vector ||
            kind == WireKind.Union || kind == WireKind.SharedStruct;
    }
}
