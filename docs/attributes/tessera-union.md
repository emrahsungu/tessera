# `[TesseraUnion]`

**What it does:** lists which derived classes a field may hold when the field is declared as a base class or an
interface.

```csharp
[TesseraUnion(typeof(Potion), typeof(Key))]      // on the base class: every field of this type
public abstract class Item { }

public class Chest
{
    public Item? Loot;
    [TesseraUnion(typeof(Potion))]                // on one field: only potions here
    public Item? Reward;
}
```

Without the attribute, a field declared as an abstract class or an interface may hold every public or internal,
non-generic derived class of the project, and every derived class of another project that is marked
[`[Tessera]`](tessera.md) or listed in [`[assembly: TesseraRoot]`](tessera-root.md).

The lists add up: all `[TesseraUnion]` attributes on the field and all on its declared type count.

## When you need it

- **Only some derived classes belong in saved data**: test doubles, editor-only subclasses, classes meant for another
  file format.
- **The derived classes are in another project that doesn't mark them**, and you can't change it: list them here
  ([TESSERA003](../diagnostics/TESSERA003.md)).
- **You want the list explicit**, so that adding a class to the project doesn't silently add it to saved data.
- **The base class is not abstract**: a field of a concrete class type normally holds exactly that class. With
  `[TesseraUnion]` on the class, such fields may hold the listed classes. List the base class too if it is allowed
  itself.

## Details

- On a list or array field (`List<Item>`), the attribute applies to the elements.
- On a single field, the C++ header gets a separate accessor class for it, named after the allowed classes
  (`Item_Potion` for `Reward` above).
- List only classes that derive from (or implement) the field's declared type.
- Each derived class is identified in the buffer by a 32-bit ID, a hash of its class name: see
  [`[Tessera("Name")]`](tessera.md).
- Removing a class from the list makes saved values of that class read as an unknown type (in C++, `type()` returns a
  value outside the enum, and every `as_x()` is empty). Adding a class is safe for old readers in the same way.
