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
$russianTuningInjectedPath = Join-Path $repoRoot "Languages\Russian (Русский)\DefInjected\PawnDiary.DiaryKnowledgeTuningDef\DiaryKnowledgeTuningDef.xml"
$optionalMemoryAdapterPath = Join-Path $repoRoot "Source\Core\DiaryGameComponent.MemorySummaryWording.cs"
$catalogRoot = Join-Path $repoRoot "benchmarks\MemoryThreadBenchmarks\Catalog"
$capacityPath = Join-Path $catalogRoot "memory-capacity-catalog-v1.json"
$fixturePath = Join-Path $catalogRoot "memory-m0-fixture-catalog-v1.json"
$payloadPath = Join-Path $catalogRoot "memory-payload-atom-catalog-v1.json"
$reachabilityPath = Join-Path $catalogRoot "memory-consumer-reachability-v1.json"
$resultRoot = Join-Path $repoRoot "benchmarks\results\memory-system"

# Loading through XDocument catches malformed XML while preserving case-sensitive element names.
$important = [System.Xml.Linq.XDocument]::Load($importantPath)
$tuning = [System.Xml.Linq.XDocument]::Load($tuningPath)
$english = [System.Xml.Linq.XDocument]::Load($englishPath)
$russianTuningInjected = [System.Xml.Linq.XDocument]::Load($russianTuningInjectedPath)
$optionalMemoryAdapter = Get-Content -Raw -LiteralPath $optionalMemoryAdapterPath
$russianInjected = [System.Xml.Linq.XDocument]::Load($russianInjectedPath)
$capacity = Get-Content -Raw -LiteralPath $capacityPath | ConvertFrom-Json
$fixture = Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json
$payload = Get-Content -Raw -LiteralPath $payloadPath | ConvertFrom-Json
$reachability = Get-Content -Raw -LiteralPath $reachabilityPath | ConvertFrom-Json

Require ($capacity.schema -ceq "memory-capacity-catalog-v1") "Wrong memory capacity catalog schema."
Require ($fixture.schema -ceq "memory-m0-fixture-catalog-v1") "Wrong M0 fixture catalog schema."
Require ($payload.schema -ceq "memory-benchmark-payload-atom-catalog-v1") "Wrong payload atom catalog schema."
Require ($reachability.schema -ceq "memory-consumer-reachability-v1") "Wrong consumer reachability schema."
Require ($fixture.activationBuildState -ceq "CurrentRelease") "M11 must activate CurrentRelease."
Require ([string]$capacity.m0SelectedVectorId -cmatch '^[0-9a-f]{64}$') "The recorded M0-selected vector ID is invalid."
Require (@($fixture.pureGateIds) -ccontains 'SURROGATE-DTO-LIST-CULTURE-STRINGS') "The two-culture-label Library surrogate gate is not registered."

$knownKinds = @("event", "landmark", "summary")
$knownCategories = @("personal", "relationships", "family", "factions")
$knownImportance = @("low", "medium", "high")
$knownSubjects = @("pawn", "faction", "stream")
$knownStreamSubjects = @(
    "body_history", "colony_membership", "growth", "belief", "ideology_role",
    "royal_title", "psylink", "genetic_identity", "mechlink", "persona_bond"
)
$knownConsumers = @(
    "ordinary_diary", "existing_reflection", "narrative_arc", "comparison",
    "anniversary", "quiet_memory", "summary_wording"
)
$reachabilityRows = @($reachability.entries)
Require ($reachabilityRows.Count -eq $knownConsumers.Count) "Consumer reachability report row count drifted."
Require ((@($reachabilityRows | ForEach-Object { [string]$_.consumerId }) -join '/') -ceq ($knownConsumers -join '/')) "Consumer reachability report order/identity drifted."
foreach ($row in $reachabilityRows) {
    Require (@('routed','gap') -ccontains [string]$row.status) "Consumer '$($row.consumerId)' has an invalid reachability status."
    if ([string]$row.status -ceq 'routed') {
        Require ([string]::IsNullOrEmpty([string]$row.gapReason)) "Routed consumer '$($row.consumerId)' carries a gap reason."
        Require (-not [string]::IsNullOrWhiteSpace([string]$row.sourcePath)) "Routed consumer '$($row.consumerId)' has no source path."
        Require (-not [string]::IsNullOrWhiteSpace([string]$row.sourceNeedle)) "Routed consumer '$($row.consumerId)' has no source evidence."
        $source = Get-Content -Raw -LiteralPath (Join-Path $repoRoot ([string]$row.sourcePath))
        Require ($source.Contains([string]$row.sourceNeedle)) "Routed consumer '$($row.consumerId)' source evidence is stale."
    }
    else {
        Require (-not [string]::IsNullOrWhiteSpace([string]$row.gapReason)) "Unrouted consumer '$($row.consumerId)' has no stable gap reason."
        Require ([string]::IsNullOrEmpty([string]$row.sourcePath) -and [string]::IsNullOrEmpty([string]$row.sourceNeedle)) "Unrouted consumer '$($row.consumerId)' claims production evidence."
    }
}
$aggregationValues = @{
    count_occurrences = "empty"
    ordinal_set = "ordinal"
    int64_range = "int64"
    latest_state = "state"
}
$knownChapterDirectives = @(
    "continue_current", "close_after_current_event", "close_and_start_with_current_event",
    "start_new_after_closed_root", "remain_standalone"
)
$knownClosureReasons = @("formal_end", "reversal", "lifecycle", "inactivity", "repair")

