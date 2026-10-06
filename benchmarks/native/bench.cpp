// C++ read benchmark: Tessera vs FlatBuffers vs MessagePack (msgpack-cxx) on buffers written by
// benchmarks/Tessera.Benchmarks. Every reader must reproduce the C# checksum before anything is timed.
//
// usage: tessera_bench <generated/data dir> [--rounds N] [--round-ms N] [--json out.json] [--filter text]
//   --filter keeps only cases whose "workload/library/operation" contains the text (for focused tuning).
#include "readers.hpp"

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <functional>
#include <map>
#include <memory>
#include <numeric>
#include <random>
#include <sstream>
#include <string>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#elif defined(__linux__)
#include <pthread.h>
#include <sched.h>
#endif

namespace {

volatile std::uint64_t g_sink;

struct Buffer {
    std::unique_ptr<std::uint64_t[]> storage;  // 8-byte aligned, as a file loaded by an engine would be
    std::size_t size = 0;
    const std::uint8_t* data() const { return reinterpret_cast<const std::uint8_t*>(storage.get()); }
};

Buffer load(const std::string& path) {
    std::ifstream f(path, std::ios::binary | std::ios::ate);
    if (!f) {
        std::fprintf(stderr, "missing %s\n", path.c_str());
        std::exit(2);
    }
    Buffer b;
    b.size = static_cast<std::size_t>(f.tellg());
    b.storage.reset(new std::uint64_t[(b.size + 7) / 8]());
    f.seekg(0);
    f.read(reinterpret_cast<char*>(b.storage.get()), static_cast<std::streamsize>(b.size));
    return b;
}

struct Case {
    std::string workload, library, op;
    std::function<std::uint64_t()> fn;
    std::vector<double> ns;
};

std::uint64_t calibrate(Case& c, double ms) {
    std::uint64_t n = 1;
    for (;;) {
        auto t0 = std::chrono::steady_clock::now();
        for (std::uint64_t i = 0; i < n; ++i) g_sink = g_sink ^ c.fn();
        double el = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();
        if (el >= ms / 4 || n >= (1u << 26)) return std::max<std::uint64_t>(1, static_cast<std::uint64_t>(n * ms / std::max(el, 0.0001)));
        n *= 2;
    }
}

double median(std::vector<double> v) {
    std::sort(v.begin(), v.end());
    return v.size() % 2 ? v[v.size() / 2] : (v[v.size() / 2 - 1] + v[v.size() / 2]) / 2;
}

double mad_percent(const std::vector<double>& v) {
    double m = median(v);
    std::vector<double> d;
    for (double x : v) d.push_back(std::abs(x - m));
    return m > 0 ? 100 * median(d) / m : 0;
}

void pin_thread() {
#if defined(_WIN32)
    SetThreadAffinityMask(GetCurrentThread(), 1ull << 3);
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
#elif defined(__linux__)
    cpu_set_t set;
    CPU_ZERO(&set);
    CPU_SET(3, &set);
    pthread_setaffinity_np(pthread_self(), sizeof(set), &set);
#endif
}

std::string compiler() {
#if defined(__clang__)
    return "clang " + std::to_string(__clang_major__) + "." + std::to_string(__clang_minor__);
#elif defined(_MSC_VER)
    return "msvc " + std::to_string(_MSC_FULL_VER / 10000000) + "." + std::to_string(_MSC_FULL_VER / 100000 % 100) + "." + std::to_string(_MSC_FULL_VER % 100000);
#elif defined(__GNUC__)
    return "gcc " + std::to_string(__GNUC__) + "." + std::to_string(__GNUC_MINOR__);
#else
    return "unknown";
#endif
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: %s <data dir> [--rounds N] [--round-ms N] [--json file]\n", argv[0]);
        return 2;
    }
    const std::string dir = argv[1];
    int rounds = 11;
    double round_ms = 60;
    std::string json_path, filter;
    for (int i = 2; i + 1 < argc; ++i) {
        if (!std::strcmp(argv[i], "--rounds")) rounds = std::atoi(argv[++i]);
        else if (!std::strcmp(argv[i], "--round-ms")) round_ms = std::atof(argv[++i]);
        else if (!std::strcmp(argv[i], "--json")) json_path = argv[++i];
        else if (!std::strcmp(argv[i], "--filter")) filter = argv[++i];
    }

