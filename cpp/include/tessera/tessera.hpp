// Tessera C++20 runtime - zero-copy views over buffers written by the Tessera .NET writer.
// Header-only. Generated headers (one per C# model assembly) include this file.
//
// Copyright (c) Tessera contributors. Licensed under the Apache License, Version 2.0.
#pragma once

#include <algorithm>
#include <array>
#include <bit>
#include <charconv>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iterator>
#include <limits>
#include <memory>
#include <new>
#include <optional>
#include <string>
#include <string_view>
#include <tuple>
#include <type_traits>
#include <utility>
#include <vector>

#if defined(_MSC_VER) && !defined(__clang__)
#define TESSERA_ALWAYS_INLINE __forceinline
#define TESSERA_NOINLINE __declspec(noinline)
#define TESSERA_MSVC_FORCEINLINE __forceinline  // where MSVC needs it and GCC compiles worse with it (measured)
#else
#define TESSERA_ALWAYS_INLINE inline __attribute__((always_inline))
#define TESSERA_NOINLINE __attribute__((noinline))
#define TESSERA_MSVC_FORCEINLINE inline
#endif
// Inline with GCC, out of line with MSVC and Clang: for a heavy, optional path in a hot function. GCC saves registers
// only on the paths that use them; MSVC saves them in the prologue of every call, and Clang ran the light path faster
// with the heavy one out of line (measured).
#if defined(__GNUC__) && !defined(__clang__)
#define TESSERA_GCC_INLINE TESSERA_ALWAYS_INLINE
#else
#define TESSERA_GCC_INLINE TESSERA_NOINLINE
#endif

/// Generated accessors are one-line wrappers; force inlining so every compiler sees the whole field read.
#define TESSERA_INLINE TESSERA_ALWAYS_INLINE

namespace tessera {

static_assert(std::endian::native == std::endian::little, "Tessera buffers are little-endian; big-endian hosts are not supported.");

/// Wire kinds (must match WireFormat.cs).
enum class Kind : std::uint8_t {
    Invalid = 0, Bool = 1, Int8 = 2, UInt8 = 3, Int16 = 4, UInt16 = 5, Int32 = 6, UInt32 = 7, Int64 = 8, UInt64 = 9,
    Float32 = 10, Float64 = 11, Char16 = 12, Struct = 13, String = 14, Object = 15, Vector = 16, Union = 17, SharedStruct = 18,
};

/// Result of opening or verifying a buffer.
enum class Error : std::uint8_t {
    None = 0,
    TooSmall,        // shorter than the 16-byte header
    BadMagic,        // not a Tessera buffer
    BadVersion,      // written by an unsupported format version
    SchemaMismatch,  // fingerprint differs and the buffer carries no schema
    BadSchema,       // schema section malformed
    OutOfBounds,     // an item extends past the end of the buffer
    BadOffset,       // an offset is zero, backward or misaligned
    BadString,       // missing terminator or invalid UTF-8
    BadUnionTag,     // union tag not declared by the schema
    ReservedBits,    // header bits that must be zero are set
    TooDeep,         // nesting deeper than Options::max_depth
    TooManyItems,    // more than Options::max_items objects/vectors verified
    Misaligned,      // buffer start is not 8-byte aligned
};

[[nodiscard]] constexpr const char* to_string(Error e) noexcept {
    switch (e) {
        case Error::None: return "ok";
        case Error::TooSmall: return "buffer too small";
        case Error::BadMagic: return "not a Tessera buffer";
        case Error::BadVersion: return "unsupported format version";
        case Error::SchemaMismatch: return "schema mismatch and no embedded schema";
        case Error::BadSchema: return "malformed schema section";
        case Error::OutOfBounds: return "item out of bounds";
        case Error::BadOffset: return "invalid offset";
        case Error::BadString: return "invalid string";
        case Error::BadUnionTag: return "unknown union tag";
        case Error::ReservedBits: return "reserved header bits set";
        case Error::TooDeep: return "nesting too deep";
        case Error::TooManyItems: return "too many items";
        case Error::Misaligned: return "buffer is not 8-byte aligned";
    }
    return "unknown error";
}

/// Settings for opening a buffer.
struct Options {
    bool verify = true;                  // walk and bounds-check the whole buffer before use
    bool utf8 = false;                   // also validate UTF-8 in strings (when verifying)
    std::uint32_t max_depth = 128;       // nesting limit: objects and vectors (same default as the C# MaxDepth)
    std::uint32_t max_items = 1u << 24;  // objects/vectors verified (a shared item is verified once)
};

/// Options that skip verification: only for buffers you produced yourself.
inline constexpr Options trusted{.verify = false};

template <class T> struct schema;          // specialized by generated headers
template <class T> struct shared {};       // field marker: struct stored once behind an offset
struct none_t {};

class TableBase;
class UnionBase;
template <class T> class Vector;

namespace detail {

template <class T> [[nodiscard]] TESSERA_ALWAYS_INLINE T load(const std::uint8_t* p) noexcept {
    T v;
    std::memcpy(&v, p, sizeof(T));
    return v;
}
[[nodiscard]] TESSERA_ALWAYS_INLINE std::uint32_t load32(const std::uint8_t* p) noexcept { return load<std::uint32_t>(p); }

/// Zero bytes standing in for absent objects, vectors and strings, so views never hold null pointers.
alignas(8) inline constexpr std::uint8_t kEmpty[2048] = {};

[[nodiscard]] TESSERA_ALWAYS_INLINE std::string_view string_at(const std::uint8_t* s) noexcept {
    return {reinterpret_cast<const char*>(s + 4), load32(s)};
}

// ---------------------------------------------------------------- type traits

template <class T> struct is_optional : std::false_type {};
template <class U> struct is_optional<std::optional<U>> : std::true_type { using inner = U; };
template <class T> struct is_vector : std::false_type {};
template <class E> struct is_vector<Vector<E>> : std::true_type { using element = E; };
template <class T> struct is_shared : std::false_type {};
template <class S> struct is_shared<shared<S>> : std::true_type { using inner = S; };

template <class T> inline constexpr bool is_scalar_v = std::is_arithmetic_v<T> || std::is_enum_v<T> || std::is_same_v<T, char16_t>;

enum class Cat : std::uint8_t { Scalar, Optional, String, Vector, Table, Union, Struct, Shared };

template <class T> consteval Cat category() {
    if constexpr (is_optional<T>::value) return Cat::Optional;
    else if constexpr (is_scalar_v<T>) return Cat::Scalar;
    else if constexpr (std::is_same_v<T, std::string_view>) return Cat::String;
    else if constexpr (is_vector<T>::value) return Cat::Vector;
    else if constexpr (is_shared<T>::value) return Cat::Shared;
    else if constexpr (std::is_base_of_v<TableBase, T>) return Cat::Table;
    else if constexpr (std::is_base_of_v<UnionBase, T>) return Cat::Union;
    else {
        static_assert(std::is_trivially_copyable_v<T>, "inline struct fields must be trivially copyable");
        return Cat::Struct;
    }
}

template <class T> consteval Kind scalar_kind() {
    if constexpr (std::is_enum_v<T>) return scalar_kind<std::underlying_type_t<T>>();
    else if constexpr (std::is_same_v<T, bool>) return Kind::Bool;
    else if constexpr (std::is_same_v<T, char16_t>) return Kind::Char16;
    else if constexpr (std::is_same_v<T, float>) return Kind::Float32;
    else if constexpr (std::is_same_v<T, double>) return Kind::Float64;
    else if constexpr (sizeof(T) == 1) return std::is_signed_v<T> ? Kind::Int8 : Kind::UInt8;
    else if constexpr (sizeof(T) == 2) return std::is_signed_v<T> ? Kind::Int16 : Kind::UInt16;
    else if constexpr (sizeof(T) == 4) return std::is_signed_v<T> ? Kind::Int32 : Kind::UInt32;
    else return std::is_signed_v<T> ? Kind::Int64 : Kind::UInt64;
}

/// Value type a field or element of declared type T reads as.
template <class T, Cat C = category<T>()> struct value_of { using type = T; };
template <class T> struct value_of<T, Cat::Shared> { using type = typename is_shared<T>::inner; };

template <class T> consteval Kind wire_kind() {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Optional) return wire_kind<typename is_optional<T>::inner>();
    else if constexpr (c == Cat::Scalar) return scalar_kind<T>();
    else if constexpr (c == Cat::String) return Kind::String;
    else if constexpr (c == Cat::Vector) return Kind::Vector;
    else if constexpr (c == Cat::Table) return Kind::Object;
    else if constexpr (c == Cat::Union) return Kind::Union;
    else if constexpr (c == Cat::Shared) return Kind::SharedStruct;
    else return Kind::Struct;
}

template <class T> consteval std::uint32_t cell_size() {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Optional) return cell_size<typename is_optional<T>::inner>();
    else if constexpr (std::is_same_v<T, bool>) return 0;
    else if constexpr (c == Cat::Scalar || c == Cat::Struct) return sizeof(T);
    else if constexpr (c == Cat::Union) return 8;
    else return 4;
}

