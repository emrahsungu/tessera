# Builds and runs the whole three-library benchmark and writes docs/BENCHMARKS.md and the README's charts (docs/images/benchmarks).
#   scripts/bench.ps1             full run (several minutes)
#   scripts/bench.ps1 -Quick      fewer, shorter rounds (for a smoke test)
#   scripts/bench.ps1 -NoGcc      skip the GCC run in WSL
param([switch]$Quick, [switch]$NoGcc, [switch]$NoClang)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The newest Visual Studio with the C++ tools that this CMake supports: CMake marks it as its default generator.
function VsGenerator {
    $g = (cmake --help) -match '^\* Visual Studio \d+ \d{4}' | Select-Object -First 1
    if (-not $g) { throw 'CMake finds no Visual Studio it supports. Install Visual Studio 2022 or later with "Desktop development with C++"; Visual Studio 2026 needs CMake 4.2 or later (its Developer PowerShell has one).' }
    return ($g -replace '^\* (Visual Studio \d+ \d{4}).*$', '$1')
}
$bench = Join-Path $root 'benchmarks'
$results = Join-Path $bench 'results'
$data = Join-Path $bench 'generated/data'
New-Item -ItemType Directory -Force $results | Out-Null

function Run([string]$what, [scriptblock]$block) {
    Write-Host "==> $what" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

Run 'dependencies' { & (Join-Path $PSScriptRoot 'deps.ps1') }
Run 'flatc' {
    $flatc = Join-Path $root '.deps/flatc/flatc.exe'
    & $flatc --cpp --scoped-enums -o (Join-Path $bench 'generated/fb-cpp') (Join-Path $bench 'schemas/bench.fbs')
    & $flatc --csharp -o (Join-Path $bench 'generated/fb-csharp') (Join-Path $bench 'schemas/bench.fbs')
}
$proj = Join-Path $bench 'Tessera.Benchmarks/Tessera.Benchmarks.csproj'
Run '.NET build' { dotnet build $proj -c Release --nologo -v q }
Run 'export buffers' { dotnet run --project $proj -c Release --no-build -- --export-only }

$rounds = if ($Quick) { 3 } else { 15 }
$roundMs = if ($Quick) { 20 } else { 80 }
$native = Join-Path $bench 'native'
Run 'C++ build (MSVC)' { cmake -S $native -B (Join-Path $native 'build/msvc') -G (VsGenerator) -A x64 | Out-Null; cmake --build (Join-Path $native 'build/msvc') --config Release | Out-Null }
Run 'C++ read benchmark (MSVC)' { & (Join-Path $native 'build/msvc/Release/tessera_bench.exe') $data --rounds $rounds --round-ms $roundMs --json (Join-Path $results 'native-msvc.json') }
if (-not $NoClang) {
    Run 'C++ build (clang-cl)' { cmake -S $native -B (Join-Path $native 'build/clangcl') -G (VsGenerator) -A x64 -T ClangCL | Out-Null; cmake --build (Join-Path $native 'build/clangcl') --config Release | Out-Null }
    Run 'C++ read benchmark (clang-cl)' { & (Join-Path $native 'build/clangcl/Release/tessera_bench.exe') $data --rounds $rounds --round-ms $roundMs --json (Join-Path $results 'native-clang.json') }
}
if (-not $NoGcc -and (Get-Command wsl -ErrorAction SilentlyContinue)) {
    $wslRoot = (wsl wslpath -a ($root -replace '\\', '/')).Trim()
    Run 'C++ build + read benchmark (GCC, WSL)' {
        wsl bash -c "cmake -S '$wslRoot/benchmarks/native' -B /tmp/tessera-bench-gcc -G Ninja -DCMAKE_CXX_COMPILER=g++ -DCMAKE_BUILD_TYPE=Release >/dev/null && cmake --build /tmp/tessera-bench-gcc >/dev/null && /tmp/tessera-bench-gcc/tessera_bench '$wslRoot/benchmarks/generated/data' --rounds $rounds --round-ms $roundMs --json '$wslRoot/benchmarks/results/native-gcc.json'"
    }
}

$writeArgs = if ($Quick) { @('--quick') } else { @('--rounds', '15', '--round-ms', '100') }
Run '.NET write benchmark' { dotnet run --project $proj -c Release --no-build -- @writeArgs }
Run 'report' { dotnet run --project $proj -c Release --no-build -- --report $results (Join-Path $root 'docs/BENCHMARKS.md') }
# Keep the raw data next to the report (benchmarks/results itself is git-ignored scratch space).
$raw = Join-Path $root 'docs/benchmarks'
New-Item -ItemType Directory -Force $raw | Out-Null
Copy-Item (Join-Path $results '*.json') $raw
Write-Host "Done: docs/BENCHMARKS.md and docs/images/benchmarks (raw data in benchmarks/results)" -ForegroundColor Green
