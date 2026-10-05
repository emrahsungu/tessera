#!/usr/bin/env bash
# Builds and runs the C++ tests with GCC and Clang (AddressSanitizer + UBSan when the compiler's sanitizer runtime is
# installed). Run from WSL or Linux after the .NET tests have exported tests/cpp/generated.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
for cxx in g++ clang++; do
  command -v "$cxx" >/dev/null || continue
  build="/tmp/tessera-tests-$cxx-$(printf %s "$here" | md5sum | cut -c1-8)"  # one per checkout (CMake caches the source path)
  asan=ON
  echo 'int main(){}' | "$cxx" -x c++ -fsanitize=address,undefined -o /tmp/tessera-asan-probe - 2>/dev/null || asan=OFF
  cmake -S "$here/tests/cpp" -B "$build" -G Ninja -DCMAKE_CXX_COMPILER="$cxx" -DCMAKE_BUILD_TYPE=RelWithDebInfo -DTESSERA_ASAN=$asan >/dev/null
  cmake --build "$build" >/dev/null
  echo "== $cxx $($cxx -dumpfullversion 2>/dev/null || $cxx --version | head -1) (sanitizers: $asan)"
  "$build/interop_test" "$here/tests/cpp/generated"
  "$build/interop_test_once" "$here/tests/cpp/generated"
  tree="$("$build/fuzz_test" "$here/tests/cpp/generated" "${FUZZ_ITERATIONS:-2000}" "${FUZZ_SEED:-7}")"
  echo "$tree"
  once="$("$build/fuzz_test_once" "$here/tests/cpp/generated" "${FUZZ_ITERATIONS:-2000}" "${FUZZ_SEED:-7}")"
  echo "$once (every item verified once)"
  [ "$tree" = "$once" ] || { echo "the verdicts differ from the tree walk" >&2; exit 1; }
  "$build/multi_assembly_test" "$here/tests/cpp/generated"
  "$build/utf8_test"
done
