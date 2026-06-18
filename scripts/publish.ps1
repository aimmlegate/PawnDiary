<#
.SYNOPSIS
  Build the mod and prepare an uploadable Workshop payload in dist.

.DESCRIPTION
  This script performs a two-phase publish prep:

    1. Build PawnDiary.dll into a throwaway temp folder.
    2. Assemble a clean payload folder (About, 1.6/Defs, Languages, assemblies, docs) and
       normalize the published mod name/packageId by stripping "(development)" markers.

  Branch/tag creation from earlier versions is intentionally disabled; this keeps the script focused
  on local payload prep and is suitable for simple, repeatable Workshop uploads.

.PARAMETER Version
  Optional version stamp used for the temp build folder name. Defaults to beta-<today>.

.PARAMETER OutDir
  Output folder for the release payload. Defaults to <repo>/dist/<published packageId>.

.PARAMETER Configuration
  MSBuild configuration for PawnDiary.dll. Default: Release.

.PARAMETER PackageId
  Override the published packageId. By default strips a trailing "(developement)" /
  "(development)" marker from the source About.xml packageId.

.PARAMETER Author
  Override the published <author> value. By default keeps the source value.
#>
[CmdletBinding()]
param(
    [string]$Version = "beta-$(Get-Date -Format yyyyMMdd)",
    [string]$OutDir,
    [string]$Configuration = "Release",
    [string]$PackageId,
    [string]$Author,
    [switch]$SkipBranch,
    [switch]$Force
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
        throw "Command failed (exit ${LASTEXITCODE}): $FilePath $($Arguments -join ' ')"
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
    throw "MSBuild was not found. Install Visual Studio (Build Tools) or add MSBuild to PATH."
}

function Get-AboutValue {
    param([string]$Text, [string]$Element)
    $match = [regex]::Match($Text, "(?s)<$Element>\s*(.*?)\s*</$Element>")
    if (-not $match.Success) { return "" }
    return $match.Groups[1].Value.Trim()
}

function Set-AboutValue {
    param([string]$Text, [string]$Element, [string]$Value)
    $escapedValue = [System.Security.SecurityElement]::Escape($Value)
    $replacementValue = $escapedValue -replace '\$', '$$'
    return [regex]::Replace(
        $Text,
        "(?s)(<$Element>\s*)(.*?)(\s*</$Element>)",
        ('${1}' + $replacementValue + '${3}')
    )
}

function Remove-DevelopmentPostfix {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return ($Value -replace '\s*\((?:developement|development)\)\s*$', '').Trim()
}

function Get-SafeFolderName {
    param([string]$Value, [string]$Fallback)
    $folderName = $Value
    if ([string]::IsNullOrWhiteSpace($folderName)) { $folderName = $Fallback }
    foreach ($invalidChar in [System.IO.Path]::GetInvalidFileNameChars()) {
        $folderName = $folderName.Replace([string]$invalidChar, "")
    }
    $folderName = ($folderName -replace '\s+', ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($folderName)) { return $Fallback }
    return $folderName
}

function Copy-Payload {
    param([string]$RelPath, [switch]$Required)
    $src = Join-Path $repoRoot $RelPath
    if (-not (Test-Path -LiteralPath $src)) {
        if ($Required) { throw "Required path missing: $RelPath" }
        return $false
    }
    $dest = Join-Path $OutDir $RelPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    return $true
}

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw "Not inside a git repository." }
Set-Location -LiteralPath $repoRoot

$sourceAboutPath = Join-Path $repoRoot "About\About.xml"
if (-not (Test-Path -LiteralPath $sourceAboutPath)) { throw "Missing required file: About\About.xml" }
$sourceAboutText = [System.IO.File]::ReadAllText($sourceAboutPath)
$devModName = Get-AboutValue $sourceAboutText "name"
$devPackageId = Get-AboutValue $sourceAboutText "packageId"

$publishedModName = if ([string]::IsNullOrWhiteSpace($PackageId)) {
    Remove-DevelopmentPostfix $devModName
} else {
    $devModName
}
$publishedPackageId = if ([string]::IsNullOrWhiteSpace($PackageId)) {
    Remove-DevelopmentPostfix $devPackageId
} else {
    $PackageId.Trim()
}
if ([string]::IsNullOrWhiteSpace($publishedModName)) { $publishedModName = "PawnDiary" }
if ([string]::IsNullOrWhiteSpace($publishedPackageId)) { $publishedPackageId = "aimmlegate.pawndiary" }

$payloadFolderName = Get-SafeFolderName $publishedPackageId "pawn-diary"
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "dist\$payloadFolderName" }

