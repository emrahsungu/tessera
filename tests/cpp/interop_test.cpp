// Reads buffers written by the .NET tests (tests/Tessera.Tests) through the generated header and checks the values.
#include "Tessera.Tests.tessera.hpp"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace m = testmodels;

static int g_failures = 0;
#define CHECK(cond)                                                                              \
    do {                                                                                         \
        if (!(cond)) {                                                                           \
            std::fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #cond);       \
            ++g_failures;                                                                        \
        }                                                                                        \
    } while (0)

struct Buffer {
    std::vector<std::uint64_t> storage;  // 8-byte aligned
    std::size_t size = 0;
    const void* data() const { return storage.data(); }
};

static Buffer load(const std::string& dir, const char* name) {
    std::ifstream f(dir + "/" + name + ".bin", std::ios::binary | std::ios::ate);
    Buffer b;
    if (!f) {
        std::fprintf(stderr, "cannot open %s.bin\n", name);
        ++g_failures;
        return b;
    }
    b.size = static_cast<std::size_t>(f.tellg());
    b.storage.resize((b.size + 7) / 8);
    f.seekg(0);
    f.read(reinterpret_cast<char*>(b.storage.data()), static_cast<std::streamsize>(b.size));
    return b;
}

static void check_full(const m::Monster& mo) {
    CHECK(mo.name() == "Orc \"Grunt\" \xc3\xbcn\xc3\xaf\x63\xc3\xb8\x64\xc3\xa9 \xe2\x9c\x93");
    CHECK(mo.hp() == 300);
    CHECK(mo.mana().has_value() && *mo.mana() == 0);
    CHECK(mo.pos().x == 1 && mo.pos().y == 2 && mo.pos().z == 3);
    CHECK(mo.velocity().has_value() && std::signbit(mo.velocity()->x) && mo.velocity()->y == 3.40282347e+38f);
    CHECK(mo.color() == m::Color::Red);
    CHECK(mo.friendly() == true);
    CHECK(mo.boss().has_value() && *mo.boss() == false);
    CHECK(mo.weapons().size() == 3);
    CHECK(mo.weapons()[0].name() == "Axe" && mo.weapons()[0].damage() == 9);
    CHECK(mo.weapons()[1].name() == "Bow" && mo.weapons()[1].damage() == -3);
    CHECK(mo.inventory().size() == 5 && mo.inventory()[3] == INT32_MIN && mo.inventory()[4] == INT32_MAX);
    CHECK(mo.tags().size() == 4 && mo.tags()[0] == "a" && mo.tags()[1].data() == nullptr && mo.tags()[2].empty() && mo.tags()[2].data() != nullptr);
    CHECK(mo.equipped().name() == "Sword" && mo.equipped().damage() == 12);
    CHECK(std::isnan(mo.score()));
    CHECK(mo.id() == INT64_MIN);
    CHECK(mo.loot().type() == m::Item::Tag::Key);
    CHECK(mo.loot().as_key().door() == 7 && mo.loot().as_key().golden() && mo.loot().as_key().label() == "gate");
    CHECK(!mo.loot().as_potion());
    CHECK(mo.bag().size() == 3 && mo.bag()[0].as_potion().heal() == 25 && !mo.bag()[1] && mo.bag()[2].as_key().door() == 1);
    CHECK(mo.path().size() == 2 && mo.path()[1].z == 1);
    CHECK(mo.grid().size() == 3 && mo.grid()[0].size() == 2 && mo.grid()[0][1] == 2 && !mo.grid()[1] && mo.grid()[2] && mo.grid()[2].empty());
    CHECK(mo.abilities() == (m::Abilities::Fly | m::Abilities::Burrow));
    CHECK(mo.initial() == u'\x0416');
    CHECK(mo.blob().size() == 3 && mo.blob()[1] == 255);
    CHECK(mo.move_speed() == 4.5f);
    CHECK(mo.tint().r == 1 && mo.tint().a == 4);
    CHECK(mo.ref().component_name == 0xDEADBEEFu && mo.ref().index == -1 && mo.ref().object_ref_id == 5);
    CHECK(mo.odd().tag == 7 && mo.odd().value == -2.5 && mo.odd().flag);
    CHECK(mo.samples().size() == 2 && mo.samples()[0] == 0.1 && std::signbit(mo.samples()[1]));
    CHECK(mo.unique().size() == 1 && mo.unique()[0] == "x");
    CHECK(mo.big() == UINT64_MAX);
    CHECK(mo.tiny() == -128);
    CHECK(mo.switches().size() == 3 && mo.switches()[0] && !mo.switches()[1]);
    int sum = 0;
    for (int v : mo.inventory()) sum += v == INT32_MIN || v == INT32_MAX ? 0 : v;
    CHECK(sum == 6);
}

