using System;
using System.Collections.Generic;
using Bench.Models;
using Google.FlatBuffers;
using Fb = Bench.Fb;

namespace Bench;

/// <summary>
/// Writes the benchmark objects with the FlatBuffers builder the way a careful user would: one reused builder, child
/// offsets kept on a reused stack (no per-vector arrays), fields added largest first, structs inline.
/// </summary>
public sealed class FbWriters
{
    private readonly FlatBufferBuilder _b = new(1 << 16);
    private readonly bool _sharedStrings;
    private int[] _stack = new int[256];
    private int _top;

    public FbWriters(bool sharedStrings) => _sharedStrings = sharedStrings;

    public FlatBufferBuilder Builder => _b;

    public byte[] ToArray() => _b.SizedByteArray();

    public ArraySegment<byte> Written => _b.DataBuffer.ToArraySegment(_b.DataBuffer.Position, _b.Offset);

    private StringOffset Str(string? s) => s == null ? default : _sharedStrings ? _b.CreateSharedString(s) : _b.CreateString(s);

    private int Push(int n)
    {
        int start = _top;
        if (_top + n > _stack.Length) Array.Resize(ref _stack, Math.Max(_top + n, _stack.Length * 2));
        _top += n;
        return start;
    }

    private VectorOffset OffsetVector(int start, int n)
    {
        _b.StartVector(4, n, 4);
        for (int i = n - 1; i >= 0; i--) _b.AddOffset(_stack[start + i]);
        _top = start;
        return _b.EndVector();
    }

    // ------------------------------------------------------------------ prefab

    public void Write(Prefab p)
    {
        _b.Clear();
        _top = 0;
        VectorOffset objects = default;
        if (p.Objects != null)
        {
            int n = p.Objects.Count, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Object(p.Objects[i]).Value; _stack[s + i] = v; }
            objects = OffsetVector(s, n);
        }