Write-Host "$publishedModName publish prep" -ForegroundColor Green
Write-Host "  version     : $Version"
Write-Host "  build mode  : $Configuration"
Write-Host "  mod name    : $publishedModName"
Write-Host "  packageId   : $publishedPackageId"
Write-Host "  payload out : $OutDir"
if ($SkipBranch -or $Force) {
    Write-Host "  branch mode: disabled (flags accepted for compatibility)"
}

Write-Step "Build PawnDiary.dll ($Configuration)"
$msbuild = Find-MSBuild
$buildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-build-$Version"
if (Test-Path $buildOut) { Remove-Item -Recurse -Force $buildOut }
New-Item -ItemType Directory -Force -Path $buildOut | Out-Null

Invoke-Native $msbuild @(
    (Join-Path $repoRoot "Source\PawnDiary.csproj"),
    "/t:Rebuild",
    "/p:Configuration=$Configuration",
    "/p:OutputPath=$buildOut",
    "/nologo",
    "/v:minimal"
)

$builtDll = Join-Path $buildOut "PawnDiary.dll"
if (-not (Test-Path $builtDll)) { throw "Build reported success but PawnDiary.dll was not produced in $buildOut." }

Write-Step "Prepare dist payload"
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# About metadata + preview + published-file-id, all required for Workshop updates.
Copy-Payload "About\About.xml" -Required | Out-Null
Copy-Payload "About\Preview.png" -Required | Out-Null
Copy-Payload "About\PublishedFileId.txt" | Out-Null

# Core runtime assets.
Copy-Payload "1.6\Defs" -Required | Out-Null
Copy-Payload "Languages" | Out-Null

$aboutDest = Join-Path $OutDir "About\About.xml"
$aboutText = [System.IO.File]::ReadAllText($aboutDest)

$payloadModName = Get-AboutValue $aboutText "name"
if (-not [string]::IsNullOrWhiteSpace($payloadModName)) {
    $cleanName = Remove-DevelopmentPostfix $payloadModName
    if ([string]::IsNullOrWhiteSpace($cleanName)) { $cleanName = $payloadModName.Trim() }
    if ($cleanName -ne $payloadModName) {
        $aboutText = Set-AboutValue $aboutText "name" $cleanName
        Write-Host "  name      : '$payloadModName' -> '$cleanName'"
    } else {
        Write-Host "  name      : $cleanName"
    }
}

$devId = Get-AboutValue $aboutText "packageId"
if (-not [string]::IsNullOrWhiteSpace($devId)) {
    $cleanId = if ([string]::IsNullOrWhiteSpace($PackageId)) {
        Remove-DevelopmentPostfix $devId
    } else {
        $PackageId.Trim()
    }
    if ([string]::IsNullOrWhiteSpace($cleanId)) { $cleanId = $devId.Trim() }

    if ($cleanId -ne $devId) {
        $aboutText = Set-AboutValue $aboutText "packageId" $cleanId
        Write-Host "  packageId : '$devId' -> '$cleanId'"
    } else {
        Write-Host "  packageId : $cleanId"
    }
} else {
    throw "About.xml does not contain a <packageId>."
}

if (-not [string]::IsNullOrWhiteSpace($Author)) {
    $devAuthor = Get-AboutValue $aboutText "author"
    $aboutText = Set-AboutValue $aboutText "author" $Author.Trim()
    Write-Host "  author    : '$devAuthor' -> '$Author'"
}

[System.IO.File]::WriteAllText($aboutDest, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

# Assemblies for Workshop release.
$asmDir = Join-Path $OutDir "1.6\Assemblies"
New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $asmDir -Force
$harmony = Join-Path $buildOut "0Harmony.dll"
if (Test-Path $harmony) {
    Copy-Item -LiteralPath $harmony -Destination $asmDir -Force
} else {
    Copy-Payload "1.6\Assemblies\0Harmony.dll" -Required | Out-Null
}

foreach ($doc in @("README.md", "CHANGELOG.md", "LICENSE", "LICENSE.txt", "LICENSE.md")) {
    Copy-Payload $doc | Out-Null
}

Remove-Item -Recurse -Force $buildOut -ErrorAction SilentlyContinue

$payloadFiles = Get-ChildItem -Recurse -File $OutDir
$payloadBytes = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
Write-Step "Done"
Write-Host "Uploadable payload prepared:" -ForegroundColor Green
Write-Host "  $OutDir"
Write-Host "  Built DLL: $builtDll"
Write-Host "  Prepared About.xml: $aboutDest"
Write-Host ("  shipped {0} files, {1:N2} MB" -f $payloadFiles.Count, ($payloadBytes / 1MB))