    std::map<std::string, std::uint64_t> expected;
    {
        std::ifstream f(dir + "/expected.txt");
        std::string name;
        std::uint64_t value;
        while (f >> name >> value) expected[name] = value;
    }

    std::map<std::string, Buffer> buffers;
    for (const char* w : {"prefab", "monsters", "records", "series", "dense-unique", "sparse-unique", "dense-shared", "lookup", "canada"})
        for (const char* lib : {"tessera", "tessera-all", "tessera-noshare", "fb", "msgpack"})
            buffers[std::string(w) + "." + lib] = load(dir + "/" + w + "." + lib + ".bin");
    buffers["series.tessera-fixed"] = load(dir + "/series.tessera-fixed.bin");
    std::map<std::string, std::shared_ptr<std::vector<std::uint32_t>>> picks_of;

    // Parsed MessagePack trees kept alive for the traversal/random-access cases (parse cost is measured separately).
    std::vector<msgpack::object_handle> parsed;
    std::vector<Case> cases;
    bool ok = true;
    std::mt19937 rng(7);

    auto check = [&](const std::string& what, std::uint64_t got, std::uint64_t want) {
        if (got != want) {
            std::fprintf(stderr, "CHECKSUM MISMATCH %s: %llu != %llu\n", what.c_str(), (unsigned long long)got, (unsigned long long)want);
            ok = false;
        }
    };

