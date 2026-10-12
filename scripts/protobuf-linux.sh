#!/usr/bin/env bash
# Builds libprotobuf (protobuf 36.2 and the Abseil version it pins) with GCC for the C++ benchmark, once, into
# ~/.cache/tessera-protobuf-36.2-gcc, with the benchmark's architecture flags (the Windows counterpart is
# scripts/protobuf.ps1). Usage: scripts/protobuf-linux.sh <the repository's .deps folder>
set -euo pipefail
deps="$1"
prefix="$HOME/.cache/tessera-protobuf-36.2-gcc"
if [ -d "$prefix/lib/cmake/protobuf" ] || [ -d "$prefix/lib64/cmake/protobuf" ]; then exit 0; fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
echo "Building libprotobuf 36.2 with GCC (once; this takes a few minutes)"
python3 -m zipfile -e "$deps/downloads/protobuf-36.2.zip" "$work"
tar -xzf "$deps/downloads/abseil-cpp-20250512.1.tar.gz" -C "$work"
cmake -S "$work/protobuf-36.2" -B "$work/b" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_COMPILER=g++ \
    -DCMAKE_CXX_FLAGS=-march=x86-64-v3 -DCMAKE_CXX_STANDARD=20 -DABSL_PROPAGATE_CXX_STD=ON \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DCMAKE_INSTALL_PREFIX="$prefix" \
    -DFETCHCONTENT_SOURCE_DIR_ABSL="$work/abseil-cpp-20250512.1" -Dprotobuf_INSTALL=ON \
    -Dprotobuf_BUILD_TESTS=OFF -Dprotobuf_BUILD_CONFORMANCE=OFF -Dprotobuf_BUILD_EXAMPLES=OFF \
    -Dprotobuf_BUILD_PROTOC_BINARIES=OFF -Dprotobuf_BUILD_LIBUPB=OFF >/dev/null
cmake --build "$work/b" --target install >/dev/null
echo "libprotobuf (GCC) installed in $prefix"
