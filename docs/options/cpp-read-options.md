# C++ read options

When C++ opens a buffer, it can validate it first. Validation reads the whole buffer once and checks that every
reference, length and type ID in it is sane, so that reading fields afterwards can never touch memory outside the
buffer. You choose how much checking to do:

| You write | Checks | Use it for |
|---|---|---|
| `tessera::Reader<T> r(data, size);` | everything (the default) | data from outside your program: files, the network, mods, user content |
| `tessera::Reader<T> r(data, size, options);` | everything, with your limits | the same, with [UTF-8 checks or other limits](#options) |
| `tessera::Reader<T> r(data, size, tessera::trusted);` | the 16-byte header only | data your own program wrote and nobody else can change |
| `T root = tessera::root_unchecked<T>(data);` | nothing | data your own program just wrote, in memory, with exactly these classes |
| `tessera::Error e = tessera::verify<T>(data, size);` | everything, without keeping a reader | checking files in a tool, a server or a test |

## Options

```cpp
tessera::Options options;
options.utf8 = true;              // also check that every string is valid UTF-8 (default: false)
options.max_depth = 64;           // the deepest nesting of objects and lists (default: 128)
options.max_items = 100'000;      // the most objects and lists to check (default: 16,777,216)
tessera::Reader<game::Save> reader(data, size, options);
```

### `verify` (default: `true`)

Validate the whole buffer before the first read. Turn it off only through `tessera::trusted` (below).

Validation walks the buffer once, without copying: about 7–34 microseconds per 100 KB of objects in the
[benchmarks](../BENCHMARKS.md), and almost nothing for large arrays of numbers (1 µs for canada's 895 KB of
coordinates). After it, every field read is a plain memory load with no checks.

### `utf8` (default: `false`)

Also check that every string is valid UTF-8, and reject the buffer with `Error::BadString` otherwise.

**Use it when** strings go to code that requires valid UTF-8: a JSON writer, a UI text renderer, a database, a
network protocol; or when the data comes from people who might hand-edit it. The C# writer always writes valid UTF-8,
so this only catches corrupted or forged data.

**Cost:** it reads every byte of every string. In the benchmarks, validation took up to 2.4 times as long on
text-heavy data, and no longer on numeric data.

### `max_depth` (default: `128`)

The deepest nesting of objects and lists to accept; deeper buffers fail with `Error::TooDeep`. It keeps hostile data
from exhausting the stack. Keep it equal to the C# writer's [`MaxDepth`](max-depth.md): raise both for deep trees, or
lower this one for input you know is shallow.

### `max_items` (default: 2<sup>24</sup> = 16,777,216)

The most objects and lists to check; more fail with `Error::TooManyItems`. It bounds validation time for hostile
buffers built from millions of tiny objects, or from a few objects shared in many places. Raise it for legitimate
buffers with more than 16 million objects and lists.

## Skipping validation

### `tessera::trusted`

```cpp
tessera::Reader<game::Cache> reader(data, size, tessera::trusted);
```

Checks the header only, not the contents. A buffer written by another version of your classes is still matched and
converted.

**Use it for** data your own program wrote and nobody else can change: a cache in the program's own folder, a file
embedded in the executable, data received from another part of the same process.

**Never use it** for files users can reach, downloads or network data: invalid data then leads to reads outside the
buffer.

### `tessera::root_unchecked<T>(data)`

```cpp
game::Snapshot s = tessera::root_unchecked<game::Snapshot>(buffer.data());
```

No checks at all: it returns the top-level object directly, like `flatbuffers::GetRoot`. The buffer must have been
written with exactly the classes of this header.

**Use it for** the tightest loops over data your program just produced, in memory, such as snapshots taken and read
within one run.

### `tessera::verify<T>(data, size, options)`

```cpp
if (tessera::verify<game::Save>(data, size) != tessera::Error::None) reject(file);
```

Validates a buffer and returns the result, without keeping a `Reader` and without converting a buffer of another
version.

**Use it for** checking data without reading it: an upload endpoint on a server, an import step in a tool, a test.
