# TESSERA007: A C++ method was renamed to avoid a clash

**Info.** `The C++ method for field 'HasName' of 'Monster' was renamed to 'has_name_2' to avoid a clash`

**What it means:** each field gets a C++ method named after it (`name()`) and a method that tells whether it was saved
(`has_name()`). Two of these names clashed, for example a field `HasName` next to the `has_name()` of a field `Name`,
so the generator added a number to one of them. (Names that are C++ keywords are changed without a message.)

## Fix

Nothing is wrong: the saved data is the same either way. To choose the C++ name yourself, rename the C# field, keeping
its saved name with [`[TesseraName]`](../attributes/tessera-name.md) if data has already been saved:

```csharp
[TesseraName("HasName")] public bool NameKnown;   // C++: name_known()
```

How C# names become C++ names is set by `TesseraCppNaming` ([MSBuild settings](../guide/msbuild.md)).
