# Builds libprotobuf (protobuf 36.2 and the Abseil version it pins) for the C++ benchmark with one Windows compiler,
# into .deps/protobuf/<toolchain>, with the benchmark's architecture flags. scripts/deps.ps1 runs it once per compiler,
# after downloading the sources; scripts/protobuf-linux.sh does the same with GCC in WSL.
#   scripts/protobuf.ps1 -Toolchain msvc|clangcl
param([Parameter(Mandatory)][ValidateSet('msvc', 'clangcl')][string]$Toolchain)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$downloads = Join-Path $root '.deps/downloads'
$prefix = Join-Path $root ".deps/protobuf/$Toolchain"
# Abseil's source paths are deep: build in a short folder, so that no path exceeds Windows' 260-character limit.
$work = Join-Path ([IO.Path]::GetTempPath()) "tessera-pb-$Toolchain"
# The newest Visual Studio with the C++ tools that this CMake supports: CMake marks it as its default generator.
function VsGenerator {
    $g = (cmake --help) -match '^\* Visual Studio \d+ \d{4}' | Select-Object -First 1
    if (-not $g) { throw 'CMake finds no Visual Studio it supports.' }
    return ($g -replace '^\* (Visual Studio \d+ \d{4}).*$', '$1')
}

Write-Host "Building libprotobuf 36.2 with $Toolchain (once; this takes a few minutes)"
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
tar -xf (Join-Path $downloads 'protobuf-36.2.zip') -C $work
if ($LASTEXITCODE -ne 0) { throw 'extracting protobuf failed' }
tar -xzf (Join-Path $downloads 'abseil-cpp-20250512.1.tar.gz') -C $work
if ($LASTEXITCODE -ne 0) { throw 'extracting Abseil failed' }

$toolset = if ($Toolchain -eq 'clangcl') { @('-T', 'ClangCL') } else { @() }
$old = $env:CXXFLAGS
# Read by CMake when it first configures the build folder, in addition to its default flags.
$env:CXXFLAGS = if ($Toolchain -eq 'clangcl') { '/clang:-march=x86-64-v3' } else { '/arch:AVX2' }
try {
    cmake -S (Join-Path $work 'protobuf-36.2') -B (Join-Path $work 'b') -G (VsGenerator) -A x64 @toolset `
        "-DCMAKE_INSTALL_PREFIX=$prefix" "-DFETCHCONTENT_SOURCE_DIR_ABSL=$(Join-Path $work 'abseil-cpp-20250512.1')" `
        -DCMAKE_CXX_STANDARD=20 -DABSL_PROPAGATE_CXX_STD=ON -Dprotobuf_INSTALL=ON -Dprotobuf_MSVC_STATIC_RUNTIME=OFF `
        -Dprotobuf_BUILD_TESTS=OFF -Dprotobuf_BUILD_CONFORMANCE=OFF -Dprotobuf_BUILD_EXAMPLES=OFF `
        -Dprotobuf_BUILD_PROTOC_BINARIES=OFF -Dprotobuf_BUILD_LIBUPB=OFF | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'configuring protobuf failed' }
} finally {
    $env:CXXFLAGS = $old
}
cmake --build (Join-Path $work 'b') --config Release --target install -- /m /v:q /nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'building protobuf failed' }
Remove-Item -Recurse -Force $work
Write-Host "libprotobuf ($Toolchain) installed in $prefix"
