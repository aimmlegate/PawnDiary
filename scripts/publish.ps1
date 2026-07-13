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
  Ignored when -AutoBump is set.

.PARAMETER AutoBump
  Increment the patch component of the source About.xml <modVersion> by one (0.2.2 -> 0.2.3),
  write the new value back to the source About.xml, commit nothing, and use that bumped value as
  the release version. Fails if the source version is missing or not a major.minor.patch number.
  Use this instead of -Version for a one-command "cut a new patch release" flow. Has no effect on
  branches/tags; pair with your own git commit of the bumped About.xml.

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

.PARAMETER PublishExampleAdapter
  Build and package the example adapter mod alongside the main Workshop payloads. Enabled by
  default; pass -PublishExampleAdapter:$false to skip it.

.PARAMETER ExampleAdapterOutDir
  Output folder for the example adapter payload. Defaults to
  <repo>/dist/<example adapter packageId>.

.PARAMETER ExampleAdapterPackageId
  Override the example adapter packageId. Defaults to the packageId in
  integrations/PawnDiary.ExampleAdapter/About/About.xml.

.PARAMETER ExampleAdapterPublishedFileId
  Workshop item id for the example adapter payload. When set, it is written as that payload's
  About/PublishedFileId.txt. Leave blank before the first Workshop upload.

.PARAMETER ExampleAdapterPublishedFileIdPath
  Optional source file for the example adapter Workshop id. Defaults to
  About/PublishedFileId-ExampleAdapter.txt and is copied as About/PublishedFileId.txt when present.

.PARAMETER ExampleAdapterLinkName
  Folder name to create under -ModsDir for the example adapter junction when both -InstallToMods
  and -PublishExampleAdapter are enabled.

.PARAMETER PublishSpeakUpAdapter
  Build and package the reflection-only SpeakUp adapter alongside the main Workshop payloads.
  Enabled by default; pass -PublishSpeakUpAdapter:$false to skip it.

.PARAMETER SpeakUpAdapterOutDir
  Output folder for the SpeakUp adapter payload. Defaults to
  <repo>/dist/<SpeakUp adapter packageId>.

.PARAMETER SpeakUpAdapterPackageId
  Override the SpeakUp adapter packageId. Defaults to the packageId in
  integrations/PawnDiary.SpeakUp/About/About.xml.

.PARAMETER SpeakUpAdapterPublishedFileId
  Workshop item id for the SpeakUp adapter payload. When set, it is written as that payload's
  About/PublishedFileId.txt. Leave blank before the first Workshop upload.

.PARAMETER SpeakUpAdapterPublishedFileIdPath
  Optional source file for the SpeakUp adapter Workshop id. Defaults to
  About/PublishedFileId-SpeakUp.txt and is copied as About/PublishedFileId.txt when present.

.PARAMETER SpeakUpAdapterLinkName
  Folder name to create under -ModsDir for the SpeakUp adapter junction when both -InstallToMods
  and -PublishSpeakUpAdapter are enabled.

.PARAMETER PublishAllAdapters
  Also build and package the RimTalk, Rimpsyche, 1-2-3 Personalities, and VSIE adapters. The
  example and SpeakUp adapters keep their existing default-on behavior, so this produces all six.

.PARAMETER PublishRimTalkAdapter
  Build and package the typed RimTalk bridge. Its target mod assembly must be installed at the
  path configured by PawnDiaryRimTalkBridge.csproj.

.PARAMETER PublishRimpsycheAdapter
  Build and package the typed Rimpsyche adapter. Its target mod assembly must be installed at the
  path configured by PawnDiaryRimpsyche.csproj.

.PARAMETER PublishPersonalitiesAdapter
  Build and package the typed 1-2-3 Personalities adapter. Its Module 1 assembly must be installed
  at the path configured by PawnDiaryPersonalities123.csproj.

.PARAMETER PublishVsieAdapter
  Build and package the VSIE adapter. It patches base-game symbols and needs no VSIE assembly at
  build time.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$AutoBump,
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
    [switch]$PublishExampleAdapter = $true,
    [string]$ExampleAdapterOutDir,
    [string]$ExampleAdapterPackageId,
    [string]$ExampleAdapterPublishedFileId,
    [string]$ExampleAdapterPublishedFileIdPath = "About\PublishedFileId-ExampleAdapter.txt",
    [string]$ExampleAdapterLinkName,
    [switch]$PublishSpeakUpAdapter = $true,
    [string]$SpeakUpAdapterOutDir,
    [string]$SpeakUpAdapterPackageId,
    [string]$SpeakUpAdapterPublishedFileId,
    [string]$SpeakUpAdapterPublishedFileIdPath = "About\PublishedFileId-SpeakUp.txt",
    [string]$SpeakUpAdapterLinkName,
    [switch]$PublishAllAdapters,
    [switch]$PublishRimTalkAdapter,
    [switch]$PublishRimpsycheAdapter,
    [switch]$PublishPersonalitiesAdapter,
    [switch]$PublishVsieAdapter,
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

function Set-FirstNestedAboutValue {
    param([string]$Text, [string]$ContainerElement, [string]$Element, [string]$Value)
    $escapedValue = [System.Security.SecurityElement]::Escape($Value)
    $replacementValue = $escapedValue -replace '\$', '$$'
    $regex = [regex]::new("(?s)(<$ContainerElement(?:\s[^>]*)?>.*?<$Element(?:\s[^>]*)?>\s*)(.*?)(\s*</$Element>)")
    if (-not $regex.IsMatch($Text)) { return $Text }
    return $regex.Replace($Text, ('${1}' + $replacementValue + '${3}'), 1)
}

