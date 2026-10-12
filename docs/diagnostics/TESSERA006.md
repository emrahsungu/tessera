# TESSERA006: Unsupported struct layout

**Error.** `Struct 'Packet' cannot be stored inline: explicit layout is not supported`

A struct whose fields are all unmanaged is stored inline, in its C# layout, and read in C++ as a struct of the same
layout. This one cannot be, for the reason given:

| Reason | Fix |
|---|---|
| explicit layout is not supported (`[StructLayout(LayoutKind.Explicit)]`, overlapping fields) | use sequential layout, or store the overlapping views as one integer |
| fixed-size buffers are not supported (`fixed byte Data[16]`) | use separate fields, or a vector member in a model class |
| field '...' has an unsupported type | see [TESSERA001](TESSERA001.md); the same types are unsupported in structs |
| it has no instance fields | add a field, or don't store the struct |
| none of its fields are visible; structs from other assemblies must have public fields | make the fields public, or copy the values into a struct of your own |

See also: [Plain structs](../guide/types.md#plain-structs).
