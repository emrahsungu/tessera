// Tessera C++20 runtime, part 2: schema section, bindings for other schema versions, verification, Reader, JSON dump.
// Included by tessera.hpp; do not include directly.
//
// Copyright (c) Tessera contributors. Licensed under the Apache License, Version 2.0.
#pragma once

namespace tessera {
namespace detail {

inline constexpr std::uint16_t kNoType = 0xFFFF;
inline constexpr std::uint8_t kFieldFixed = 2;     // schema field flag: a fixed cell
inline constexpr std::uint8_t kEntryFixedCells = 1; // object entry flag: fixed cell count and size follow
inline constexpr std::uint8_t kElementOptional = 2; // vector entry flag: optional values (presence bits first)
inline constexpr std::uint32_t kMaxFixed = 1024;   // bytes of fixed cells per object (WireFormat.MaxFixedBytes)

/// Fixed cell size of a kind; 0 for bool, -1 for inline structs (size comes from the struct entry).
constexpr int kind_cell_size(Kind k) noexcept {
    switch (k) {
        case Kind::Bool: return 0;
        case Kind::Int8: case Kind::UInt8: return 1;
        case Kind::Int16: case Kind::UInt16: case Kind::Char16: return 2;
        case Kind::Int32: case Kind::UInt32: case Kind::Float32: case Kind::String: case Kind::Object: case Kind::Vector: case Kind::SharedStruct: return 4;
        case Kind::Int64: case Kind::UInt64: case Kind::Float64: case Kind::Union: return 8;
        default: return -1;
    }
}

constexpr bool is_ref_kind(Kind k) noexcept {
    return k == Kind::String || k == Kind::Object || k == Kind::Vector || k == Kind::Union || k == Kind::SharedStruct;
}

constexpr bool needs_type_ref(Kind k) noexcept {
    return k == Kind::Struct || k == Kind::SharedStruct || k == Kind::Object || k == Kind::Vector || k == Kind::Union;
}

// ---------------------------------------------------------------- schema section (written by the .NET writer)

/// One schema entry. Common header: u8 category, u8 flags, u16 count, u32 type id, u64 fingerprint, u64 deep fingerprint.
struct Entry {
    const std::uint8_t* p = nullptr;
    [[nodiscard]] std::uint8_t category() const noexcept { return p[0]; }
    [[nodiscard]] std::uint16_t count() const noexcept { return load<std::uint16_t>(p + 2); }
    [[nodiscard]] std::uint32_t type_id() const noexcept { return load32(p + 4); }
    [[nodiscard]] std::uint64_t fingerprint() const noexcept { return load<std::uint64_t>(p + 8); }
    [[nodiscard]] std::uint64_t deep() const noexcept { return load<std::uint64_t>(p + 16); }
    // objects: u16 header words, u16 cells with presence bits, [u16 fixed cells, u16 bytes of fixed cells: when flags
    // bit 0 is set], then count x {u64 hash, u8 kind, u8 flags, u16 type ref}: the fixed cells, the other cells, the bools
    [[nodiscard]] bool has_fixed() const noexcept { return (p[1] & kEntryFixedCells) != 0; }
    [[nodiscard]] std::uint16_t words() const noexcept { return load<std::uint16_t>(p + 24); }
    [[nodiscard]] std::uint16_t cell_count() const noexcept { return load<std::uint16_t>(p + 26); }
    [[nodiscard]] std::uint16_t fixed_count() const noexcept { return has_fixed() ? load<std::uint16_t>(p + 28) : 0; }
    [[nodiscard]] std::uint16_t fixed_size() const noexcept { return has_fixed() ? load<std::uint16_t>(p + 30) : 0; }
    [[nodiscard]] const std::uint8_t* field(std::uint32_t i) const noexcept { return p + (has_fixed() ? 32 : 28) + 12 * i; }
    [[nodiscard]] std::uint64_t field_hash(std::uint32_t i) const noexcept { return load<std::uint64_t>(field(i)); }
    [[nodiscard]] Kind field_kind(std::uint32_t i) const noexcept { return static_cast<Kind>(field(i)[8]); }
    [[nodiscard]] std::uint8_t field_flags(std::uint32_t i) const noexcept { return field(i)[9]; }
    [[nodiscard]] std::uint16_t field_ref(std::uint32_t i) const noexcept { return load<std::uint16_t>(field(i) + 10); }
    [[nodiscard]] std::uint32_t bool_start() const noexcept { return (cell_count() + 1u) & ~1u; }
    /// Presence bit of a cell or bool field (fixed cells have none).
    [[nodiscard]] std::uint32_t field_bit(std::uint32_t i) const noexcept {
        const std::uint32_t c = i - fixed_count();
        return c < cell_count() ? c : bool_start() + 2u * (c - cell_count());
    }
    // structs: u16 size, u8 align
    [[nodiscard]] std::uint16_t struct_size() const noexcept { return load<std::uint16_t>(p + 24); }
    [[nodiscard]] std::uint8_t struct_align() const noexcept { return p[26]; }
    // vectors: u8 element kind, u8 flags (bit 0: elements may be null; bit 1: optional values), u16 element type ref
    [[nodiscard]] Kind elem_kind() const noexcept { return static_cast<Kind>(p[24]); }
    [[nodiscard]] bool elem_optional() const noexcept { return (p[25] & kElementOptional) != 0; }
    [[nodiscard]] std::uint16_t elem_ref() const noexcept { return load<std::uint16_t>(p + 26); }
    // unions: count x {u32 tag, u16 type ref, u16 reserved}
    [[nodiscard]] std::uint32_t member_tag(std::uint32_t i) const noexcept { return load32(p + 24 + 8 * i); }
    [[nodiscard]] std::uint16_t member_ref(std::uint32_t i) const noexcept { return load<std::uint16_t>(p + 28 + 8 * i); }
};

/// Validated view of the schema section that follows the header.
class SchemaView {
public:
    bool parse(const std::uint8_t* buf, std::size_t size) noexcept {
        if (size < 16 + 8) return false;
        base_ = buf + 16;
        len_ = load32(base_);
        if (len_ < 8 || len_ > size - 16) return false;
        count_ = load<std::uint16_t>(base_ + 4);
        root_ = load<std::uint16_t>(base_ + 6);
        if (count_ == 0 || root_ >= count_ || 8u + 4u * count_ > len_) return false;
        for (std::uint32_t i = 0; i < count_; ++i) {
            const std::uint32_t off = load32(base_ + 8 + 4 * i);
            if ((off & 3u) || off < 8u + 4u * count_ || off > len_ || len_ - off < 24) return false;
            const Entry e{base_ + off};
            const std::uint32_t room = len_ - off;
            switch (e.category()) {
                case 1: {  // object
                    if ((e.p[1] & ~kEntryFixedCells) != 0) return false;
                    if (room < (e.has_fixed() ? 32u : 28u) + 12u * e.count()) return false;
                    const std::uint32_t X = e.fixed_count(), C = e.cell_count();
                    if (X + C > e.count() || e.fixed_size() > kMaxFixed || (e.has_fixed() && X == 0)) return false;
                    const std::uint32_t bits = e.bool_start() + 2u * (e.count() - X - C);
                    const std::uint32_t words = bits == 0 ? 1u : (bits + 31u) / 32u;
                    if (e.words() != words) return false;
                    for (std::uint32_t f = 0; f < e.count(); ++f) {
                        const Kind k = e.field_kind(f);
                        if (k == Kind::Invalid || static_cast<std::uint8_t>(k) > 18) return false;
                        if ((k == Kind::Bool) != (f >= X + C)) return false;
                        // Fixed cells: exactly the first X fields, all scalars or inline structs.
                        const bool fixed = (e.field_flags(f) & kFieldFixed) != 0;
                        if (fixed != (f < X) || (fixed && !(k == Kind::Struct || (k >= Kind::Int8 && k <= Kind::Char16)))) return false;
                        const std::uint16_t r = e.field_ref(f);
                        if (needs_type_ref(k) ? (r != kNoType && r >= count_) : r != kNoType) return false;
                        if ((k == Kind::Struct || k == Kind::SharedStruct) && r == kNoType) return false;
                    }
                    break;
                }
                case 2:  // struct
                    if (room < 28 || e.struct_size() == 0) return false;
                    break;
                case 3: {  // vector
                    if (room < 28) return false;
                    const Kind k = e.elem_kind();
                    if (k == Kind::Invalid || static_cast<std::uint8_t>(k) > 18) return false;
                    if ((e.p[25] & ~3u) != 0 || (e.elem_optional() && !(k == Kind::Struct || (k >= Kind::Bool && k <= Kind::Char16)))) return false;
                    if (needs_type_ref(k) ? e.elem_ref() >= count_ : e.elem_ref() != kNoType) return false;
                    break;
                }
                case 4:  // union
                    if (room < 24u + 8u * e.count()) return false;
                    for (std::uint32_t m = 0; m < e.count(); ++m)
                        if (e.member_ref(m) != kNoType && e.member_ref(m) >= count_) return false;
                    break;
                default:
                    return false;
            }
        }
        // Struct-kind fields must reference struct entries; object/vector/union refs must have the right category.
        for (std::uint32_t i = 0; i < count_; ++i) {
            const Entry e = entry(static_cast<std::uint16_t>(i));
            if (e.category() == 1) {
                for (std::uint32_t f = 0; f < e.count(); ++f)
                    if (!ref_matches(e.field_kind(f), e.field_ref(f))) return false;
                // The fixed cells, back to back, padded to the other cells' largest alignment.
                std::uint32_t end = 0, align = 1;
                for (std::uint32_t f = 0; f < e.fixed_count(); ++f) end += cell_size(e, f);
                for (std::uint32_t f = e.fixed_count(); f < std::uint32_t{e.fixed_count()} + e.cell_count(); ++f) align = std::max(align, cell_align(e, f));
                if (e.fixed_size() != (end + align - 1) / align * align) return false;
            } else if (e.category() == 3) {
                if (!ref_matches(e.elem_kind(), e.elem_ref())) return false;
            } else if (e.category() == 4) {
                for (std::uint32_t m = 0; m < e.count(); ++m)
                    if (e.member_ref(m) != kNoType && entry(e.member_ref(m)).category() != 1) return false;
            }
        }
        return true;
    }

