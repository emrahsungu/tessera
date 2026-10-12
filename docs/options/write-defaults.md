# `WriteDefaults`

**What it does:** writes every non-nullable field, even when it equals its default value. Default: `false`.

```csharp
byte[] data = TesseraSerializer.Serialize(settings, new TesseraOptions { WriteDefaults = true });
```

## How it works

By default, a field equal to its default value (its initializer, its `[DefaultValue]`, or zero) is not written, and
the C++ reader returns the default it was compiled with. That keeps buffers small, but it means the default is
effectively part of the data format. With `WriteDefaults = true`, every non-nullable field is written, so the buffer
contains every value and doesn't depend on anyone's defaults.

## Use it when

- **defaults may change** between versions of your program, and saved data must keep the values it had. A field left
  out because it equaled the old default would read as the new default
  ([Default values](../attributes/default-value.md#changing-a-default));
- **the reader may be built with different defaults**, for example another program or another version that shares
  the data;
- **C++ must see `has_x()` true for every field** of the objects you save, for example to tell "set to 0" from "not
  set" in an editor;
- **you inspect the data**, for example with `tessera::to_json`, and want to see every value.

## When not to use it

When defaults are stable, it only makes buffers larger: each field equal to its default takes its full size.

## Details

- Nullable fields (`int?`, strings, objects, lists) are unaffected: null is never written, other values always are.
- To write the default of only some fields, always, mark them [`[TesseraKeepDefault]`](../attributes/tessera-keep-default.md)
  instead; that also stores number, enum and struct fields at a fixed position, which C++ reads faster.
