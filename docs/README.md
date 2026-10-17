# Tessera documentation

Tessera saves .NET objects as binary data that C++ and .NET read directly, without parsing or copying. During
compilation, a source generator writes, for each of your classes: a C# serializer, which turns objects into bytes; a
C# read type and a C++ header with an accessor class, which read the fields straight from those bytes; and the C# code
that creates the objects again.

The [README](../README.md) is the short tour; these pages explain everything.

## Guide

Read these in order the first time:

1. [Installation](guide/installation.md): add the package from nuget.org, build it from source, or use this
   repository's projects in your solution.
2. [Getting started](guide/getting-started.md): save an object in C#, read it in C++.
3. [Which types get generated code](guide/generated-types.md): which of your types Tessera generates code for, and
   when you need to mark one.
4. [Supported types](guide/supported-types.md): what each C# type becomes in C++: numbers, strings, lists,
   dictionaries, structs, and fields declared as a base class or interface.
5. [Writing in C#](guide/writing.md): the three ways to save, and the options.
6. [Reading in C++](guide/reading-cpp.md): opening a buffer, validating it, reading fields, and errors.
7. [Reading in .NET](guide/reading-dotnet.md): reading a buffer in place, or creating the objects again.
8. [Changing your classes](guide/versioning.md): which changes keep data saved earlier readable.
9. [Classes in several projects](guide/multiple-projects.md): when your types are spread over several assemblies.
10. [MSBuild settings](guide/msbuild.md): where the C++ headers go, and how things are named in C++.

## Options

Every option, with what it does and every case to use it in:

- [Saving in C#](options/README.md): [`Sharing`](options/sharing.md) (`None`, `Strings`, `All`),
  [`IncludeSchema`](options/include-schema.md), [`WriteDefaults`](options/write-defaults.md),
  [`MaxDepth`](options/max-depth.md).
- [Reading in C++](options/cpp-read-options.md): validation, the UTF-8 check, nesting and size limits, and reading
  your own data without checks.
- [Reading in .NET](options/dotnet-read-options.md): the same, for the read types and `Deserialize`.

## Reference

- [Attributes](attributes/README.md): every attribute, what it does and when to use it.
- [C# API](api/csharp.md): every public class and method of the .NET library, and the generated read types.
- [C++ API](api/cpp.md): `Reader`, accessor classes, lists, dictionaries, errors.
- [Compiler messages](diagnostics/README.md): every `TESSERA` error and warning, what causes it, and how to fix it.
- [Binary format](FORMAT.md): the exact byte layout.
- [Benchmark results](BENCHMARKS.md): every table behind the README's charts.

Found a mistake or a gap? These pages live in the repository next to the code: fix them in a pull request.
