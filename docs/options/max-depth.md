# `MaxDepth`

**What it does:** limits how deeply objects and lists may nest. Saving deeper data throws a `TesseraException`
instead. Default: `128`.

```csharp
byte[] data = TesseraSerializer.Serialize(tree, new TesseraOptions { MaxDepth = 1024 });
```

## How it works

The writer counts nesting levels as it goes. Every object and every list is a level, and the object you pass to
`Serialize` is level 1: a `World` holding a `List<Monster>` whose monsters hold a `List<Weapon>` is 5 levels deep at
the weapons. The number of elements in a list doesn't matter, only how deep the nesting goes.

A cycle of references (an object that contains itself, directly or through others) would nest forever, so this limit
also turns cycles into an exception: *Object graph is deeper than MaxDepth (128 nested objects and vectors); it may
contain a cycle.*

The C++ reader has the same limit, `Options::max_depth`, also 128 by default, and rejects deeper buffers with
`Error::TooDeep` ([C++ read options](cpp-read-options.md)).

## Raise it when

- **your data is a deep tree stored as nested objects**: a scene graph whose nodes hold their children, a nested menu,
  an expression tree. Raise **both** limits, the C# `MaxDepth` and the C++ `max_depth`, to the same value, or C++
  rejects what C# saved.

If the nesting depth grows with the data (a linked list, or a chain of nodes), consider storing the nodes in one flat
list with parent indexes instead: that is shallow, compact, and faster to read.

## Lower it when

- **you want deep data to fail early**, for example user-generated content that should never be deep.

## Details

Cycles are always an error: a buffer can't contain one. To save a graph with cycles, store the nodes in a list and
the links as indexes into it.
