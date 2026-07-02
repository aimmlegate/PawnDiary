# Copies every adapter mod under integrations/ to the RimWorld Mods root (siblings of this repo),
# because RimWorld does not load mods from nested folders. Safe by design: a destination folder is
# only replaced when it is clearly one of OUR adapters (its About.xml packageId starts with the
# adapter prefix), so this can never clobber an unrelated mod that happens to share a folder name.
#
# Usage (from the repo root):
#   powershell -ExecutionPolicy Bypass -File scripts\deploy-integrations.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\deploy-integrations.ps1 -ModsRoot "D:\...\Mods"
param(
    # RimWorld's Mods folder. Default: the parent of this repo (the repo itself lives in Mods/).
    [string]$ModsRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$integrationsDir = Join-Path $repoRoot 'integrations'
$adapterPackagePrefix = 'aimmlegate.pawndiary.adapter.'

if (-not (Test-Path $integrationsDir)) {
    Write-Output "No integrations/ folder found at $integrationsDir - nothing to deploy."
    exit 0
}

$adapters = Get-ChildItem -Path $integrationsDir -Directory
if ($adapters.Count -eq 0) {
    Write-Output 'No adapter folders under integrations/ - nothing to deploy.'
    exit 0
}

foreach ($adapter in $adapters) {
    $aboutPath = Join-Path $adapter.FullName 'About\About.xml'
    if (-not (Test-Path $aboutPath)) {
        Write-Output "SKIP  $($adapter.Name): no About/About.xml (not a mod folder)."
        continue
    }

    $target = Join-Path $ModsRoot $adapter.Name
    if (Test-Path $target) {
        # Replace the target only when it is one of our adapters; refuse anything else.
        $targetAbout = Join-Path $target 'About\About.xml'
        $targetIsOurs = $false
        if (Test-Path $targetAbout) {
            try {
                $packageId = ([xml](Get-Content -Raw -Encoding UTF8 $targetAbout)).ModMetaData.packageId
                $targetIsOurs = $null -ne $packageId -and $packageId.StartsWith($adapterPackagePrefix)
            } catch {}
        }

        if (-not $targetIsOurs) {
            Write-Output "FAIL  $($adapter.Name): $target exists and is not a Pawn Diary adapter - remove it manually if you really want this name."
            continue
        }

        Remove-Item -Recurse -Force -Confirm:$false $target
    }

    # Copy the whole adapter folder (Source/ included, matching the core mod's layout), minus
    # build intermediates.
    Copy-Item -Recurse -Force $adapter.FullName $target
    Get-ChildItem -Path $target -Recurse -Directory -Force |
        Where-Object { $_.Name -eq 'obj' -or $_.Name -eq 'bin' } |
        ForEach-Object { Remove-Item -Recurse -Force -Confirm:$false $_.FullName }
    Write-Output "OK    $($adapter.Name) -> $target"
}
