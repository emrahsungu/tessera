using System;
using System.Collections.Generic;
using System.Linq;
using Bench.Models;

namespace Bench;

/// <summary>Deterministic benchmark data. Distributions imitate real data: repeated names, mostly-default transforms.</summary>
public static class DataGen
{
    public static Prefab Prefab(int objects = 400, int seed = 1)
    {
        var r = new Random(seed);
        string[] names =
        {
            "Root", "BG", "Frame", "Icon", "Label", "Title", "Button", "ButtonLabel", "Image", "Text", "Shadow", "Glow", "Mask",
            "Content", "Scroll", "Item", "ItemIcon", "ItemName", "Badge", "Arrow", "Cursor", "Gauge", "GaugeFill", "Number",
            "Panel", "Header", "Footer", "List", "Grid", "Slot",
        };
        string[] sprites = Enumerable.Range(0, 120).Select(i => $"ui/atlas/common/icon_{i:000}").ToArray();
        string[] materials = { "UI/Default", "UI/Additive", "UI/Grayscale", "UI/Glow" };
        string[] fonts = { "Fonts/Main", "Fonts/Title", "Fonts/Number" };
        string[] texts = Enumerable.Range(0, 300).Select(i => TextLine(r, i)).ToArray();
        Vec4[] palette =
        {
            new(1, 1, 1, 1), new(0, 0, 0, 1), new(1, 0.8f, 0.2f, 1), new(0.2f, 0.6f, 1, 1), new(1, 1, 1, 0.5f),
            new(0.9f, 0.1f, 0.1f, 1), new(0.5f, 0.5f, 0.5f, 1), new(0.1f, 0.9f, 0.3f, 1),
        };
        Vec2[] pivots = { new(0, 0), new(1, 1), new(0, 1), new(1, 0), new(0, 0.5f), new(0.5f, 1) };
        (Vec2 Min, Vec2 Max)[] anchors =
        {
            (new(0, 1), new(0, 1)), (new(1, 1), new(1, 1)), (new(0, 0), new(1, 0)), (new(0, 1), new(1, 1)), (new(0, 0), new(0, 1)),
        };
        Vec2[] sizes =
        {
            new(100, 100), new(200, 50), new(64, 64), new(128, 128), new(300, 80), new(48, 48), new(1920, 1080), new(256, 32),
            new(400, 400), new(32, 32), new(180, 60), new(90, 90),
        };
        uint[] componentNames = Enumerable.Range(0, 40).Select(_ => (uint)r.Next() * 2654435761u).ToArray();

        var list = new List<PrefabObject>(objects);
        var children = new List<int>[objects];
        for (int i = 0; i < objects; i++)
        {
            if (i > 0)
            {
                int parent = r.Next(Math.Max(0, i - 20), i);
                (children[parent] ??= new List<int>()).Add(i);
            }
        }

        for (int i = 0; i < objects; i++)
        {
            var o = new PrefabObject
            {
                Name = names[Skewed(r, names.Length)] + (r.Next(100) < 40 ? "_" + r.Next(8) : ""),
                Id = i,
                Active = r.Next(100) < 90,
                Children = children[i],
                Rotation = r.Next(100) < 95 ? new Vec4(0, 0, 0, 1) : ZRotation(r),
                Position = new Vec3(r.Next(-500, 501), r.Next(-300, 301), 0),
                Scale = r.Next(100) < 90 ? new Vec3(1, 1, 1) : Uniform(new[] { 0.5f, 0.8f, 1.2f, 2f }[r.Next(4)]),
                Pivot = r.Next(100) < 75 ? new Vec2(0.5f, 0.5f) : pivots[r.Next(pivots.Length)],
                SizeDelta = r.Next(100) < 70 ? sizes[r.Next(sizes.Length)] : new Vec2(r.Next(10, 800), r.Next(10, 600)),
            };
            int a = r.Next(100);
            (o.AnchorMin, o.AnchorMax) = a < 50 ? (new Vec2(0.5f, 0.5f), new Vec2(0.5f, 0.5f)) : a < 75 ? (new Vec2(0, 0), new Vec2(1, 1)) : anchors[r.Next(anchors.Length)];

            int count = Pick(r, 10, 50, 30, 10);
            if (count > 0) o.Components = new List<Component>(count);
            for (int c = 0; c < count; c++)
            {
                int kind = r.Next(100);
                if (kind < 45)
                {
                    o.Components!.Add(new ImageComponent
                    {
                        Sprite = r.Next(100) < 95 ? sprites[Skewed(r, sprites.Length)] : null,
                        Material = r.Next(100) < 20 ? materials[r.Next(materials.Length)] : null,
                        Color = r.Next(100) < 70 ? palette[0] : palette[r.Next(palette.Length)],
                        UvRect = r.Next(100) < 90 ? new Vec4(0, 0, 1, 1) : new Vec4(0, 0, (float)r.NextDouble(), 1),
                        FillMethod = r.Next(100) < 90 ? 0 : r.Next(1, 5),
                        FillAmount = r.Next(100) < 90 ? 1 : (float)r.NextDouble(),
                        Enable = r.Next(100) < 95,
                        RaycastTarget = r.Next(2) == 0,
                        PreserveAspect = r.Next(100) < 20,
                    });
                }
                else if (kind < 75)
                {
                    o.Components!.Add(new TextComponent
                    {
                        Text = r.Next(100) < 85 ? texts[Skewed(r, texts.Length)] : "x" + r.Next(10000),
                        Font = fonts[r.Next(fonts.Length)],
                        Color = r.Next(100) < 60 ? palette[0] : palette[r.Next(palette.Length)],
                        FontSize = new[] { 18f, 20f, 24f, 28f, 32f, 40f }[r.Next(6)],
                        LineSpacing = r.Next(100) < 80 ? 0 : (float)r.NextDouble() * 10,
                        Alignment = new short[] { 0, 1, 4, 257, 258, 260 }[r.Next(6)],
                        ColorMode = (byte)(r.Next(100) < 80 ? 0 : 1),
                        Enable = r.Next(100) < 95,
                        RichText = r.Next(100) < 30,
                    });
                }
                else if (kind < 87)
                {
                    o.Components!.Add(new ButtonComponent
                    {
                        Target = Ref(r, componentNames),
                        OnClick = Ref(r, componentNames),
                        Interactable = r.Next(100) < 90,
                        Transition = (byte)r.Next(4),
                    });
                }
                else if (kind < 95)
                {
                    o.Components!.Add(new LayoutComponent
                    {
                        Padding = r.Next(100) < 60 ? default : new Vec4(r.Next(20), r.Next(20), r.Next(20), r.Next(20)),
                        Spacing = new[] { 0f, 4f, 8f, 10f, 16f }[r.Next(5)],
                        ChildAlignment = r.Next(9),
                        ControlWidth = r.Next(2) == 0,
                        ControlHeight = r.Next(2) == 0,
                        ExpandWidth = r.Next(2) == 0,
                        ExpandHeight = r.Next(2) == 0,
                    });
                }
                else
                {
                    o.Components!.Add(Controller(r, componentNames));
                }
            }

            list.Add(o);
        }

        return new Prefab { Objects = list };
    }