    [[nodiscard]] std::uint16_t count() const noexcept { return count_; }
    [[nodiscard]] std::uint16_t root() const noexcept { return root_; }
    [[nodiscard]] Entry entry(std::uint16_t i) const noexcept { return Entry{base_ + load32(base_ + 8 + 4 * i)}; }

    /// Cell size of field f of object entry e in the writer's layout.
    [[nodiscard]] std::uint32_t cell_size(const Entry& e, std::uint32_t f) const noexcept {
        const Kind k = e.field_kind(f);
        return k == Kind::Struct ? entry(e.field_ref(f)).struct_size() : static_cast<std::uint32_t>(kind_cell_size(k));
    }

    /// Alignment of field f's cell (structs: the struct's alignment; unions: 4).
    [[nodiscard]] std::uint32_t cell_align(const Entry& e, std::uint32_t f) const noexcept {
        const Kind k = e.field_kind(f);
        if (k == Kind::Struct) return entry(e.field_ref(f)).struct_align();
        return k == Kind::Union ? 4u : static_cast<std::uint32_t>(std::max(1, kind_cell_size(k)));
    }

    /// Position of fixed field f (f < fixed_count) after the header words.
    [[nodiscard]] std::uint32_t fixed_offset(const Entry& e, std::uint32_t f) const noexcept {
        std::uint32_t off = 0;
        for (std::uint32_t k = 0; k < f; ++k) off += cell_size(e, k);
        return off;
    }

    [[nodiscard]] std::uint32_t elem_size(const Entry& v) const noexcept {
        const Kind k = v.elem_kind();
        if (k == Kind::Bool) return 1;
        if (k == Kind::Struct) return entry(v.elem_ref()).struct_size();
        return static_cast<std::uint32_t>(kind_cell_size(k));
    }

private:
    bool ref_matches(Kind k, std::uint16_t r) const noexcept {
        if (!needs_type_ref(k) || r == kNoType) return true;
        const std::uint8_t c = entry(r).category();
        switch (k) {
            case Kind::Struct: case Kind::SharedStruct: return c == 2;
            case Kind::Object: return c == 1;
            case Kind::Vector: return c == 3;
            case Kind::Union: return c == 4;
            default: return false;
        }
    }

    const std::uint8_t* base_ = nullptr;
    std::uint32_t len_ = 0;
    std::uint16_t count_ = 0;
    std::uint16_t root_ = 0;
};

// ---------------------------------------------------------------- binder

/// Owns the bindings created for one buffer.
struct BindState {
    SchemaView schema;
    std::vector<std::unique_ptr<Binding>> bindings;
};

/// Matches the reader's compiled schema against the writer's schema section, field by field (by name hash and kind).
/// "Identical" is decided by comparing layouts structurally, never by trusting fingerprints stored in the buffer, so a
/// forged schema cannot make the reader skip the checks the verifier applied.
class Binder {
public:
    explicit Binder(BindState& state) noexcept : s_(state.schema), store_(state.bindings) {}

