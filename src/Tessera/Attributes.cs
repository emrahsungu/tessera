using System;

namespace Tessera;

/// <summary>
/// Optional. Makes a class, record or struct a model even when nothing in its project serializes it. Without it, a type
/// is a model when its project serializes it (<c>TesseraSerializer.Serialize(value)</c> with a known type), when a
/// model uses it, or when it derives from the type of an abstract or interface member of a model. On a type that is
/// already a model, a bare <c>[Tessera]</c> changes neither the buffers nor the generated code of its project. What it
/// adds: C++ gets the type's header even if no C# code writes it, other projects see its member initializers and find
/// it as a union member, and <c>[Tessera("Name")]</c> fixes its union tag.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class TesseraAttribute : Attribute
{
    /// <summary>Creates the attribute using the C# type name as the stable type name.</summary>
    public TesseraAttribute() { }

    /// <summary>Creates the attribute with an explicit stable type name (used for union tags).</summary>
    public TesseraAttribute(string name) => Name = name;

    /// <summary>Stable type name. Defaults to the C# type name. Only union tags depend on it.</summary>
    public string? Name { get; }
}

/// <summary>
/// Makes types models of this project without editing them: types that only C++ reads, types this project does not
/// serialize itself, and types of other projects. For a type declared in this project it has the same effect as
/// <c>[Tessera]</c>. <code>[assembly: TesseraRoot(typeof(Monster), typeof(World))]</code>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class TesseraRootAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraRootAttribute(params Type[] types) => Types = types;

    /// <summary>The model types.</summary>
    public Type[] Types { get; }
}

/// <summary>Excludes a field or property from serialization.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraIgnoreAttribute : Attribute { }

/// <summary>
/// Sets the stable wire name of a member. Member identity on the wire is a hash of this name, so keeping the old name
/// here lets you rename the C# member without breaking existing buffers.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraNameAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraNameAttribute(string name) => Name = name;

    /// <summary>The wire name.</summary>
    public string Name { get; }
}

/// <summary>
/// Stores a fixed-size struct member out of line behind a 4-byte offset so equal values are written once (when sharing
/// is enabled). By default structs are stored inline. Can be placed on a member or on the struct type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Struct)]
public sealed class TesseraSharedAttribute : Attribute { }

/// <summary>
/// Always writes a non-nullable member, even when it equals its default value. Without it, members equal to their
/// default are omitted and readers return the default. Scalars, enums and inline structs marked with it are stored at
/// a fixed position without a presence bit, so C++ reads them with a single load.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraKeepDefaultAttribute : Attribute { }

/// <summary>
/// Declares the concrete types that a polymorphic (abstract or interface) member may hold. Without it the generator
/// uses every public or internal non-abstract type of this project that derives from the member type, and every type
/// that a referenced project marks with [Tessera] or lists in [assembly: TesseraRoot] and that derives from it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class TesseraUnionAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraUnionAttribute(params Type[] types) => Types = types;

    /// <summary>The allowed concrete types.</summary>
    public Type[] Types { get; }
}

/// <summary>
/// Records a member's declared default (its initializer, e.g. <c>public short Hp = 100;</c>) in the compiled assembly,
/// where the source generator of another model assembly that uses the type can read it. Emitted by the source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class TesseraDefaultAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraDefaultAttribute(Type type, string member, object value)
    {
        Type = type;
        Member = member;
        Value = value;
    }

    /// <summary>The type that declares the member.</summary>
    public Type Type { get; }

    /// <summary>The C# member name.</summary>
    public string Member { get; }

    /// <summary>The default value.</summary>
    public object Value { get; }
}

/// <summary>
/// Records that the source generator of this assembly treated a type declared here as a model, so the member defaults
/// it recorded in <see cref="TesseraDefaultAttribute"/> are complete. Emitted by the source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class TesseraDeclaredAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraDeclaredAttribute(Type type) => Type = type;

    /// <summary>The model type.</summary>
    public Type Type { get; }
}

/// <summary>Carries a generated C++ header inside the compiled assembly. Emitted by the source generator.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class TesseraCppHeaderAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraCppHeaderAttribute(string fileName, string content)
    {
        FileName = fileName;
        Content = content;
    }

    /// <summary>Suggested header file name.</summary>
    public string FileName { get; }

    /// <summary>Header text.</summary>
    public string Content { get; }
}
