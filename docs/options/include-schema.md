# `IncludeSchema`

**What it does:** stores a description of your classes in the buffer: every field's name (as a hash) and type, about
12 bytes per field and 32 per class. With it, a program built from an older or newer version of your classes can still
read the buffer. Default: `true`.

```csharp
byte[] data = TesseraSerializer.Serialize(state, new TesseraOptions { IncludeSchema = false });
```

## How it works

Every buffer records a hash of the layout of its classes. When a C++ reader opens a buffer:

- **The hash matches its own** (same version of the classes): it reads the buffer directly, schema or not.
- **The hash differs, and the buffer has a schema**: it matches fields by name, once, converts the buffer to its own
  layout, then reads it at full speed. Fields it doesn't know are skipped; fields missing from the buffer read as
  missing ([Changing your classes](../guide/versioning.md)).
- **The hash differs, and there is no schema**: it refuses the buffer with `Error::SchemaMismatch`.

## Keep it (`true`, the default) when

- **the data is saved and loaded later**: save games, settings, content files, caches that outlive a program update;
- **the writer and the reader are updated separately**: a game and its tools, a client and a server, a game and its
  mods, a newer editor writing data an older build must read.

## Leave it out (`false`) when

- **the writer and the reader are always built from the same source at the same time**, so the layout hashes always
  match: messages between two parts of one program, temporary files, caches deleted on every update;
- **buffers are small and many**: the description has a fixed size per class, so for small buffers it can be most of
  the bytes. In the [first example](../guide/getting-started.md), the monster takes 328 bytes with it and 104 without.

## Details

- Reordering fields doesn't change the layout hash, so it works with or without the schema. Adding, removing or
  changing fields breaks buffers without a schema.
- The schema's size depends on the classes, not on the data: for large buffers it is negligible.
