using System.Collections.Generic;
using Tessera;

// Two versions of the same model. Buffers written with one are read in C++ with the other's header.

namespace TestModels.V1
{
    [Tessera("Player")]
    public class Player
    {
        public string? Name;
        public int Level;
        public float Health;
        public bool Online;
        public List<Item>? Items;
        public Stats? Stats;
        [TesseraKeepDefault] public int Removed;            // a fixed cell the new version lacks
        public short KindChanged;
        public Item? Favorite;
        public Dictionary<string, int>? Scores;
    }

    [Tessera("Item")]
    public class Item
    {
        public string? Id;
        public int Count;
    }

    [Tessera("Stats")]
    public class Stats
    {
        public int Str;
        public int Dex;
    }
}

namespace TestModels.V2
{
    [Tessera("Player")]
    public class Player
    {
        public bool Online;                               // moved
        [TesseraName("Name")] public string? DisplayName;   // renamed, wire name kept
        [TesseraKeepDefault] public long Gold = 50;         // added, with a default, as a fixed cell
        [TesseraKeepDefault] public int Level;              // now a fixed cell
        [TesseraKeepDefault] public float Health;           // now a fixed cell
        public List<Item>? Items;
        public Stats? Stats;
        public int KindChanged;                           // short -> int: unreadable, reads as absent
        public string? Title;                             // added
        public Item? Favorite;
        public Dictionary<string, long>? Scores;          // values int -> long: unreadable, so the dictionary has no values
    }

    [Tessera("Item")]
    public class Item
    {
        [TesseraKeepDefault] public byte Rarity;            // added, as a fixed cell
        public int Count;                                 // moved
        public string? Id;
    }

    [Tessera("Stats")]
    public class Stats                                    // unchanged: read with compile-time positions
    {
        public int Str;
        public int Dex;
    }
}

namespace TestModels.V3
{
    // V1 with every member moved and some fields turned into properties: the same layout, so V1 and V3 read each
    // other's buffers with compile-time positions.
    [Tessera("Player")]
    public class Player
    {
        public Item? Favorite { get; set; }
        public short KindChanged;
        public List<Item>? Items;
        public bool Online { get; set; }
        [TesseraKeepDefault] public int Removed;
        public float Health;
        public Stats? Stats;
        public int Level { get; set; }
        public Dictionary<string, int>? Scores { get; set; }
        public string? Name;
    }

    [Tessera("Item")]
    public class Item
    {
        public int Count;
        public string? Id { get; set; }
    }

    [Tessera("Stats")]
    public class Stats
    {
        public int Dex;
        public int Str;
    }
}
