# Quickstart sample

The example at the top of the [README](../../README.md): C# writes a `Monster` buffer, and C++ reads it in place
through the generated header.

```
dotnet run                                  # builds, writes cpp/generated/*.hpp and monster.bin, prints "328 bytes"
cmake -S cpp -B cpp/build
cmake --build cpp/build --config Release
cpp/build/Release/quickstart monster.bin    # (cpp/build/quickstart with single-config generators)
```

The C++ reader prints:

```
Orc: hp 100, mana 25, position 1 2 3
  Axe: damage 9
  Bow: damage 4
```
