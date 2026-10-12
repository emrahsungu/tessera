using System;
using System.Collections.Generic;
using Bench.Models;
using Google.Protobuf;
using Pb = Bench.Pb;

namespace Bench;

/// <summary>
/// Protocol Buffers (Google.Protobuf) writes its generated message classes (benchmarks/schemas/bench.proto), so the
/// benchmark objects are converted to them on every write, as FlatBuffers builds its buffer from them; the
/// "prebuilt messages" case leaves the conversion out. Null strings, values and lists stay absent.
/// </summary>
public static class ProtobufFormats
{
    public static IMessage Convert<T>(T value) => value switch
    {
        Prefab x => Message(x),
        World x => Message(x),
        RecordSet x => Message(x),
        Series x => Message(x),
        Scene x => Message(x),
        Lookup x => Message(x),
        FeatureCollection x => Message(x),
        _ => throw new NotSupportedException("no protobuf message for " + typeof(T)),
    };

    private static Pb.Vec2 V(Vec2 v) => new() { X = v.X, Y = v.Y };

    private static Pb.Vec3 V(Vec3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };

    private static Pb.Vec4 V(Vec4 v) => new() { X = v.X, Y = v.Y, Z = v.Z, W = v.W };

    private static Pb.ObjectRef R(ObjectRef r) => new() { ComponentName = r.ComponentName, Index = r.Index, ObjectRefId = r.ObjectRefId };

    private static Pb.ObjectRef? R(ObjectRef? r) => r is { } x ? R(x) : null;

    private static Pb.ObjectRefList? Refs(List<ObjectRef>? list)
    {
        if (list == null) return null;
        var m = new Pb.ObjectRefList();
        foreach (var r in list) m.Items.Add(R(r));
        return m;
    }

    private static Pb.Int32List? Ints(IEnumerable<int>? list)
    {
        if (list == null) return null;
        var m = new Pb.Int32List();
        m.Items.AddRange(list);
        return m;
    }

    // ------------------------------------------------------------------ prefab

    private static Pb.Prefab Message(Prefab p)
    {
        var m = new Pb.Prefab();
        if (p.Objects != null)
            foreach (var o in p.Objects) m.Objects.Add(Message(o));
        return m;
    }

    private static Pb.PrefabObject Message(PrefabObject o)
    {
        var m = new Pb.PrefabObject
        {
            Children = Ints(o.Children),
            Rotation = V(o.Rotation),
            Position = V(o.Position),
            Scale = V(o.Scale),
            Pivot = V(o.Pivot),
            AnchorMin = V(o.AnchorMin),
            AnchorMax = V(o.AnchorMax),
            SizeDelta = V(o.SizeDelta),
            Id = o.Id,
            Active = o.Active,
        };
        if (o.Name != null) m.Name = o.Name;
        if (o.Components != null)
        {
            var list = new Pb.ComponentList();
            foreach (var c in o.Components) list.Items.Add(Message(c));
            m.Components = list;
        }

        return m;
    }

