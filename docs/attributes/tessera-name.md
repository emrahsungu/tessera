# `[TesseraName]`

**What it does:** saves the field under another name than its C# name.

```csharp
public class Monster
{
    [TesseraName("Hp")]
    public short HitPoints = 100;    // renamed in C#; still saved as "Hp"
}
```

Tessera finds a field in saved data by its name (the buffer stores a 64-bit hash of it). If you rename a field in C#,
data saved with the old name has no field with the new name, and the field reads as missing. `[TesseraName]` keeps
the old name, so you can rename the C# field freely.

## When you need it

- **You rename a field** in a class whose data already exists: save files, shipped content, data another program
  writes.
- **Two fields end up with the same saved name** ([TESSERA002](../diagnostics/TESSERA002.md)). Give one of them a
  different name.
- **The saved name must follow a convention** that the C# name doesn't, for example names shared with another tool.

## Details

- The C++ accessor still follows the C# name (`hit_points()` above), so C++ code changes with the rename; the saved
  data does not.
- Names are case-sensitive: `"hp"` and `"Hp"` are different fields.
- To rename a class without breaking saved data, use [`[Tessera("OldName")]`](tessera.md) instead.

See also: [Changing your classes](../guide/versioning.md).