$defs = @($important.Root.Elements("PawnDiary.DiaryImportantEventDef"))
Require ($defs.Count -eq 35) "Expected 35 shipped important-event Defs, found $($defs.Count)."
$defNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$eventFacts = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$factCategoryOwner = @{}
$seenCategories = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$standaloneCount = 0
$seenStreamSubjects = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($def in $defs) {
    $defName = Child-Text $def "defName"
    Require (-not [string]::IsNullOrWhiteSpace($defName)) "A capture Def has no defName."
    Require ($defNames.Add($defName)) "Duplicate capture Def: $defName"
    Require ($knownKinds -ccontains (Child-Text $def "memoryKind")) "$defName has invalid memoryKind."
    Require ($knownCategories -ccontains (Child-Text $def "memoryCategory")) "$defName has invalid memoryCategory."
    [void]$seenCategories.Add((Child-Text $def "memoryCategory"))
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
        $category = Child-Text $def 'memoryCategory'
        if ($factCategoryOwner.ContainsKey($factKind)) {
            Require ([string]$factCategoryOwner[$factKind] -ceq $category) "$factKind has more than one category owner."
        }
        else { $factCategoryOwner[$factKind] = $category }
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
        $directive = Child-Text $route "chapterDirective"
        if ([string]::IsNullOrWhiteSpace($directive)) { $directive = "continue_current" }
        $closureReason = Child-Text $route "chapterClosureReasonToken"
        Require ($knownChapterDirectives -ccontains $directive) "$defName has an invalid chapter directive."
        $closes = $directive -ceq "close_after_current_event" -or $directive -ceq "close_and_start_with_current_event"
        if ($closes) {
            Require ($knownClosureReasons -ccontains $closureReason) "$defName closing directive lacks a valid reason."
        }
        else { Require ([string]::IsNullOrEmpty($closureReason)) "$defName has a closure reason without a closing directive." }
        $extractors = $route.Element([System.Xml.Linq.XName]"equivalentExtractors")
        Require ($null -ne $extractors -and @($extractors.Elements("li")).Count -gt 0) "$defName has no exact route extractor."
        $extractorValues = @($extractors.Elements("li") | ForEach-Object { $_.Value.Trim() })
        foreach ($extractor in $extractors.Elements("li")) {
            Require ($extractor.Value -ceq $extractor.Value.Trim()) "$defName has a noncanonical route extractor."
        }
        Require (($extractorValues | Sort-Object -Unique -CaseSensitive).Count -eq $extractorValues.Count) "$defName repeats a route extractor."
        if ((Child-Text $route "subjectKind") -ceq "stream") {
            foreach ($extractor in $extractorValues) {
                Require ($extractor.StartsWith("constant:", [System.StringComparison]::Ordinal)) "$defName stream route is not an exact constant."
                $streamToken = $extractor.Substring("constant:".Length)
                Require ($knownStreamSubjects -ccontains $streamToken) "$defName names unknown stream '$streamToken'."
                [void]$seenStreamSubjects.Add($streamToken)
            }
        }
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

    $ownedRelations = $def.Element([System.Xml.Linq.XName]"authoritativeRelationDefNames")
    if ($null -ne $ownedRelations) {
        Require ((Child-Text $def "authoritativePageOwned") -ceq "true") "$defName owns relation Defs without an authoritative page."
        Require ((Child-Text $def "memoryCategory") -ceq "relationships") "$defName owns relation Defs outside Relationships."
        foreach ($relation in $ownedRelations.Elements("li")) {
            Require (-not [string]::IsNullOrWhiteSpace($relation.Value) -and $relation.Value -ceq $relation.Value.Trim()) "$defName has an invalid authoritative relation Def name."
        }
    }

    # Existing Def prose uses source-English plus Russian DefInjected parity. New M0 fields are tokens,
    # not prose, so no second localization mechanism is introduced.
    Require ($null -ne $russianInjected.Root.Element([System.Xml.Linq.XName]"$defName.label")) "Missing Russian label for $defName."
    Require ($null -ne $russianInjected.Root.Element([System.Xml.Linq.XName]"$defName.lineTemplate")) "Missing Russian lineTemplate for $defName."
}
Require ($standaloneCount -eq 2) "Expected exactly two intentionally Standalone shipped capture rules."
Require ($seenCategories.Count -eq 4) "The shipped capture catalog must own exactly four categories."
Require ((@($seenStreamSubjects | Sort-Object) -join '/') -ceq (@($knownStreamSubjects | Sort-Object) -join '/')) "The shipped stream-token set does not match the closed M0 allowlist."

