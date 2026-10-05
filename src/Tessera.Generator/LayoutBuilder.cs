using System;
using System.Collections.Generic;
using System.Linq;

namespace Tessera.Generator
{
    /// <summary>
    /// Computes wire layouts, fingerprints and encoded schema entries. The rules here are the format: the C# writer and
    /// the C++ reader both follow the bit/cell order computed by <see cref="LayoutObject"/>.
    /// </summary>
    internal static class LayoutBuilder
    {
        public static void Build(List<TypeModel> types)
        {
            foreach (var t in types)
            {
                if (t is ObjectModel o) LayoutObject(o);
            }

            foreach (var t in types) t.References.Clear();
            foreach (var t in types) t.Fingerprint = XxHash.Hash64(OwnBytes(t));
            foreach (var t in types) t.DeepFingerprint = Deep(t);
            foreach (var t in types) Encode(t);
        }

        /// <summary>
        /// Cells are ordered by alignment (descending), then size (ascending), then name hash. Fixed cells
        /// ([TesseraKeepDefault] values) come first, at constant positions and without presence bits; the other cells'
        /// presence bits follow their order; bool members come last with two bits each (present, value).
        /// </summary>
        public static void LayoutObject(ObjectModel o)
        {
            o.Fixed.Clear();
            o.Cells.Clear();
            o.Bools.Clear();
            var fixedCandidates = new List<MemberModel>();
            foreach (var m in o.Members)
            {
                m.IsFixed = false;
                m.FixedOffset = 0;
                if (m.Value.Kind == WireKind.Bool)
                {
                    m.CellSize = 0;
                    m.CellAlign = 1;
                    o.Bools.Add(m);
                    continue;
                }

                if (m.Value.Kind == WireKind.Struct)
                {
                    m.CellSize = m.Value.Struct!.Size;
                    m.CellAlign = m.Value.Struct.Align;
                }
                else
                {
                    m.CellSize = WireFormat.CellSize(m.Value.Kind);
                    m.CellAlign = WireFormat.CellAlign(m.Value.Kind);
                }

                if (IsFixedCandidate(m)) fixedCandidates.Add(m);
                else o.Cells.Add(m);
            }

            Comparison<MemberModel> cellOrder = (a, b) =>
            {
                int c = b.CellAlign.CompareTo(a.CellAlign);
                if (c != 0) return c;
                c = a.CellSize.CompareTo(b.CellSize);
                if (c != 0) return c;
                c = a.NameHash.CompareTo(b.NameHash);
                return c != 0 ? c : string.CompareOrdinal(a.WireName, b.WireName);
            };
            fixedCandidates.Sort(cellOrder);

            // Fixed cells, back to back (descending alignment keeps each aligned), up to the absent-object block's size;
            // the rest stay ordinary cells, always present.
            int fixedEnd = 0;
            foreach (var m in fixedCandidates)
            {
                if (fixedEnd + m.CellSize > WireFormat.MaxFixedBytes)
                {
                    o.Cells.Add(m);
                    continue;
                }

                m.IsFixed = true;
                m.FixedOffset = fixedEnd;
                m.Bit = -1;
                fixedEnd += m.CellSize;
                o.Fixed.Add(m);
            }

            o.Cells.Sort(cellOrder);
            o.Bools.Sort((a, b) =>
            {
                int c = a.NameHash.CompareTo(b.NameHash);
                return c != 0 ? c : string.CompareOrdinal(a.WireName, b.WireName);
            });

            int bit = 0;
            o.MaxCellAlign = 1;
            o.RefCellCount = 0;
            int cellAlign = 1;
            foreach (var m in o.Fixed) o.MaxCellAlign = Math.Max(o.MaxCellAlign, m.CellAlign);
            foreach (var m in o.Cells)
            {
                m.Bit = bit++;
                o.MaxCellAlign = Math.Max(o.MaxCellAlign, m.CellAlign);
                cellAlign = Math.Max(cellAlign, m.CellAlign);
                if (WireFormat.IsReference(m.Value.Kind)) o.RefCellCount++;
            }

            // The other cells follow the fixed ones, so pad the fixed cells to their alignment.
            o.FixedSize = (fixedEnd + cellAlign - 1) / cellAlign * cellAlign;

            // Bool pairs start on an even bit so a pair never straddles two header words.
            bit = (bit + 1) & ~1;
            foreach (var m in o.Bools)
            {
                m.Bit = bit;
                bit += 2;
            }

            o.HeaderWords = Math.Max(1, (bit + 31) / 32);
        }

