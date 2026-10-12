# `[TesseraName]`

```csharp
public class Monster
{
    [TesseraName("Hp")]
    public short HitPoints = 100;    // renamed in C#; buffers still call it "Hp"
}
```

On a field or property. Sets the member's wire name. A member is identified on the wire by a 64-bit hash of its name,
so by default renaming a member in C# makes it a new member, and old buffers read the old one as absent. Keeping the
old name here lets you rename the C# member freely.

## Use it when

- **You rename a member** in a model whose buffers already exist: save files, shipped content, data another program
  writes.
- **Two members collide**: the same wire name, or names whose hashes collide
  ([TESSERA002](../diagnostics/TESSERA002.md)). Give one of them a different name.
- **The wire name should follow a convention** that the C# name does not, for example names shared with another tool.

## Details

- The C++ accessor still follows the C# name (`hit_points()` above), so C++ code changes with the rename; the buffers
  do not.
- The name is case-sensitive: `"hp"` and `"Hp"` are different members.
- Union member types are renamed safely with [`[Tessera("OldName")]`](tessera.md) instead; `[TesseraName]` is for
  members.

See also: [Schema evolution](../guide/schema-evolution.md).
