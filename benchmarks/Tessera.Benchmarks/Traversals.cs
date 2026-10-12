using System;
using System.Runtime.CompilerServices;
using Bench.Models;
using Google.FlatBuffers;
using Tessera;
using Tessera.Generated.Tessera_Benchmarks;
using Fb = Bench.Fb;

namespace Bench;

/// <summary>
/// The .NET read benchmark's traversals of buffers read in place: every field, through Tessera's read types and through
/// FlatBuffers' generated accessors, mixed into the same checksum as <see cref="Checksum"/> (and the C++ readers), so a
/// traversal that skips or misreads something fails the benchmark. Strings mix their UTF-8 length and first byte without
/// being decoded, as in C++.
/// </summary>
public static class Traversals
{
    /// <summary><see cref="Checksum"/>'s hash, as a struct.</summary>
    private struct Hash
    {
        private const ulong AbsentMark = 0x9E3779B97F4A7C15UL;
        private ulong _h;
        private ulong _counts;

        public static Hash Start() => new() { _h = 0xCBF29CE484222325UL };

        public readonly ulong Value => _h ^ unchecked(_counts * 0x9E3779B97F4A7C15UL);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Mix(ulong v) => _h = unchecked((_h ^ v) * 0x100000001B3UL);

        public void Count(int n) => _counts += (ulong)n;

        public void Absent() => Mix(AbsentMark);

        public void I(long v) => Mix(unchecked((ulong)v));

        public void U(ulong v) => Mix(v);

        public void F(float v) => Mix(BitConverter.SingleToUInt32Bits(v));

        public void D(double v) => Mix(BitConverter.DoubleToUInt64Bits(v));

        public void B(bool v) => Mix(v ? 1UL : 0UL);

        public void S(bool present, ReadOnlySpan<byte> utf8)
        {
            if (!present)
            {
                Absent();
                return;
            }

            Mix((ulong)utf8.Length);
            Mix(utf8.Length > 0 ? utf8[0] : 0UL);
        }

        public void S(ArraySegment<byte>? utf8)
        {
            if (utf8 is not { } s)
            {
                Absent();
                return;
            }

            Mix((ulong)s.Count);
            Mix(s.Count > 0 ? s[0] : 0UL);
        }

        public void OptI(long? v)
        {
            if (v.HasValue) I(v.GetValueOrDefault());
            else Absent();
        }

        public void OptF(float? v)
        {
            if (v.HasValue) F(v.GetValueOrDefault());
            else Absent();
        }

        public void OptD(double? v)
        {
            if (v.HasValue) D(v.GetValueOrDefault());
            else Absent();
        }

        public void OptB(bool? v)
        {
            if (v.HasValue) B(v.GetValueOrDefault());
            else Absent();
        }

        public void V2(Vec2 v)
        {
            F(v.X);
            F(v.Y);
        }

        public void V3(Vec3 v)
        {
            F(v.X);
            F(v.Y);
            F(v.Z);
        }

        public void V4(Vec4 v)
        {
            F(v.X);
            F(v.Y);
            F(v.Z);
            F(v.W);
        }

        public void Ref(ObjectRef r)
        {
            U(r.ComponentName);
            I(r.Index);
            I(r.ObjectRefId);
        }

        public void Refs(bool present, ReadOnlySpan<ObjectRef> refs)
        {
            if (!present)
            {
                Absent();
                return;
            }

            Count(refs.Length);
            foreach (ObjectRef r in refs) Ref(r);
        }

        public void OptRef(ObjectRef? r)
        {
            if (r.HasValue) Ref(r.GetValueOrDefault());
            else Absent();
        }

        public void Ints(bool present, ReadOnlySpan<int> values)
        {
            if (!present)
            {
                Absent();
                return;
            }

            Count(values.Length);
            foreach (int x in values) I(x);
        }
    }

    // ------------------------------------------------------------------ Tessera's read types

