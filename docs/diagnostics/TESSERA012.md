# TESSERA012: Strict mode: a saved class isn't marked

**Error.** `'Weapon' is a model but has no [Tessera] and is not listed in [assembly: TesseraRoot];
TesseraRequireAttribute requires every model class, record and struct to be marked`

**What it means:** the project turned on strict mode (`<TesseraRequireAttribute>true</TesseraRequireAttribute>`),
which requires every class that gets saved to be marked, so that nothing enters your saved data unannounced. This
class would be saved, because your code saves it or uses it as a field type, but it isn't marked.

## Fix

If it belongs in your saved data, mark it:

```csharp
[Tessera]
public class Weapon { ... }
```

or list it in [`[assembly: TesseraRoot(typeof(Weapon))]`](../attributes/tessera-root.md). If it doesn't, remove the
field that uses it, or leave that field out with [`[TesseraIgnore]`](../attributes/tessera-ignore.md).

Enums, plain structs and abstract base classes need no mark.

See also: [Strict mode](../guide/generated-types.md#strict-mode).
