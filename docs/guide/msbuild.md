# MSBuild settings

Set these in your project file, inside a `<PropertyGroup>`.

| Property | Default | What it does |
|---|---|---|
| `TesseraCppOutputDir` | empty: no headers on disk | After each build, writes the generated C++ headers into this folder (relative to the project), and the C++ library into its `tessera/` subfolder. Files whose content didn't change are left alone, so your C++ build doesn't rebuild needlessly. |
| `TesseraCppHeaderName` | `<AssemblyName>.tessera.hpp` | The name of the header that includes every class of the project. |
| `TesseraCppNamespace` | each class's C# namespace in lower case (`Game.Items` → `game::items`); the assembly name for classes without a namespace | One C++ namespace for every generated class of the project, in C++ syntax (`game::data`). |
| `TesseraCppNaming` | `snake_case` | How C# field names become C++ method names: `snake_case`, `camelCase`, `PascalCase`, or `original` (unchanged). |
| `TesseraCppCopyRuntime` | `true` | `false`: don't copy the C++ library into `TesseraCppOutputDir`, for example because your C++ build takes it from elsewhere. |
| `TesseraRequireAttribute` | `false` | `true`: every saved class, record and struct must be marked `[Tessera]` or listed in `TesseraRoot` ([strict mode](generated-types.md#strict-mode)). |

For example, an engine whose code uses `PascalCase` and keeps third-party code in `ThirdParty/`:

```xml
<PropertyGroup>
  <TesseraCppOutputDir>../Engine/ThirdParty/Generated</TesseraCppOutputDir>
  <TesseraCppNamespace>game::data</TesseraCppNamespace>
  <TesseraCppNaming>PascalCase</TesseraCppNaming>
  <TesseraCppHeaderName>GameData.hpp</TesseraCppHeaderName>
</PropertyGroup>
```

C++ names that would be keywords are changed silently. When two generated names would clash, the generator changes
one and tells you: two C++ classes with the same name in one namespace ([TESSERA008](../diagnostics/TESSERA008.md)),
or two methods of one class, such as a field `HasName` next to the `has_name()` of a field `Name`
([TESSERA007](../diagnostics/TESSERA007.md)).

## How the headers get to disk

The source generator stores each header inside the compiled assembly. After the build, a small tool from the package,
`Tessera.Cpp`, reads them from the assembly and writes them to `TesseraCppOutputDir`; none of your code runs. To do
the same from your own code or build step, use `TesseraCppHeaders.Export` ([C# API](../api/csharp.md#tesseracppheaders-the-c-headers-from-code)).

Two more properties override where the tool and the C++ library are taken from; you only need them for unusual
layouts:

| Property | Default |
|---|---|
| `TesseraCppTool` | the package's `tools/net8.0/Tessera.Cpp.dll`, or `src/Tessera.Cpp` in this repository |
| `TesseraCppRuntimeDir` | the package's `include/tessera`, or `cpp/include/tessera` in this repository |

Back to the [documentation index](../README.md).