    private static Pb.Component Message(Component component)
    {
        switch (component)
        {
            case ImageComponent x:
            {
                var m = new Pb.ImageComponent
                {
                    Color = V(x.Color), UvRect = V(x.UvRect), FillMethod = x.FillMethod, FillAmount = x.FillAmount, Enable = x.Enable,
                    RaycastTarget = x.RaycastTarget, PreserveAspect = x.PreserveAspect,
                };
                if (x.Sprite != null) m.Sprite = x.Sprite;
                if (x.Material != null) m.Material = x.Material;
                return new Pb.Component { Image = m };
            }
            case TextComponent x:
            {
                var m = new Pb.TextComponent
                {
                    Color = V(x.Color), FontSize = x.FontSize, LineSpacing = x.LineSpacing, Alignment = x.Alignment, ColorMode = x.ColorMode,
                    Enable = x.Enable, RichText = x.RichText,
                };
                if (x.Text != null) m.Text = x.Text;
                if (x.Font != null) m.Font = x.Font;
                return new Pb.Component { Text = m };
            }
            case ButtonComponent x:
                return new Pb.Component
                {
                    Button = new Pb.ButtonComponent { Target = R(x.Target), OnClick = R(x.OnClick), Interactable = x.Interactable, Transition = x.Transition },
                };
            case LayoutComponent x:
                return new Pb.Component
                {
                    Layout = new Pb.LayoutComponent
                    {
                        Padding = V(x.Padding), Spacing = x.Spacing, ChildAlignment = x.ChildAlignment, ControlWidth = x.ControlWidth,
                        ControlHeight = x.ControlHeight, ExpandWidth = x.ExpandWidth, ExpandHeight = x.ExpandHeight,
                    },
                };
            case ControllerComponent x:
            {
                var m = new Pb.ControllerComponent
                {
                    Sets = Refs(x.Sets), Empties = Refs(x.Empties), Hides = Refs(x.Hides), Images = Refs(x.Images), Icons = Refs(x.Icons),
                    Names = Refs(x.Names), Levels = Refs(x.Levels), Skills = Refs(x.Skills), Texts = Refs(x.Texts), Buttons = Refs(x.Buttons),
                    Icon = R(x.Icon), Glow = R(x.Glow), Level = R(x.Level), Star = R(x.Star), SkillList = R(x.SkillList), Badge = R(x.Badge),
                    Banner = R(x.Banner), Counter = R(x.Counter), Divider = R(x.Divider), Header = R(x.Header), Background = R(x.Background),
                    Group = R(x.Group), Caption = R(x.Caption), Thumbnail = R(x.Thumbnail), Tooltip = R(x.Tooltip),
                };
                if (x.IsPinned is bool a) m.IsPinned = a;
                if (x.ShowTitle is bool b) m.ShowTitle = b;
                if (x.IsFeatured is bool c) m.IsFeatured = c;
                if (x.Locked is bool d) m.Locked = d;
                if (x.Enable is bool e) m.Enable = e;
                return new Pb.Component { Controller = m };
            }
            default:
                throw new InvalidOperationException();
        }
    }

    // ------------------------------------------------------------------ monsters

    private static Pb.Weapon Message(Weapon w)
    {
        var m = new Pb.Weapon { Damage = w.Damage };
        if (w.Name != null) m.Name = w.Name;
        return m;
    }

    private static Pb.World Message(World w)
    {
        var world = new Pb.World();
        if (w.Monsters == null) return world;
        foreach (var x in w.Monsters)
        {
            var m = new Pb.Monster { Hp = x.Hp, Pos = V(x.Pos), Color = (Pb.Color)(int)x.Color, Friendly = x.Friendly };
            if (x.Name != null) m.Name = x.Name;
            if (x.Mana is short mana) m.Mana = mana;
            // Wraps the array without copying it (the objects are not changed while they are written).
            if (x.Inventory != null) m.Inventory = UnsafeByteOperations.UnsafeWrap(x.Inventory);
            if (x.Weapons != null)
            {
                var list = new Pb.WeaponList();
                foreach (var weapon in x.Weapons) list.Items.Add(Message(weapon));
                m.Weapons = list;
            }

            if (x.Equipped != null) m.Equipped = Message(x.Equipped);
            if (x.Path != null)
            {
                var list = new Pb.Vec3List();
                foreach (var v in x.Path) list.Items.Add(V(v));
                m.Path = list;
            }

            world.Monsters.Add(m);
        }

        return world;
    }

    // ------------------------------------------------------------------ records