    /// nullptr: identical layout for the whole subtree; kIncompatible: cannot be read; otherwise a binding.
    const Binding* bind(const TypeInfo& r, std::uint16_t e) {
        if (e == kNoType) return kIncompatible;
        for (auto& m : memo_) {
            if (m.r == &r && m.e == e) {
                if (m.in_progress) m.cyclic = true;
                return m.b;
            }
        }
        const Entry E = s_.entry(e);
        if (r.kind == Kind::Object && E.category() == 1) return bind_object(r, e, E);
        if (r.kind == Kind::Union && E.category() == 4) return bind_union(r, E);
        if (r.kind == Kind::Vector && E.category() == 3) return bind_vector(r, E);
        return kIncompatible;
    }

private:
    /// Struct-valued fields must agree on fingerprint and size (the reader copies sizeof(S) bytes).
    bool struct_matches(Kind k, std::uint16_t ref, std::uint64_t fp, std::uint32_t size) const noexcept {
        if (k != Kind::Struct && k != Kind::SharedStruct) return true;
        if (ref == kNoType) return false;
        const Entry S = s_.entry(ref);
        return S.fingerprint() == fp && S.struct_size() == size;
    }

    const Binding* bind_object(const TypeInfo& r, std::uint16_t e, const Entry& E) {
        store_.push_back(std::make_unique<Binding>());
        Binding* b = store_.back().get();
        const std::size_t mi = memo_.size();
        memo_.push_back({&r, e, b, true, false});
        b->words = E.words();
        b->fixed = E.fixed_size();
        b->fields.resize(r.field_count);
        bool identical = r.field_count == E.count() && r.cell_count == E.cell_count() && r.words == E.words() && r.fixed == E.fixed_size();
        const std::uint32_t X = E.fixed_count();
        bool children_identical = true;
        std::vector<std::pair<std::uint64_t, std::uint32_t>> byHash(E.count());
        for (std::uint32_t j = 0; j < E.count(); ++j) byHash[j] = {E.field_hash(j), j};
        std::sort(byHash.begin(), byHash.end());
        for (std::uint32_t i = 0; i < r.field_count; ++i) {
            const FieldInfo& rf = r.fields[i];
            FieldBinding& fb = b->fields[i];
            auto it = std::lower_bound(byHash.begin(), byHash.end(), std::pair<std::uint64_t, std::uint32_t>{rf.hash, 0u});
            if (it == byHash.end() || it->first != rf.hash) { identical = false; continue; }
            const std::uint32_t j = it->second;
            const Kind wk = E.field_kind(j);
            if (wk != rf.kind || !struct_matches(wk, E.field_ref(j), rf.struct_fp, rf.struct_size)) { identical = false; continue; }
            const bool fixed = j < X;
            const std::uint32_t bit = fixed ? kFixedBit + s_.fixed_offset(E, j) : E.field_bit(j);
            if (j != i || bit != rf.bit || (wk != Kind::Bool && s_.cell_size(E, j) != rf.cell)) identical = false;
            if (rf.child) {
                const Binding* child = bind(rf.child(), E.field_ref(j));
                if (child == kIncompatible) { identical = false; continue; }
                fb.child = child;
                if (child != nullptr) children_identical = false;
            }
            if (fixed) {
                fb.bit = kFixedBinding;
                fb.term_start = 4u * E.words() + s_.fixed_offset(E, j);
                continue;
            }
            fb.bit = static_cast<std::uint16_t>(E.field_bit(j));
            if (wk == Kind::Bool) continue;
            // Terms: runs of equal-size writer cells in the same header word before cell j (presence bit k - X).
            fb.term_start = static_cast<std::uint32_t>(b->terms.size());
            for (std::uint32_t k = X; k < j;) {
                const std::uint32_t start = k, size = s_.cell_size(E, k);
                while (k < j && s_.cell_size(E, k) == size && (k - X) / 32 == (start - X) / 32) ++k;
                const std::uint32_t lo = (start - X) % 32, hi = (k - 1 - X) % 32;
                const std::uint32_t mask = (hi == 31 ? 0xFFFFFFFFu : ((1u << (hi + 1)) - 1u)) & ~((1u << lo) - 1u);
                b->terms.push_back(Term{static_cast<std::uint16_t>((start - X) / 32), static_cast<std::uint16_t>(size), mask});
            }
            fb.term_count = static_cast<std::uint16_t>(b->terms.size() - fb.term_start);
        }
        b->own_identical = identical;
        memo_[mi].in_progress = false;
        if (identical && children_identical && !memo_[mi].cyclic) {
            memo_[mi].b = nullptr;  // nobody holds b yet: the whole subtree reads with compile-time positions
            return nullptr;
        }
        return b;
    }

    const Binding* bind_union(const TypeInfo& r, const Entry& E) {
        std::vector<std::pair<std::uint32_t, const Binding*>> members;
        bool identical = true;
        for (std::uint32_t k = 0; k < r.member_count; ++k) {
            const std::uint32_t tag = r.tags[k];
            for (std::uint32_t m = 0; m < E.count(); ++m) {
                if (E.member_tag(m) != tag) continue;
                const Binding* mb = bind(r.members[k](), E.member_ref(m));
                if (mb != nullptr) identical = false;
                members.emplace_back(tag, mb);
                break;
            }
        }
        if (identical) return nullptr;
        store_.push_back(std::make_unique<Binding>());
        store_.back()->members = std::move(members);
        return store_.back().get();
    }

    const Binding* bind_vector(const TypeInfo& r, const Entry& E) {
        const Kind k = E.elem_kind();
        if (k != r.elem_kind || E.elem_optional() != r.elem_optional || !struct_matches(k, E.elem_ref(), r.elem_struct_fp, r.elem_struct_size)) return kIncompatible;
        if (k != Kind::SharedStruct && s_.elem_size(E) != r.elem_size) return kIncompatible;
        if (!r.elem) return nullptr;
        const Binding* child = bind(r.elem(), E.elem_ref());
        if (child == kIncompatible || child == nullptr) return child;
        store_.push_back(std::make_unique<Binding>());
        store_.back()->element = child;
        return store_.back().get();
    }

    struct Memo {
        const TypeInfo* r;
        std::uint16_t e;
        const Binding* b;
        bool in_progress;
        bool cyclic;
    };

