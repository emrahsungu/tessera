# Generated attributes

The source generator emits three assembly attributes into every project with models. They are public because other
assemblies, and the header export tool, read them; you never write them yourself.

| Attribute | Carries | Read by |
|---|---|---|
| `[assembly: TesseraDefault(typeof(Monster), "Hp", 100)]` | the initializer of a member of one of this assembly's models (`public short Hp = 100;`) | the source generators of other projects that use the type, which cannot read initializers in compiled code ([Models in several projects](../guide/multiple-projects.md)) |
| `[assembly: TesseraDeclared(typeof(Monster))]` | that this assembly treated the type as a model, so its `TesseraDefault` entries are complete | the same generators: a type without it makes them warn [TESSERA011](../diagnostics/TESSERA011.md) |
| `[assembly: TesseraCppHeader("game/Monster.tessera.hpp", "...")]` | a generated C++ header | the `Tessera.Cpp` tool, which writes the headers to `TesseraCppOutputDir` after the build ([MSBuild settings](../guide/msbuild.md)) |

`TesseraDefault` and `TesseraDeclared` are hidden from IntelliSense.
