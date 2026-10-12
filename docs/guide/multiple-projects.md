# Models in several projects

Models can span several assemblies: shared types in a library, game-specific types in the game, tool types in an
editor plugin. Each project that references Tessera runs the source generator over its own code.

## Headers

An assembly's headers also cover the model types it uses from other assemblies, and the headers for one type are
identical whichever assembly wrote them. So you can include several assemblies' headers in one C++ file. If two
assemblies were built from different versions of one type's model, the headers for that type differ, and including
both stops the C++ build with an `#error` naming the type.

## Member defaults across projects

A member's default comes from its initializer (`public short Hp = 100;`) or a `[DefaultValue]` attribute. Writers
leave out members equal to their default, and readers return it, so both sides must agree on it.

Initializers are compiled into constructors, which a source generator cannot read in a referenced assembly. So each
project's generator records the initializers of its own models in its assembly (in a generated
`[assembly: TesseraDefault]`), and other projects read them from there.

A type's project records its initializers only when it treats the type as a model there. When it does not, a project
that uses the type cannot see them: it takes zero as the default of those members, and warns
([TESSERA011](../diagnostics/TESSERA011.md)). Three ways to fix it, in the type's own project:

- mark the type [`[Tessera]`](../attributes/tessera.md);
- list it in [`[assembly: TesseraRoot(typeof(...))]`](../attributes/tessera-root.md);
- or give the members `[DefaultValue(100)]`, which is visible from anywhere.

## Unions across projects

A union member's possible types are the concrete types that derive from its type. Inside one project, every public or
internal one counts. Types from other projects count only when their project marks them with `[Tessera]` or lists them
in `[assembly: TesseraRoot]`: a project cannot otherwise know which types of an assembly are meant to be models.
[`[TesseraUnion]`](../attributes/tessera-union.md) lists the types explicitly instead.

## One model, two projects

A type is a model of every project that uses it, so two projects can write the same type. Their serializers are
generated separately from the same model; as long as both see the same member defaults (see above), they write the
same buffers, and either project's header reads them.

Next: [MSBuild settings](msbuild.md).