    private static ControllerComponent Controller(Random r, uint[] names)
    {
        List<ObjectRef>? L() => r.Next(100) < 15 ? Enumerable.Range(0, r.Next(1, 7)).Select(_ => Ref(r, names)).ToList() : null;
        ObjectRef? R() => r.Next(100) < 15 ? Ref(r, names) : null;
        bool? B() => r.Next(100) < 15 ? r.Next(2) == 0 : null;
        return new ControllerComponent
        {
            Sets = L(), Empties = L(), Hides = L(), Images = L(), Icons = L(), Names = L(), Levels = L(), Skills = L(), Texts = L(),
            Buttons = L(), Icon = R(), Glow = R(), Level = R(), Star = R(), SkillList = R(), Badge = R(), Banner = R(),
            Counter = R(), Divider = R(), Header = R(), Background = R(), Group = R(), Caption = R(), Thumbnail = R(),
            Tooltip = R(), IsPinned = B(), ShowTitle = B(), IsFeatured = B(), Locked = B(), Enable = B(),
        };
    }

    public static World World(int monsters = 1000, int seed = 2)
    {
        var r = new Random(seed);
        string[] weaponNames = { "Sword", "Axe", "Bow", "Spear", "Dagger", "Staff", "Mace", "Hammer", "Wand", "Crossbow", "Scythe", "Whip", "Club", "Halberd", "Katana", "Sling" };
        Vec3 V() => new((float)(r.NextDouble() * 2000 - 1000), (float)(r.NextDouble() * 2000 - 1000), (float)(r.NextDouble() * 200 - 100));
        Weapon W() => new() { Name = weaponNames[r.Next(weaponNames.Length)], Damage = (short)r.Next(1, 200) };
        var list = new List<Monster>(monsters);
        for (int i = 0; i < monsters; i++)
        {
            var m = new Monster
            {
                Name = "Monster_" + i + (r.Next(100) < 10 ? "_Elite" : ""),
                Hp = (short)(r.Next(100) < 10 ? 100 : r.Next(1, 1000)),
                Mana = r.Next(100) < 70 ? (short)r.Next(0, 500) : null,
                Pos = V(),
                Color = (Color)r.Next(3),
                Friendly = r.Next(2) == 0,
            };
            if (r.Next(2) == 0)
            {
                m.Inventory = new byte[r.Next(0, 33)];
                r.NextBytes(m.Inventory);
            }

            int wc = r.Next(0, 4);
            if (wc > 0) m.Weapons = Enumerable.Range(0, wc).Select(_ => W()).ToList();
            if (r.Next(100) < 60) m.Equipped = W();
            int pc = r.Next(0, 9);
            if (pc > 0) m.Path = Enumerable.Range(0, pc).Select(_ => V()).ToList();
            list.Add(m);
        }

        return new World { Monsters = list };
    }