        /// <summary>[TesseraKeepDefault] values that need no presence: non-nullable scalars, enums and inline structs.</summary>
        private static bool IsFixedCandidate(MemberModel m)
        {
            if (!m.KeepDefault || m.Value.IsNullableValueType) return false;
            var k = m.Value.Kind;
            return k == WireKind.Struct || (k >= WireKind.Int8 && k <= WireKind.Char16);
        }

        /// <summary>Every member in wire order: fixed cells, cells, bools.</summary>
        public static IEnumerable<MemberModel> WireOrder(ObjectModel o) => o.Fixed.Concat(o.Cells).Concat(o.Bools);

        // ------------------------------------------------------------------ fingerprints

        private static byte[] OwnBytes(TypeModel t)
        {
            var w = new ByteWriter();
            w.U8((byte)t.Category);
            switch (t)
            {
                case ObjectModel o:
                    w.U16((ushort)o.HeaderWords);
                    w.U16((ushort)o.Fixed.Count);
                    w.U16((ushort)o.FixedSize);
                    w.U16((ushort)o.Cells.Count);
                    w.U16((ushort)o.Bools.Count);
                    foreach (var m in WireOrder(o))
                    {
                        w.U64(m.NameHash);
                        w.U8((byte)m.Value.Kind);
                        w.U16((ushort)m.CellSize);
                        if (m.Value.Struct != null) w.U64(StructBytesHash(m.Value.Struct));
                    }

                    break;
                case StructModel s:
                    w.U64(StructBytesHash(s));
                    break;
                case VectorModel v:
                    w.U8((byte)v.Element.Kind);
                    if (v.Element.Struct != null) w.U64(StructBytesHash(v.Element.Struct));
                    if (v.Element.IsNullableValueType) w.U8(WireFormat.ElementOptional);
                    break;
                case UnionModel u:
                    w.U16((ushort)u.Members.Count);
                    foreach (var m in u.Members) w.U32(m.Tag);
                    break;
            }

            return w.ToArray();
        }

        private static ulong StructBytesHash(StructModel s)
        {
            var w = new ByteWriter();
            WriteStructShape(w, s);
            return XxHash.Hash64(w.ToArray());
        }

        private static void WriteStructShape(ByteWriter w, StructModel s)
        {
            w.U16((ushort)s.Size);
            w.U8((byte)s.Align);
            w.U16((ushort)s.Fields.Count);
            foreach (var f in s.Fields)
            {
                w.U64(XxHash.Hash64(f.Name));
                w.U8((byte)f.Kind);
                w.U16((ushort)f.Offset);
                if (f.Struct != null) WriteStructShape(w, f.Struct);
            }
        }

        /// <summary>Hash of the closure reachable from <paramref name="root"/>, types numbered in DFS order.</summary>
        private static ulong Deep(TypeModel root)
        {
            var order = new Dictionary<TypeModel, int>();
            var list = new List<TypeModel>();
            void Visit(TypeModel t)
            {
                if (order.ContainsKey(t)) return;
                order[t] = list.Count;
                list.Add(t);
                foreach (var c in Children(t)) Visit(c);
            }

            Visit(root);
            var w = new ByteWriter();
            foreach (var t in list)
            {
                w.U64(t.Fingerprint);
                foreach (var c in Children(t)) w.U32((uint)order[c]);
            }

            return XxHash.Hash64(w.ToArray());
        }

