// Protocol Buffers readers (libprotobuf) for the messages that Google.Protobuf writes from the benchmark objects
// (benchmarks/schemas/bench.proto). They walk the parsed messages and compute the same checksum as the readers in
// readers.hpp (benchmarks/Tessera.Benchmarks/Checksum.cs).
#pragma once

#include "bench.pb.h"  // protoc
#include "readers.hpp"

#include <bit>
#include <cstdint>
#include <string>
#include <type_traits>

namespace pbm = bench::pb;

namespace pb_read {

// Optional strings: absent unless set (a present empty string is set).
BENCH_HELPER void str(Checksum& c, bool has, const std::string& s) {
    if (has) c.s(s);
    else c.absent();
}

BENCH_HELPER void v2(Checksum& c, const pbm::Vec2& v) { c.f(v.x()); c.f(v.y()); }
BENCH_HELPER void v3(Checksum& c, const pbm::Vec3& v) { c.f(v.x()); c.f(v.y()); c.f(v.z()); }
BENCH_HELPER void v4(Checksum& c, const pbm::Vec4& v) { c.f(v.x()); c.f(v.y()); c.f(v.z()); c.f(v.w()); }
BENCH_HELPER void ref(Checksum& c, const pbm::ObjectRef& r) { c.u(r.component_name()); c.i(r.index()); c.i(r.object_ref_id()); }

BENCH_HELPER void refs(Checksum& c, bool has, const pbm::ObjectRefList& v) {
    if (!has) { c.absent(); return; }
    c.n(static_cast<std::uint64_t>(v.items_size()));
    for (const pbm::ObjectRef& r : v.items()) ref(c, r);
}

BENCH_HELPER void opt_ref(Checksum& c, bool has, const pbm::ObjectRef& r) {
    if (has) ref(c, r);
    else c.absent();
}

BENCH_HELPER void opt_bool(Checksum& c, bool has, bool b) {
    if (has) c.b(b);
    else c.absent();
}

BENCH_HELPER void ints(Checksum& c, bool has, const pbm::Int32List& v) {
    if (!has) { c.absent(); return; }
    c.n(static_cast<std::uint64_t>(v.items_size()));
    for (std::int32_t x : v.items()) c.i(x);
}

inline void component(Checksum& c, const pbm::Component& comp) {
    switch (comp.kind_case()) {
        case pbm::Component::kImage: {
            const auto& x = comp.image();
            c.u(0);
            str(c, x.has_sprite(), x.sprite());
            str(c, x.has_material(), x.material());
            v4(c, x.color());
            v4(c, x.uv_rect());
            c.i(x.fill_method());
            c.f(x.fill_amount());
            c.b(x.enable());
            c.b(x.raycast_target());
            c.b(x.preserve_aspect());
            break;
        }
        case pbm::Component::kText: {
            const auto& x = comp.text();
            c.u(1);
            str(c, x.has_text(), x.text());
            str(c, x.has_font(), x.font());
            v4(c, x.color());
            c.f(x.font_size());
            c.f(x.line_spacing());
            c.i(x.alignment());
            c.u(x.color_mode());
            c.b(x.enable());
            c.b(x.rich_text());
            break;
        }
        case pbm::Component::kButton: {
            const auto& x = comp.button();
            c.u(2);
            ref(c, x.target());
            ref(c, x.on_click());
            c.b(x.interactable());
            c.u(x.transition());
            break;
        }
        case pbm::Component::kLayout: {
            const auto& x = comp.layout();
            c.u(3);
            v4(c, x.padding());
            c.f(x.spacing());
            c.i(x.child_alignment());
            c.b(x.control_width());
            c.b(x.control_height());
            c.b(x.expand_width());
            c.b(x.expand_height());
            break;
        }
        case pbm::Component::kController: {
            const auto& x = comp.controller();
            c.u(4);
            refs(c, x.has_sets(), x.sets()); refs(c, x.has_empties(), x.empties()); refs(c, x.has_hides(), x.hides());
            refs(c, x.has_images(), x.images()); refs(c, x.has_icons(), x.icons()); refs(c, x.has_names(), x.names());
            refs(c, x.has_levels(), x.levels()); refs(c, x.has_skills(), x.skills()); refs(c, x.has_texts(), x.texts());
            refs(c, x.has_buttons(), x.buttons());
            opt_ref(c, x.has_icon(), x.icon()); opt_ref(c, x.has_glow(), x.glow()); opt_ref(c, x.has_level(), x.level());
            opt_ref(c, x.has_star(), x.star()); opt_ref(c, x.has_skill_list(), x.skill_list()); opt_ref(c, x.has_badge(), x.badge());
            opt_ref(c, x.has_banner(), x.banner()); opt_ref(c, x.has_counter(), x.counter()); opt_ref(c, x.has_divider(), x.divider());
            opt_ref(c, x.has_header(), x.header()); opt_ref(c, x.has_background(), x.background()); opt_ref(c, x.has_group(), x.group());
            opt_ref(c, x.has_caption(), x.caption()); opt_ref(c, x.has_thumbnail(), x.thumbnail()); opt_ref(c, x.has_tooltip(), x.tooltip());
            opt_bool(c, x.has_is_pinned(), x.is_pinned()); opt_bool(c, x.has_show_title(), x.show_title());
            opt_bool(c, x.has_is_featured(), x.is_featured()); opt_bool(c, x.has_locked(), x.locked()); opt_bool(c, x.has_enable(), x.enable());
            break;
        }
        default:
            c.absent();
    }
}

inline std::uint64_t prefab(const pbm::Prefab& p) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(p.objects_size()));
    for (const pbm::PrefabObject& o : p.objects()) {
        str(c, o.has_name(), o.name());
        if (!o.has_components()) c.absent();
        else {
            c.n(static_cast<std::uint64_t>(o.components().items_size()));
            for (const pbm::Component& comp : o.components().items()) component(c, comp);
        }
        ints(c, o.has_children(), o.children());
        v4(c, o.rotation());
        v3(c, o.position());
        v3(c, o.scale());
        v2(c, o.pivot());
        v2(c, o.anchor_min());
        v2(c, o.anchor_max());
        v2(c, o.size_delta());
        c.i(o.id());
        c.b(o.active());
    }
    return c.value();
}