    // ------------------------------------------------------------------------------------------------ per workload
    auto add_workload = [&](const std::string& w, auto tessera_root_tag, auto tessera_walk, auto fb_root, auto fb_verify, auto fb_walk,
                            auto mp_walk, auto tessera_pick, auto fb_pick, auto mp_pick, auto count) {
        using TesseraRoot = decltype(tessera_root_tag);
        const Buffer& in = buffers[w + ".tessera"];
        const Buffer& fbb = buffers[w + ".fb"];
        const Buffer& mp = buffers[w + ".msgpack"];
        const std::uint64_t want = expected[w];

        for (const char* variant : {".tessera", ".tessera-all", ".tessera-noshare"}) {
            const Buffer& b = buffers[w + variant];
            tessera::Reader<TesseraRoot> r(b.data(), b.size);
            if (!r) {
                std::fprintf(stderr, "%s%s: %s\n", w.c_str(), variant, tessera::to_string(r.error()));
                ok = false;
                continue;
            }
            check(w + variant, tessera_walk(r.root()), want);
        }
        {
            flatbuffers::Verifier v(fbb.data(), fbb.size);
            if (!fb_verify(v)) { std::fprintf(stderr, "%s: flatbuffers verify failed\n", w.c_str()); ok = false; }
            check(w + ".fb", fb_walk(fb_root(fbb.data())), want);
        }
        parsed.push_back(msgpack::unpack(reinterpret_cast<const char*>(mp.data()), mp.size));
        // Shallow copy of the root object; its zone stays alive in `parsed` (handles may move, the zone does not).
        const auto mpo = std::make_shared<msgpack::object>(parsed.back().get());
        check(w + ".msgpack", mp_walk(*mpo), want);

        // Random picks shared by all libraries.
        const std::uint32_t n = count(tessera::root_unchecked<TesseraRoot>(in.data()));
        auto picks = std::make_shared<std::vector<std::uint32_t>>(1000);
        for (auto& p : *picks) p = static_cast<std::uint32_t>(rng() % n);
        picks_of[w] = picks;

        const std::uint8_t* inp = in.data();
        const std::size_t ins = in.size;
        const std::uint8_t* fbp = fbb.data();
        const std::size_t fbs = fbb.size;
        const char* mpp = reinterpret_cast<const char*>(mp.data());
        const std::size_t mps = mp.size;

        cases.push_back({w, "Tessera", "verify", [=] { return static_cast<std::uint64_t>(tessera::verify<TesseraRoot>(inp, ins)); }, {}});
        cases.push_back({w, "FlatBuffers", "verify", [=] { flatbuffers::Verifier v(fbp, fbs); return static_cast<std::uint64_t>(fb_verify(v)); }, {}});
        cases.push_back({w, "MessagePack", "verify (parse)", [=] { auto oh = msgpack::unpack(mpp, mps); return static_cast<std::uint64_t>(oh.get().via.array.size); }, {}});

        cases.push_back({w, "Tessera", "full traversal", [=] { return tessera_walk(tessera::root_unchecked<TesseraRoot>(inp)); }, {}});
        cases.push_back({w, "FlatBuffers", "full traversal", [=] { return fb_walk(fb_root(fbp)); }, {}});
        cases.push_back({w, "MessagePack", "full traversal (parsed tree)", [=] { return mp_walk(*mpo); }, {}});

        cases.push_back({w, "Tessera", "verify + traversal", [=] {
            tessera::Reader<TesseraRoot> r(inp, ins);
            return tessera_walk(r.root());
        }, {}});
        cases.push_back({w, "FlatBuffers", "verify + traversal", [=] {
            flatbuffers::Verifier v(fbp, fbs);
            return fb_verify(v) ? fb_walk(fb_root(fbp)) : 0;
        }, {}});
        cases.push_back({w, "MessagePack", "verify + traversal", [=] {
            auto oh = msgpack::unpack(mpp, mps);
            return mp_walk(oh.get());
        }, {}});

        cases.push_back({w, "Tessera", "random access x1000", [=] {
            auto root = tessera::root_unchecked<TesseraRoot>(inp);
            std::uint64_t s = 0;
            for (std::uint32_t i : *picks) s += tessera_pick(root, i);
            return s;
        }, {}});
        cases.push_back({w, "FlatBuffers", "random access x1000", [=] {
            auto root = fb_root(fbp);
            std::uint64_t s = 0;
            for (std::uint32_t i : *picks) s += fb_pick(root, i);
            return s;
        }, {}});
        cases.push_back({w, "MessagePack", "random access x1000 (parsed tree)", [=] {
            std::uint64_t s = 0;
            for (std::uint32_t i : *picks) s += mp_pick(*mpo, i);
            return s;
        }, {}});

        const std::uint32_t mid = n / 2;
        // Trusted open, like flatbuffers::GetRoot (no checks). tessera::Reader adds header and schema checks (~10 ns).
        cases.push_back({w, "Tessera", "open + read one field", [=] { return tessera_pick(tessera::root_unchecked<TesseraRoot>(inp), mid); }, {}});
        cases.push_back({w, "FlatBuffers", "open + read one field", [=] { return fb_pick(fb_root(fbp), mid); }, {}});
        cases.push_back({w, "MessagePack", "open + read one field", [=] {
            auto oh = msgpack::unpack(mpp, mps);
            return mp_pick(oh.get(), mid);
        }, {}});

        // Tessera with full sharing (equal objects and vectors stored once), and Tessera's verifier with UTF-8 validation.
        const Buffer& all = buffers[w + ".tessera-all"];
        const std::uint8_t* allp = all.data();
        const std::size_t alls = all.size;
        cases.push_back({w, "Tessera (full sharing)", "verify", [=] { return static_cast<std::uint64_t>(tessera::verify<TesseraRoot>(allp, alls)); }, {}});
        cases.push_back({w, "Tessera (full sharing)", "full traversal", [=] { return tessera_walk(tessera::root_unchecked<TesseraRoot>(allp)); }, {}});
        cases.push_back({w, "Tessera (full sharing)", "verify + traversal", [=] {
            tessera::Reader<TesseraRoot> r(allp, alls);
            return tessera_walk(r.root());
        }, {}});
        cases.push_back({w, "Tessera (full sharing)", "random access x1000", [=] {
            auto root = tessera::root_unchecked<TesseraRoot>(allp);
            std::uint64_t s = 0;
            for (std::uint32_t i : *picks) s += tessera_pick(root, i);
            return s;
        }, {}});
        cases.push_back({w, "Tessera", "UTF-8 verify", [=] {
            tessera::Options o;
            o.utf8 = true;
            return static_cast<std::uint64_t>(tessera::verify<TesseraRoot>(inp, ins, o));
        }, {}});
        {
            tessera::Options o;
            o.utf8 = true;
            if (tessera::verify<TesseraRoot>(inp, ins, o) != tessera::Error::None) {
                std::fprintf(stderr, "%s: UTF-8 verification failed\n", w.c_str());
                ok = false;
            }
        }

        // Cross-check the random-access picks agree across libraries.
        std::uint64_t a = 0, a2 = 0, b = 0, c = 0;
        for (std::uint32_t i : *picks) {
            a += tessera_pick(tessera::root_unchecked<TesseraRoot>(inp), i);
            a2 += tessera_pick(tessera::root_unchecked<TesseraRoot>(allp), i);
            b += fb_pick(fb_root(fbp), i);
            c += mp_pick(*mpo, i);
        }
        if (a != b || a != c || a != a2) {
            std::fprintf(stderr, "RANDOM ACCESS MISMATCH %s: %llu %llu %llu %llu\n", w.c_str(), (unsigned long long)a, (unsigned long long)a2, (unsigned long long)b, (unsigned long long)c);
            ok = false;
        }
    };

