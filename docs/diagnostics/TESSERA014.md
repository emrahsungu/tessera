# TESSERA014: Type cannot be the root of a buffer

**Error.** `This call always throws: 'Item' cannot be the root of a buffer, because it is abstract or an interface, but
the root of a buffer has one concrete type; serialize the concrete type, or a model with a member of this type`

The root of a buffer, the value you pass to `Serialize` or `Write`, must be a model: a concrete class, record or struct
with members. The call passes something else, so it could never succeed, and the generator stops the build instead of
letting it throw at run time.

| Reason | Fix |
|---|---|
| it is abstract or an interface | serialize the concrete type, or a model with a member of this type |
| values of this type are stored inside models, not as the root of a buffer (a collection, a dictionary, a plain struct) | wrap it in a model: `public class Inventory { public List<Item> Items; }` |
| it is not a model type (`object`, a delegate, a pointer, `dynamic`) | serialize a class, record or struct |

```csharp
TesseraSerializer.Serialize(items);                          // TESSERA014: a List<Item>
TesseraSerializer.Serialize(new Inventory { Items = items }); // fine
```