inline std::uint64_t prefab_pick(const pbm::Prefab& p, std::uint32_t i) {
    const pbm::PrefabObject& o = p.objects(static_cast<int>(i));
    return (o.has_name() ? o.name().size() : 0) + static_cast<std::uint64_t>(o.position().x()) +
           (o.has_components() ? static_cast<std::uint64_t>(o.components().items_size()) : 0) + static_cast<std::uint64_t>(o.id());
}

inline void weapon(Checksum& c, const pbm::Weapon& w) {
    str(c, w.has_name(), w.name());
    c.i(w.damage());
}

inline std::uint64_t world(const pbm::World& w) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(w.monsters_size()));
    for (const pbm::Monster& m : w.monsters()) {
        str(c, m.has_name(), m.name());
        c.i(m.hp());
        if (m.has_mana()) c.i(m.mana());
        else c.absent();
        v3(c, m.pos());
        c.u(static_cast<std::uint64_t>(m.color()));
        if (!m.has_inventory()) c.absent();
        else {
            c.n(m.inventory().size());
            for (unsigned char x : m.inventory()) c.u(x);
        }
        if (!m.has_weapons()) c.absent();
        else {
            c.n(static_cast<std::uint64_t>(m.weapons().items_size()));
            for (const pbm::Weapon& x : m.weapons().items()) weapon(c, x);
        }
        if (m.has_equipped()) weapon(c, m.equipped());
        else c.absent();
        if (!m.has_path()) c.absent();
        else {
            c.n(static_cast<std::uint64_t>(m.path().items_size()));
            for (const pbm::Vec3& v : m.path().items()) v3(c, v);
        }
        c.b(m.friendly());
    }
    return c.value();
}

inline std::uint64_t world_pick(const pbm::World& w, std::uint32_t i) {
    const pbm::Monster& m = w.monsters(static_cast<int>(i));
    return static_cast<std::uint64_t>(m.hp()) + static_cast<std::uint64_t>(m.pos().x()) +
           (m.has_weapons() ? static_cast<std::uint64_t>(m.weapons().items_size()) : 0) + (m.has_name() ? m.name().size() : 0);
}

template <class T> BENCH_HELPER void opt(Checksum& c, bool has, T v) {
    if (!has) c.absent();
    else if constexpr (std::is_same_v<T, float>) c.f(v);
    else if constexpr (std::is_same_v<T, double>) c.d(v);
    else if constexpr (std::is_same_v<T, bool>) c.b(v);
    else c.i(v);
}

inline std::uint64_t records(const pbm::RecordSet& s) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(s.records_size()));
    for (const pbm::Record& x : s.records()) {
        opt(c, x.has_i0(), x.i0()); opt(c, x.has_i1(), x.i1()); opt(c, x.has_i2(), x.i2()); opt(c, x.has_i3(), x.i3());
        opt(c, x.has_i4(), x.i4()); opt(c, x.has_i5(), x.i5()); opt(c, x.has_i6(), x.i6()); opt(c, x.has_i7(), x.i7());
        opt(c, x.has_i8(), x.i8()); opt(c, x.has_i9(), x.i9()); opt(c, x.has_i10(), x.i10()); opt(c, x.has_i11(), x.i11());
        opt(c, x.has_i12(), x.i12()); opt(c, x.has_i13(), x.i13()); opt(c, x.has_i14(), x.i14()); opt(c, x.has_i15(), x.i15());
        opt(c, x.has_f0(), x.f0()); opt(c, x.has_f1(), x.f1()); opt(c, x.has_f2(), x.f2()); opt(c, x.has_f3(), x.f3());
        opt(c, x.has_f4(), x.f4()); opt(c, x.has_f5(), x.f5()); opt(c, x.has_f6(), x.f6()); opt(c, x.has_f7(), x.f7());
        opt(c, x.has_d0(), x.d0()); opt(c, x.has_d1(), x.d1()); opt(c, x.has_d2(), x.d2()); opt(c, x.has_d3(), x.d3());
        opt(c, x.has_b0(), x.b0()); opt(c, x.has_b1(), x.b1()); opt(c, x.has_b2(), x.b2()); opt(c, x.has_b3(), x.b3());
        opt(c, x.has_b4(), x.b4()); opt(c, x.has_b5(), x.b5()); opt(c, x.has_b6(), x.b6()); opt(c, x.has_b7(), x.b7());
        str(c, x.has_s0(), x.s0()); str(c, x.has_s1(), x.s1()); str(c, x.has_s2(), x.s2()); str(c, x.has_s3(), x.s3());
    }
    return c.value();
}

