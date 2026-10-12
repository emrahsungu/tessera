# Classes in several projects

Your classes can be spread over several projects: shared types in a library, game-specific types in the game, tool
types in an editor plugin. Each project that references Tessera runs the source generator on its own code.

## C++ headers

A project's headers also cover the classes it uses from other projects, and the header for one class is identical
whichever project generated it. So you can include several projects' headers in one C++ file.

If two projects were built from different versions of one class, their headers for it differ, and including both
stops the C++ build with an `#error` naming the class.

## Initial values of fields

A field's default value comes from its initializer (`public short Hp = 100;`) or from `[DefaultValue]`. Fields equal to
it aren't saved, and readers return it, so every project must see the same default.

Initializers are compiled into constructors, and a source generator can't read code inside a referenced assembly. So
each project's generator records the initializers of its own classes in its assembly (as generated
`[assembly: TesseraDefault]` attributes), and other projects read them from there.

A project records them only for classes it generates code for. If project `Game` saves a class from project `Data`
that `Data` doesn't generate code for, `Game` can't see that class's initializers: it takes zero as the default of
those fields, and warns ([TESSERA011](../diagnostics/TESSERA011.md)). Fix it in `Data`:

- mark the class [`[Tessera]`](../attributes/tessera.md);
- or list it in [`[assembly: TesseraRoot(typeof(...))]`](../attributes/tessera-root.md);
- or give the fields [`[DefaultValue(100)]`](../attributes/default-value.md), which every project can read.

## Derived classes in other projects

A field declared as a base class or an interface may hold every derived class of its own project. Derived classes of
**other** projects count only when their project marks them `[Tessera]` or lists them in `[assembly: TesseraRoot]`:
the generator can't otherwise tell which classes of another assembly are meant to be saved.
[`[TesseraUnion]`](../attributes/tessera-union.md) lists the allowed classes explicitly instead.

## One class, saved by two projects

Two projects can save the same class. Each generates its own serializer, from the same class; as long as both see the
same default values (above), they write the same bytes, and either project's header reads them.

Next: [MSBuild settings](msbuild.md).
