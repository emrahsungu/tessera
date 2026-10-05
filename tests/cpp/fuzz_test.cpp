// Mutation fuzzing of the verifier: every buffer that verifies must be readable end to end without touching memory
// outside the buffer. Build with TESSERA_ASAN=ON so out-of-bounds reads abort.
#include "Tessera.Tests.tessera.hpp"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <memory>
#include <string>
#include <vector>

namespace {

std::vector<std::uint8_t> read_file(const std::string& path) {
    std::ifstream f(path, std::ios::binary | std::ios::ate);
    if (!f) return {};
    std::vector<std::uint8_t> v(static_cast<std::size_t>(f.tellg()));
    f.seekg(0);
    f.read(reinterpret_cast<char*>(v.data()), static_cast<std::streamsize>(v.size()));
    return v;
}

struct Rng {
    std::uint64_t s;
    std::uint64_t next() {
        std::uint64_t z = (s += 0x9E3779B97F4A7C15ull);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
        return z ^ (z >> 31);
    }
    std::uint32_t below(std::uint32_t n) { return n ? static_cast<std::uint32_t>(next() % n) : 0; }
};

std::size_t g_accepted = 0, g_rejected = 0;

template <class Root> void try_read(const std::uint8_t* data, std::size_t size) {
    tessera::Reader<Root> r(data, size);
    if (!r) {
        ++g_rejected;
        return;
    }
    ++g_accepted;
    volatile std::size_t sink = tessera::to_json(r.root()).size();
    (void)sink;
}

void try_all(const std::vector<std::uint8_t>& bytes) {
    // Exact-size heap copy so AddressSanitizer catches any read past the end.
    std::unique_ptr<std::uint8_t[]> copy(new std::uint8_t[bytes.size() == 0 ? 1 : bytes.size()]);
    if (!bytes.empty()) std::memcpy(copy.get(), bytes.data(), bytes.size());
    const std::uint8_t* d = copy.get();
    const std::size_t n = bytes.size();
    try_read<testmodels::Monster>(d, n);
    try_read<testmodels::Node>(d, n);
    try_read<testmodels::Wide>(d, n);
    try_read<testmodels::Empty>(d, n);
    try_read<testmodels::v1::Player>(d, n);
    try_read<testmodels::v2::Player>(d, n);
    try_read<testmodels::Swarm>(d, n);
    try_read<testmodels::Catalog>(d, n);
    try_read<testmodels::Readings>(d, n);
}

void mutate(std::vector<std::uint8_t>& b, Rng& rng) {
    const std::uint32_t edits = 1 + rng.below(4);
    for (std::uint32_t e = 0; e < edits && !b.empty(); ++e) {
        switch (rng.below(4)) {
            case 0:  // flip a bit
                b[rng.below(static_cast<std::uint32_t>(b.size()))] ^= static_cast<std::uint8_t>(1u << rng.below(8));
                break;
            case 1:  // random byte
                b[rng.below(static_cast<std::uint32_t>(b.size()))] = static_cast<std::uint8_t>(rng.next());
                break;
            default: {  // interesting 32-bit value at an aligned position
                if (b.size() < 4) break;
                const std::uint32_t pos = rng.below(static_cast<std::uint32_t>(b.size() / 4)) * 4;
                static const std::uint32_t values[] = {0u, 1u, 2u, 3u, 4u, 8u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu, 0xFFFFu, 0x10000u};
                std::uint32_t v = rng.below(2) ? values[rng.below(11)] : static_cast<std::uint32_t>(b.size() - pos + rng.below(16) - 8);
                std::memcpy(b.data() + pos, &v, 4);
                break;
            }
        }
    }
}

}  // namespace

int main(int argc, char** argv) {
    const std::string dir = argc > 1 ? argv[1] : "generated";
    const std::uint32_t iterations = argc > 2 ? static_cast<std::uint32_t>(std::strtoul(argv[2], nullptr, 10)) : 20000;
    Rng rng{argc > 3 ? std::strtoull(argv[3], nullptr, 10) : 12345};
    const char* names[] = {"monster_full", "monster_full_noshare", "monster_full_shareall", "monster_full_noschema", "monster_sparse", "monster_defaults",
                           "tree", "empty", "player_v1", "player_v1_noschema", "player_v2", "wide_dense", "wide_sparse", "dag_small", "dag_twice", "swarm", "catalog", "readings"};
    for (const char* name : names) {
        const auto original = read_file(dir + "/" + name + ".bin");
        if (original.empty()) {
            std::fprintf(stderr, "missing %s.bin\n", name);
            return 1;
        }
        for (std::size_t len = 0; len <= original.size(); ++len) try_all(std::vector<std::uint8_t>(original.begin(), original.begin() + static_cast<std::ptrdiff_t>(len)));
        for (std::uint32_t i = 0; i < iterations; ++i) {
            auto b = original;
            mutate(b, rng);
            try_all(b);
        }
    }
    std::printf("fuzz: %zu opened, %zu rejected, no invalid reads\n", g_accepted, g_rejected);
    return 0;
}
