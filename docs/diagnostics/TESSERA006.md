# TESSERA006: A struct's memory layout can't be saved as it is

**Error.** `Struct 'Packet' cannot be stored inline: explicit layout is not supported`

**What it means:** a struct with only numbers and other plain values inside is saved byte for byte, in its C# memory
layout, and C++ reads it as a struct with the same layout. This struct can't be, for the reason given:

| Reason | Fix |
|---|---|
| explicit layout is not supported (`[StructLayout(LayoutKind.Explicit)]`, overlapping fields) | use the default sequential layout, or save the overlapping values as one integer |
| fixed-size buffers are not supported (`fixed byte Data[16]`) | use separate fields, or a list field in a class |
| field '...' has an unsupported type | see [TESSERA001](TESSERA001.md): the same types can't be saved in structs |
| it has no instance fields | add a field, or don't save the struct |
| none of its fields are visible; structs from other assemblies must have public fields | make its fields public, or copy the values into a struct of your own |

See also: [Plain structs](../guide/supported-types.md#plain-structs).
