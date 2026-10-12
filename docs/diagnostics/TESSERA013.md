# TESSERA013: A private or protected class can't be saved

**Error.** `'Inventory.Slot' cannot be saved because the generated serializers cannot access it; it and the classes it
is nested in must be public or internal (not private, protected or file-local)`

**What it means:** the generated serializers are ordinary code in your assembly, outside your classes, so they can
only use classes that such code can access. A private or protected nested class, a `file`-local class, or any class
nested inside one of those, is out of their reach.

## Fix

Make the class, and every class it is nested in, `internal` or `public`:

```csharp
public class Inventory
{
    internal sealed class Slot { ... }   // was: private
}
```