    public static ulong Tessera(PrefabView p)
    {
        var c = Hash.Start();
        if (!p.HasObjects)
        {
            c.Absent();
            return c.Value;
        }

        PrefabObjectViewList objects = p.Objects;
        c.Count(objects.Count);
        foreach (PrefabObjectView o in objects)
        {
            c.S(o.HasName, o.NameUtf8);
            if (!o.HasComponents) c.Absent();
            else
            {
                ComponentViewList components = o.Components;
                c.Count(components.Count);
                foreach (ComponentView x in components) Component(ref c, x);
            }

            c.Ints(o.HasChildren, o.Children);
            c.V4(o.Rotation);
            c.V3(o.Position);
            c.V3(o.Scale);
            c.V2(o.Pivot);
            c.V2(o.AnchorMin);
            c.V2(o.AnchorMax);
            c.V2(o.SizeDelta);
            c.I(o.Id);
            c.B(o.Active);
        }

        return c.Value;
    }

    private static void Component(ref Hash c, ComponentView component)
    {
        switch (component.Tag)
        {
            case ComponentView.Tags.ImageComponent:
            {
                ImageComponentView x = component.AsImageComponent;
                c.U(0);
                c.S(x.HasSprite, x.SpriteUtf8);
                c.S(x.HasMaterial, x.MaterialUtf8);
                c.V4(x.Color);
                c.V4(x.UvRect);
                c.I(x.FillMethod);
                c.F(x.FillAmount);
                c.B(x.Enable);
                c.B(x.RaycastTarget);
                c.B(x.PreserveAspect);
                break;
            }
            case ComponentView.Tags.TextComponent:
            {
                TextComponentView x = component.AsTextComponent;
                c.U(1);
                c.S(x.HasText, x.TextUtf8);
                c.S(x.HasFont, x.FontUtf8);
                c.V4(x.Color);
                c.F(x.FontSize);
                c.F(x.LineSpacing);
                c.I(x.Alignment);
                c.U(x.ColorMode);
                c.B(x.Enable);
                c.B(x.RichText);
                break;
            }
            case ComponentView.Tags.ButtonComponent:
            {
                ButtonComponentView x = component.AsButtonComponent;
                c.U(2);
                c.Ref(x.Target);
                c.Ref(x.OnClick);
                c.B(x.Interactable);
                c.U(x.Transition);
                break;
            }
            case ComponentView.Tags.LayoutComponent:
            {
                LayoutComponentView x = component.AsLayoutComponent;
                c.U(3);
                c.V4(x.Padding);
                c.F(x.Spacing);
                c.I(x.ChildAlignment);
                c.B(x.ControlWidth);
                c.B(x.ControlHeight);
                c.B(x.ExpandWidth);
                c.B(x.ExpandHeight);
                break;
            }
            case ComponentView.Tags.ControllerComponent:
            {
                ControllerComponentView x = component.AsControllerComponent;
                c.U(4);
                c.Refs(x.HasSets, x.Sets);
                c.Refs(x.HasEmpties, x.Empties);
                c.Refs(x.HasHides, x.Hides);
                c.Refs(x.HasImages, x.Images);
                c.Refs(x.HasIcons, x.Icons);
                c.Refs(x.HasNames, x.Names);
                c.Refs(x.HasLevels, x.Levels);
                c.Refs(x.HasSkills, x.Skills);
                c.Refs(x.HasTexts, x.Texts);
                c.Refs(x.HasButtons, x.Buttons);
                c.OptRef(x.Icon);
                c.OptRef(x.Glow);
                c.OptRef(x.Level);
                c.OptRef(x.Star);
                c.OptRef(x.SkillList);
                c.OptRef(x.Badge);
                c.OptRef(x.Banner);
                c.OptRef(x.Counter);
                c.OptRef(x.Divider);
                c.OptRef(x.Header);
                c.OptRef(x.Background);
                c.OptRef(x.Group);
                c.OptRef(x.Caption);
                c.OptRef(x.Thumbnail);
                c.OptRef(x.Tooltip);
                c.OptB(x.IsPinned);
                c.OptB(x.ShowTitle);
                c.OptB(x.IsFeatured);
                c.OptB(x.Locked);
                c.OptB(x.Enable);
                break;
            }
            default:
                throw new InvalidOperationException("unknown component");
        }
    }

