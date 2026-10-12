# `[TesseraKeepDefault]`

```csharp
public class Sample
{
    [TesseraKeepDefault] public long Timestamp;
    [TesseraKeepDefault] public double Value;
    public int Status;                 // usually 0: better left out when it is
    public string? Note;
}
```

On a non-nullable field or property. Always stores the member, even when it equals its default. Scalars, enums and
plain structs marked with it become **fixed cells**: they come first in the object, at constant positions, without a
presence bit, so C++ reads one with a single load.

## Use it when

- **The member is almost always set**: timestamps, ids, positions, values. Leaving out the rare default saves nothing,
  and the presence bit costs a little on every read. On the benchmark's time series, marking the always-set values
  made random reads 20–33% faster and verification up to 7% faster, at the same size.
- **The member's default may change.** A member equal to its default is normally not stored, and readers fill in the
  default they were compiled with, so changing a default changes what old buffers read. A member that is always
  stored is immune ([Defaults](default-value.md#changing-a-default)).
- **C++ must tell "set to the default" from "not set"** for a non-nullable value: with the attribute, `has_x()` is
  true whenever the buffer was written by a version that had the member.

## When not to use it

- On members that usually equal their default, such as flags or counters that are mostly zero: each one then takes
  its full size in every object.
- On nullable members: they already store every non-null value, and null means absent.

## Details

- Only scalars, enums and plain structs become fixed cells. On strings, objects and vectors, the attribute only means
  "store even when equal to the default".
- Adding or removing the attribute changes the type's layout, so readers built before the change translate buffers
  written after it (and vice versa), matching the member by name, once when the buffer is opened
  ([Schema evolution](../guide/schema-evolution.md)).
- `WriteDefaults = true` stores every member's default for one write; the attribute does it for one member, always,
  and adds the fixed position ([Writing in .NET](../guide/writing.md#writedefaults)).
