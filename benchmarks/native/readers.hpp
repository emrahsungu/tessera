// Full-traversal readers for the three libraries. Each computes the same checksum as
// benchmarks/Tessera.Benchmarks/Checksum.cs (fields in C# declaration order).
#pragma once

#ifndef MSGPACK_NO_BOOST
#define MSGPACK_NO_BOOST
#endif
#include <msgpack.hpp>

#include "bench_generated.h"  // FlatBuffers (flatc)
#include "bench.tessera.hpp"    // Tessera (generated from the C# models)

#include <bit>
#include <cstdint>
#include <string_view>

namespace im = bench::models;
namespace fbm = Bench::Fb;

constexpr std::uint64_t kAbsent = 0x9E3779B97F4A7C15ull;

struct Checksum {
    std::uint64_t h = 0xCBF29CE484222325ull;
    void mix(std::uint64_t v) noexcept { h = (h ^ v) * 0x100000001B3ull; }
    void i(std::int64_t v) noexcept { mix(static_cast<std::uint64_t>(v)); }
    void u(std::uint64_t v) noexcept { mix(v); }
    void f(float v) noexcept { mix(std::bit_cast<std::uint32_t>(v)); }
    void d(double v) noexcept { mix(std::bit_cast<std::uint64_t>(v)); }
    void b(bool v) noexcept { mix(v ? 1u : 0u); }
    void absent() noexcept { mix(kAbsent); }
    void s(std::string_view v) noexcept {
        mix(v.size());
        mix(v.empty() ? 0u : static_cast<std::uint8_t>(v[0]));
    }
};

// ============================================================================ Tessera

