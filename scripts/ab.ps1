# A/B benchmark: a candidate (B) against a baseline (A), on the same machine, in alternating order (AB, BA, AB, BA)
# so drift affects both alike. FlatBuffers and MessagePack run unchanged on both sides, so their deltas show the noise.
# Both sides are built in their own git worktrees under .ab/, so the working tree can keep changing meanwhile.
#   scripts/ab.ps1 -Candidate HEAD                    B = the last commit, A = its parent
#   scripts/ab.ps1 -Baseline <ref> -Candidate <ref>   any two commits (the same ref twice measures the noise)
#   scripts/ab.ps1 -Compilers msvc                    fewer compilers; -NoNative / -NoDotnet to skip a side
#   scripts/ab.ps1 -Out docs/x.md                     also write the report to a file
#   scripts/ab.ps1 -ReportOnly [-Results <dir>]       only report on earlier results (default .ab/results)
#   scripts/ab.ps1 -Jcc                               build with the JCC-erratum mitigation (own build folders), so
#                                                     code placement cannot change which jumps miss the uop cache
param(
    [string]$Candidate = 'HEAD',
    [string]$Baseline = '',
    [string[]]$Compilers = @('msvc', 'clang', 'gcc'),
    [int]$Alternations = 4,
    [int]$Rounds = 5,
    [int]$RoundMs = 40,
    [switch]$NoNative,
    [switch]$NoDotnet,
    [string]$Filter = '',
    [string]$Out = '',
    [switch]$ReportOnly,
    [string]$Results = '',
    [switch]$Jcc
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# The newest Visual Studio with the C++ tools that this CMake supports: CMake marks it as its default generator.
function VsGenerator {
    $g = (cmake --help) -match '^\* Visual Studio \d+ \d{4}' | Select-Object -First 1
    if (-not $g) { throw 'CMake finds no Visual Studio it supports. Install Visual Studio 2022 or later with "Desktop development with C++"; Visual Studio 2026 needs CMake 4.2 or later (its Developer PowerShell has one).' }
    return ($g -replace '^\* (Visual Studio \d+ \d{4}).*$', '$1')
}
$ab = Join-Path $root '.ab'
$base = Join-Path $ab 'base'
$cand = Join-Path $ab 'cand'
$results = if ($Results) { [IO.Path]::GetFullPath($Results) } else { Join-Path $ab 'results' }
if (-not $Baseline) { $Baseline = "$Candidate~1" }
$inv = [System.Globalization.CultureInfo]::InvariantCulture
# Non-ASCII characters by code, so the script reads the same in Windows PowerShell (ANSI) and PowerShell 7 (UTF-8).
$Delta = [char]0x394; $Times = [char]0xD7; $Micro = [char]0xB5

function Run([string]$what, [scriptblock]$block) {
    Write-Host "==> $what" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    # The blocks run native tools, whose exit codes say whether they failed. 'Continue' keeps Windows PowerShell from
    # turning their redirected stderr output (such as git's progress messages) into terminating errors.
    $ErrorActionPreference = 'Continue'
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

if (-not $ReportOnly) {
    # ---------------------------------------------------------------------------------------------------- baseline tree
    New-Item -ItemType Directory -Force $ab | Out-Null
    if (Test-Path $results) { Get-ChildItem $results -File | Remove-Item -Force }
    New-Item -ItemType Directory -Force $results | Out-Null
    function Worktree([string]$path, [string]$ref) {
        $sha = (git -C $root rev-parse $ref).Trim()
        if (-not (Test-Path (Join-Path $path '.git'))) { Run "worktree $ref" { git -C $root worktree add --detach $path $sha 2>&1 | Out-Null } }
        else { Run "checkout $ref" { git -C $path checkout --detach --force $sha 2>&1 | Out-Null } }
        if (-not (Test-Path (Join-Path $path '.deps'))) { New-Item -ItemType Junction -Path (Join-Path $path '.deps') -Target (Join-Path $root '.deps') | Out-Null }
        return $sha
    }
    $sha = Worktree $base $Baseline
    $shaB = Worktree $cand $Candidate
    Write-Host "A = $Baseline ($($sha.Substring(0, 10))), B = $Candidate ($($shaB.Substring(0, 10)))"
    @{ baseline = $Baseline; candidate = $Candidate; shaA = $sha; shaB = $shaB; rounds = $Rounds; jcc = [bool]$Jcc } | ConvertTo-Json | Set-Content (Join-Path $results 'meta.json')
    # -Jcc: Skylake's microcode fix for the JCC erratum keeps jumps that cross or end at a 32-byte boundary out of the
    # uop cache, so where the linker puts unchanged code can change its speed. The mitigation pads the code instead.
    $j = if ($Jcc) { '-jcc' } else { '' }
    function Configure([string]$flags, [scriptblock]$block) {
        $old = $env:CXXFLAGS
        if ($Jcc) { $env:CXXFLAGS = $flags }  # read by CMake when it first configures a build folder
        try { & $block } finally { $env:CXXFLAGS = $old }
    }

    $sides = @(@{ Name = 'A'; Root = $base }, @{ Name = 'B'; Root = $cand })
    $flatc = Join-Path $root '.deps/flatc/flatc.exe'
    $wsl = [bool](Get-Command wsl -ErrorAction SilentlyContinue)
    function WslPath([string]$p) { (wsl wslpath -a ($p -replace '\\', '/')).Trim() }

    foreach ($s in $sides) {
        $r = $s.Root
        Run "$($s.Name): flatc" {
            & $flatc --cpp --scoped-enums -o (Join-Path $r 'benchmarks/generated/fb-cpp') (Join-Path $r 'benchmarks/schemas/bench.fbs')
            & $flatc --csharp -o (Join-Path $r 'benchmarks/generated/fb-csharp') (Join-Path $r 'benchmarks/schemas/bench.fbs')
        }
        $proj = Join-Path $r 'benchmarks/Tessera.Benchmarks/Tessera.Benchmarks.csproj'
        Run "$($s.Name): .NET build" { dotnet build $proj -c Release --nologo -v q | Out-Null }
        Run "$($s.Name): export buffers" { dotnet run --project $proj -c Release --no-build -- --export-only | Out-Null }
        $sizes = [ordered]@{}
        Get-ChildItem (Join-Path $r 'benchmarks/generated/data') -Filter '*.bin' | Sort-Object Name | ForEach-Object { $sizes[$_.BaseName] = $_.Length }
        $sizes | ConvertTo-Json | Set-Content (Join-Path $results "sizes-$($s.Name).json")
        if ($NoNative) { continue }
        $native = Join-Path $r 'benchmarks/native'
        $deps = Join-Path $root '.deps'
        foreach ($c in $Compilers) {
            switch ($c) {
                'msvc' { Run "$($s.Name): C++ build (MSVC)" { Configure '/QIntel-jcc-erratum' { cmake -S $native -B (Join-Path $native "build/ab-msvc$j") -G (VsGenerator) -A x64 "-DDEPS=$deps" | Out-Null }; cmake --build (Join-Path $native "build/ab-msvc$j") --config Release | Out-Null } }
                'clang' { Run "$($s.Name): C++ build (clang-cl)" { Configure '/clang:-mbranches-within-32B-boundaries' { cmake -S $native -B (Join-Path $native "build/ab-clangcl$j") -G (VsGenerator) -A x64 -T ClangCL "-DDEPS=$deps" | Out-Null }; cmake --build (Join-Path $native "build/ab-clangcl$j") --config Release | Out-Null } }
                'gcc' {
                    if (-not $wsl) { continue }
                    $wn = WslPath $native; $wd = WslPath $deps
                    $cxx = if ($Jcc) { "CXXFLAGS='-Wa,-mbranches-within-32B-boundaries' " } else { '' }
                    Run "$($s.Name): C++ build (GCC, WSL)" { wsl bash -c "${cxx}cmake -S '$wn' -B ~/.cache/tessera-ab-$($s.Name)-gcc$j -G Ninja -DCMAKE_CXX_COMPILER=g++ -DCMAKE_BUILD_TYPE=Release '-DDEPS=$wd' >/dev/null && cmake --build ~/.cache/tessera-ab-$($s.Name)-gcc$j >/dev/null" }
                }
            }
        }
    }

    # ---------------------------------------------------------------------------------------------------- alternating runs
    $filterArgs = if ($Filter) { @('--filter', $Filter) } else { @() }
    for ($i = 1; $i -le $Alternations; $i++) {
        $order = if ($i % 2) { $sides } else { @($sides[1], $sides[0]) }
        foreach ($s in $order) {
            $r = $s.Root
            $data = Join-Path $r 'benchmarks/generated/data'
            if (-not $NoNative) {
                foreach ($c in $Compilers) {
                    $json = Join-Path $results "native-$c-$($s.Name)-$i.json"
                    switch ($c) {
                        'msvc' { Run "$($s.Name) run $i (MSVC)" { & (Join-Path $r "benchmarks/native/build/ab-msvc$j/Release/tessera_bench.exe") $data --rounds $Rounds --round-ms $RoundMs --json $json @filterArgs 2>$null | Out-Null } }
                        'clang' { Run "$($s.Name) run $i (clang-cl)" { & (Join-Path $r "benchmarks/native/build/ab-clangcl$j/Release/tessera_bench.exe") $data --rounds $Rounds --round-ms $RoundMs --json $json @filterArgs 2>$null | Out-Null } }
                        'gcc' {
                            if (-not $wsl) { continue }
                            $wdata = WslPath $data; $wjson = WslPath $json
                            $wf = if ($Filter) { "--filter '$Filter'" } else { '' }
                            Run "$($s.Name) run $i (GCC)" { wsl bash -c "~/.cache/tessera-ab-$($s.Name)-gcc$j/tessera_bench '$wdata' --rounds $Rounds --round-ms $RoundMs --json '$wjson' $wf >/dev/null 2>&1" }
                        }
                    }
                }
            }
            if (-not $NoDotnet) {
                $proj = Join-Path $r 'benchmarks/Tessera.Benchmarks/Tessera.Benchmarks.csproj'
                Run "$($s.Name) run $i (.NET write)" { dotnet run --project $proj -c Release --no-build -- --rounds $Rounds --round-ms ([Math]::Max(60, $RoundMs)) --warmup-ms 300 2>$null | Out-Null }
                Copy-Item (Join-Path $r 'benchmarks/results/dotnet.json') (Join-Path $results "dotnet-$($s.Name)-$i.json")
            }
        }
    }
}

# ---------------------------------------------------------------------------------------------------- report
function Median([double[]]$v) { $s = $v | Sort-Object; $n = $s.Count; if ($n -eq 0) { return [double]::NaN }; if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function Pct([double]$a, [double]$b) { (($b / $a) - 1) * 100 }
function FmtPct([double]$p) { ($(if ($p -ge 0) { '+' } else { '' })) + $p.ToString('0.0', $inv) + '%' }
function GeoPct($ratios) { if (-not $ratios.Count) { return 0 }; $s = 0.0; foreach ($r in $ratios) { $s += [Math]::Log($r) }; ([Math]::Exp($s / $ratios.Count) - 1) * 100 }

$meta = if (Test-Path (Join-Path $results 'meta.json')) { Get-Content (Join-Path $results 'meta.json') -Raw | ConvertFrom-Json } else { $null }
$titleA = if ($meta) { "$($meta.baseline) ($($meta.shaA.Substring(0, 10)))" } else { $Baseline }
$titleB = if ($meta) { "$($meta.candidate) ($($meta.shaB.Substring(0, 10)))" } else { $Candidate }
$runs = @(Get-ChildItem $results -Filter '*-A-*.json' | ForEach-Object { [int]($_.BaseName -replace '.*-', '') } | Sort-Object -Unique).Count
$md = New-Object System.Text.StringBuilder
$summary = New-Object System.Collections.Generic.List[string]
[void]$md.AppendLine("# A/B: $titleA vs $titleB")
[void]$md.AppendLine()
if ($meta -and $meta.jcc) {
    [void]$md.AppendLine('Built with the JCC-erratum mitigation (`-Jcc`: GCC and clang-cl `-mbranches-within-32B-boundaries`, MSVC `/QIntel-jcc-erratum`), so code placement does not change which jumps miss the uop cache.')
    [void]$md.AppendLine()
}
[void]$md.AppendLine("$runs alternations (AB, BA, ...) $Times $(if ($meta) { $meta.rounds } else { $Rounds }) rounds; medians of all samples per side. Negative = B faster/smaller. A row is marked * when it changed by more than the noise threshold (the larger of 3% and twice the median |$($Delta)| of the FlatBuffers and MessagePack rows, which are identical code on both sides), and in every alternation by at least half the threshold in the same direction. The geometric means cover all Tessera rows; the control's is the drift between the sides.")
[void]$md.AppendLine()

# key -> @{ All = every sample; Runs = alternation -> median of that run's samples }
function Collect($files, [scriptblock]$rows, [scriptblock]$key, [scriptblock]$samples) {
    $m = @{}
    foreach ($f in $files) {
        $run = [int]($f.BaseName -replace '.*-', '')
        $j = Get-Content $f.FullName -Raw | ConvertFrom-Json
        foreach ($r in (& $rows $j)) {
            $k = & $key $r
            if (-not $m.ContainsKey($k)) { $m[$k] = @{ All = New-Object System.Collections.Generic.List[double]; Runs = @{} } }
            $s = [double[]]@(& $samples $r)
            foreach ($x in $s) { $m[$k].All.Add($x) }
            $m[$k].Runs[$run] = Median $s
        }
    }
    return $m
}

function Report([string]$title, $a, $b, [scriptblock]$isControl) {
    $lines = @(); $control = @(); $ratios = @(); $controlRatios = @()
    foreach ($k in $a.Keys) {
        if (-not $b.ContainsKey($k)) { continue }
        $ma = Median $a[$k].All.ToArray(); $mb = Median $b[$k].All.ToArray(); $p = Pct $ma $mb
        # The change in each alternation (both sides ran in it).
        $runs = @(foreach ($run in $a[$k].Runs.Keys) { if ($b[$k].Runs.ContainsKey($run)) { Pct $a[$k].Runs[$run] $b[$k].Runs[$run] } })
        if (& $isControl $k) { $control += [Math]::Abs($p); $controlRatios += $mb / $ma }
        else { $lines += [pscustomobject]@{ Key = $k; A = $ma; B = $mb; P = $p; Runs = $runs; Consistent = $false }; $ratios += $mb / $ma }
    }
    $noise = if ($control.Count) { Median $control } else { 0 }
    $threshold = [Math]::Max(3, 2 * $noise)
    # A bimodal case can land in its slow mode more often on one side: every alternation must show the change.
    foreach ($l in $lines) { $l.Consistent = @($l.Runs | Where-Object { [Math]::Sign($_) -ne [Math]::Sign($l.P) -or [Math]::Abs($_) -lt $threshold / 2 }).Count -eq 0 }
    $marked = @($lines | Where-Object { [Math]::Abs($_.P) -gt $threshold -and $_.Consistent })
    $faster = @($marked | Where-Object { $_.P -lt 0 }).Count; $slower = $marked.Count - $faster
    $geo = GeoPct $ratios; $geoControl = GeoPct $controlRatios
    $summary.Add("| $title | $(FmtPct $geo) | $(FmtPct $geoControl) | $faster | $slower |")
    [void]$md.AppendLine("## $title")
    [void]$md.AppendLine()
    [void]$md.AppendLine("Tessera rows: geometric mean $(FmtPct $geo) (control drift $(FmtPct $geoControl)). Noise (median |$($Delta)| of the unchanged libraries): $($noise.ToString('0.0', $inv))%; threshold $($threshold.ToString('0.0', $inv))%.")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Case | A | B | $($Delta) |")
    [void]$md.AppendLine('|---|---:|---:|---:|')
    foreach ($l in ($lines | Sort-Object Key)) {
        $mark = if ([Math]::Abs($l.P) -gt $threshold -and $l.Consistent) { ' *' } elseif ([Math]::Abs($l.P) -gt $threshold) { ' (mixed)' } else { '' }
        [void]$md.AppendLine("| $($l.Key) | $($l.A.ToString('0.###', $inv)) | $($l.B.ToString('0.###', $inv)) | $(FmtPct $l.P)$mark |")
    }
    [void]$md.AppendLine()
}

$body = New-Object System.Text.StringBuilder
$head = $md; $md = $body
foreach ($c in $Compilers) {
    $fa = @(Get-ChildItem $results -Filter "native-$c-A-*.json"); $fb = @(Get-ChildItem $results -Filter "native-$c-B-*.json")
    if (-not $fa.Count -or -not $fb.Count) { continue }
    $rows = { param($j) $j.read }; $key = { param($r) "$($r.workload) / $($r.library) / $($r.operation)" }; $samples = { param($r) $r.samples_us }
    Report "C++ read, $c ($($Micro)s)" (Collect $fa $rows $key $samples) (Collect $fb $rows $key $samples) { param($k) $k -match ' / (FlatBuffers|MessagePack) / ' }
}
$da = @(Get-ChildItem $results -Filter 'dotnet-A-*.json'); $db = @(Get-ChildItem $results -Filter 'dotnet-B-*.json')
if ($da.Count -and $db.Count) {
    $rows = { param($j) $j.write }; $key = { param($r) "$($r.workload) / $($r.library) / $($r.variant)" }; $samples = { param($r) $r.samples_us }
    Report ".NET write ($($Micro)s)" (Collect $da $rows $key $samples) (Collect $db $rows $key $samples) { param($k) $k -match ' / (FlatBuffers|MessagePack)' }
}

# Sizes are exact: the exported buffers' lengths, recorded when each side was built.
$sizeLines = @()
$sa = Join-Path $results 'sizes-A.json'; $sb = Join-Path $results 'sizes-B.json'
if ((Test-Path $sa) -and (Test-Path $sb)) {
    $la = Get-Content $sa -Raw | ConvertFrom-Json; $lb = Get-Content $sb -Raw | ConvertFrom-Json
    foreach ($p in $lb.PSObject.Properties) {
        $x = $la.PSObject.Properties[$p.Name]
        if ($x -and $x.Value -ne $p.Value) { $sizeLines += "| $($p.Name) | $(([long]$x.Value).ToString('N0', $inv)) | $(([long]$p.Value).ToString('N0', $inv)) | $(FmtPct (Pct $x.Value $p.Value)) |" }
    }
    [void]$md.AppendLine('## Size (bytes)')
    [void]$md.AppendLine()
    if ($sizeLines.Count) { [void]$md.AppendLine("| Buffer | A | B | $($Delta) |"); [void]$md.AppendLine('|---|---:|---:|---:|'); $sizeLines | ForEach-Object { [void]$md.AppendLine($_) } }
    else { [void]$md.AppendLine('All buffers have the same size.') }
    $summary.Add("| Size | $(if ($sizeLines.Count) { "$($sizeLines.Count) buffers changed" } else { 'unchanged' }) | | | |")
}

[void]$head.AppendLine("| Section | Tessera geomean $($Delta) | Control drift | Faster rows | Slower rows |")
[void]$head.AppendLine('|---|---:|---:|---:|---:|')
foreach ($s in $summary) { [void]$head.AppendLine($s) }
[void]$head.AppendLine()
$text = $head.ToString() + $body.ToString()
if ($Out) { [IO.File]::WriteAllText((Join-Path $root $Out), $text, (New-Object System.Text.UTF8Encoding $false)) }
Write-Output $text
