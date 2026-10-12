# Tessera documentation

Tessera writes binary buffers from plain .NET objects, and C++20 reads them where they lie. These pages explain every
part of it; the [README](../README.md) is the short tour.

## Guide

Read these in order the first time:

1. [Installation](guide/installation.md): the NuGet package, or this repository's projects in your solution.
2. [Your first model](guide/first-model.md): write a buffer in .NET, read it in C++.
3. [Which types are models](guide/models.md): what the source generator picks up, and when `[Tessera]` matters.
4. [Types](guide/types.md): every supported member type, unions, dictionaries and plain structs, and what to use
   instead of the types Tessera does not support.
5. [Writing in .NET](guide/writing.md): `TesseraSerializer`, reusing a `TesseraWriter`, and the write options.
6. [Reading in C++](guide/reading-cpp.md): the headers, `Reader`, verification, views, errors and trusted buffers.
7. [Schema evolution](guide/schema-evolution.md): the wire contract, and which changes old and new readers survive.
8. [Models in several projects](guide/multiple-projects.md): headers and defaults across assemblies.
9. [MSBuild settings](guide/msbuild.md): where the headers go, C++ names and namespaces, strict mode.

## Reference

- [Attributes](attributes/README.md): one page per attribute, with its use cases.
- [Diagnostics](diagnostics/README.md): every `TESSERA` warning and error, with its cause and fix.
- [Wire format](FORMAT.md): the byte layout of a buffer.
- [Benchmark results](BENCHMARKS.md): every table behind the README's charts.

Found a mistake or a gap? These pages live in the repository, next to the code they describe: fix them in a pull
request.