namespace tessera_read {

inline void v2(Checksum& c, im::Vec2 v) { c.f(v.x); c.f(v.y); }
inline void v3(Checksum& c, im::Vec3 v) { c.f(v.x); c.f(v.y); c.f(v.z); }
inline void v4(Checksum& c, im::Vec4 v) { c.f(v.x); c.f(v.y); c.f(v.z); c.f(v.w); }
inline void ref(Checksum& c, im::ObjectRef r) { c.u(r.component_name); c.i(r.index); c.i(r.object_ref_id); }

// Absent strings are views with data() == nullptr (a present empty string points into the buffer).
inline void str(Checksum& c, std::string_view s) {
    if (s.data()) c.s(s);
    else c.absent();
}

inline void refs(Checksum& c, tessera::Vector<im::ObjectRef> v) {
    if (!v) { c.absent(); return; }
    c.u(v.size());
    for (im::ObjectRef r : v) ref(c, r);
}

inline void opt_ref(Checksum& c, std::optional<im::ObjectRef> r) {
    if (r) ref(c, *r);
    else c.absent();
}

inline void opt_bool(Checksum& c, std::optional<bool> b) {
    if (b) c.b(*b);
    else c.absent();
}

inline void component(Checksum& c, im::Component comp) {
    switch (comp.type()) {
        case im::Component::Tag::ImageComponent: {
            auto x = comp.as_image_component();
            c.u(0);
            str(c, x.sprite());
            str(c, x.material());
            v4(c, x.color());
            v4(c, x.uv_rect());
            c.i(x.fill_method());
            c.f(x.fill_amount());
            c.b(x.enable());
            c.b(x.raycast_target());
            c.b(x.preserve_aspect());
            break;
        }
        case im::Component::Tag::TextComponent: {
            auto x = comp.as_text_component();
            c.u(1);
            str(c, x.text());
            str(c, x.font());
            v4(c, x.color());
            c.f(x.font_size());
            c.f(x.line_spacing());
            c.i(x.alignment());
            c.u(x.color_mode());
            c.b(x.enable());
            c.b(x.rich_text());
            break;
        }
        case im::Component::Tag::ButtonComponent: {
            auto x = comp.as_button_component();
            c.u(2);
            ref(c, x.target());
            ref(c, x.on_click());
            c.b(x.interactable());
            c.u(x.transition());
            break;
        }
        case im::Component::Tag::LayoutComponent: {
            auto x = comp.as_layout_component();
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
        case im::Component::Tag::ControllerComponent: {
            auto x = comp.as_controller_component();
            c.u(4);
            refs(c, x.sets()); refs(c, x.empties()); refs(c, x.hides()); refs(c, x.images()); refs(c, x.icons());
            refs(c, x.names()); refs(c, x.levels()); refs(c, x.skills()); refs(c, x.texts()); refs(c, x.buttons());
            opt_ref(c, x.icon()); opt_ref(c, x.glow()); opt_ref(c, x.level()); opt_ref(c, x.star()); opt_ref(c, x.skill_list());
            opt_ref(c, x.badge()); opt_ref(c, x.banner()); opt_ref(c, x.counter()); opt_ref(c, x.divider());
            opt_ref(c, x.header()); opt_ref(c, x.background()); opt_ref(c, x.group()); opt_ref(c, x.caption());
            opt_ref(c, x.thumbnail()); opt_ref(c, x.tooltip());
            opt_bool(c, x.is_pinned()); opt_bool(c, x.show_title()); opt_bool(c, x.is_featured());
            opt_bool(c, x.locked()); opt_bool(c, x.enable());
            break;
        }
        default:
            c.absent();
    }
}

inline std::uint64_t prefab(im::Prefab p) {
    Checksum c;
    auto objects = p.objects();
    if (!objects) { c.absent(); return c.h; }
    c.u(objects.size());
    for (im::PrefabObject o : objects) {
        str(c, o.name());
        auto comps = o.components();
        if (!comps) c.absent();
        else {
            c.u(comps.size());
            for (im::Component comp : comps) component(c, comp);
        }
        auto children = o.children();
        if (!children) c.absent();
        else {
            c.u(children.size());
            for (std::int32_t ch : children) c.i(ch);
        }
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
    return c.h;
}

inline void weapon(Checksum& c, im::Weapon w) {
    str(c, w.name());
    c.i(w.damage());
}

inline std::uint64_t world(im::World w) {
    Checksum c;
    auto monsters = w.monsters();
    if (!monsters) { c.absent(); return c.h; }
    c.u(monsters.size());
    for (im::Monster m : monsters) {
        str(c, m.name());
        c.i(m.hp());
        if (auto mana = m.mana()) c.i(*mana);
        else c.absent();
        v3(c, m.pos());
        c.u(static_cast<std::uint8_t>(m.color()));
        auto inv = m.inventory();
        if (!inv) c.absent();
        else {
            c.u(inv.size());
            for (std::uint8_t x : inv) c.u(x);
        }
        auto weapons = m.weapons();
        if (!weapons) c.absent();
        else {
            c.u(weapons.size());
            for (im::Weapon x : weapons) weapon(c, x);
        }
        if (auto e = m.equipped()) weapon(c, e);
        else c.absent();
        auto path = m.path();
        if (!path) c.absent();
        else {
            c.u(path.size());
            for (im::Vec3 v : path) v3(c, v);
        }
        c.b(m.friendly());
    }
    return c.h;
}

template <class T> inline void opt(Checksum& c, std::optional<T> v) {
    if (!v) c.absent();
    else if constexpr (std::is_same_v<T, float>) c.f(*v);
    else if constexpr (std::is_same_v<T, double>) c.d(*v);
    else if constexpr (std::is_same_v<T, bool>) c.b(*v);
    else c.i(*v);
}

inline std::uint64_t records(im::RecordSet s) {
    Checksum c;
    auto records = s.records();
    if (!records) { c.absent(); return c.h; }
    c.u(records.size());
    for (im::Record x : records) {
        opt(c, x.i0()); opt(c, x.i1()); opt(c, x.i2()); opt(c, x.i3()); opt(c, x.i4()); opt(c, x.i5()); opt(c, x.i6()); opt(c, x.i7());
        opt(c, x.i8()); opt(c, x.i9()); opt(c, x.i10()); opt(c, x.i11()); opt(c, x.i12()); opt(c, x.i13()); opt(c, x.i14()); opt(c, x.i15());
        opt(c, x.f0()); opt(c, x.f1()); opt(c, x.f2()); opt(c, x.f3()); opt(c, x.f4()); opt(c, x.f5()); opt(c, x.f6()); opt(c, x.f7());
        opt(c, x.d0()); opt(c, x.d1()); opt(c, x.d2()); opt(c, x.d3());
        opt(c, x.b0()); opt(c, x.b1()); opt(c, x.b2()); opt(c, x.b3()); opt(c, x.b4()); opt(c, x.b5()); opt(c, x.b6()); opt(c, x.b7());
        str(c, x.s0()); str(c, x.s1()); str(c, x.s2()); str(c, x.s3());
    }
    return c.h;
}

inline std::uint64_t series(im::Series s) {
    Checksum c;
    auto samples = s.samples();
    if (!samples) { c.absent(); return c.h; }
    c.u(samples.size());
    for (im::Sample x : samples) {
        c.i(x.timestamp());
        c.d(x.value());
        c.i(x.status());
        c.f(x.quality());
        str(c, x.note());
    }
    return c.h;
}

inline std::uint64_t lookup(im::Lookup x) {
    Checksum c;
    auto items = x.items();
    if (!items) c.absent();
    else {
        auto keys = items.keys();
        auto values = items.values();
        c.u(keys.size());
        for (std::uint32_t i = 0; i < keys.size(); ++i) {
            str(c, keys[i]);
            c.i(values[i].count());
            c.f(values[i].weight());
        }
    }
    auto prices = x.prices();
    if (!prices) c.absent();
    else {
        auto keys = prices.keys();
        auto values = prices.values();
        c.u(keys.size());
        for (std::uint32_t i = 0; i < keys.size(); ++i) {
            c.i(keys[i]);
            c.d(values[i]);
        }
    }
    return c.h;
}

inline std::uint64_t fixed_series(im::FixedSeries s) {
    Checksum c;
    auto samples = s.samples();
    if (!samples) { c.absent(); return c.h; }
    c.u(samples.size());
    for (im::FixedSample x : samples) {
        c.i(x.timestamp());
        c.d(x.value());
        c.i(x.status());
        c.f(x.quality());
        str(c, x.note());
    }
    return c.h;
}

inline std::uint64_t scene(im::Scene s) {
    Checksum c;
    auto nodes = s.nodes();
    if (!nodes) c.absent();
    else {
        c.u(nodes.size());
        for (im::Node n : nodes) {
            c.i(n.id());
            str(c, n.name());
            opt_bool(c, n.active());
            c.f(n.x());
            c.f(n.y());
            c.f(n.z());
            if (auto p = n.parent()) c.i(*p);
            else c.absent();
            auto ch = n.children();
            if (!ch) c.absent();
            else {
                c.u(ch.size());
                for (std::int32_t x : ch) c.i(x);
            }
        }
    }
    str(c, s.name());
    return c.h;
}

}  // namespace tessera_read

// ============================================================================ FlatBuffers

namespace fb_read {

inline void str(Checksum& c, const flatbuffers::String* s) {
    if (!s) c.absent();
    else c.s(std::string_view(s->c_str(), s->size()));
}

inline void v2(Checksum& c, const fbm::Vec2* v) { c.f(v ? v->x() : 0); c.f(v ? v->y() : 0); }
inline void v3(Checksum& c, const fbm::Vec3* v) { c.f(v ? v->x() : 0); c.f(v ? v->y() : 0); c.f(v ? v->z() : 0); }
inline void v4(Checksum& c, const fbm::Vec4* v) { c.f(v ? v->x() : 0); c.f(v ? v->y() : 0); c.f(v ? v->z() : 0); c.f(v ? v->w() : 0); }
inline void ref(Checksum& c, const fbm::ObjectRef& r) { c.u(r.component_name()); c.i(r.index()); c.i(r.object_ref_id()); }

inline void ref_or_zero(Checksum& c, const fbm::ObjectRef* r) {
    if (r) ref(c, *r);
    else { c.u(0); c.i(0); c.i(0); }
}

inline void refs(Checksum& c, const flatbuffers::Vector<const fbm::ObjectRef*>* v) {
    if (!v) { c.absent(); return; }
    c.u(v->size());
    for (const fbm::ObjectRef* r : *v) ref(c, *r);
}

inline void opt_ref(Checksum& c, const fbm::ObjectRef* r) {
    if (r) ref(c, *r);
    else c.absent();
}

inline void opt_bool(Checksum& c, flatbuffers::Optional<bool> b) {
    if (b.has_value()) c.b(b.value());
    else c.absent();
}

inline void component(Checksum& c, fbm::Component type, const void* p) {
    switch (type) {
        case fbm::Component::ImageComponent: {
            auto x = static_cast<const fbm::ImageComponent*>(p);
            c.u(0);
            str(c, x->sprite());
            str(c, x->material());
            v4(c, x->color());
            v4(c, x->uv_rect());
            c.i(x->fill_method());
            c.f(x->fill_amount());
            c.b(x->enable());
            c.b(x->raycast_target());
            c.b(x->preserve_aspect());
            break;
        }
        case fbm::Component::TextComponent: {
            auto x = static_cast<const fbm::TextComponent*>(p);
            c.u(1);
            str(c, x->text());
            str(c, x->font());
            v4(c, x->color());
            c.f(x->font_size());
            c.f(x->line_spacing());
            c.i(x->alignment());
            c.u(x->color_mode());
            c.b(x->enable());
            c.b(x->rich_text());
            break;
        }
        case fbm::Component::ButtonComponent: {
            auto x = static_cast<const fbm::ButtonComponent*>(p);
            c.u(2);
            ref_or_zero(c, x->target());
            ref_or_zero(c, x->on_click());
            c.b(x->interactable());
            c.u(x->transition());
            break;
        }
        case fbm::Component::LayoutComponent: {
            auto x = static_cast<const fbm::LayoutComponent*>(p);
            c.u(3);
            v4(c, x->padding());
            c.f(x->spacing());
            c.i(x->child_alignment());
            c.b(x->control_width());
            c.b(x->control_height());
            c.b(x->expand_width());
            c.b(x->expand_height());
            break;
        }
        case fbm::Component::ControllerComponent: {
            auto x = static_cast<const fbm::ControllerComponent*>(p);
            c.u(4);
            refs(c, x->sets()); refs(c, x->empties()); refs(c, x->hides()); refs(c, x->images()); refs(c, x->icons());
            refs(c, x->names()); refs(c, x->levels()); refs(c, x->skills()); refs(c, x->texts()); refs(c, x->buttons());
            opt_ref(c, x->icon()); opt_ref(c, x->glow()); opt_ref(c, x->level()); opt_ref(c, x->star()); opt_ref(c, x->skill_list());
            opt_ref(c, x->badge()); opt_ref(c, x->banner()); opt_ref(c, x->counter()); opt_ref(c, x->divider());
            opt_ref(c, x->header()); opt_ref(c, x->background()); opt_ref(c, x->group()); opt_ref(c, x->caption());
            opt_ref(c, x->thumbnail()); opt_ref(c, x->tooltip());
            opt_bool(c, x->is_pinned()); opt_bool(c, x->show_title()); opt_bool(c, x->is_featured());
            opt_bool(c, x->locked()); opt_bool(c, x->enable());
            break;
        }
        default:
            c.absent();
    }
}

inline std::uint64_t prefab(const fbm::Prefab* p) {
    Checksum c;
    auto objects = p->objects();
    if (!objects) { c.absent(); return c.h; }
    c.u(objects->size());
    for (const fbm::PrefabObject* o : *objects) {
        str(c, o->name());
        auto types = o->components_type();
        auto comps = o->components();
        if (!comps) c.absent();
        else {
            c.u(comps->size());
            for (flatbuffers::uoffset_t i = 0; i < comps->size(); ++i) component(c, types->Get(i), comps->Get(i));
        }
        auto children = o->children();
        if (!children) c.absent();
        else {
            c.u(children->size());
            for (std::int32_t ch : *children) c.i(ch);
        }
        v4(c, o->rotation());
        v3(c, o->position());
        v3(c, o->scale());
        v2(c, o->pivot());
        v2(c, o->anchor_min());
        v2(c, o->anchor_max());
        v2(c, o->size_delta());
        c.i(o->id());
        c.b(o->active());
    }
    return c.h;
}

inline void weapon(Checksum& c, const fbm::Weapon* w) {
    str(c, w->name());
    c.i(w->damage());
}

inline std::uint64_t world(const fbm::World* w) {
    Checksum c;
    auto monsters = w->monsters();
    if (!monsters) { c.absent(); return c.h; }
    c.u(monsters->size());
    for (const fbm::Monster* m : *monsters) {
        str(c, m->name());
        c.i(m->hp());
        auto mana = m->mana();
        if (mana.has_value()) c.i(mana.value());
        else c.absent();
        v3(c, m->pos());
        c.u(static_cast<std::uint8_t>(m->color()));
        auto inv = m->inventory();
        if (!inv) c.absent();
        else {
            c.u(inv->size());
            for (std::uint8_t x : *inv) c.u(x);
        }
        auto weapons = m->weapons();
        if (!weapons) c.absent();
        else {
            c.u(weapons->size());
            for (const fbm::Weapon* x : *weapons) weapon(c, x);
        }
        if (auto e = m->equipped()) weapon(c, e);
        else c.absent();
        auto path = m->path();
        if (!path) c.absent();
        else {
            c.u(path->size());
            for (const fbm::Vec3* v : *path) v3(c, v);
        }
        c.b(m->friendly());
    }
    return c.h;
}

template <class T> inline void opt(Checksum& c, flatbuffers::Optional<T> v) {
    if (!v.has_value()) c.absent();
    else if constexpr (std::is_same_v<T, float>) c.f(v.value());
    else if constexpr (std::is_same_v<T, double>) c.d(v.value());
    else if constexpr (std::is_same_v<T, bool>) c.b(v.value());
    else c.i(v.value());
}

inline std::uint64_t records(const fbm::RecordSet* s) {
    Checksum c;
    auto records = s->records();
    if (!records) { c.absent(); return c.h; }
    c.u(records->size());
    for (const fbm::Record* x : *records) {
        opt(c, x->i0()); opt(c, x->i1()); opt(c, x->i2()); opt(c, x->i3()); opt(c, x->i4()); opt(c, x->i5()); opt(c, x->i6()); opt(c, x->i7());
        opt(c, x->i8()); opt(c, x->i9()); opt(c, x->i10()); opt(c, x->i11()); opt(c, x->i12()); opt(c, x->i13()); opt(c, x->i14()); opt(c, x->i15());
        opt(c, x->f0()); opt(c, x->f1()); opt(c, x->f2()); opt(c, x->f3()); opt(c, x->f4()); opt(c, x->f5()); opt(c, x->f6()); opt(c, x->f7());
        opt(c, x->d0()); opt(c, x->d1()); opt(c, x->d2()); opt(c, x->d3());
        opt(c, x->b0()); opt(c, x->b1()); opt(c, x->b2()); opt(c, x->b3()); opt(c, x->b4()); opt(c, x->b5()); opt(c, x->b6()); opt(c, x->b7());
        str(c, x->s0()); str(c, x->s1()); str(c, x->s2()); str(c, x->s3());
    }
    return c.h;
}

inline std::uint64_t lookup(const fbm::Lookup* x) {
    Checksum c;
    if (!x->items()) c.absent();
    else {
        c.u(x->items()->size());
        for (const fbm::LookupItem* e : *x->items()) {
            str(c, e->name());
            c.i(e->count());
            c.f(e->weight());
        }
    }
    if (!x->prices()) c.absent();
    else {
        c.u(x->prices()->size());
        for (const fbm::LookupPrice* e : *x->prices()) {
            c.i(e->id());
            c.d(e->price());
        }
    }
    return c.h;
}

inline std::uint64_t series(const fbm::Series* s) {
    Checksum c;
    auto samples = s->samples();
    if (!samples) { c.absent(); return c.h; }
    c.u(samples->size());
    for (const fbm::Sample* x : *samples) {
        c.i(x->timestamp());
        c.d(x->value());
        c.i(x->status());
        c.f(x->quality());
        str(c, x->note());
    }
    return c.h;
}

inline std::uint64_t scene(const fbm::Scene* s) {
    Checksum c;
    auto nodes = s->nodes();
    if (!nodes) c.absent();
    else {
        c.u(nodes->size());
        for (const fbm::Node* n : *nodes) {
            c.i(n->id());
            str(c, n->name());
            opt_bool(c, n->active());
            c.f(n->x());
            c.f(n->y());
            c.f(n->z());
            if (auto p = n->parent(); p.has_value()) c.i(p.value());
            else c.absent();
            auto ch = n->children();
            if (!ch) c.absent();
            else {
                c.u(ch->size());
                for (std::int32_t x : *ch) c.i(x);
            }
        }
    }
    str(c, s->name());
    return c.h;
}

}  // namespace fb_read

// ============================================================================ MessagePack (msgpack-cxx object tree)

namespace mp_read {

using obj = msgpack::object;

inline const obj& at(const obj& a, std::uint32_t i) { return a.via.array.ptr[i]; }
inline bool nil(const obj& o) { return o.type == msgpack::type::NIL; }
inline std::uint64_t integer(const obj& o) { return o.type == msgpack::type::NEGATIVE_INTEGER ? static_cast<std::uint64_t>(o.via.i64) : o.via.u64; }
inline float f32(const obj& o) { return static_cast<float>(o.via.f64); }

inline void str(Checksum& c, const obj& o) {
    if (nil(o)) c.absent();
    else c.s(std::string_view(o.via.str.ptr, o.via.str.size));
}

inline void v2(Checksum& c, const obj& v) { c.f(f32(at(v, 0))); c.f(f32(at(v, 1))); }
inline void v3(Checksum& c, const obj& v) { c.f(f32(at(v, 0))); c.f(f32(at(v, 1))); c.f(f32(at(v, 2))); }
inline void v4(Checksum& c, const obj& v) { c.f(f32(at(v, 0))); c.f(f32(at(v, 1))); c.f(f32(at(v, 2))); c.f(f32(at(v, 3))); }
inline void ref(Checksum& c, const obj& r) { c.u(integer(at(r, 0))); c.i(static_cast<std::int64_t>(integer(at(r, 1)))); c.i(static_cast<std::int64_t>(integer(at(r, 2)))); }

inline void refs(Checksum& c, const obj& v) {
    if (nil(v)) { c.absent(); return; }
    c.u(v.via.array.size);
    for (std::uint32_t i = 0; i < v.via.array.size; ++i) ref(c, at(v, i));
}

inline void opt_ref(Checksum& c, const obj& r) {
    if (nil(r)) c.absent();
    else ref(c, r);
}

inline void opt_bool(Checksum& c, const obj& b) {
    if (nil(b)) c.absent();
    else c.b(b.via.boolean);
}

inline void i(Checksum& c, const obj& o) { c.i(static_cast<std::int64_t>(integer(o))); }

inline void component(Checksum& c, const obj& u) {
    const std::uint64_t key = integer(at(u, 0));
    const obj& x = at(u, 1);
    c.u(key);
    switch (key) {
        case 0:
            str(c, at(x, 0)); str(c, at(x, 1)); v4(c, at(x, 2)); v4(c, at(x, 3)); i(c, at(x, 4)); c.f(f32(at(x, 5)));
            c.b(at(x, 6).via.boolean); c.b(at(x, 7).via.boolean); c.b(at(x, 8).via.boolean);
            break;
        case 1:
            str(c, at(x, 0)); str(c, at(x, 1)); v4(c, at(x, 2)); c.f(f32(at(x, 3))); c.f(f32(at(x, 4))); i(c, at(x, 5));
            c.u(integer(at(x, 6))); c.b(at(x, 7).via.boolean); c.b(at(x, 8).via.boolean);
            break;
        case 2:
            ref(c, at(x, 0)); ref(c, at(x, 1)); c.b(at(x, 2).via.boolean); c.u(integer(at(x, 3)));
            break;
        case 3:
            v4(c, at(x, 0)); c.f(f32(at(x, 1))); i(c, at(x, 2));
            c.b(at(x, 3).via.boolean); c.b(at(x, 4).via.boolean); c.b(at(x, 5).via.boolean); c.b(at(x, 6).via.boolean);
            break;
        case 4:
            for (std::uint32_t k = 0; k < 10; ++k) refs(c, at(x, k));
            for (std::uint32_t k = 10; k < 25; ++k) opt_ref(c, at(x, k));
            for (std::uint32_t k = 25; k < 30; ++k) opt_bool(c, at(x, k));
            break;
        default:
            break;
    }
}

inline std::uint64_t prefab(const obj& p) {
    Checksum c;
    const obj& objects = at(p, 0);
    if (nil(objects)) { c.absent(); return c.h; }
    c.u(objects.via.array.size);
    for (std::uint32_t k = 0; k < objects.via.array.size; ++k) {
        const obj& o = at(objects, k);
        str(c, at(o, 0));
        const obj& comps = at(o, 1);
        if (nil(comps)) c.absent();
        else {
            c.u(comps.via.array.size);
            for (std::uint32_t j = 0; j < comps.via.array.size; ++j) component(c, at(comps, j));
        }
        const obj& children = at(o, 2);
        if (nil(children)) c.absent();
        else {
            c.u(children.via.array.size);
            for (std::uint32_t j = 0; j < children.via.array.size; ++j) i(c, at(children, j));
        }
        v4(c, at(o, 3)); v3(c, at(o, 4)); v3(c, at(o, 5)); v2(c, at(o, 6)); v2(c, at(o, 7)); v2(c, at(o, 8)); v2(c, at(o, 9));
        i(c, at(o, 10));
        c.b(at(o, 11).via.boolean);
    }
    return c.h;
}

inline void weapon(Checksum& c, const obj& w) {
    str(c, at(w, 0));
    i(c, at(w, 1));
}

inline std::uint64_t world(const obj& w) {
    Checksum c;
    const obj& monsters = at(w, 0);
    if (nil(monsters)) { c.absent(); return c.h; }
    c.u(monsters.via.array.size);
    for (std::uint32_t k = 0; k < monsters.via.array.size; ++k) {
        const obj& m = at(monsters, k);
        str(c, at(m, 0));
        i(c, at(m, 1));
        if (nil(at(m, 2))) c.absent();
        else i(c, at(m, 2));
        v3(c, at(m, 3));
        c.u(integer(at(m, 4)));
        const obj& inv = at(m, 5);
        if (nil(inv)) c.absent();
        else {
            c.u(inv.via.bin.size);
            for (std::uint32_t j = 0; j < inv.via.bin.size; ++j) c.u(static_cast<std::uint8_t>(inv.via.bin.ptr[j]));
        }
        const obj& weapons = at(m, 6);
        if (nil(weapons)) c.absent();
        else {
            c.u(weapons.via.array.size);
            for (std::uint32_t j = 0; j < weapons.via.array.size; ++j) weapon(c, at(weapons, j));
        }
        if (nil(at(m, 7))) c.absent();
        else weapon(c, at(m, 7));
        const obj& path = at(m, 8);
        if (nil(path)) c.absent();
        else {
            c.u(path.via.array.size);
            for (std::uint32_t j = 0; j < path.via.array.size; ++j) v3(c, at(path, j));
        }
        c.b(at(m, 9).via.boolean);
    }
    return c.h;
}

inline std::uint64_t records(const obj& s) {
    Checksum c;
    const obj& records = at(s, 0);
    if (nil(records)) { c.absent(); return c.h; }
    c.u(records.via.array.size);
    for (std::uint32_t k = 0; k < records.via.array.size; ++k) {
        const obj& x = at(records, k);
        for (std::uint32_t j = 0; j < 16; ++j) { const obj& v = at(x, j); if (nil(v)) c.absent(); else i(c, v); }
        for (std::uint32_t j = 16; j < 24; ++j) { const obj& v = at(x, j); if (nil(v)) c.absent(); else c.f(f32(v)); }
        for (std::uint32_t j = 24; j < 28; ++j) { const obj& v = at(x, j); if (nil(v)) c.absent(); else c.d(v.via.f64); }
        for (std::uint32_t j = 28; j < 36; ++j) { const obj& v = at(x, j); if (nil(v)) c.absent(); else c.b(v.via.boolean); }
        for (std::uint32_t j = 36; j < 40; ++j) str(c, at(x, j));
    }
    return c.h;
}

inline std::uint64_t lookup(const obj& x) {
    Checksum c;
    const obj& items = at(x, 0);
    if (nil(items)) c.absent();
    else {
        c.u(items.via.map.size);
        for (std::uint32_t k = 0; k < items.via.map.size; ++k) {
            const auto& kv = items.via.map.ptr[k];
            str(c, kv.key);
            i(c, at(kv.val, 0));
            c.f(f32(at(kv.val, 1)));
        }
    }
    const obj& prices = at(x, 1);
    if (nil(prices)) c.absent();
    else {
        c.u(prices.via.map.size);
        for (std::uint32_t k = 0; k < prices.via.map.size; ++k) {
            const auto& kv = prices.via.map.ptr[k];
            i(c, kv.key);
            c.d(kv.val.via.f64);
        }
    }
    return c.h;
}

inline std::uint64_t series(const obj& s) {
    Checksum c;
    const obj& samples = at(s, 0);
    if (nil(samples)) { c.absent(); return c.h; }
    c.u(samples.via.array.size);
    for (std::uint32_t k = 0; k < samples.via.array.size; ++k) {
        const obj& x = at(samples, k);
        i(c, at(x, 0));
        c.d(at(x, 1).via.f64);
        i(c, at(x, 2));
        c.f(f32(at(x, 3)));
        str(c, at(x, 4));
    }
    return c.h;
}

inline std::uint64_t scene(const obj& s) {
    Checksum c;
    const obj& nodes = at(s, 0);
    if (nil(nodes)) c.absent();
    else {
        c.u(nodes.via.array.size);
        for (std::uint32_t k = 0; k < nodes.via.array.size; ++k) {
            const obj& n = at(nodes, k);
            i(c, at(n, 0));
            str(c, at(n, 1));
            opt_bool(c, at(n, 2));
            c.f(f32(at(n, 3)));
            c.f(f32(at(n, 4)));
            c.f(f32(at(n, 5)));
            if (nil(at(n, 6))) c.absent();
            else i(c, at(n, 6));
            const obj& ch = at(n, 7);
            if (nil(ch)) c.absent();
            else {
                c.u(ch.via.array.size);
                for (std::uint32_t j = 0; j < ch.via.array.size; ++j) i(c, at(ch, j));
            }
        }
    }
    str(c, at(s, 1));
    return c.h;
}

}  // namespace mp_read
