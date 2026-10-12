# `[TesseraIgnore]`

**What it does:** does not save the field or property. It gets no accessor in the C++ header either.

```csharp
public class Monster
{
    public string? Name;

    [TesseraIgnore]
    public Texture? CachedTexture;   // runtime only
}
```

`[IgnoreDataMember]` (System.Runtime.Serialization) and `[NonSerialized]` (on fields) do the same, so classes that
other serializers also use need no second attribute.

## When you need it

- **The value is derived** from other fields: a cached length, a lookup table built when loading.
- **The value only makes sense while the program runs**: handles, references to engine objects, locks, events.
- **The type can't be saved** ([TESSERA001](../diagnostics/TESSERA001.md)) and C++ doesn't need it, or you save it
  another way (a `DateTime` as a `long` of ticks, in a second field).

## Details

Private and protected fields are never saved, so they need no attribute. Only public fields and properties are, and
internal ones of the same assembly.

Ignoring a field that old saved data contains works like deleting it: new code skips it
([Changing your classes](../guide/versioning.md)).
