// Reads the buffer written by Program.cs. Build with CMake (see CMakeLists.txt) after `dotnet run` of the sample.
#include "game/Monster.tessera.hpp"

#include <cstdint>
#include <fstream>
#include <iostream>
#include <vector>

int main(int argc, char** argv) {
    // Load the file into 8-byte aligned memory.
    std::ifstream file(argc > 1 ? argv[1] : "monster.bin", std::ios::binary | std::ios::ate);
    if (!file) {
        std::cerr << "cannot open the buffer\n";
        return 1;
    }
    const auto size = static_cast<std::size_t>(file.tellg());
    std::vector<std::uint64_t> data((size + 7) / 8);
    file.seekg(0);
    file.read(reinterpret_cast<char*>(data.data()), static_cast<std::streamsize>(size));

    tessera::Reader<game::Monster> reader(data.data(), size);  // verifies the buffer once
    if (!reader) {
        std::cerr << "invalid buffer: " << tessera::to_string(reader.error()) << '\n';
        return 1;
    }

    game::Monster orc = reader.root();  // a view: reads in place, nothing is parsed or copied
    const game::Vec3& p = orc.position();
    std::cout << orc.name() << ": hp " << orc.hp() << ", mana " << orc.mana().value_or(0)
              << ", position " << p.x << ' ' << p.y << ' ' << p.z << '\n';
    for (game::Weapon w : orc.weapons()) std::cout << "  " << w.name() << ": damage " << w.damage() << '\n';
    return 0;
}
