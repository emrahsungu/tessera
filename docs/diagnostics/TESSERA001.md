# TESSERA001: Unsupported member type

**Error.** `Member 'Created' of 'SaveGame' has type 'DateTime', which Tessera cannot serialize: DateTime has no C++
equivalent; store DateTime.Ticks (or ToBinary()) as a long`

A member's type has no form that C++ can read. The message ends with the reason and, for common types, what to store
instead.

## Fix

Store a supported type, and keep the convenient one for .NET code if you like:

```csharp
public class SaveGame
{
    public long CreatedTicks;                       // stored

    [TesseraIgnore]                                 // not stored
    public DateTime Created
    {
        get => new DateTime(CreatedTicks, DateTimeKind.Utc);
        set => CreatedTicks = value.ToUniversalTime().Ticks;
    }
}
```

Or leave the member out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md) if C++ does not need it.

| Type | Reason | Store instead |
|---|---|---|
| `DateTime`, `TimeSpan` | no C++ equivalent | `Ticks` as a `long` |
| `DateTimeOffset` | no C++ equivalent | `UtcTicks` as a `long`, and the offset separately |
| `Guid` | no C++ equivalent | a string, or your own 16-byte plain struct |
| `decimal` | no C++ equivalent | a `double`, a scaled `long`, or a string |
| `nint`, `nuint` | no fixed size on the wire | `int` or `long` |
| `Half`, `Int128`, `UInt128` | no portable C++ equivalent | `float`, two `long`s |
| `T[,]` | only single-dimensional arrays are supported | nested lists, or a flat array |
| `object`, open generic types | use a concrete model type | a model, or an abstract base class for a union |
| delegates | delegates cannot be serialized | nothing: leave the member out |

See also: [Types](../guide/types.md).
