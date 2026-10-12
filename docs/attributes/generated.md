# Generated attributes

The source generator adds three assembly attributes to every project it generates code for. You never write them
yourself; they are public because other projects' generators, and the header export tool, need to read them.

| Attribute | What it records | Who reads it |
|---|---|---|
| `[assembly: TesseraDefault(typeof(Monster), "Hp", 100)]` | the initial value of a field (`public short Hp = 100;`) | the source generator of another project that saves the type: it can't read initializers from compiled code ([Classes in several projects](../guide/multiple-projects.md)) |
| `[assembly: TesseraDeclared(typeof(Monster))]` | that this project generated code for the type, so its `TesseraDefault` entries are complete | the same generators: a type without it triggers [TESSERA011](../diagnostics/TESSERA011.md) |
| `[assembly: TesseraCppHeader("game/Monster.tessera.hpp", "...")]` | a generated C++ header | the `Tessera.Cpp` tool, which writes the headers to `TesseraCppOutputDir` after each build ([MSBuild settings](../guide/msbuild.md)) |

`TesseraDefault` and `TesseraDeclared` are hidden from IntelliSense.
