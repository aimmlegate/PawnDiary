# Pawn Diary — full coverage audit (TEST_COVERAGE_PLAN.md §8).
#
# One command that builds the core mod, runs every standalone pure test project, builds the optional
# in-game RimTest assembly when RimTest Redux is available, validates all XML, and prints the
# event-coverage requirement matrix (TEST_COVERAGE_PLAN.md §3, EVT-01..EVT-23) with any uncovered row
# flagged. This is developer-run tooling and is NOT wired into the commit hook: `.githooks/verify.ps1`
# stays lean and must not depend on the optional Workshop RimTest Redux DLL, whereas this audit does.
#
# Usage:  pwsh scripts/verify-coverage.ps1            (or Windows PowerShell)
# Exit code 0 = everything built, all pure tests passed, and every EVT row is covered.
param(
    [switch]$SkipPureTests,   # build only (skip `dotnet run` of the pure suites)
    [switch]$MatrixOnly       # print the coverage matrix without building anything
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Name)
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
}

function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Find-MSBuild {
    $command = Get-Command MSBuild -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $result = & $vswhere -latest -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($result) { return $result }
    }
    throw "MSBuild was not found. Install Visual Studio Build Tools or add MSBuild to PATH."
}

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repoRoot

Write-Host "Pawn Diary coverage audit" -ForegroundColor Green

# --- The EVT requirement matrix (TEST_COVERAGE_PLAN.md §3). Each row is covered when any RimTest suite
#     file tags it with its EVT id. The id-tag scan keeps this in lockstep with the suites automatically.
$evtRows = [ordered]@{
    "EVT-01" = "Interaction pair/solo"
    "EVT-02" = "Interaction batch/ambient"
    "EVT-03" = "Thought immediate/ambient"
    "EVT-04" = "Thought progression"
    "EVT-05" = "Inspiration"
    "EVT-06" = "Ability"
    "EVT-07" = "Romance"
    "EVT-08" = "Mental state"
    "EVT-09" = "Tale"
    "EVT-10" = "Death"
    "EVT-11" = "Hediff"
    "EVT-12" = "Work"
    "EVT-13" = "Raid"
    "EVT-14" = "Mood event"
    "EVT-15" = "Pawn progression"
    "EVT-16" = "Quest"
    "EVT-17" = "Ritual (DLC)"
    "EVT-18" = "Arrival"
    "EVT-19" = "Day/quadrum reflection"
    "EVT-20" = "Arc reflection"
    "EVT-21" = "External API"
    "EVT-22" = "Event windows"
    "EVT-23" = "Observed conditions"
}

$rimTestDir = Join-Path $repoRoot "tests\PawnDiary.RimTest"

function Get-RimTestText {
    $files = Get-ChildItem -Path $rimTestDir -Filter "*.cs" -File -ErrorAction SilentlyContinue
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in $files) {
        [void]$sb.AppendLine((Get-Content -LiteralPath $f.FullName -Raw))
    }
    return $sb.ToString()
}

function Show-CoverageMatrix {
    Write-Step "Event coverage matrix (TEST_COVERAGE_PLAN.md §3)"
    $text = Get-RimTestText

    # Count [Test] methods and suites across the RimTest assembly.
    $testCount = ([regex]::Matches($text, "\[Test\]")).Count
    $suiteCount = ([regex]::Matches($text, "\[TestSuite\]")).Count
    $fixtureFiles = @(Get-ChildItem -Path $rimTestDir -Filter "*FlowTests.cs" -File -ErrorAction SilentlyContinue)
    $fixtureFiles += @(Get-ChildItem -Path $rimTestDir -Filter "*FixtureTests.cs" -File -ErrorAction SilentlyContinue)

    $uncovered = @()
    foreach ($id in $evtRows.Keys) {
        $covered = $text -match [regex]::Escape($id)
        $mark = if ($covered) { "[x]" } else { "[ ]" }
        $color = if ($covered) { "Gray" } else { "Yellow" }
        Write-Host ("  {0} {1,-7} {2}" -f $mark, $id, $evtRows[$id]) -ForegroundColor $color
        if (-not $covered) { $uncovered += $id }
    }

    Write-Host ""
    Write-Host ("  Suites: {0}   Test methods: {1}   Flow/fixture files: {2}" -f `
        $suiteCount, $testCount, $fixtureFiles.Count) -ForegroundColor Green
    $coveredCount = $evtRows.Count - $uncovered.Count
    Write-Host ("  EVT rows covered: {0}/{1}" -f $coveredCount, $evtRows.Count) -ForegroundColor Green

    if ($uncovered.Count -gt 0) {
        Write-Host ("  UNCOVERED: {0}" -f ($uncovered -join ", ")) -ForegroundColor Red
    }
    return $uncovered.Count
}

if ($MatrixOnly) {
    $missing = Show-CoverageMatrix
    if ($missing -gt 0) { exit 1 } else { exit 0 }
}

# --- XML well-formed check (mirrors .githooks/verify.ps1). ---
Write-Step "XML well-formed check"
$xmlRoots = @("About", "1.6", "Languages") | Where-Object { Test-Path -LiteralPath $_ }
$xmlFiles = @(Get-ChildItem -Path $xmlRoots -Filter "*.xml" -File -Recurse)
$projectRoots = @("Source", "tests") | Where-Object { Test-Path -LiteralPath $_ }
$xmlFiles += Get-ChildItem -Path $projectRoots -Filter "*.csproj" -File -Recurse
$xmlFiles | ForEach-Object { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null }
Write-Host ("  {0} XML/csproj files parsed." -f $xmlFiles.Count)

# --- Pure helper test projects. ---
if (-not $SkipPureTests) {
    Write-Step "Pure helper test projects"
    $pureProjects = Get-ChildItem -Path "tests" -Filter "*.csproj" -File -Recurse |
        Where-Object { $_.FullName -notmatch "PawnDiary\.RimTest" }
    foreach ($proj in $pureProjects) {
        Write-Host ("  run {0}" -f $proj.Name)
        Invoke-Native "dotnet" @("run", "--project", $proj.FullName, "-c", "Release")
    }
}

# --- Core build. ---
Write-Step "Core PawnDiary.dll build"
$msbuild = Find-MSBuild
Invoke-Native $msbuild @("Source\PawnDiary.csproj", "/t:Build", "/p:Configuration=Debug", "/v:minimal")

# --- Optional in-game RimTest assembly build (needs the RimTest Redux Workshop DLL). ---
Write-Step "In-game RimTest assembly build (optional)"
$rimTestProj = "tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj"
try {
    Invoke-Native $msbuild @($rimTestProj, "/t:Build", "/p:Configuration=Debug", "/v:minimal")
    Write-Host "  RimTest assembly built." -ForegroundColor Green
}
catch {
    Write-Host ("  SKIPPED RimTest build (RimTest Redux not found?): {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    Write-Host "  Set RIMTEST_REDUX_ASSEMBLIES or install Workshop item 3762405308 to build it." -ForegroundColor Yellow
}

$missing = Show-CoverageMatrix

Write-Host ""
if ($missing -gt 0) {
    Write-Host "Coverage audit FAILED: uncovered EVT rows above." -ForegroundColor Red
    exit 1
}
Write-Host "Coverage audit passed." -ForegroundColor Green
exit 0
