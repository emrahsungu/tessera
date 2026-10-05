# Tessera wire format, version 2

All integers are little-endian. "Offset" always means a `u32` holding the forward distance in bytes from the offset's
own position to its target; the target always lies after the offset. Zero means null where a null is allowed. The
writer builds buffers back to front, so references point forward and cycles are impossible. Readers require the
buffer to start at an 8-byte aligned address. Every value in the buffer is naturally aligned relative to that start.

## Buffer header (16 bytes)

| Offset | Type | Value |
|---:|---|---|
| 0 | `u32` | absolute offset of the root object |
| 4 | `u16` | magic `0x5354` (bytes `54 53`, "TS") |
| 6 | `u8` | format version, `2` (readers reject other versions) |
| 7 | `u8` | flags: bit 0 = schema section present; other bits are zero |
| 8 | `u64` | deep fingerprint of the root type (see [Fingerprints](#fingerprints)) |

When bit 0 of the flags is set, the schema section starts at offset 16. The buffer length is a multiple of 8.

## Objects

An object (class, record, or a struct with reference members) is a presence header of `W` 32-bit words, then its
fixed cells, then the cells of its present members.

**Wire order.** The non-bool members are the *cells*. Members marked `[TesseraKeepDefault]` whose value is a scalar
(not bool), an enum or an inline struct, and is not nullable, are *fixed cells*: always stored, at a constant
position, without a presence bit. Fixed cells come first, then the other cells, each group sorted by cell
alignment (descending), then cell size (ascending), then member name hash, then name (ordinal). Fixed cells take at
most 1,024 bytes; any beyond that are ordinary cells. Bool members come after the cells, sorted by name hash and
then name.

**Fixed cells.** Stored back to back from byte `4W`, so each one's position is a constant. They are padded with zero
bytes to `F`, a multiple of the largest alignment of the other cells, so those stay aligned. `F` is 0 when there are
no fixed cells.

**Presence bits.** Bit `j` of the header is (non-fixed) cell `j`'s presence bit. Bit `j` lives in word `j / 32`, at position
`j % 32`. Bool members take two bits each (present, value), starting at the first even bit after the cells:
`boolStart = cellCount` rounded up to even. A pair therefore never straddles two words. `W = max(1, ceil((boolStart
+ 2 * boolCount) / 32))`. Unused bits are zero, and a bool's value bit may be set only when its present bit is set.

**Cells.** Only present cells are stored, back to back in wire order, starting at byte `4W + F`. A present cell's
position is `4W + F` plus the sizes of the present cells before it. A reader computes it as one popcount per run of
equal-size cells in a header word. Because cells are sorted by alignment, every cell is naturally aligned once the
cell area is. The writer aligns the cell area (at `4W`) to the largest alignment of any cell, fixed or not, and to
at least 4.

| Kind | Id | Cell size / alignment | Cell contents |
|---|---:|---|---|
| Bool | 1 | — | header bits only |
| Int8, UInt8 | 2, 3 | 1 / 1 | value |
| Int16, UInt16 | 4, 5 | 2 / 2 | value |
| Int32, UInt32 | 6, 7 | 4 / 4 | value |
| Int64, UInt64 | 8, 9 | 8 / 8 | value |
| Float32, Float64 | 10, 11 | 4 / 4, 8 / 8 | IEEE 754 bits |
| Char16 | 12 | 2 / 2 | UTF-16 code unit |
| Struct | 13 | struct size / struct alignment | the struct's bytes, inline |
| String | 14 | 4 / 4 | offset to a string |
| Object | 15 | 4 / 4 | offset to an object |
| Vector | 16 | 4 / 4 | offset to a vector |
| Union | 17 | 8 / 4 | `u32` tag, then an offset (relative to its own position, cell + 4) to the member object |
| SharedStruct | 18 | 4 / 4 | offset to the struct's bytes |

Enums are stored as their underlying integer kind. A member that is absent, null, or (for non-nullable members)
equal to its default value has its presence bit cleared. The reader then returns the default compiled into its own
header. Inline structs use C layout: fields at their natural alignment, padding bytes zero, `bool` as one byte (0 or
1).

## Strings

`[u32 byteLength][UTF-8 bytes][0]`, starting at a 4-byte boundary. The terminating zero is not counted in
`byteLength`.

## Vectors

`[u32 count][elements]`. The element area starts at a multiple of `max(4, element alignment)`, so the count sits
directly in front of it.

| Element kind | Element encoding |
|---|---|
| Bool | 1 byte, 0 or 1 |
| scalars, enums | the value (element size = cell size) |
| Struct | the struct's bytes; element stride = struct size |
| String, Object, Vector, SharedStruct | offset (relative to the element's own position); 0 = null element |
| Union | `u32` tag, then an offset relative to its own position (element + 4); both 0 = null element |

**Optional values.** A vector of nullable scalars, enums or inline structs (C# `List<int?>`, `Vec3?[]`) is `[u32
count][u32 presence[ceil(count / 32)]][values]`. Presence bit `i` (word `i / 32`, position `i % 32`) says whether
element `i` has a value; every element has a value slot, zero bytes when absent, so element `i` is still at a fixed
position. The values start at a multiple of `max(4, element alignment)`, with the presence words directly in front.
Unused presence bits are zero. The vector's schema entry has flags bit 1 set.

## Dictionaries

A dictionary is an object with two vector members: `Keys`, in ascending order, and `Values`, in the same order.
Integer, char and enum keys are ordered by value; string keys by code point, which is the order of their UTF-8
bytes. The schema describes it like any object, so readers of other versions match `Keys` and `Values` by name. A
reader finds a key by binary search; a buffer whose keys are not sorted reads safely but finds the wrong values.

## Unions

A union value is a reference to an object of one of the union's member types. The tag identifies the member type:
it is `xxHash32` (seed 0) of the type's stable name. That name is the `[Tessera("name")]` argument, or else the C# type
name, with containing types joined by `.` and generic arguments in `<...>`. Tag 0 never denotes a member.

## Sharing

With `Sharing.Strings` (the default), equal strings are written once and referenced from every use. With
`Sharing.All`, equal strings, vectors, shared structs and objects are written once (compared by content). The buffer
is then a DAG. Readers do not care, and verification checks each shared item once (see below).

## Schema section

Present when header flag bit 0 is set. It lets a reader built from a different version of the models match members
by name hash.

```
u32 byteLength          // of the whole section
u16 entryCount
u16 rootEntry           // index of the root object's entry
u32 entryOffset[entryCount]   // from the section start
entries...
```

The section contains only the types the buffer uses: the root, the types reachable from it, and the union member
types actually written. Every entry starts with a 24-byte header:

```
u8  category            // 1 object, 2 struct, 3 vector, 4 union
u8  flags               // objects: bit 0 = has fixed cells; otherwise 0
u16 count               // objects: member count; structs: field count; unions: member count; vectors: 0
u32 typeId              // xxHash32 of the stable type name
u64 fingerprint         // own layout
u64 deepFingerprint     // layout of everything reachable
```

followed by:

- **Object:** `u16 W`, `u16 cellCount` (cells with a presence bit), then, only when flags bit 0 is set, `u16
  fixedCount` and `u16 F`. Then one 12-byte record per member in wire order (fixed cells, cells, bools): `u64
  nameHash`, `u8 kind`, `u8 flags` (bit 0 = nullable, bit 1 = fixed cell), `u16 typeRef`.
- **Struct:** `u16 size`, `u8 alignment`, `u8 0`.
- **Vector:** `u8 elementKind`, `u8 flags` (bit 0 = elements may be null, bit 1 = optional values), `u16
  elementTypeRef`.
- **Union:** per member: `u32 tag`, `u16 typeRef`, `u16 0`.

A `typeRef` is an entry index, or `0xFFFF` for none (scalars, strings, bools). Member identity is `nameHash`:
`xxHash64` (seed 0) of the UTF-8 wire name. That is the C# member name, or the `[TesseraName]` argument. Union tags
stay 32-bit.

## Fingerprints

All fingerprints use `xxHash64` with seed 0.

- **Own fingerprint:** a hash of the type's own layout. That is its category plus, for objects, `W`, the cell count,
  the bool count and, per member, the name hash, kind and cell size (and the hashed struct shape for struct cells).
  For structs it is the shape (size, alignment, and per field the name hash, kind, offset and nested shapes). For
  vectors it is the element kind (and struct shape). For unions it is the member tags.
- **Deep fingerprint:** a hash of the own fingerprints of every type reachable from the type, numbered in DFS order,
  together with the reference structure.

A reader whose compiled deep fingerprint equals the buffer's uses compile-time positions throughout. Otherwise it
binds its schema against the buffer's schema section: members are matched by name hash and must have the same kind
(and struct shape). Unmatched members read as absent. The binder compares layouts structurally and never trusts the
fingerprints stored in the buffer.

## Verification

`tessera::Reader` (and `tessera::verify`) check, before any access:

- **Header:** size, magic, version, reserved flag bits and alignment.
- **Schema section:** fully parsed and bounds-checked, if used.
- **Objects:** every object's header words, presence bits and cells lie in bounds.
- **Offsets:** non-zero where required, forward, in bounds and aligned to their target's alignment.
- **Strings and vectors:** lengths fit the buffer; strings are zero-terminated, and valid UTF-8 if `Options::utf8`.
- **Union tags:** belong to the union.
- **Limits:** nesting at most `max_depth` levels (each object and each vector is one level), and at most
  `max_items` objects and vectors visited.

References only point forward, so the items form a DAG. A verifier that walks it as a tree visits a shared item once
per path to it, which grows exponentially with nesting. The reference verifier walks the tree until it has visited more
items than the buffer has room for (size / 4, the smallest item being 4 bytes), or validated more than 16 × size bytes
of UTF-8: either proves that items are shared. It then verifies the buffer again, each (position, type) pair once,
remembering the height of its subtree, so a later visit only checks that the subtree fits under `max_depth` from
there. `max_items` then counts the items verified. Buffers that share little never reach either limit.

After a successful verification, every accessor is a plain bounds-free load.
