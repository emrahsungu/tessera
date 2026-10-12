# TESSERA003: Polymorphic type without known members

**Error.** `'Item' is abstract or an interface, but no public or internal concrete type of this project derives from
it, and no referenced project marks one with [Tessera] or lists one in [assembly: TesseraRoot]; mark the derived types
in their project, or list them with [TesseraUnion(...)]`

A model has a member whose type is an abstract class or an interface, so the member is a union. The generator found no
concrete type it can store there. It considers every public or internal concrete type of this project that derives
from the member's type, but types of other projects only when their project marks them: it cannot tell which types of
another assembly are meant to be models.

## Fix

One of these:

- In the project that declares the derived types, mark them [`[Tessera]`](../attributes/tessera.md), or list them in
  [`[assembly: TesseraRoot(typeof(Potion), typeof(Key))]`](../attributes/tessera-root.md).
- On the member or on the base type, list them with
  [`[TesseraUnion(typeof(Potion), typeof(Key))]`](../attributes/tessera-union.md); this works when you cannot change
  their project.
- If the derived types are private or protected, make them internal or public
  ([TESSERA013](TESSERA013.md)).

See also: [Unions](../guide/types.md#unions).
