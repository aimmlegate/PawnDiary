param(
    [switch]$SelfTest
)

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

function Sha256-CanonicalUtf8File {
    param([string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF `
        -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    Require (-not $hasBom) "$Path contains a forbidden UTF-8 BOM."
    $value = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    return Sha256-Text ($value.Replace("`r`n", "`n").Replace("`r", "`n"))
}

function Read-OneArtifactFile {
    param(
        [string]$Pattern,
        [string]$Description
    )

    $matches = @(Get-ChildItem -LiteralPath $resultRoot -Filter $Pattern -File)
    Require ($matches.Count -eq 1) "Expected exactly one $Description for candidate source identity; found $($matches.Count)."
    return $matches[0]
}

function Require-GeneratedJsonScalar {
    param(
        [IO.FileInfo]$File,
        [string]$Name,
        [string]$ExpectedJson
    )

    # The benchmark owns this stable indented format. Anchoring at two spaces distinguishes the
    # top-level provenance field from similarly named values inside the large vector table.
    $pattern = '^  "' + [Regex]::Escape($Name) + '": ' +
        [Regex]::Escape($ExpectedJson) + ',?$'
    $matches = @(Select-String -LiteralPath $File.FullName -Pattern $pattern -CaseSensitive)
    Require ($matches.Count -eq 1) "$($File.Name) has stale or ambiguous '$Name'."
}

function Read-OneArtifact {
    param(
        [string]$Pattern,
        [string]$Description
    )

    $matches = @(Get-ChildItem -LiteralPath $resultRoot -Filter $Pattern -File)
    Require ($matches.Count -eq 1) "Expected exactly one $Description for candidate source identity; found $($matches.Count)."
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
    Require ([string]$Artifact.gitCommitObjectId -ceq $gitCommitObjectId) "$Context does not bind the candidate full commit."
    Require ([string]$Artifact.sourceCommitIdentity -ceq $sourceCommitIdentity) "$Context has the wrong source identity."
}

function Test-EvidenceOnlyPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $normalized = $Path.Replace('\', '/')
    return $normalized.StartsWith(
        'benchmarks/results/memory-system/',
        [StringComparison]::Ordinal)
}

function Test-CandidateLineage {
    param(
        [string]$CandidateCommit,
        [string]$RepositoryHead
    )

    if ($CandidateCommit -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { return $false }
    & git -C $repoRoot rev-parse --verify --quiet "${CandidateCommit}^{commit}" | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    & git -C $repoRoot merge-base --is-ancestor $CandidateCommit $RepositoryHead
    if ($LASTEXITCODE -ne 0) { return $false }
    $changed = @(& git -C $repoRoot diff --name-only "${CandidateCommit}..${RepositoryHead}" --)
    if ($LASTEXITCODE -ne 0) { return $false }
    return @($changed | Where-Object { -not (Test-EvidenceOnlyPath $_) }).Count -eq 0
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
$catalogRoot = Join-Path $repoRoot "benchmarks\MemoryThreadBenchmarks\Catalog"
$capacityPath = Join-Path $catalogRoot "memory-capacity-catalog-v1.json"
$fixturePath = Join-Path $catalogRoot "memory-m0-fixture-catalog-v1.json"
$payloadPath = Join-Path $catalogRoot "memory-payload-atom-catalog-v1.json"

if ($SelfTest) {
    Require (Test-EvidenceOnlyPath 'benchmarks/results/memory-system/evidence.json') "Evidence path policy rejected its exact directory."
    Require (Test-EvidenceOnlyPath 'benchmarks\results\memory-system\evidence.json') "Evidence path policy rejected Windows separators."
    Require (-not (Test-EvidenceOnlyPath 'benchmarks/results/memory-systemish/evidence.json')) "Evidence path policy accepted a prefix collision."
    Require (-not (Test-EvidenceOnlyPath 'Source/Pipeline/Knowledge/MemoryThreadContracts.cs')) "Evidence path policy accepted candidate source."
    $goldenIdentity = Sha256-Text ((Segment 'memory-source-commit-v1') + (Segment 'sha1') + (Segment 'd4b98ea4fdf3ccb05e2099cd0cf43963568a73a8'))
    Require ($goldenIdentity -ceq '8767148747a22820751214a6c1d20e90c395aa819a5d5b6f91c9f593aa03e94d') "Source-commit identity golden drifted."
    $selfTestHead = (& git -C $repoRoot rev-parse HEAD).Trim()
    Require ($LASTEXITCODE -eq 0 -and (Test-CandidateLineage $selfTestHead $selfTestHead)) "An exact candidate/HEAD lineage was rejected."
    Require (-not (Test-CandidateLineage ('0' * $selfTestHead.Length) $selfTestHead)) "A nonexistent candidate commit was accepted."
    Write-Host 'Memory release-evidence verifier self-tests passed: source identity and evidence-only lineage policy.'
    return
}

$activation = Get-Content -Raw -LiteralPath $activationPath
if ($activation -cnotmatch 'public const string BuildState = CurrentRelease;') {
    Write-Host "Memory release-evidence verification skipped: build state is not CurrentRelease."
    return
}

$gitObjectFormat = (& git -C $repoRoot rev-parse --show-object-format).Trim()
$repositoryHead = (& git -C $repoRoot rev-parse HEAD).Trim()
Require ($LASTEXITCODE -eq 0) "Could not resolve the repository commit identity."
Require ($gitObjectFormat -ceq "sha1" -or $gitObjectFormat -ceq "sha256") "Unsupported Git object format '$gitObjectFormat'."
$expectedCommitLength = if ($gitObjectFormat -ceq "sha1") { 40 } else { 64 }
Require ($repositoryHead -cmatch "^[0-9a-f]{$expectedCommitLength}$") "Repository HEAD is not full lowercase hexadecimal."
$candidateDllHash = Sha256-File $dllPath

# Evidence is generated from a clean candidate commit and then checked in by an evidence-only
# descendant commit. Binding artifacts to the verifier's current HEAD makes that workflow
# self-referential: committing the artifacts changes HEAD and invalidates them. Select the one
# manifest whose candidate is an ancestor, whose DLL still matches, and whose descendant diff
# contains evidence files only.
$releaseCandidates = @()
foreach ($file in @(Get-ChildItem -LiteralPath $resultRoot -Filter '*-memory-release-manifest-*.json' -File)) {
    try { $value = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json }
    catch { continue }
    $basicIdentityMatches = [string]$value.schema -ceq 'memory-release-candidate-manifest-v1' `
        -and [string]$value.gitObjectFormat -ceq $gitObjectFormat `
        -and [string]$value.gitCommitObjectId -cmatch "^[0-9a-f]{$expectedCommitLength}$"
    if (-not $basicIdentityMatches) { continue }
    $identity = Sha256-Text ((Segment 'memory-source-commit-v1') + (Segment $gitObjectFormat) + (Segment ([string]$value.gitCommitObjectId)))
    $candidateMatches = [string]$value.sourceCommitIdentity -ceq $identity `
        -and $file.Name.StartsWith(
            $identity + '-memory-release-manifest-', [StringComparison]::Ordinal) `
        -and [string]$value.candidateDllSha256 -ceq $candidateDllHash `
        -and (Test-CandidateLineage ([string]$value.gitCommitObjectId) $repositoryHead)
    if (-not $candidateMatches) { continue }
    $releaseCandidates += [pscustomobject]@{ File = $file; Value = $value }
}
Require ($releaseCandidates.Count -eq 1) "Expected exactly one release manifest for an unchanged candidate plus evidence-only descendants; found $($releaseCandidates.Count)."
$releaseArtifact = $releaseCandidates[0]
$release = $releaseArtifact.Value
$gitCommitObjectId = [string]$release.gitCommitObjectId
$sourceCommitIdentity = [string]$release.sourceCommitIdentity

# M11 cannot reuse M0/LegacyShadow surrogate output. Require a freshly generated pure artifact
# whose source identity, catalogs, activation, and production M4 reducer trace all match this release.
$pureFile = Read-OneArtifactFile "$sourceCommitIdentity-pure.json" "M11 pure benchmark artifact"
Require-GeneratedJsonScalar $pureFile "schema" '"memory-system-benchmark-v1"'
Require-GeneratedJsonScalar $pureFile "gitObjectFormat" ('"' + $gitObjectFormat + '"')
Require-GeneratedJsonScalar $pureFile "gitCommitObjectId" ('"' + $gitCommitObjectId + '"')
Require-GeneratedJsonScalar $pureFile "sourceCommitIdentity" ('"' + $sourceCommitIdentity + '"')
Require-GeneratedJsonScalar $pureFile "activationBuildState" '"CurrentRelease"'
Require-GeneratedJsonScalar $pureFile "capacityCatalogSha256" ('"' + (Sha256-CanonicalUtf8File $capacityPath) + '"')
Require-GeneratedJsonScalar $pureFile "fixtureCatalogSha256" ('"' + (Sha256-CanonicalUtf8File $fixturePath) + '"')
Require-GeneratedJsonScalar $pureFile "payloadAtomCatalogSha256" ('"' + (Sha256-CanonicalUtf8File $payloadPath) + '"')
Require-GeneratedJsonScalar $pureFile "pureCoverageDisposition" '"m0_capacity_surrogate_plus_m4_reducer_trace"'
Require-GeneratedJsonScalar $pureFile "retentionReducerTraceExecuted" 'true'
$selectedIdMatches = @(Select-String -LiteralPath $pureFile.FullName -CaseSensitive `
    -Pattern '^  "selectedVectorId": "([0-9a-f]{64})",?$')
Require ($selectedIdMatches.Count -eq 1) "$($pureFile.Name) has no unique top-level selected vector."
$pureSelectedVectorId = $selectedIdMatches[0].Matches[0].Groups[1].Value

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
Require (@($selected.entries | Where-Object { [string]$_.vectorId -cne $pureSelectedVectorId }).Count -eq 0) "The loaded selected rerun does not bind the pure benchmark's selected vector."
Require ([string]$selected.candidateDllSha256 -ceq $candidateDllHash) "The selected-rerun manifest does not bind the checked-in candidate DLL."

$selectedAggregateArtifact = Read-OneArtifact "$sourceCommitIdentity-rimtest-selected-aggregate-$($selected.manifestId).json" "M11 selected-rerun aggregate"
Require-LoadedAggregate $selectedAggregateArtifact.Value ([string]$selected.manifestId) "The M11 selected-rerun aggregate"

$dirty = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all) -join "`n"
Require ($LASTEXITCODE -eq 0) "Could not inspect repository cleanliness."
Require ([string]::IsNullOrWhiteSpace($dirty)) "Authenticated release evidence requires a clean committed worktree."

Write-Host "Memory M11 release evidence verified for source ${sourceCommitIdentity}: release candidates, defensive D, selected rerun, N=4/12/64, and accepted-prompt Scribe gates."