    const SchemaView& s_;
    std::vector<std::unique_ptr<Binding>>& store_;
    std::vector<Memo> memo_;
};
// ---------------------------------------------------------------- verification

/// Per-type key of the items verified once (writable data, so identical-data folding never merges two types' keys).
template <class T> inline char once_key = 0;
template <class T> struct vector_of {};

#ifdef TESSERA_VERIFY_ONCE_ALWAYS  // tests: skip the tree walk and verify every item once
inline constexpr bool kVerifyOnceAlways = true;
#else
inline constexpr bool kVerifyOnceAlways = false;
#endif

// A buffer written with sharing references an item from several places, so walking it as a tree verifies the item once
// per reference: for a deep DAG, exponentially often. No item, and no reference in a vector, is smaller than 4 bytes,
// and references only point forward (so items never contain themselves): more than size / 4 items visited, or vector
// references followed, prove that the buffer revisits items. Walking a shared item again is cheaper than looking it
// up, though, so the tree walk goes on up to 16 times that (items only until max_items, if lower), and validates up to
// 16 times the buffer size of long strings' UTF-8. It is the same code as for buffers that share nothing. Past a limit,
// with sharing proven, Reader verifies again with each (position, type) verified once. A once-verified item remembers
// its subtree's height, so a later visit checks the depth limit for its own path.
class Verifier {
public:
    Verifier(const std::uint8_t* base, std::size_t size, const Options& o) noexcept
        : base_(base), size_(size), o_(o), limit_(std::min<std::uint64_t>(o.max_items, 16 * (std::uint64_t{size} / 4))),
          refs_left_(16 * (std::uint64_t{size} / 4)), utf8_left_(16 * std::uint64_t{size} + 65536) {}
    Verifier(const Verifier&) = delete;
    Verifier& operator=(const Verifier&) = delete;
    ~Verifier() {
        if (memo_ != nullptr) delete[] memo_;  // the check saves a call when nothing was shared
    }

    bool fail(Error e) noexcept {
        if (err_ == Error::None) err_ = e;
        return false;
    }

    [[nodiscard]] Error error() const noexcept { return err_; }
    [[nodiscard]] const Options& options() const noexcept { return o_; }

    bool range(const std::uint8_t* p, std::uint64_t n) noexcept {
        const std::uint64_t off = static_cast<std::uint64_t>(p - base_);
        return (off <= size_ && n <= size_ - off) || fail(Error::OutOfBounds);
    }

    /// Target of the 4-byte offset at `slot` (must be non-zero, 4-aligned and in bounds).
    const std::uint8_t* follow(const std::uint8_t* slot) noexcept {
        const std::uint32_t off = load32(slot);
        if (off == 0 || (off & 3u)) { fail(Error::BadOffset); return nullptr; }
        const std::uint64_t pos = static_cast<std::uint64_t>(slot - base_) + off;
        if (pos >= size_) { fail(Error::OutOfBounds); return nullptr; }
        return base_ + pos;
    }

    bool enter(std::uint32_t depth) noexcept {
        if (depth > o_.max_depth) return fail(Error::TooDeep);
        if (++items_ > limit_) return fail(Error::TooManyItems);
        return true;
    }

    /// A vector's `n` references, about to be followed.
    bool refs(std::uint32_t n) noexcept {
        if (n <= refs_left_) [[likely]] {
            refs_left_ -= n;
            return true;
        }
        refs_over_ = true;
        return fail(Error::TooManyItems);
    }

    /// After the tree walk failed: whether it stopped because the buffer shares items rather than at a limit.
    [[nodiscard]] bool shared() const noexcept {
        return memo_ == nullptr && err_ == Error::TooManyItems && (items_ > size_ / 4 || refs_over_ || utf8_over_);
    }

    /// Starts over, verifying each item once; max_items then counts the items verified.
    bool start_once() noexcept {
        err_ = Error::None;
        items_ = 0;
        limit_ = o_.max_items;
        refs_left_ = ~std::uint64_t{0};  // each vector is verified once
        deepest_ = 0;
        own_ = false;
        return grow(1024) || fail(Error::TooManyItems);
    }

    /// Whether this visit comes from verify_once (so verify the item) rather than from a reference to it.
    bool own_visit() noexcept { return std::exchange(own_, false); }

    /// The item at `p` as `key`, verified by `verify` (the item's verifier, called again) unless it was already.
    using VerifyFn = bool (*)(Verifier&, const std::uint8_t*, std::uint32_t) noexcept;
    TESSERA_NOINLINE bool verify_once(const std::uint8_t* p, std::uintptr_t key, std::uint32_t depth, VerifyFn verify) noexcept {
        return verify_once_with(p, key, depth, [&]() noexcept { return verify(*this, p, depth); });
    }

    template <class F> bool verify_once_with(const std::uint8_t* p, std::uintptr_t key, std::uint32_t depth, F&& verify) noexcept {
        const auto pos = static_cast<std::uint64_t>(p - base_);
        if (const Slot* s = find(pos, key); s->key != 0) {
            if (std::uint64_t{depth} + s->height > o_.max_depth) return fail(Error::TooDeep);
            if (depth + s->height > deepest_) deepest_ = depth + s->height;
            return true;
        }
        const std::uint32_t outer = deepest_;
        deepest_ = depth;
        own_ = true;
        if (!verify()) return false;
        if (!remember(pos, key, deepest_ - depth)) return false;
        if (outer > deepest_) deepest_ = outer;
        return true;
    }

    bool aligned(const std::uint8_t* p, std::uint32_t a) noexcept {
        return (static_cast<std::uint64_t>(p - base_) & (a - 1u)) == 0 || fail(Error::BadOffset);
    }

    // Out of line: inlined, it grows the table verifiers past what compilers inline into vector loops.
    TESSERA_NOINLINE bool string(const std::uint8_t* s) noexcept {
        if (!range(s, 4)) return false;
        const std::uint32_t n = load32(s);
        if (!range(s, std::uint64_t{n} + 5)) return false;
        if (s[4 + std::size_t{n}] != 0) return fail(Error::BadString);
        // Unlikely: UTF-8 validation is opt-in. Without the hint, GCC put this return 1 KB further, after the inlined
        // validator, so every string verified without it took a jump to another cache line.
        if (o_.utf8) [[unlikely]] return utf8(s, n);
        return true;
    }

    // With MSVC and Clang its own function, reached by a tail jump: inlined into string(), the validator's registers
    // would be saved on every call, with or without Options::utf8. GCC saves them only when it validates, and the jump
    // made its validation of short strings slower (measured), so there it stays inline.
    TESSERA_GCC_INLINE bool utf8(const std::uint8_t* s, std::uint32_t n) noexcept {
        if (n >= kLongString) [[unlikely]] return utf8_long(s, n);
        if (!valid_utf8(s + 4, n)) return fail(Error::BadString);
        return true;
    }

