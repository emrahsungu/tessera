# `[TesseraShared]`

**What it does:** stores a struct field's value outside the object, with a 4-byte reference to it. When you save with
`Sharing.All`, equal values are then stored once, and every object that holds one refers to the same copy.

```csharp
[TesseraShared]                         // on the struct: every field of this type
public struct Material
{
    public Color Albedo, Emission;
    public float Roughness, Metalness, Opacity;
    public int ShaderId, Flags;
    // ... 64 bytes in all
}

public class MeshNode
{
    public Material Material;              // stored once per distinct value, with Sharing.All
    [TesseraShared] public Matrix4 Pivot;  // or on a single field
}
```

A plain struct (only numbers, enums and other plain structs inside) is normally stored inside the object that holds
it, like a C struct member. That is fastest to read, but a value repeated in 1,000 objects is stored 1,000 times.

## When you need it

- **A large struct value repeats across many objects**, and you save with `Sharing.All`: materials, transforms, style
  blocks. Each repetition then costs 4 bytes instead of the whole struct.

## When not to use it

- **Without `Sharing.All`**, nothing is deduplicated: every value is still stored once per object, plus the 4-byte
  reference, and reading costs one more memory load.
- **For small structs** (a `Vec3`, a color), the 4-byte reference saves little or nothing, and inline values read
  faster.

## Details

- C++ reads the field exactly like an inline struct: the accessor returns `const Material&` (or a copy, when the
  struct has a `bool` field). Following the reference costs one more load.
- Values are compared byte for byte.
- Adding or removing the attribute changes how the field is stored, so the other version of your code reads the field
  as missing. Decide before saving data that must stay readable
  ([Changing your classes](../guide/versioning.md)).

See also: [`Sharing`](../options/sharing.md).