    public static ulong Tessera(WorldView w)
    {
        var c = Hash.Start();
        if (!w.HasMonsters)
        {
            c.Absent();
            return c.Value;
        }

        MonsterViewList monsters = w.Monsters;
        c.Count(monsters.Count);
        foreach (MonsterView m in monsters)
        {
            c.S(m.HasName, m.NameUtf8);
            c.I(m.Hp);
            c.OptI(m.Mana);
            c.V3(m.Pos);
            c.U((ulong)m.Color);
            if (!m.HasInventory) c.Absent();
            else
            {
                ReadOnlySpan<byte> inventory = m.Inventory;
                c.Count(inventory.Length);
                foreach (byte x in inventory) c.U(x);
            }

            if (!m.HasWeapons) c.Absent();
            else
            {
                WeaponViewList weapons = m.Weapons;
                c.Count(weapons.Count);
                foreach (WeaponView x in weapons)
                {
                    c.S(x.HasName, x.NameUtf8);
                    c.I(x.Damage);
                }
            }

            WeaponView equipped = m.Equipped;
            if (equipped.IsNull) c.Absent();
            else
            {
                c.S(equipped.HasName, equipped.NameUtf8);
                c.I(equipped.Damage);
            }

            if (!m.HasPath) c.Absent();
            else
            {
                ReadOnlySpan<Vec3> path = m.Path;
                c.Count(path.Length);
                foreach (Vec3 v in path) c.V3(v);
            }

            c.B(m.Friendly);
        }

        return c.Value;
    }

    public static ulong Tessera(RecordSetView s)
    {
        var c = Hash.Start();
        if (!s.HasRecords)
        {
            c.Absent();
            return c.Value;
        }

        RecordViewList records = s.Records;
        c.Count(records.Count);
        foreach (RecordView x in records)
        {
            c.OptI(x.I0); c.OptI(x.I1); c.OptI(x.I2); c.OptI(x.I3); c.OptI(x.I4); c.OptI(x.I5); c.OptI(x.I6); c.OptI(x.I7);
            c.OptI(x.I8); c.OptI(x.I9); c.OptI(x.I10); c.OptI(x.I11); c.OptI(x.I12); c.OptI(x.I13); c.OptI(x.I14); c.OptI(x.I15);
            c.OptF(x.F0); c.OptF(x.F1); c.OptF(x.F2); c.OptF(x.F3); c.OptF(x.F4); c.OptF(x.F5); c.OptF(x.F6); c.OptF(x.F7);
            c.OptD(x.D0); c.OptD(x.D1); c.OptD(x.D2); c.OptD(x.D3);
            c.OptB(x.B0); c.OptB(x.B1); c.OptB(x.B2); c.OptB(x.B3); c.OptB(x.B4); c.OptB(x.B5); c.OptB(x.B6); c.OptB(x.B7);
            c.S(x.HasS0, x.S0Utf8);
            c.S(x.HasS1, x.S1Utf8);
            c.S(x.HasS2, x.S2Utf8);
            c.S(x.HasS3, x.S3Utf8);
        }

        return c.Value;
    }

    public static ulong Tessera(LookupView x)
    {
        var c = Hash.Start();
        if (!x.HasItems) c.Absent();
        else
        {
            StringStockViewMap items = x.Items;
            TesseraStringList keys = items.Keys;
            StockViewList values = items.Values;
            c.Count(items.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                c.S(!keys.IsNull(i), keys.GetUtf8(i));
                StockView stock = values[i];
                c.I(stock.Count);
                c.F(stock.Weight);
            }
        }

        if (!x.HasPrices) c.Absent();
        else
        {
            Int32DoubleMap prices = x.Prices;
            ReadOnlySpan<int> keys = prices.Keys;
            ReadOnlySpan<double> values = prices.Values;
            c.Count(prices.Count);
            for (int i = 0; i < keys.Length; i++)
            {
                c.I(keys[i]);
                c.D(values[i]);
            }
        }

        return c.Value;
    }