$dimensions = @($capacity.dimensions)
Require ($dimensions.Count -eq 64) "Capacity catalog must contain exactly 64 dimensions."
$dimensionGateId = [string]$capacity.dimensionGateId
Require (-not [string]::IsNullOrWhiteSpace($dimensionGateId)) "Capacity dimensions have no executable gate mapping."
Require (@($fixture.pureGateIds) -ccontains $dimensionGateId) "Capacity dimension gate '$dimensionGateId' is not registered."
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
    $actualValue = Child-Text $xmlVector[$index] "valueEncoding"
    Require ($actualName -ceq $expectedName) "XML capacity order mismatch at ${index}: $actualName vs $expectedName."
    Require (@($dimensions[$index].values) -ccontains $actualValue) "XML capacity value '$actualValue' is not swept for $actualName."
}

# The current generator is not the historical M0 decision: changing feasibility/timing code must not
# silently move the ceiling. Recover the tracked decision's exact canonical vector, verify its hash,
# then enforce componentwise-lower XML coordinates against that frozen evidence.
$selectedEncodings = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($decision in @(Get-ChildItem -LiteralPath $resultRoot -Filter '*-decision.md' -File)) {
    $text = (Get-Content -Raw -LiteralPath $decision.FullName).Replace("`r`n", "`n").Replace("`r", "`n")
    if ($text -cnotmatch ('- Selected vector: `' + [Regex]::Escape([string]$capacity.m0SelectedVectorId) + '`')) { continue }
    $match = [Regex]::Match($text, '(?ms)## Selected vector encoding\s+```text\n(?<encoding>memory-system-vector-v1\n.*?\n)```')
    Require ($match.Success) "$($decision.Name) has no canonical selected-vector encoding."
    [void]$selectedEncodings.Add($match.Groups['encoding'].Value)
}
Require ($selectedEncodings.Count -eq 1) "Expected one byte-identical tracked encoding for the recorded M0-selected vector."
$m0Encoding = @($selectedEncodings)[0]
$m0Bytes = [Text.UTF8Encoding]::new($false).GetBytes($m0Encoding)
$m0Sha = [Security.Cryptography.SHA256]::Create()
try {
    $m0Id = ([BitConverter]::ToString($m0Sha.ComputeHash($m0Bytes))).Replace('-', '').ToLowerInvariant()
}
finally { $m0Sha.Dispose() }
Require ($m0Id -ceq [string]$capacity.m0SelectedVectorId) "The tracked M0-selected vector encoding does not hash to its catalog ID."
$m0Values = @{}
foreach ($line in @($m0Encoding -split "`n" | Select-Object -Skip 1)) {
    if ([string]::IsNullOrEmpty($line)) { continue }
    $equals = $line.IndexOf('=')
    Require ($equals -gt 0) "The tracked M0 vector contains a malformed row."
    $name = $line.Substring(0, $equals)
    Require (-not $m0Values.ContainsKey($name)) "The tracked M0 vector repeats '$name'."
    $m0Values[$name] = $line.Substring($equals + 1)
}
Require ($m0Values.Count -eq $xmlVector.Count) "The tracked M0 vector dimension count drifted."
for ($index = 0; $index -lt $xmlVector.Count; $index++) {
    $name = Child-Text $xmlVector[$index] 'name'
    $releaseParts = @((Child-Text $xmlVector[$index] 'valueEncoding') -split '/')
    $m0Parts = @(([string]$m0Values[$name]) -split '/')
    Require ($releaseParts.Count -eq $m0Parts.Count) "The release/M0 tuple arity differs for '$name'."
    for ($part = 0; $part -lt $releaseParts.Count; $part++) {
        Require ([uint64]$releaseParts[$part] -le [uint64]$m0Parts[$part]) "Release capacity '$name' part $part exceeds the recorded M0-selected vector."
    }
}

