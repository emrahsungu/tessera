# Attributes

No attribute is required: plain classes, records and structs are models as they are
([Which types are models](../guide/models.md)). Attributes change details, one each:

| Attribute | On | What it does |
|---|---|---|
| [`[Tessera]`](tessera.md), `[Tessera("Name")]` | class, record, struct | makes the type a model even when nothing serializes it; the name fixes its union tag |
| [`[assembly: TesseraRoot(typeof(A), ...)]`](tessera-root.md) | assembly | makes types models without editing them |
| [`[TesseraIgnore]`](tessera-ignore.md) | field, property | leaves the member out (so do `[IgnoreDataMember]` and `[NonSerialized]`) |
| [`[TesseraName("wire")]`](tessera-name.md) | field, property | the member's wire name: rename it in C# without breaking buffers |
| [`[DefaultValue(x)]`](default-value.md), or an initializer | field, property | the default: not stored, and returned by C++ when absent |
| [`[TesseraKeepDefault]`](tessera-keep-default.md) | field, property | always stores the member; scalars, enums and plain structs then sit at a fixed position |
| [`[TesseraShared]`](tessera-shared.md) | struct, field, property | stores a struct behind an offset, so equal values are stored once with `Sharing.All` |
| [`[TesseraUnion(typeof(A), ...)]`](tessera-union.md) | class, interface, field, property | the concrete types a union member may hold |

The source generator also emits three attributes into your assembly: `[assembly: TesseraDefault]`,
`[assembly: TesseraDeclared]` and `[assembly: TesseraCppHeader]`. You never write them; [Generated
attributes](generated.md) explains what they carry.