static void check_sparse(const m::Monster& mo) {
    CHECK(!mo.has_name() && mo.name().empty());
    CHECK(!mo.has_hp() && mo.hp() == 100);
    CHECK(!mo.mana().has_value());
    CHECK(!mo.has_color() && mo.color() == m::Color::Blue);
    CHECK(!mo.friendly() && !mo.boss().has_value());
    CHECK(!mo.weapons() && mo.weapons().size() == 0);
    CHECK(!mo.equipped() && mo.equipped().damage() == 0);
    CHECK(!mo.loot() && mo.loot().type() == m::Item::Tag::None);
    CHECK(!mo.has_pos() && mo.pos().x == 0);
}

static void check_evolution(const std::string& dir) {
    namespace v1 = testmodels::v1;
    namespace v2 = testmodels::v2;
    namespace v3 = testmodels::v3;
    {   // Reordered members (V3 is V1 with every member moved): the same layout, so both read with compile-time positions.
        Buffer b = load(dir, "player_v3");
        tessera::Reader<v1::Player> r(b.data(), b.size);
        CHECK(r && r.exact_schema());
        auto p = r.root();
        CHECK(p.name() == "Ann" && p.level() == 12 && p.health() == 0.75f && p.online());
        CHECK(p.removed() == 99 && p.kind_changed() == -2);
        CHECK(p.items().size() == 2 && p.items()[0].id() == "potion" && p.items()[0].count() == 3 && p.items()[1].id() == "key");
        CHECK(p.stats().str() == 5 && p.stats().dex() == 7 && p.favorite().id() == "key" && p.favorite().count() == 1);
        Buffer b1 = load(dir, "player_v1");
        tessera::Reader<v3::Player> r3(b1.data(), b1.size);
        CHECK(r3 && r3.exact_schema());
        CHECK(r3.root().name() == "Ann" && r3.root().removed() == 99 && r3.root().stats().dex() == 7 && r3.root().items()[1].count() == 1);
    }
    {   // Old buffer, new reader.
        Buffer b = load(dir, "player_v1");
        tessera::Reader<v2::Player> r(b.data(), b.size);
        CHECK(r);
        CHECK(!r.exact_schema());
        auto p = r.root();
        CHECK(p.display_name() == "Ann");
        CHECK(p.level() == 12 && p.health() == 0.75f && p.online());
        CHECK(!p.has_gold() && p.gold() == 50);
        CHECK(!p.has_kind_changed() && p.kind_changed() == 0);
        CHECK(!p.has_title());
        CHECK(p.items().size() == 2 && p.items()[0].id() == "potion" && p.items()[0].count() == 3 && !p.items()[0].has_rarity());
        CHECK(p.items()[1].id() == "key" && p.items()[1].count() == 1);
        CHECK(p.stats().str() == 5 && p.stats().dex() == 7);
        CHECK(p.stats().tessera_binding() == nullptr);  // unchanged type: compile-time positions
        CHECK(p.favorite().id() == "key");
    }
    {   // New buffer, old reader.
        Buffer b = load(dir, "player_v2");
        tessera::Reader<v1::Player> r(b.data(), b.size);
        CHECK(r);
        auto p = r.root();
        CHECK(p.name() == "Bob");
        CHECK(p.level() == 3 && p.health() == 1.0f && !p.online() && !p.has_online());
        CHECK(!p.has_removed() && !p.has_kind_changed());
        CHECK(p.items().size() == 1 && p.items()[0].id() == "gem" && p.items()[0].count() == 5);
        CHECK(p.stats().str() == 1 && p.stats().dex() == 2);
        CHECK(!p.favorite());
    }
    {   // Same version through the exact path.
        Buffer b = load(dir, "player_v2");
        tessera::Reader<v2::Player> r(b.data(), b.size);
        CHECK(r && r.exact_schema() && r.root().gold() == 1000 && r.root().title() == "Sir" && r.root().kind_changed() == 123456);
    }
    {   // Without an embedded schema a different version cannot be read.
        Buffer b = load(dir, "player_v1_noschema");
        tessera::Reader<v2::Player> r(b.data(), b.size);
        CHECK(!r && r.error() == tessera::Error::SchemaMismatch);
        tessera::Reader<v1::Player> same(b.data(), b.size);
        CHECK(same && same.root().removed() == 99);
    }
}

