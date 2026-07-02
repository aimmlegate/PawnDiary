<#
.SYNOPSIS
  Build the mod and prepare an uploadable Workshop payload in dist.

.DESCRIPTION
  This script performs a two-phase publish prep:

    1. Build PawnDiary.dll into a throwaway temp folder.
    2. Assemble a clean payload folder with runnable mod assets and reference docs,
       then normalize the published mod name/packageId by stripping "(development)" markers.

  Branch/tag creation from earlier versions is intentionally disabled; this keeps the script focused
  on local payload prep and is suitable for simple, repeatable Workshop uploads.

.PARAMETER Version
  Optional mod version written to About/About.xml in the release payloads. Defaults to the source
  About.xml <modVersion> value, falling back to release-<today> when the source metadata is blank.

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
  Create or refresh junctions in your RimWorld Mods folder that point to the built dist payloads.
  This is enabled by default; pass -InstallToMods:$false to prepare dist only.

.PARAMETER ModsDir
  RimWorld Mods folder used when -InstallToMods is enabled. Defaults to your current repo's
  parent Mods directory if it exists.

.PARAMETER LinkName
  Folder name to create under -ModsDir for the junction.

.PARAMETER SplitRussianLocalization
  Build a second payload for the Russian language files and remove Russian from the main payload.
  This is enabled by default; pass -SplitRussianLocalization:$false or -IncludeRussianInMainPayload
  for the legacy bundled-language payload.

.PARAMETER IncludeRussianInMainPayload
  Legacy packaging mode: keep Russian in the main payload and skip the separate localization payload.

.PARAMETER RussianLocalizationOutDir
  Output folder for the Russian localization payload. Defaults to
  <repo>/dist/<published packageId>.russian.

.PARAMETER RussianLocalizationPackageId
  Override the Russian localization packageId. Defaults to <published packageId>.russian.

.PARAMETER RussianLocalizationPublishedFileId
  Workshop item id for the Russian localization payload. When set, it is written as that payload's
  About/PublishedFileId.txt. Leave blank before the first Workshop upload.

.PARAMETER RussianLocalizationPublishedFileIdPath
  Optional source file for the Russian localization Workshop id. Defaults to
  About/PublishedFileId-Russian.txt and is copied as About/PublishedFileId.txt when present.

.PARAMETER RussianLocalizationLinkName
  Folder name to create under -ModsDir for the Russian localization junction when both
  -InstallToMods and the separate Russian localization payload are enabled.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutDir,
    [string]$Configuration = "Release",
    [string]$PackageId,
    [string]$Author,
    [switch]$InstallToMods = $true,
    [string]$ModsDir,
    [string]$LinkName,
    [switch]$SplitRussianLocalization = $true,
    [switch]$IncludeRussianInMainPayload,
    [string]$RussianLocalizationOutDir,
    [string]$RussianLocalizationPackageId,
    [string]$RussianLocalizationPublishedFileId,
    [string]$RussianLocalizationPublishedFileIdPath = "About\PublishedFileId-Russian.txt",
    [string]$RussianLocalizationLinkName,
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
    $match = [regex]::Match($Text, "(?s)<$Element(?:\s[^>]*)?>\s*(.*?)\s*</$Element>")
    if (-not $match.Success) { return "" }
    return $match.Groups[1].Value.Trim()
}

function Set-AboutValue {
    param([string]$Text, [string]$Element, [string]$Value)
    $escapedValue = [System.Security.SecurityElement]::Escape($Value)
    $replacementValue = $escapedValue -replace '\$', '$$'
    $regex = [regex]::new("(?s)(<$Element(?:\s[^>]*)?>\s*)(.*?)(\s*</$Element>)")
    return $regex.Replace($Text, ('${1}' + $replacementValue + '${3}'), 1)
}

