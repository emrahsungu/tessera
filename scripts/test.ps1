# Runs every test: .NET (runtime + generator), then the C++ interop and fuzz tests on the buffers the .NET tests export.
#   scripts/test.ps1              .NET tests + C++ tests with MSVC
#   scripts/test.ps1 -Asan        also an MSVC AddressSanitizer build of the C++ tests
#   scripts/test.ps1 -Linux       also GCC and Clang (with ASan + UBSan) in WSL via scripts/test-cpp-linux.sh
#   scripts/test.ps1 -Package     also packs Tessera and builds samples/Quickstart from the package, outside the repo
param([switch]$Asan, [switch]$Linux, [switch]$Package, [int]$FuzzIterations = 2000)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The newest Visual Studio with the C++ tools that this CMake supports: CMake marks it as its default generator.
function VsGenerator {
    $g = (cmake --help) -match '^\* Visual Studio \d+ \d{4}' | Select-Object -First 1
    if (-not $g) { throw 'CMake finds no Visual Studio it supports. Install Visual Studio 2022 or later with "Desktop development with C++"; Visual Studio 2026 needs CMake 4.2 or later (its Developer PowerShell has one).' }
    return ($g -replace '^\* (Visual Studio \d+ \d{4}).*$', '$1')
}
$cpp = Join-Path $root 'tests/cpp'
$generated = Join-Path $cpp 'generated'

function Run([string]$what, [scriptblock]$block) {
    Write-Host "==> $what" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

Run '.NET build' { dotnet build (Join-Path $root 'Tessera.slnx') -c Release --nologo -v q }
Run '.NET runtime tests (exports tests/cpp/generated)' { dotnet run --project (Join-Path $root 'tests/Tessera.Tests') -c Release --no-build }
Run '.NET generator tests' { dotnet run --project (Join-Path $root 'tests/Tessera.Generator.Tests') -c Release --no-build }

$configs = @(@{ Name = 'MSVC'; Dir = 'build/msvc'; Args = @() })
if ($Asan) { $configs += @{ Name = 'MSVC + AddressSanitizer'; Dir = 'build/msvc-asan'; Args = @('-DTESSERA_ASAN=ON') } }
foreach ($c in $configs) {
    $build = Join-Path $cpp $c.Dir
    $config = if ($c.Args.Count -gt 0) { 'RelWithDebInfo' } else { 'Release' }
    Run "C++ build ($($c.Name))" { cmake -S $cpp -B $build -G (VsGenerator) -A x64 @($c.Args) | Out-Null; cmake --build $build --config $config | Out-Null }
    if ($c.Args.Count -gt 0) {
        # The ASan runtime DLL sits next to the MSVC compiler.
        $cl = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\VC\Tools\MSVC\*\bin\Hostx64\x64\clang_rt.asan_dynamic-x86_64.dll" -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($cl) { $env:PATH = "$($cl.DirectoryName);$env:PATH" }
    }
    Run "C++ interop test ($($c.Name))" { & (Join-Path $build "$config/interop_test.exe") $generated }
    Run "C++ interop test, every item verified once ($($c.Name))" { & (Join-Path $build "$config/interop_test_once.exe") $generated }
    $script:fuzz = ''
    Run "C++ fuzz test ($($c.Name))" { $script:fuzz = & (Join-Path $build "$config/fuzz_test.exe") $generated $FuzzIterations 7; $script:fuzz }
    Run "C++ fuzz test, every item verified once ($($c.Name))" {
        $once = & (Join-Path $build "$config/fuzz_test_once.exe") $generated $FuzzIterations 7
        $once
        if ("$once" -ne "$script:fuzz") { Write-Host 'the verdicts differ from the tree walk'; $global:LASTEXITCODE = 1 }
    }
    Run "C++ multi-assembly test ($($c.Name))" { & (Join-Path $build "$config/multi_assembly_test.exe") $generated }
    Run "C++ UTF-8 test ($($c.Name))" { & (Join-Path $build "$config/utf8_test.exe") }
}

if ($Linux) {
    $wslRoot = (wsl wslpath -a ($root -replace '\\', '/')).Trim()
    Run 'C++ tests (GCC + Clang, WSL)' { wsl bash -c "FUZZ_ITERATIONS=$FuzzIterations bash '$wslRoot/scripts/test-cpp-linux.sh'" }
}

if ($Package) {
    # A consumer outside the repository (no Directory.Build.props, no project references) using only the .nupkg.
    $work = Join-Path ([System.IO.Path]::GetTempPath()) "tessera-package-test"
    if (Test-Path $work) { Get-ChildItem $work -Force | Remove-Item -Recurse -Force }
    $feed = Join-Path $work 'feed'
    $app = Join-Path $work 'app'
    New-Item -ItemType Directory -Force (Join-Path $app 'cpp') | Out-Null
    Run 'pack' { dotnet pack (Join-Path $root 'src/Tessera') -c Release -o $feed --nologo -v q }
    $sample = Join-Path $root 'samples/Quickstart'
    Copy-Item (Join-Path $sample '*.cs') $app
    Copy-Item (Join-Path $sample 'cpp/main.cpp'), (Join-Path $sample 'cpp/CMakeLists.txt') (Join-Path $app 'cpp')
    $props = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup
    $version = $props.VersionPrefix + $(if ($props.VersionSuffix) { '-' + $props.VersionSuffix } else { '' })
    Set-Content (Join-Path $app 'nuget.config') "<configuration><packageSources><clear /><add key=`"feed`" value=`"$feed`" /></packageSources></configuration>"
    Set-Content (Join-Path $app 'App.csproj') @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <TesseraCppOutputDir>cpp/generated</TesseraCppOutputDir>
    <TesseraCppHeaderName>Quickstart.tessera.hpp</TesseraCppHeaderName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Tessera" Version="$version" />
  </ItemGroup>
</Project>
"@
    Push-Location $app
    try {
        # A private package folder: the global cache is left alone, and a rebuilt package with the same version is
        # never taken from it.
        Run 'package consumer: restore' { dotnet restore --packages (Join-Path $work 'packages') --nologo -v q }
        Run 'package consumer: build + run' { dotnet run -c Release --no-restore -- monster.bin }
        Run 'package consumer: C++ build' { cmake -S cpp -B cpp/build -G (VsGenerator) -A x64 | Out-Null; cmake --build cpp/build --config Release | Out-Null }
        Run 'package consumer: C++ read' { & ./cpp/build/Release/quickstart.exe monster.bin }
    } finally {
        Pop-Location
    }
}

Write-Host 'All tests passed.' -ForegroundColor Green