    // One instance per caller, so each has a single call site: GCC then inlines it into utf8() as it did when that was
    // its only caller (forcing the inline compiles its loop worse); MSVC needs the inline forced.
    template <int Caller = 0> static TESSERA_MSVC_FORCEINLINE bool valid_utf8(const std::uint8_t* p, std::uint32_t n) noexcept {
        // ASCII runs are skipped 8 bytes at a time, at the start and after each multi-byte character. The byte loop
        // never retries the skip, so the bytes of short strings cost what they did without it.
        std::uint32_t i = 0;
        while (n - i >= 8 && (load<std::uint64_t>(p + i) & 0x8080808080808080ull) == 0) i += 8;
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
            while (n - i >= 8 && (load<std::uint64_t>(p + i) & 0x8080808080808080ull) == 0) i += 8;
        }
        return true;
    }

private:
    struct Slot {
        std::uintptr_t key;  // 0: empty
        std::uint64_t pos;
        std::uint32_t height;
    };
    static constexpr std::uintptr_t kStringKey = 1;  // neither an address nor a schema entry key (2e + 2, 2e + 3)
    // Validating a short string once per reference costs at most kLongString bytes per reference (and references are
    // counted); long strings count against a budget, or are validated once.
    static constexpr std::uint32_t kLongString = 256;

    TESSERA_NOINLINE bool utf8_long(const std::uint8_t* s, std::uint32_t n) noexcept {
        if (memo_ == nullptr) {
            if (n > utf8_left_) {
                utf8_over_ = true;  // more than the buffer could hold without sharing
                return fail(Error::TooManyItems);
            }
            utf8_left_ -= n;
            return valid_utf8<1>(s + 4, n) || fail(Error::BadString);
        }
        const auto pos = static_cast<std::uint64_t>(s - base_);
        if (find(pos, kStringKey)->key != 0) return true;
        if (!valid_utf8<1>(s + 4, n)) return fail(Error::BadString);
        return remember(pos, kStringKey, 0);
    }

    // Open addressing with linear probing; at most half full.
    const Slot* find(std::uint64_t pos, std::uintptr_t key) const noexcept {
        const std::uint64_t h = (pos + std::uint64_t{key} * 0xC2B2AE3D27D4EB4Full) * 0x9E3779B97F4A7C15ull;
        for (auto i = static_cast<std::uint32_t>(h >> 32) & mask_;; i = (i + 1) & mask_) {
            const Slot& s = memo_[i];
            if (s.key == 0 || (s.key == key && s.pos == pos)) return &s;
        }
    }

    bool remember(std::uint64_t pos, std::uintptr_t key, std::uint32_t height) noexcept {
        if (2 * (used_ + 1) > mask_ + 1 && (mask_ >= (1u << 30) || !grow(2 * (mask_ + 1)))) return fail(Error::TooManyItems);
        *const_cast<Slot*>(find(pos, key)) = Slot{key, pos, height};
        ++used_;
        return true;
    }

    bool grow(std::uint32_t capacity) noexcept {
        Slot* table = new (std::nothrow) Slot[capacity]();
        if (table == nullptr) return false;
        Slot* old = memo_;
        const std::uint32_t oldCapacity = old ? mask_ + 1 : 0;
        memo_ = table;
        mask_ = capacity - 1;
        for (std::uint32_t i = 0; i < oldCapacity; ++i)
            if (old[i].key != 0) *const_cast<Slot*>(find(old[i].pos, old[i].key)) = old[i];
        delete[] old;
        return true;
    }

    const std::uint8_t* base_;
    std::size_t size_;
    const Options& o_;
    std::uint64_t items_ = 0;
    std::uint64_t limit_;            // tree walk: 16 x size / 4 (or max_items, if lower); verifying once: max_items
    std::uint64_t refs_left_;        // vector references the tree walk may follow
    std::uint64_t utf8_left_;        // bytes of long strings the tree walk may validate
    bool refs_over_ = false;
    bool utf8_over_ = false;
    Slot* memo_ = nullptr;           // the items verified once
    std::uint32_t mask_ = 0, used_ = 0;
    std::uint32_t deepest_ = 0;      // deepest depth reached under the item being verified once
    bool own_ = false;
    Error err_ = Error::None;
};

// Compile-time verification for buffers written with the reader's own schema. Once = true verifies each item once.

template <class S> consteval auto header_masks() {
    struct Masks {
        std::array<std::uint32_t, S::words> reserved{};
        std::array<std::uint32_t, S::words> bool_present{};
    } m{};
    std::array<std::uint32_t, S::words> used{};
    std::apply([&](const auto&... f) {
        [[maybe_unused]] auto mark = [&](const auto& fld) {
            using T = typename std::remove_cvref_t<decltype(fld)>::type;
            if (fld.bit >= kFixedBit) return;  // fixed cells have no presence bit
            used[fld.bit / 32] |= 1u << (fld.bit % 32);
            if constexpr (std::is_same_v<T, bool> || std::is_same_v<T, std::optional<bool>>) {
                used[(fld.bit + 1) / 32] |= 1u << ((fld.bit + 1) % 32);
                m.bool_present[fld.bit / 32] |= 1u << (fld.bit % 32);
            }
        };
        (mark(f), ...);
    }, S::fields);
    for (std::size_t w = 0; w < S::words; ++w) m.reserved[w] = ~used[w];
    return m;
}

template <class D, bool Once = false> bool verify_table(Verifier& v, const std::uint8_t* p, std::uint32_t depth) noexcept;
template <class E, bool Once = false> bool verify_vector(Verifier& v, const std::uint8_t* t, std::uint32_t depth) noexcept;
template <class T, bool Once = false> bool verify_target(Verifier& v, const std::uint8_t* t, std::uint32_t depth) noexcept;

template <class U, bool Once> bool verify_union(Verifier& v, std::uint32_t tag, const std::uint8_t* t, std::uint32_t depth) noexcept {
    bool matched = false, ok = true;
    std::apply([&](const auto&... m) {
        ((m.tag == tag ? (matched = true, ok = verify_table<typename std::remove_cvref_t<decltype(m)>::type, Once>(v, t, depth)) : false), ...);
    }, schema<U>::members);
    return matched ? ok : v.fail(Error::BadUnionTag);
}

/// Optional values (C# int?, Vec3? elements): presence bits, none past the last element, then every element's value.
template <class X> bool verify_optional_values(Verifier& v, const std::uint8_t* t, std::uint32_t n) noexcept {
    constexpr std::uint32_t xs = std::is_same_v<X, bool> ? 1u : static_cast<std::uint32_t>(sizeof(X));
    const std::uint64_t words = (std::uint64_t{n} + 31) / 32;
    if (!v.range(t, 4 + 4 * words + std::uint64_t{n} * xs)) return false;
    if (n % 32 != 0 && (load32(t + 4 + 4 * (words - 1)) >> (n % 32)) != 0) return v.fail(Error::ReservedBits);
    if constexpr (alignof(X) >= 8) return v.aligned(t + 4 + 4 * words, 8);
    else return true;
}

template <class E, bool Once> bool verify_vector(Verifier& v, const std::uint8_t* t, std::uint32_t depth) noexcept {
    if constexpr (Once) {
        if (!v.own_visit()) return v.verify_once(t, reinterpret_cast<std::uintptr_t>(&once_key<vector_of<E>>), depth, &verify_vector<E, true>);
    }
    if (!v.enter(depth) || !v.range(t, 4)) return false;
    const std::uint32_t n = load32(t);
    constexpr Cat c = category<E>();
    [[maybe_unused]] constexpr std::uint32_t es = c == Cat::Union ? 8u : std::is_same_v<E, bool> ? 1u : (c == Cat::Scalar || c == Cat::Struct) ? static_cast<std::uint32_t>(sizeof(E)) : 4u;
    if constexpr (c == Cat::Optional) {
        return verify_optional_values<typename is_optional<E>::inner>(v, t, n);
    } else if (!v.range(t, 4 + std::uint64_t{n} * es)) {
        return false;
    } else if constexpr (c == Cat::Scalar || c == Cat::Struct) {
        if constexpr (alignof(E) >= 8) return v.aligned(t + 4, 8);
        else return true;
    } else if constexpr (c == Cat::Union) {
        if (!v.refs(n)) return false;
        for (std::uint32_t i = 0; i < n; ++i) {
            const std::uint8_t* slot = t + 4 + std::size_t{i} * 8;
            if (load32(slot + 4) == 0) continue;
            const std::uint8_t* target = v.follow(slot + 4);
            if (!target || !verify_union<E, Once>(v, load32(slot), target, depth + 1)) return false;
        }
        return true;
    } else {
        if (!v.refs(n)) return false;
        for (std::uint32_t i = 0; i < n; ++i) {
            const std::uint8_t* slot = t + 4 + std::size_t{i} * 4;
            if (load32(slot) == 0) continue;
            const std::uint8_t* target = v.follow(slot);
            if (!target || !verify_target<E, Once>(v, target, depth + 1)) return false;
        }
        return true;
    }
}

template <class T, bool Once> bool verify_target(Verifier& v, const std::uint8_t* t, std::uint32_t depth) noexcept {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::String) return v.string(t);
    else if constexpr (c == Cat::Vector) return verify_vector<typename is_vector<T>::element, Once>(v, t, depth);
    else if constexpr (c == Cat::Table) return verify_table<T, Once>(v, t, depth);
    else if constexpr (c == Cat::Shared) return v.range(t, sizeof(typename is_shared<T>::inner));
    else return true;
}