    using mp_read::at;
    using mp_read::integer;
    add_workload(
        "prefab", im::Prefab{}, tessera_read::prefab, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::Prefab>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::Prefab>(nullptr); }, fb_read::prefab, mp_read::prefab,
        [](im::Prefab p, std::uint32_t i) -> std::uint64_t {
            auto o = p.objects()[i];
            return o.name().size() + static_cast<std::uint64_t>(o.position().x) + o.components().size() + static_cast<std::uint64_t>(o.id());
        },
        [](const fbm::Prefab* p, std::uint32_t i) -> std::uint64_t {
            auto o = p->objects()->Get(i);
            return (o->name() ? o->name()->size() : 0) + static_cast<std::uint64_t>(o->position() ? o->position()->x() : 0.f) +
                   (o->components() ? o->components()->size() : 0) + static_cast<std::uint64_t>(o->id());
        },
        [](const msgpack::object& p, std::uint32_t i) -> std::uint64_t {
            const auto& o = at(at(p, 0), i);
            const auto& name = at(o, 0);
            const auto& comps = at(o, 1);
            return (name.type == msgpack::type::NIL ? 0 : name.via.str.size) + static_cast<std::uint64_t>(mp_read::f32(at(at(o, 4), 0))) +
                   (comps.type == msgpack::type::NIL ? 0 : comps.via.array.size) + integer(at(o, 10));
        },
        [](im::Prefab p) { return p.objects().size(); });

    add_workload(
        "monsters", im::World{}, tessera_read::world, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::World>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::World>(nullptr); }, fb_read::world, mp_read::world,
        [](im::World w, std::uint32_t i) -> std::uint64_t {
            auto m = w.monsters()[i];
            return static_cast<std::uint64_t>(m.hp()) + static_cast<std::uint64_t>(m.pos().x) + m.weapons().size() + m.name().size();
        },
        [](const fbm::World* w, std::uint32_t i) -> std::uint64_t {
            auto m = w->monsters()->Get(i);
            return static_cast<std::uint64_t>(m->hp()) + static_cast<std::uint64_t>(m->pos() ? m->pos()->x() : 0.f) +
                   (m->weapons() ? m->weapons()->size() : 0) + (m->name() ? m->name()->size() : 0);
        },
        [](const msgpack::object& w, std::uint32_t i) -> std::uint64_t {
            const auto& m = at(at(w, 0), i);
            const auto& weapons = at(m, 6);
            const auto& name = at(m, 0);
            return integer(at(m, 1)) + static_cast<std::uint64_t>(mp_read::f32(at(at(m, 3), 0))) +
                   (weapons.type == msgpack::type::NIL ? 0 : weapons.via.array.size) + (name.type == msgpack::type::NIL ? 0 : name.via.str.size);
        },
        [](im::World w) { return w.monsters().size(); });

    add_workload(
        "records", im::RecordSet{}, tessera_read::records, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::RecordSet>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::RecordSet>(nullptr); }, fb_read::records, mp_read::records,
        [](im::RecordSet s, std::uint32_t i) -> std::uint64_t {
            auto r = s.records()[i];
            return static_cast<std::uint64_t>(r.i7().value_or(1)) + r.s2().size() + static_cast<std::uint64_t>(r.f3().value_or(2.f)) + r.b5().value_or(true);
        },
        [](const fbm::RecordSet* s, std::uint32_t i) -> std::uint64_t {
            auto r = s->records()->Get(i);
            return static_cast<std::uint64_t>(r->i7().value_or(1)) + (r->s2() ? r->s2()->size() : 0) +
                   static_cast<std::uint64_t>(r->f3().value_or(2.f)) + r->b5().value_or(true);
        },
        [](const msgpack::object& s, std::uint32_t i) -> std::uint64_t {
            const auto& r = at(at(s, 0), i);
            const auto& i7 = at(r, 7);
            const auto& s2 = at(r, 38);
            const auto& f3 = at(r, 19);
            const auto& b5 = at(r, 33);
            return (i7.type == msgpack::type::NIL ? 1 : integer(i7)) + (s2.type == msgpack::type::NIL ? 0 : s2.via.str.size) +
                   static_cast<std::uint64_t>(f3.type == msgpack::type::NIL ? 2.f : mp_read::f32(f3)) + (b5.type == msgpack::type::NIL ? true : b5.via.boolean);
        },
        [](im::RecordSet s) { return s.records().size(); });

    add_workload(
        "series", im::Series{}, tessera_read::series, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::Series>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::Series>(nullptr); }, fb_read::series, mp_read::series,
        [](im::Series s, std::uint32_t i) -> std::uint64_t {
            auto x = s.samples()[i];
            return static_cast<std::uint64_t>(x.value()) + static_cast<std::uint64_t>(x.timestamp());
        },
        [](const fbm::Series* s, std::uint32_t i) -> std::uint64_t {
            auto x = s->samples()->Get(i);
            return static_cast<std::uint64_t>(x->value()) + static_cast<std::uint64_t>(x->timestamp());
        },
        [](const msgpack::object& s, std::uint32_t i) -> std::uint64_t {
            const auto& x = at(at(s, 0), i);
            return static_cast<std::uint64_t>(at(x, 1).via.f64) + integer(at(x, 0));
        },
        [](im::Series s) { return s.samples().size(); });

    // Scene graphs of 1,024 nodes: dense, sparse, and 16 distinct nodes repeated.
    for (const char* sw : {"dense-unique", "sparse-unique", "dense-shared"}) {
        add_workload(
            sw, im::Scene{}, tessera_read::scene, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::Scene>(p); },
            [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::Scene>(nullptr); }, fb_read::scene, mp_read::scene,
            [](im::Scene s, std::uint32_t i) -> std::uint64_t {
                auto n = s.nodes()[i];
                return static_cast<std::uint64_t>(n.id()) + n.name().size() + n.children().size() + static_cast<std::uint64_t>(n.parent().value_or(0));
            },
            [](const fbm::Scene* s, std::uint32_t i) -> std::uint64_t {
                auto n = s->nodes()->Get(i);
                return static_cast<std::uint64_t>(n->id()) + (n->name() ? n->name()->size() : 0) + (n->children() ? n->children()->size() : 0) +
                       static_cast<std::uint64_t>(n->parent().value_or(0));
            },
            [](const msgpack::object& s, std::uint32_t i) -> std::uint64_t {
                const auto& n = at(at(s, 0), i);
                const auto& name = at(n, 1);
                const auto& ch = at(n, 7);
                return integer(at(n, 0)) + (name.type == msgpack::type::NIL ? 0 : name.via.str.size) + (ch.type == msgpack::type::NIL ? 0 : ch.via.array.size) +
                       (at(n, 6).type == msgpack::type::NIL ? 0 : integer(at(n, 6)));
            },
            [](im::Scene s) { return s.nodes().size(); });
    }

    // Dictionaries: Tessera maps and FlatBuffers vectors sorted by key are binary-searched; MessagePack maps are searched
    // in the parsed tree, front to back.
    add_workload(
        "lookup", im::Lookup{}, tessera_read::lookup, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::Lookup>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::Lookup>(nullptr); }, fb_read::lookup, mp_read::lookup,
        [](im::Lookup x, std::uint32_t i) -> std::uint64_t {
            return x.items().keys()[i].size() + static_cast<std::uint64_t>(x.items().values()[i].count());
        },
        [](const fbm::Lookup* x, std::uint32_t i) -> std::uint64_t {
            auto e = x->items()->Get(i);
            return e->name()->size() + static_cast<std::uint64_t>(e->count());
        },
        [](const msgpack::object& x, std::uint32_t i) -> std::uint64_t {
            const auto& kv = at(x, 0).via.map.ptr[i];
            return kv.key.via.str.size + integer(at(kv.val, 0));
        },
        [](im::Lookup x) { return x.items().size(); });
    {
        const std::string w = "lookup";
        const Buffer& in = buffers["lookup.tessera"];
        const Buffer& fbb = buffers["lookup.fb"];
        const std::uint8_t* inp = in.data();
        const std::uint8_t* fbp = fbb.data();
        const auto mpo = std::make_shared<msgpack::object>(parsed.back().get());  // the lookup workload's tree
        // 1,000 keys of each kind, from the data.
        auto names = std::make_shared<std::vector<std::string>>();
        auto ids = std::make_shared<std::vector<std::int32_t>>();
        const im::Lookup root = tessera::root_unchecked<im::Lookup>(inp);
        std::mt19937 krng(11);
        for (int k = 0; k < 1000; ++k) {
            names->emplace_back(root.items().keys()[krng() % root.items().size()]);
            ids->push_back(root.prices().keys()[krng() % root.prices().size()]);
        }
        auto tessera_names = [=] {
            const auto items = tessera::root_unchecked<im::Lookup>(inp).items();
            std::uint64_t s = 0;
            for (const std::string& k : *names) s += static_cast<std::uint64_t>(items.find(k)->count());
            return s;
        };
        auto fb_names = [=] {
            const auto* items = flatbuffers::GetRoot<fbm::Lookup>(fbp)->items();
            std::uint64_t s = 0;
            for (const std::string& k : *names) s += static_cast<std::uint64_t>(items->LookupByKey(k.c_str())->count());
            return s;
        };
        auto mp_names = [=] {
            const msgpack::object& items = at(*mpo, 0);
            std::uint64_t s = 0;
            for (const std::string& k : *names) {
                for (std::uint32_t j = 0; j < items.via.map.size; ++j) {
                    const auto& kv = items.via.map.ptr[j];
                    if (std::string_view(kv.key.via.str.ptr, kv.key.via.str.size) == k) { s += integer(at(kv.val, 0)); break; }
                }
            }
            return s;
        };
        auto tessera_ids = [=] {
            const auto prices = tessera::root_unchecked<im::Lookup>(inp).prices();
            double s = 0;
            for (std::int32_t k : *ids) s += *prices.find(k);
            return static_cast<std::uint64_t>(s);
        };
        auto fb_ids = [=] {
            const auto* prices = flatbuffers::GetRoot<fbm::Lookup>(fbp)->prices();
            double s = 0;
            for (std::int32_t k : *ids) s += prices->LookupByKey(k)->price();
            return static_cast<std::uint64_t>(s);
        };
        auto mp_ids = [=] {
            const msgpack::object& prices = at(*mpo, 1);
            double s = 0;
            for (std::int32_t k : *ids) {
                for (std::uint32_t j = 0; j < prices.via.map.size; ++j) {
                    const auto& kv = prices.via.map.ptr[j];
                    if (static_cast<std::int32_t>(static_cast<std::int64_t>(integer(kv.key))) == k) { s += kv.val.via.f64; break; }
                }
            }
            return static_cast<std::uint64_t>(s);
        };
        if (tessera_names() != fb_names() || tessera_names() != mp_names() || tessera_ids() != fb_ids() || tessera_ids() != mp_ids()) {
            std::fprintf(stderr, "LOOKUP MISMATCH\n");
            ok = false;
        }
        cases.push_back({w, "Tessera", "1000 lookups by string", tessera_names, {}});
        cases.push_back({w, "FlatBuffers", "1000 lookups by string", fb_names, {}});
        cases.push_back({w, "MessagePack", "1000 lookups by string (parsed tree, linear)", mp_names, {}});
        cases.push_back({w, "Tessera", "1000 lookups by int", tessera_ids, {}});
        cases.push_back({w, "FlatBuffers", "1000 lookups by int", fb_ids, {}});
        cases.push_back({w, "MessagePack", "1000 lookups by int (parsed tree, linear)", mp_ids, {}});
    }

    // Real data: canada.json, one GeoJSON polygon of 480 rings and 55,563 points. A random read takes a ring's middle
    // point (FlatBuffers has no vectors of vectors, so there each ring is a table holding a vector of points).
    add_workload(
        "canada", im::FeatureCollection{}, tessera_read::canada, [](const std::uint8_t* p) { return flatbuffers::GetRoot<fbm::FeatureCollection>(p); },
        [](flatbuffers::Verifier& v) { return v.VerifyBuffer<fbm::FeatureCollection>(nullptr); }, fb_read::canada, mp_read::canada,
        [](im::FeatureCollection x, std::uint32_t i) -> std::uint64_t {
            auto ring = x.features()[0].geometry().coordinates()[i];
            const im::Point& p = ring[ring.size() / 2];
            return ring.size() + std::bit_cast<std::uint64_t>(p.x) + std::bit_cast<std::uint64_t>(p.y);
        },
        [](const fbm::FeatureCollection* x, std::uint32_t i) -> std::uint64_t {
            auto points = x->features()->Get(0)->geometry()->coordinates()->Get(i)->points();
            const fbm::Point* p = points->Get(points->size() / 2);
            return points->size() + std::bit_cast<std::uint64_t>(p->x()) + std::bit_cast<std::uint64_t>(p->y());
        },
        [](const msgpack::object& x, std::uint32_t i) -> std::uint64_t {
            const auto& ring = at(at(at(at(at(x, 1), 0), 2), 1), i);
            const auto& p = at(ring, ring.via.array.size / 2);
            return ring.via.array.size + std::bit_cast<std::uint64_t>(at(p, 0).via.f64) + std::bit_cast<std::uint64_t>(at(p, 1).via.f64);
        },
        [](im::FeatureCollection x) { return x.features()[0].geometry().coordinates().size(); });

    // Fixed cells: the series again, with its always-set values marked [TesseraKeepDefault] (constant positions, no
    // presence bits), read like the Tessera rows above with the same picks.
    {
        const std::string w = "series";
        const Buffer& fx = buffers["series.tessera-fixed"];
        const std::uint8_t* fxp = fx.data();
        const std::size_t fxs = fx.size;
        const auto picks = picks_of[w];
        auto pick = [](im::FixedSeries s, std::uint32_t i) -> std::uint64_t {
            auto x = s.samples()[i];
            return static_cast<std::uint64_t>(x.value()) + static_cast<std::uint64_t>(x.timestamp());
        };
        tessera::Reader<im::FixedSeries> r(fxp, fxs);
        if (!r) {
            std::fprintf(stderr, "series.tessera-fixed: %s\n", tessera::to_string(r.error()));
            ok = false;
        } else {
            check("series.tessera-fixed", tessera_read::fixed_series(r.root()), expected[w]);
            std::uint64_t a = 0, b = 0;
            for (std::uint32_t i : *picks) {
                a += pick(r.root(), i);
                const auto x = tessera::root_unchecked<im::Series>(buffers["series.tessera"].data()).samples()[i];
                b += static_cast<std::uint64_t>(x.value()) + static_cast<std::uint64_t>(x.timestamp());
            }
            if (a != b) { std::fprintf(stderr, "RANDOM ACCESS MISMATCH series (fixed)\n"); ok = false; }
        }
        cases.push_back({w, "Tessera (fixed)", "verify", [=] { return static_cast<std::uint64_t>(tessera::verify<im::FixedSeries>(fxp, fxs)); }, {}});
        cases.push_back({w, "Tessera (fixed)", "full traversal", [=] { return tessera_read::fixed_series(tessera::root_unchecked<im::FixedSeries>(fxp)); }, {}});
        cases.push_back({w, "Tessera (fixed)", "verify + traversal", [=] {
            tessera::Reader<im::FixedSeries> rd(fxp, fxs);
            return tessera_read::fixed_series(rd.root());
        }, {}});
        cases.push_back({w, "Tessera (fixed)", "random access x1000", [=] {
            auto root = tessera::root_unchecked<im::FixedSeries>(fxp);
            std::uint64_t s = 0;
            for (std::uint32_t i : *picks) s += pick(root, i);
            return s;
        }, {}});
    }

    if (!ok) {
        std::fprintf(stderr, "correctness checks failed; not timing\n");
        return 1;
    }
    std::fprintf(stderr, "checksums: all readers agree with the C# objects\n");
    if (!filter.empty()) {
        std::erase_if(cases, [&](const Case& c) { return (c.workload + "/" + c.library + "/" + c.op).find(filter) == std::string::npos; });
    }

    pin_thread();
    for (auto& c : cases) {
        if (std::getenv("TESSERA_BENCH_TRACE")) std::fprintf(stderr, "warm %s %s %s\n", c.workload.c_str(), c.library.c_str(), c.op.c_str());
        auto n = calibrate(c, 100);
        for (std::uint64_t i = 0; i < n; ++i) g_sink = g_sink ^ c.fn();
    }
    std::vector<std::size_t> order(cases.size());
    std::iota(order.begin(), order.end(), 0);
    for (int r = 0; r < rounds; ++r) {
        std::fprintf(stderr, "round %d/%d\n", r + 1, rounds);
        std::shuffle(order.begin(), order.end(), rng);
        for (std::size_t i : order) {
            Case& c = cases[i];
            auto n = calibrate(c, round_ms);
            auto t0 = std::chrono::steady_clock::now();
            for (std::uint64_t k = 0; k < n; ++k) g_sink = g_sink ^ c.fn();
            double ns = std::chrono::duration<double, std::nano>(std::chrono::steady_clock::now() - t0).count() / static_cast<double>(n);
            c.ns.push_back(ns);
        }
    }

    std::printf("| Workload | Library | Operation | Median µs | ±MAD |\n|---|---|---|---:|---:|\n");
    for (auto& c : cases) std::printf("| %s | %s | %s | %.2f | %.1f%% |\n", c.workload.c_str(), c.library.c_str(), c.op.c_str(), median(c.ns) / 1000, mad_percent(c.ns));

    if (!json_path.empty()) {
        std::ofstream j(json_path);
        j << "{\n  \"compiler\": \"" << compiler() << "\",\n  \"rounds\": " << rounds << ",\n  \"round_ms\": " << round_ms << ",\n  \"read\": [\n";
        for (std::size_t i = 0; i < cases.size(); ++i) {
            auto& c = cases[i];
            j << "    {\"workload\": \"" << c.workload << "\", \"library\": \"" << c.library << "\", \"operation\": \"" << c.op
              << "\", \"median_us\": " << median(c.ns) / 1000 << ", \"mad_percent\": " << mad_percent(c.ns) << ", \"samples_us\": [";
            for (std::size_t k = 0; k < c.ns.size(); ++k) j << (k ? ", " : "") << c.ns[k] / 1000;
            j << "]}" << (i + 1 < cases.size() ? "," : "") << "\n";
        }
        j << "  ]\n}\n";
    }
    return 0;
}
