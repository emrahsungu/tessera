# MSBuild settings

Set these in your project file, in a `<PropertyGroup>`.

| Property | Default | What it does |
|---|---|---|
| `TesseraCppOutputDir` | empty: no headers on disk | after each build, writes the generated C++ headers here (relative to the project), plus the C++ runtime headers in `tessera/`. Unchanged files are left alone, so C++ builds don't rebuild needlessly |
| `TesseraCppHeaderName` | `<AssemblyName>.tessera.hpp` | the name of the header that includes every model type of the assembly |
| `TesseraCppNamespace` | each type's C# namespace in lower case (`Game.Items` → `game::items`); the assembly name for the global namespace | one C++ namespace for every generated type of the assembly, in C++ syntax (`game::data`) |
| `TesseraCppNaming` | `snake_case` | the accessors' names: `snake_case`, `camelCase`, `PascalCase` or `original` (as in C#) |
| `TesseraCppCopyRuntime` | `true` | `false`: don't copy the C++ runtime headers (when your C++ build takes them from elsewhere) |
| `TesseraRequireAttribute` | `false` | `true`: every model class, record and struct must be marked ([strict mode](models.md#strict-mode)) |

For example, a game whose engine code uses `PascalCase` and keeps third-party code in `ThirdParty/`:

```xml
<PropertyGroup>
  <TesseraCppOutputDir>../Engine/ThirdParty/Generated</TesseraCppOutputDir>
  <TesseraCppNamespace>game::data</TesseraCppNamespace>
  <TesseraCppNaming>PascalCase</TesseraCppNaming>
  <TesseraCppHeaderName>GameData.hpp</TesseraCppHeaderName>
</PropertyGroup>
```

Names that are C++ keywords are adjusted silently. When two generated names would clash, the generator adjusts one and
says so: two C++ types of the same name in one namespace ([TESSERA008](../diagnostics/TESSERA008.md)), or two
accessors of one type, such as a member `HasName` next to the accessor `has_name()` of a member `Name`
([TESSERA007](../diagnostics/TESSERA007.md)).

## How the headers get to disk

The source generator puts each header into the compiled assembly (as `[assembly: TesseraCppHeader]`). After the build,
a small tool from the package, `Tessera.Cpp`, reads them from the assembly's metadata and writes them to
`TesseraCppOutputDir`. No model code runs. Two more properties override where the tool and the runtime headers are
found; you only need them for unusual layouts:

| Property | Default |
|---|---|
| `TesseraCppTool` | the package's `tools/net8.0/Tessera.Cpp.dll`, or `src/Tessera.Cpp` in this repository |
| `TesseraCppRuntimeDir` | the package's `include/tessera`, or `cpp/include/tessera` in this repository |

Back to the [documentation index](../README.md).