        Fb.Prefab.StartPrefab(_b);
        if (p.Objects != null) Fb.Prefab.AddObjects(_b, objects);
        _b.Finish(Fb.Prefab.EndPrefab(_b).Value);
    }

    private Offset<Fb.PrefabObject> Object(PrefabObject o)
    {
        var name = Str(o.Name);
        VectorOffset comps = default, types = default, children = default;
        if (o.Components != null)
        {
            int n = o.Components.Count, s = Push(n);
            Span<byte> kinds = stackalloc byte[n];
            for (int i = 0; i < n; i++) { int v = Component(o.Components[i], out kinds[i]); _stack[s + i] = v; }
            Fb.PrefabObject.StartComponentsTypeVector(_b, n);
            for (int i = n - 1; i >= 0; i--) _b.AddByte(kinds[i]);
            types = _b.EndVector();
            comps = OffsetVector(s, n);
        }

        if (o.Children != null)
        {
            Fb.PrefabObject.StartChildrenVector(_b, o.Children.Count);
            for (int i = o.Children.Count - 1; i >= 0; i--) _b.AddInt(o.Children[i]);
            children = _b.EndVector();
        }

        Fb.PrefabObject.StartPrefabObject(_b);
        Fb.PrefabObject.AddRotation(_b, Fb.Vec4.CreateVec4(_b, o.Rotation.X, o.Rotation.Y, o.Rotation.Z, o.Rotation.W));
        Fb.PrefabObject.AddPosition(_b, Fb.Vec3.CreateVec3(_b, o.Position.X, o.Position.Y, o.Position.Z));
        Fb.PrefabObject.AddScale(_b, Fb.Vec3.CreateVec3(_b, o.Scale.X, o.Scale.Y, o.Scale.Z));
        Fb.PrefabObject.AddPivot(_b, Fb.Vec2.CreateVec2(_b, o.Pivot.X, o.Pivot.Y));
        Fb.PrefabObject.AddAnchorMin(_b, Fb.Vec2.CreateVec2(_b, o.AnchorMin.X, o.AnchorMin.Y));
        Fb.PrefabObject.AddAnchorMax(_b, Fb.Vec2.CreateVec2(_b, o.AnchorMax.X, o.AnchorMax.Y));
        Fb.PrefabObject.AddSizeDelta(_b, Fb.Vec2.CreateVec2(_b, o.SizeDelta.X, o.SizeDelta.Y));
        if (o.Name != null) Fb.PrefabObject.AddName(_b, name);
        if (o.Components != null)
        {
            Fb.PrefabObject.AddComponentsType(_b, types);
            Fb.PrefabObject.AddComponents(_b, comps);
        }

        if (o.Children != null) Fb.PrefabObject.AddChildren(_b, children);
        Fb.PrefabObject.AddId(_b, o.Id);
        Fb.PrefabObject.AddActive(_b, o.Active);
        return Fb.PrefabObject.EndPrefabObject(_b);
    }

    private int Component(Component c, out byte kind)
    {
        switch (c)
        {
            case ImageComponent x:
            {
                kind = (byte)Fb.Component.ImageComponent;
                var sprite = Str(x.Sprite);
                var material = Str(x.Material);
                Fb.ImageComponent.StartImageComponent(_b);
                Fb.ImageComponent.AddColor(_b, Fb.Vec4.CreateVec4(_b, x.Color.X, x.Color.Y, x.Color.Z, x.Color.W));
                Fb.ImageComponent.AddUvRect(_b, Fb.Vec4.CreateVec4(_b, x.UvRect.X, x.UvRect.Y, x.UvRect.Z, x.UvRect.W));
                if (x.Sprite != null) Fb.ImageComponent.AddSprite(_b, sprite);
                if (x.Material != null) Fb.ImageComponent.AddMaterial(_b, material);
                Fb.ImageComponent.AddFillMethod(_b, x.FillMethod);
                Fb.ImageComponent.AddFillAmount(_b, x.FillAmount);
                Fb.ImageComponent.AddEnable(_b, x.Enable);
                Fb.ImageComponent.AddRaycastTarget(_b, x.RaycastTarget);
                Fb.ImageComponent.AddPreserveAspect(_b, x.PreserveAspect);
                return Fb.ImageComponent.EndImageComponent(_b).Value;
            }
            case TextComponent x:
            {
                kind = (byte)Fb.Component.TextComponent;
                var text = Str(x.Text);
                var font = Str(x.Font);
                Fb.TextComponent.StartTextComponent(_b);
                Fb.TextComponent.AddColor(_b, Fb.Vec4.CreateVec4(_b, x.Color.X, x.Color.Y, x.Color.Z, x.Color.W));
                if (x.Text != null) Fb.TextComponent.AddText(_b, text);
                if (x.Font != null) Fb.TextComponent.AddFont(_b, font);
                Fb.TextComponent.AddFontSize(_b, x.FontSize);
                Fb.TextComponent.AddLineSpacing(_b, x.LineSpacing);
                Fb.TextComponent.AddAlignment(_b, x.Alignment);
                Fb.TextComponent.AddColorMode(_b, x.ColorMode);
                Fb.TextComponent.AddEnable(_b, x.Enable);
                Fb.TextComponent.AddRichText(_b, x.RichText);
                return Fb.TextComponent.EndTextComponent(_b).Value;
            }
            case ButtonComponent x:
                kind = (byte)Fb.Component.ButtonComponent;
                Fb.ButtonComponent.StartButtonComponent(_b);
                Fb.ButtonComponent.AddTarget(_b, Fb.ObjectRef.CreateObjectRef(_b, x.Target.ComponentName, x.Target.Index, x.Target.ObjectRefId));
                Fb.ButtonComponent.AddOnClick(_b, Fb.ObjectRef.CreateObjectRef(_b, x.OnClick.ComponentName, x.OnClick.Index, x.OnClick.ObjectRefId));
                Fb.ButtonComponent.AddInteractable(_b, x.Interactable);
                Fb.ButtonComponent.AddTransition(_b, x.Transition);
                return Fb.ButtonComponent.EndButtonComponent(_b).Value;
            case LayoutComponent x:
                kind = (byte)Fb.Component.LayoutComponent;
                Fb.LayoutComponent.StartLayoutComponent(_b);
                Fb.LayoutComponent.AddPadding(_b, Fb.Vec4.CreateVec4(_b, x.Padding.X, x.Padding.Y, x.Padding.Z, x.Padding.W));
                Fb.LayoutComponent.AddSpacing(_b, x.Spacing);
                Fb.LayoutComponent.AddChildAlignment(_b, x.ChildAlignment);
                Fb.LayoutComponent.AddControlWidth(_b, x.ControlWidth);
                Fb.LayoutComponent.AddControlHeight(_b, x.ControlHeight);
                Fb.LayoutComponent.AddExpandWidth(_b, x.ExpandWidth);
                Fb.LayoutComponent.AddExpandHeight(_b, x.ExpandHeight);
                return Fb.LayoutComponent.EndLayoutComponent(_b).Value;
            case ControllerComponent x:
                kind = (byte)Fb.Component.ControllerComponent;
                return Controller(x);
            default:
                throw new InvalidOperationException();
        }
    }

    private VectorOffset Refs(List<ObjectRef>? list)
    {
        if (list == null) return default;
        _b.StartVector(8, list.Count, 4);
        for (int i = list.Count - 1; i >= 0; i--) Fb.ObjectRef.CreateObjectRef(_b, list[i].ComponentName, list[i].Index, list[i].ObjectRefId);
        return _b.EndVector();
    }

    private Offset<Fb.ObjectRef> Ref(ObjectRef r) => Fb.ObjectRef.CreateObjectRef(_b, r.ComponentName, r.Index, r.ObjectRefId);

    private int Controller(ControllerComponent x)
    {
        VectorOffset sets = Refs(x.Sets), empties = Refs(x.Empties), hides = Refs(x.Hides), images = Refs(x.Images), icons = Refs(x.Icons),
            names = Refs(x.Names), levels = Refs(x.Levels), skills = Refs(x.Skills), texts = Refs(x.Texts), buttons = Refs(x.Buttons);
        Fb.ControllerComponent.StartControllerComponent(_b);
        if (x.Icon is { } a) Fb.ControllerComponent.AddIcon(_b, Ref(a));
        if (x.Glow is { } b) Fb.ControllerComponent.AddGlow(_b, Ref(b));
        if (x.Level is { } c) Fb.ControllerComponent.AddLevel(_b, Ref(c));
        if (x.Star is { } d) Fb.ControllerComponent.AddStar(_b, Ref(d));
        if (x.SkillList is { } e) Fb.ControllerComponent.AddSkillList(_b, Ref(e));
        if (x.Badge is { } f) Fb.ControllerComponent.AddBadge(_b, Ref(f));
        if (x.Banner is { } g) Fb.ControllerComponent.AddBanner(_b, Ref(g));
        if (x.Counter is { } h) Fb.ControllerComponent.AddCounter(_b, Ref(h));
        if (x.Divider is { } i) Fb.ControllerComponent.AddDivider(_b, Ref(i));
        if (x.Header is { } j) Fb.ControllerComponent.AddHeader(_b, Ref(j));
        if (x.Background is { } k) Fb.ControllerComponent.AddBackground(_b, Ref(k));
        if (x.Group is { } l) Fb.ControllerComponent.AddGroup(_b, Ref(l));
        if (x.Caption is { } m) Fb.ControllerComponent.AddCaption(_b, Ref(m));
        if (x.Thumbnail is { } n) Fb.ControllerComponent.AddThumbnail(_b, Ref(n));
        if (x.Tooltip is { } o) Fb.ControllerComponent.AddTooltip(_b, Ref(o));
        if (x.Sets != null) Fb.ControllerComponent.AddSets(_b, sets);
        if (x.Empties != null) Fb.ControllerComponent.AddEmpties(_b, empties);
        if (x.Hides != null) Fb.ControllerComponent.AddHides(_b, hides);
        if (x.Images != null) Fb.ControllerComponent.AddImages(_b, images);
        if (x.Icons != null) Fb.ControllerComponent.AddIcons(_b, icons);
        if (x.Names != null) Fb.ControllerComponent.AddNames(_b, names);
        if (x.Levels != null) Fb.ControllerComponent.AddLevels(_b, levels);
        if (x.Skills != null) Fb.ControllerComponent.AddSkills(_b, skills);
        if (x.Texts != null) Fb.ControllerComponent.AddTexts(_b, texts);
        if (x.Buttons != null) Fb.ControllerComponent.AddButtons(_b, buttons);
        Fb.ControllerComponent.AddIsPinned(_b, x.IsPinned);
        Fb.ControllerComponent.AddShowTitle(_b, x.ShowTitle);
        Fb.ControllerComponent.AddIsFeatured(_b, x.IsFeatured);
        Fb.ControllerComponent.AddLocked(_b, x.Locked);
        Fb.ControllerComponent.AddEnable(_b, x.Enable);
        return Fb.ControllerComponent.EndControllerComponent(_b).Value;
    }

    // ------------------------------------------------------------------ world

    public void Write(World w)
    {
        _b.Clear();
        _top = 0;
        VectorOffset monsters = default;
        if (w.Monsters != null)
        {
            int n = w.Monsters.Count, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Monster(w.Monsters[i]).Value; _stack[s + i] = v; }
            monsters = OffsetVector(s, n);
        }

        Fb.World.StartWorld(_b);
        if (w.Monsters != null) Fb.World.AddMonsters(_b, monsters);
        _b.Finish(Fb.World.EndWorld(_b).Value);
    }

    private Offset<Fb.Weapon> Weapon(Weapon w)
    {
        var name = Str(w.Name);
        Fb.Weapon.StartWeapon(_b);
        if (w.Name != null) Fb.Weapon.AddName(_b, name);
        Fb.Weapon.AddDamage(_b, w.Damage);
        return Fb.Weapon.EndWeapon(_b);
    }

    private Offset<Fb.Monster> Monster(Monster m)
    {
        var name = Str(m.Name);
        VectorOffset inventory = default, weapons = default, path = default;
        if (m.Inventory != null) inventory = Fb.Monster.CreateInventoryVectorBlock(_b, m.Inventory);
        if (m.Weapons != null)
        {
            int n = m.Weapons.Count, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Weapon(m.Weapons[i]).Value; _stack[s + i] = v; }
            weapons = OffsetVector(s, n);
        }

        Offset<Fb.Weapon> equipped = m.Equipped != null ? Weapon(m.Equipped) : default;
        if (m.Path != null)
        {
            Fb.Monster.StartPathVector(_b, m.Path.Count);
            for (int i = m.Path.Count - 1; i >= 0; i--) Fb.Vec3.CreateVec3(_b, m.Path[i].X, m.Path[i].Y, m.Path[i].Z);
            path = _b.EndVector();
        }

        Fb.Monster.StartMonster(_b);
        Fb.Monster.AddPos(_b, Fb.Vec3.CreateVec3(_b, m.Pos.X, m.Pos.Y, m.Pos.Z));
        if (m.Name != null) Fb.Monster.AddName(_b, name);
        if (m.Inventory != null) Fb.Monster.AddInventory(_b, inventory);
        if (m.Weapons != null) Fb.Monster.AddWeapons(_b, weapons);
        if (m.Equipped != null) Fb.Monster.AddEquipped(_b, equipped);
        if (m.Path != null) Fb.Monster.AddPath(_b, path);
        Fb.Monster.AddHp(_b, m.Hp);
        Fb.Monster.AddMana(_b, m.Mana);
        Fb.Monster.AddColor(_b, (Fb.Color)m.Color);
        Fb.Monster.AddFriendly(_b, m.Friendly);
        return Fb.Monster.EndMonster(_b);
    }

    // ------------------------------------------------------------------ records

    public void Write(RecordSet set)
    {
        _b.Clear();
        _top = 0;
        VectorOffset records = default;
        if (set.Records != null)
        {
            int n = set.Records.Count, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Record(set.Records[i]).Value; _stack[s + i] = v; }
            records = OffsetVector(s, n);
        }

        Fb.RecordSet.StartRecordSet(_b);
        if (set.Records != null) Fb.RecordSet.AddRecords(_b, records);
        _b.Finish(Fb.RecordSet.EndRecordSet(_b).Value);
    }

    private Offset<Fb.Record> Record(Record x)
    {
        StringOffset s0 = Str(x.S0), s1 = Str(x.S1), s2 = Str(x.S2), s3 = Str(x.S3);
        Fb.Record.StartRecord(_b);
        Fb.Record.AddD0(_b, x.D0);
        Fb.Record.AddD1(_b, x.D1);
        Fb.Record.AddD2(_b, x.D2);
        Fb.Record.AddD3(_b, x.D3);
        Fb.Record.AddI0(_b, x.I0);
        Fb.Record.AddI1(_b, x.I1);
        Fb.Record.AddI2(_b, x.I2);
        Fb.Record.AddI3(_b, x.I3);
        Fb.Record.AddI4(_b, x.I4);
        Fb.Record.AddI5(_b, x.I5);
        Fb.Record.AddI6(_b, x.I6);
        Fb.Record.AddI7(_b, x.I7);
        Fb.Record.AddI8(_b, x.I8);
        Fb.Record.AddI9(_b, x.I9);
        Fb.Record.AddI10(_b, x.I10);
        Fb.Record.AddI11(_b, x.I11);
        Fb.Record.AddI12(_b, x.I12);
        Fb.Record.AddI13(_b, x.I13);
        Fb.Record.AddI14(_b, x.I14);
        Fb.Record.AddI15(_b, x.I15);
        Fb.Record.AddF0(_b, x.F0);
        Fb.Record.AddF1(_b, x.F1);
        Fb.Record.AddF2(_b, x.F2);
        Fb.Record.AddF3(_b, x.F3);
        Fb.Record.AddF4(_b, x.F4);
        Fb.Record.AddF5(_b, x.F5);
        Fb.Record.AddF6(_b, x.F6);
        Fb.Record.AddF7(_b, x.F7);
        if (x.S0 != null) Fb.Record.AddS0(_b, s0);
        if (x.S1 != null) Fb.Record.AddS1(_b, s1);
        if (x.S2 != null) Fb.Record.AddS2(_b, s2);
        if (x.S3 != null) Fb.Record.AddS3(_b, s3);
        Fb.Record.AddB0(_b, x.B0);
        Fb.Record.AddB1(_b, x.B1);
        Fb.Record.AddB2(_b, x.B2);
        Fb.Record.AddB3(_b, x.B3);
        Fb.Record.AddB4(_b, x.B4);
        Fb.Record.AddB5(_b, x.B5);
        Fb.Record.AddB6(_b, x.B6);
        Fb.Record.AddB7(_b, x.B7);
        return Fb.Record.EndRecord(_b);
    }

    // ------------------------------------------------------------------ series

    public void Write(Lookup x)
    {
        _b.Clear();
        _top = 0;
        VectorOffset items = default, prices = default;
        if (x.Items != null)
        {
            var offsets = new Offset<Fb.LookupItem>[x.Items.Count];
            int i = 0;
            foreach (var kv in x.Items) offsets[i++] = Fb.LookupItem.CreateLookupItem(_b, Str(kv.Key), kv.Value.Count, kv.Value.Weight);
            items = Fb.LookupItem.CreateSortedVectorOfLookupItem(_b, offsets);
        }

        if (x.Prices != null)
        {
            var offsets = new Offset<Fb.LookupPrice>[x.Prices.Count];
            int i = 0;
            foreach (var kv in x.Prices) offsets[i++] = Fb.LookupPrice.CreateLookupPrice(_b, kv.Key, kv.Value);
            prices = Fb.LookupPrice.CreateSortedVectorOfLookupPrice(_b, offsets);
        }

        Fb.Lookup.StartLookup(_b);
        if (x.Items != null) Fb.Lookup.AddItems(_b, items);
        if (x.Prices != null) Fb.Lookup.AddPrices(_b, prices);
        _b.Finish(Fb.Lookup.EndLookup(_b).Value);
    }

    public void Write(Series series)
    {
        _b.Clear();
        _top = 0;
        VectorOffset samples = default;
        if (series.Samples != null)
        {
            int n = series.Samples.Count, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Sample(series.Samples[i]).Value; _stack[s + i] = v; }
            samples = OffsetVector(s, n);
        }

        Fb.Series.StartSeries(_b);
        if (series.Samples != null) Fb.Series.AddSamples(_b, samples);
        _b.Finish(Fb.Series.EndSeries(_b).Value);
    }

    private Offset<Fb.Sample> Sample(Sample x)
    {
        var note = Str(x.Note);
        Fb.Sample.StartSample(_b);
        Fb.Sample.AddTimestamp(_b, x.Timestamp);
        Fb.Sample.AddValue(_b, x.Value);
        if (x.Note != null) Fb.Sample.AddNote(_b, note);
        Fb.Sample.AddStatus(_b, x.Status);
        Fb.Sample.AddQuality(_b, x.Quality);
        return Fb.Sample.EndSample(_b);
    }

    // ------------------------------------------------------------------ scene

    public void Write(Scene scene)
    {
        _b.Clear();
        // FlatBuffers skips scalars equal to their default, and -0.0 == 0.0 (node 0 has Y = -0.0), so force defaults
        // to keep the values.
        _b.ForceDefaults = true;
        _top = 0;
        VectorOffset nodes = default;
        if (scene.Nodes != null)
        {
            int n = scene.Nodes.Length, s = Push(n);
            for (int i = 0; i < n; i++) { int v = Node(scene.Nodes[i]).Value; _stack[s + i] = v; }
            nodes = OffsetVector(s, n);
        }

        var name = Str(scene.Name);
        Fb.Scene.StartScene(_b);
        if (scene.Nodes != null) Fb.Scene.AddNodes(_b, nodes);
        if (scene.Name != null) Fb.Scene.AddName(_b, name);
        _b.Finish(Fb.Scene.EndScene(_b).Value);
    }

    private Offset<Fb.Node> Node(Node x)
    {
        var name = Str(x.Name);
        VectorOffset children = x.Children == null ? default : Fb.Node.CreateChildrenVector(_b, x.Children);
        Fb.Node.StartNode(_b);
        Fb.Node.AddId(_b, x.Id);
        if (x.Name != null) Fb.Node.AddName(_b, name);
        if (x.Active is bool active) Fb.Node.AddActive(_b, active);
        Fb.Node.AddX(_b, x.X);
        Fb.Node.AddY(_b, x.Y);
        Fb.Node.AddZ(_b, x.Z);
        if (x.Parent is int parent) Fb.Node.AddParent(_b, parent);
        if (x.Children != null) Fb.Node.AddChildren(_b, children);
        return Fb.Node.EndNode(_b);
    }
}
