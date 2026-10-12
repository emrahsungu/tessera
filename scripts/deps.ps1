# Fetches the pinned third-party code and data used only by the benchmarks.
# The Tessera library itself has no dependencies. Everything lands in the git-ignored .deps folder.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell can default to TLS 1.0, which GitHub rejects.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
$deps = Join-Path $root '.deps'
$downloads = Join-Path $deps 'downloads'
New-Item -ItemType Directory -Force $downloads | Out-Null

$flatbuffersTag = 'v25.12.19-2026-02-06-03fffb2'
$pins = @(
    @{ Name = 'flatbuffers-25.12.19-2026-02-06.zip'
       Url = "https://github.com/google/flatbuffers/archive/refs/tags/$flatbuffersTag.zip"
       Sha256 = '08EFEB8A2C9A4B5F6C305EF1CED64D08F18C6DA6BD2B7692F2F109D84A04C904' },
    @{ Name = 'flatc-25.12.19-2026-02-06-windows.zip'
       Url = "https://github.com/google/flatbuffers/releases/download/$flatbuffersTag/Windows.flatc.binary.zip"
       Sha256 = '68D51916873A3DBDAF7997DDFBBBFD6472B5907FFC62CCC9A88D146BBC0DB87D' },
    @{ Name = 'msgpack-cxx-9.0.0.tar.gz'
       Url = 'https://github.com/msgpack/msgpack-c/releases/download/cpp-9.0.0/msgpack-cxx-9.0.0.tar.gz'
       Sha256 = '303D3A7321AEE65EB9450DB8A6C973954E00AF34B88BA0C0ACA236BC50BFB8A9' },
    # The canada workload's data: the contour of Canada as GeoJSON, a common JSON benchmark (nativejson-benchmark, MIT).
    @{ Name = 'canada.json'
       Url = 'https://raw.githubusercontent.com/miloyip/nativejson-benchmark/478d5727c2a4048e835a29c65adecc7d795360d5/data/canada.json'
       Sha256 = 'F83B3B354030D5DD58740C68AC4FECEF64CB730A0D12A90362A7F23077F50D78' },
    # The C++ JSON reader: simdjson's single-header release (Apache-2.0 or MIT).
    @{ Name = 'simdjson-5.0.3.h'
       Url = 'https://raw.githubusercontent.com/simdjson/simdjson/bf89ddd00ef5fcb6a7f20611ea8853d258b67249/singleheader/simdjson.h'
       Sha256 = 'C23412D6A5AEDB3215CA6617DB882EEE47642318C330173C319A8FCA454B9F3D' },
    @{ Name = 'simdjson-5.0.3.cpp'
       Url = 'https://raw.githubusercontent.com/simdjson/simdjson/bf89ddd00ef5fcb6a7f20611ea8853d258b67249/singleheader/simdjson.cpp'
       Sha256 = 'F44B2798458DD6473AFCD3C564A3433C657E38493A10BD50812589A20E2EC00D' },
    # Protocol Buffers: protoc for the generated code, and the C++ runtime (libprotobuf, with the Abseil version it
    # pins), built from source for each compiler by scripts/protobuf.ps1 and scripts/protobuf-linux.sh (BSD-3-Clause,
    # Apache-2.0).
    @{ Name = 'protoc-36.2-win64.zip'
       Url = 'https://github.com/protocolbuffers/protobuf/releases/download/v36.2/protoc-36.2-win64.zip'
       Sha256 = 'F0C128DC0D8492ECEECE83BB459A4C0E316764B929FFBF1AA416357FD644EDD3' },
    @{ Name = 'protobuf-36.2.zip'
       Url = 'https://github.com/protocolbuffers/protobuf/releases/download/v36.2/protobuf-36.2.zip'
       Sha256 = 'EC61440905B46D42DC58CE7B490F0469263A21F93407F741C97E9B6B09533EB6' },
    @{ Name = 'abseil-cpp-20250512.1.tar.gz'
       Url = 'https://github.com/abseil/abseil-cpp/releases/download/20250512.1/abseil-cpp-20250512.1.tar.gz'
       Sha256 = '9B7A064305E9FD94D124FFA6CC358592EB42B5DA588FB4E07D09254AA40086DB' }
)