template <class T, Cat C = category<T>()> struct default_of { using type = none_t; };
template <class T> struct default_of<T, Cat::Scalar> { using type = T; };

} // namespace detail

/// Field "bits" from kFixedBit up mark fixed cells: always stored, at byte bit - kFixedBit after the header words.
inline constexpr std::uint16_t kFixedBit = 0x8000;
[[nodiscard]] constexpr std::uint16_t fixed_at(std::uint32_t offset) noexcept { return static_cast<std::uint16_t>(kFixedBit + offset); }

/// Generated schemas describe each table field with one of these (wire order).
template <class T> struct field {
    using type = T;
    std::string_view name;   // C# wire name
    std::uint64_t hash;      // xxHash64 of the wire name
    std::uint16_t bit;       // presence bit (bools: present bit; the value bit follows)
    typename detail::default_of<T>::type def{};
};

/// Generated union schemas list their members with one of these.
template <class T> struct member {
    using type = T;
    std::uint32_t tag;
};

/// Generated struct schemas describe each member with one of these (declaration order).
template <class S, class M> struct struct_field {
    using type = M;
    std::string_view name;
    M S::*member;
};

namespace detail {

// ---------------------------------------------------------------- bindings (reading buffers from other schema versions)

struct TypeInfo;
using TypeInfoFn = const TypeInfo& (*)() noexcept;

struct FieldInfo {
    std::string_view name;
    std::uint64_t hash;
    std::uint16_t bit;
    Kind kind;
    std::uint32_t cell;          // cell size (0 for bool)
    std::uint64_t struct_fp;     // struct fingerprint for Struct/SharedStruct kinds
    std::uint32_t struct_size;   // sizeof the struct for Struct/SharedStruct kinds
    TypeInfoFn child;            // objects, vectors, unions
};

struct TypeInfo {
    Kind kind = Kind::Invalid;   // Object, Vector, Union
    std::string_view name;
    std::uint64_t fingerprint = 0;
    std::uint64_t deep = 0;
    // objects
    std::uint32_t words = 0;
    std::uint32_t fixed = 0;                // bytes of fixed cells
    std::uint32_t cell_count = 0;           // cells with presence bits
    const FieldInfo* fields = nullptr;
    std::uint32_t field_count = 0;
    const std::uint16_t* cells = nullptr;   // cell sizes in bit order
    // vectors
    Kind elem_kind = Kind::Invalid;
    bool elem_optional = false;             // optional values: presence bits, then every value
    std::uint32_t elem_size = 0;
    std::uint64_t elem_struct_fp = 0;
    std::uint32_t elem_struct_size = 0;
    TypeInfoFn elem = nullptr;
    // unions
    const std::uint32_t* tags = nullptr;
    const TypeInfoFn* members = nullptr;
    std::uint32_t member_count = 0;
};

struct Term {
    std::uint16_t word;
    std::uint16_t size;
    std::uint32_t mask;
};

struct Binding;

inline constexpr std::uint16_t kFixedBinding = 0xFFFE;

struct FieldBinding {
    std::uint16_t bit = 0xFFFF;   // writer presence bit; 0xFFFF: the writer lacks the field; kFixedBinding: a fixed cell
    std::uint16_t term_count = 0;
    std::uint32_t term_start = 0; // first term; for a fixed cell, its position in the table
    const Binding* child = nullptr;
};

struct Binding {
    std::uint32_t words = 1;
    std::uint32_t fixed = 0;      // the writer's bytes of fixed cells
    bool own_identical = false;
    std::vector<FieldBinding> fields;   // indexed by reader field index
    std::vector<Term> terms;
    const Binding* element = nullptr;   // vectors
    std::vector<std::pair<std::uint32_t, const Binding*>> members;   // unions: tag -> member binding

