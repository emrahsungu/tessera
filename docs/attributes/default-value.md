# Defaults: initializers and `[DefaultValue]`

```csharp
using System.ComponentModel;

public class Monster
{
    public short Hp = 100;                 // initializer: the default is 100

    [DefaultValue(Color.Blue)]
    public Color Color { get; set; } = Color.Blue;
}
```

Every non-nullable member has a default: its initializer, its `[DefaultValue]` (System.ComponentModel), or else zero
(`false`, `0`, the enum's zero value, an all-zero struct). A member equal to its default is not stored, and C++ returns
the default compiled into the header when the member is absent:

```cpp
std::int16_t hp = monster.hp();   // 100 when the buffer did not store it
bool stored = monster.has_hp();   // false then
```

Nullable members (`int?`, strings, objects, collections) have no default: null means absent.

## Use `[DefaultValue]` when

- **Another project uses the type** and its project does not treat the type as a model. Initializers are compiled into
  constructors, which other projects cannot read; `[DefaultValue]` is metadata, which they can
  ([TESSERA011](../diagnostics/TESSERA011.md)).
- **The default should be visible** in the declaration, for tools or other serializers that read `[DefaultValue]`.

## Changing a default

The default is part of the wire contract: buffers leave the member out when it equals the default at write time, and
readers fill in the default they were compiled with. If you change a default, members that old buffers left out read as
the new default. If a default may change, mark the member [`[TesseraKeepDefault]`](tessera-keep-default.md), so it is
always stored, or write with `WriteDefaults = true` ([Writing in .NET](../guide/writing.md#writedefaults)).

See also: [Schema evolution](../guide/schema-evolution.md).
