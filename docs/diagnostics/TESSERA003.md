# TESSERA003: No class can be saved in a field of this type

**Error.** `Fields of type 'Item' can hold only classes derived from it, and none can be saved: no public or internal
class of this project derives from it, and no referenced project marks one with [Tessera] or lists one in [assembly:
TesseraRoot]; mark the derived classes in their project, or list them with [TesseraUnion(...)]`

**What it means:** a field is declared as an abstract class or an interface, so it holds instances of derived
classes. The generator found no derived class it could save there.

It looks for derived classes in two places: every public or internal class of this project, and classes of other
projects, but only those marked `[Tessera]` or listed in `[assembly: TesseraRoot]` in their project (the generator
can't tell which classes of another assembly are meant to be saved).

## Fix

One of these:

- In the project that declares the derived classes, mark them [`[Tessera]`](../attributes/tessera.md), or list them
  in [`[assembly: TesseraRoot(typeof(Potion), typeof(Key))]`](../attributes/tessera-root.md).
- On the field or on the base class, list them with
  [`[TesseraUnion(typeof(Potion), typeof(Key))]`](../attributes/tessera-union.md). This works even when you can't
  change their project.
- If the derived classes are private or protected, make them internal or public
  ([TESSERA013](TESSERA013.md)).

See also: [Fields declared as a base class or interface](../guide/supported-types.md#fields-declared-as-a-base-class-or-interface).
