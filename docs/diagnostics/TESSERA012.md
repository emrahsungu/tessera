# TESSERA012: Model is not marked

**Error.** `'Weapon' is a model but has no [Tessera] and is not listed in [assembly: TesseraRoot];
TesseraRequireAttribute requires every model class, record and struct to be marked`

The project turned on strict mode (`<TesseraRequireAttribute>true</TesseraRequireAttribute>`), which requires every
model to be declared, so that no type joins the wire format unannounced. This type became a model without a mark:
something serializes it, or a model uses it.

## Fix

Mark it, if it belongs in the wire format:

```csharp
[Tessera]
public class Weapon { ... }
```

or list it in [`[assembly: TesseraRoot(typeof(Weapon))]`](../attributes/tessera-root.md). If it should not be in the
wire format, remove the member that uses it, or leave that member out with
[`[TesseraIgnore]`](../attributes/tessera-ignore.md).

Enums, plain structs and abstract union bases need no mark.

See also: [Strict mode](../guide/models.md#strict-mode).
