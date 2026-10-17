# Installation

Tessera is one NuGet package, `Tessera.Serialization`, with four parts:

- the .NET library you call (`TesseraSerializer`, the attributes);
- a **source generator**: code that runs inside the C# compiler and writes a serializer for each of your classes;
- a small tool that, after each build, writes the generated **C++ headers** to a folder you choose;
- the C++ library those headers use (header-only C++20).

You can use the package, or reference this repository's projects directly.

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
  <PackageReference Include="Tessera.Serialization" Version="0.1.0-preview" />
</ItemGroup>
<PropertyGroup>
  <!-- After each build, the generated headers and the C++ library are written here. -->
  <TesseraCppOutputDir>cpp/generated</TesseraCppOutputDir>
</PropertyGroup>
```

That's all: the package brings the source generator along, so every build generates the serializers and the C++
headers. Without `TesseraCppOutputDir`, no headers are written to disk (they stay inside the compiled assembly). In
code, the namespace is `Tessera` (`using Tessera;`).

NuGet caches packages by version number. If you rebuild the package without changing its version, delete
`~/.nuget/packages/tessera.serialization` (`%USERPROFILE%\.nuget\packages\tessera.serialization` on Windows), or
restore keeps the old copy.

## B. This repository's projects, in your solution

To work on Tessera itself, or to debug into its code, reference its projects instead of the package. Add one line at
the end of your project file:

```xml
<Import Project="path/to/tessera/src/Tessera.ProjectReference.targets" />
```

It adds three project references, plus the package's build settings, so your project behaves exactly as with the
package:

| Project | How it is referenced | What it is |
|---|---|---|
| `src/Tessera` | a normal project reference | the .NET library: `TesseraSerializer`, the attributes |
| `src/Tessera.Generator` | as an analyzer (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`) | the source generator |
| `src/Tessera.Cpp` | built, but not referenced (`ReferenceOutputAssembly="false"`) | the tool that writes the C++ headers after each build |

### Why a plain project reference to `src/Tessera` isn't enough

A `<ProjectReference>` to `src/Tessera/Tessera.csproj` alone compiles fine: the attributes and `TesseraSerializer` are
in that project. But the C# compiler only runs a source generator that is referenced **as an analyzer**, and a project
reference doesn't pass analyzers on to the projects that reference it. So no serializers are generated, and the first
`TesseraSerializer.Serialize` call throws a `TesseraException` that explains this and names the fix. Inside the
Tessera repository's folder, the build also warns ([TESSERA015](../diagnostics/TESSERA015.md)).

If you'd rather write the references yourself:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/tessera/src/Tessera/Tessera.csproj" />
  <ProjectReference Include="path/to/tessera/src/Tessera.Generator/Tessera.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

That gives you the serializers, but writes no C++ headers to disk: that takes `src/Tessera.Cpp` and the package's
`build/Tessera.Serialization.props` and `build/Tessera.Serialization.targets`, which the import line adds.

## Requirements

- .NET 8 or later, and a C# compiler from the .NET 8 SDK or later.
- C++: a C++20 compiler. Tested with MSVC 19.42, clang-cl 19, GCC 15 and Clang 21.

Next: [Getting started](getting-started.md).