Require ((Child-Text $tuningDef "minorMemoryLifetimeDefaultDays") -ceq "15") "Minor lifetime default drifted."
Require ((Child-Text $tuningDef "regularMemoryLifetimeDefaultDays") -ceq "60") "Regular lifetime default drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetMinimum") -ceq "4") "Thread target minimum drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetDefault") -ceq "12") "Thread target default drifted."
Require ((Child-Text $tuningDef "memoryThreadTargetMaximum") -ceq "64") "Thread target maximum drifted."
Require ((Child-Text $tuningDef "quietReflectionChanceBasisPoints") -ceq "200") "Quiet chance drifted."
Require ((Child-Text $tuningDef "meaningfulMemoryDelayTicks") -ceq "60000") "Meaningful-memory delay drifted."
Require ((Child-Text $tuningDef "optionalMemoryOpportunityExpiryTicks") -ceq "120000") "Optional-memory expiry drifted."
Require ((Child-Text $tuningDef "meaningfulMemoryPriority") -ceq "100") "Meaningful-memory priority drifted."
Require ((Child-Text $tuningDef "quietMemoryPriority") -ceq "50") "Quiet-memory priority drifted."
Require ((Child-Text $tuningDef "memoryWordingPriority") -ceq "10") "Memory-wording priority drifted."
Require ((Child-Text $tuningDef "summaryWordingPriority") -ceq "0") "Summary-wording priority drifted."
Require ((Child-Text $tuningDef "memoryReflectionMaxTokens") -ceq "220") "Memory-reflection token cap drifted."
Require ((Child-Text $tuningDef "summaryWordingMaxTokens") -ceq "80") "Summary-wording token cap drifted."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "memoryReflectionSystemPrompt"))) "Memory-reflection DefInjected system prompt is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "memoryReflectionLabel"))) "Memory-reflection DefInjected label is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "memoryReflectionInstruction"))) "Memory-reflection DefInjected instruction is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "summaryWordingSystemPrompt"))) "Summary-wording DefInjected system prompt is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "summaryWordingInstruction"))) "Summary-wording DefInjected instruction is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "memoryWordingSystemPrompt"))) "Memory-wording DefInjected system prompt is missing."
Require (-not [string]::IsNullOrWhiteSpace((Child-Text $tuningDef "memoryWordingInstruction"))) "Memory-wording DefInjected instruction is missing."
$memoryWordingSystemPrompt = Child-Text $tuningDef "memoryWordingSystemPrompt"
Require ($memoryWordingSystemPrompt -cmatch 'do not add an emotion, evaluation') "Memory-wording truth guard no longer forbids invented feelings/evaluations."
Require ($memoryWordingSystemPrompt -cmatch 'unless it is explicit in memory_fact') "Memory-wording truth guard no longer binds additions to the canonical fact."
Require ($memoryWordingSystemPrompt -cmatch 'previous_wording' -and $memoryWordingSystemPrompt -cmatch 'memory_fact') "Memory-wording anti-repeat schema tokens drifted."
$summaryWordingSystemPrompt = Child-Text $tuningDef "summaryWordingSystemPrompt"
Require ($summaryWordingSystemPrompt -cmatch 'previous_wording' -and $summaryWordingSystemPrompt -cmatch 'deterministic_summary') "Summary-wording anti-repeat schema tokens drifted."
$optionalAiDefInjectedFields = @(
    'memoryReflectionSystemPrompt',
    'memoryReflectionLabel',
    'memoryReflectionInstruction',
    'summaryWordingSystemPrompt',
    'summaryWordingInstruction',
    'memoryWordingSystemPrompt',
    'memoryWordingInstruction'
)
foreach ($field in $optionalAiDefInjectedFields) {
    $key = "Diary_Knowledge.$field"
    $translated = $russianTuningInjected.Root.Element([System.Xml.Linq.XName]$key)
    Require ($null -ne $translated -and -not [string]::IsNullOrWhiteSpace($translated.Value)) "Missing Russian optional-AI DefInjected value for $field."
}
$russianMemoryWordingSystemPrompt = $russianTuningInjected.Root.Element(
    [System.Xml.Linq.XName]'Diary_Knowledge.memoryWordingSystemPrompt').Value
