# TESSERA002: Duplicate wire name

**Error.** `Members 'Hp' and 'HitPoints' of 'Monster' have the same wire name or name hash; give one of them a
different [TesseraName]`

Members are identified on the wire by a 64-bit hash of their wire name: the C# name, or the one given with
[`[TesseraName]`](../attributes/tessera-name.md). Two members of one type ended up with the same name, usually because
one was renamed with `[TesseraName]` to a name another member already has. (Two different names with the same 64-bit
hash are possible in theory and reported the same way.)

## Fix

Give one of the members a different wire name:

```csharp
public class Monster
{
    public short Hp;
    [TesseraName("HitPointsMax")] public short HitPoints;
}
```

If buffers with the old name exist, keep the old name on the member that should read them.
