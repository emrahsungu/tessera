# Fetches the pinned third-party code used only by the benchmarks and interop tests.
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
       Sha256 = '303D3A7321AEE65EB9450DB8A6C973954E00AF34B88BA0C0ACA236BC50BFB8A9' }
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

Write-Host "Dependencies ready in $deps (FlatBuffers $flatbuffersTag, msgpack-cxx 9.0.0)."
