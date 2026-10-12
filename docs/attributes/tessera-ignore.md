# `[TesseraIgnore]`

```csharp
public class Monster
{
    public string? Name;

    [TesseraIgnore]
    public Texture? CachedTexture;   // runtime only: not written, not in the C++ header
}
```

On a field or property. Leaves the member out: it is not written, it has no accessor in the C++ header, and it is not
part of the schema. `[IgnoreDataMember]` (System.Runtime.Serialization) and `[NonSerialized]` (on fields) do the
same, so models shared with other serializers need no second attribute.

## Use it when

- **The member is derived** from others (a cached length, a lookup table built at load time).
- **The member only makes sense at run time**: handles, references to engine objects, locks, events.
- **The member's type is not supported** ([TESSERA001](../diagnostics/TESSERA001.md)) and you don't need it on the
  C++ side, or you store it another way (a `DateTime` as a `long` of ticks, next to it).

## Details

Only public members, and internal ones of the same assembly, are serialized anyway; private and protected members need
no attribute. Ignoring a member that old buffers contain is like deleting it: readers of the new version skip it
([Schema evolution](../guide/schema-evolution.md)).
