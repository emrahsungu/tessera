# TESSERA009: Union tag is zero

**Error.** `Type 'Rune' hashes to union tag 0, which means 'no value'; give it a different [Tessera("name")]`

A union member's tag is a 32-bit hash of its type name. Tag 0 is reserved: it marks a union member that is null. One
type's name happens to hash to 0 (about one name in four billion).

## Fix

Give the type another stable name, which changes only its tag:

```csharp
[Tessera("RuneItem")]
public sealed class Rune : Item { ... }
```

See also: [`[Tessera]`](../attributes/tessera.md).
