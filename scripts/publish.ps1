<#
.SYNOPSIS
  Build the mod and prepare an uploadable Workshop payload in dist.

.DESCRIPTION
  This script performs a two-phase publish prep:

    1. Build PawnDiary.dll into a throwaway temp folder.
    2. Assemble a clean payload folder with runnable mod assets, source code, and reference docs,
       then normalize the published mod name/packageId by stripping "(development)" markers.

  Branch/tag creation from earlier versions is intentionally disabled; this keeps the script focused
  on local payload prep and is suitable for simple, repeatable Workshop uploads.

.PARAMETER Version
  Optional version stamp used for the temp build folder name. Defaults to release-<today>.

.PARAMETER OutDir
  Output folder for the release payload. Defaults to <repo>/dist/<published packageId>.

.PARAMETER Configuration
  MSBuild configuration for PawnDiary.dll. Default: Release.

.PARAMETER PackageId
  Override the published packageId. By default strips a trailing "(developement)" /
  "(development)" marker from the source About.xml packageId.

.PARAMETER Author
  Override the published <author> value. By default keeps the source value.

.PARAMETER InstallToMods
  Create or refresh a junction in your RimWorld Mods folder that points to the built dist payload.

.PARAMETER ModsDir
  RimWorld Mods folder used when -InstallToMods is enabled. Defaults to your current repo's
  parent Mods directory if it exists.

.PARAMETER LinkName
  Folder name to create under -ModsDir for the junction.
#>
[CmdletBinding()]
param(
    [string]$Version = "release-$(Get-Date -Format yyyyMMdd)",
    [string]$OutDir,
    [string]$Configuration = "Release",
    [string]$PackageId,
    [string]$Author,
    [switch]$InstallToMods,
    [string]$ModsDir,
    [string]$LinkName,
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
    $regex = [regex]::new("(?s)(<$Element>\s*)(.*?)(\s*</$Element>)")
    return $regex.Replace($Text, ('${1}' + $replacementValue + '${3}'), 1)
}

function Remove-DevelopmentPostfix {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    $clean = $Value.Trim()
    $clean = $clean -replace '\s*\((?:developement|development)\)\s*$', ''
    $clean = $clean -replace '[._-](?:developement|development)$', ''
    return $clean.Trim()
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

function Copy-SourcePayload {
    $src = Join-Path $repoRoot "Source"
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Required path missing: Source"
    }

    $dest = Join-Path $OutDir "Source"
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force

    $artifactDirs = Get-ChildItem -LiteralPath $dest -Directory -Recurse -Force |
        Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" } |
        Sort-Object FullName -Descending
    foreach ($dir in $artifactDirs) {
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    }
}

function Resolve-ModsFolder {
    param([string]$Candidate)
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }
    $expanded = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Candidate)
    if (Test-Path -LiteralPath $expanded) { return $expanded }
    return $null
}

