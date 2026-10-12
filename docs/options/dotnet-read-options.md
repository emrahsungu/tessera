# .NET read options

`MonsterView.Read`, `TryRead`, `TesseraSerializer.Deserialize` and `TryDeserialize` take a `TesseraReadOptions`. Before
reading, they can check the buffer: that every reference, length, string and type ID in it is sane, so that no read
afterwards can go outside the buffer. You choose how much to check:

| You write | Checks | Use it for |
|---|---|---|
| `MonsterView.Read(buffer)` | everything (the default) | data from outside your program: files, the network, mods, user content |
| `MonsterView.Read(buffer, options)` | everything, with your limits | the same, with [UTF-8 checks or other limits](#options) |
| `MonsterView.Read(buffer, TesseraReadOptions.Trusted)` | the 16-byte header only | data your own program wrote and nobody else can change |

`TesseraReadOptions` is an immutable record: `TesseraReadOptions.Default` holds the defaults; make variants with
`with`, or create one:

```csharp
var options = new TesseraReadOptions
{
    ValidateUtf8 = true,   // also check that every string is valid UTF-8 (default: false)
    MaxDepth = 64,         // the deepest nesting of objects and lists (default: 128)
    MaxItems = 100_000,    // the most objects and lists to check (default: 16,777,216)
};
SaveView save = SaveView.Read(buffer, options);
```

The same options do the same for C++: [C++ read options](cpp-read-options.md).

## Options

### `Verify` (default: `true`)

Check the whole buffer before the first read. It reads the buffer once, without copying it: in the
[benchmarks](../BENCHMARKS.md), 4–57 microseconds for buffers of 33–580 KB of objects, and 2 µs for canada's 895 KB of
coordinates. After it, each field is read without checks. `Deserialize` checks as it creates the objects, in the same
pass.

`Verify = false` (or `TesseraReadOptions.Trusted`) checks the 16-byte header only. A buffer written by another
version of your classes is still matched by name and converted.

**Use it** for data your own program wrote and nobody else can change: a cache in the program's own folder, a file
embedded in the program, data passed between parts of one process.

**Never use it** for files users can reach, downloads or network data: damaged data could then make a read go
outside the buffer, which can crash the process or read unrelated memory.

### `ValidateUtf8` (default: `false`)

Also check that every string is valid UTF-8, and reject the buffer with `TesseraReadError.BadString` otherwise.

Without it, strings are not checked: reading one as a `string` turns invalid bytes into the replacement character
(U+FFFD), and its `Utf8` bytes are returned as they are.

**Use it when** strings go to code that requires valid UTF-8 bytes: a JSON writer, a database, a network protocol;
or when the data comes from people who might edit it by hand. The C# writer always writes valid UTF-8, so this only
catches damaged or forged data.

**Cost:** it reads every byte of every string.

### `MaxDepth` (default: `128`)

The deepest nesting of objects and lists to accept, counting the top-level object as 0; deeper buffers fail with
`TesseraReadError.TooDeep`. It keeps hostile data from making the check, or `Deserialize`, recurse deeper than the
stack allows. Keep it equal to the writer's [`MaxDepth`](max-depth.md): raise both for deep trees, or lower this one for
input you know is shallow.

Whatever its value, nesting deep enough to exhaust the thread's stack fails with `TooDeep` instead of ending the
process.

### `MaxItems` (default: 2<sup>24</sup> = 16,777,216)

The most objects and lists to check; more fail with `TesseraReadError.TooManyItems`. It bounds the time spent on hostile
buffers built from millions of tiny objects, or from a few objects shared in many places (a buffer written with
[`Sharing.All`](sharing.md) that shares heavily is checked once per stored item). Lower it for untrusted input whose
size you know; raise it for legitimate buffers with more than 16 million objects and lists.
