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
Invoke-Native "dotnet" @("run", "--project", "tests\DiaryTextDecorationTests\DiaryTextDecorationTests.csproj")

Write-Step "RimWorld DLL build"
$msbuild = Find-MSBuild
Invoke-Native $msbuild @("Source\PawnDiary.csproj", "/t:Build", "/p:Configuration=Debug")

Write-Step "Committed DLL freshness check"
$sourceHarmony = Join-Path $repoRoot "Source\Libs\0Harmony.dll"
$runtimeHarmony = Join-Path $repoRoot "1.6\Assemblies\0Harmony.dll"
if (-not (Test-Path -LiteralPath $runtimeHarmony)) {
    throw "Missing committed runtime dependency: 1.6/Assemblies/0Harmony.dll"
}
if (-not (Test-Path -LiteralPath $sourceHarmony)) {
    throw "Missing build reference dependency: Source/Libs/0Harmony.dll"
}
if ((Get-FileHash -LiteralPath $sourceHarmony).Hash -ne (Get-FileHash -LiteralPath $runtimeHarmony).Hash) {
    throw "Source/Libs/0Harmony.dll differs from 1.6/Assemblies/0Harmony.dll. Update and stage the runtime copy."
}

& git diff --quiet -- "1.6/Assemblies/PawnDiary.dll" "1.6/Assemblies/0Harmony.dll"
if ($LASTEXITCODE -ne 0) {
    throw "Build changed committed runtime DLLs. Stage the rebuilt DLL(s) and retry."
}

Write-Host ""
Write-Host "PawnDiary $HookName verification passed."