    private static Pb.RecordSet Message(RecordSet s)
    {
        var set = new Pb.RecordSet();
        if (s.Records == null) return set;
        foreach (var x in s.Records)
        {
            var m = new Pb.Record();
            if (x.I0 is int i0) m.I0 = i0;
            if (x.I1 is int i1) m.I1 = i1;
            if (x.I2 is int i2) m.I2 = i2;
            if (x.I3 is int i3) m.I3 = i3;
            if (x.I4 is int i4) m.I4 = i4;
            if (x.I5 is int i5) m.I5 = i5;
            if (x.I6 is int i6) m.I6 = i6;
            if (x.I7 is int i7) m.I7 = i7;
            if (x.I8 is int i8) m.I8 = i8;
            if (x.I9 is int i9) m.I9 = i9;
            if (x.I10 is int i10) m.I10 = i10;
            if (x.I11 is int i11) m.I11 = i11;
            if (x.I12 is int i12) m.I12 = i12;
            if (x.I13 is int i13) m.I13 = i13;
            if (x.I14 is int i14) m.I14 = i14;
            if (x.I15 is int i15) m.I15 = i15;
            if (x.F0 is float f0) m.F0 = f0;
            if (x.F1 is float f1) m.F1 = f1;
            if (x.F2 is float f2) m.F2 = f2;
            if (x.F3 is float f3) m.F3 = f3;
            if (x.F4 is float f4) m.F4 = f4;
            if (x.F5 is float f5) m.F5 = f5;
            if (x.F6 is float f6) m.F6 = f6;
            if (x.F7 is float f7) m.F7 = f7;
            if (x.D0 is double d0) m.D0 = d0;
            if (x.D1 is double d1) m.D1 = d1;
            if (x.D2 is double d2) m.D2 = d2;
            if (x.D3 is double d3) m.D3 = d3;
            if (x.B0 is bool b0) m.B0 = b0;
            if (x.B1 is bool b1) m.B1 = b1;
            if (x.B2 is bool b2) m.B2 = b2;
            if (x.B3 is bool b3) m.B3 = b3;
            if (x.B4 is bool b4) m.B4 = b4;
            if (x.B5 is bool b5) m.B5 = b5;
            if (x.B6 is bool b6) m.B6 = b6;
            if (x.B7 is bool b7) m.B7 = b7;
            if (x.S0 != null) m.S0 = x.S0;
            if (x.S1 != null) m.S1 = x.S1;
            if (x.S2 != null) m.S2 = x.S2;
            if (x.S3 != null) m.S3 = x.S3;
            set.Records.Add(m);
        }

        return set;
    }

    // ------------------------------------------------------------------ series

    private static Pb.Series Message(Series s)
    {
        var series = new Pb.Series();
        if (s.Samples == null) return series;
        foreach (var x in s.Samples)
        {
            var m = new Pb.Sample { Timestamp = x.Timestamp, Value = x.Value, Status = x.Status, Quality = x.Quality };
            if (x.Note != null) m.Note = x.Note;
            series.Samples.Add(m);
        }

        return series;
    }

    // ------------------------------------------------------------------ scenes

    private static Pb.Scene Message(Scene s)
    {
        var scene = new Pb.Scene();
        if (s.Name != null) scene.Name = s.Name;
        if (s.Nodes == null) return scene;
        foreach (var n in s.Nodes)
        {
            var m = new Pb.Node { Id = n.Id, X = n.X, Y = n.Y, Z = n.Z, Children = Ints(n.Children) };
            if (n.Name != null) m.Name = n.Name;
            if (n.Active is bool active) m.Active = active;
            if (n.Parent is int parent) m.Parent = parent;
            scene.Nodes.Add(m);
        }

        return scene;
    }

    // ------------------------------------------------------------------ lookup

    private static Pb.Lookup Message(Lookup x)
    {
        var m = new Pb.Lookup();
        if (x.Items != null)
            foreach (var kv in x.Items) m.Items.Add(new Pb.ItemEntry { Key = kv.Key, Value = new Pb.Stock { Count = kv.Value.Count, Weight = kv.Value.Weight } });
        if (x.Prices != null)
            foreach (var kv in x.Prices) m.Prices.Add(new Pb.PriceEntry { Key = kv.Key, Value = kv.Value });
        return m;
    }

    // ------------------------------------------------------------------ canada

    private static Pb.FeatureCollection Message(FeatureCollection c)
    {
        var m = new Pb.FeatureCollection();
        if (c.Type != null) m.Type = c.Type;
        if (c.Features == null) return m;
        foreach (var f in c.Features)
        {
            var feature = new Pb.Feature();
            if (f.Type != null) feature.Type = f.Type;
            if (f.Properties != null)
                foreach (var kv in f.Properties) feature.Properties.Add(new Pb.PropertyEntry { Key = kv.Key, Value = kv.Value });
            if (f.Geometry != null)
            {
                var geometry = new Pb.Geometry();
                if (f.Geometry.Type != null) geometry.Type = f.Geometry.Type;
                if (f.Geometry.Coordinates != null)
                {
                    foreach (var ring in f.Geometry.Coordinates)
                    {
                        var r = new Pb.Ring();
                        foreach (var p in ring) r.Points.Add(new Pb.Point { X = p.X, Y = p.Y });
                        geometry.Coordinates.Add(r);
                    }
                }

                feature.Geometry = geometry;
            }

            m.Features.Add(feature);
        }

        return m;
    }
}
