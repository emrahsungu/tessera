# TESSERA002: Two fields would be saved under the same name

**Error.** `Fields 'Hp' and 'HitPoints' of 'Monster' would be saved under the same name (or names with the same hash);
give one of them a different [TesseraName]`

**What it means:** two fields of one class would be saved under the same name. Saved data identifies each field by a
64-bit hash of its name: the C# name, or the one given with [`[TesseraName]`](../attributes/tessera-name.md). This
usually happens when a field is renamed with `[TesseraName]` to a name another field already has. (Two different
names with the same hash are possible in theory, and reported the same way.)

## Fix

Give one of the fields a different saved name:

```csharp
public class Monster
{
    public short Hp;
    [TesseraName("HitPointsMax")] public short HitPoints;
}
```

If data was already saved under the old name, keep that name on the field that should read it.