function Install-ModJunction {
    param(
        [string]$DestinationRoot,
        [string]$FolderName,
        [string]$TargetFolder,
        [switch]$AllowReplace
    )

    if (-not (Test-Path -LiteralPath $DestinationRoot)) {
        throw "Mods folder does not exist: $DestinationRoot"
    }

    $linkPath = Join-Path $DestinationRoot $FolderName
    if (Test-Path -LiteralPath $linkPath) {
        $existing = Get-Item -LiteralPath $linkPath
        $existingTarget = $existing.Target
        if ($existingTarget) {
            $existingTarget = $existingTarget | Select-Object -First 1
            $normalizedExisting = [System.IO.Path]::GetFullPath($existingTarget.TrimEnd('\', '/'))
            $normalizedTarget = [System.IO.Path]::GetFullPath($TargetFolder.TrimEnd('\', '/'))
            if ($normalizedExisting -eq $normalizedTarget) {
                Write-Host "  mods link  : existing link already points to payload ($linkPath -> $TargetFolder)"
                return
            }
            if (-not $AllowReplace) {
                throw "Mods entry exists but points elsewhere: $linkPath -> $existingTarget. Use -Force to replace."
            }
        } elseif (-not $AllowReplace) {
            throw "Mods entry exists and is not a link: $linkPath. Use -Force to replace."
        }

        Remove-Item -LiteralPath $linkPath -Recurse -Force
    }

    New-Item -ItemType Junction -Path $linkPath -Target $TargetFolder | Out-Null
    Write-Host "  mods link  : $linkPath -> $TargetFolder"
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
if (-not $LinkName) { $LinkName = $payloadFolderName }

$resolvedModsDir = Resolve-ModsFolder $ModsDir
if (-not $resolvedModsDir) {
    $fallbackMods = Split-Path $repoRoot -Parent
    $resolvedModsDir = Resolve-ModsFolder $fallbackMods
}

Write-Host "$publishedModName publish prep" -ForegroundColor Green
Write-Host "  version     : $Version"
Write-Host "  build mode  : $Configuration"
Write-Host "  mod name    : $publishedModName"
Write-Host "  packageId   : $publishedPackageId"
Write-Host "  payload out : $OutDir"
if ($SkipBranch -or $Force) {
    Write-Host "  branch mode: disabled (flags accepted for compatibility)"
}
if ($InstallToMods) {
    if (-not $resolvedModsDir) {
        throw "Could not determine Mods folder automatically. Please pass -ModsDir explicitly."
    }
    Write-Host "  install to  : $resolvedModsDir\$LinkName"
}

Write-Step "Build PawnDiary.dll ($Configuration)"
$msbuild = Find-MSBuild
$buildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-build-$Version"
if (Test-Path -LiteralPath $buildOut) { Remove-Item -LiteralPath $buildOut -Recurse -Force }
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
if (-not (Test-Path -LiteralPath $builtDll)) { throw "Build reported success but PawnDiary.dll was not produced in $buildOut." }

Write-Step "Prepare dist payload"
if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# About metadata + preview + published-file-id, all required for Workshop updates.
Copy-Payload "About\About.xml" -Required | Out-Null
Copy-Payload "About\Preview.png" -Required | Out-Null
Copy-Payload "About\ModIcon.png" | Out-Null
Copy-Payload "About\PublishedFileId.txt" | Out-Null

# Core runtime assets.
Copy-Payload "1.6\Defs" -Required | Out-Null
Copy-Payload "Textures" | Out-Null
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
$payloadDll = Join-Path $asmDir "PawnDiary.dll"
Copy-Item -LiteralPath $builtDll -Destination $payloadDll -Force
$harmony = Join-Path $buildOut "0Harmony.dll"
if (Test-Path -LiteralPath $harmony) {
    Copy-Item -LiteralPath $harmony -Destination $asmDir -Force
} else {
    Copy-Payload "1.6\Assemblies\0Harmony.dll" -Required | Out-Null
}

# Ship readable source and reference docs with the Workshop payload.
Copy-SourcePayload
Copy-Payload "README.md" | Out-Null
Copy-Payload "DOCUMENTATION.md" -Required | Out-Null
Copy-Payload "CHANGELOG.md" -Required | Out-Null
Copy-Payload "EVENT_PROMPT_MAP.md" -Required | Out-Null
foreach ($doc in @("LICENSE", "LICENSE.txt", "LICENSE.md")) {
    Copy-Payload $doc | Out-Null
}

Remove-Item -LiteralPath $buildOut -Recurse -Force -ErrorAction SilentlyContinue

$payloadFiles = Get-ChildItem -Recurse -File $OutDir
$payloadBytes = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
Write-Step "Done"
Write-Host "Uploadable payload prepared:" -ForegroundColor Green
Write-Host "  $OutDir"
Write-Host "  Payload DLL: $payloadDll"
Write-Host "  Prepared About.xml: $aboutDest"
Write-Host ("  shipped {0} files, {1:N2} MB" -f $payloadFiles.Count, ($payloadBytes / 1MB))

if ($InstallToMods) {
    Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $LinkName -TargetFolder $OutDir -AllowReplace:$Force
}
