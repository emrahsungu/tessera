# Attributes

You don't need any attribute: Tessera generates code for the classes you serialize, and for the types of their fields
([Which types get generated code](../guide/generated-types.md)). Attributes change specific behavior:

| Attribute | On | What it does |
|---|---|---|
| [`[Tessera]`](tessera.md) | class, record, struct | Generates the serializer and the C++ header for the type even if no code serializes it directly, for example a type only C++ reads. |
| [`[Tessera("Name")]`](tessera.md) | class, record, struct | The same, and sets the name stored in the buffer to identify this class when a field is declared as its base class or interface. Lets you rename the class without breaking saved data. |
| [`[assembly: TesseraRoot(typeof(A), ...)]`](tessera-root.md) | assembly | Same as `[Tessera]`, for types you list in one place instead of annotating them: types from other projects, or types your code only serializes through a generic method. |
| [`[TesseraIgnore]`](tessera-ignore.md) | field, property | Does not save the field. (`[IgnoreDataMember]` and `[NonSerialized]` work too.) |
| [`[TesseraName("OldName")]`](tessera-name.md) | field, property | Saves the field under another name. Lets you rename a field in C# while data saved with the old name still loads. |
| [`[DefaultValue(x)]`](default-value.md), or an initializer (`= 100`) | field, property | The field's default value. A field equal to its default is not written to the buffer, and C++ returns the default when the field is missing. |
| [`[TesseraKeepDefault]`](tessera-keep-default.md) | field, property | Always writes the field, even when it equals its default. Number, enum and plain-struct fields are then stored at a fixed position, so C++ reads them with a single memory load. |
| [`[TesseraShared]`](tessera-shared.md) | struct, field, property | Stores the struct value separately, with a reference to it in the object, so equal values are stored only once when writing with `Sharing.All`. |
| [`[TesseraUnion(typeof(A), ...)]`](tessera-union.md) | class, interface, field, property | Lists which derived classes a field declared as a base class or interface may hold. Without it, every derived class in the project is allowed. |

The source generator also adds three attributes to your compiled assembly, which you never write yourself:
[generated attributes](generated.md).
