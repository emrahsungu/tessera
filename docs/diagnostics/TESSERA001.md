# TESSERA001: A field's type can't be saved

**Error.** `Member 'Created' of 'SaveGame' has type 'DateTime', which Tessera cannot serialize: DateTime has no C++
equivalent; store DateTime.Ticks (or ToBinary()) as a long`

**What it means:** a field's type has no form that C++ can read. The message ends with the reason and, for common
types, what to save instead.

## Fix

Save a supported type instead, and keep the convenient one for your C# code if you like:

```csharp
public class SaveGame
{
    public long CreatedTicks;                       // saved

    [TesseraIgnore]                                 // not saved
    public DateTime Created
    {
        get => new DateTime(CreatedTicks, DateTimeKind.Utc);
        set => CreatedTicks = value.ToUniversalTime().Ticks;
    }
}
```

Or leave the field out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md) if C++ doesn't need it.

| Type | Reason | Save instead |
|---|---|---|
| `DateTime`, `TimeSpan` | C++ has no equivalent | `Ticks` as a `long` |
| `DateTimeOffset` | C++ has no equivalent | `UtcTicks` as a `long`, and the offset separately |
| `Guid` | C++ has no equivalent | a string, or your own 16-byte plain struct |
| `decimal` | C++ has no equivalent | a `double`, a scaled `long`, or a string |
| `nint`, `nuint` | their size differs between machines | `int` or `long` |
| `Half`, `Int128`, `UInt128` | no portable C++ equivalent | `float`, two `long`s |
| `T[,]` | only one-dimensional arrays are supported | nested lists, or a flat array |
| `object`, open generic types | the generator needs a concrete class | a class, or an abstract base class with derived classes |
| delegates | not data | nothing: leave the field out |

See also: [Supported types](../guide/supported-types.md).
