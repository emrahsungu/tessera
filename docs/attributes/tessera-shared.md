# `[TesseraShared]`

```csharp
[TesseraShared]                         // on the struct: every member of this type
public struct Material
{
    public Color Albedo, Emission;
    public float Roughness, Metalness, Opacity;
    public int ShaderId, Flags;
    // ... 64 bytes in all
}

public class MeshNode
{
    public Material Material;            // stored once per distinct value, with Sharing.All
    [TesseraShared] public Matrix4 Pivot;  // or on one member
}
```

On a plain struct type, or on a field or property of a plain struct type. Plain structs are normally stored inline,
inside the object. A shared one is stored out of line, behind a 4-byte offset, so that with `Sharing.All` equal values
are stored once and every object that holds one points to the same copy.

## Use it when

- **A large struct repeats across many objects**, and you write with `Sharing.All`: materials, transforms, style
  blocks. Each repetition then costs 4 bytes instead of the struct's size.

## When not to use it

- **Without `Sharing.All`**: nothing is deduplicated, so every value is still stored once per object, now with an extra
  4-byte offset and one more indirection on every read.
- **For small structs** (a `Vec3`, a color): the 4-byte offset saves little or nothing, and inline values read faster.

## Details

- C++ reads a shared struct exactly like an inline one: the accessor returns `const Material&` (a copy, if the struct
  has a `bool` field). It costs one more load, to follow the offset.
- Adding or removing the attribute changes the member's kind on the wire, so readers built from the other version see
  the member as absent. Decide before buffers that must stay readable exist
  ([Schema evolution](../guide/schema-evolution.md)).
- Equal values are compared by content, byte for byte.

See also: [Sharing](../guide/writing.md#sharing).
