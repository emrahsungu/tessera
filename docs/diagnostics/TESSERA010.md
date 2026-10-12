# TESSERA010: Model type not visible at this call

**Info.** `This call serializes the type parameter 'T', so the generator cannot tell which models it writes; types used
only through it get no writer unless they have [Tessera], are listed in [assembly: TesseraRoot(typeof(...))] or are
serialized elsewhere with their concrete type`

A `Serialize` or `Write` call serializes a type parameter:

```csharp
public static byte[] Save<T>(T value) => TesseraSerializer.Serialize(value);
```

The generator finds models by looking at calls whose type the compiler knows. This one could receive any type, so the
generator learns nothing from it. That is fine when the types that reach it are models anyway; when one is not, the
call throws a `TesseraException` at run time, naming the type.

## Fix

Make sure every type that reaches the call is a model:

- list them: [`[assembly: TesseraRoot(typeof(SaveGame), typeof(Settings))]`](../attributes/tessera-root.md);
- or mark them [`[Tessera]`](../attributes/tessera.md);
- or serialize them somewhere with their concrete type, which makes them models too.

When that is done, silence the message for the project if you like: `<NoWarn>$(NoWarn);TESSERA010</NoWarn>`.

See also: [Generic code](../guide/models.md#generic-code).
