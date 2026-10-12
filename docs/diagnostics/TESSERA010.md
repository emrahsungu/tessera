# TESSERA010: A generic method hides which types it saves

**Info.** `This call saves the type parameter 'T', so the generator cannot tell which classes it saves; a class saved
only through it gets no serializer unless it has [Tessera], is listed in [assembly: TesseraRoot(typeof(...))], or is
saved elsewhere with its real type`

**What it means:** a generic method passes its type parameter to `Serialize`:

```csharp
public static byte[] Save<T>(T value) => TesseraSerializer.Serialize(value);
```

The generator decides which classes get a serializer by looking at `Serialize` calls whose type the compiler knows.
This call could receive any type, so the generator learns nothing from it. That's fine as long as every type that
reaches the call gets a serializer for another reason. If one doesn't, the call throws a `TesseraException` at run
time, naming the type.

## Fix

Make sure every type that reaches the call gets a serializer:

- list them: [`[assembly: TesseraRoot(typeof(SaveGame), typeof(Settings))]`](../attributes/tessera-root.md);
- or mark them [`[Tessera]`](../attributes/tessera.md);
- or call `Serialize` somewhere with their real type, which also generates their serializers.

When that's done, you can hide the message for the project: `<NoWarn>$(NoWarn);TESSERA010</NoWarn>`.

See also: [Generic code](../guide/generated-types.md#generic-code).