template <class D, std::size_t I, bool Once> TESSERA_ALWAYS_INLINE bool verify_field(Verifier& v, const std::uint8_t* p, std::uint32_t depth) noexcept {
    using S = schema<D>;
    constexpr const auto& f = std::get<I>(S::fields);
    using T = typename std::remove_cvref_t<decltype(f)>::type;
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Scalar || c == Cat::Optional || c == Cat::Struct) {
        return true;
    } else {
        if (!((load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 1u)) return true;
        const std::uint8_t* cell = p + cell_offset<S, f.bit>(p);
        if constexpr (c == Cat::Union) {
            const std::uint8_t* t = v.follow(cell + 4);
            return t && verify_union<T, Once>(v, load32(cell), t, depth + 1);
        } else {
            const std::uint8_t* t = v.follow(cell);
            return t && verify_target<T, Once>(v, t, depth + 1);
        }
    }
}

template <class D, bool Once> bool verify_table(Verifier& v, const std::uint8_t* p, std::uint32_t depth) noexcept {
    using S = schema<D>;
    if constexpr (Once) {
        if (!v.own_visit()) return v.verify_once(p, reinterpret_cast<std::uintptr_t>(&once_key<D>), depth, &verify_table<D, true>);
    }
    if (!v.enter(depth) || !v.range(p, 4u * S::words)) return false;
    static constexpr auto masks = header_masks<S>();
    for (std::uint32_t w = 0; w < S::words; ++w) {
        const std::uint32_t word = load32(p + 4 * w);
        if ((word & masks.reserved[w]) != 0 || ((word >> 1) & ~word & masks.bool_present[w]) != 0) return v.fail(Error::ReservedBits);
    }
    constexpr std::uint32_t C = static_cast<std::uint32_t>(std::size(S::cells)) - 1;
    if (!v.range(p, cell_offset<S, C>(p))) return false;
    constexpr std::size_t N = std::tuple_size_v<std::remove_cvref_t<decltype(S::fields)>>;
    return [&]<std::size_t... I>(std::index_sequence<I...>) noexcept {
        return (verify_field<D, I, Once>(v, p, depth) && ...);
    }(std::make_index_sequence<N>{});
}

// Schema-driven verification for buffers written with a different schema version. Once = true verifies each item once.