static void check_fixed(const std::string& dir, bool print) {
    Buffer b = load(dir, "swarm");
    tessera::Reader<m::Swarm> r(b.data(), b.size);
    CHECK(r && r.exact_schema());
    auto s = r.root();
    CHECK(s.particles().size() == 2);
    auto p = s.particles()[0];
    CHECK(p.id() == INT64_MIN && p.x() == 0.0f && std::signbit(p.x()));
    CHECK(p.velocity().x == 1 && p.velocity().y == 2 && p.velocity().z == 3);
    CHECK(p.kind() == -7 && p.flags() == 0xFF && p.tint() == m::Color::Red && p.visible());
    CHECK(p.mass().has_value() && *p.mass() == 2.5 && p.name() == "p0" && p.has_life() && p.life() == 0);
    CHECK(p.has_id() && p.has_x() && p.has_velocity() && p.has_kind() && p.has_flags() && p.has_tint());
    // Fixed cells are stored even when they equal their defaults; the other members are not.
    auto d = s.particles()[1];
    CHECK(d.id() == 0 && d.x() == 1.5f && d.tint() == m::Color::Blue && d.has_x() && d.has_tint());
    CHECK(!d.has_mass() && !d.has_name() && !d.has_life() && d.life() == 10 && !d.visible());
    CHECK(s.leader().id() == 42 && s.leader().name() == "lead" && s.leader().x() == 1.5f);
    // An absent particle reads every member as its default, fixed cells included.
    auto none = s.missing();
    CHECK(!none && none.id() == 0 && none.x() == 1.5f && none.tint() == m::Color::Blue && none.velocity().z == 0);
    CHECK(!none.has_id() && !none.has_x() && !none.has_tint() && none.life() == 10);
    if (print) std::cout << "swarm: " << tessera::to_json(s) << "\n";
}

static void check_maps(const std::string& dir, bool print) {
    Buffer b = load(dir, "catalog");
    tessera::Reader<m::Catalog> r(b.data(), b.size);
    CHECK(r);
    auto c = r.root();
    auto stock = c.stock();
    CHECK(stock.size() == 6);
    CHECK(stock.find("apple") == 5 && stock.find("pear") == 3 && stock.find("") == 7 && stock.find("zebra") == 0);
    CHECK(!stock.find("plum") && !stock.find("appl") && !stock.contains("zz"));
    CHECK(stock.find("\xF0\x9F\x98\x80 smile") == 9 && stock.find("\xEF\xBF\xBD replacement") == 1);
    for (std::uint32_t i = 1; i < stock.size(); ++i) CHECK(stock.keys()[i - 1] < stock.keys()[i]);  // by UTF-8 bytes
    auto names = c.names();
    CHECK(names.keys()[0] == -1 && names.find(-1) == "minus one" && names.find(INT32_MAX) == "max" && names.contains(0) && !names.find(1));
    CHECK(c.by_color().find(m::Color::Red)->name() == "Axe" && c.by_color().find(m::Color::Blue)->damage() == 2);
    CHECK(!c.by_color().find(m::Color::Green));
    CHECK(c.tags().find(10)->size() == 2 && (*c.tags().find(10))[1] == "b" && c.tags().find(5)->empty() && !c.tags().find(7));
    CHECK(c.points().find(u'a')->x == 4 && c.points().find(u'z')->z == 3);
    CHECK(c.nested().find("x")->find("y") == 1 && !c.nested().find("y"));
    CHECK(c.empty() && c.empty().size() == 0 && !c.empty().find(1));
    CHECK(!c.missing() && c.missing().size() == 0 && !c.missing().contains(1));
    if (print) std::cout << "catalog: " << tessera::to_json(c) << "\n";
}

