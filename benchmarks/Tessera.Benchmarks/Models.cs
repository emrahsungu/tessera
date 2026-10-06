// Benchmark models. The same classes are written by Tessera and MessagePack ([MessagePackObject]). Program.cs writes
// them through generic code, which hides the types from the generator, so they are marked [Tessera].
// FlatBuffers is written from them with its builder API (FbWriters.cs, schema in benchmarks/schemas/bench.fbs).
using System.Collections.Generic;
using Tessera;
using MessagePack;

namespace Bench.Models;

[MessagePackObject]
public struct Vec2
{
    [Key(0)] public float X;
    [Key(1)] public float Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }
}

[MessagePackObject]
public struct Vec3
{
    [Key(0)] public float X;
    [Key(1)] public float Y;
    [Key(2)] public float Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

[MessagePackObject]
public struct Vec4
{
    [Key(0)] public float X;
    [Key(1)] public float Y;
    [Key(2)] public float Z;
    [Key(3)] public float W;

    public Vec4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
}

/// <summary>Reference to another object in the prefab: component name hash, index and id.</summary>
[MessagePackObject]
public struct ObjectRef
{
    [Key(0)] public uint ComponentName;
    [Key(1)] public short Index;
    [Key(2)] public short ObjectRefId;
}

// ---------------------------------------------------------------- UI prefab

[Tessera]
[Union(0, typeof(ImageComponent))]
[Union(1, typeof(TextComponent))]
[Union(2, typeof(ButtonComponent))]
[Union(3, typeof(LayoutComponent))]
[Union(4, typeof(ControllerComponent))]
public abstract class Component
{
}

[Tessera, MessagePackObject]
public sealed class ImageComponent : Component
{
    [Key(0)] public string? Sprite;
    [Key(1)] public string? Material;
    [Key(2)] public Vec4 Color;
    [Key(3)] public Vec4 UvRect;
    [Key(4)] public int FillMethod;
    [Key(5)] public float FillAmount;
    [Key(6)] public bool Enable;
    [Key(7)] public bool RaycastTarget;
    [Key(8)] public bool PreserveAspect;
}

[Tessera, MessagePackObject]
public sealed class TextComponent : Component
{
    [Key(0)] public string? Text;
    [Key(1)] public string? Font;
    [Key(2)] public Vec4 Color;
    [Key(3)] public float FontSize;
    [Key(4)] public float LineSpacing;
    [Key(5)] public short Alignment;
    [Key(6)] public byte ColorMode;
    [Key(7)] public bool Enable;
    [Key(8)] public bool RichText;
}

[Tessera, MessagePackObject]
public sealed class ButtonComponent : Component
{
    [Key(0)] public ObjectRef Target;
    [Key(1)] public ObjectRef OnClick;
    [Key(2)] public bool Interactable;
    [Key(3)] public byte Transition;
}

[Tessera, MessagePackObject]
public sealed class LayoutComponent : Component
{
    [Key(0)] public Vec4 Padding;
    [Key(1)] public float Spacing;
    [Key(2)] public int ChildAlignment;
    [Key(3)] public bool ControlWidth;
    [Key(4)] public bool ControlHeight;
    [Key(5)] public bool ExpandWidth;
    [Key(6)] public bool ExpandHeight;
}

/// <summary>Wide and sparse: 30 members, most of them absent.</summary>
[Tessera, MessagePackObject]
public sealed class ControllerComponent : Component
{
    [Key(0)] public List<ObjectRef>? Sets;
    [Key(1)] public List<ObjectRef>? Empties;
    [Key(2)] public List<ObjectRef>? Hides;
    [Key(3)] public List<ObjectRef>? Images;
    [Key(4)] public List<ObjectRef>? Icons;
    [Key(5)] public List<ObjectRef>? Names;
    [Key(6)] public List<ObjectRef>? Levels;
    [Key(7)] public List<ObjectRef>? Skills;
    [Key(8)] public List<ObjectRef>? Texts;
    [Key(9)] public List<ObjectRef>? Buttons;
    [Key(10)] public ObjectRef? Icon;
    [Key(11)] public ObjectRef? Glow;
    [Key(12)] public ObjectRef? Level;
    [Key(13)] public ObjectRef? Star;
    [Key(14)] public ObjectRef? SkillList;
    [Key(15)] public ObjectRef? Badge;
    [Key(16)] public ObjectRef? Banner;
    [Key(17)] public ObjectRef? Counter;
    [Key(18)] public ObjectRef? Divider;
    [Key(19)] public ObjectRef? Header;
    [Key(20)] public ObjectRef? Background;
    [Key(21)] public ObjectRef? Group;
    [Key(22)] public ObjectRef? Caption;
    [Key(23)] public ObjectRef? Thumbnail;
    [Key(24)] public ObjectRef? Tooltip;
    [Key(25)] public bool? IsPinned;
    [Key(26)] public bool? ShowTitle;
    [Key(27)] public bool? IsFeatured;
    [Key(28)] public bool? Locked;
    [Key(29)] public bool? Enable;
}

[Tessera, MessagePackObject]
public sealed class PrefabObject
{
    [Key(0)] public string? Name;
    [Key(1)] public List<Component>? Components;
    [Key(2)] public List<int>? Children;
    [Key(3)] public Vec4 Rotation;
    [Key(4)] public Vec3 Position;
    [Key(5)] public Vec3 Scale;
    [Key(6)] public Vec2 Pivot;
    [Key(7)] public Vec2 AnchorMin;
    [Key(8)] public Vec2 AnchorMax;
    [Key(9)] public Vec2 SizeDelta;
    [Key(10)] public int Id;
    [Key(11)] public bool Active;
}

[Tessera, MessagePackObject]
public sealed class Prefab
{
    [Key(0)] public List<PrefabObject>? Objects;
}

// ---------------------------------------------------------------- game monsters

public enum Color : byte { Red, Green, Blue }

[Tessera, MessagePackObject]
public sealed class Weapon
{
    [Key(0)] public string? Name;
    [Key(1)] public short Damage;
}

[Tessera, MessagePackObject]
public sealed class Monster
{
    [Key(0)] public string? Name;
    [Key(1)] public short Hp = 100;
    [Key(2)] public short? Mana;
    [Key(3)] public Vec3 Pos;
    [Key(4)] public Color Color = Color.Blue;
    [Key(5)] public byte[]? Inventory;
    [Key(6)] public List<Weapon>? Weapons;
    [Key(7)] public Weapon? Equipped;
    [Key(8)] public List<Vec3>? Path;
    [Key(9)] public bool Friendly;
}

[Tessera, MessagePackObject]
public sealed class World
{
    [Key(0)] public List<Monster>? Monsters;
}

// ---------------------------------------------------------------- sparse records

[Tessera, MessagePackObject]
public sealed class Record
{
    [Key(0)] public int? I0;
    [Key(1)] public int? I1;
    [Key(2)] public int? I2;
    [Key(3)] public int? I3;
    [Key(4)] public int? I4;
    [Key(5)] public int? I5;
    [Key(6)] public int? I6;
    [Key(7)] public int? I7;
    [Key(8)] public int? I8;
    [Key(9)] public int? I9;
    [Key(10)] public int? I10;
    [Key(11)] public int? I11;
    [Key(12)] public int? I12;
    [Key(13)] public int? I13;
    [Key(14)] public int? I14;
    [Key(15)] public int? I15;
    [Key(16)] public float? F0;
    [Key(17)] public float? F1;
    [Key(18)] public float? F2;
    [Key(19)] public float? F3;
    [Key(20)] public float? F4;
    [Key(21)] public float? F5;
    [Key(22)] public float? F6;
    [Key(23)] public float? F7;
    [Key(24)] public double? D0;
    [Key(25)] public double? D1;
    [Key(26)] public double? D2;
    [Key(27)] public double? D3;
    [Key(28)] public bool? B0;
    [Key(29)] public bool? B1;
    [Key(30)] public bool? B2;
    [Key(31)] public bool? B3;
    [Key(32)] public bool? B4;
    [Key(33)] public bool? B5;
    [Key(34)] public bool? B6;
    [Key(35)] public bool? B7;
    [Key(36)] public string? S0;
    [Key(37)] public string? S1;
    [Key(38)] public string? S2;
    [Key(39)] public string? S3;
}

[Tessera, MessagePackObject]
public sealed class RecordSet
{
    [Key(0)] public List<Record>? Records;
}

// ---------------------------------------------------------------- dense time series

[Tessera, MessagePackObject]
public sealed class Sample
{
    [Key(0)] public long Timestamp;
    [Key(1)] public double Value;
    [Key(2)] public int Status;
    [Key(3)] public float Quality;
    [Key(4)] public string? Note;
}

[Tessera, MessagePackObject]
public sealed class Series
{
    [Key(0)] public List<Sample>? Samples;
}

// The same samples with the values that are (almost) always set as fixed cells: a constant position each, no presence
// bits. Status is 0 (its default, so not stored) in 90% of samples, so it stays an ordinary cell.
[Tessera]
public sealed class FixedSample
{
    [TesseraKeepDefault] public long Timestamp;
    [TesseraKeepDefault] public double Value;
    public int Status;
    [TesseraKeepDefault] public float Quality;
    public string? Note;
}

[Tessera]
public sealed class FixedSeries
{
    public List<FixedSample>? Samples;

