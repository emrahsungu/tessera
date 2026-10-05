using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tessera;

public sealed partial class TesseraWriter
{
    // Section index + 1 of every type used by the current buffer, indexed by TesseraType.Id (0 = not used).
    private int[] _sectionIndex = new int[64];
    private readonly List<TesseraType> _usedTypes = new();
    private TesseraType[] _cachedTypes = Array.Empty<TesseraType>();
    private byte[] _cachedSchema = Array.Empty<byte>();

    private void ResetSchema()
    {
        foreach (TesseraType t in _usedTypes) _sectionIndex[t.Id] = 0;
        _usedTypes.Clear();
    }

    /// <summary>
    /// Marks a type (and the types it statically references, except union members) as present in this buffer so its
    /// schema entry is embedded. Generated code calls this for each concrete type written through a union.
    /// </summary>
    public void MarkUsed(TesseraType type)
    {
        if (!_options.IncludeSchema) return;
        if (type.Id < _sectionIndex.Length && _sectionIndex[type.Id] != 0) return;
        MarkSlow(type);
    }

    private void MarkSlow(TesseraType type)
    {
        if (type.Id >= _sectionIndex.Length) Array.Resize(ref _sectionIndex, Math.Max(type.Id + 1, _sectionIndex.Length * 2));
        if (_sectionIndex[type.Id] != 0) return;
        _usedTypes.Add(type);
        _sectionIndex[type.Id] = _usedTypes.Count;
        if (type.Category == EntryCategory.Union) return;
        foreach (TesseraType child in type.References) MarkSlow(child);
    }

    // Section layout: u32 byteLength, u16 entryCount, u16 rootEntry, u32 entryOffset[entryCount], entries.
    private void WriteSchema(TesseraType root)
    {
        MarkSlow(root);
        int count = _usedTypes.Count;
        if (count > 0xFFFE) throw new TesseraException("Too many types in one buffer.");
        if (!SameAsCached())
        {
            _cachedSchema = Encode(root, count);
            _cachedTypes = _usedTypes.ToArray();
        }

        _cachedSchema.CopyTo(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref Reserve(_cachedSchema.Length, 8, _cachedSchema.Length), _cachedSchema.Length));
    }

    private bool SameAsCached()
    {
        if (_cachedTypes.Length != _usedTypes.Count) return false;
        for (int i = 0; i < _cachedTypes.Length; i++)
        {
            if (!ReferenceEquals(_cachedTypes[i], _usedTypes[i])) return false;
        }

        return true;
    }

    private byte[] Encode(TesseraType root, int count)
    {
        int size = 8 + 4 * count;
        foreach (TesseraType t in _usedTypes) size += t.Entry.Length;
        var s = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(s, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(6), (ushort)(_sectionIndex[root.Id] - 1));
        int at = 8 + 4 * count;
        for (int i = 0; i < count; i++)
        {
            TesseraType t = _usedTypes[i];
            BinaryPrimitives.WriteUInt32LittleEndian(s.AsSpan(8 + 4 * i), (uint)at);
            Span<byte> entry = s.AsSpan(at, t.Entry.Length);
            t.Entry.CopyTo(entry);
            foreach (ushort slot in t.RefSlots)
            {
                ushort local = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(slot));
                ushort mapped = WireFormat.NoType;
                if (local != WireFormat.NoType)
                {
                    TesseraType target = t.References[local];
                    if (target.Id < _sectionIndex.Length && _sectionIndex[target.Id] != 0) mapped = (ushort)(_sectionIndex[target.Id] - 1);
                }

                BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(slot), mapped);
            }

            at += t.Entry.Length;
        }

        return s;
    }
}
