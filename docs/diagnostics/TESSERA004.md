# TESSERA004: Two derived classes would get the same type ID

**Error.** `Classes 'Game.Items.Key' and 'Game.Doors.Key', both saved in fields of type 'Item', would get the same
type ID (a hash of the class name); give one of them a different [Tessera("name")]`

**What it means:** a field declared as a base class or interface can hold several derived classes, so saved data
records which class each value is, with a 32-bit ID: a hash of the class name, without its namespace. Two of the
derived classes have the same name in different namespaces (or, rarely, two names have the same hash), so they would
get the same ID.

## Fix

Give one of them a different saved name:

```csharp
namespace Game.Doors;

[Tessera("DoorKey")]
public sealed class Key : Item { ... }
```

The name only changes the ID. If data has already been saved, rename the class that didn't exist back then, so the
existing ID stays with the existing class.

See also: [`[Tessera]`](../attributes/tessera.md),
[Fields declared as a base class or interface](../guide/supported-types.md#fields-declared-as-a-base-class-or-interface).
