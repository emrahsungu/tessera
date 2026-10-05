cmake_minimum_required(VERSION 3.20)
project(tessera_bench CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

set(ROOT ${CMAKE_CURRENT_SOURCE_DIR}/../..)
set(DEPS ${ROOT}/.deps CACHE PATH "Third-party downloads (scripts/deps.ps1)")

add_executable(tessera_bench bench.cpp)
target_include_directories(tessera_bench PRIVATE
  ${ROOT}/cpp/include
  ${ROOT}/benchmarks/generated/tessera
  ${ROOT}/benchmarks/generated/fb-cpp
  ${DEPS}/flatbuffers/include
  ${DEPS}/msgpack-cxx/include)
target_compile_definitions(tessera_bench PRIVATE MSGPACK_NO_BOOST NDEBUG)

# Same optimization flags for all three libraries.
# x86-64-v3 = Haswell-class (AVX2, POPCNT, BMI1/2); MSVC's /arch:AVX2 implies the same instruction set.
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
  target_compile_options(tessera_bench PRIVATE /O2 /permissive- /Zc:__cplusplus /utf-8 /EHsc /W3 /clang:-march=x86-64-v3)
elseif(MSVC)
  target_compile_options(tessera_bench PRIVATE /O2 /Ob3 /arch:AVX2 /permissive- /Zc:__cplusplus /utf-8 /EHsc /W3)
else()
  target_compile_options(tessera_bench PRIVATE -O3 -march=x86-64-v3)
endif()
