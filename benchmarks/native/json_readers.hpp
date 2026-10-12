// JSON readers (simdjson On-Demand) for the JSON that System.Text.Json and Newtonsoft.Json write: public fields in
// declaration order, null members left out, and a union member's "$type" first. They compute the same checksum as the
// readers in readers.hpp (benchmarks/Tessera.Benchmarks/Checksum.cs), streaming: no tree is built.
#pragma once

#include "readers.hpp"
#include "simdjson.h"

#include <bit>
#include <charconv>
#include <cstdint>
#include <span>
#include <string_view>

namespace json_read {

namespace od = simdjson::ondemand;
using names = std::span<const std::string_view>;

inline float f32(od::value v) { return static_cast<float>(double(v.get_double())); }
inline double f64(od::value v) { return double(v.get_double()); }
inline std::int64_t i64(od::value v) { return std::int64_t(v.get_int64()); }
inline std::uint64_t u64(od::value v) { return std::uint64_t(v.get_uint64()); }
inline bool boolean(od::value v) { return bool(v.get_bool()); }
inline std::string_view str(od::value v) { return std::string_view(v.get_string()); }

// An object's members in the writer's order; a member that is missing (it was null) is absent. on(i, value) reads
// member i. Member names need no escapes, so the raw key is compared.
template <class On> inline void members(Checksum& c, od::object o, names expected, On&& on) {
    std::size_t next = 0;
    for (od::field f : o) {
        const std::string_view key = f.escaped_key();
        std::size_t i = next;
        while (i < expected.size() && expected[i] != key) ++i;
        if (i == expected.size()) continue;
        for (; next < i; ++next) c.absent();
        on(i, f.value());
        next = i + 1;
    }
    for (; next < expected.size(); ++next) c.absent();
}

// The length is counted while reading (JSON does not store it); the checksum sums lengths apart from the chain.
template <class Each> inline void list(Checksum& c, od::value v, Each&& each) {
    std::uint64_t n = 0;
    for (od::value x : v.get_array()) {
        each(x);
        ++n;
    }
    c.n(n);
}

// Vec2/Vec3/Vec4 (floats) and Point (doubles): every member is present, in order.
inline void vec(Checksum& c, od::value v) {
    for (od::field f : v.get_object()) c.f(f32(f.value()));
}

inline void ref(Checksum& c, od::value v) {
    int k = 0;
    for (od::field f : v.get_object()) {
        if (k++ == 0) c.u(u64(f.value()));
        else c.i(i64(f.value()));
    }
}

// byte[] is base64.
inline int b64(char ch) {
    if (ch >= 'A' && ch <= 'Z') return ch - 'A';
    if (ch >= 'a' && ch <= 'z') return ch - 'a' + 26;
    if (ch >= '0' && ch <= '9') return ch - '0' + 52;
    return ch == '+' ? 62 : ch == '/' ? 63 : -1;
}

inline void bytes(Checksum& c, std::string_view s) {
    std::size_t n = s.size() / 4 * 3;
    if (!s.empty() && s.back() == '=') n -= s.size() >= 2 && s[s.size() - 2] == '=' ? 2 : 1;
    c.n(n);
    std::size_t out = 0;
    for (std::size_t i = 0; i + 3 < s.size() && out < n; i += 4) {
        const std::uint32_t q = (static_cast<std::uint32_t>(b64(s[i])) << 18) | (static_cast<std::uint32_t>(b64(s[i + 1])) << 12) |
                                (static_cast<std::uint32_t>(s[i + 2] == '=' ? 0 : b64(s[i + 2])) << 6) | static_cast<std::uint32_t>(s[i + 3] == '=' ? 0 : b64(s[i + 3]));
        for (int k = 2; k >= 0 && out < n; --k, ++out) c.u((q >> (8 * k)) & 0xFF);
    }
}

// ---------------------------------------------------------------------------------------------------- prefab

constexpr std::string_view kImage[] = {"Sprite", "Material", "Color", "UvRect", "FillMethod", "FillAmount", "Enable", "RaycastTarget", "PreserveAspect"};
constexpr std::string_view kText[] = {"Text", "Font", "Color", "FontSize", "LineSpacing", "Alignment", "ColorMode", "Enable", "RichText"};
constexpr std::string_view kButton[] = {"Target", "OnClick", "Interactable", "Transition"};
constexpr std::string_view kLayout[] = {"Padding", "Spacing", "ChildAlignment", "ControlWidth", "ControlHeight", "ExpandWidth", "ExpandHeight"};
constexpr std::string_view kController[] = {
    "Sets", "Empties", "Hides", "Images", "Icons", "Names", "Levels", "Skills", "Texts", "Buttons",
    "Icon", "Glow", "Level", "Star", "SkillList", "Badge", "Banner", "Counter", "Divider", "Header", "Background", "Group", "Caption", "Thumbnail", "Tooltip",
    "IsPinned", "ShowTitle", "IsFeatured", "Locked", "Enable"};

inline void component_member(Checksum& c, int kind, std::size_t i, od::value v) {
    switch (kind) {
        case 0:  // Image
            if (i < 2) c.s(str(v));
            else if (i < 4) vec(c, v);
            else if (i == 4) c.i(i64(v));
            else if (i == 5) c.f(f32(v));
            else c.b(boolean(v));
            break;
        case 1:  // Text
            if (i < 2) c.s(str(v));
            else if (i == 2) vec(c, v);
            else if (i < 5) c.f(f32(v));
            else if (i == 5) c.i(i64(v));
            else if (i == 6) c.u(u64(v));
            else c.b(boolean(v));
            break;
        case 2:  // Button
            if (i < 2) ref(c, v);
            else if (i == 2) c.b(boolean(v));
            else c.u(u64(v));
            break;
        case 3:  // Layout
            if (i == 0) vec(c, v);
            else if (i == 1) c.f(f32(v));
            else if (i == 2) c.i(i64(v));
            else c.b(boolean(v));
            break;
        default:  // Controller
            if (i < 10) list(c, v, [&](od::value x) { ref(c, x); });
            else if (i < 25) ref(c, v);
            else c.b(boolean(v));
            break;
    }
}

// The union's member: "$type" first, then the members of that type.
inline void component(Checksum& c, od::value v) {
    int kind = -1;
    names expected;
    std::size_t next = 0;
    for (od::field f : v.get_object()) {
        if (kind < 0) {
            const std::string_view t = str(f.value());
            kind = t == "Image" ? 0 : t == "Text" ? 1 : t == "Button" ? 2 : t == "Layout" ? 3 : 4;
            expected = kind == 0 ? names(kImage) : kind == 1 ? names(kText) : kind == 2 ? names(kButton) : kind == 3 ? names(kLayout) : names(kController);
            c.u(static_cast<std::uint64_t>(kind));
            continue;
        }
        const std::string_view key = f.escaped_key();
        std::size_t i = next;
        while (i < expected.size() && expected[i] != key) ++i;
        if (i == expected.size()) continue;
        for (; next < i; ++next) c.absent();
        component_member(c, kind, i, f.value());
        next = i + 1;
    }
    for (; next < expected.size(); ++next) c.absent();
}

constexpr std::string_view kPrefabObject[] = {"Name", "Components", "Children", "Rotation", "Position", "Scale", "Pivot", "AnchorMin", "AnchorMax", "SizeDelta", "Id", "Active"};

inline std::uint64_t prefab(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Objects"};
    members(c, doc.get_object(), root, [&](std::size_t, od::value objects) {
        list(c, objects, [&](od::value o) {
            members(c, o.get_object(), kPrefabObject, [&](std::size_t i, od::value v) {
                if (i == 0) c.s(str(v));
                else if (i == 1) list(c, v, [&](od::value x) { component(c, x); });
                else if (i == 2) list(c, v, [&](od::value x) { c.i(i64(x)); });
                else if (i < 10) vec(c, v);
                else if (i == 10) c.i(i64(v));
                else c.b(boolean(v));
            });
        });
    });
    return c.value();
}

inline std::uint64_t prefab_pick(od::document& doc, std::uint32_t index) {
    od::value o = doc["Objects"].get_array().at(index);
    std::uint64_t name = 0, comps = 0, x = 0, id = 0;
    for (od::field f : o.get_object()) {
        const std::string_view key = f.escaped_key();
        if (key == "Name") name = str(f.value()).size();
        else if (key == "Components") { od::array a = f.value().get_array(); comps = std::size_t(a.count_elements()); }
        else if (key == "Position") {
            for (od::field p : f.value().get_object()) {
                x = static_cast<std::uint64_t>(f32(p.value()));
                break;
            }
        } else if (key == "Id") id = static_cast<std::uint64_t>(static_cast<std::int32_t>(i64(f.value())));
    }
    return name + x + comps + id;
}

// ---------------------------------------------------------------------------------------------------- monsters

constexpr std::string_view kWeapon[] = {"Name", "Damage"};
constexpr std::string_view kMonster[] = {"Name", "Hp", "Mana", "Pos", "Color", "Inventory", "Weapons", "Equipped", "Path", "Friendly"};

inline void weapon(Checksum& c, od::value v) {
    members(c, v.get_object(), kWeapon, [&](std::size_t i, od::value x) {
        if (i == 0) c.s(str(x));
        else c.i(i64(x));
    });
}

inline std::uint64_t world(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Monsters"};
    members(c, doc.get_object(), root, [&](std::size_t, od::value monsters) {
        list(c, monsters, [&](od::value m) {
            members(c, m.get_object(), kMonster, [&](std::size_t i, od::value v) {
                switch (i) {
                    case 0: c.s(str(v)); break;
                    case 1: case 2: c.i(i64(v)); break;
                    case 3: vec(c, v); break;
                    case 4: c.u(u64(v)); break;
                    case 5: bytes(c, str(v)); break;
                    case 6: list(c, v, [&](od::value x) { weapon(c, x); }); break;
                    case 7: weapon(c, v); break;
                    case 8: list(c, v, [&](od::value x) { vec(c, x); }); break;
                    default: c.b(boolean(v)); break;
                }
            });
        });
    });
    return c.value();
}

inline std::uint64_t world_pick(od::document& doc, std::uint32_t index) {
    od::value m = doc["Monsters"].get_array().at(index);
    std::uint64_t hp = 0, x = 0, weapons = 0, name = 0;
    for (od::field f : m.get_object()) {
        const std::string_view key = f.escaped_key();
        if (key == "Name") name = str(f.value()).size();
        else if (key == "Hp") hp = static_cast<std::uint64_t>(static_cast<std::int16_t>(i64(f.value())));
        else if (key == "Pos") {
            for (od::field p : f.value().get_object()) {
                x = static_cast<std::uint64_t>(f32(p.value()));
                break;
            }
        } else if (key == "Weapons") { od::array a = f.value().get_array(); weapons = std::size_t(a.count_elements()); }
    }
    return hp + x + weapons + name;
}

// ---------------------------------------------------------------------------------------------------- records

constexpr std::string_view kRecord[] = {
    "I0", "I1", "I2", "I3", "I4", "I5", "I6", "I7", "I8", "I9", "I10", "I11", "I12", "I13", "I14", "I15",
    "F0", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "D0", "D1", "D2", "D3",
    "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "S0", "S1", "S2", "S3"};

inline std::uint64_t records(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Records"};
    members(c, doc.get_object(), root, [&](std::size_t, od::value records) {
        list(c, records, [&](od::value r) {
            members(c, r.get_object(), kRecord, [&](std::size_t i, od::value v) {
                if (i < 16) c.i(i64(v));
                else if (i < 24) c.f(f32(v));
                else if (i < 28) c.d(f64(v));
                else if (i < 36) c.b(boolean(v));
                else c.s(str(v));
            });
        });
    });
    return c.value();
}

inline std::uint64_t records_pick(od::document& doc, std::uint32_t index) {
    od::value r = doc["Records"].get_array().at(index);
    std::int64_t i7 = 1;
    std::uint64_t s2 = 0;
    float f3 = 2.f;
    bool b5 = true;
    for (od::field f : r.get_object()) {
        const std::string_view key = f.escaped_key();
        if (key == "I7") i7 = static_cast<std::int32_t>(i64(f.value()));
        else if (key == "S2") s2 = str(f.value()).size();
        else if (key == "F3") f3 = f32(f.value());
        else if (key == "B5") b5 = boolean(f.value());
    }
    return static_cast<std::uint64_t>(static_cast<std::int32_t>(i7)) + s2 + static_cast<std::uint64_t>(f3) + b5;
}

// ---------------------------------------------------------------------------------------------------- series

constexpr std::string_view kSample[] = {"Timestamp", "Value", "Status", "Quality", "Note"};

inline std::uint64_t series(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Samples"};
    members(c, doc.get_object(), root, [&](std::size_t, od::value samples) {
        list(c, samples, [&](od::value x) {
            members(c, x.get_object(), kSample, [&](std::size_t i, od::value v) {
                if (i == 0 || i == 2) c.i(i64(v));
                else if (i == 1) c.d(f64(v));
                else if (i == 3) c.f(f32(v));
                else c.s(str(v));
            });
        });
    });
    return c.value();
}

inline std::uint64_t series_pick(od::document& doc, std::uint32_t index) {
    od::value x = doc["Samples"].get_array().at(index);
    std::uint64_t value = 0, timestamp = 0;
    for (od::field f : x.get_object()) {
        const std::string_view key = f.escaped_key();
        if (key == "Timestamp") timestamp = static_cast<std::uint64_t>(i64(f.value()));
        else if (key == "Value") value = static_cast<std::uint64_t>(f64(f.value()));
    }
    return value + timestamp;
}

// ---------------------------------------------------------------------------------------------------- scenes

constexpr std::string_view kNode[] = {"Id", "Name", "Active", "X", "Y", "Z", "Parent", "Children"};

inline std::uint64_t scene(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Nodes", "Name"};
    members(c, doc.get_object(), root, [&](std::size_t i, od::value v) {
        if (i == 1) {
            c.s(str(v));
            return;
        }
        list(c, v, [&](od::value n) {
            members(c, n.get_object(), kNode, [&](std::size_t k, od::value x) {
                if (k == 0 || k == 6) c.i(i64(x));
                else if (k == 1) c.s(str(x));
                else if (k == 2) c.b(boolean(x));
                else if (k < 6) c.f(f32(x));
                else list(c, x, [&](od::value ch) { c.i(i64(ch)); });
            });
        });
    });
    return c.value();
}

inline std::uint64_t scene_pick(od::document& doc, std::uint32_t index) {
    od::value n = doc["Nodes"].get_array().at(index);
    std::uint64_t id = 0, name = 0, children = 0, parent = 0;
    for (od::field f : n.get_object()) {
        const std::string_view key = f.escaped_key();
        if (key == "Id") id = static_cast<std::uint64_t>(static_cast<std::int32_t>(i64(f.value())));
        else if (key == "Name") name = str(f.value()).size();
        else if (key == "Parent") parent = static_cast<std::uint64_t>(static_cast<std::int32_t>(i64(f.value())));
        else if (key == "Children") { od::array a = f.value().get_array(); children = std::size_t(a.count_elements()); }
    }
    return id + name + children + parent;
}

// ---------------------------------------------------------------------------------------------------- lookup

// Dictionaries are objects: string keys as written, int keys as decimal strings, in the dictionary's order.
inline std::uint64_t lookup(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Items", "Prices"};
    members(c, doc.get_object(), root, [&](std::size_t i, od::value v) {
        std::uint64_t n = 0;
        for (od::field f : v.get_object()) {
            ++n;
            if (i == 0) {
                c.s(std::string_view(f.unescaped_key()));
                int k = 0;
                for (od::field s : f.value().get_object()) {
                    if (k++ == 0) c.i(i64(s.value()));
                    else c.f(f32(s.value()));
                }
            } else {
                const std::string_view key = f.escaped_key();
                std::int32_t id = 0;
                std::from_chars(key.data(), key.data() + key.size(), id);
                c.i(id);
                c.d(f64(f.value()));
            }
        }
        c.n(n);
    });
    return c.value();
}

inline std::uint64_t lookup_pick(od::document& doc, std::uint32_t index) {
    od::object items = doc["Items"].get_object();
    std::uint32_t k = 0;
    for (od::field f : items) {
        if (k++ != index) continue;
        const std::uint64_t size = f.escaped_key().size();
        for (od::field s : f.value().get_object()) return size + static_cast<std::uint64_t>(static_cast<std::int32_t>(i64(s.value())));
    }
    return 0;
}

// ---------------------------------------------------------------------------------------------------- canada

constexpr std::string_view kFeature[] = {"Type", "Properties", "Geometry"};
constexpr std::string_view kGeometry[] = {"Type", "Coordinates"};

inline std::uint64_t canada(od::document& doc) {
    Checksum c;
    constexpr std::string_view root[] = {"Type", "Features"};
    members(c, doc.get_object(), root, [&](std::size_t i, od::value v) {
        if (i == 0) {
            c.s(str(v));
            return;
        }
        list(c, v, [&](od::value feature) {
            members(c, feature.get_object(), kFeature, [&](std::size_t k, od::value x) {
                if (k == 0) c.s(str(x));
                else if (k == 1) {
                    std::uint64_t n = 0;
                    for (od::field p : x.get_object()) {
                        c.s(std::string_view(p.unescaped_key()));
                        c.s(str(p.value()));
                        ++n;
                    }
                    c.n(n);
                } else {
                    members(c, x.get_object(), kGeometry, [&](std::size_t g, od::value y) {
                        if (g == 0) c.s(str(y));
                        else list(c, y, [&](od::value ring) {
                            list(c, ring, [&](od::value point) {
                                for (od::field xy : point.get_object()) c.d(f64(xy.value()));
                            });
                        });
                    });
                }
            });
        });
    });
    return c.value();
}

inline std::uint64_t canada_pick(od::document& doc, std::uint32_t index) {
    od::array features = doc["Features"].get_array();
    for (od::value feature : features) {
        od::array ring = feature["Geometry"]["Coordinates"].get_array().at(index).get_array();
        const std::size_t size = ring.count_elements();
        od::value point = ring.at(size / 2);
        double xy[2] = {};
        int k = 0;
        for (od::field f : point.get_object()) xy[k++] = f64(f.value());
        return size + std::bit_cast<std::uint64_t>(xy[0]) + std::bit_cast<std::uint64_t>(xy[1]);
    }
    return 0;
}

}  // namespace json_read
