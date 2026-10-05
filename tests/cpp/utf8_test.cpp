// UTF-8 validation: the verifier's validator against a byte-at-a-time reference, on every short sequence of
// interesting bytes at every offset from an 8-byte boundary, and on random mixes of ASCII runs and multi-byte
// characters (valid and corrupted).
#include <tessera/tessera.hpp>

#include <cstdint>
#include <cstdio>
#include <vector>

namespace {

bool reference(const std::uint8_t* p, std::uint32_t n) {
    std::uint32_t i = 0;
    while (i < n) {
        const std::uint8_t c = p[i];
        if (c < 0x80) { ++i; continue; }
        std::uint32_t len, cp;
        if ((c & 0xE0) == 0xC0) { len = 2; cp = c & 0x1Fu; }
        else if ((c & 0xF0) == 0xE0) { len = 3; cp = c & 0x0Fu; }
        else if ((c & 0xF8) == 0xF0) { len = 4; cp = c & 0x07u; }
        else return false;
        if (n - i < len) return false;
        for (std::uint32_t k = 1; k < len; ++k) {
            if ((p[i + k] & 0xC0) != 0x80) return false;
            cp = (cp << 6) | (p[i + k] & 0x3Fu);
        }
        if ((len == 2 && cp < 0x80) || (len == 3 && cp < 0x800) || (len == 4 && (cp < 0x10000 || cp > 0x10FFFF)) || (cp >= 0xD800 && cp <= 0xDFFF)) return false;
        i += len;
    }
    return true;
}

struct Rng {
    std::uint64_t s;
    std::uint64_t next() {
        std::uint64_t z = (s += 0x9E3779B97F4A7C15ull);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
        return z ^ (z >> 31);
    }
    std::uint32_t below(std::uint32_t n) { return static_cast<std::uint32_t>(next() % n); }
};

int g_failures = 0, g_checked = 0;

void check(const std::vector<std::uint8_t>& s) {
    // Exact-size heap copy, so a sanitizer build catches any read past the end.
    std::vector<std::uint8_t> copy(s);
    const auto n = static_cast<std::uint32_t>(copy.size());
    const bool want = reference(copy.data(), n);
    const bool got = tessera::detail::Verifier::valid_utf8(copy.data(), n);
    ++g_checked;
    if (want != got && ++g_failures <= 10) {
        std::fprintf(stderr, "mismatch (want %d):", want);
        for (std::uint8_t b : s) std::fprintf(stderr, " %02X", b);
        std::fprintf(stderr, "\n");
    }
}

void append_char(std::vector<std::uint8_t>& s, std::uint32_t cp) {
    if (cp < 0x80) s.push_back(static_cast<std::uint8_t>(cp));
    else if (cp < 0x800) { s.push_back(static_cast<std::uint8_t>(0xC0 | (cp >> 6))); s.push_back(static_cast<std::uint8_t>(0x80 | (cp & 0x3F))); }
    else if (cp < 0x10000) {
        s.push_back(static_cast<std::uint8_t>(0xE0 | (cp >> 12)));
        s.push_back(static_cast<std::uint8_t>(0x80 | ((cp >> 6) & 0x3F)));
        s.push_back(static_cast<std::uint8_t>(0x80 | (cp & 0x3F)));
    } else {
        s.push_back(static_cast<std::uint8_t>(0xF0 | (cp >> 18)));
        s.push_back(static_cast<std::uint8_t>(0x80 | ((cp >> 12) & 0x3F)));
        s.push_back(static_cast<std::uint8_t>(0x80 | ((cp >> 6) & 0x3F)));
        s.push_back(static_cast<std::uint8_t>(0x80 | (cp & 0x3F)));
    }
}

}  // namespace

int main() {
    static const std::uint8_t bytes[] = {0x00, 0x41, 0x7F, 0x80, 0x8F, 0x90, 0x9F, 0xA0, 0xBF, 0xC0, 0xC1, 0xC2, 0xDF,
                                         0xE0, 0xE1, 0xEC, 0xED, 0xEE, 0xEF, 0xF0, 0xF1, 0xF4, 0xF5, 0xF8, 0xFF};
    constexpr int kBytes = static_cast<int>(sizeof(bytes));
    // Every sequence of up to 4 interesting bytes, after 0 to 9 ASCII bytes and before 0 to 9 more.
    for (int len = 1; len <= 4; ++len) {
        int total = 1;
        for (int k = 0; k < len; ++k) total *= kBytes;
        for (int code = 0; code < total; ++code) {
            std::vector<std::uint8_t> seq;
            for (int k = 0, c = code; k < len; ++k, c /= kBytes) seq.push_back(bytes[c % kBytes]);
            // Fewer positions for the 4-byte sequences (390,625 of them), so sanitizer builds stay quick.
            static const std::vector<int> all = {0, 1, 7, 8, 9}, few = {0, 7};
            for (int before : len < 4 ? all : few) {
                for (int after : len < 4 ? all : few) {
                    std::vector<std::uint8_t> s(static_cast<std::size_t>(before), 'a');
                    s.insert(s.end(), seq.begin(), seq.end());
                    s.insert(s.end(), static_cast<std::size_t>(after), 'z');
                    check(s);
                }
            }
        }
    }
    // Random text: ASCII runs of every length mixed with characters of every width, then a few corrupted bytes.
    Rng rng{20261005};
    static const std::uint32_t chars[] = {0x7F, 0x80, 0xE9, 0x7FF, 0x800, 0x4E2D, 0xD7FF, 0xE000, 0xFFFD, 0xFFFF, 0x10000, 0x1F600, 0x10FFFF};
    for (int round = 0; round < 200000; ++round) {
        std::vector<std::uint8_t> s;
        const std::uint32_t parts = 1 + rng.below(6);
        for (std::uint32_t k = 0; k < parts; ++k) {
            const std::uint32_t run = rng.below(4) == 0 ? rng.below(40) : rng.below(10);
            for (std::uint32_t r = 0; r < run; ++r) s.push_back(static_cast<std::uint8_t>(0x20 + rng.below(0x5F)));
            if (rng.below(3) != 0) append_char(s, chars[rng.below(static_cast<std::uint32_t>(sizeof(chars) / sizeof(chars[0])))]);
        }
        if (!s.empty() && rng.below(2) == 0) {
            const std::uint32_t edits = 1 + rng.below(3);
            for (std::uint32_t e = 0; e < edits; ++e) s[rng.below(static_cast<std::uint32_t>(s.size()))] = static_cast<std::uint8_t>(rng.next());
        }
        check(s);
    }
    if (g_failures != 0) {
        std::fprintf(stderr, "utf8: %d of %d strings judged differently\n", g_failures, g_checked);
        return 1;
    }
    std::printf("utf8: %d strings, same as the reference\n", g_checked);
    return 0;
}