function Set-OrAddAboutValue {
    param([string]$Text, [string]$Element, [string]$Value, [string]$InsertAfterElement)
    if ([regex]::IsMatch($Text, "(?s)<$Element(?:\s[^>]*)?>")) {
        return Set-AboutValue $Text $Element $Value
    }

    $escapedValue = [System.Security.SecurityElement]::Escape($Value)
    $openElement = if ($Element -eq "modVersion") { 'modVersion IgnoreIfNoMatchingField="True"' } else { $Element }
    $line = "  <$openElement>$escapedValue</$Element>"
    if (-not [string]::IsNullOrWhiteSpace($InsertAfterElement)) {
        $insertRegex = [regex]::new("(?s)(<$InsertAfterElement(?:\s[^>]*)?>.*?</$InsertAfterElement>\s*)")
        if ($insertRegex.IsMatch($Text)) {
            return $insertRegex.Replace($Text, ('${1}' + $line + [Environment]::NewLine), 1)
        }
    }

    $metadataEndRegex = [regex]::new("(?s)(\s*</ModMetaData>)")
    return $metadataEndRegex.Replace($Text, ([Environment]::NewLine + $line + '${1}'), 1)
}

function Escape-XmlText {
    param([string]$Value)
    if ([string]::IsNullOrEmpty($Value)) { return "" }
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-Utf8TextFromBase64 {
    param([string]$Value)
    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($Value))
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
    param([string]$RelPath, [switch]$Required, [string]$DestinationRoot = $OutDir)
    $src = Join-Path $repoRoot $RelPath
    if (-not (Test-Path -LiteralPath $src)) {
        if ($Required) { throw "Required path missing: $RelPath" }
        return $false
    }
    $dest = Join-Path $DestinationRoot $RelPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    return $true
}

function Get-RussianLanguageFolderName {
    param([string]$LanguagesRoot)

    if (-not (Test-Path -LiteralPath $LanguagesRoot)) {
        throw "Missing required path: Languages"
    }

    $folder = Get-ChildItem -LiteralPath $LanguagesRoot -Directory |
        Where-Object { $_.Name -like "Russian*" } |
        Select-Object -First 1
    if (-not $folder) {
        throw "Russian language folder was not found under Languages."
    }
    return $folder.Name
}

function Remove-LanguageFromPayload {
    param([string]$PayloadRoot, [string]$LanguageFolderName)

    $languagePath = Join-Path (Join-Path $PayloadRoot "Languages") $LanguageFolderName
    if (Test-Path -LiteralPath $languagePath) {
        Remove-Item -LiteralPath $languagePath -Recurse -Force
        Write-Host "  language  : excluded $LanguageFolderName from main payload"
    }
}

function Copy-RussianLocalizationPreview {
    param([string]$DestinationRoot)

    $localizedPreview = Join-Path $repoRoot "About\Preview-Russian.png"
    $destination = Join-Path $DestinationRoot "About\Preview.png"
    New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
    if (Test-Path -LiteralPath $localizedPreview) {
        Copy-Item -LiteralPath $localizedPreview -Destination $destination -Force
        return "About\Preview-Russian.png"
    }

    Copy-Payload "About\Preview.png" -Required -DestinationRoot $DestinationRoot | Out-Null
    return "About\Preview.png"
}

