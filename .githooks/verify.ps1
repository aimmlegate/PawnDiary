param(
    [string]$HookName = "manual"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Name)
    Write-Host ""
    Write-Host "==> $Name"
}

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Find-MSBuild {
    $command = Get-Command MSBuild -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $result = & $vswhere -latest -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($result) {
            return $result
        }
    }

    throw "MSBuild was not found. Install Visual Studio Build Tools or add MSBuild to PATH."
}

if ($env:PAWNDIARY_SKIP_VERIFY_HOOKS -eq "1") {
    Write-Host "PawnDiary verification hook skipped because PAWNDIARY_SKIP_VERIFY_HOOKS=1."
    exit 0
}

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repoRoot

Write-Host "PawnDiary $HookName verification"

Write-Step "Whitespace check"
if ($HookName -eq "pre-commit") {
    Invoke-Native "git" @("diff", "--check", "--cached")
} else {
    Invoke-Native "git" @("diff", "--check")
    Invoke-Native "git" @("diff", "--check", "--cached")
}

Write-Step "XML well-formed check"
$xmlRoots = @("About", "1.6", "Languages") | Where-Object { Test-Path -LiteralPath $_ }
$xmlFiles = @(Get-ChildItem -Path $xmlRoots -Filter "*.xml" -File -Recurse)
$projectRoots = @("Source", "tests") | Where-Object { Test-Path -LiteralPath $_ }
$xmlFiles += Get-ChildItem -Path $projectRoots -Filter "*.csproj" -File -Recurse
$xmlFiles | ForEach-Object {
    [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null
}

Write-Step "Pure helper tests"
Invoke-Native "dotnet" @("run", "--project", "tests\LlmResponseParserTests\LlmResponseParserTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryPipelineTests\DiaryPipelineTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryBiotechPolicyTests\DiaryBiotechPolicyTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryOdysseyPolicyTests\DiaryOdysseyPolicyTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\NarrativeContinuityTests\NarrativeContinuityTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryTextDecorationTests\DiaryTextDecorationTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryCapturePolicyTests\DiaryCapturePolicyTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\PromptVariantsTests\PromptVariantsTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryRetentionTests\DiaryRetentionTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiarySaveNormalizationTests\DiarySaveNormalizationTests.csproj")
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryObservedConditionTests\DiaryObservedConditionTests.csproj")

Write-Step "RimWorld DLL build"
$msbuild = Find-MSBuild
Invoke-Native $msbuild @("Source\PawnDiary.csproj", "/t:Build", "/p:Configuration=Debug")

Write-Step "Committed DLL freshness check"
# Pawn Diary must not ship Harmony — its runtime comes from the active brrainz.harmony mod. The
# build-time reference copy (Source/Libs/0Harmony.dll) stays for compilation only and must never
# appear in the shipped 1.6/Assemblies/ output.
$sourceHarmony = Join-Path $repoRoot "Source\Libs\0Harmony.dll"
$runtimeHarmony = Join-Path $repoRoot "1.6\Assemblies\0Harmony.dll"
if (-not (Test-Path -LiteralPath $sourceHarmony)) {
    throw "Missing build reference dependency: Source/Libs/0Harmony.dll"
}
if (Test-Path -LiteralPath $runtimeHarmony) {
    throw "Pawn Diary must not bundle Harmony: 1.6/Assemblies/0Harmony.dll exists. The runtime comes from brrainz.harmony."
}

& git diff --quiet -- "1.6/Assemblies/PawnDiary.dll"
if ($LASTEXITCODE -ne 0) {
    throw "Build changed committed runtime DLL. Stage the rebuilt PawnDiary.dll and retry."
}

Write-Host ""
Write-Host "PawnDiary $HookName verification passed."
