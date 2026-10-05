cmake_minimum_required(VERSION 3.20)
project(tessera_quickstart CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(quickstart main.cpp)
# Quickstart.tessera.hpp and tessera/*.hpp are written here by `dotnet build` (TesseraCppOutputDir).
target_include_directories(quickstart PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/generated)
