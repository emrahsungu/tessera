using System;

namespace Tessera;

/// <summary>
/// Optional. Generates the serializer and the C++ header for this class, record or struct even if no code in its project
/// saves it. Without it, they are generated when the project saves the type (<c>TesseraSerializer.Serialize(value)</c>
/// with the type known), uses it as the type of a field, or derives it from the declared type of a field. On a type
/// that gets them anyway, the attribute changes nothing in its project. What it adds: C++ gets the header even if no C#
/// code saves the type; other projects see the initial values of its fields, and can store it in their fields declared
/// as its base class or interface; and <c>[Tessera("Name")]</c> fixes the name that identifies it in such fields.
/// Documentation: docs/attributes/tessera.md.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class TesseraAttribute : Attribute
{
    /// <summary>Creates the attribute using the C# type name as the stable type name.</summary>
    public TesseraAttribute() { }

    /// <summary>Creates the attribute with a fixed name, which identifies the class in fields declared as its base class or interface.</summary>
    public TesseraAttribute(string name) => Name = name;

    /// <summary>The name that identifies the class in fields declared as its base class or interface (saved as a hash). Defaults to the C# class name.</summary>
    public string? Name { get; }
}

/// <summary>
/// Generates the serializers and C++ headers of the listed types, as <c>[Tessera]</c> on each would: for types only C++
/// reads, types this project saves only through generic code, and types of other projects.
/// <code>[assembly: TesseraRoot(typeof(Monster), typeof(World))]</code> Documentation: docs/attributes/tessera-root.md.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class TesseraRootAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraRootAttribute(params Type[] types) => Types = types;

    /// <summary>The listed types.</summary>
    public Type[] Types { get; }
}

/// <summary>Does not save this field or property.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraIgnoreAttribute : Attribute { }

/// <summary>
/// Saves the field under this name instead of its C# name. Saved data identifies fields by a hash of their name, so
/// keeping the old name here lets you rename the C# field without breaking data saved before.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraNameAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraNameAttribute(string name) => Name = name;

    /// <summary>The saved name.</summary>
    public string Name { get; }
}

/// <summary>
/// Stores this struct's value outside the object, with a 4-byte reference to it, so that equal values are stored once
/// when saving with <c>Sharing.All</c>. Plain structs are otherwise stored inside the object. On a field, a property, or
/// the struct type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Struct)]
public sealed class TesseraSharedAttribute : Attribute { }

/// <summary>
/// Always saves this field, even when it equals its default value (normally such fields are left out, and readers
/// return the default). Number, enum and plain-struct fields are then stored at a fixed position, so C++ reads them
/// with a single memory load.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TesseraKeepDefaultAttribute : Attribute { }

/// <summary>
/// Lists the classes that a field declared as a base class or interface may hold. Without it, such a field may hold
/// every public or internal class of this project that derives from its type, and every class of a referenced project
/// that is marked [Tessera] or listed in [assembly: TesseraRoot].
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class TesseraUnionAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraUnionAttribute(params Type[] types) => Types = types;

    /// <summary>The allowed classes.</summary>
    public Type[] Types { get; }
}

/// <summary>
/// Records the initial value of a field (<c>public short Hp = 100;</c>) in the compiled assembly, so that the source
/// generator of another project that saves the type can read it. Added by the source generator; don't write it.
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

    /// <summary>The C# field or property name.</summary>
    public string Member { get; }

    /// <summary>The default value.</summary>
    public object Value { get; }
}

/// <summary>
/// Records that this assembly generated code for the type, so its <see cref="TesseraDefaultAttribute"/> entries are
/// complete. Added by the source generator; don't write it.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class TesseraDeclaredAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public TesseraDeclaredAttribute(Type type) => Type = type;

    /// <summary>The type.</summary>
    public Type Type { get; }
}

/// <summary>Carries a generated C++ header inside the compiled assembly. Added by the source generator; don't write it.</summary>
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
