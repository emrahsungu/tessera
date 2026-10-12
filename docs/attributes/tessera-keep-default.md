# `[TesseraKeepDefault]`

**What it does:** always writes the field, even when it equals its default value. A number, enum or plain-struct
field is then also stored at a fixed position in the object, so C++ reads it with a single memory load.

```csharp
public class Sample
{
    [TesseraKeepDefault] public long Timestamp;
    [TesseraKeepDefault] public double Value;
    public int Status;                 // usually 0: better left out when it is
    public string? Note;
}
```

Normally, each object records which fields were written (one bit per field), and leaves out fields equal to their
default. Reading a field then means checking its bit and computing where it is. A `[TesseraKeepDefault]` field is
always there, at the same position, so neither step is needed.

## When you need it

- **The field is almost always set**: timestamps, IDs, positions, measured values. Leaving out the rare default saves
  nothing. On the benchmark's time series, marking the always-set fields made random reads 20–33% faster and
  validation up to 7% faster, at the same size.
- **The field's default may change.** Saved data leaves out fields equal to the default, and readers fill in the
  default they were compiled with. A field that is always written doesn't depend on it
  ([Changing a default](default-value.md#changing-a-default)).
- **C++ must know whether the value was set, even when it equals the default**: `has_x()` is then true for all data
  saved by a version of the class that has the attribute.

## When not to use it

- On fields that usually equal their default, such as flags or counters that are mostly zero: each one then takes its
  full size in every object.
- On nullable fields: they already save every non-null value.

## Details

- Only numbers, enums and plain structs get a fixed position. On a string, object or list field, the attribute only
  means "write it even when it equals its default".
- Adding or removing the attribute changes the class's binary layout. Data saved with the other version still loads:
  the reader converts it once when opening it ([Changing your classes](../guide/versioning.md)).
- `WriteDefaults = true` writes every field's default, for one save; the attribute does it for one field, always,
  and adds the fixed position ([`WriteDefaults`](../options/write-defaults.md)).
