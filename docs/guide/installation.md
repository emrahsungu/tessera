# Installation

Tessera is one NuGet package. It holds the .NET runtime, the source generator, the tool that writes the C++ headers
after each build, and the C++ runtime headers. There are two ways to use it: the package, or this repository's
projects in your solution.

## A. The NuGet package

Tessera is not on nuget.org yet, so build the package once from this repository:

```shell
dotnet pack src/Tessera -c Release -o artifacts
```

Add that folder as a package source in your `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="tessera-local" value="path/to/tessera/artifacts" />
  </packageSources>
</configuration>
```

Reference the package, and choose where the C++ headers go:

```xml
<ItemGroup>
  <PackageReference Include="Tessera" Version="0.1.0-preview" />
</ItemGroup>
<PropertyGroup>
  <!-- After each build, the generated headers and the C++ runtime headers are written here. -->
  <TesseraCppOutputDir>cpp/generated</TesseraCppOutputDir>
</PropertyGroup>
```

The package brings the source generator along, so every build of your project generates the serializers and the
headers. Without `TesseraCppOutputDir`, the headers stay inside the compiled assembly and nothing is written to disk.

NuGet caches packages by version. After repacking the same version, delete `~/.nuget/packages/tessera`
(`%USERPROFILE%\.nuget\packages\tessera` on Windows), or restore keeps using the old copy.

## B. This repository's projects, in your solution

To work on Tessera itself or step into its code, reference its projects instead of the package. Import one file at
the end of your project file:

```xml
<Import Project="path/to/tessera/src/Tessera.ProjectReference.targets" />
```

It references three projects and the package's MSBuild files, so your project behaves exactly like one with the
package:

| Project | Referenced as | What it does |
|---|---|---|
| `src/Tessera` | a project reference | the runtime: `TesseraSerializer`, `TesseraWriter`, the attributes |
| `src/Tessera.Generator` | an analyzer (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`) | the source generator: serializers and C++ headers |
| `src/Tessera.Cpp` | built only (`ReferenceOutputAssembly="false"`) | writes the headers to `TesseraCppOutputDir` after each build |

### Why a project reference to `src/Tessera` alone is not enough

A `<ProjectReference>` to `src/Tessera/Tessera.csproj` alone compiles: the attributes and `TesseraSerializer` are
there. But a source generator runs only when it is referenced as an analyzer, and analyzers do not flow through
project references. So no serializer is generated, and the first `TesseraSerializer.Serialize` call throws a
`TesseraException` that says so and names the fix. A project inside the Tessera repository's folder also gets a build
warning, [TESSERA015](../diagnostics/TESSERA015.md).

To write the references yourself instead of importing the file:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/tessera/src/Tessera/Tessera.csproj" />
  <ProjectReference Include="path/to/tessera/src/Tessera.Generator/Tessera.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

That gives you the serializers, but no C++ headers on disk: writing them takes `src/Tessera.Cpp` and the package's
`build/Tessera.props` and `build/Tessera.targets`, which the import brings along.

## Requirements

- .NET 8 or later, and a C# compiler from the .NET 8 SDK or later.
- C++: a C++20 compiler. Tested with MSVC 19.42, clang-cl 19, GCC 15 and Clang 21. The C++ runtime is header-only.

Next: [Your first model](first-model.md).
