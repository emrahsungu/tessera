# Quickstart sample

C# writes a `Monster` buffer; C++ reads it in place through the generated header.

```
dotnet run                                  # builds, writes cpp/generated/*.hpp and monster.bin
cmake -S cpp -B cpp/build
cmake --build cpp/build --config Release
cpp/build/Release/quickstart monster.bin    # (cpp/build/quickstart with single-config generators)
```