    public static ulong Tessera(SeriesView s)
    {
        var c = Hash.Start();
        if (!s.HasSamples)
        {
            c.Absent();
            return c.Value;
        }

        SampleViewList samples = s.Samples;
        c.Count(samples.Count);
        foreach (SampleView x in samples)
        {
            c.I(x.Timestamp);
            c.D(x.Value);
            c.I(x.Status);
            c.F(x.Quality);
            c.S(x.HasNote, x.NoteUtf8);
        }

        return c.Value;
    }

    public static ulong Tessera(SceneView s)
    {
        var c = Hash.Start();
        if (!s.HasNodes) c.Absent();
        else
        {
            NodeViewList nodes = s.Nodes;
            c.Count(nodes.Count);
            foreach (NodeView n in nodes)
            {
                c.I(n.Id);
                c.S(n.HasName, n.NameUtf8);
                c.OptB(n.Active);
                c.F(n.X);
                c.F(n.Y);
                c.F(n.Z);
                c.OptI(n.Parent);
                c.Ints(n.HasChildren, n.Children);
            }
        }

        c.S(s.HasName, s.NameUtf8);
        return c.Value;
    }

    public static ulong Tessera(FeatureCollectionView x)
    {
        var c = Hash.Start();
        c.S(x.HasType, x.TypeUtf8);
        if (!x.HasFeatures)
        {
            c.Absent();
            return c.Value;
        }

        FeatureViewList features = x.Features;
        c.Count(features.Count);
        foreach (FeatureView f in features)
        {
            c.S(f.HasType, f.TypeUtf8);
            if (!f.HasProperties) c.Absent();
            else
            {
                StringStringMap properties = f.Properties;
                TesseraStringList keys = properties.Keys, values = properties.Values;
                c.Count(properties.Count);
                for (int i = 0; i < keys.Count; i++)
                {
                    c.S(!keys.IsNull(i), keys.GetUtf8(i));
                    c.S(!values.IsNull(i), values.GetUtf8(i));
                }
            }

            GeometryView g = f.Geometry;
            if (g.IsNull)
            {
                c.Absent();
                continue;
            }

            c.S(g.HasType, g.TypeUtf8);
            if (!g.HasCoordinates)
            {
                c.Absent();
                continue;
            }

            TesseraSpanList<Point> rings = g.Coordinates;
            c.Count(rings.Count);
            for (int i = 0; i < rings.Count; i++)
            {
                if (rings.IsNull(i))
                {
                    c.Absent();
                    continue;
                }

                ReadOnlySpan<Point> points = rings[i];
                c.Count(points.Length);
                foreach (Point p in points)
                {
                    c.D(p.X);
                    c.D(p.Y);
                }
            }
        }

        return c.Value;
    }

    // ------------------------------------------------------------------ FlatBuffers' accessors

    /// <summary>Whether field <paramref name="field"/> (its position in the schema; a union counts twice) is stored.</summary>
    private static bool Has<T>(T table, int field) where T : struct, IFlatbufferObject =>
        Unsafe.As<T, Table>(ref table).__offset(4 + 2 * field) != 0;

    private static Vec2 V(Fb.Vec2? v) => v is { } x ? new Vec2(x.X, x.Y) : default;

    private static Vec3 V(Fb.Vec3? v) => v is { } x ? new Vec3(x.X, x.Y, x.Z) : default;

    private static Vec4 V(Fb.Vec4? v) => v is { } x ? new Vec4(x.X, x.Y, x.Z, x.W) : default;

    private static ObjectRef R(Fb.ObjectRef r) => new() { ComponentName = r.ComponentName, Index = r.Index, ObjectRefId = r.ObjectRefId };

    private static ObjectRef? OptR(Fb.ObjectRef? r) => r is { } x ? R(x) : null;

    private static void FbRefs(ref Hash c, bool present, int n, Func<int, Fb.ObjectRef?> get)
    {
        if (!present)
        {
            c.Absent();
            return;
        }

        c.Count(n);
        for (int i = 0; i < n; i++) c.Ref(R(get(i)!.Value));
    }

