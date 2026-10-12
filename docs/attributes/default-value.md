# Default values: initializers and `[DefaultValue]`

**What it does:** sets a field's default value. A field equal to its default is not written to the buffer, and C++
returns the default when the field is missing.

```csharp
using System.ComponentModel;

public class Monster
{
    public short Hp = 100;                 // the default is 100

    [DefaultValue(Color.Blue)]
    public Color Color { get; set; } = Color.Blue;
}
```

```cpp
std::int16_t hp = monster.hp();   // 100 when the buffer doesn't contain it
bool saved = monster.has_hp();    // false then
```

Every non-nullable field has a default: its initializer, its `[DefaultValue]` (System.ComponentModel), or else zero
(`false`, `0`, the enum's zero value, an all-zero struct). Nullable fields (`int?`, strings, objects, lists) have no
default value: null means "missing".

## When you need `[DefaultValue]`

An initializer is enough in most cases. Add `[DefaultValue]` when:

- **another project saves the type** and the type's own project doesn't generate code for it. Other projects can't
  read initializers from compiled code, but they can read `[DefaultValue]` ([TESSERA011](../diagnostics/TESSERA011.md));
- **other tools or serializers** read `[DefaultValue]` too.

## Changing a default

Data saved earlier leaves out every field that was equal to the old default. After you change the default, those
fields read as the new default. If a default may change, mark the field
[`[TesseraKeepDefault]`](tessera-keep-default.md), which always writes it, or save with `WriteDefaults = true`
([`WriteDefaults`](../options/write-defaults.md)).
