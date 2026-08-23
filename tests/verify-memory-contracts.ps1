param()

$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 does not always load LINQ-to-XML on first type use.
# Load it explicitly so this standalone contract verifier behaves like the pwsh hook path.
Add-Type -AssemblyName System.Xml.Linq

function Require {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Child-Text {
    param(
        [System.Xml.Linq.XElement]$Parent,
        [string]$Name
    )

    $element = $Parent.Element([System.Xml.Linq.XName]$Name)
    if ($null -eq $element) { return "" }
    return $element.Value.Trim()
}

function Child-ExactText {
    param(
        [System.Xml.Linq.XElement]$Parent,
        [string]$Name
    )

    $element = $Parent.Element([System.Xml.Linq.XName]$Name)
    if ($null -eq $element) { return "" }
    return $element.Value
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$importantPath = Join-Path $repoRoot "1.6\Defs\DiaryImportantEventDefs.xml"
$tuningPath = Join-Path $repoRoot "1.6\Defs\DiaryKnowledgeTuningDef.xml"
$englishPath = Join-Path $repoRoot "Languages\English\Keyed\PawnDiary.xml"
$russianInjectedPath = Join-Path $repoRoot "Languages\Russian (Русский)\DefInjected\PawnDiary.DiaryImportantEventDef\DiaryImportantEventDefs.xml"
$catalogRoot = Join-Path $repoRoot "benchmarks\MemoryThreadBenchmarks\Catalog"
$capacityPath = Join-Path $catalogRoot "memory-capacity-catalog-v1.json"
$fixturePath = Join-Path $catalogRoot "memory-m0-fixture-catalog-v1.json"
$payloadPath = Join-Path $catalogRoot "memory-payload-atom-catalog-v1.json"

# Loading through XDocument catches malformed XML while preserving case-sensitive element names.
$important = [System.Xml.Linq.XDocument]::Load($importantPath)
$tuning = [System.Xml.Linq.XDocument]::Load($tuningPath)
$english = [System.Xml.Linq.XDocument]::Load($englishPath)
$russianInjected = [System.Xml.Linq.XDocument]::Load($russianInjectedPath)
$capacity = Get-Content -Raw -LiteralPath $capacityPath | ConvertFrom-Json
$fixture = Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json
$payload = Get-Content -Raw -LiteralPath $payloadPath | ConvertFrom-Json

Require ($capacity.schema -ceq "memory-capacity-catalog-v1") "Wrong memory capacity catalog schema."
Require ($fixture.schema -ceq "memory-m0-fixture-catalog-v1") "Wrong M0 fixture catalog schema."
Require ($payload.schema -ceq "memory-benchmark-payload-atom-catalog-v1") "Wrong payload atom catalog schema."
Require ($fixture.activationBuildState -ceq "LegacyShadow") "M0 must remain behind LegacyShadow."

$knownKinds = @("event", "landmark", "summary")
$knownCategories = @("personal", "relationships", "family", "factions")
$knownImportance = @("low", "medium", "high")
$knownSubjects = @("pawn", "faction", "stream")
$knownConsumers = @(
    "ordinary_diary", "existing_reflection", "narrative_arc", "comparison",
    "anniversary", "quiet_memory", "summary_wording"
)
$aggregationValues = @{
    count_occurrences = "empty"
    ordinal_set = "ordinal"
    int64_range = "int64"
    latest_state = "state"
}

$defs = @($important.Root.Elements("PawnDiary.DiaryImportantEventDef"))
Require ($defs.Count -eq 29) "Expected 29 shipped important-event Defs, found $($defs.Count)."
$defNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$eventFacts = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$standaloneCount = 0
foreach ($def in $defs) {
    $defName = Child-Text $def "defName"
    Require (-not [string]::IsNullOrWhiteSpace($defName)) "A capture Def has no defName."
    Require ($defNames.Add($defName)) "Duplicate capture Def: $defName"
    Require ($knownKinds -ccontains (Child-Text $def "memoryKind")) "$defName has invalid memoryKind."
    Require ($knownCategories -ccontains (Child-Text $def "memoryCategory")) "$defName has invalid memoryCategory."
    Require ($knownImportance -ccontains (Child-Text $def "baseImportance")) "$defName has invalid baseImportance."
    Require (-not [string]::IsNullOrWhiteSpace((Child-Text $def "captureSourceToken"))) "$defName has no captureSourceToken."
    foreach ($tokenField in @('captureSourceToken','memoryKind','memoryCategory','baseImportance')) {
        $rawToken = Child-ExactText $def $tokenField
        Require ($rawToken -ceq $rawToken.Trim()) "$defName/$tokenField has noncanonical surrounding whitespace."
    }
    Require (-not [string]::IsNullOrWhiteSpace((Child-Text $def "consolidationEligible"))) "$defName has no consolidation decision."
    Require (-not [string]::IsNullOrWhiteSpace((Child-Text $def "authoritativePageOwned"))) "$defName has no page-ownership decision."

    $factsElement = $def.Element([System.Xml.Linq.XName]"memoryFacts")
    Require ($null -ne $factsElement) "$defName has no memoryFacts."
    $facts = @($factsElement.Elements("li"))
    Require ($facts.Count -gt 0) "$defName has an empty memoryFacts list."
    foreach ($fact in $facts) {
        $factKind = Child-Text $fact "factKind"
        $aggregation = Child-Text $fact "aggregationToken"
        $valueKind = Child-Text $fact "canonicalValueKind"
        foreach ($tokenField in @('factKind','contextKey','aggregationToken','canonicalValueKind')) {
            $rawToken = Child-ExactText $fact $tokenField
            Require ($rawToken -ceq $rawToken.Trim()) "$defName/fact/$tokenField has noncanonical surrounding whitespace."
        }
        Require (-not [string]::IsNullOrWhiteSpace($factKind)) "$defName has a blank factKind."
        Require ($aggregationValues.ContainsKey($aggregation)) "$defName/$factKind has an unknown aggregation token."
        Require ($aggregationValues[$aggregation] -ceq $valueKind) "$defName/$factKind has a mismatched value grammar."
        $compound = "$defName`0$factKind`0$(Child-Text $def 'memoryCategory')"
        Require ($eventFacts.Add($compound)) "$defName emits one fact/category twice."
        if ($aggregation -ceq "latest_state") {
            $states = $fact.Element([System.Xml.Linq.XName]"allowedStates")
            Require ($null -ne $states -and @($states.Elements("li")).Count -gt 0) "$defName/$factKind state grammar lacks an allowlist."
            foreach ($state in $states.Elements("li")) {
                Require ($state.Value -ceq $state.Value.Trim()) "$defName/$factKind has a noncanonical allowed state."
            }
        }
    }

    $route = $def.Element([System.Xml.Linq.XName]"threadRoute")
    if ($null -eq $route) {
        $standaloneCount++
    }
    else {
        foreach ($tokenField in @('subjectKind','chapterPhasePolicy','fallbackLabelSource')) {
            $rawToken = Child-ExactText $route $tokenField
            Require ($rawToken -ceq $rawToken.Trim()) "$defName/route/$tokenField has noncanonical surrounding whitespace."
        }
        Require ($knownSubjects -ccontains (Child-Text $route "subjectKind")) "$defName has an invalid route subject kind."
        $extractors = $route.Element([System.Xml.Linq.XName]"equivalentExtractors")
        Require ($null -ne $extractors -and @($extractors.Elements("li")).Count -gt 0) "$defName has no exact route extractor."
        $extractorValues = @($extractors.Elements("li") | ForEach-Object { $_.Value.Trim() })
        foreach ($extractor in $extractors.Elements("li")) {
            Require ($extractor.Value -ceq $extractor.Value.Trim()) "$defName has a noncanonical route extractor."
        }
        Require (($extractorValues | Sort-Object -Unique -CaseSensitive).Count -eq $extractorValues.Count) "$defName repeats a route extractor."
    }

    $consumerElement = $def.Element([System.Xml.Linq.XName]"promptConsumerIds")
    Require ($null -ne $consumerElement) "$defName has no prompt consumer declaration."
    $consumers = @($consumerElement.Elements("li") | ForEach-Object { $_.Value.Trim() })
    Require ($consumers.Count -gt 0) "$defName is prompt-unreachable."
    foreach ($consumer in $consumers) {
        Require ($knownConsumers -ccontains $consumer) "$defName names unknown consumer '$consumer'."
    }
    foreach ($consumer in $consumerElement.Elements("li")) {
        Require ($consumer.Value -ceq $consumer.Value.Trim()) "$defName has a noncanonical consumer token."
    }

    # Existing Def prose uses source-English plus Russian DefInjected parity. New M0 fields are tokens,
    # not prose, so no second localization mechanism is introduced.
    Require ($null -ne $russianInjected.Root.Element([System.Xml.Linq.XName]"$defName.label")) "Missing Russian label for $defName."
    Require ($null -ne $russianInjected.Root.Element([System.Xml.Linq.XName]"$defName.lineTemplate")) "Missing Russian lineTemplate for $defName."
}
Require ($standaloneCount -eq 1) "Expected exactly one intentionally Standalone shipped capture rule."

$dimensions = @($capacity.dimensions)
Require ($dimensions.Count -eq 64) "Capacity catalog must contain exactly 64 dimensions."
$dimensionNames = @($dimensions | ForEach-Object { [string]$_.name })
Require (($dimensionNames | Sort-Object -Unique -CaseSensitive).Count -eq 64) "Capacity dimension names are not unique."
Require (@($capacity.startVector.psobject.Properties).Count -eq 64) "Start vector is not exhaustive."
foreach ($dimension in $dimensions) {
    $start = [string]$capacity.startVector.($dimension.name)
    Require (@($dimension.values) -ccontains $start) "Start vector value is not listed for $($dimension.name)."
    foreach ($value in @($dimension.values)) {
        Require ([string]$value -cmatch '^(0|[1-9][0-9]*)(/(0|[1-9][0-9]*))*$') "Noncanonical capacity value '$value'."
    }
}

$tuningDef = $tuning.Root.Element([System.Xml.Linq.XName]"PawnDiary.DiaryKnowledgeTuningDef")
$xmlVector = @($tuningDef.Element([System.Xml.Linq.XName]"memoryCapacityVector").Elements("li"))
Require ($xmlVector.Count -eq 64) "XML production vector must contain exactly 64 rows."
for ($index = 0; $index -lt $xmlVector.Count; $index++) {
    $expectedName = $dimensionNames[$index]
    $actualName = Child-Text $xmlVector[$index] "name"
    Require ($actualName -ceq $expectedName) "XML capacity order mismatch at ${index}: $actualName vs $expectedName."
}

Require ((Child-Text $tuningDef "minorMemoryLifetimeDefaultDays") -ceq "15") "Minor lifetime default drifted."
Require ((Child-Text $tuningDef "regularMemoryLifetimeDefaultDays") -ceq "60") "Regular lifetime default drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetMinimum") -ceq "4") "Thread target minimum drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetDefault") -ceq "12") "Thread target default drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetMaximum") -ceq "64") "Thread target maximum drifted."
Require ((Child-Text $tuningDef "quietReflectionChanceBasisPoints") -ceq "200") "Quiet chance drifted."

