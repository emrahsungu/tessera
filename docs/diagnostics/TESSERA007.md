# TESSERA007: C++ name adjusted

**Info.** `C++ accessor for member 'HasName' of 'Monster' was renamed to 'has_name_2' to avoid a clash`

Every member gets an accessor named after it (`name()`) and a presence check (`has_name()`). Two of these names clashed
in C++, for example a member `HasName` next to the `has_name()` of a member `Name`, so the generator appended a number
to one of them. Names that are C++ keywords are adjusted without a message.

## Fix

Nothing is wrong: the buffer is the same either way. To choose the C++ name yourself, rename the C# member, keeping its
wire name with [`[TesseraName]`](../attributes/tessera-name.md) if buffers already exist:

```csharp
[TesseraName("HasName")] public bool NameKnown;   // C++: name_known()
```

The accessor names also depend on `TesseraCppNaming` ([MSBuild settings](../guide/msbuild.md)).
