cmake_minimum_required(VERSION 3.20)
project(tessera_cpp_tests CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(TESSERA_ASAN "Build with AddressSanitizer (and UBSan where available)" OFF)

if(MSVC)
  add_compile_options(/W4 /WX /permissive- /Zc:__cplusplus /utf-8 /arch:AVX2)
  if(TESSERA_ASAN)
    add_compile_options(/fsanitize=address /Zi)
    add_link_options(/INCREMENTAL:NO /DEBUG)
  endif()
else()
  add_compile_options(-Wall -Wextra -Wpedantic -Werror -march=native)
  if(TESSERA_ASAN)
    add_compile_options(-fsanitize=address,undefined -fno-sanitize-recover=all -fno-omit-frame-pointer -g)
    add_link_options(-fsanitize=address,undefined)
  endif()
endif()

set(TESSERA_INCLUDE ${CMAKE_CURRENT_SOURCE_DIR}/../../cpp/include ${CMAKE_CURRENT_SOURCE_DIR}/generated)

add_executable(interop_test interop_test.cpp)
target_include_directories(interop_test PRIVATE ${TESSERA_INCLUDE})

add_executable(fuzz_test fuzz_test.cpp)
target_include_directories(fuzz_test PRIVATE ${TESSERA_INCLUDE})

# Two model assemblies' headers in one program (see multi_assembly_test.cpp).
add_executable(multi_assembly_test multi_assembly_test.cpp multi_assembly_other.cpp)
target_include_directories(multi_assembly_test PRIVATE ${TESSERA_INCLUDE} ${CMAKE_CURRENT_SOURCE_DIR}/generated-shared)

# The interop and fuzz tests again, with every item verified once from the start instead of after the verifier sees
# sharing: the fuzz verdicts must be the same as the tree walk's.
foreach(test interop_test fuzz_test)
  add_executable(${test}_once ${test}.cpp)
  target_include_directories(${test}_once PRIVATE ${TESSERA_INCLUDE})
  target_compile_definitions(${test}_once PRIVATE TESSERA_VERIFY_ONCE_ALWAYS)
endforeach()

# The UTF-8 validator against a byte-at-a-time reference (runtime headers only).
add_executable(utf8_test utf8_test.cpp)
target_include_directories(utf8_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../../cpp/include)