function Set-OrAddFirstNestedAboutValue {
    param(
        [string]$Text,
        [string]$ContainerElement,
        [string]$Element,
        [string]$Value
    )

    # Work inside the first <li> only. Looking for $Element across the whole container can find the
    # target mod's second dependency and accidentally replace its Workshop URL when the core row has
    # no URL of its own.
    $firstItemRegex = [regex]::new("(?s)(<$ContainerElement(?:\s[^>]*)?>\s*)(<li(?:\s[^>]*)?>.*?</li>)")
    $firstItemMatch = $firstItemRegex.Match($Text)
    if (-not $firstItemMatch.Success) { return $Text }

    $firstItem = $firstItemMatch.Groups[2].Value
    $escapedValue = [System.Security.SecurityElement]::Escape($Value)
    $replacementValue = $escapedValue -replace '\$', '$$'
    $existingRegex = [regex]::new("(?s)(<$Element(?:\s[^>]*)?>\s*)(.*?)(\s*</$Element>)")
    if ($existingRegex.IsMatch($firstItem)) {
        $firstItem = $existingRegex.Replace($firstItem, ('${1}' + $replacementValue + '${3}'), 1)
    } else {
        # Dependency fields conventionally appear packageId, displayName, URL. Appending immediately
        # before the first item closes preserves that order without touching later dependency rows.
        $line = "      <$Element>$escapedValue</$Element>"
        $itemEndRegex = [regex]::new("(?s)(\s*</li>)")
        $firstItem = $itemEndRegex.Replace($firstItem, ([Environment]::NewLine + $line + '${1}'), 1)
    }

    $itemStart = $firstItemMatch.Groups[2].Index
    $itemLength = $firstItemMatch.Groups[2].Length
    return $Text.Substring(0, $itemStart) + $firstItem + $Text.Substring($itemStart + $itemLength)
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

# Increments the patch component of a major.minor.patch version string (0.2.2 -> 0.2.3) and writes
# the new value back to the SOURCE About.xml so the bump is permanent in the repo. Returns the
# bumped version. Used by -AutoBump for the one-command "cut a new patch release" flow.
function Invoke-AutoBump {
    param([string]$SourceAboutPath, [string]$CurrentVersion)
    if ([string]::IsNullOrWhiteSpace($CurrentVersion)) {
        throw "-AutoBump needs an existing major.minor.patch <modVersion> in About.xml, but none was found."
    }

    $parts = $CurrentVersion.Trim().Split('.')
    if ($parts.Length -ne 3) {
        throw "-AutoBump needs a major.minor.patch version (got '$CurrentVersion'). Bump -Version manually or set <modVersion> in About.xml first."
    }

    # Only the patch number changes; pre-release suffixes (e.g. 0.2.2-rc1) or leading 'v' prefixes
    # are not supported on purpose — patch releases go through this script as plain numbers.
    $major = $parts[0]
    $minor = $parts[1]
    $patchPart = $parts[2]
    if (-not ($major -match '^\d+$') -or -not ($minor -match '^\d+$') -or -not ($patchPart -match '^\d+$')) {
        throw "-AutoBump needs three numeric version components (got '$CurrentVersion'). Pre-release suffixes and 'v' prefixes are not auto-bumped."
    }
    $newPatch = [int]$patchPart + 1
    $bumped = "$major.$minor.$newPatch"

    $sourceText = [System.IO.File]::ReadAllText($SourceAboutPath)
    $updated = Set-AboutValue $sourceText "modVersion" $bumped
    [System.IO.File]::WriteAllText($SourceAboutPath, $updated, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  auto-bump : '$CurrentVersion' -> '$bumped' (written to source About.xml)"
    return $bumped
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

function Copy-PathFromRoot {
    param([string]$SourceRoot, [string]$RelPath, [switch]$Required, [string]$DestinationRoot)
    $src = Join-Path $SourceRoot $RelPath
    if (-not (Test-Path -LiteralPath $src)) {
        if ($Required) { throw "Required path missing: $src" }
        return $false
    }
    $dest = Join-Path $DestinationRoot $RelPath
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force
    return $true
}

function Remove-BuildIntermediates {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return }
    Get-ChildItem -LiteralPath $Root -Recurse -Directory -Force |
        Where-Object { $_.Name -eq "obj" -or $_.Name -eq "bin" } |
        Sort-Object FullName -Descending |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
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

function New-ExampleAdapterPayload {
    param(
        [string]$AdapterSourceRoot,
        [string]$DestinationRoot,
        [string]$AdapterPackageId,
        [string]$Version,
        [string]$CorePackageId,
        [string]$CoreDisplayName,
        [string]$CorePublishedFileId,
        [string]$BuiltDll,
        [string]$BuiltPdb,
        [string]$PublishedFileId,
        [string]$PublishedFileIdPath
    )

    if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    Copy-PathFromRoot $AdapterSourceRoot "About" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "1.6\Defs" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "Languages" -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "Source" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "API_EXPLORER.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    Remove-BuildIntermediates (Join-Path $DestinationRoot "Source")

    Copy-Payload "INTEGRATIONS.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-Payload "EXTERNAL_API.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    foreach ($doc in @("LICENSE", "LICENSE.txt", "LICENSE.md")) {
        Copy-Payload $doc -DestinationRoot $DestinationRoot | Out-Null
    }

    $aboutDestination = Join-Path $DestinationRoot "About\About.xml"
    $aboutText = [System.IO.File]::ReadAllText($aboutDestination)
    $aboutText = Set-AboutValue $aboutText "packageId" $AdapterPackageId
    $aboutText = Set-OrAddAboutValue $aboutText "modVersion" $Version "packageId"
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "packageId" $CorePackageId
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "displayName" $CoreDisplayName
    if (-not [string]::IsNullOrWhiteSpace($CorePublishedFileId)) {
        $coreSteamUrl = "steam://url/CommunityFilePage/$($CorePublishedFileId.Trim())"
        $aboutText = Set-OrAddFirstNestedAboutValue $aboutText "modDependencies" "steamWorkshopUrl" $coreSteamUrl
    }
    $aboutText = Set-FirstNestedAboutValue $aboutText "loadAfter" "li" $CorePackageId
    [System.IO.File]::WriteAllText($aboutDestination, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

    $asmDir = Join-Path $DestinationRoot "1.6\Assemblies"
    New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
    $payloadDll = Join-Path $asmDir "PawnDiaryExampleAdapter.dll"
    Copy-Item -LiteralPath $BuiltDll -Destination $payloadDll -Force
    if ($BuiltPdb -and (Test-Path -LiteralPath $BuiltPdb)) {
        Copy-Item -LiteralPath $BuiltPdb -Destination (Join-Path $asmDir "PawnDiaryExampleAdapter.pdb") -Force
    }

    $publishedFileDestination = Join-Path $DestinationRoot "About\PublishedFileId.txt"
    if (-not [string]::IsNullOrWhiteSpace($PublishedFileId)) {
        [System.IO.File]::WriteAllText($publishedFileDestination, $PublishedFileId.Trim(), (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  example id: $($PublishedFileId.Trim())"
    } elseif (-not [string]::IsNullOrWhiteSpace($PublishedFileIdPath)) {
        $sourcePublishedFileId = Join-Path $repoRoot $PublishedFileIdPath
        if (Test-Path -LiteralPath $sourcePublishedFileId) {
            Copy-Item -LiteralPath $sourcePublishedFileId -Destination $publishedFileDestination -Force
            Write-Host "  example id: copied from $PublishedFileIdPath"
        } else {
            Write-Host "  example id: none (new Workshop item on first upload)"
        }
    }

    return @{
        AboutPath = $aboutDestination
        DllPath = $payloadDll
        SourcePath = (Join-Path $DestinationRoot "Source")
    }
}

function New-SpeakUpAdapterPayload {
    param(
        [string]$AdapterSourceRoot,
        [string]$DestinationRoot,
        [string]$AdapterPackageId,
        [string]$Version,
        [string]$CorePackageId,
        [string]$CoreDisplayName,
        [string]$CorePublishedFileId,
        [string]$BuiltDll,
        [string]$BuiltPdb,
        [string]$PublishedFileId,
        [string]$PublishedFileIdPath
    )

    if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    # Keep the adapter independently inspectable like the example payload, but never copy its checked-in
    # build output: the fresh DLL/PDB from this publish run is installed below.
    Copy-PathFromRoot $AdapterSourceRoot "About" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "1.6\Defs" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "1.6\Patches" -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "Languages" -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "Source" -DestinationRoot $DestinationRoot | Out-Null
    Remove-BuildIntermediates (Join-Path $DestinationRoot "Source")

    Copy-Payload "INTEGRATIONS.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-Payload "EXTERNAL_API.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    foreach ($doc in @("LICENSE", "LICENSE.txt", "LICENSE.md")) {
        Copy-Payload $doc -DestinationRoot $DestinationRoot | Out-Null
    }

    $aboutDestination = Join-Path $DestinationRoot "About\About.xml"
    $aboutText = [System.IO.File]::ReadAllText($aboutDestination)
    $aboutText = Set-AboutValue $aboutText "packageId" $AdapterPackageId
    $aboutText = Set-OrAddAboutValue $aboutText "modVersion" $Version "packageId"
    # Pawn Diary is the first dependency/loadAfter row; the existing SpeakUp row is deliberately
    # preserved so Workshop and RimWorld still express the target-mod requirement.
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "packageId" $CorePackageId
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "displayName" $CoreDisplayName
    if (-not [string]::IsNullOrWhiteSpace($CorePublishedFileId)) {
        $coreSteamUrl = "steam://url/CommunityFilePage/$($CorePublishedFileId.Trim())"
        $aboutText = Set-OrAddFirstNestedAboutValue $aboutText "modDependencies" "steamWorkshopUrl" $coreSteamUrl
    }
    $aboutText = Set-FirstNestedAboutValue $aboutText "loadAfter" "li" $CorePackageId
    [System.IO.File]::WriteAllText($aboutDestination, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

    $asmDir = Join-Path $DestinationRoot "1.6\Assemblies"
    New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
    $payloadDll = Join-Path $asmDir "PawnDiarySpeakUp.dll"
    Copy-Item -LiteralPath $BuiltDll -Destination $payloadDll -Force
    if ($BuiltPdb -and (Test-Path -LiteralPath $BuiltPdb)) {
        Copy-Item -LiteralPath $BuiltPdb -Destination (Join-Path $asmDir "PawnDiarySpeakUp.pdb") -Force
    }

    $publishedFileDestination = Join-Path $DestinationRoot "About\PublishedFileId.txt"
    if (-not [string]::IsNullOrWhiteSpace($PublishedFileId)) {
        [System.IO.File]::WriteAllText($publishedFileDestination, $PublishedFileId.Trim(), (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  SpeakUp id: $($PublishedFileId.Trim())"
    } elseif (-not [string]::IsNullOrWhiteSpace($PublishedFileIdPath)) {
        $sourcePublishedFileId = Join-Path $repoRoot $PublishedFileIdPath
        if (Test-Path -LiteralPath $sourcePublishedFileId) {
            Copy-Item -LiteralPath $sourcePublishedFileId -Destination $publishedFileDestination -Force
            Write-Host "  SpeakUp id: copied from $PublishedFileIdPath"
        } else {
            Write-Host "  SpeakUp id: none (new Workshop item on first upload)"
        }
    }

    return @{
        AboutPath = $aboutDestination
        DllPath = $payloadDll
        SourcePath = (Join-Path $DestinationRoot "Source")
    }
}

# Builds the clean runtime payload shared by the typed adapters. Unlike the example adapter (and the
# older SpeakUp package), these Workshop items do not ship their development Source tree. The source
# About.xml deliberately points at the development core while working from this checkout; release
# prep replaces that exact package id in both modDependencies and loadAfter before anything is staged.
function New-RuntimeAdapterPayload {
    param(
        [string]$Label,
        [string]$AdapterSourceRoot,
        [string]$DestinationRoot,
        [string]$AdapterPackageId,
        [string]$Version,
        [string]$DevelopmentCorePackageId,
        [string]$CorePackageId,
        [string]$CoreDisplayName,
        [string]$CorePublishedFileId,
        [string]$AssemblyName,
        [string]$BuiltDll,
        [string]$BuiltPdb,
        [string]$PublishedFileIdPath
    )

    if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    Copy-PathFromRoot $AdapterSourceRoot "About" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "1.6\Defs" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "1.6\Patches" -DestinationRoot $DestinationRoot | Out-Null
    Copy-PathFromRoot $AdapterSourceRoot "Languages" -DestinationRoot $DestinationRoot | Out-Null

    Copy-Payload "INTEGRATIONS.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    Copy-Payload "EXTERNAL_API.md" -Required -DestinationRoot $DestinationRoot | Out-Null
    foreach ($doc in @("LICENSE", "LICENSE.txt", "LICENSE.md")) {
        Copy-Payload $doc -DestinationRoot $DestinationRoot | Out-Null
    }

    $aboutDestination = Join-Path $DestinationRoot "About\About.xml"
    $aboutText = [System.IO.File]::ReadAllText($aboutDestination)
    $aboutText = Set-AboutValue $aboutText "packageId" $AdapterPackageId
    $aboutText = Set-OrAddAboutValue $aboutText "modVersion" $Version "packageId"
    if (-not [string]::IsNullOrWhiteSpace($DevelopmentCorePackageId)) {
        $aboutText = $aboutText.Replace($DevelopmentCorePackageId, $CorePackageId)
    }
    # Pawn Diary is the first dependency in every typed adapter. Other target-mod dependency rows
    # stay untouched; Rimpsyche's leading Harmony loadAfter row is why the exact-id replacement above
    # is used instead of blindly rewriting the first loadAfter entry.
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "packageId" $CorePackageId
    $aboutText = Set-FirstNestedAboutValue $aboutText "modDependencies" "displayName" $CoreDisplayName
    if (-not [string]::IsNullOrWhiteSpace($CorePublishedFileId)) {
        $coreSteamUrl = "steam://url/CommunityFilePage/$($CorePublishedFileId.Trim())"
        $aboutText = Set-OrAddFirstNestedAboutValue $aboutText "modDependencies" "steamWorkshopUrl" $coreSteamUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($DevelopmentCorePackageId) -and $aboutText.Contains($DevelopmentCorePackageId)) {
        throw "$Label release About.xml still contains the development core package id."
    }
    [xml]$aboutCheck = $aboutText
    $dependencyItems = @($aboutCheck.ModMetaData.modDependencies.li)
    if ($dependencyItems.Count -eq 0 -or $dependencyItems[0].packageId -ne $CorePackageId) {
        throw "$Label release About.xml does not declare the published core as its first dependency."
    }
    if (@($aboutCheck.ModMetaData.loadAfter.li) -notcontains $CorePackageId) {
        throw "$Label release About.xml does not load after the published core package id."
    }
    [System.IO.File]::WriteAllText($aboutDestination, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

    $asmDir = Join-Path $DestinationRoot "1.6\Assemblies"
    New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
    $payloadDll = Join-Path $asmDir "$AssemblyName.dll"
    Copy-Item -LiteralPath $BuiltDll -Destination $payloadDll -Force
    if ($BuiltPdb -and (Test-Path -LiteralPath $BuiltPdb)) {
        Copy-Item -LiteralPath $BuiltPdb -Destination (Join-Path $asmDir "$AssemblyName.pdb") -Force
    }

    $publishedFileDestination = Join-Path $DestinationRoot "About\PublishedFileId.txt"
    if (-not [string]::IsNullOrWhiteSpace($PublishedFileIdPath)) {
        $sourcePublishedFileId = Join-Path $repoRoot $PublishedFileIdPath
        if (Test-Path -LiteralPath $sourcePublishedFileId) {
            Copy-Item -LiteralPath $sourcePublishedFileId -Destination $publishedFileDestination -Force
            Write-Host "  $Label id: copied from $PublishedFileIdPath"
        } elseif (-not (Test-Path -LiteralPath $publishedFileDestination)) {
            Write-Host "  $Label id: none (new Workshop item on first upload)"
        }
    }

    return @{
        AboutPath = $aboutDestination
        DllPath = $payloadDll
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

if ($AutoBump -and -not [string]::IsNullOrWhiteSpace($Version)) {
    throw "Use either -AutoBump or -Version, not both."
}
if ($AutoBump) {
    $publishedVersion = Invoke-AutoBump -SourceAboutPath $sourceAboutPath -CurrentVersion $devVersion
} else {
    $publishedVersion = if ([string]::IsNullOrWhiteSpace($Version)) { $devVersion } else { $Version.Trim() }
}
if ([string]::IsNullOrWhiteSpace($publishedVersion)) { $publishedVersion = "release-$(Get-Date -Format yyyyMMdd)" }
$buildVersionSlug = Get-SafeFolderName $publishedVersion "release-$(Get-Date -Format yyyyMMdd)"

$payloadFolderName = Get-SafeFolderName $publishedPackageId "pawn-diary"
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "dist\$payloadFolderName" }
if (-not $LinkName) { $LinkName = $payloadFolderName }

$mainPublishedFileIdPath = Join-Path $repoRoot "About\PublishedFileId.txt"
$mainPublishedFileId = ""
if (Test-Path -LiteralPath $mainPublishedFileIdPath) {
    $mainPublishedFileId = [System.IO.File]::ReadAllText($mainPublishedFileIdPath).Trim()
}

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

$buildExampleAdapter = [bool]$PublishExampleAdapter
$exampleAdapterRoot = Join-Path $repoRoot "integrations\PawnDiary.ExampleAdapter"
$exampleAdapterBuildOut = $null
$exampleAdapterPayload = $null
if ($buildExampleAdapter) {
    $exampleAdapterAboutPath = Join-Path $exampleAdapterRoot "About\About.xml"
    if (-not (Test-Path -LiteralPath $exampleAdapterAboutPath)) {
        throw "Example adapter source mod is missing About/About.xml: $exampleAdapterRoot"
    }

    if ([string]::IsNullOrWhiteSpace($ExampleAdapterPackageId)) {
        $exampleAdapterAboutText = [System.IO.File]::ReadAllText($exampleAdapterAboutPath)
        $ExampleAdapterPackageId = Get-AboutValue $exampleAdapterAboutText "packageId"
    } else {
        $ExampleAdapterPackageId = $ExampleAdapterPackageId.Trim()
    }
    if ([string]::IsNullOrWhiteSpace($ExampleAdapterPackageId)) {
        $ExampleAdapterPackageId = "$publishedPackageId.adapter.example"
    }

    $exampleAdapterPayloadFolderName = Get-SafeFolderName $ExampleAdapterPackageId "pawn-diary-example-adapter"
    if (-not $ExampleAdapterOutDir) {
        $ExampleAdapterOutDir = Join-Path $repoRoot "dist\$exampleAdapterPayloadFolderName"
    }
    if (-not $ExampleAdapterLinkName) { $ExampleAdapterLinkName = $exampleAdapterPayloadFolderName }

    $examplePayloadFullPath = [System.IO.Path]::GetFullPath($ExampleAdapterOutDir)
    $mainPayloadFullPath = [System.IO.Path]::GetFullPath($OutDir)
    if ($mainPayloadFullPath.TrimEnd('\', '/') -eq $examplePayloadFullPath.TrimEnd('\', '/')) {
        throw "Example adapter payload must not use the same OutDir as the main payload."
    }
    if ($buildRussianLocalization) {
        $russianPayloadFullPath = [System.IO.Path]::GetFullPath($RussianLocalizationOutDir)
        if ($russianPayloadFullPath.TrimEnd('\', '/') -eq $examplePayloadFullPath.TrimEnd('\', '/')) {
            throw "Example adapter payload must not use the same OutDir as the Russian localization payload."
        }
    }
}

$buildSpeakUpAdapter = [bool]$PublishSpeakUpAdapter
$speakUpAdapterRoot = Join-Path $repoRoot "integrations\PawnDiary.SpeakUp"
$speakUpAdapterBuildOut = $null
$speakUpAdapterPayload = $null
if ($buildSpeakUpAdapter) {
    $speakUpAdapterAboutPath = Join-Path $speakUpAdapterRoot "About\About.xml"
    if (-not (Test-Path -LiteralPath $speakUpAdapterAboutPath)) {
        throw "SpeakUp adapter source mod is missing About/About.xml: $speakUpAdapterRoot"
    }

    if ([string]::IsNullOrWhiteSpace($SpeakUpAdapterPackageId)) {
        $speakUpAdapterAboutText = [System.IO.File]::ReadAllText($speakUpAdapterAboutPath)
        $SpeakUpAdapterPackageId = Get-AboutValue $speakUpAdapterAboutText "packageId"
    } else {
        $SpeakUpAdapterPackageId = $SpeakUpAdapterPackageId.Trim()
    }
    if ([string]::IsNullOrWhiteSpace($SpeakUpAdapterPackageId)) {
        $SpeakUpAdapterPackageId = "$publishedPackageId.adapter.speakup"
    }

    $speakUpAdapterPayloadFolderName = Get-SafeFolderName $SpeakUpAdapterPackageId "pawn-diary-speakup"
    if (-not $SpeakUpAdapterOutDir) {
        $SpeakUpAdapterOutDir = Join-Path $repoRoot "dist\$speakUpAdapterPayloadFolderName"
    }
    if (-not $SpeakUpAdapterLinkName) { $SpeakUpAdapterLinkName = $speakUpAdapterPayloadFolderName }

    $speakUpPayloadFullPath = [System.IO.Path]::GetFullPath($SpeakUpAdapterOutDir)
    $speakUpSourceFullPath = [System.IO.Path]::GetFullPath($speakUpAdapterRoot)
    if ($speakUpSourceFullPath.TrimEnd('\', '/') -eq $speakUpPayloadFullPath.TrimEnd('\', '/')) {
        throw "SpeakUp adapter payload must not overwrite its integrations source folder."
    }
    $mainPayloadFullPath = [System.IO.Path]::GetFullPath($OutDir)
    if ($mainPayloadFullPath.TrimEnd('\', '/') -eq $speakUpPayloadFullPath.TrimEnd('\', '/')) {
        throw "SpeakUp adapter payload must not use the same OutDir as the main payload."
    }
    if ($buildRussianLocalization) {
        $russianPayloadFullPath = [System.IO.Path]::GetFullPath($RussianLocalizationOutDir)
        if ($russianPayloadFullPath.TrimEnd('\', '/') -eq $speakUpPayloadFullPath.TrimEnd('\', '/')) {
            throw "SpeakUp adapter payload must not use the same OutDir as the Russian localization payload."
        }
    }
    if ($buildExampleAdapter) {
        $examplePayloadFullPath = [System.IO.Path]::GetFullPath($ExampleAdapterOutDir)
        if ($examplePayloadFullPath.TrimEnd('\', '/') -eq $speakUpPayloadFullPath.TrimEnd('\', '/')) {
            throw "SpeakUp and example adapter payloads must use different output folders."
        }
    }
}

# The four typed adapters are opt-in because three compile against assemblies owned by optional
# Workshop mods. -PublishAllAdapters is the release-machine convenience switch; the individual
# switches remain useful when only one integration is being updated.
$runtimeAdapterDefinitions = @(
    [pscustomobject]@{
        Enabled = ([bool]$PublishAllAdapters -or [bool]$PublishRimTalkAdapter)
        Key = "rimtalk"
        Label = "RimTalk"
        SourceFolder = "PawnDiary.RimTalkBridge"
        ProjectFile = "Source\PawnDiaryRimTalkBridge.csproj"
        AssemblyName = "PawnDiaryRimTalkBridge"
        PublishedFileIdPath = "About\PublishedFileId-RimTalk.txt"
        FallbackFolderName = "pawn-diary-rimtalk"
    },
    [pscustomobject]@{
        Enabled = ([bool]$PublishAllAdapters -or [bool]$PublishRimpsycheAdapter)
        Key = "rimpsyche"
        Label = "Rimpsyche"
        SourceFolder = "PawnDiary.RimpsycheBridge"
        ProjectFile = "Source\PawnDiaryRimpsyche.csproj"
        AssemblyName = "PawnDiaryRimpsyche"
        PublishedFileIdPath = "About\PublishedFileId-Rimpsyche.txt"
        FallbackFolderName = "pawn-diary-rimpsyche"
    },
    [pscustomobject]@{
        Enabled = ([bool]$PublishAllAdapters -or [bool]$PublishPersonalitiesAdapter)
        Key = "personalities123"
        Label = "1-2-3 Personalities"
        SourceFolder = "PawnDiary.PersonalitiesBridge"
        ProjectFile = "Source\PawnDiaryPersonalities123.csproj"
        AssemblyName = "PawnDiaryPersonalities123"
        PublishedFileIdPath = "About\PublishedFileId-Personalities123.txt"
        FallbackFolderName = "pawn-diary-personalities123"
    },
    [pscustomobject]@{
        Enabled = ([bool]$PublishAllAdapters -or [bool]$PublishVsieAdapter)
        Key = "vsie"
        Label = "VSIE"
        SourceFolder = "PawnDiary.Vsie"
        ProjectFile = "Source\PawnDiaryVsie.csproj"
        AssemblyName = "PawnDiaryVsie"
        PublishedFileIdPath = "About\PublishedFileId-Vsie.txt"
        FallbackFolderName = "pawn-diary-vsie"
    }
)

$occupiedPayloadPaths = @([System.IO.Path]::GetFullPath($OutDir).TrimEnd('\', '/'))
if ($buildRussianLocalization) {
    $occupiedPayloadPaths += [System.IO.Path]::GetFullPath($RussianLocalizationOutDir).TrimEnd('\', '/')
}
if ($buildExampleAdapter) {
    $occupiedPayloadPaths += [System.IO.Path]::GetFullPath($ExampleAdapterOutDir).TrimEnd('\', '/')
}
if ($buildSpeakUpAdapter) {
    $occupiedPayloadPaths += [System.IO.Path]::GetFullPath($SpeakUpAdapterOutDir).TrimEnd('\', '/')
}

$runtimeAdapters = @()
foreach ($definition in $runtimeAdapterDefinitions) {
    if (-not $definition.Enabled) { continue }

    $adapterRoot = Join-Path $repoRoot "integrations\$($definition.SourceFolder)"
    $adapterAboutPath = Join-Path $adapterRoot "About\About.xml"
    if (-not (Test-Path -LiteralPath $adapterAboutPath)) {
        throw "$($definition.Label) adapter source mod is missing About/About.xml: $adapterRoot"
    }
    $adapterAboutText = [System.IO.File]::ReadAllText($adapterAboutPath)
    $adapterPackageId = Remove-DevelopmentPostfix (Get-AboutValue $adapterAboutText "packageId")
    if ([string]::IsNullOrWhiteSpace($adapterPackageId)) {
        throw "$($definition.Label) adapter About.xml does not contain a packageId."
    }

    $payloadFolderName = Get-SafeFolderName $adapterPackageId $definition.FallbackFolderName
    $adapterOutDir = Join-Path $repoRoot "dist\$payloadFolderName"
    $adapterPayloadFullPath = [System.IO.Path]::GetFullPath($adapterOutDir).TrimEnd('\', '/')
    $adapterSourceFullPath = [System.IO.Path]::GetFullPath($adapterRoot).TrimEnd('\', '/')
    if ($adapterSourceFullPath -eq $adapterPayloadFullPath) {
        throw "$($definition.Label) adapter payload must not overwrite its integrations source folder."
    }
    if ($occupiedPayloadPaths -contains $adapterPayloadFullPath) {
        throw "$($definition.Label) adapter payload collides with another payload output: $adapterOutDir"
    }
    $occupiedPayloadPaths += $adapterPayloadFullPath

    $runtimeAdapters += [pscustomobject]@{
        Key = $definition.Key
        Label = $definition.Label
        Root = $adapterRoot
        ProjectPath = (Join-Path $adapterRoot $definition.ProjectFile)
        AssemblyName = $definition.AssemblyName
        PackageId = $adapterPackageId
        OutDir = $adapterOutDir
        LinkName = $payloadFolderName
        PublishedFileIdPath = $definition.PublishedFileIdPath
        BuildOut = $null
        BuiltDll = $null
        BuiltPdb = $null
        Payload = $null
    }
}

$resolvedModsDir = Resolve-ModsFolder $ModsDir
if (-not $resolvedModsDir) {
    $fallbackMods = Split-Path $repoRoot -Parent
    $resolvedModsDir = Resolve-ModsFolder $fallbackMods
}

Write-Host "$publishedModName publish prep" -ForegroundColor Green
Write-Host "  version     : $publishedVersion$(if ($AutoBump) { ' (auto-bumped)' })"
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
if ($buildExampleAdapter) {
    Write-Host "  example id  : $ExampleAdapterPackageId"
    Write-Host "  example out : $ExampleAdapterOutDir"
} else {
    Write-Host "  example    : skipped"
}
if ($buildSpeakUpAdapter) {
    Write-Host "  SpeakUp id  : $SpeakUpAdapterPackageId"
    Write-Host "  SpeakUp out : $SpeakUpAdapterOutDir"
} else {
    Write-Host "  SpeakUp    : skipped"
}
foreach ($adapter in $runtimeAdapters) {
    Write-Host "  $($adapter.Label) id  : $($adapter.PackageId)"
    Write-Host "  $($adapter.Label) out : $($adapter.OutDir)"
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
    if ($buildExampleAdapter) {
        Write-Host "  install ex  : $resolvedModsDir\$ExampleAdapterLinkName"
    }
    if ($buildSpeakUpAdapter) {
        Write-Host "  install su  : $resolvedModsDir\$SpeakUpAdapterLinkName"
    }
    foreach ($adapter in $runtimeAdapters) {
        Write-Host "  install $($adapter.Key): $resolvedModsDir\$($adapter.LinkName)"
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

if ($buildExampleAdapter) {
    Write-Step "Build example adapter DLL ($Configuration)"
    $exampleAdapterBuildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-example-adapter-build-$buildVersionSlug"
    if (Test-Path -LiteralPath $exampleAdapterBuildOut) { Remove-Item -LiteralPath $exampleAdapterBuildOut -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $exampleAdapterBuildOut | Out-Null

    Invoke-Native $msbuild @(
        (Join-Path $repoRoot "integrations\PawnDiary.ExampleAdapter\Source\PawnDiaryExampleAdapter.csproj"),
        "/t:Rebuild",
        "/p:Configuration=$Configuration",
        "/p:OutputPath=$exampleAdapterBuildOut",
        "/p:PawnDiaryReference=$builtDll",
        "/nologo",
        "/v:minimal"
    )

    $builtExampleAdapterDll = Join-Path $exampleAdapterBuildOut "PawnDiaryExampleAdapter.dll"
    if (-not (Test-Path -LiteralPath $builtExampleAdapterDll)) {
        throw "Build reported success but PawnDiaryExampleAdapter.dll was not produced in $exampleAdapterBuildOut."
    }
    $builtExampleAdapterPdb = Join-Path $exampleAdapterBuildOut "PawnDiaryExampleAdapter.pdb"
}

if ($buildSpeakUpAdapter) {
    Write-Step "Build SpeakUp adapter DLL ($Configuration)"
    $speakUpAdapterBuildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-speakup-adapter-build-$buildVersionSlug"
    if (Test-Path -LiteralPath $speakUpAdapterBuildOut) { Remove-Item -LiteralPath $speakUpAdapterBuildOut -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $speakUpAdapterBuildOut | Out-Null

    # This adapter is reflection-only: it deliberately needs no SpeakUp.dll path at release time.
    Invoke-Native $msbuild @(
        (Join-Path $repoRoot "integrations\PawnDiary.SpeakUp\Source\PawnDiarySpeakUp.csproj"),
        "/t:Rebuild",
        "/p:Configuration=$Configuration",
        "/p:OutputPath=$speakUpAdapterBuildOut",
        "/p:PawnDiaryReference=$builtDll",
        "/nologo",
        "/v:minimal"
    )

    $builtSpeakUpAdapterDll = Join-Path $speakUpAdapterBuildOut "PawnDiarySpeakUp.dll"
    if (-not (Test-Path -LiteralPath $builtSpeakUpAdapterDll)) {
        throw "Build reported success but PawnDiarySpeakUp.dll was not produced in $speakUpAdapterBuildOut."
    }
    $builtSpeakUpAdapterPdb = Join-Path $speakUpAdapterBuildOut "PawnDiarySpeakUp.pdb"
}

foreach ($adapter in $runtimeAdapters) {
    Write-Step "Build $($adapter.Label) adapter DLL ($Configuration)"
    $adapter.BuildOut = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-$($adapter.Key)-adapter-build-$buildVersionSlug"
    if (Test-Path -LiteralPath $adapter.BuildOut) { Remove-Item -LiteralPath $adapter.BuildOut -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $adapter.BuildOut | Out-Null

    Invoke-Native $msbuild @(
        $adapter.ProjectPath,
        "/t:Rebuild",
        "/p:Configuration=$Configuration",
        "/p:OutputPath=$($adapter.BuildOut)",
        "/p:PawnDiaryReference=$builtDll",
        "/nologo",
        "/v:minimal"
    )

    $adapter.BuiltDll = Join-Path $adapter.BuildOut "$($adapter.AssemblyName).dll"
    if (-not (Test-Path -LiteralPath $adapter.BuiltDll)) {
        throw "Build reported success but $($adapter.AssemblyName).dll was not produced in $($adapter.BuildOut)."
    }
    $adapter.BuiltPdb = Join-Path $adapter.BuildOut "$($adapter.AssemblyName).pdb"
}

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

if ($buildExampleAdapter) {
    Write-Step "Prepare example adapter payload"
    $exampleAdapterPayload = New-ExampleAdapterPayload `
        -AdapterSourceRoot $exampleAdapterRoot `
        -DestinationRoot $ExampleAdapterOutDir `
        -AdapterPackageId $ExampleAdapterPackageId `
        -Version $publishedVersion `
        -CorePackageId $publishedPackageId `
        -CoreDisplayName $publishedModName `
        -CorePublishedFileId $mainPublishedFileId `
        -BuiltDll $builtExampleAdapterDll `
        -BuiltPdb $builtExampleAdapterPdb `
        -PublishedFileId $ExampleAdapterPublishedFileId `
        -PublishedFileIdPath $ExampleAdapterPublishedFileIdPath
}

if ($buildSpeakUpAdapter) {
    Write-Step "Prepare SpeakUp adapter payload"
    $speakUpAdapterPayload = New-SpeakUpAdapterPayload `
        -AdapterSourceRoot $speakUpAdapterRoot `
        -DestinationRoot $SpeakUpAdapterOutDir `
        -AdapterPackageId $SpeakUpAdapterPackageId `
        -Version $publishedVersion `
        -CorePackageId $publishedPackageId `
        -CoreDisplayName $publishedModName `
        -CorePublishedFileId $mainPublishedFileId `
        -BuiltDll $builtSpeakUpAdapterDll `
        -BuiltPdb $builtSpeakUpAdapterPdb `
        -PublishedFileId $SpeakUpAdapterPublishedFileId `
        -PublishedFileIdPath $SpeakUpAdapterPublishedFileIdPath
}

foreach ($adapter in $runtimeAdapters) {
    Write-Step "Prepare $($adapter.Label) adapter payload"
    $adapter.Payload = New-RuntimeAdapterPayload `
        -Label $adapter.Label `
        -AdapterSourceRoot $adapter.Root `
        -DestinationRoot $adapter.OutDir `
        -AdapterPackageId $adapter.PackageId `
        -Version $publishedVersion `
        -DevelopmentCorePackageId $devPackageId `
        -CorePackageId $publishedPackageId `
        -CoreDisplayName $publishedModName `
        -CorePublishedFileId $mainPublishedFileId `
        -AssemblyName $adapter.AssemblyName `
        -BuiltDll $adapter.BuiltDll `
        -BuiltPdb $adapter.BuiltPdb `
        -PublishedFileIdPath $adapter.PublishedFileIdPath
}

Remove-Item -LiteralPath $buildOut -Recurse -Force -ErrorAction SilentlyContinue
if ($exampleAdapterBuildOut) {
    Remove-Item -LiteralPath $exampleAdapterBuildOut -Recurse -Force -ErrorAction SilentlyContinue
}
if ($speakUpAdapterBuildOut) {
    Remove-Item -LiteralPath $speakUpAdapterBuildOut -Recurse -Force -ErrorAction SilentlyContinue
}
foreach ($adapter in $runtimeAdapters) {
    if ($adapter.BuildOut) {
        Remove-Item -LiteralPath $adapter.BuildOut -Recurse -Force -ErrorAction SilentlyContinue
    }
}

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
if ($buildExampleAdapter) {
    $exampleAdapterPayloadFiles = Get-ChildItem -Recurse -File $ExampleAdapterOutDir
    $exampleAdapterPayloadBytes = ($exampleAdapterPayloadFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host "Example adapter payload prepared:" -ForegroundColor Green
    Write-Host "  $ExampleAdapterOutDir"
    Write-Host "  Payload DLL: $($exampleAdapterPayload.DllPath)"
    Write-Host "  Prepared About.xml: $($exampleAdapterPayload.AboutPath)"
    Write-Host "  Source folder: $($exampleAdapterPayload.SourcePath)"
    Write-Host ("  shipped {0} files, {1:N2} MB" -f $exampleAdapterPayloadFiles.Count, ($exampleAdapterPayloadBytes / 1MB))
}
if ($buildSpeakUpAdapter) {
    $speakUpAdapterPayloadFiles = Get-ChildItem -Recurse -File $SpeakUpAdapterOutDir
    $speakUpAdapterPayloadBytes = ($speakUpAdapterPayloadFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host "SpeakUp adapter payload prepared:" -ForegroundColor Green
    Write-Host "  $SpeakUpAdapterOutDir"
    Write-Host "  Payload DLL: $($speakUpAdapterPayload.DllPath)"
    Write-Host "  Prepared About.xml: $($speakUpAdapterPayload.AboutPath)"
    Write-Host "  Source folder: $($speakUpAdapterPayload.SourcePath)"
    Write-Host ("  shipped {0} files, {1:N2} MB" -f $speakUpAdapterPayloadFiles.Count, ($speakUpAdapterPayloadBytes / 1MB))
}
foreach ($adapter in $runtimeAdapters) {
    $adapterPayloadFiles = Get-ChildItem -Recurse -File $adapter.OutDir
    $adapterPayloadBytes = ($adapterPayloadFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host "$($adapter.Label) adapter payload prepared:" -ForegroundColor Green
    Write-Host "  $($adapter.OutDir)"
    Write-Host "  Payload DLL: $($adapter.Payload.DllPath)"
    Write-Host "  Prepared About.xml: $($adapter.Payload.AboutPath)"
    Write-Host ("  shipped {0} files, {1:N2} MB" -f $adapterPayloadFiles.Count, ($adapterPayloadBytes / 1MB))
}

if ($InstallToMods) {
    Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $LinkName -TargetFolder $OutDir -AllowReplace:$Force
    if ($buildRussianLocalization) {
        Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $RussianLocalizationLinkName -TargetFolder $RussianLocalizationOutDir -AllowReplace:$Force
    }
    if ($buildExampleAdapter) {
        Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $ExampleAdapterLinkName -TargetFolder $ExampleAdapterOutDir -AllowReplace:$Force
    }
    if ($buildSpeakUpAdapter) {
        Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $SpeakUpAdapterLinkName -TargetFolder $SpeakUpAdapterOutDir -AllowReplace:$Force
    }
    foreach ($adapter in $runtimeAdapters) {
        Install-ModJunction -DestinationRoot $resolvedModsDir -FolderName $adapter.LinkName -TargetFolder $adapter.OutDir -AllowReplace:$Force
    }
}
