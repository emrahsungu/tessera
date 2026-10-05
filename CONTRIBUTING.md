# Contributing to Tessera

Thank you for helping. Bug reports, questions and pull requests are welcome.

## Reporting a problem

Open an issue with the C# model involved, what you expected and what happened. For a buffer that a reader rejects or
misreads, the smallest model and data that show it are the most useful. Report security problems privately instead;
see [SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET 10 SDK, CMake 3.20 or later and a C++20 compiler; the Linux tests also need WSL with GCC and Clang.

```shell
dotnet build Tessera.slnx -c Release
powershell scripts/test.ps1 -Linux      # .NET tests, then the C++ tests with MSVC, GCC and Clang
```

A pull request should pass `scripts/test.ps1 -Linux`. Add `-Asan` for an MSVC AddressSanitizer build when you change
the C++ runtime, and `-Package` when you change the package or its MSBuild files.

## Changes to the format, the writers or the readers

- **Wire format:** update [docs/FORMAT.md](docs/FORMAT.md) in the same pull request, and raise the format version when
  old readers could misread new buffers.
- **Performance:** measure the change against the commit before it, and include the report in the pull request:

  ```shell
  powershell scripts/ab.ps1 -Candidate HEAD -Out ab-report.md
  ```

  Both commits are built in their own worktrees and run in alternating order, with FlatBuffers and MessagePack as the
  noise control. A change is kept when no Tessera row gets slower beyond the noise and buffers do not grow (unless that
  is the point of the change). On CPUs affected by the JCC erratum (Skylake and its successors up to Comet Lake), add
  `-Jcc` when rows move in code the change does not touch.
- **Tests:** new behavior needs a test. For readers that is usually an export in `tests/Tessera.Tests/InteropExport.cs`
  and a check in `tests/cpp/interop_test.cpp`; buffers the verifier must reject belong in the fuzz corpus
  (`tests/cpp/fuzz_test.cpp`).

## Style

Follow `.editorconfig` and the code around your change. The C++ runtime is header-only C++20 and must compile without
warnings on MSVC, Clang and GCC.

## License

Tessera is licensed under the [Apache License 2.0](LICENSE). Contributions are accepted under the same license.