$fixedRows = @($fixture.fixedRows)
$expectedFixedNames = @(
    'activeDiaryRetention','threadTarget','rollingSummaryPerRoot','summarySubjectLookupEntries',
    'summaryOpportunityPerOwner','settingsNumericDraftUnits','minorRegularLifetimeDays',
    'quietReflectionBasisPoints','repetitionDaysEntries','brainwipeMetadataReserveBytes',
    'summarySearchScratchUnits'
)
Require ($fixedRows.Count -eq $expectedFixedNames.Count) "The fixed/derived registry row count drifted."
for ($index = 0; $index -lt $fixedRows.Count; $index++) {
    Require ([string]$fixedRows[$index].name -ceq $expectedFixedNames[$index]) "Fixed/derived registry order drifted at $index."
    Require (@('fixedProduct','fixedInvariant','derivedEquality') -ccontains [string]$fixedRows[$index].disposition) "Fixed/derived registry disposition is invalid at $index."
    Require (-not [string]::IsNullOrWhiteSpace([string]$fixedRows[$index].gate)) "Fixed/derived registry gate is blank at $index."
    if ([string]$fixedRows[$index].disposition -cne 'derivedEquality') {
        Require ([string]$fixedRows[$index].value -cmatch '^(0|[1-9][0-9]*)(/(0|[1-9][0-9]*))*$') "Fixed product value is noncanonical at $index."
    }
}

$englishKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($element in $english.Root.Elements()) { [void]$englishKeys.Add($element.Name.LocalName) }
foreach ($key in @($fixture.requiredLocalizationKeys)) {
    Require ($englishKeys.Contains([string]$key)) "Missing English Keyed memory string: $key"
}
$englishMemoryKeys = @($english.Root.Elements() | ForEach-Object { $_.Name.LocalName } | Where-Object { $_.StartsWith('PawnDiary.Memory.', [System.StringComparison]::Ordinal) })
Require ($englishMemoryKeys.Count -eq @($fixture.requiredLocalizationKeys).Count) "The M0 memory localization registry is not exhaustive."

$payloadTypes = @($payload.types)
Require ($payloadTypes.Count -eq 32) "Payload schema catalog must contain the exact 32 M0 types."
$payloadTypeNames = @($payloadTypes | ForEach-Object { [string]$_.name })
Require (($payloadTypeNames | Sort-Object -Unique -CaseSensitive).Count -eq $payloadTypes.Count) "Payload type names are duplicated."
foreach ($type in $payloadTypes) {
    $fields = @($type.fields | ForEach-Object { [string]$_ })
    Require ($fields.Count -gt 0) "Payload type $($type.name) has no fields."
    Require (($fields | Sort-Object -Unique -CaseSensitive).Count -eq $fields.Count) "Payload type $($type.name) repeats a field."
}
$expectedAtomPaths = [System.Collections.Generic.List[string]]::new()
foreach ($type in $payloadTypes) {
    foreach ($field in @($type.fields)) {
        $expectedAtomPaths.Add("$($type.name).$field")
    }
}
$atomRows = @($payload.atomRows)
Require ($expectedAtomPaths.Count -eq 399) "Payload schema must declare the exact 399 M0 field paths."
Require ($atomRows.Count -eq 399) "Payload atom catalog must contain the exact 399 M0 atoms."
for ($index = 0; $index -lt $atomRows.Count; $index++) {
    $atom = $atomRows[$index]
    Require ([int]$atom.pathOrdinal -eq $index) "Payload atom ordinal drifted at $index."
    Require ([string]$atom.canonicalFieldPath -ceq $expectedAtomPaths[$index]) "Payload atom path drifted at $index."
    Require (@($atom.scopeMask).Count -gt 0) "Payload atom $index has no scope mask."
    Require (@('bool','int32','int64','string','row','nullable_row','list') -ccontains [string]$atom.atomKindToken) "Payload atom $index has an invalid atom kind."
    Require ($null -ne $atom.candidateValueEncoding) "Payload atom $index has no candidate value encoding."
}
$atomKindByPath = @{}
foreach ($atom in $atomRows) { $atomKindByPath[[string]$atom.canonicalFieldPath] = [string]$atom.atomKindToken }
Require ($atomKindByPath['PawnKnowledgeState.schemaVersion'] -ceq 'int32') "PawnKnowledgeState.schemaVersion must be int32."
Require ($atomKindByPath['SavedActiveLogicalRequestV1.schemaVersion'] -ceq 'int32') "SavedActiveLogicalRequestV1.schemaVersion must be int32."
Require ($atomKindByPath['SavedMemoryAppliedPolicyStateV1.saveNewMemories'] -ceq 'bool') "saveNewMemories must be bool."
Require ($atomKindByPath['SavedActiveLogicalRequestV1.sessionId'] -ceq 'int64') "sessionId must be int64."
Require ($atomKindByPath['SavedFrozenPromptVariantV1.variantOrdinal'] -ceq 'int32') "variantOrdinal must be int32."
Require ($atomKindByPath['PawnReflectionStateMemoryFields.lastQuietMemoryEvaluatedAbsoluteDay'] -ceq 'int32') "Reflection quiet-day field must be int32."