    public static RecordSet Records(int records = 2000, int seed = 3)
    {
        var r = new Random(seed);
        string[] pool = Enumerable.Range(0, 50).Select(i => "value-" + i + new string('x', r.Next(0, 16))).ToArray();
        var list = new List<Record>(records);
        bool P() => r.Next(100) < 15;
        for (int i = 0; i < records; i++)
        {
            var x = new Record();
            int? I() => P() ? r.Next(int.MinValue, int.MaxValue) : null;
            float? F() => P() ? (float)(r.NextDouble() * 1000) : null;
            double? D() => P() ? r.NextDouble() * 1e6 : null;
            bool? B() => P() ? r.Next(2) == 0 : null;
            string? S() => P() ? pool[r.Next(pool.Length)] : null;
            x.I0 = I(); x.I1 = I(); x.I2 = I(); x.I3 = I(); x.I4 = I(); x.I5 = I(); x.I6 = I(); x.I7 = I();
            x.I8 = I(); x.I9 = I(); x.I10 = I(); x.I11 = I(); x.I12 = I(); x.I13 = I(); x.I14 = I(); x.I15 = I();
            x.F0 = F(); x.F1 = F(); x.F2 = F(); x.F3 = F(); x.F4 = F(); x.F5 = F(); x.F6 = F(); x.F7 = F();
            x.D0 = D(); x.D1 = D(); x.D2 = D(); x.D3 = D();
            x.B0 = B(); x.B1 = B(); x.B2 = B(); x.B3 = B(); x.B4 = B(); x.B5 = B(); x.B6 = B(); x.B7 = B();
            x.S0 = S(); x.S1 = S(); x.S2 = S(); x.S3 = S();
            list.Add(x);
        }

        return new RecordSet { Records = list };
    }

