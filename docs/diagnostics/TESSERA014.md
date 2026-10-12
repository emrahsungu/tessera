# TESSERA014: This Serialize call could never succeed

**Error.** `This call always throws: 'Item' cannot be the top-level object of a buffer, because it is abstract or an
interface, and the top-level object must be one concrete class; serialize the concrete class, or a class with a field
of this type`

**What it means:** the object you pass to `Serialize` becomes the top-level object of the buffer, and it must be a
concrete class, record or struct with fields. This call passes something else, so it could never succeed; the
generator stops the build instead of letting it throw at run time.

| The message says | What you passed | Fix |
|---|---|---|
| "it is abstract or an interface" | an abstract class or an interface | serialize the concrete class, or a class with a field of this type |
| "values of this type can only be stored in the fields of a class" | a list, a dictionary, a plain struct, an enum, a string or a nullable value: these can only be fields of a class | wrap it in a class: `public class Inventory { public List<Item> Items; }` |
| "it is not a class, record or struct" | `object`, a delegate, a pointer, or `dynamic` | serialize a class, record or struct |

```csharp
TesseraSerializer.Serialize(items);                          // TESSERA014: a List<Item>
TesseraSerializer.Serialize(new Inventory { Items = items }); // fine
```