template <bool Once = false> class EntryVerifier {
public:
    EntryVerifier(Verifier& v, const SchemaView& s) noexcept : v_(v), s_(s) {}

    bool object(std::uint16_t e, const std::uint8_t* p, std::uint32_t depth) noexcept {
        if constexpr (Once) {
            // Keys: 2e + 2 for objects, 2e + 3 for vectors (never an address, never the verifier's string key).
            if (!v_.own_visit()) return v_.verify_once_with(p, 2 * std::uintptr_t{e} + 2, depth, [&]() noexcept { return object(e, p, depth); });
        }
        const Entry E = s_.entry(e);
        if (!v_.enter(depth) || !v_.range(p, 4u * E.words())) return false;
        const std::uint32_t X = E.fixed_count(), C = E.cell_count(), B = E.count() - X - C, bs = E.bool_start();
        for (std::uint32_t w = 0; w < E.words(); ++w) {
            std::uint32_t used = 0, present = 0;
            for (std::uint32_t b = 32 * w; b < 32 * w + 32; ++b) {
                const bool cellBit = b < C;
                const bool boolBit = b >= bs && b < bs + 2 * B;
                if (cellBit || boolBit) used |= 1u << (b % 32);
                if (boolBit && ((b - bs) & 1u) == 0) present |= 1u << (b % 32);
            }
            const std::uint32_t word = load32(p + 4 * w);
            if ((word & ~used) != 0 || ((word >> 1) & ~word & present) != 0) return v_.fail(Error::ReservedBits);
        }
        // Pass 1: the cells must fit. Pass 2: follow the references (fixed cells hold none).
        std::uint64_t end = 4u * E.words() + E.fixed_size();
        for (std::uint32_t j = 0; j < C; ++j)
            if ((load32(p + 4 * (j / 32)) >> (j % 32)) & 1u) end += s_.cell_size(E, X + j);
        if (!v_.range(p, end)) return false;
        std::uint64_t off = 4u * E.words() + E.fixed_size();
        for (std::uint32_t j = 0; j < C; ++j) {
            if (!((load32(p + 4 * (j / 32)) >> (j % 32)) & 1u)) continue;
            const std::uint8_t* cell = p + off;
            off += s_.cell_size(E, X + j);
            const Kind kind = E.field_kind(X + j);
            if (!is_ref_kind(kind)) continue;
            const std::uint16_t ref = E.field_ref(X + j);
            if (kind == Kind::Union) {
                const std::uint8_t* t = v_.follow(cell + 4);
                if (!t || !union_member(ref, load32(cell), t, depth + 1)) return false;
            } else {
                const std::uint8_t* t = v_.follow(cell);
                if (!t || !value(kind, ref, t, depth + 1)) return false;
            }
        }
        return true;
    }

private:
    bool union_member(std::uint16_t ref, std::uint32_t tag, const std::uint8_t* t, std::uint32_t depth) noexcept {
        if (ref == kNoType) return v_.fail(Error::BadSchema);
        const Entry U = s_.entry(ref);
        for (std::uint32_t m = 0; m < U.count(); ++m) {
            if (U.member_tag(m) != tag) continue;
            if (U.member_ref(m) == kNoType) return v_.fail(Error::BadUnionTag);
            return object(U.member_ref(m), t, depth);
        }
        return v_.fail(Error::BadUnionTag);
    }

    bool value(Kind kind, std::uint16_t ref, const std::uint8_t* t, std::uint32_t depth) noexcept {
        switch (kind) {
            case Kind::String: return v_.string(t);
            case Kind::SharedStruct: return v_.range(t, s_.entry(ref).struct_size());
            case Kind::Object: return ref != kNoType ? object(ref, t, depth) : v_.fail(Error::BadSchema);
            case Kind::Vector: return ref != kNoType ? vector(ref, t, depth) : v_.fail(Error::BadSchema);
            default: return v_.fail(Error::BadSchema);
        }
    }

    bool vector(std::uint16_t e, const std::uint8_t* t, std::uint32_t depth) noexcept {
        if constexpr (Once) {
            if (!v_.own_visit()) return v_.verify_once_with(t, 2 * std::uintptr_t{e} + 3, depth, [&]() noexcept { return vector(e, t, depth); });
        }
        const Entry V = s_.entry(e);
        if (!v_.enter(depth) || !v_.range(t, 4)) return false;
        const std::uint32_t n = load32(t);
        const std::uint32_t es = s_.elem_size(V);
        if (V.elem_optional()) {
            const std::uint64_t words = (std::uint64_t{n} + 31) / 32;
            if (!v_.range(t, 4 + 4 * words + std::uint64_t{n} * es)) return false;
            if (n % 32 != 0 && (load32(t + 4 + 4 * (words - 1)) >> (n % 32)) != 0) return v_.fail(Error::ReservedBits);
            return es < 8 || V.elem_kind() == Kind::Struct || v_.aligned(t + 4 + 4 * words, 8);
        }
        if (!v_.range(t, 4 + std::uint64_t{n} * es)) return false;
        const Kind k = V.elem_kind();
        if (!is_ref_kind(k)) return es < 8 || k == Kind::Struct || v_.aligned(t + 4, 8);
        if (!v_.refs(n)) return false;
        for (std::uint32_t i = 0; i < n; ++i) {
            if (k == Kind::Union) {
                const std::uint8_t* slot = t + 4 + std::size_t{i} * 8;
                if (load32(slot + 4) == 0) continue;
                const std::uint8_t* target = v_.follow(slot + 4);
                if (!target || !union_member(V.elem_ref(), load32(slot), target, depth + 1)) return false;
            } else {
                const std::uint8_t* slot = t + 4 + std::size_t{i} * 4;
                if (load32(slot) == 0) continue;
                const std::uint8_t* target = v_.follow(slot);
                if (!target || !value(k, V.elem_ref(), target, depth + 1)) return false;
            }
        }
        return true;
    }

    Verifier& v_;
    const SchemaView& s_;
};

/// Checks the 16-byte header; returns the root pointer or nullptr.
inline const std::uint8_t* check_header(const std::uint8_t* d, std::size_t size, Error& err) noexcept {
    if (d == nullptr || size < 16) { err = Error::TooSmall; return nullptr; }
    if (reinterpret_cast<std::uintptr_t>(d) & 7u) { err = Error::Misaligned; return nullptr; }
    if (load<std::uint16_t>(d + 4) != 0x5354) { err = Error::BadMagic; return nullptr; }
    if (d[6] != 2) { err = Error::BadVersion; return nullptr; }
    if (d[7] & ~1u) { err = Error::ReservedBits; return nullptr; }  // unknown flags: written by a newer writer
    const std::uint32_t root = load32(d);
    if (root < 16 || (root & 3u) || root >= size) { err = Error::BadOffset; return nullptr; }
    err = Error::None;
    return d + root;
}

} // namespace detail

// ---------------------------------------------------------------- Reader

/// Opens a buffer for reading. Cheap to create when the buffer was written with the same schema as the generated header;
/// otherwise the embedded schema is matched field by field once, and views use that binding. Views must not outlive the
/// Reader (or the buffer).
template <class Root> class Reader {
public:
    Reader() noexcept = default;

    Reader(const void* data, std::size_t size, const Options& options = {}) { open(static_cast<const std::uint8_t*>(data), size, options); }

    explicit operator bool() const noexcept { return error_ == Error::None; }
    [[nodiscard]] Error error() const noexcept { return error_; }

    /// The root table (an absent view if opening failed).
    [[nodiscard]] Root root() const noexcept { return error_ == Error::None ? Root(root_, binding_) : Root(); }

    /// True when the buffer was written with exactly this schema (all accesses use compile-time positions).
    [[nodiscard]] bool exact_schema() const noexcept { return state_ == nullptr; }

private:
    void open(const std::uint8_t* d, std::size_t size, const Options& options) {
        const std::uint8_t* root = detail::check_header(d, size, error_);
        if (!root) return;
        const std::uint64_t fp = detail::load<std::uint64_t>(d + 8);
        if (fp != schema<Root>::deep_fingerprint) {
            if (!(d[7] & 1u)) { error_ = Error::SchemaMismatch; return; }
            auto state = std::make_shared<detail::BindState>();
            if (!state->schema.parse(d, size)) { error_ = Error::BadSchema; return; }
            detail::Binder binder(*state);
            const detail::Binding* b = binder.bind(detail::type_info<Root>(), state->schema.root());
            if (b == detail::kIncompatible) { error_ = Error::SchemaMismatch; return; }
            binding_ = b;
            state_ = std::move(state);
        }
        if (options.verify) {
            detail::Verifier v(d, size, options);
            bool ok = !detail::kVerifyOnceAlways && (state_ ? detail::EntryVerifier<>(v, state_->schema).object(state_->schema.root(), root, 0)
                                                            : detail::verify_table<Root>(v, root, 0));
            if (!ok && (detail::kVerifyOnceAlways || v.shared())) ok = verify_once(v, root);
            if (!ok) { error_ = v.error(); return; }
        }
        root_ = root;
    }

    /// The buffer shares items: verify each (position, type) once.
    TESSERA_NOINLINE bool verify_once(detail::Verifier& v, const std::uint8_t* root) const noexcept {
        if (!v.start_once()) return false;
        return state_ ? detail::EntryVerifier<true>(v, state_->schema).object(state_->schema.root(), root, 0) : detail::verify_table<Root, true>(v, root, 0);
    }

    const std::uint8_t* root_ = detail::kEmpty;
    const detail::Binding* binding_ = nullptr;
    Error error_ = Error::TooSmall;
    std::shared_ptr<detail::BindState> state_;
};

