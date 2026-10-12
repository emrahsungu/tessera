# TESSERA004: Union tag collision

**Error.** `Types 'Game.Items.Key' and 'Game.Doors.Key' in union 'Item' have the same type id; give one of them a
different [Tessera("name")]`

Each type in a union is told apart by its tag: a 32-bit hash of its type name, without the namespace. Two types in one
union have the same name in different namespaces (or, rarely, two names have the same hash).

## Fix

Give one of them a different stable name:

```csharp
namespace Game.Doors;

[Tessera("DoorKey")]
public sealed class Key : Item { ... }
```

The name only changes the tag. If buffers already hold values of that type, give the new name to the type that did not
exist when they were written, so the existing tag stays with the existing type.

See also: [`[Tessera]`](../attributes/tessera.md), [Unions](../guide/types.md#unions).