    public static FixedSeries From(Series s) => new()
    {
        Samples = s.Samples?.ConvertAll(x => new FixedSample { Timestamp = x.Timestamp, Value = x.Value, Status = x.Status, Quality = x.Quality, Note = x.Note }),
    };
}

// ---------------------------------------------------------------- scene graphs

[Tessera, MessagePackObject]
public sealed class Node
{
    [Key(0)] public int Id;
    [Key(1)] public string? Name;
    [Key(2)] public bool? Active;
    [Key(3)] public float X;
    [Key(4)] public float Y;
    [Key(5)] public float Z;
    [Key(6)] public int? Parent;
    [Key(7)] public int[]? Children;
}

[Tessera, MessagePackObject]
public sealed class Scene
{
    [Key(0)] public Node[]? Nodes;
    [Key(1)] public string? Name;
}

// ---------------------------------------------------------------- dictionaries (keyed lookups)

[Tessera, MessagePackObject]
public sealed class Stock
{
    [Key(0)] public int Count;
    [Key(1)] public float Weight;
}

[Tessera, MessagePackObject]
public sealed class Lookup
{
    [Key(0)] public Dictionary<string, Stock>? Items;
    [Key(1)] public Dictionary<int, double>? Prices;
}

// ---------------------------------------------------------------- GeoJSON (canada.json)

/// <summary>A GeoJSON position, [longitude, latitude].</summary>
[MessagePackObject]
public struct Point
{
    [Key(0)] public double X;
    [Key(1)] public double Y;

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }
}

[Tessera, MessagePackObject]
public sealed class Geometry
{
    [Key(0)] public string? Type;
    [Key(1)] public List<List<Point>>? Coordinates;  // a polygon: rings of points
}

[Tessera, MessagePackObject]
public sealed class Feature
{
    [Key(0)] public string? Type;
    [Key(1)] public Dictionary<string, string>? Properties;
    [Key(2)] public Geometry? Geometry;
}

[Tessera, MessagePackObject]
public sealed class FeatureCollection
{
    [Key(0)] public string? Type;
    [Key(1)] public List<Feature>? Features;
}