function New-RussianLocalizationAboutXml {
    param(
        [string]$Name,
        [string]$PackageId,
        [string]$Version,
        [string]$Author,
        [string]$MainPackageId,
        [string]$MainDisplayName,
        [string]$MainPublishedFileId
    )

    $localizedDescription = Get-Utf8TextFromBase64 "0KDRg9GB0YHQutCw0Y8g0LvQvtC60LDQu9C40LfQsNGG0LjRjyDQtNC70Y8gUGF3biBEaWFyeS4g0KLRgNC10LHRg9C10YIg0L7RgdC90L7QstC90L7QuSDQvNC+0LQgUGF3biBEaWFyeSDQuCDQtNC+0LvQttC90LAg0LfQsNCz0YDRg9C20LDRgtGM0YHRjyDQv9C+0YHQu9C1INC90LXQs9C+Lg=="

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('<?xml version="1.0" encoding="utf-8"?>')
    $lines.Add('<ModMetaData>')
    $lines.Add("  <name>$(Escape-XmlText $Name)</name>")
    $lines.Add("  <author>$(Escape-XmlText $Author)</author>")
    $lines.Add("  <packageId>$(Escape-XmlText $PackageId)</packageId>")
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $lines.Add("  <modVersion IgnoreIfNoMatchingField=`"True`">$(Escape-XmlText $Version)</modVersion>")
    }
    $lines.Add('  <supportedVersions>')
    $lines.Add('    <li>1.6</li>')
    $lines.Add('  </supportedVersions>')
    $lines.Add('  <modDependencies>')
    $lines.Add('    <li>')
    $lines.Add("      <packageId>$(Escape-XmlText $MainPackageId)</packageId>")
    $lines.Add("      <displayName>$(Escape-XmlText $MainDisplayName)</displayName>")
    if (-not [string]::IsNullOrWhiteSpace($MainPublishedFileId)) {
        $mainSteamUrl = "steam://url/CommunityFilePage/$MainPublishedFileId"
        $lines.Add("      <steamWorkshopUrl>$(Escape-XmlText $mainSteamUrl)</steamWorkshopUrl>")
    }
    $lines.Add('    </li>')
    $lines.Add('  </modDependencies>')
    $lines.Add('  <loadAfter>')
    $lines.Add("    <li>$(Escape-XmlText $MainPackageId)</li>")
    $lines.Add('  </loadAfter>')
    $lines.Add("  <description>$(Escape-XmlText $localizedDescription)</description>")
    $lines.Add('</ModMetaData>')
    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

function New-RussianLocalizationPayload {
    param(
        [string]$DestinationRoot,
        [string]$LanguageFolderName,
        [string]$LocalizationName,
        [string]$LocalizationPackageId,
        [string]$Version,
        [string]$LocalizationAuthor,
        [string]$MainPackageId,
        [string]$MainDisplayName,
        [string]$MainPublishedFileId,
        [string]$PublishedFileId,
        [string]$PublishedFileIdPath
    )

    if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    $previewSource = Copy-RussianLocalizationPreview -DestinationRoot $DestinationRoot
    Write-Host "  ru preview: $previewSource"
    Copy-Payload "About\ModIcon.png" -DestinationRoot $DestinationRoot | Out-Null

    $languageSource = Join-Path (Join-Path $repoRoot "Languages") $LanguageFolderName
    if (-not (Test-Path -LiteralPath $languageSource)) {
        throw "Required Russian language folder missing: $languageSource"
    }
    $languageDestination = Join-Path (Join-Path $DestinationRoot "Languages") $LanguageFolderName
    New-Item -ItemType Directory -Force -Path (Split-Path $languageDestination -Parent) | Out-Null
    Copy-Item -LiteralPath $languageSource -Destination $languageDestination -Recurse -Force

    $aboutDestination = Join-Path $DestinationRoot "About\About.xml"
    New-Item -ItemType Directory -Force -Path (Split-Path $aboutDestination -Parent) | Out-Null
    $aboutXml = New-RussianLocalizationAboutXml `
        -Name $LocalizationName `
        -PackageId $LocalizationPackageId `
        -Version $Version `
        -Author $LocalizationAuthor `
        -MainPackageId $MainPackageId `
        -MainDisplayName $MainDisplayName `
        -MainPublishedFileId $MainPublishedFileId
    [System.IO.File]::WriteAllText($aboutDestination, $aboutXml, (New-Object System.Text.UTF8Encoding($false)))

    $publishedFileDestination = Join-Path $DestinationRoot "About\PublishedFileId.txt"
    if (-not [string]::IsNullOrWhiteSpace($PublishedFileId)) {
        [System.IO.File]::WriteAllText($publishedFileDestination, $PublishedFileId.Trim(), (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  ru file id: $($PublishedFileId.Trim())"
    } elseif (-not [string]::IsNullOrWhiteSpace($PublishedFileIdPath)) {
        $sourcePublishedFileId = Join-Path $repoRoot $PublishedFileIdPath
        if (Test-Path -LiteralPath $sourcePublishedFileId) {
            Copy-Item -LiteralPath $sourcePublishedFileId -Destination $publishedFileDestination -Force
            Write-Host "  ru file id: copied from $PublishedFileIdPath"
        } else {
            Write-Host "  ru file id: none (new Workshop item on first upload)"
        }
    }

    return @{
        AboutPath = $aboutDestination
        LanguagePath = $languageDestination
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
$devVersion = Get-AboutValue $sourceAboutText "modVersion"
$devAuthor = Get-AboutValue $sourceAboutText "author"

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
$publishedAuthor = if ([string]::IsNullOrWhiteSpace($Author)) { $devAuthor } else { $Author.Trim() }
if ([string]::IsNullOrWhiteSpace($publishedAuthor)) { $publishedAuthor = "aimmlegate" }
$publishedVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $devVersion } else { $Version.Trim() }
if ([string]::IsNullOrWhiteSpace($publishedVersion)) { $publishedVersion = "release-$(Get-Date -Format yyyyMMdd)" }
$buildVersionSlug = Get-SafeFolderName $publishedVersion "release-$(Get-Date -Format yyyyMMdd)"

$payloadFolderName = Get-SafeFolderName $publishedPackageId "pawn-diary"
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "dist\$payloadFolderName" }
if (-not $LinkName) { $LinkName = $payloadFolderName }

$buildRussianLocalization = [bool]$SplitRussianLocalization
if ($IncludeRussianInMainPayload) { $buildRussianLocalization = $false }

$russianLanguageFolder = $null
if ($buildRussianLocalization) {
    $russianLanguageFolder = Get-RussianLanguageFolderName (Join-Path $repoRoot "Languages")
    if ([string]::IsNullOrWhiteSpace($RussianLocalizationPackageId)) {
        $RussianLocalizationPackageId = "$publishedPackageId.russian"
    } else {
        $RussianLocalizationPackageId = $RussianLocalizationPackageId.Trim()
    }
    $russianPayloadFolderName = Get-SafeFolderName $RussianLocalizationPackageId "pawn-diary-russian"
    if (-not $RussianLocalizationOutDir) {
        $RussianLocalizationOutDir = Join-Path $repoRoot "dist\$russianPayloadFolderName"
    }
    if (-not $RussianLocalizationLinkName) { $RussianLocalizationLinkName = $russianPayloadFolderName }

    $mainPayloadFullPath = [System.IO.Path]::GetFullPath($OutDir)
    $russianPayloadFullPath = [System.IO.Path]::GetFullPath($RussianLocalizationOutDir)
    if ($mainPayloadFullPath.TrimEnd('\', '/') -eq $russianPayloadFullPath.TrimEnd('\', '/')) {
        throw "Russian localization payload must not use the same OutDir as the main payload."
    }
}

$resolvedModsDir = Resolve-ModsFolder $ModsDir
if (-not $resolvedModsDir) {
    $fallbackMods = Split-Path $repoRoot -Parent
    $resolvedModsDir = Resolve-ModsFolder $fallbackMods
}

Write-Host "$publishedModName publish prep" -ForegroundColor Green
Write-Host "  version     : $publishedVersion"
Write-Host "  build mode  : $Configuration"
Write-Host "  mod name    : $publishedModName"
Write-Host "  packageId   : $publishedPackageId"
Write-Host "  payload out : $OutDir"
if ($buildRussianLocalization) {
    Write-Host "  ru package  : $RussianLocalizationPackageId"
    Write-Host "  ru out      : $RussianLocalizationOutDir"
} else {
    Write-Host "  ru mode     : bundled in main payload"
}
if ($SkipBranch -or $Force) {
    Write-Host "  branch mode: disabled (flags accepted for compatibility)"
}
if ($InstallToMods) {
    if (-not $resolvedModsDir) {
        throw "Could not determine Mods folder automatically. Please pass -ModsDir explicitly."
    }
    Write-Host "  install main: $resolvedModsDir\$LinkName"
    if ($buildRussianLocalization) {
        Write-Host "  install ru  : $resolvedModsDir\$RussianLocalizationLinkName"
    }
}

Write-Step "Build PawnDiary.dll ($Configuration)"
$msbuild = Find-MSBuild
$buildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-build-$buildVersionSlug"
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
if ($buildRussianLocalization) {
    Remove-LanguageFromPayload -PayloadRoot $OutDir -LanguageFolderName $russianLanguageFolder
}

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

$payloadVersion = Get-AboutValue $aboutText "modVersion"
$aboutText = Set-OrAddAboutValue $aboutText "modVersion" $publishedVersion "packageId"
if ([string]::IsNullOrWhiteSpace($payloadVersion)) {
    Write-Host "  modVersion: $publishedVersion"
} elseif ($payloadVersion -ne $publishedVersion) {
    Write-Host "  modVersion: '$payloadVersion' -> '$publishedVersion'"
} else {
    Write-Host "  modVersion: $publishedVersion"
}

if (-not [string]::IsNullOrWhiteSpace($Author)) {
    $aboutText = Set-AboutValue $aboutText "author" $Author.Trim()
    Write-Host "  author    : '$devAuthor' -> '$Author'"
}

[System.IO.File]::WriteAllText($aboutDest, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

# Assemblies for Workshop release. Only the mod's own DLL ships here — the Harmony runtime comes
# from the active brrainz.harmony mod (declared in About/About.xml), never bundled with Pawn Diary.
$asmDir = Join-Path $OutDir "1.6\Assemblies"
New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
$payloadDll = Join-Path $asmDir "PawnDiary.dll"
Copy-Item -LiteralPath $builtDll -Destination $payloadDll -Force
$bundledHarmony = Join-Path $asmDir "0Harmony.dll"
if (Test-Path -LiteralPath $bundledHarmony) {
    Remove-Item -LiteralPath $bundledHarmony -Force
    Write-Host "  assemblies: removed stray 0Harmony.dll from payload"
}

# Ship reference docs with the Workshop payload, but keep development source/tests out of releases.
Copy-Payload "README.md" | Out-Null
Copy-Payload "DOCUMENTATION.md" -Required | Out-Null
Copy-Payload "CHANGELOG.md" -Required | Out-Null
Copy-Payload "EVENT_PROMPT_MAP.md" -Required | Out-Null
foreach ($doc in @("LICENSE", "LICENSE.txt", "LICENSE.md")) {
    Copy-Payload $doc | Out-Null
}

if ($buildRussianLocalization) {
    Write-Step "Prepare Russian localization payload"
    $mainPublishedFileIdPath = Join-Path $repoRoot "About\PublishedFileId.txt"
    $mainPublishedFileId = ""
    if (Test-Path -LiteralPath $mainPublishedFileIdPath) {
        $mainPublishedFileId = [System.IO.File]::ReadAllText($mainPublishedFileIdPath).Trim()
    }

    $russianLocalizationName = Get-Utf8TextFromBase64 "UGF3biBEaWFyeSAtINGA0YPRgdGB0LrQsNGPINC70L7QutCw0LvQuNC30LDRhtC40Y8="

    $russianPayload = New-RussianLocalizationPayload `
        -DestinationRoot $RussianLocalizationOutDir `
        -LanguageFolderName $russianLanguageFolder `
        -LocalizationName $russianLocalizationName `
        -LocalizationPackageId $RussianLocalizationPackageId `
        -Version $publishedVersion `
        -LocalizationAuthor $publishedAuthor `
        -MainPackageId $publishedPackageId `
        -MainDisplayName $publishedModName `
        -MainPublishedFileId $mainPublishedFileId `
        -PublishedFileId $RussianLocalizationPublishedFileId `
        -PublishedFileIdPath $RussianLocalizationPublishedFileIdPath
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
if ($buildRussianLocalization) {
    $russianPayloadFiles = Get-ChildItem -Recurse -File $RussianLocalizationOutDir
    $russianPayloadBytes = ($russianPayloadFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host "Russian localization payload prepared:" -ForegroundColor Green
    Write-Host "  $RussianLocalizationOutDir"
    Write-Host "  Prepared About.xml: $($russianPayload.AboutPath)"
    Write-Host "  Language folder: $($russianPayload.LanguagePath)"
    Write-Host ("  shipped {0} files, {1:N2} MB" -f $russianPayloadFiles.Count, ($russianPayloadBytes / 1MB))
}

if ($InstallToMods) {
    Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $LinkName -TargetFolder $OutDir -AllowReplace:$Force
    if ($buildRussianLocalization) {
        Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $RussianLocalizationLinkName -TargetFolder $RussianLocalizationOutDir -AllowReplace:$Force
    }
}
