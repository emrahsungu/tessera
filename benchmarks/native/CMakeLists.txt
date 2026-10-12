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

# libprotobuf, built with this compiler and these flags (scripts/protobuf.ps1 on Windows, scripts/protobuf-linux.sh
# with GCC), with the Abseil it was built with.
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
  set(_protobuf ${DEPS}/protobuf/clangcl)
elseif(MSVC)
  set(_protobuf ${DEPS}/protobuf/msvc)
else()
  set(_protobuf $ENV{HOME}/.cache/tessera-protobuf-36.2-gcc)
endif()
set(PROTOBUF_PREFIX ${_protobuf} CACHE PATH "libprotobuf built with this compiler")
list(PREPEND CMAKE_PREFIX_PATH ${PROTOBUF_PREFIX})
find_package(Protobuf CONFIG REQUIRED)
if(NOT Protobuf_VERSION VERSION_EQUAL 36.2.0)
  message(FATAL_ERROR "Found libprotobuf ${Protobuf_VERSION} in ${Protobuf_DIR}; the code protoc 36.2 generates needs 36.2.")
endif()

add_executable(tessera_bench bench.cpp ${DEPS}/simdjson/simdjson.cpp ${ROOT}/benchmarks/generated/pb-cpp/bench.pb.cc)
target_include_directories(tessera_bench PRIVATE
  ${ROOT}/cpp/include
  ${ROOT}/benchmarks/generated/tessera
  ${ROOT}/benchmarks/generated/fb-cpp
  ${ROOT}/benchmarks/generated/pb-cpp
  ${DEPS}/flatbuffers/include
  ${DEPS}/msgpack-cxx/include
  ${DEPS}/simdjson)
target_compile_definitions(tessera_bench PRIVATE MSGPACK_NO_BOOST NDEBUG)
target_link_libraries(tessera_bench PRIVATE protobuf::libprotobuf)

# Same optimization flags for every library.
# x86-64-v3 = Haswell-class (AVX2, POPCNT, BMI1/2); MSVC's /arch:AVX2 implies the same instruction set.
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
  target_compile_options(tessera_bench PRIVATE /O2 /permissive- /Zc:__cplusplus /utf-8 /EHsc /W3 /clang:-march=x86-64-v3)
  # Under clang-cl, simdjson takes its CPU features from the compiler flags, and x86-64-v3 leaves out the carry-less
  # multiply its Haswell kernel uses (every x86-64-v3 CPU has it). Only simdjson's own source gets it.
  set_source_files_properties(${DEPS}/simdjson/simdjson.cpp PROPERTIES COMPILE_OPTIONS "/clang:-mpclmul")
elseif(MSVC)
  target_compile_options(tessera_bench PRIVATE /O2 /Ob3 /arch:AVX2 /permissive- /Zc:__cplusplus /utf-8 /EHsc /W3)
else()
  target_compile_options(tessera_bench PRIVATE -O3 -march=x86-64-v3)
endif()
