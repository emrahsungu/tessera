using System;
using System.Collections.Generic;
using System.Text;
using Bench.Models;

namespace Bench;

/// <summary>
/// Reference checksum over every field, computed from the C# objects. The C++ readers (benchmarks/native) compute the
/// same value from each library's buffer; a mismatch fails the benchmark. Absent nullable values mix a marker, strings
/// mix their UTF-8 length and first byte, vectors their count and every element, unions their type index.
/// </summary>
public sealed class Checksum
{
    private const ulong Absent = 0x9E3779B97F4A7C15UL;
    private ulong _h = 0xCBF29CE484222325UL;

    public ulong Value => _h;

    private void Mix(ulong v) => _h = unchecked((_h ^ v) * 0x100000001B3UL);

    private void I(long v) => Mix(unchecked((ulong)v));

    private void U(ulong v) => Mix(v);

    private void F(float v) => Mix(BitConverter.SingleToUInt32Bits(v));

    private void D(double v) => Mix(BitConverter.DoubleToUInt64Bits(v));

    private void B(bool v) => Mix(v ? 1UL : 0UL);

    private void S(string? s)
    {
        if (s == null)
        {
            Mix(Absent);
            return;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        Mix((ulong)utf8.Length);
        Mix(utf8.Length > 0 ? utf8[0] : 0UL);
    }

    private void Opt<T>(T? v, Action<T> f) where T : struct
    {
        if (v.HasValue) f(v.Value);
        else Mix(Absent);
    }

    private void List<T>(IReadOnlyList<T>? list, Action<T> f)
    {
        if (list == null)
        {
            Mix(Absent);
            return;
        }

        Mix((ulong)list.Count);
        foreach (var x in list) f(x);
    }

    private void V2(Vec2 v)
    {
        F(v.X);
        F(v.Y);
    }

    private void V3(Vec3 v)
    {
        F(v.X);
        F(v.Y);
        F(v.Z);
    }

    private void V4(Vec4 v)
    {
        F(v.X);
        F(v.Y);
        F(v.Z);
        F(v.W);
    }

    private void Ref(ObjectRef r)
    {
        U(r.ComponentName);
        I(r.Index);
        I(r.ObjectRefId);
    }

    // ------------------------------------------------------------------ workloads

    public static ulong Of(Prefab p)
    {
        var c = new Checksum();
        c.List(p.Objects, c.Object);
        return c.Value;
    }

    private void Object(PrefabObject o)
    {
        S(o.Name);
        List(o.Components, Component);
        List(o.Children, x => I(x));
        V4(o.Rotation);
        V3(o.Position);
        V3(o.Scale);
        V2(o.Pivot);
        V2(o.AnchorMin);
        V2(o.AnchorMax);
        V2(o.SizeDelta);
        I(o.Id);
        B(o.Active);
    }

    private void Component(Component component)
    {
        switch (component)
        {
            case ImageComponent x:
                U(0);
                S(x.Sprite);
                S(x.Material);
                V4(x.Color);
                V4(x.UvRect);
                I(x.FillMethod);
                F(x.FillAmount);
                B(x.Enable);
                B(x.RaycastTarget);
                B(x.PreserveAspect);
                break;
            case TextComponent x:
                U(1);
                S(x.Text);
                S(x.Font);
                V4(x.Color);
                F(x.FontSize);
                F(x.LineSpacing);
                I(x.Alignment);
                U(x.ColorMode);
                B(x.Enable);
                B(x.RichText);
                break;
            case ButtonComponent x:
                U(2);
                Ref(x.Target);
                Ref(x.OnClick);
                B(x.Interactable);
                U(x.Transition);
                break;
            case LayoutComponent x:
                U(3);
                V4(x.Padding);
                F(x.Spacing);
                I(x.ChildAlignment);
                B(x.ControlWidth);
                B(x.ControlHeight);
                B(x.ExpandWidth);
                B(x.ExpandHeight);
                break;
            case ControllerComponent x:
                U(4);
                foreach (var l in new[] { x.Sets, x.Empties, x.Hides, x.Images, x.Icons, x.Names, x.Levels, x.Skills, x.Texts, x.Buttons }) List(l, Ref);
                foreach (var r in new[] { x.Icon, x.Glow, x.Level, x.Star, x.SkillList, x.Badge, x.Banner, x.Counter, x.Divider, x.Header, x.Background, x.Group, x.Caption, x.Thumbnail, x.Tooltip }) Opt(r, Ref);
                foreach (var b in new[] { x.IsPinned, x.ShowTitle, x.IsFeatured, x.Locked, x.Enable }) Opt(b, B);
                break;
            default:
                throw new InvalidOperationException();
        }
    }

    public static ulong Of(World w)
    {
        var c = new Checksum();
        c.List(w.Monsters, c.Monster);
        return c.Value;
    }

    private void Monster(Monster m)
    {
        S(m.Name);
        I(m.Hp);
        Opt(m.Mana, x => I(x));
        V3(m.Pos);
        U((ulong)m.Color);
        List(m.Inventory, x => U(x));
        List(m.Weapons, Weapon);
        if (m.Equipped == null) Mix(Absent);
        else Weapon(m.Equipped);
        List(m.Path, V3);
        B(m.Friendly);
    }

    private void Weapon(Weapon w)
    {
        S(w.Name);
        I(w.Damage);
    }

    public static ulong Of(RecordSet s)
    {
        var c = new Checksum();
        c.List(s.Records, c.Record);
        return c.Value;
    }

    private void Record(Record x)
    {
        foreach (var v in new[] { x.I0, x.I1, x.I2, x.I3, x.I4, x.I5, x.I6, x.I7, x.I8, x.I9, x.I10, x.I11, x.I12, x.I13, x.I14, x.I15 }) Opt(v, y => I(y));
        foreach (var v in new[] { x.F0, x.F1, x.F2, x.F3, x.F4, x.F5, x.F6, x.F7 }) Opt(v, F);
        foreach (var v in new[] { x.D0, x.D1, x.D2, x.D3 }) Opt(v, D);
        foreach (var v in new[] { x.B0, x.B1, x.B2, x.B3, x.B4, x.B5, x.B6, x.B7 }) Opt(v, B);
        S(x.S0);
        S(x.S1);
        S(x.S2);
        S(x.S3);
    }

    public static ulong Of(Lookup x)
    {
        var c = new Checksum();
        if (x.Items == null) c.Mix(Absent);
        else
        {
            c.Mix((ulong)x.Items.Count);
            foreach (var kv in x.Items)
            {
                c.S(kv.Key);
                c.I(kv.Value.Count);
                c.F(kv.Value.Weight);
            }
        }

        if (x.Prices == null) c.Mix(Absent);
        else
        {
            c.Mix((ulong)x.Prices.Count);
            foreach (var kv in x.Prices)
            {
                c.I(kv.Key);
                c.D(kv.Value);
            }
        }

        return c.Value;
    }

    public static ulong Of(Series s)
    {
        var c = new Checksum();
        c.List(s.Samples, c.Sample);
        return c.Value;
    }

    private void Sample(Sample x)
    {
        I(x.Timestamp);
        D(x.Value);
        I(x.Status);
        F(x.Quality);
        S(x.Note);
    }

    public static ulong Of(Scene s)
    {
        var c = new Checksum();
        c.List(s.Nodes, c.Node);
        c.S(s.Name);
        return c.Value;
    }

    private void Node(Node n)
    {
        I(n.Id);
        S(n.Name);
        Opt(n.Active, B);
        F(n.X);
        F(n.Y);
        F(n.Z);
        Opt(n.Parent, x => I(x));
        List(n.Children, x => I(x));
    }
}