$pending = @($fixture.loadedPendingFixtures)
Require ($pending.Count -eq 5) "Every later loaded-only evidence family must have one exact pending fixture."
Require ((@($fixture.threadTargets) -join '/') -ceq '4/12/64') "Authenticated thread targets drifted."
Require ((@($fixture.textModes) -join '/') -ceq 'asciiByteBoundary/utf8WorstPerUtf16Unit/xmlEscapeWorstPerUtf16Unit') "Required text modes drifted."
$baselines = @($fixture.currentBaselineMetrics)
Require ($baselines.Count -eq 7) "M0 must name all seven current baseline metric families."
Require (($baselines | ForEach-Object { [string]$_.metricId } | Sort-Object -Unique -CaseSensitive).Count -eq 7) "Baseline metric IDs are duplicated."
$scenarios = @($fixture.syntheticScenarios)
Require ($scenarios.Count -eq 7) "M0 must freeze all seven synthetic worst-case scenario families."
Require (($scenarios | ForEach-Object { [string]$_.scenarioId } | Sort-Object -Unique -CaseSensitive).Count -eq 7) "Synthetic scenario IDs are duplicated."
Require ($fixture.settings.migration.missingOrVersionZero -ceq 'release_defaults_v1') "Missing settings must migrate to release defaults."
Require ($fixture.settings.migration.unknownFutureVersion -ceq 'inert_preserve_raw') "Future settings migration must fail closed."

Write-Host "Memory contract verification passed: $($defs.Count) capture rules, $($dimensions.Count) capacity dimensions, $($payloadTypes.Count) payload types, $($scenarios.Count) synthetic scenarios, $($pending.Count) named loaded fixture families."