    /// <summary>
    /// 5,000 items by name and 5,000 prices by id. Entries are added in key order (ASCII names), so every library
    /// iterates them in the same order: Tessera and FlatBuffers sort by key, MessagePack keeps the dictionary's order.
    /// </summary>
    public static Lookup Lookup(int entries = 5000, int seed = 9)
    {
        var r = new Random(seed);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        while (names.Count < entries) names.Add("item-" + r.Next(0x1000000).ToString("x6"));
        var items = new Dictionary<string, Stock>(entries);
        foreach (string name in names) items[name] = new Stock { Count = r.Next(1000), Weight = (float)Math.Round(r.NextDouble() * 50, 2) };
        var ids = new SortedSet<int>();
        while (ids.Count < entries) ids.Add(r.Next(int.MinValue, int.MaxValue));
        var prices = new Dictionary<int, double>(entries);
        foreach (int id in ids) prices[id] = Math.Round(r.NextDouble() * 1000, 2);
        return new Lookup { Items = items, Prices = prices };
    }

    public static Series Series(int samples = 20000, int seed = 4)
    {
        var r = new Random(seed);
        var list = new List<Sample>(samples);
        long t = 1_700_000_000_000;
        for (int i = 0; i < samples; i++)
        {
            t += 1000 + r.Next(-20, 21);
            list.Add(new Sample
            {
                Timestamp = t,
                Value = Math.Round(Math.Sin(i * 0.01) * 100 + r.NextDouble(), 3),
                Status = r.Next(100) < 90 ? 0 : r.Next(1, 8),
                Quality = r.Next(100) < 80 ? 1f : (float)r.NextDouble(),
                Note = r.Next(1000) < 10 ? "spike at " + i : null,
            });
        }

        return new Series { Samples = list };
    }

    /// <summary>
    /// Scene graphs of 1,024 nodes: "dense" sets every member, "sparse" leaves most optional members null, and
    /// "shared" repeats 16 distinct nodes.
    /// </summary>
    public static Scene Scene(string workload, int count = 1024)
    {
        bool sparse = workload.StartsWith("sparse", StringComparison.Ordinal), shared = workload.EndsWith("shared", StringComparison.Ordinal);
        return new Scene
        {
            Name = workload,
            Nodes = Enumerable.Range(0, count).Select(i =>
            {
                int n = shared ? i % 16 : i;
                return new Node
                {
                    Id = n, Name = sparse && i % 4 != 0 ? null : "node-" + n, Active = sparse && i % 3 != 0 ? null : n % 2 == 0,
                    X = n * 0.125f, Y = n * -0.25f, Z = n * 0.5f, Parent = sparse && i % 4 != 0 ? null : n - 1,
                    Children = sparse && i % 8 != 0 ? null : new[] { n + 1, n + 2, n + 3 },
                };
            }).ToArray(),
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Index biased toward small values (roughly Zipf-like), so a few entries dominate.</summary>
    private static int Skewed(Random r, int n) => Math.Min(n - 1, (int)(n * Math.Pow(r.NextDouble(), 2.5)));

    private static int Pick(Random r, params int[] weights)
    {
        int x = r.Next(weights.Sum());
        for (int i = 0; i < weights.Length; i++)
        {
            if (x < weights[i]) return i;
            x -= weights[i];
        }

        return weights.Length - 1;
    }

    private static Vec3 Uniform(float s) => new(s, s, 1);

    private static Vec4 ZRotation(Random r)
    {
        double a = r.NextDouble() * Math.PI;
        return new Vec4(0, 0, (float)Math.Sin(a / 2), (float)Math.Cos(a / 2));
    }

    private static ObjectRef Ref(Random r, uint[] names) => new()
    {
        ComponentName = names[r.Next(names.Length)],
        Index = (short)r.Next(-1, 400),
        ObjectRefId = (short)r.Next(-1, 64),
    };

    private static string TextLine(Random r, int i)
    {
        string[] words = { "Attack", "Defense", "Level", "Quest", "Reward", "Item", "Equip", "Skill", "Gold", "Upgrade", "Confirm", "Cancel", "Back", "Start", "Options", "Shop", "Bonus", "Daily", "Event", "Limited" };
        int n = 1 + r.Next(6);
        return string.Join(" ", Enumerable.Range(0, n).Select(_ => words[r.Next(words.Length)])) + (i % 7 == 0 ? " ×" + r.Next(99) : "");
    }
}