Require ($russianMemoryWordingSystemPrompt -cmatch 'не добавляйте эмоцию, оценку') "Russian memory-wording truth guard no longer forbids invented feelings/evaluations."
Require ($russianMemoryWordingSystemPrompt -cmatch 'previous_wording' -and $russianMemoryWordingSystemPrompt -cmatch 'memory_fact') "Russian memory-wording anti-repeat schema tokens drifted."
$russianSummaryWordingSystemPrompt = $russianTuningInjected.Root.Element(
    [System.Xml.Linq.XName]'Diary_Knowledge.summaryWordingSystemPrompt').Value
Require ($russianSummaryWordingSystemPrompt -cmatch 'previous_wording' -and $russianSummaryWordingSystemPrompt -cmatch 'deterministic_summary') "Russian summary-wording anti-repeat schema tokens drifted."
Require ($optionalMemoryAdapter -cmatch '"memory_wording:v2"' -and $optionalMemoryAdapter -cmatch '"optional-memory-wording:v2"') "Event/Landmark wording prompt identities did not advance to v2."
Require ($optionalMemoryAdapter -cmatch 'purpose \+ ":v2"' -and $optionalMemoryAdapter -cmatch '"optional-memory:v2"') "Summary wording prompt identities did not advance to v2."
Require ($optionalMemoryAdapter -cnotmatch 'transport_variant=') "Optional-memory transport metadata leaked into provider prompt text."

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
Require ($expectedAtomPaths.Count -eq 406) "Payload schema must declare the exact 406 current field paths."
Require ($atomRows.Count -eq 406) "Payload atom catalog must contain the exact 406 current atoms."
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
Require ($atomKindByPath['SavedMemoryBlock.optionalLlmWordingRevision'] -ceq 'int64') "Optional block wording revision must be int64."
Require ($atomKindByPath['SavedSummaryWordingOpportunityV1.expectedOptionalLlmWordingRevision'] -ceq 'int64') "Expected optional wording revision must be int64."
foreach ($path in @(
    'SavedMemoryAttemptAuditRow.attemptOrdinal',
    'DiaryGameComponentMemory.memoryComponentSchemaVersion',
    'DiaryGameComponentMemory.memoryCoordinatorSchemaVersion',
    'DiaryGameComponentMemory.memoryDispatchSchemaVersion'
)) {
    Require ($atomKindByPath[$path] -ceq 'int32') "$path must be int32."
}
foreach ($path in @(
    'SavedMemoryBlock.primarySubject',
    'SavedImportedMemoryRow.primarySubject',
    'SavedFrozenPromptVariantV1.receiptPlan',
    'SavedLegacyUnresolvedOwnerArchiveInputV1.legacyRecord'
)) {
    Require ($atomKindByPath[$path] -ceq 'nullable_row') "$path must include nullable-row presence bytes."
}
$atomByPath = @{}
foreach ($atom in $atomRows) { $atomByPath[[string]$atom.canonicalFieldPath] = $atom }
foreach ($path in @(
    'SavedMemoryBlock.optionalLlmFingerprint',
    'SavedMemorySummaryPayload.lastSettledWordingFingerprint',
    'SavedMemorySummaryPayload.lastWordingDispositionToken',
    'SavedFrozenPromptVariantV1.contextDetailIdentity'
)) {
    Require (-not [bool]$atomByPath[$path].freeTextModeEligible) "$path is a stable token/identity, not free text."
}
Require ([bool]$atomByPath['SavedMemoryBlock.optionalLlmWording'].freeTextModeEligible) "SavedMemoryBlock.optionalLlmWording must participate in free-text sizing."

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

$routedConsumerCount = @($reachabilityRows | Where-Object { [string]$_.status -ceq 'routed' }).Count
$gapConsumerCount = $reachabilityRows.Count - $routedConsumerCount
Write-Host "Memory contract verification passed: $($defs.Count) capture rules, $($dimensions.Count) capacity dimensions, $($payloadTypes.Count) payload types, $($scenarios.Count) synthetic scenarios, $($pending.Count) named loaded fixture families, consumer reachability $routedConsumerCount routed/$gapConsumerCount reported gaps."