    public static ulong FlatBuffers(Fb.Prefab p)
    {
        var c = Hash.Start();
        if (!Has(p, 0))
        {
            c.Absent();
            return c.Value;
        }

        int n = p.ObjectsLength;
        c.Count(n);
        for (int i = 0; i < n; i++)
        {
            Fb.PrefabObject o = p.Objects(i)!.Value;
            c.S(o.GetNameBytes());
            if (!Has(o, 2)) c.Absent();
            else
            {
                int components = o.ComponentsLength;
                c.Count(components);
                for (int j = 0; j < components; j++) FbComponent(ref c, o, j);
            }

            if (!Has(o, 3)) c.Absent();
            else
            {
                int children = o.ChildrenLength;
                c.Count(children);
                for (int j = 0; j < children; j++) c.I(o.Children(j));
            }

            c.V4(V(o.Rotation));
            c.V3(V(o.Position));
            c.V3(V(o.Scale));
            c.V2(V(o.Pivot));
            c.V2(V(o.AnchorMin));
            c.V2(V(o.AnchorMax));
            c.V2(V(o.SizeDelta));
            c.I(o.Id);
            c.B(o.Active);
        }

        return c.Value;
    }

    private static void FbComponent(ref Hash c, Fb.PrefabObject o, int j)
    {
        switch (o.ComponentsType(j))
        {
            case Fb.Component.ImageComponent:
            {
                var x = o.Components<Fb.ImageComponent>(j)!.Value;
                c.U(0);
                c.S(x.GetSpriteBytes());
                c.S(x.GetMaterialBytes());
                c.V4(V(x.Color));
                c.V4(V(x.UvRect));
                c.I(x.FillMethod);
                c.F(x.FillAmount);
                c.B(x.Enable);
                c.B(x.RaycastTarget);
                c.B(x.PreserveAspect);
                break;
            }
            case Fb.Component.TextComponent:
            {
                var x = o.Components<Fb.TextComponent>(j)!.Value;
                c.U(1);
                c.S(x.GetTextBytes());
                c.S(x.GetFontBytes());
                c.V4(V(x.Color));
                c.F(x.FontSize);
                c.F(x.LineSpacing);
                c.I(x.Alignment);
                c.U(x.ColorMode);
                c.B(x.Enable);
                c.B(x.RichText);
                break;
            }
            case Fb.Component.ButtonComponent:
            {
                var x = o.Components<Fb.ButtonComponent>(j)!.Value;
                c.U(2);
                c.Ref(OptR(x.Target) ?? default);
                c.Ref(OptR(x.OnClick) ?? default);
                c.B(x.Interactable);
                c.U(x.Transition);
                break;
            }
            case Fb.Component.LayoutComponent:
            {
                var x = o.Components<Fb.LayoutComponent>(j)!.Value;
                c.U(3);
                c.V4(V(x.Padding));
                c.F(x.Spacing);
                c.I(x.ChildAlignment);
                c.B(x.ControlWidth);
                c.B(x.ControlHeight);
                c.B(x.ExpandWidth);
                c.B(x.ExpandHeight);
                break;
            }
            case Fb.Component.ControllerComponent:
            {
                var x = o.Components<Fb.ControllerComponent>(j)!.Value;
                c.U(4);
                FbRefs(ref c, Has(x, 0), x.SetsLength, x.Sets);
                FbRefs(ref c, Has(x, 1), x.EmptiesLength, x.Empties);
                FbRefs(ref c, Has(x, 2), x.HidesLength, x.Hides);
                FbRefs(ref c, Has(x, 3), x.ImagesLength, x.Images);
                FbRefs(ref c, Has(x, 4), x.IconsLength, x.Icons);
                FbRefs(ref c, Has(x, 5), x.NamesLength, x.Names);
                FbRefs(ref c, Has(x, 6), x.LevelsLength, x.Levels);
                FbRefs(ref c, Has(x, 7), x.SkillsLength, x.Skills);
                FbRefs(ref c, Has(x, 8), x.TextsLength, x.Texts);
                FbRefs(ref c, Has(x, 9), x.ButtonsLength, x.Buttons);
                c.OptRef(OptR(x.Icon));
                c.OptRef(OptR(x.Glow));
                c.OptRef(OptR(x.Level));
                c.OptRef(OptR(x.Star));
                c.OptRef(OptR(x.SkillList));
                c.OptRef(OptR(x.Badge));
                c.OptRef(OptR(x.Banner));
                c.OptRef(OptR(x.Counter));
                c.OptRef(OptR(x.Divider));
                c.OptRef(OptR(x.Header));
                c.OptRef(OptR(x.Background));
                c.OptRef(OptR(x.Group));
                c.OptRef(OptR(x.Caption));
                c.OptRef(OptR(x.Thumbnail));
                c.OptRef(OptR(x.Tooltip));
                c.OptB(x.IsPinned);
                c.OptB(x.ShowTitle);
                c.OptB(x.IsFeatured);
                c.OptB(x.Locked);
                c.OptB(x.Enable);
                break;
            }
            default:
                c.Absent();
                break;
        }
    }