foreach ($pin in $pins) {
    $file = Join-Path $downloads $pin.Name
    if (-not (Test-Path $file)) {
        Write-Host "Downloading $($pin.Name)"
        Invoke-WebRequest -Uri $pin.Url -OutFile $file -UseBasicParsing
    }
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($actual -ne $pin.Sha256) { throw "SHA256 mismatch for $($pin.Name): $actual" }
}

$fbRoot = Join-Path $deps 'flatbuffers'
if (-not (Test-Path (Join-Path $fbRoot 'include/flatbuffers/flatbuffers.h'))) {
    $tmp = Join-Path $deps 'tmp-flatbuffers'
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    Expand-Archive (Join-Path $downloads $pins[0].Name) $tmp
    $src = Get-ChildItem $tmp -Directory | Select-Object -First 1
    New-Item -ItemType Directory -Force $fbRoot | Out-Null
    Copy-Item -Recurse (Join-Path $src.FullName 'include') $fbRoot
    Copy-Item -Recurse (Join-Path $src.FullName 'net') $fbRoot
    Copy-Item (Join-Path $src.FullName 'LICENSE') $fbRoot
    Remove-Item -Recurse -Force $tmp
}

$flatc = Join-Path $deps 'flatc'
if (-not (Test-Path (Join-Path $flatc 'flatc.exe'))) {
    Expand-Archive (Join-Path $downloads $pins[1].Name) $flatc -Force
}

$mp = Join-Path $deps 'msgpack-cxx'
if (-not (Test-Path (Join-Path $mp 'include/msgpack.hpp'))) {
    $tmp = Join-Path $deps 'tmp-msgpack'
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $tmp | Out-Null
    tar -xzf (Join-Path $downloads $pins[2].Name) -C $tmp
    if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
    $src = Get-ChildItem $tmp -Directory | Select-Object -First 1
    New-Item -ItemType Directory -Force $mp | Out-Null
    Copy-Item -Recurse (Join-Path $src.FullName 'include') $mp
    Copy-Item (Join-Path $src.FullName 'LICENSE_1_0.txt') $mp -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $tmp
}

$simdjson = Join-Path $deps 'simdjson'
if (-not (Test-Path (Join-Path $simdjson 'simdjson.cpp'))) {
    New-Item -ItemType Directory -Force $simdjson | Out-Null
    Copy-Item (Join-Path $downloads 'simdjson-5.0.3.h') (Join-Path $simdjson 'simdjson.h')
    Copy-Item (Join-Path $downloads 'simdjson-5.0.3.cpp') (Join-Path $simdjson 'simdjson.cpp')
}

$protoc = Join-Path $deps 'protoc'
if (-not (Test-Path (Join-Path $protoc 'bin/protoc.exe'))) {
    Expand-Archive (Join-Path $downloads 'protoc-36.2-win64.zip') $protoc -Force
}

# libprotobuf for each Windows compiler; it takes a few minutes the first time.
foreach ($toolchain in 'msvc', 'clangcl') {
    if (-not (Test-Path (Join-Path $deps "protobuf/$toolchain/lib/cmake/protobuf"))) {
        & (Join-Path $PSScriptRoot 'protobuf.ps1') -Toolchain $toolchain
        if ($LASTEXITCODE -ne 0) { throw "building libprotobuf ($toolchain) failed" }
    }
}

Write-Host "Dependencies ready in $deps (FlatBuffers $flatbuffersTag, msgpack-cxx 9.0.0, simdjson 5.0.3, protobuf 36.2, canada.json)."
