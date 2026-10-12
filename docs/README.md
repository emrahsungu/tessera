# Tessera documentation

Tessera saves .NET objects as binary data that C++ reads directly, without parsing or copying. During compilation, a
source generator writes two things for each of your classes: a C# serializer, which turns objects into bytes, and a
C++ header with an accessor class, which reads the fields straight from those bytes.

The [README](../README.md) is the short tour; these pages explain everything.

## Guide

Read these in order the first time:

1. [Installation](guide/installation.md): add the NuGet package, or use this repository's projects in your solution.
2. [Getting started](guide/getting-started.md): save an object in C#, read it in C++.
3. [Which types get generated code](guide/generated-types.md): which of your types Tessera generates code for, and
   when you need to mark one.
4. [Supported types](guide/supported-types.md): what each C# type becomes in C++: numbers, strings, lists,
   dictionaries, structs, and fields declared as a base class or interface.
5. [Writing in C#](guide/writing.md): the three ways to save, and the options.
6. [Reading in C++](guide/reading-cpp.md): opening a buffer, validating it, reading fields, and errors.
7. [Changing your classes](guide/versioning.md): which changes keep data saved earlier readable.
8. [Classes in several projects](guide/multiple-projects.md): when your types are spread over several assemblies.
9. [MSBuild settings](guide/msbuild.md): where the C++ headers go, and how things are named in C++.

## Options

Every option, with what it does and every case to use it in:

- [Saving in C#](options/README.md): [`Sharing`](options/sharing.md) (`None`, `Strings`, `All`),
  [`IncludeSchema`](options/include-schema.md), [`WriteDefaults`](options/write-defaults.md),
  [`MaxDepth`](options/max-depth.md).
- [Reading in C++](options/cpp-read-options.md): validation, the UTF-8 check, nesting and size limits, and reading
  your own data without checks.

## Reference

- [Attributes](attributes/README.md): every attribute, what it does and when to use it.
- [C# API](api/csharp.md): every public class and method of the .NET library.
- [C++ API](api/cpp.md): `Reader`, accessor classes, lists, dictionaries, errors.
- [Compiler messages](diagnostics/README.md): every `TESSERA` error and warning, what causes it, and how to fix it.
- [Binary format](FORMAT.md): the exact byte layout.
- [Benchmark results](BENCHMARKS.md): every table behind the README's charts.

Found a mistake or a gap? These pages live in the repository next to the code: fix them in a pull request.