    public static ulong FlatBuffers(Fb.World w)
    {
        var c = Hash.Start();
        if (!Has(w, 0))
        {
            c.Absent();
            return c.Value;
        }

        int n = w.MonstersLength;
        c.Count(n);
        for (int i = 0; i < n; i++)
        {
            Fb.Monster m = w.Monsters(i)!.Value;
            c.S(m.GetNameBytes());
            c.I(m.Hp);
            c.OptI(m.Mana);
            c.V3(V(m.Pos));
            c.U((byte)m.Color);
            if (m.GetInventoryBytes() is not { } inventory) c.Absent();
            else
            {
                c.Count(inventory.Count);
                foreach (byte x in inventory) c.U(x);
            }

            if (!Has(m, 6)) c.Absent();
            else
            {
                int weapons = m.WeaponsLength;
                c.Count(weapons);
                for (int j = 0; j < weapons; j++)
                {
                    Fb.Weapon x = m.Weapons(j)!.Value;
                    c.S(x.GetNameBytes());
                    c.I(x.Damage);
                }
            }

            if (m.Equipped is not { } equipped) c.Absent();
            else
            {
                c.S(equipped.GetNameBytes());
                c.I(equipped.Damage);
            }

            if (!Has(m, 8)) c.Absent();
            else
            {
                int path = m.PathLength;
                c.Count(path);
                for (int j = 0; j < path; j++) c.V3(V(m.Path(j)));
            }

            c.B(m.Friendly);
        }

        return c.Value;
    }

    public static ulong FlatBuffers(Fb.RecordSet s)
    {
        var c = Hash.Start();
        if (!Has(s, 0))
        {
            c.Absent();
            return c.Value;
        }

        int n = s.RecordsLength;
        c.Count(n);
        for (int i = 0; i < n; i++)
        {
            Fb.Record x = s.Records(i)!.Value;
            c.OptI(x.I0); c.OptI(x.I1); c.OptI(x.I2); c.OptI(x.I3); c.OptI(x.I4); c.OptI(x.I5); c.OptI(x.I6); c.OptI(x.I7);
            c.OptI(x.I8); c.OptI(x.I9); c.OptI(x.I10); c.OptI(x.I11); c.OptI(x.I12); c.OptI(x.I13); c.OptI(x.I14); c.OptI(x.I15);
            c.OptF(x.F0); c.OptF(x.F1); c.OptF(x.F2); c.OptF(x.F3); c.OptF(x.F4); c.OptF(x.F5); c.OptF(x.F6); c.OptF(x.F7);
            c.OptD(x.D0); c.OptD(x.D1); c.OptD(x.D2); c.OptD(x.D3);
            c.OptB(x.B0); c.OptB(x.B1); c.OptB(x.B2); c.OptB(x.B3); c.OptB(x.B4); c.OptB(x.B5); c.OptB(x.B6); c.OptB(x.B7);
            c.S(x.GetS0Bytes());
            c.S(x.GetS1Bytes());
            c.S(x.GetS2Bytes());
            c.S(x.GetS3Bytes());
        }

        return c.Value;
    }

