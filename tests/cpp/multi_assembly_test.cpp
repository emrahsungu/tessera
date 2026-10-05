// Two model assemblies. tests/Tessera.Tests uses types of tests/Tessera.Tests.Shared, so its headers (generated/) define
// those types too; generated-shared/ holds the shared assembly's own headers. Including both must work: identical
// type headers merge through their guards. multi_assembly_other.cpp includes only the shared headers, so the views
// also cross translation units. The buffer relies on defaults declared by initializers in the shared assembly.
#include "Tessera.Tests.tessera.hpp"
#include "Tessera.Tests.Shared.tessera.hpp"

#include <cstdio>
#include <fstream>
#include <string>
#include <vector>

std::int16_t badge_level(sharedmodels::Badge badge);  // multi_assembly_other.cpp

static int g_failures = 0;
#define CHECK(cond)                                                                        \
    do {                                                                                   \
        if (!(cond)) {                                                                     \
            std::fprintf(stderr, "%s:%d: CHECK failed: %s\n", __FILE__, __LINE__, #cond); \
            ++g_failures;                                                                  \
        }                                                                                  \
    } while (0)

int main(int argc, char** argv) {
    if (argc < 2) return 2;
    std::ifstream f(std::string(argv[1]) + "/profile.bin", std::ios::binary | std::ios::ate);
    if (!f) {
        std::fprintf(stderr, "cannot open profile.bin\n");
        return 1;
    }
    const auto size = static_cast<std::size_t>(f.tellg());
    std::vector<std::uint64_t> storage((size + 7) / 8);
    f.seekg(0);
    f.read(reinterpret_cast<char*>(storage.data()), static_cast<std::streamsize>(size));

    tessera::Reader<testmodels::behavior::Profile> reader(storage.data(), size);
    CHECK(reader);
    auto p = reader.root();
    CHECK(p.name() == "Ann");
    auto badge = p.badge();
    CHECK(badge.title() == "Champion");
    CHECK(!badge.has_level() && badge.level() == 5);
    CHECK(!badge.has_rarity() && badge.rarity() == sharedmodels::Rarity::Rare);
    CHECK(badge_level(badge) == 5);
    CHECK(p.badges().size() == 1);
    CHECK(p.badges()[0].level() == 9 && p.badges()[0].rarity() == sharedmodels::Rarity::Epic);
    CHECK(p.badges()[0].where().x == 1 && p.badges()[0].where().y == 2);
    CHECK(badge_level(p.badges()[0]) == 9);
    auto gold = p.reward().as_gold();
    CHECK(gold && !gold.has_amount() && gold.amount() == 10);
    CHECK(!p.reward().as_gem());

    if (g_failures == 0) std::printf("multi-assembly: all checks passed\n");
    return g_failures == 0 ? 0 : 1;
}