/// Verifies a buffer without keeping a Reader.
template <class Root> [[nodiscard]] Error verify(const void* data, std::size_t size, Options options = {}) {
    options.verify = true;
    return Reader<Root>(data, size, options).error();
}

/// Root view without any checks. Only for trusted buffers written with exactly this schema.
template <class Root> [[nodiscard]] TESSERA_ALWAYS_INLINE Root root_unchecked(const void* data) noexcept {
    const auto* d = static_cast<const std::uint8_t*>(data);
    return Root(d + detail::load32(d), nullptr);
}

// ---------------------------------------------------------------- JSON (debugging and cross-language tests)

namespace detail {

inline void json_string(std::string& out, std::string_view s) {
    out += '"';
    for (char ch : s) {
        const auto c = static_cast<unsigned char>(ch);
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            default:
                if (c < 0x20) {
                    static constexpr char hex[] = "0123456789abcdef";
                    out += "\\u00";
                    out += hex[c >> 4];
                    out += hex[c & 15];
                } else {
                    out += ch;
                }
        }
    }
    out += '"';
}

/// Shortest decimal text that reads back to the same value; NaN and infinities as strings (JSON has no literal for them).
template <class F> void json_float(std::string& out, F v) {
    if (v != v) { out += "\"NaN\""; return; }
    if (v == std::numeric_limits<F>::infinity()) { out += "\"Infinity\""; return; }
    if (v == -std::numeric_limits<F>::infinity()) { out += "\"-Infinity\""; return; }
    char text[32];
    const auto r = std::to_chars(text, text + sizeof(text), v);
    out.append(text, r.ptr);
}

template <class T> void json_value(std::string& out, const T& v);

template <class D> void json_table(std::string& out, const Table<D>& t);

template <class T> void json_scalar(std::string& out, T v) {
    if constexpr (std::is_same_v<T, bool>) out += v ? "true" : "false";
    else if constexpr (std::is_floating_point_v<T>) json_float(out, v);
    else if constexpr (std::is_enum_v<T>) json_scalar(out, static_cast<std::underlying_type_t<T>>(v));
    else if constexpr (std::is_same_v<T, char16_t>) out += std::to_string(static_cast<unsigned>(v));
    else if constexpr (sizeof(T) == 1) out += std::to_string(static_cast<int>(v));
    else out += std::to_string(v);
}

template <class S> void json_struct(std::string& out, const S& s) {
    out += '{';
    bool first = true;
    std::apply([&](const auto&... f) {
        ((out += first ? "" : ",", first = false, json_string(out, f.name), out += ':', json_value(out, s.*(f.member))), ...);
    }, schema<S>::fields);
    out += '}';
}

template <class T> void json_value(std::string& out, const T& v) {
    if constexpr (std::is_same_v<T, std::string_view>) json_string(out, v);
    else if constexpr (is_scalar_v<T>) json_scalar(out, v);
    else if constexpr (is_optional<T>::value) { if (v) json_value(out, *v); else out += "null"; }
    else if constexpr (is_vector<T>::value) {
        using E = typename is_vector<T>::element;
        out += '[';
        for (std::uint32_t i = 0; i < v.size(); ++i) {
            if (i) out += ',';
            constexpr Cat c = category<E>();
            if constexpr (c == Cat::Table || c == Cat::Vector || c == Cat::Union) {
                auto e = v[i];
                if (!e) { out += "null"; continue; }
                json_value(out, e);
            } else if constexpr (c == Cat::String) {
                auto e = v[i];
                if (e.data() == nullptr) { out += "null"; continue; }
                json_string(out, e);
            } else {
                json_value(out, v[i]);
            }
        }
        out += ']';
    } else if constexpr (std::is_base_of_v<TableBase, T>) json_table(out, v);
    else if constexpr (std::is_base_of_v<UnionBase, T>) {
        bool done = false;
        std::apply([&](const auto&... m) {
            ((!done && v.tag() == m.tag ? (done = true, out += "{\"$type\":", json_string(out, schema<typename std::remove_cvref_t<decltype(m)>::type>::name),
                out += ",\"$value\":", json_table(out, v.template tessera_as<typename std::remove_cvref_t<decltype(m)>::type>(m.tag)), out += '}', 0) : 0), ...);
        }, schema<T>::members);
        if (!done) out += "null";
    } else json_struct(out, v);
}

template <class D> void json_table(std::string& out, const Table<D>& t) {
    using S = schema<D>;
    constexpr std::size_t N = std::tuple_size_v<std::remove_cvref_t<decltype(S::fields)>>;
    // Fields in name order so the output is canonical.
    std::vector<std::pair<std::string_view, std::string>> parts;
    parts.reserve(N);
    [&]<std::size_t... I>(std::index_sequence<I...>) {
        ((t.template tessera_has<I>() ? (parts.emplace_back(std::get<I>(S::fields).name, std::string()), json_value(parts.back().second, t.template tessera_get<I>()), 0) : 0), ...);
    }(std::make_index_sequence<N>{});
    std::sort(parts.begin(), parts.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
    out += '{';
    for (std::size_t i = 0; i < parts.size(); ++i) {
        if (i) out += ',';
        json_string(out, parts[i].first);
        out += ':';
        out += parts[i].second;
    }
    out += '}';
}

} // namespace detail

/// Canonical JSON of a view (fields sorted by wire name, absent fields omitted, floats as hex bit patterns).
template <class T> [[nodiscard]] std::string to_json(const T& value) {
    std::string out;
    detail::json_value(out, value);
    return out;
}



} // namespace tessera