    public static ulong FlatBuffers(Fb.Lookup x)
    {
        var c = Hash.Start();
        if (!Has(x, 0)) c.Absent();
        else
        {
            int n = x.ItemsLength;
            c.Count(n);
            for (int i = 0; i < n; i++)
            {
                Fb.LookupItem e = x.Items(i)!.Value;
                c.S(e.GetNameBytes());
                c.I(e.Count);
                c.F(e.Weight);
            }
        }

        if (!Has(x, 1)) c.Absent();
        else
        {
            int n = x.PricesLength;
            c.Count(n);
            for (int i = 0; i < n; i++)
            {
                Fb.LookupPrice e = x.Prices(i)!.Value;
                c.I(e.Id);
                c.D(e.Price);
            }
        }

        return c.Value;
    }

    public static ulong FlatBuffers(Fb.Series s)
    {
        var c = Hash.Start();
        if (!Has(s, 0))
        {
            c.Absent();
            return c.Value;
        }

        int n = s.SamplesLength;
        c.Count(n);
        for (int i = 0; i < n; i++)
        {
            Fb.Sample x = s.Samples(i)!.Value;
            c.I(x.Timestamp);
            c.D(x.Value);
            c.I(x.Status);
            c.F(x.Quality);
            c.S(x.GetNoteBytes());
        }

        return c.Value;
    }

    public static ulong FlatBuffers(Fb.Scene s)
    {
        var c = Hash.Start();
        if (!Has(s, 0)) c.Absent();
        else
        {
            int n = s.NodesLength;
            c.Count(n);
            for (int i = 0; i < n; i++)
            {
                Fb.Node node = s.Nodes(i)!.Value;
                c.I(node.Id);
                c.S(node.GetNameBytes());
                c.OptB(node.Active);
                c.F(node.X);
                c.F(node.Y);
                c.F(node.Z);
                c.OptI(node.Parent);
                if (!Has(node, 7)) c.Absent();
                else
                {
                    int children = node.ChildrenLength;
                    c.Count(children);
                    for (int j = 0; j < children; j++) c.I(node.Children(j));
                }
            }
        }

        c.S(s.GetNameBytes());
        return c.Value;
    }

    public static ulong FlatBuffers(Fb.FeatureCollection x)
    {
        var c = Hash.Start();
        c.S(x.GetTypeBytes());
        if (!Has(x, 1))
        {
            c.Absent();
            return c.Value;
        }

        int n = x.FeaturesLength;
        c.Count(n);
        for (int i = 0; i < n; i++)
        {
            Fb.Feature f = x.Features(i)!.Value;
            c.S(f.GetTypeBytes());
            if (!Has(f, 1)) c.Absent();
            else
            {
                int properties = f.PropertiesLength;
                c.Count(properties);
                for (int j = 0; j < properties; j++)
                {
                    Fb.Property p = f.Properties(j)!.Value;
                    c.S(p.GetKeyBytes());
                    c.S(p.GetValueBytes());
                }
            }

            if (f.Geometry is not { } g)
            {
                c.Absent();
                continue;
            }

            c.S(g.GetTypeBytes());
            if (!Has(g, 1))
            {
                c.Absent();
                continue;
            }

            int rings = g.CoordinatesLength;
            c.Count(rings);
            for (int j = 0; j < rings; j++)
            {
                Fb.Ring ring = g.Coordinates(j)!.Value;
                if (!Has(ring, 0))
                {
                    c.Absent();
                    continue;
                }

                int points = ring.PointsLength;
                c.Count(points);
                for (int k = 0; k < points; k++)
                {
                    Fb.Point p = ring.Points(k)!.Value;
                    c.D(p.X);
                    c.D(p.Y);
                }
            }
        }

        return c.Value;
    }
}