        /// <summary>Entries referenced by an entry, in a fixed order.</summary>
        public static IEnumerable<TypeModel> Children(TypeModel t)
        {
            switch (t)
            {
                case ObjectModel o:
                    foreach (var m in WireOrder(o))
                    {
                        if (m.Value.Entry != null) yield return m.Value.Entry;
                    }

                    break;
                case VectorModel v:
                    if (v.Element.Entry != null) yield return v.Element.Entry;
                    break;
                case UnionModel u:
                    foreach (var m in u.Members) yield return m.Type;
                    break;
            }
        }

        // ------------------------------------------------------------------ schema entries

        // Common header (24 bytes): u8 category, u8 flags, u16 count, u32 typeId, u64 fingerprint, u64 deepFingerprint.
        private static void Encode(TypeModel t)
        {
            var w = new ByteWriter();
            var slots = new List<ushort>();
            var refs = new List<TypeModel>();
            ushort RefIndex(TypeModel? target)
            {
                if (target == null) return WireFormat.NoType;
                int i = refs.IndexOf(target);
                if (i < 0)
                {
                    i = refs.Count;
                    refs.Add(target);
                }

                return (ushort)i;
            }

            void TypeRef(TypeModel? target)
            {
                if (target != null) slots.Add((ushort)w.Length);
                w.U16(RefIndex(target));
            }

            w.U8((byte)t.Category);
            w.U8(t is ObjectModel { Fixed.Count: > 0 } ? WireFormat.EntryFixedCells : (byte)0);
            switch (t)
            {
                case ObjectModel o:
                    w.U16((ushort)(o.Fixed.Count + o.Cells.Count + o.Bools.Count));
                    Header(w, t);
                    w.U16((ushort)o.HeaderWords);
                    w.U16((ushort)o.Cells.Count);
                    if (o.Fixed.Count > 0)
                    {
                        w.U16((ushort)o.Fixed.Count);
                        w.U16((ushort)o.FixedSize);
                    }

                    foreach (var m in WireOrder(o))
                    {
                        w.U64(m.NameHash);
                        w.U8((byte)m.Value.Kind);
                        byte flags = m.Value.IsNullableValueType || m.Value.IsReferenceType ? WireFormat.FieldNullable : (byte)0;
                        w.U8(m.IsFixed ? (byte)(flags | WireFormat.FieldFixed) : flags);
                        TypeRef(m.Value.Entry);
                    }

                    break;
                case StructModel s:
                    w.U16((ushort)s.Fields.Count);
                    Header(w, t);
                    w.U16((ushort)s.Size);
                    w.U8((byte)s.Align);
                    w.U8(0);
                    break;
                case VectorModel v:
                    w.U16(0);
                    Header(w, t);
                    w.U8((byte)v.Element.Kind);
                    w.U8(v.Element.IsReferenceType ? WireFormat.ElementNullable : v.Element.IsNullableValueType ? WireFormat.ElementOptional : (byte)0);
                    TypeRef(v.Element.Entry);
                    break;
                case UnionModel u:
                    w.U16((ushort)u.Members.Count);
                    Header(w, t);
                    foreach (var m in u.Members)
                    {
                        w.U32(m.Tag);
                        TypeRef(m.Type);
                        w.U16(0);
                    }

                    break;
            }

            t.Entry = w.ToArray();
            t.RefSlots = slots;
            t.References = refs;
        }

        private static void Header(ByteWriter w, TypeModel t)
        {
            w.U32(t.TypeId);
            w.U64(t.Fingerprint);
            w.U64(t.DeepFingerprint);
        }

        private sealed class ByteWriter
        {
            private readonly List<byte> _bytes = new List<byte>();

            public int Length => _bytes.Count;

            public void U8(byte v) => _bytes.Add(v);

            public void U16(ushort v)
            {
                _bytes.Add((byte)v);
                _bytes.Add((byte)(v >> 8));
            }

            public void U32(uint v)
            {
                for (int i = 0; i < 4; i++) _bytes.Add((byte)(v >> (8 * i)));
            }

            public void U64(ulong v)
            {
                for (int i = 0; i < 8; i++) _bytes.Add((byte)(v >> (8 * i)));
            }

            public byte[] ToArray() => _bytes.ToArray();
        }
    }
}