    [[nodiscard]] const Binding* member(std::uint32_t tag) const noexcept {
        for (const auto& m : members)
            if (m.first == tag) return m.second;
        return nullptr;
    }
};

/// Marks a member whose writer layout cannot be read with the reader's type (reads as absent).
inline const Binding kIncompatibleBinding{};
inline const Binding* const kIncompatible = &kIncompatibleBinding;

// ---------------------------------------------------------------- cell addressing

/// Run-length terms "size x popcount(word & mask)" whose sum is the cell offset of presence bit `Bit`.
template <std::size_t N> struct Terms {
    Term t[N == 0 ? 1 : N]{};
    std::size_t n = 0;
};

template <class S> consteval std::size_t term_count(std::uint32_t bit) {
    std::size_t n = 0;
    for (std::uint32_t j = 0; j < bit;) {
        const std::uint32_t start = j, size = S::cells[j];
        while (j < bit && S::cells[j] == size && j / 32 == start / 32) ++j;
        ++n;
    }
    return n;
}

template <class S, std::uint32_t Bit> consteval auto make_terms() {
    Terms<term_count<S>(Bit)> r{};
    for (std::uint32_t j = 0; j < Bit;) {
        const std::uint32_t start = j, size = S::cells[j];
        while (j < Bit && S::cells[j] == size && j / 32 == start / 32) ++j;
        const std::uint32_t lo = start % 32, hi = (j - 1) % 32;
        const std::uint32_t mask = (hi == 31 ? 0xFFFFFFFFu : ((1u << (hi + 1)) - 1u)) & ~((1u << lo) - 1u);
        r.t[r.n++] = Term{static_cast<std::uint16_t>(start / 32), static_cast<std::uint16_t>(size), mask};
    }
    return r;
}

template <class S, std::uint32_t Bit, std::size_t K> TESSERA_ALWAYS_INLINE std::uint32_t cell_term(const std::uint8_t* p) noexcept {
    // Locals declared constexpr so every compiler emits the word offset, mask and size as immediates.
    constexpr Term t = make_terms<S, Bit>().t[K];
    constexpr std::uint32_t word = 4u * t.word, mask = t.mask, size = t.size;
    const auto n = static_cast<std::uint32_t>(std::popcount(load32(p + word) & mask));
    if constexpr (size == 1) return n;
    else if constexpr ((size & (size - 1)) == 0) {
        constexpr int shift = std::countr_zero(size);
        return n << shift;
    }
    else return n * size;
}

/// Byte offset (from the table start) of the cell of presence bit `Bit`: header and fixed cells, plus the sizes of all
/// present cells before it, computed with one popcount per run of equal-size cells.
template <class S, std::uint32_t Bit> TESSERA_ALWAYS_INLINE std::uint32_t cell_offset(const std::uint8_t* p) noexcept {
    constexpr std::size_t n = term_count<S>(Bit);
    return [&]<std::size_t... K>(std::index_sequence<K...>) noexcept {
        return 4u * S::words + S::fixed + (0u + ... + cell_term<S, Bit, K>(p));
    }(std::make_index_sequence<n>{});
}

inline const std::uint8_t* slow_cell(const std::uint8_t* p, const Binding* b, std::size_t i) noexcept {
    const FieldBinding& f = b->fields[i];
    if (f.bit == kFixedBinding) return p == kEmpty ? nullptr : p + f.term_start;
    if (p == kEmpty || f.bit == 0xFFFF || !((load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 1u)) return nullptr;
    std::uint32_t off = 4u * b->words + b->fixed;
    for (std::uint32_t k = 0; k < f.term_count; ++k) {
        const Term& t = b->terms[f.term_start + k];
        off += t.size * static_cast<std::uint32_t>(std::popcount(load32(p + 4u * t.word) & t.mask));
    }
    return p + off;
}

inline std::uint32_t slow_bool(const std::uint8_t* p, const Binding* b, std::size_t i) noexcept {
    const FieldBinding& f = b->fields[i];
    if (p == kEmpty || f.bit == 0xFFFF) return 0;
    return (load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 3u;
}

template <class T, Cat C = category<T>()> struct return_of;
template <class T> using return_t = typename return_of<T>::type;
template <class T> return_t<T> read_field(const std::uint8_t* cell, const Binding* child, const typename default_of<T>::type& def) noexcept;
template <class T> return_t<T> read_present(const std::uint8_t* cell, const Binding* child) noexcept;
template <class T> return_t<T> read_absent(const typename default_of<T>::type& def) noexcept;

template <class M> consteval bool member_has_bool();

/// True when an inline struct contains a bool (directly or in a nested struct).
template <class S> consteval bool struct_has_bool() {
    return std::apply([](const auto&... f) { return (false || ... || member_has_bool<typename std::remove_cvref_t<decltype(f)>::type>()); }, schema<S>::fields);
}

template <class M> consteval bool member_has_bool() {
    if constexpr (std::is_same_v<M, bool>) return true;
    else if constexpr (category<M>() == Cat::Struct) return struct_has_bool<M>();
    else return false;
}

/// Rewrites bool members from the source bytes as 0/1, so a struct copied from untrusted bytes never holds an invalid bool.
template <class S> void fix_bools(S& s, const std::uint8_t* p) noexcept {
    std::apply([&](const auto&... f) {
        ([&](const auto& fld) {
            using M = typename std::remove_cvref_t<decltype(fld)>::type;
            const auto off = reinterpret_cast<const unsigned char*>(&(s.*(fld.member))) - reinterpret_cast<const unsigned char*>(&s);
            if constexpr (std::is_same_v<M, bool>) s.*(fld.member) = p[off] != 0;
            else if constexpr (category<M>() == Cat::Struct) {
                if constexpr (struct_has_bool<M>()) fix_bools(s.*(fld.member), p + off);
            }
        }(f), ...);
    }, schema<S>::fields);
}

/// Zero value returned (by reference) for absent struct fields.
template <class S> inline constexpr S kZero{};

/// What reading a field or element of declared type T returns: inline structs without bool members are returned by
/// reference into the buffer (no copy, like FlatBuffers); everything else by value.
template <class T, Cat C> struct return_of { using type = typename value_of<T>::type; };
template <class T> struct return_of<T, Cat::Struct> { using type = std::conditional_t<struct_has_bool<T>(), T, const T&>; };
template <class T> struct return_of<T, Cat::Shared> {
    using S = typename is_shared<T>::inner;
    using type = std::conditional_t<struct_has_bool<S>(), S, const S&>;
};

/// Copies a scalar or inline struct out of the buffer.
template <class T> TESSERA_ALWAYS_INLINE T load_value(const std::uint8_t* p) noexcept {
    if constexpr (category<T>() == Cat::Struct) {
        if constexpr (struct_has_bool<T>()) {
            T v;
            std::memcpy(static_cast<void*>(&v), p, sizeof(T));
            fix_bools(v, p);
            return v;
        } else {
            return load<T>(p);
        }
    } else {
        return load<T>(p);
    }
}
template <class T> return_t<T> read_element(const std::uint8_t* base, std::uint32_t i, const Binding* eb) noexcept;

} // namespace detail

// ---------------------------------------------------------------- tables

/// Base of every generated table view. A view is two pointers; absent tables are views over zero bytes.
class TableBase {
public:
    constexpr TableBase() noexcept = default;
    TableBase(const std::uint8_t* p, const detail::Binding* b) noexcept : p_(p), b_(b) {}

    /// False when the table is absent.
    explicit operator bool() const noexcept { return p_ != detail::kEmpty; }

    /// Address of the table in the buffer.
    [[nodiscard]] const std::uint8_t* tessera_data() const noexcept { return p_; }
    [[nodiscard]] const detail::Binding* tessera_binding() const noexcept { return b_; }

protected:
    const std::uint8_t* p_ = detail::kEmpty;
    const detail::Binding* b_ = nullptr;
};

/// CRTP helper that implements field access from `tessera::schema<D>`.
template <class D> class Table : public TableBase {
public:
    using TableBase::TableBase;

    /// Value of field I (index into schema<D>::fields).
    template <std::size_t I> [[nodiscard]] TESSERA_ALWAYS_INLINE decltype(auto) tessera_get() const noexcept;

    /// Whether field I is present.
    template <std::size_t I> [[nodiscard]] TESSERA_ALWAYS_INLINE bool tessera_has() const noexcept;

};


namespace detail {

/// Whether a fixed field's default is all zero bytes, so an absent table (kEmpty) reads it back without a test.
template <class F> consteval bool zero_default(const F& f) {
    using T = typename F::type;
    if constexpr (category<T>() == Cat::Scalar) {
        using B = std::conditional_t<sizeof(T) == 1, std::uint8_t, std::conditional_t<sizeof(T) == 2, std::uint16_t, std::conditional_t<sizeof(T) == 4, std::uint32_t, std::uint64_t>>>;
        return std::bit_cast<B>(f.def) == 0;
    } else {
        return true;  // inline structs default to zero
    }
}

/// Reads field I of a table at `p` laid out exactly as schema<D> says (compile-time cell position).
template <class D, std::size_t I> TESSERA_ALWAYS_INLINE decltype(auto) read_exact(const std::uint8_t* p, const Binding* child) noexcept {
    using S = schema<D>;
    constexpr const auto& f = std::get<I>(S::fields);
    using T = typename std::remove_cvref_t<decltype(f)>::type;
    if constexpr (f.bit >= kFixedBit) {
        static_assert(4u * S::words + S::fixed <= sizeof(kEmpty), "an absent table's fixed cells are read from kEmpty");
        const std::uint8_t* cell = p + (4u * S::words + (f.bit - kFixedBit));
        if constexpr (zero_default(f)) return read_present<T>(cell, child);
        else return p != kEmpty ? read_present<T>(cell, child) : read_absent<T>(f.def);
    } else {
        const std::uint32_t w = load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u);
        if constexpr (std::is_same_v<T, bool>) {
            return (w & 1u) ? ((w >> 1) & 1u) != 0 : f.def;
        } else if constexpr (std::is_same_v<T, std::optional<bool>>) {
            return (w & 1u) ? std::optional<bool>(((w >> 1) & 1u) != 0) : std::optional<bool>();
        } else {
            if (!(w & 1u)) return read_absent<T>(f.def);
            return read_present<T>(p + cell_offset<S, f.bit>(p), child);
        }
    }
}

}  // namespace detail

namespace detail {

// Buffers written with another schema version. Kept out of line, taking the view's pointers by value (not 'this') and
// returning only a cell pointer or two bits, so the fast path stays small, views stay in registers, and values are never
// merged through memory.
template <class D, std::size_t I> TESSERA_NOINLINE const std::uint8_t* bound_cell(const std::uint8_t* p, const Binding* b) noexcept {
    using S = schema<D>;
    constexpr const auto& f = std::get<I>(S::fields);
    if constexpr (f.bit >= kFixedBit) {
        if (b->own_identical) return p != kEmpty ? p + (4u * S::words + (f.bit - kFixedBit)) : nullptr;
    } else {
        if (b->own_identical) return ((load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 1u) ? p + cell_offset<S, f.bit>(p) : nullptr;
    }
    return slow_cell(p, b, I);
}

template <class D, std::size_t I> TESSERA_NOINLINE std::uint32_t bound_bits(const std::uint8_t* p, const Binding* b) noexcept {
    constexpr const auto& f = std::get<I>(schema<D>::fields);
    if (b->own_identical) return (load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 3u;
    return slow_bool(p, b, I);
}

template <class D, std::size_t I> TESSERA_NOINLINE bool has_bound(const std::uint8_t* p, const Binding* b) noexcept {
    constexpr const auto& f = std::get<I>(schema<D>::fields);
    if constexpr (f.bit >= kFixedBit) {
        if (b->own_identical) return p != kEmpty;
    } else {
        if (b->own_identical) return (load32(p + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 1u;
    }
    const FieldBinding& fb = b->fields[I];
    if (fb.bit == kFixedBinding) return p != kEmpty;
    return fb.bit != 0xFFFF && p != kEmpty && ((load32(p + 4u * (fb.bit / 32u)) >> (fb.bit % 32u)) & 1u);
}

}  // namespace detail

template <class D>
template <std::size_t I>
TESSERA_ALWAYS_INLINE decltype(auto) Table<D>::tessera_get() const noexcept {
    if (b_ == nullptr) [[likely]] return detail::read_exact<D, I>(p_, nullptr);
    constexpr const auto& f = std::get<I>(schema<D>::fields);
    using T = typename std::remove_cvref_t<decltype(f)>::type;
    if constexpr (std::is_same_v<T, bool>) {
        const std::uint32_t two = detail::bound_bits<D, I>(p_, b_);
        return (two & 1u) ? (two >> 1) != 0 : f.def;
    } else if constexpr (std::is_same_v<T, std::optional<bool>>) {
        const std::uint32_t two = detail::bound_bits<D, I>(p_, b_);
        return (two & 1u) ? std::optional<bool>((two >> 1) != 0) : std::optional<bool>();
    } else {
        return detail::read_field<T>(detail::bound_cell<D, I>(p_, b_), b_->fields[I].child, f.def);
    }
}

template <class D>
template <std::size_t I>
TESSERA_ALWAYS_INLINE bool Table<D>::tessera_has() const noexcept {
    constexpr const auto& f = std::get<I>(schema<D>::fields);
    if constexpr (f.bit >= kFixedBit) {
        if (b_ == nullptr) [[likely]] return p_ != detail::kEmpty;  // fixed cells are always stored
    } else {
        if (b_ == nullptr) [[likely]] return (detail::load32(p_ + 4u * (f.bit / 32u)) >> (f.bit % 32u)) & 1u;
    }
    return detail::has_bound<D, I>(p_, b_);
}
// ---------------------------------------------------------------- unions

/// Base of generated union views: a type tag plus the selected table.
class UnionBase {
public:
    constexpr UnionBase() noexcept = default;
    UnionBase(std::uint32_t tag, const std::uint8_t* p, const detail::Binding* b) noexcept : p_(p), tag_(tag), b_(b) {}

    /// Type tag of the stored table (0 when absent).
    [[nodiscard]] std::uint32_t tag() const noexcept { return tag_; }
    explicit operator bool() const noexcept { return tag_ != 0; }

    /// The stored table viewed as T, or an absent T if the tag differs.
    template <class T> [[nodiscard]] TESSERA_ALWAYS_INLINE T tessera_as(std::uint32_t tag) const noexcept {
        if (b_ == nullptr) [[likely]] return T(tag_ == tag ? p_ : detail::kEmpty, nullptr);
        return as_bound<T>(p_, tag_, b_, tag);
    }

private:
    template <class T> static TESSERA_NOINLINE T as_bound(const std::uint8_t* p, std::uint32_t stored, const detail::Binding* b, std::uint32_t tag) noexcept {
        if (stored != tag) return T{};
        const detail::Binding* mb = b->member(tag);
        if (mb == detail::kIncompatible) return T{};
        return T(p, mb);
    }

protected:

protected:
    const std::uint8_t* p_ = detail::kEmpty;
    std::uint32_t tag_ = 0;
    const detail::Binding* b_ = nullptr;
};

// ---------------------------------------------------------------- vectors

/// Read-only view of a vector. Elements are returned by value (views for strings/tables/vectors), inline structs by
/// reference into the buffer.
template <class T> class Vector {
public:
    using value_type = typename detail::value_of<T>::type;
    using reference = detail::return_t<T>;

    class iterator {
    public:
        using iterator_category = std::random_access_iterator_tag;
        using iterator_concept = std::random_access_iterator_tag;
        using value_type = typename Vector::value_type;
        using difference_type = std::ptrdiff_t;
        using reference = typename Vector::reference;
        using pointer = void;

        iterator() noexcept = default;
        iterator(const std::uint8_t* base, const detail::Binding* eb, std::uint32_t i) noexcept : base_(base), eb_(eb), i_(i) {}
        TESSERA_ALWAYS_INLINE reference operator*() const noexcept { return detail::read_element<T>(base_, i_, eb_); }
        reference operator[](difference_type n) const noexcept { return detail::read_element<T>(base_, static_cast<std::uint32_t>(i_ + n), eb_); }
        iterator& operator++() noexcept { ++i_; return *this; }
        iterator operator++(int) noexcept { iterator t = *this; ++i_; return t; }
        iterator& operator--() noexcept { --i_; return *this; }
        iterator operator--(int) noexcept { iterator t = *this; --i_; return t; }
        iterator& operator+=(difference_type n) noexcept { i_ = static_cast<std::uint32_t>(i_ + n); return *this; }
        iterator& operator-=(difference_type n) noexcept { i_ = static_cast<std::uint32_t>(i_ - n); return *this; }
        friend iterator operator+(iterator a, difference_type n) noexcept { return a += n; }
        friend iterator operator+(difference_type n, iterator a) noexcept { return a += n; }
        friend iterator operator-(iterator a, difference_type n) noexcept { return a -= n; }
        friend difference_type operator-(const iterator& a, const iterator& b) noexcept { return static_cast<difference_type>(a.i_) - static_cast<difference_type>(b.i_); }
        friend bool operator==(const iterator& a, const iterator& b) noexcept { return a.i_ == b.i_; }
        friend auto operator<=>(const iterator& a, const iterator& b) noexcept { return a.i_ <=> b.i_; }

    private:
        const std::uint8_t* base_ = nullptr;  // first element
        const detail::Binding* eb_ = nullptr;
        std::uint32_t i_ = 0;
    };

    constexpr Vector() noexcept = default;
    Vector(const std::uint8_t* p, const detail::Binding* b) noexcept : p_(p), b_(b) {}

    [[nodiscard]] std::uint32_t size() const noexcept { return detail::load32(p_); }
    [[nodiscard]] bool empty() const noexcept { return size() == 0; }
    /// False when the vector is absent (an empty vector is present).
    explicit operator bool() const noexcept { return p_ != detail::kEmpty; }

    /// Element i (no bounds check).
    [[nodiscard]] TESSERA_ALWAYS_INLINE reference operator[](std::uint32_t i) const noexcept {
        return detail::read_element<T>(p_ + 4, i, b_ ? b_->element : nullptr);
    }

    /// Element i, or a default value when out of range.
    [[nodiscard]] value_type at(std::uint32_t i) const noexcept { return i < size() ? (*this)[i] : value_type{}; }

    [[nodiscard]] iterator begin() const noexcept { return iterator(p_ + 4, b_ ? b_->element : nullptr, 0); }
    [[nodiscard]] iterator end() const noexcept { return iterator(p_ + 4, nullptr, size()); }

    /// Contiguous elements for scalar and inline-struct vectors (8-byte elements are 8-aligned when the buffer is).
    [[nodiscard]] const T* data() const noexcept
        requires((detail::category<T>() == detail::Cat::Scalar && !std::is_same_v<T, bool>) || (detail::category<T>() == detail::Cat::Struct && !detail::struct_has_bool<T>()))
    {
        return reinterpret_cast<const T*>(p_ + 4);
    }

private:
    const std::uint8_t* p_ = detail::kEmpty;
    const detail::Binding* b_ = nullptr;
};

/// Vector of optional values (C# `List<int?>`, `Vec3?[]`): a presence bit per element, then every element's value
/// (zero bytes when absent), so element i is still one load away.
template <class E> class Vector<std::optional<E>> {
public:
    using value_type = std::optional<typename detail::value_of<E>::type>;
    using reference = value_type;

    class iterator {
    public:
        using iterator_category = std::random_access_iterator_tag;
        using iterator_concept = std::random_access_iterator_tag;
        using value_type = typename Vector::value_type;
        using difference_type = std::ptrdiff_t;
        using reference = value_type;
        using pointer = void;

        iterator() noexcept = default;
        iterator(const Vector* v, std::uint32_t i) noexcept : v_(v), i_(i) {}
        reference operator*() const noexcept { return (*v_)[i_]; }
        reference operator[](difference_type n) const noexcept { return (*v_)[static_cast<std::uint32_t>(i_ + n)]; }
        iterator& operator++() noexcept { ++i_; return *this; }
        iterator operator++(int) noexcept { iterator t = *this; ++i_; return t; }
        iterator& operator--() noexcept { --i_; return *this; }
        iterator operator--(int) noexcept { iterator t = *this; --i_; return t; }
        iterator& operator+=(difference_type n) noexcept { i_ = static_cast<std::uint32_t>(i_ + n); return *this; }
        iterator& operator-=(difference_type n) noexcept { i_ = static_cast<std::uint32_t>(i_ - n); return *this; }
        friend iterator operator+(iterator a, difference_type n) noexcept { return a += n; }
        friend iterator operator+(difference_type n, iterator a) noexcept { return a += n; }
        friend iterator operator-(iterator a, difference_type n) noexcept { return a -= n; }
        friend difference_type operator-(const iterator& a, const iterator& b) noexcept { return static_cast<difference_type>(a.i_) - static_cast<difference_type>(b.i_); }
        friend bool operator==(const iterator& a, const iterator& b) noexcept { return a.i_ == b.i_; }
        friend auto operator<=>(const iterator& a, const iterator& b) noexcept { return a.i_ <=> b.i_; }

    private:
        const Vector* v_ = nullptr;
        std::uint32_t i_ = 0;
    };

    constexpr Vector() noexcept = default;
    Vector(const std::uint8_t* p, const detail::Binding*) noexcept : p_(p) {}

    [[nodiscard]] std::uint32_t size() const noexcept { return detail::load32(p_); }
    [[nodiscard]] bool empty() const noexcept { return size() == 0; }
    /// False when the vector is absent (an empty vector is present).
    explicit operator bool() const noexcept { return p_ != detail::kEmpty; }

    /// Whether element i has a value (no bounds check).
    [[nodiscard]] bool has(std::uint32_t i) const noexcept { return (detail::load32(p_ + 4 + 4 * (i / 32)) >> (i % 32)) & 1u; }

    /// Element i (no bounds check).
    [[nodiscard]] TESSERA_ALWAYS_INLINE value_type operator[](std::uint32_t i) const noexcept {
        if (!has(i)) return std::nullopt;
        const std::uint8_t* values = p_ + 4 + 4 * ((size() + 31) / 32);
        if constexpr (std::is_same_v<E, bool>) return values[i] != 0;
        else return detail::load_value<E>(values + std::size_t{i} * sizeof(E));
    }

    [[nodiscard]] iterator begin() const noexcept { return iterator(this, 0); }
    [[nodiscard]] iterator end() const noexcept { return iterator(this, size()); }

private:
    const std::uint8_t* p_ = detail::kEmpty;
};

/// View of a C# dictionary: its keys in ascending order (strings by UTF-8 bytes, which is code point order) and its
/// values in the same order. Keys are integers, chars, enums or strings. Lookups are binary searches.
template <class K, class V> class Map final : public Table<Map<K, V>> {
public:
    using Table<Map<K, V>>::Table;
    using key_type = K;
    using value_type = typename detail::value_of<V>::type;

    [[nodiscard]] Vector<K> keys() const noexcept { return this->template tessera_get<schema<Map>::keys>(); }
    [[nodiscard]] Vector<V> values() const noexcept { return this->template tessera_get<schema<Map>::values>(); }
    [[nodiscard]] std::uint32_t size() const noexcept { return keys().size(); }
    [[nodiscard]] bool empty() const noexcept { return size() == 0; }

    /// Position of `key` in keys() and values(), or size() when it is not there.
    [[nodiscard]] std::uint32_t index_of(const K& key) const noexcept {
        const Vector<K> k = keys();
        const std::uint32_t size = k.size();
        std::uint32_t lo = 0, n = size;
        while (n > 0) {
            const std::uint32_t half = n / 2;
            if (k[lo + half] < key) {
                lo += half + 1;
                n -= half + 1;
            } else {
                n = half;
            }
        }
        return lo < size && k[lo] == key ? lo : size;
    }

    [[nodiscard]] bool contains(const K& key) const noexcept { return index_of(key) != size(); }

    /// The value stored for `key`, or nothing.
    [[nodiscard]] std::optional<value_type> find(const K& key) const noexcept {
        const std::uint32_t i = index_of(key);
        const Vector<V> v = values();
        if (i >= v.size()) return std::nullopt;  // not there (or a buffer with fewer values than keys)
        return value_type(v[i]);
    }
};

namespace detail {

/// Value of a present field whose cell is at `cell`.
template <class T>
TESSERA_ALWAYS_INLINE return_t<T> read_present(const std::uint8_t* cell, const Binding* child) noexcept {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Scalar) return load<T>(cell);
    else if constexpr (c == Cat::Optional) return T(load_value<typename is_optional<T>::inner>(cell));
    else if constexpr (c == Cat::String) return string_at(cell + load32(cell));
    else if constexpr (c == Cat::Vector || c == Cat::Table) return T(cell + load32(cell), child);
    else if constexpr (c == Cat::Union) return T(load32(cell), cell + 4 + load32(cell + 4), child);
    else if constexpr (c == Cat::Shared) {
        using S = typename is_shared<T>::inner;
        if constexpr (struct_has_bool<S>()) return load_value<S>(cell + load32(cell));
        else return *reinterpret_cast<const S*>(cell + load32(cell));
    } else if constexpr (struct_has_bool<T>()) {
        return load_value<T>(cell);
    } else {
        return *reinterpret_cast<const T*>(cell);
    }
}

/// Value of an absent field: the declared default for scalars, otherwise an empty/absent view or a zero struct.
template <class T>
TESSERA_ALWAYS_INLINE return_t<T> read_absent(const typename default_of<T>::type& def) noexcept {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Scalar) return def;
    else if constexpr (c == Cat::Struct || c == Cat::Shared) {
        using V = typename value_of<T>::type;
        if constexpr (struct_has_bool<V>()) return V{};
        else return kZero<V>;
    } else {
        return typename value_of<T>::type{};
    }
}

template <class T>
TESSERA_ALWAYS_INLINE return_t<T> read_field(const std::uint8_t* cell, const Binding* child, const typename default_of<T>::type& def) noexcept {
    return cell ? read_present<T>(cell, child) : read_absent<T>(def);
}

template <class T>
TESSERA_ALWAYS_INLINE return_t<T> read_element(const std::uint8_t* base, std::uint32_t i, const Binding* eb) noexcept {
    constexpr Cat c = category<T>();
    if constexpr (std::is_same_v<T, bool>) {
        return base[i] != 0;
    } else if constexpr (c == Cat::Scalar) {
        return load<T>(base + std::size_t{i} * sizeof(T));
    } else if constexpr (c == Cat::Struct) {
        if constexpr (struct_has_bool<T>()) return load_value<T>(base + std::size_t{i} * sizeof(T));
        else return reinterpret_cast<const T*>(base)[i];
    } else if constexpr (c == Cat::Union) {
        const std::uint8_t* slot = base + std::size_t{i} * 8;
        const std::uint32_t off = load32(slot + 4);
        return off ? T(load32(slot), slot + 4 + off, eb) : T();
    } else {
        const std::uint8_t* slot = base + std::size_t{i} * 4;
        const std::uint32_t off = load32(slot);
        if constexpr (c == Cat::String) return off ? string_at(slot + off) : std::string_view();
        else if constexpr (c == Cat::Shared) return off ? read_present<T>(slot, eb) : read_absent<T>(none_t{});
        else return off ? T(slot + off, eb) : T();
    }
}

// ---------------------------------------------------------------- type info (runtime view of generated schemas)

template <class T> const TypeInfo& type_info() noexcept;

template <class T> constexpr std::uint64_t struct_fp() noexcept {
    if constexpr (category<T>() == Cat::Optional) return struct_fp<typename is_optional<T>::inner>();
    else if constexpr (category<T>() == Cat::Struct) return schema<T>::fingerprint;
    else if constexpr (category<T>() == Cat::Shared) return schema<typename is_shared<T>::inner>::fingerprint;
    else return 0;
}

template <class T> constexpr std::uint32_t struct_size() noexcept {
    if constexpr (category<T>() == Cat::Optional) return struct_size<typename is_optional<T>::inner>();
    else if constexpr (category<T>() == Cat::Struct) return sizeof(T);
    else if constexpr (category<T>() == Cat::Shared) return sizeof(typename is_shared<T>::inner);
    else return 0;
}

template <class T> constexpr TypeInfoFn child_fn() noexcept {
    constexpr Cat c = category<T>();
    if constexpr (c == Cat::Table || c == Cat::Vector || c == Cat::Union) return &type_info<T>;
    else return nullptr;
}

template <class F> constexpr FieldInfo make_field(const F& f) noexcept {
    using T = typename F::type;
    return FieldInfo{f.name, f.hash, f.bit, wire_kind<T>(), cell_size<T>(), struct_fp<T>(), struct_size<T>(), child_fn<T>()};
}

template <class T> struct type_info_builder;

template <class D> requires std::is_base_of_v<TableBase, D> struct type_info_builder<D> {
    using S = schema<D>;
    static constexpr std::size_t N = std::tuple_size_v<std::remove_cvref_t<decltype(S::fields)>>;
    static constexpr auto fields = []<std::size_t... I>(std::index_sequence<I...>) {
        return std::array<FieldInfo, N == 0 ? 1 : N>{make_field(std::get<I>(S::fields))...};
    }(std::make_index_sequence<N>{});
    static TypeInfo build() noexcept {
        TypeInfo t;
        t.kind = Kind::Object;
        t.name = S::name;
        t.fingerprint = S::fingerprint;
        t.deep = S::deep_fingerprint;
        t.words = S::words;
        t.fixed = S::fixed;
        t.cell_count = static_cast<std::uint32_t>(std::size(S::cells)) - 1;
        t.cells = S::cells;
        t.fields = fields.data();
        t.field_count = static_cast<std::uint32_t>(N);
        return t;
    }
};

template <class E> struct type_info_builder<Vector<E>> {
    static TypeInfo build() noexcept {
        TypeInfo t;
        t.kind = Kind::Vector;
        t.name = "vector";
        t.elem_kind = wire_kind<E>();
        t.elem_optional = category<E>() == Cat::Optional;
        t.elem_size = category<E>() == Cat::Union ? 8u : (std::is_same_v<E, bool> ? 1u : cell_size<E>());
        t.elem_struct_fp = struct_fp<E>();
        t.elem_struct_size = struct_size<E>();
        t.elem = child_fn<E>();
        return t;
    }
};

template <class U> requires std::is_base_of_v<UnionBase, U> struct type_info_builder<U> {
    using S = schema<U>;
    static constexpr std::size_t N = std::tuple_size_v<std::remove_cvref_t<decltype(S::members)>>;
    static constexpr auto tags = []<std::size_t... I>(std::index_sequence<I...>) {
        return std::array<std::uint32_t, N == 0 ? 1 : N>{std::get<I>(S::members).tag...};
    }(std::make_index_sequence<N>{});
    static constexpr auto members = []<std::size_t... I>(std::index_sequence<I...>) {
        return std::array<TypeInfoFn, N == 0 ? 1 : N>{&type_info<typename std::remove_cvref_t<decltype(std::get<I>(S::members))>::type>...};
    }(std::make_index_sequence<N>{});
    static TypeInfo build() noexcept {
        TypeInfo t;
        t.kind = Kind::Union;
        t.name = S::name;
        t.fingerprint = S::fingerprint;
        t.deep = S::deep_fingerprint;
        t.tags = tags.data();
        t.members = members.data();
        t.member_count = static_cast<std::uint32_t>(N);
        return t;
    }
};

template <class T> const TypeInfo& type_info() noexcept {
    static const TypeInfo info = type_info_builder<T>::build();
    return info;
}

} // namespace detail
} // namespace tessera

#include "tessera_reader.hpp"
