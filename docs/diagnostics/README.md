# Diagnostics

The source generator reports problems with your models at compile time, so a model that cannot work never builds.
Every diagnostic has a page with its cause and its fix.

| ID | Severity | Title |
|---|---|---|
| [TESSERA001](TESSERA001.md) | Error | Unsupported member type |
| [TESSERA002](TESSERA002.md) | Error | Duplicate wire name |
| [TESSERA003](TESSERA003.md) | Error | Polymorphic type without known members |
| [TESSERA004](TESSERA004.md) | Error | Union tag collision |
| [TESSERA005](TESSERA005.md) | Error | Too many members |
| [TESSERA006](TESSERA006.md) | Error | Unsupported struct layout |
| [TESSERA007](TESSERA007.md) | Info | C++ name adjusted |
| [TESSERA008](TESSERA008.md) | Info | C++ type name adjusted |
| [TESSERA009](TESSERA009.md) | Error | Union tag is zero |
| [TESSERA010](TESSERA010.md) | Info | Model type not visible at this call |
| [TESSERA011](TESSERA011.md) | Warning | Member initializers of another project are not visible |
| [TESSERA012](TESSERA012.md) | Error | Model is not marked |
| [TESSERA013](TESSERA013.md) | Error | Model is not accessible |
| [TESSERA014](TESSERA014.md) | Error | Type cannot be the root of a buffer |
| [TESSERA015](TESSERA015.md) | Warning | Tessera referenced without its source generator (MSBuild, inside this repository) |

Info diagnostics appear in the IDE and in detailed build logs; they need no action unless the behavior they describe
is not what you want. To silence an info or warning diagnostic in a project, add its ID to `NoWarn`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);TESSERA010</NoWarn>
</PropertyGroup>
```