inline std::uint64_t records_pick(const pbm::RecordSet& s, std::uint32_t i) {
    const pbm::Record& r = s.records(static_cast<int>(i));
    return static_cast<std::uint64_t>(r.has_i7() ? r.i7() : 1) + (r.has_s2() ? r.s2().size() : 0) +
           static_cast<std::uint64_t>(r.has_f3() ? r.f3() : 2.f) + (r.has_b5() ? r.b5() : true);
}

inline std::uint64_t series(const pbm::Series& s) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(s.samples_size()));
    for (const pbm::Sample& x : s.samples()) {
        c.i(x.timestamp());
        c.d(x.value());
        c.i(x.status());
        c.f(x.quality());
        str(c, x.has_note(), x.note());
    }
    return c.value();
}

inline std::uint64_t series_pick(const pbm::Series& s, std::uint32_t i) {
    const pbm::Sample& x = s.samples(static_cast<int>(i));
    return static_cast<std::uint64_t>(x.value()) + static_cast<std::uint64_t>(x.timestamp());
}

inline std::uint64_t scene(const pbm::Scene& s) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(s.nodes_size()));
    for (const pbm::Node& n : s.nodes()) {
        c.i(n.id());
        str(c, n.has_name(), n.name());
        opt_bool(c, n.has_active(), n.active());
        c.f(n.x());
        c.f(n.y());
        c.f(n.z());
        if (n.has_parent()) c.i(n.parent());
        else c.absent();
        ints(c, n.has_children(), n.children());
    }
    str(c, s.has_name(), s.name());
    return c.value();
}

inline std::uint64_t scene_pick(const pbm::Scene& s, std::uint32_t i) {
    const pbm::Node& n = s.nodes(static_cast<int>(i));
    return static_cast<std::uint64_t>(n.id()) + (n.has_name() ? n.name().size() : 0) +
           (n.has_children() ? static_cast<std::uint64_t>(n.children().items_size()) : 0) + static_cast<std::uint64_t>(n.has_parent() ? n.parent() : 0);
}

// Dictionaries are repeated key/value entries, in the writer's order.
inline std::uint64_t lookup(const pbm::Lookup& x) {
    Checksum c;
    c.n(static_cast<std::uint64_t>(x.items_size()));
    for (const pbm::ItemEntry& e : x.items()) {
        c.s(e.key());
        c.i(e.value().count());
        c.f(e.value().weight());
    }
    c.n(static_cast<std::uint64_t>(x.prices_size()));
    for (const pbm::PriceEntry& e : x.prices()) {
        c.i(e.key());
        c.d(e.value());
    }
    return c.value();
}

inline std::uint64_t lookup_pick(const pbm::Lookup& x, std::uint32_t i) {
    const pbm::ItemEntry& e = x.items(static_cast<int>(i));
    return e.key().size() + static_cast<std::uint64_t>(e.value().count());
}

inline std::uint64_t canada(const pbm::FeatureCollection& x) {
    Checksum c;
    str(c, x.has_type(), x.type());
    c.n(static_cast<std::uint64_t>(x.features_size()));
    for (const pbm::Feature& f : x.features()) {
        str(c, f.has_type(), f.type());
        c.n(static_cast<std::uint64_t>(f.properties_size()));
        for (const pbm::PropertyEntry& p : f.properties()) {
            c.s(p.key());
            c.s(p.value());
        }
        if (!f.has_geometry()) { c.absent(); continue; }
        const pbm::Geometry& g = f.geometry();
        str(c, g.has_type(), g.type());
        c.n(static_cast<std::uint64_t>(g.coordinates_size()));
        for (const pbm::Ring& ring : g.coordinates()) {
            c.n(static_cast<std::uint64_t>(ring.points_size()));
            for (const pbm::Point& p : ring.points()) {
                c.d(p.x());
                c.d(p.y());
            }
        }
    }
    return c.value();
}

inline std::uint64_t canada_pick(const pbm::FeatureCollection& x, std::uint32_t i) {
    const pbm::Ring& ring = x.features(0).geometry().coordinates(static_cast<int>(i));
    const pbm::Point& p = ring.points(ring.points_size() / 2);
    return static_cast<std::uint64_t>(ring.points_size()) + std::bit_cast<std::uint64_t>(p.x()) + std::bit_cast<std::uint64_t>(p.y());
}

}  // namespace pb_read
