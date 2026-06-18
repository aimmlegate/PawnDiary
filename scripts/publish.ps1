<#
.SYNOPSIS
  Build the mod and produce a clean, Steam-Workshop-ready release.

.DESCRIPTION
  RimWorld's Workshop uploader publishes an ENTIRE mod folder verbatim - it has no
  ".gitignore". This dev repo also contains .git, Source, tests, .codegraph, editor
  configs and docs, so uploading the repo folder ships all of that (and can make the
  upload fail). This script avoids that by doing three things, in order:

    1. BUILD  - compiles PawnDiary.dll in Release into a throwaway temp folder, so the
                Debug DLL committed under 1.6/Assemblies (checked by the verify hook)
                is left untouched.
    2. COPY   - copies ONLY the files the mod needs to run into a clean payload folder
                (default: dist/<published packageId>). This is the folder you upload to Workshop.
                Shipped: About/ , 1.6/Assemblies/*.dll , 1.6/Defs/ , Languages/ , plus
                README/LICENSE/CHANGELOG if present. Nothing else. The published About.xml
                also gets its dev name/packageId postfix stripped (see -PackageId / -Author).
    3. SNAPSHOT - commits that same payload onto a history-free branch (release/<version>)
                and an annotated tag (v<version>) as an immutable record. This is built
                in a TEMPORARY git worktree, so your current branch and working tree are
                never touched.

  Mental model for a JS/TS dev: it's a `dist/` build (tsc + copy the publishable subset)
  followed by a `git tag`. The DLL is the compiled bundle; the Defs/Languages/About are
  the static assets that ship next to it.

.PARAMETER Version
  Release label used for the branch (release/<Version>) and tag (v<Version>).
  Defaults to beta-<today>, e.g. beta-20260619.

.PARAMETER OutDir
  Where the clean, uploadable payload is written. Default: <repo>/dist/<published packageId>.

.PARAMETER Configuration
  MSBuild configuration for the shipped DLL. Default: Release. Use Debug if you want the
  exact same build the verify hook produces.

.PARAMETER PackageId
  Override the published packageId outright. By default the script takes the dev catalog's
  packageId and strips a trailing "(developement)" / "(development)" postfix, e.g.
  aimmlegate.pawndiary(developement) -> aimmlegate.pawndiary. The dev About.xml keeps its
  postfix so the dev copy can sit in Mods next to the published copy without an id clash.

.PARAMETER Author
  Override the published <author> value. By default the dev catalog's author is kept.

.PARAMETER SkipBranch
  Build + produce the clean folder only; do not create the git branch/tag.

.PARAMETER Force
  Overwrite an existing release/<Version> branch and v<Version> tag if they already exist.

.EXAMPLE
  powershell -File scripts/publish.ps1 -Version 1.0.0-beta.1

.EXAMPLE
  # Just refresh the uploadable folder, no git snapshot:
  powershell -File scripts/publish.ps1 -SkipBranch
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

# Stop on the first unhandled error so a half-built release never looks like a success.
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------

function Write-Step {
    param([string]$Name)
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
}

# Run a native exe and throw on a non-zero exit code. We deliberately do NOT redirect
# the child's stderr: in Windows PowerShell, redirecting a native command's stderr wraps
# each line as an error record and (with -ErrorAction Stop) can throw spuriously even on
# success. We rely on $LASTEXITCODE instead.
function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed (exit ${LASTEXITCODE}): $FilePath $($Arguments -join ' ')"
    }
}

# Locate MSBuild the same way the verify hook does (PATH, then known VS install paths,
# then vswhere). This is a .NET Framework 4.7.2 legacy project, so we need MSBuild, not
# `dotnet build`.
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

# Copy one repo-relative path into the payload, preserving its folder structure.
# Returns $true if it existed and was copied. Used for optional files too.
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

# Pulls a simple one-line value from RimWorld's About.xml without reformatting the file.
function Get-AboutValue {
    param([string]$Text, [string]$Element)
    $match = [regex]::Match($Text, "(?s)<$Element>\s*(.*?)\s*</$Element>")
    if (-not $match.Success) { return "" }
    return $match.Groups[1].Value.Trim()
}

# Rewrites a simple About.xml element while preserving the surrounding whitespace and file layout.
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

# The local dev copy may be marked with the historical misspelling "(developement)"
# (or the corrected "(development)") so it can sit beside the Workshop copy.
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

# ---------------------------------------------------------------------------
# Resolve locations
# ---------------------------------------------------------------------------

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw "Not inside a git repository." }
Set-Location -LiteralPath $repoRoot

$sourceAboutPath = Join-Path $repoRoot "About\About.xml"
if (-not (Test-Path -LiteralPath $sourceAboutPath)) { throw "Required path missing: About\About.xml" }
$sourceAboutText = [System.IO.File]::ReadAllText($sourceAboutPath)
$devModName = Get-AboutValue $sourceAboutText "name"
$devPackageId = Get-AboutValue $sourceAboutText "packageId"
$publishedModName = Remove-DevelopmentPostfix $devModName
$publishedPackageId = if ([string]::IsNullOrWhiteSpace($PackageId)) {
    Remove-DevelopmentPostfix $devPackageId
} else {
    $PackageId.Trim()
}
if ([string]::IsNullOrWhiteSpace($publishedModName)) { $publishedModName = "Pawn Diary" }
if ([string]::IsNullOrWhiteSpace($publishedPackageId)) { $publishedPackageId = "pawndiary" }

$payloadFolderName = Get-SafeFolderName $publishedPackageId "pawndiary"
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "dist\$payloadFolderName" }

$branch = "release/$Version"
$tag    = "v$Version"

Write-Host "$publishedModName publish" -ForegroundColor Green
Write-Host "  version       : $Version"
Write-Host "  configuration : $Configuration"
Write-Host "  mod name      : $publishedModName"
Write-Host "  packageId     : $publishedPackageId"
Write-Host "  payload out   : $OutDir"
Write-Host "  git snapshot  : $(if ($SkipBranch) { 'skipped' } else { "$branch (+ tag $tag)" })"

# Fail early on an existing branch/tag unless -Force, so a re-run never silently no-ops.
if (-not $SkipBranch) {
    $branchExists = [bool](& git branch --list $branch)
    $tagExists    = [bool](& git tag --list $tag)
    if (($branchExists -or $tagExists) -and -not $Force) {
        throw "release '$Version' already exists (branch:$branchExists tag:$tagExists). Re-run with -Force to overwrite, or pick a new -Version."
    }
}

# ---------------------------------------------------------------------------
# 1. Build the DLL (Release) into a temp folder, NOT into 1.6/Assemblies
# ---------------------------------------------------------------------------

Write-Step "Build PawnDiary.dll ($Configuration)"
$msbuild   = Find-MSBuild
$buildOut  = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-build-$Version"
if (Test-Path $buildOut) { Remove-Item -Recurse -Force $buildOut }
New-Item -ItemType Directory -Force -Path $buildOut | Out-Null

# /p:OutputPath redirects the build output away from the committed Debug DLL under
# 1.6/Assemblies. 0Harmony.dll is copied here too (it is a <Private>True</Private> ref).
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

# ---------------------------------------------------------------------------
# 2. Assemble the clean payload (only what the mod needs to run)
# ---------------------------------------------------------------------------

Write-Step "Assemble clean payload"
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# About metadata + preview + the PublishedFileId that ties uploads to the same item.
Copy-Payload "About\About.xml"          -Required | Out-Null
Copy-Payload "About\Preview.png"        -Required | Out-Null
Copy-Payload "About\PublishedFileId.txt"          | Out-Null   # absent only before the very first upload

# Normalize the published About.xml metadata. The dev catalog's name/packageId may carry
# a postfix (e.g. "(developement)") so the dev copy can live in Mods next to the published
# copy without a duplicate-id clash; the PUBLISHED mod must use the clean values. -Author
# optionally rewrites the author. We edit text surgically (not via [xml].Save) so nothing
# else reformats.
$aboutPath = Join-Path $OutDir "About\About.xml"
$aboutText = [System.IO.File]::ReadAllText($aboutPath)

$payloadModName = Get-AboutValue $aboutText "name"
if (-not [string]::IsNullOrWhiteSpace($payloadModName)) {
    $cleanName = Remove-DevelopmentPostfix $payloadModName
    if ([string]::IsNullOrWhiteSpace($cleanName)) { $cleanName = $payloadModName.Trim() }
    $publishedModName = $cleanName
    if ($cleanName -ne $payloadModName) {
        $aboutText = Set-AboutValue $aboutText "name" $cleanName
        Write-Host "  name          : '$payloadModName' -> '$cleanName' (dev postfix stripped)"
    } else {
        Write-Host "  name          : $cleanName"
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
    $publishedPackageId = $cleanId
    if ($cleanId -ne $devId) {
        $aboutText = Set-AboutValue $aboutText "packageId" $cleanId
        Write-Host "  packageId     : '$devId' -> '$cleanId' (dev postfix stripped)"
    } else {
        Write-Host "  packageId     : $cleanId"
    }
    if ($cleanId -notmatch '^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$') {
        Write-Warning "Published packageId '$cleanId' is not a valid RimWorld id (letters/digits/underscores, dot-separated)."
    }
} else {
    throw "About.xml does not contain a <packageId>."
}

if (-not [string]::IsNullOrWhiteSpace($Author)) {
    $devAuthor = Get-AboutValue $aboutText "author"
    $aboutText = Set-AboutValue $aboutText "author" $Author.Trim()
    Write-Host "  author        : '$devAuthor' -> '$Author'"
}

[System.IO.File]::WriteAllText($aboutPath, $aboutText, (New-Object System.Text.UTF8Encoding($false)))

# Defs: the whole 1.6/Defs folder (all Diary*Defs.xml) — the mod will not work without these.
Copy-Payload "1.6\Defs"                 -Required | Out-Null

# Localization keys.
Copy-Payload "Languages"                          | Out-Null

# Freshly built assemblies (DLLs only — never ship the .pdb).
$asmDir = Join-Path $OutDir "1.6\Assemblies"
New-Item -ItemType Directory -Force -Path $asmDir | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $asmDir -Force
$harmony = Join-Path $buildOut "0Harmony.dll"
if (Test-Path $harmony) {
    Copy-Item -LiteralPath $harmony -Destination $asmDir -Force
} else {
    # Fall back to the committed copy so we never ship without Harmony.
    Copy-Payload "1.6\Assemblies\0Harmony.dll" -Required | Out-Null
}

# Optional, nice-to-have root docs (harmless inside a mod folder).
foreach ($doc in @("README.md", "CHANGELOG.md", "LICENSE", "LICENSE.txt", "LICENSE.md")) {
    Copy-Payload $doc | Out-Null
}

# Throwaway build output no longer needed.
Remove-Item -Recurse -Force $buildOut -ErrorAction SilentlyContinue

# Report what shipped.
$payloadFiles = Get-ChildItem -Recurse -File $OutDir
$payloadBytes = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("  shipped {0} files, {1:N2} MB" -f $payloadFiles.Count, ($payloadBytes / 1MB))
$defCount = (Get-ChildItem -Recurse -File (Join-Path $OutDir "1.6\Defs") -Filter *.xml).Count
Write-Host "  Defs included : $defCount XML file(s)"

# ---------------------------------------------------------------------------
# 3. Snapshot the payload onto release/<version> (+ tag) in a temp worktree
# ---------------------------------------------------------------------------

if (-not $SkipBranch) {
    Write-Step "Snapshot to git ($branch + $tag)"

    if ($Force) {
        if ([bool](& git branch --list $branch)) { Invoke-Native "git" @("branch", "-D", $branch) }
        if ([bool](& git tag    --list $tag))    { Invoke-Native "git" @("tag", "-d", $tag) }
    }

    # A linked worktree lets us build the release commit without checking out or
    # disturbing the user's current branch/working tree.
    $wt = Join-Path ([System.IO.Path]::GetTempPath()) "pawndiary-rel-wt-$Version"
    if (Test-Path $wt) {
        & git worktree remove $wt --force | Out-Null
        if (Test-Path $wt) { Remove-Item -Recurse -Force $wt -ErrorAction SilentlyContinue }
    }

    Invoke-Native "git" @("worktree", "add", "--detach", $wt, "HEAD")
    try {
        Push-Location $wt

        # Start an orphan branch (no parent history), then strip every tracked file so the
        # tree will contain ONLY the clean payload we copy in next.
        Invoke-Native "git" @("checkout", "--orphan", $branch)
        & git rm -r --force --ignore-unmatch --quiet -- . | Out-Null
        # Remove any stray untracked leftovers, but never the worktree's own .git pointer.
        Get-ChildItem -Force . | Where-Object { $_.Name -ne ".git" } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        Copy-Item -Path (Join-Path $OutDir "*") -Destination $wt -Recurse -Force

        Invoke-Native "git" @("add", "-A")
        Invoke-Native "git" @("commit", "-m", "Release $Version", "--quiet")
        Invoke-Native "git" @("tag", "-a", $tag, "-m", "$publishedModName release $Version")

        Pop-Location
    }
    finally {
        # Always clean up the temp worktree, even if the commit failed.
        Set-Location -LiteralPath $repoRoot
        if (Test-Path $wt) {
            & git worktree remove $wt --force
            if (Test-Path $wt) { Remove-Item -Recurse -Force $wt -ErrorAction SilentlyContinue }
        }
        & git worktree prune
    }

    Write-Host "  created branch $branch and tag $tag (orphan snapshot; your current branch is untouched)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Done — print next steps
# ---------------------------------------------------------------------------

Write-Step "Done"
Write-Host "Clean, uploadable mod folder:" -ForegroundColor Green
Write-Host "  $OutDir"
Write-Host ""
Write-Host "To upload to the Steam Workshop:"
Write-Host "  1. Copy '$OutDir' into your RimWorld 'Mods' folder, e.g. Mods\$payloadFolderName."
Write-Host "  2. Temporarily move the dev repo (this folder) OUT of Mods, or rename it, so"
Write-Host "     RimWorld does not see two mods with packageId '$publishedPackageId'."
Write-Host "  3. Launch RimWorld via Steam, enable Development mode, open Mods, select"
Write-Host "     '$publishedModName', and click 'Upload to Steam Workshop'."
Write-Host "     (PublishedFileId.txt is included, so this UPDATES the existing item.)"
Write-Host "  4. If this is the first upload on this account, accept the one-time Workshop"
Write-Host "     Legal Agreement, then upload again:"
Write-Host "     https://steamcommunity.com/sharedfiles/workshoplegalagreement"
Write-Host "  5. On the item's Steam page, set Visibility to 'Friends Only'."
