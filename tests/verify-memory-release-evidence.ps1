param()

$ErrorActionPreference = "Stop"

# This verifier is intentionally release-only. Ordinary correctness hooks compile the loaded
# fixtures but agents may not launch RimWorld, so only authenticated artifacts from the user's
# loaded M11 run can satisfy this gate. Provisional M0 JSON and smoke-test log lines never can.

function Require {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Segment {
    param([string]$Value)

    if ($null -eq $Value) { $Value = "" }
    return $Value.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + ":" + $Value
}

function Sha256-Text {
    param([string]$Value)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Sha256-File {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-OneArtifact {
    param(
        [string]$Pattern,
        [string]$Description
    )

    $matches = @(Get-ChildItem -LiteralPath $resultRoot -Filter $Pattern -File)
    Require ($matches.Count -eq 1) "Expected exactly one $Description for current source identity; found $($matches.Count)."
    $value = Get-Content -Raw -LiteralPath $matches[0].FullName | ConvertFrom-Json
    return [pscustomobject]@{ File = $matches[0]; Value = $value }
}

function Require-Property {
    param(
        [object]$Object,
        [string]$Name,
        [string]$Context
    )

    Require ($null -ne $Object -and $Object.PSObject.Properties.Name -ccontains $Name) "$Context is missing '$Name'."
}

function Require-SourceIdentity {
    param(
        [object]$Artifact,
        [string]$Context
    )

    Require-Property $Artifact "gitObjectFormat" $Context
    Require-Property $Artifact "gitCommitObjectId" $Context
    Require-Property $Artifact "sourceCommitIdentity" $Context
    Require ([string]$Artifact.gitObjectFormat -ceq $gitObjectFormat) "$Context has the wrong Git object format."
    Require ([string]$Artifact.gitCommitObjectId -ceq $gitCommitObjectId) "$Context does not bind the current full commit."
    Require ([string]$Artifact.sourceCommitIdentity -ceq $sourceCommitIdentity) "$Context has the wrong source identity."
}

function Require-ThreeTargets {
    param(
        [object[]]$Entries,
        [string]$Disposition,
        [bool]$RequireExactlyOneVector
    )

    $selected = @($Entries | Where-Object { [string]$_.disposition -ceq $Disposition })
    Require ($selected.Count -gt 0) "The manifest has no '$Disposition' rows."
    $groups = @($selected | Group-Object { [string]$_.vectorId })
    if ($RequireExactlyOneVector) {
        Require ($groups.Count -eq 1) "'$Disposition' must describe exactly one vector."
    }
    foreach ($group in $groups) {
        $targets = @($group.Group | ForEach-Object { [int]$_.threadTarget } | Sort-Object)
        Require (($targets -join "/") -ceq "4/12/64") "'$Disposition' vector '$($group.Name)' lacks exact N=4/12/64 siblings."
        $ids = @($group.Group | ForEach-Object { [string]$_.manifestEntryId })
        Require (($ids | Sort-Object -Unique -CaseSensitive).Count -eq 3) "'$Disposition' vector '$($group.Name)' repeats a manifest-entry identity."
    }
}

function Require-LoadedAggregate {
    param(
        [object]$Aggregate,
        [string]$ExpectedManifestId,
        [string]$Context
    )

    Require ([string]$Aggregate.schema -ceq "memory-loaded-aggregate-evidence-v1") "$Context has the wrong schema."
    Require-SourceIdentity $Aggregate $Context
    Require ([string]$Aggregate.manifestId -ceq $ExpectedManifestId) "$Context does not bind its exact manifest."
    Require-Property $Aggregate "cells" $Context
    $cells = @($Aggregate.cells)
    Require ($cells.Count -gt 0) "$Context contains no authenticated loaded cells."
    $cellKeys = @($cells | ForEach-Object {
        [string]$_.manifestId + "`0" + [string]$_.manifestEntryId + "`0" + [string]$_.cellId
    })
    Require (($cellKeys | Sort-Object -Unique -CaseSensitive).Count -eq $cells.Count) "$Context repeats a loaded cell key."
    Require (@($cells | Where-Object { -not [bool]$_.passed }).Count -eq 0) "$Context contains a failed loaded cell."
    $gates = @($cells | ForEach-Object { [string]$_.operationOrGateId })
    Require ($gates -ccontains "SCRIBE-ACCEPTED-PROMPT-OWNER") "$Context lacks owner accepted-prompt Scribe evidence."
    Require ($gates -ccontains "SCRIBE-ACCEPTED-PROMPT-GLOBAL") "$Context lacks global accepted-prompt Scribe evidence."
    if ($Aggregate.PSObject.Properties.Name -ccontains "loadedPendingFixtures") {
        Require (@($Aggregate.loadedPendingFixtures).Count -eq 0) "$Context still reports loaded-pending fixture families."
    }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resultRoot = Join-Path $repoRoot "benchmarks\results\memory-system"
$activationPath = Join-Path $repoRoot "Source\Pipeline\Knowledge\MemoryThreadContracts.cs"
$dllPath = Join-Path $repoRoot "1.6\Assemblies\PawnDiary.dll"

$activation = Get-Content -Raw -LiteralPath $activationPath
if ($activation -cnotmatch 'public const string BuildState = CurrentRelease;') {
    Write-Host "Memory release-evidence verification skipped: build state is not CurrentRelease."
    return
}

$gitObjectFormat = (& git -C $repoRoot rev-parse --show-object-format).Trim()
$gitCommitObjectId = (& git -C $repoRoot rev-parse HEAD).Trim()
Require ($LASTEXITCODE -eq 0) "Could not resolve the repository commit identity."
Require ($gitObjectFormat -ceq "sha1" -or $gitObjectFormat -ceq "sha256") "Unsupported Git object format '$gitObjectFormat'."
$expectedCommitLength = if ($gitObjectFormat -ceq "sha1") { 40 } else { 64 }
Require ($gitCommitObjectId -cmatch "^[0-9a-f]{$expectedCommitLength}$") "Git commit identity is not full lowercase hexadecimal."
$sourceCommitIdentity = Sha256-Text ((Segment "memory-source-commit-v1") + (Segment $gitObjectFormat) + (Segment $gitCommitObjectId))
$candidateDllHash = Sha256-File $dllPath

$releaseArtifact = Read-OneArtifact "$sourceCommitIdentity-memory-release-manifest-*.json" "M11 release manifest"
$release = $releaseArtifact.Value
Require ([string]$release.schema -ceq "memory-release-candidate-manifest-v1") "The M11 release manifest has the wrong schema."
Require-SourceIdentity $release "The M11 release manifest"
Require-Property $release "manifestId" "The M11 release manifest"
Require-Property $release "entries" "The M11 release manifest"
Require ([string]$release.candidateDllSha256 -ceq $candidateDllHash) "The M11 release manifest does not bind the checked-in candidate DLL."
$releaseEntries = @($release.entries)
Require-ThreeTargets $releaseEntries "releaseCandidate" $false
Require-ThreeTargets $releaseEntries "defensiveCeilingAudit" $true

$releaseAggregateArtifact = Read-OneArtifact "$sourceCommitIdentity-rimtest-aggregate-$($release.manifestId).json" "M11 loaded release aggregate"
Require-LoadedAggregate $releaseAggregateArtifact.Value ([string]$release.manifestId) "The M11 loaded release aggregate"

$selectedManifestArtifact = Read-OneArtifact "$sourceCommitIdentity-rimtest-selected-rerun-*.json" "M11 selected-rerun manifest"
$selected = $selectedManifestArtifact.Value
Require ([string]$selected.schema -ceq "memory-selected-rerun-manifest-v1") "The M11 selected-rerun manifest has the wrong schema."
Require-SourceIdentity $selected "The M11 selected-rerun manifest"
Require ([bool]$selected.selectedReleaseRerun) "The selected-rerun manifest is not domain-separated as a selected rerun."
Require ([int]$selected.shardCount -eq 1) "The selected-rerun manifest must use exactly one shard."
Require-ThreeTargets @($selected.entries) "selectedReleaseRerun" $true
Require ([string]$selected.candidateDllSha256 -ceq $candidateDllHash) "The selected-rerun manifest does not bind the checked-in candidate DLL."

$selectedAggregateArtifact = Read-OneArtifact "$sourceCommitIdentity-rimtest-selected-aggregate-$($selected.manifestId).json" "M11 selected-rerun aggregate"
Require-LoadedAggregate $selectedAggregateArtifact.Value ([string]$selected.manifestId) "The M11 selected-rerun aggregate"

$dirty = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all) -join "`n"
Require ($LASTEXITCODE -eq 0) "Could not inspect repository cleanliness."
Require ([string]::IsNullOrWhiteSpace($dirty)) "Authenticated release evidence requires a clean committed worktree."

Write-Host "Memory M11 release evidence verified for source ${sourceCommitIdentity}: release candidates, defensive D, selected rerun, N=4/12/64, and accepted-prompt Scribe gates."
