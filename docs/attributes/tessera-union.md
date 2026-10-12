# `[TesseraUnion]`

```csharp
[TesseraUnion(typeof(Potion), typeof(Key))]      // on the base type: every member of this type
public abstract class Item { }

public class Chest
{
    public Item? Loot;
    [TesseraUnion(typeof(Potion))]                // on one member: only potions here
    public Item? Reward;
}
```

On a class or an interface, or on a field or property. Lists the concrete types that a union member may hold. The
lists add up: every `[TesseraUnion]` on the member, and every one on its type.

Without it, a member whose type is an abstract class or an interface may hold every public or internal, non-generic
concrete type of the project that derives from (or implements) that type, plus every such type of a referenced project
that marks it [`[Tessera]`](tessera.md) or lists it in [`[assembly: TesseraRoot]`](tessera-root.md).

## Use it when

- **Only some derived types belong in the buffer**: test doubles, editor-only subclasses, types meant for another
  file format.
- **The derived types live in a project that does not mark them**, and you cannot change it: list them here
  ([TESSERA003](../diagnostics/TESSERA003.md)).
- **You want the list explicit**, so that adding a class to the project does not silently add it to the wire format.
- **A concrete base class should be polymorphic.** Members whose type is a non-abstract class are plain objects of
  exactly that class; with `[TesseraUnion]` on the class, they become unions of the listed types. List the base class
  itself too if instances of it are allowed.

## Details

- On a collection member (`List<Item>`, `Item[]`), the list applies to the elements.
- On a member, the C++ union view is a type of its own, named after the allowed types (`Item_Potion` for `Reward`
  above), because it holds fewer types than the base's view.
- List types that derive from (or implement) the member's type.
- Each type in a union is told apart by its tag, a hash of its type name: see [Unions](../guide/types.md#unions) and
  [`[Tessera("Name")]`](tessera.md).
- Removing a type from a union makes buffers that hold it read as an unknown type in the new version (`type()`
  outside the enum, every `as_x()` empty). Adding a type is safe for old readers in the same way.