static void check_optional_elements(const std::string& dir, bool print) {
    Buffer b = load(dir, "readings");
    tessera::Reader<m::Readings> r(b.data(), b.size);
    CHECK(r);
    auto x = r.root();
    auto ints = x.ints();
    CHECK(ints.size() == 40);
    for (std::uint32_t i = 0; i < 40; ++i) {
        if (i % 3 == 0) CHECK(!ints[i] && !ints.has(i));
        else CHECK(ints.has(i) && ints[i] == static_cast<std::int32_t>(i * 7));
    }
    int present = 0;
    for (std::optional<std::int32_t> v : ints) present += v.has_value();
    CHECK(present == 26);
    CHECK(x.doubles().size() == 3 && x.doubles()[0] == 1.5 && !x.doubles()[1] && std::signbit(*x.doubles()[2]));
    CHECK(x.flags()[0] == true && !x.flags()[1] && x.flags()[2] == false);
    CHECK(x.colors()[0] == m::Color::Red && !x.colors()[1] && x.colors()[2] == m::Color::Blue);
    CHECK(x.points().size() == 2 && x.points()[0]->y == 2 && !x.points()[1]);
    CHECK(x.nested().size() == 3 && x.nested()[0][0] == 1 && !x.nested()[0][1] && x.nested()[1].empty() && !x.nested()[2][0]);
    auto a = x.optional().find("a");
    auto none = x.optional().find("b");
    CHECK(a && *a == 1 && none && !*none && !x.optional().find("c"));
    if (print) std::cout << "readings: " << tessera::to_json(x) << "\n";
}

