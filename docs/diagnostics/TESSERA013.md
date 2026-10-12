# TESSERA013: Model is not accessible

**Error.** `'Inventory.Slot' cannot be a model because the generated writers cannot see it; it and the types that
contain it must be public or internal (not private, protected or file-local)`

The generated serializers are ordinary code in your assembly, so they can only use types that code outside the type
can see. A private or protected nested type, or a `file`-local type, is out of their reach, and so is any type nested
in one.

## Fix

Make the type, and every type it is nested in, `internal` or `public`:

```csharp
public class Inventory
{
    internal sealed class Slot { ... }   // was: private
}
```
