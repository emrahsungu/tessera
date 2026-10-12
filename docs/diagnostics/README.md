# Compiler messages

Tessera's source generator checks your classes while they compile, so a class that can't be saved never builds. Each
message has a page with its cause and its fix.

| ID | Severity | What it means |
|---|---|---|
| [TESSERA001](TESSERA001.md) | Error | a field's type can't be saved (such as `DateTime`) |
| [TESSERA002](TESSERA002.md) | Error | two fields of a class would be saved under the same name |
| [TESSERA003](TESSERA003.md) | Error | a field is declared as a base class or interface, but no class that derives from it can be saved |
| [TESSERA004](TESSERA004.md) | Error | two derived classes would get the same type ID in saved data |
| [TESSERA005](TESSERA005.md) | Error | a class has more than 4,096 saved fields |
| [TESSERA006](TESSERA006.md) | Error | a struct's memory layout can't be stored as it is |
| [TESSERA007](TESSERA007.md) | Info | a C++ method name was changed to avoid a clash |
| [TESSERA008](TESSERA008.md) | Info | a C++ class name was changed to avoid a clash |
| [TESSERA009](TESSERA009.md) | Error | a class name's hash is 0, which is reserved |
| [TESSERA010](TESSERA010.md) | Info | a generic method serializes `T`, so the generator can't see which types it saves |
| [TESSERA011](TESSERA011.md) | Warning | the initial values of another project's fields aren't visible here |
| [TESSERA012](TESSERA012.md) | Error | strict mode: a saved class isn't marked `[Tessera]` |
| [TESSERA013](TESSERA013.md) | Error | a private or protected class can't be saved |
| [TESSERA014](TESSERA014.md) | Error | this `Serialize` call could never succeed |
| [TESSERA015](TESSERA015.md) | Warning | the project references Tessera without its source generator (inside this repository) |

Info messages appear in the IDE and in detailed build logs; they need no action unless the behavior they describe is
not what you want. To hide an info message or a warning in a project, add its ID to `NoWarn`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);TESSERA010</NoWarn>
</PropertyGroup>
```