static void check_wide(const std::string& dir) {
    {
        Buffer b = load(dir, "wide_dense");
        tessera::Reader<m::Wide> r(b.data(), b.size);
        CHECK(r);
        auto w = r.root();
        CHECK(w.f00() == 1 && w.f17() == 17001 && w.f31() == 31001 && w.f32() == 32001 && w.f39() == 39001);
        CHECK(w.b00() && !w.b01() && !w.b02() && w.b03() && w.b18() && !w.b19());
        CHECK(w.s0() == "s0" && w.s7() == "s7");
        CHECK(w.d0() == 0.5 && w.d3() == 3.5);
    }
    {
        Buffer b = load(dir, "wide_sparse");
        tessera::Reader<m::Wide> r(b.data(), b.size);
        CHECK(r);
        auto w = r.root();
        CHECK(w.f07() == 7 && w.f39() == -39 && w.f00() == 0 && !w.has_f38());
        CHECK(w.b19() && w.has_b19() && !w.has_b00());
        CHECK(w.s3() == "x" && !w.has_s2());
        CHECK(w.d2() == 2.5 && w.d3() == 0);
    }
}
int main(int argc, char** argv) {
    const std::string dir = argc > 1 ? argv[1] : "generated";
    bool print = argc > 2 && std::strcmp(argv[2], "--print") == 0;

    for (const char* name : {"monster_full", "monster_full_noshare", "monster_full_shareall", "monster_full_noschema"}) {
        Buffer b = load(dir, name);
        tessera::Reader<m::Monster> r(b.data(), b.size);
        if (!r) {
            std::fprintf(stderr, "%s: %s\n", name, tessera::to_string(r.error()));
            ++g_failures;
            continue;
        }
        CHECK(r.exact_schema());
        check_full(r.root());
        if (print) std::cout << name << ": " << tessera::to_json(r.root()) << "\n";
    }

    {
        Buffer b = load(dir, "monster_sparse");
        tessera::Reader<m::Monster> r(b.data(), b.size);
        CHECK(r);
        check_sparse(r.root());
        if (print) std::cout << "sparse: " << tessera::to_json(r.root()) << "\n";
    }
    {
        Buffer b = load(dir, "monster_defaults");
        tessera::Reader<m::Monster> r(b.data(), b.size);
        CHECK(r);
        CHECK(r.root().has_hp() && r.root().hp() == 100 && r.root().has_friendly() && !r.root().friendly());
        CHECK(!r.root().has_mana());
    }
    {
        Buffer b = load(dir, "tree");
        tessera::Reader<m::Node> r(b.data(), b.size);
        CHECK(r);
        auto root = r.root();
        CHECK(root.name() == "root" && root.children().size() == 3);
        CHECK(root.children()[1].children()[0].name() == "c" && root.children()[1].children()[0].children().empty());
        CHECK(!root.children()[0].children());
        // Shared subtrees: equal nodes are stored once.
        CHECK(root.children()[0].tessera_data() == root.children()[2].tessera_data());
        if (print) std::cout << "tree: " << tessera::to_json(root) << "\n";
    }
    {
        // The deepest buffer the C# writer produces with default options verifies with default options.
        Buffer b = load(dir, "deep_default");
        tessera::Reader<m::Node> r(b.data(), b.size);
        CHECK(r);
        int levels = 1;
        for (m::Node n = r.root(); n.children().size() == 1; n = n.children()[0]) ++levels;
        CHECK(levels == 64);
    }
    {
        Buffer b = load(dir, "deep_200");
        CHECK(tessera::verify<m::Node>(b.data(), b.size) == tessera::Error::TooDeep);
        tessera::Options deep;
        deep.max_depth = 200;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, deep) == tessera::Error::None);
    }
    {
        // Each node's two children are the same node, 14 levels over: a few hundred bytes that a tree walk would visit
        // as about 65,000 items. Each item is verified once, and the depth limit still sees the deepest path.
        Buffer b = load(dir, "dag");
        CHECK(tessera::verify<m::Node>(b.data(), b.size) == tessera::Error::None);
        tessera::Options few;
        few.max_items = 1000;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, few) == tessera::Error::None);
        few.max_items = 10;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, few) == tessera::Error::TooManyItems);
        tessera::Options depth;
        depth.max_depth = 29;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, depth) == tessera::Error::None);
        depth.max_depth = 28;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, depth) == tessera::Error::TooDeep);
        tessera::Reader<m::Node> r(b.data(), b.size);
        CHECK(r);
        int levels = 0;
        for (m::Node n = r.root(); n.children().size() == 2; n = n.children()[1]) {
            CHECK(n.children()[0].tessera_data() == n.children()[1].tessera_data());
            ++levels;
        }
        CHECK(levels == 14);
    }
    for (const char* name : {"dag_twice", "dag_twice_deep_first"}) {
        // A shared subtree below the root (depth 2) and at the end of a chain (depth 12), in either order: its deepest
        // item (depth 29) is only that deep on the second path, whichever is verified first.
        Buffer b = load(dir, name);
        tessera::Options depth;
        depth.max_depth = 29;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, depth) == tessera::Error::None);
        depth.max_depth = 28;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, depth) == tessera::Error::TooDeep);
    }
    {
        // One list of 2,000 strings referenced 2,000 times: its references are counted, so it is verified once.
        Buffer b = load(dir, "shared_tags");
        tessera::Options utf8;
        utf8.utf8 = true;
        CHECK(tessera::verify<m::TagCloud>(b.data(), b.size) == tessera::Error::None);
        CHECK(tessera::verify<m::TagCloud>(b.data(), b.size, utf8) == tessera::Error::None);
        tessera::Reader<m::TagCloud> r(b.data(), b.size);
        CHECK(r && r.root().groups().size() == 2000 && r.root().groups()[1999][1999] == "tag1999");
        CHECK(r.root().groups()[0][7].data() == r.root().groups()[1999][7].data());  // one list, stored once
    }
    {
        // One 20 KB string named by 500 nodes: with utf8, validated once per reference only up to a budget.
        Buffer b = load(dir, "shared_long_string");
        tessera::Options utf8;
        utf8.utf8 = true;
        CHECK(tessera::verify<m::Node>(b.data(), b.size, utf8) == tessera::Error::None);
        tessera::Reader<m::Node> r(b.data(), b.size, utf8);
        CHECK(r && r.root().children().size() == 500 && r.root().children()[499].name().size() == 1300 * 18);
    }
    {
        // Unknown header flags come from a newer writer: refuse rather than misread.
        Buffer b = load(dir, "monster_full");
        reinterpret_cast<std::uint8_t*>(b.storage.data())[7] |= 2u;
        CHECK(tessera::verify<m::Monster>(b.data(), b.size) == tessera::Error::ReservedBits);
    }
    {
        Buffer b = load(dir, "empty");
        tessera::Reader<m::Empty> r(b.data(), b.size);
        CHECK(r);
        CHECK(tessera::to_json(r.root()) == "{}");
    }

    check_evolution(dir);
    check_wide(dir);
    check_fixed(dir, print);
    check_maps(dir, print);
    check_optional_elements(dir, print);

    if (g_failures == 0) std::printf("interop: all checks passed\n");
    return g_failures == 0 ? 0 : 1;
}
