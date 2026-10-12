# TESSERA009: A class name hashes to the reserved type ID 0

**Error.** `The name of class 'Rune' hashes to type ID 0, which is reserved for null; give it a different
[Tessera("name")]`

**What it means:** in a field declared as a base class or interface, saved data records which derived class each value
is with a 32-bit ID: a hash of the class name. The ID 0 is reserved for a null value, and this class's name happens to
hash to 0 (about one name in four billion).

## Fix

Give the class another saved name; it changes only its ID:

```csharp
[Tessera("RuneItem")]
public sealed class Rune : Item { ... }
```

See also: [`[Tessera]`](../attributes/tessera.md).
