// Reads the buffer written by Program.cs. Build with CMake (see CMakeLists.txt) after `dotnet build` of the sample.
#include "Quickstart.tessera.hpp"

#include <cstdint>
#include <cstdio>
#include <fstream>
#include <vector>

int main(int argc, char** argv) {
    const char* path = argc > 1 ? argv[1] : "monster.bin";
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file) {
        std::fprintf(stderr, "cannot open %s\n", path);
        return 1;
    }
    const auto size = static_cast<std::size_t>(file.tellg());
    std::vector<std::uint64_t> storage((size + 7) / 8);  // 8-byte aligned
    file.seekg(0);
    file.read(reinterpret_cast<char*>(storage.data()), static_cast<std::streamsize>(size));

    tessera::Reader<game::Monster> reader(storage.data(), size);  // verifies the buffer
    if (!reader) {
        std::fprintf(stderr, "invalid buffer: %s\n", tessera::to_string(reader.error()));
        return 1;
    }

    game::Monster m = reader.root();
    std::printf("name: %.*s\n", static_cast<int>(m.name().size()), m.name().data());
    std::printf("hp: %d (stored: %s)\n", m.hp(), m.has_hp() ? "yes" : "no, default");
    if (auto mana = m.mana()) std::printf("mana: %d\n", *mana);
    const game::Vec3& p = m.position();
    std::printf("position: %g %g %g\n", p.x, p.y, p.z);
    std::printf("faction: %d\n", static_cast<int>(m.faction()));
    for (game::Weapon w : m.weapons()) std::printf("weapon: %.*s (%d)\n", static_cast<int>(w.name().size()), w.name().data(), w.damage());
    if (game::Potion potion = m.loot().as_potion()) std::printf("loot: potion healing %d\n", potion.heal());
    if (game::Key key = m.loot().as_key()) std::printf("loot: key for door %u\n", key.door());
    std::printf("json: %s\n", tessera::to_json(m).c_str());
    return 0;
}
