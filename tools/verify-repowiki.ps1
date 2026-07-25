param(
    [string]$WikiRoot = ''
)

# Manual source-of-truth check for the compact Pawn Diary wiki.
#
# Reference pages carry one machine-readable HTML comment immediately before each detail
# section:
#
#   <!-- repowiki:<inventory-kind> {"id":"source-backed-value",...} -->
#
# The prose remains hand-written. This script only compares those compact contracts with current
# C#/XML, checks navigation, and reports drift; it never generates or rewrites documentation.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$primaryWikiRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'repowiki'))
if ([string]::IsNullOrWhiteSpace($WikiRoot)) {
    $WikiRoot = Join-Path $repoRoot 'repowiki'
}
elseif (-not [System.IO.Path]::IsPathRooted($WikiRoot)) {
    $WikiRoot = Join-Path $repoRoot $WikiRoot
}
$WikiRoot = [System.IO.Path]::GetFullPath($WikiRoot)

$failures = New-Object 'System.Collections.Generic.List[string]'
$metadataPattern = '^<!-- repowiki:(?<kind>[a-z-]+) (?<json>\{.*\}) -->$'

function Add-Failure([string]$message) {
    [void]$failures.Add($message)
}

function Repo-Path([string]$relativePath) {
    return Join-Path $repoRoot ($relativePath -replace '/', '\')
}

function Wiki-Path([string]$relativePath) {
    return Join-Path $WikiRoot ($relativePath -replace '/', '\')
}

function Read-DefXml([string]$relativePath) {
    return [xml](Get-Content -Raw -LiteralPath (Repo-Path $relativePath))
}

function Def-Nodes([xml]$document, [string]$typeName = '') {
    if ($null -eq $document.Defs) {
        return @()
    }
    return @($document.Defs.ChildNodes | Where-Object {
        $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and
            ([string]::IsNullOrEmpty($typeName) -or $_.LocalName -eq $typeName)
    })
}

function Parent-Map([System.Xml.XmlNode[]]$nodes) {
    $map = @{}
    foreach ($node in @($nodes)) {
        $name = $node.GetAttribute('Name')
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $map[$name] = $node
        }
    }
    return $map
}

function Direct-Node([System.Xml.XmlNode]$node, [string]$path) {
    if ($null -eq $node) {
        return $null
    }
    return $node.SelectSingleNode($path)
}

function Effective-Node(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [hashtable]$parents
) {
    $direct = Direct-Node $node $path
    if ($null -ne $direct) {
        return $direct
    }

    $parentName = $node.GetAttribute('ParentName')
    if (-not [string]::IsNullOrWhiteSpace($parentName) -and
        $parents.ContainsKey($parentName)) {
        return Effective-Node $parents[$parentName] $path $parents
    }
    return $null
}

function Effective-Text(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [string]$defaultValue,
    [hashtable]$parents
) {
    $valueNode = Effective-Node $node $path $parents
    if ($null -eq $valueNode) {
        return $defaultValue
    }
    return $valueNode.InnerText.Trim()
}

function Direct-Text(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [string]$defaultValue = ''
) {
    $valueNode = Direct-Node $node $path
    if ($null -eq $valueNode) {
        return $defaultValue
    }
    return $valueNode.InnerText.Trim()
}

function Effective-List(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [hashtable]$parents
) {
    $container = Direct-Node $node $path
    if ($null -ne $container) {
        return @($container.SelectNodes('li') | ForEach-Object { $_.InnerText.Trim() })
    }

    $parentName = $node.GetAttribute('ParentName')
    if (-not [string]::IsNullOrWhiteSpace($parentName) -and
        $parents.ContainsKey($parentName)) {
        return @(Effective-List $parents[$parentName] $path $parents)
    }
    return @()
}

function Effective-Bool(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [bool]$defaultValue,
    [hashtable]$parents
) {
    $fallback = ([string]$defaultValue).ToLowerInvariant()
    $text = Effective-Text $node $path $fallback $parents
    return [string]::Equals($text, 'true', [System.StringComparison]::OrdinalIgnoreCase)
}

function Effective-Int(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [int]$defaultValue,
    [hashtable]$parents
) {
    $text = Effective-Text $node $path ([string]$defaultValue) $parents
    $result = $defaultValue
    [void][int]::TryParse(
        $text,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$result)
    return $result
}

function Effective-Float(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [double]$defaultValue,
    [hashtable]$parents
) {
    $fallback = $defaultValue.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $text = Effective-Text $node $path $fallback $parents
    $result = $defaultValue
    [void][double]::TryParse(
        $text,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$result)
    return $result
}

function One-Line([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ''
    }
    return [regex]::Replace($text.Trim(), '\s+', ' ')
}

function Sha256([string]$value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($value)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Group-Metadata(
    [System.Xml.XmlNode]$node,
    [hashtable]$parents,
    [string]$sourceFile = ''
) {
    $batchNode = Effective-Node $node 'batch' $parents
    $batchEnabled = $false
    $batchMode = 'none'
    $batchScope = ''
    $batchSyntheticDefName = ''
    $batchWindowTicks = 0
    $batchMaxEvents = 0
    if ($null -ne $batchNode) {
        $batchEnabled = Effective-Bool $node 'batch/enabled' $true $parents
        if ($batchEnabled) {
            $batchMode = Effective-Text $node 'batch/mode' 'PairEvent' $parents
            $batchScope = Effective-Text $node 'batch/scope' 'Pair' $parents
            $batchSyntheticDefName =
                Effective-Text $node 'batch/syntheticDefName' '' $parents
            $batchWindowTicks = Effective-Int $node 'batch/windowTicks' 0 $parents
            $batchMaxEvents = Effective-Int $node 'batch/maxEvents' 1 $parents
        }
    }

    $metadata = [ordered]@{
        defName = Effective-Text $node 'defName' '' $parents
        domain = Effective-Text $node 'domain' 'Interaction' $parents
        defaultEnabled = Effective-Bool $node 'defaultEnabled' $true $parents
        important = Effective-Bool $node 'important' $true $parents
        combat = Effective-Bool $node 'combat' $false $parents
        batchEnabled = $batchEnabled
        batchMode = $batchMode
        batchScope = $batchScope
        batchSyntheticDefName = $batchSyntheticDefName
        batchWindowTicks = $batchWindowTicks
        batchMaxEvents = $batchMaxEvents
        catchAll = Effective-Bool $node 'catchAll' $false $parents
        matchDefNames = @(Effective-List $node 'matchDefNames' $parents)
        matchOrdinalDefNames = @(Effective-List $node 'matchOrdinalDefNames' $parents)
        matchPrefixes = @(Effective-List $node 'matchPrefixes' $parents)
        matchSuffixes = @(Effective-List $node 'matchSuffixes' $parents)
        matchSegments = @(Effective-List $node 'matchSegments' $parents)
        matchTokens = @(Effective-List $node 'matchTokens' $parents)
        matchPackageIds = @(Effective-List $node 'matchPackageIds' $parents)
        enableWhenPackageIdsLoaded =
            @(Effective-List $node 'enableWhenPackageIdsLoaded' $parents)
        disableWhenPackageIdsLoaded =
            @(Effective-List $node 'disableWhenPackageIdsLoaded' $parents)
        disableWhenCaptureCapabilitiesReady =
            @(Effective-List $node 'disableWhenCaptureCapabilitiesReady' $parents)
    }
    if (-not [string]::IsNullOrWhiteSpace($sourceFile)) {
        $metadata.sourceFile = $sourceFile
    }
    return [pscustomobject]$metadata
}

function Condition-Metadata(
    [System.Xml.XmlNode]$node,
    [hashtable]$parents,
    [string]$sourceFile = ''
) {
    $metadata = [ordered]@{
        defName = Effective-Text $node 'defName' '' $parents
        label = Effective-Text $node 'label' '' $parents
        conditionKey = Effective-Text $node 'conditionKey' (
            Effective-Text $node 'defName' '' $parents) $parents
        enabled = Effective-Bool $node 'enabled' $true $parents
        scope = Effective-Text $node 'scope' 'Map' $parents
        observerType = Effective-Text $node 'observerType' 'MapDanger' $parents
        pollIntervalTicks = Effective-Int $node 'pollIntervalTicks' 1000 $parents
        startDebounceTicks = Effective-Int $node 'startDebounceTicks' 0 $parents
        endDebounceTicks = Effective-Int $node 'endDebounceTicks' 2500 $parents
        dedupTicks = Effective-Int $node 'dedupTicks' 2500 $parents
        recordStartEvent = Effective-Bool $node 'recordStartEvent' $false $parents
        recordEndEvent = Effective-Bool $node 'recordEndEvent' $false $parents
        recordScope = Effective-Text $node 'recordScope' 'MapColonists' $parents
        promptEnabled = Effective-Bool $node 'promptEnabled' $true $parents
        matchDefNames = @(Effective-List $node 'matchDefNames' $parents)
        suppressWhenThingDefNames =
            @(Effective-List $node 'suppressWhenThingDefNames' $parents)
        minPollutionFraction = Effective-Float $node 'minPollutionFraction' 0 $parents
        maxPollutionFraction = Effective-Float $node 'maxPollutionFraction' -1 $parents
        maxActiveTicks = Effective-Int $node 'maxActiveTicks' 0 $parents
        restartCooldownTicks = Effective-Int $node 'restartCooldownTicks' 0 $parents
        maxPagePawns = Effective-Int $node 'maxPagePawns' 0 $parents
        mayRequire = $node.GetAttribute('MayRequire')
    }
    if (-not [string]::IsNullOrWhiteSpace($sourceFile)) {
        $metadata.sourceFile = $sourceFile
    }
    return [pscustomobject]$metadata
}

function Window-Triggers(
    [System.Xml.XmlNode]$node,
    [string]$path,
    [hashtable]$parents
) {
    $container = Effective-Node $node $path $parents
    $result = @()
    if ($null -eq $container) {
        return $result
    }
    foreach ($trigger in @($container.SelectNodes('li'))) {
        $result += [pscustomobject][ordered]@{
            source = Direct-Text $trigger 'source' ''
            signal = Direct-Text $trigger 'signal' ''
            matchDefNames =
                @($trigger.SelectNodes('matchDefNames/li') | ForEach-Object {
                    $_.InnerText.Trim()
                })
            matchTokens =
                @($trigger.SelectNodes('matchTokens/li') | ForEach-Object {
                    $_.InnerText.Trim()
                })
        }
    }
    return $result
}

function Window-Metadata(
    [System.Xml.XmlNode]$node,
    [hashtable]$parents,
    [string]$sourceFile = ''
) {
    $metadata = [ordered]@{
        defName = Effective-Text $node 'defName' '' $parents
        label = Effective-Text $node 'label' '' $parents
        windowKey = Effective-Text $node 'windowKey' (
            Effective-Text $node 'defName' '' $parents) $parents
        enabled = Effective-Bool $node 'enabled' $true $parents
        enableWhenPackageIdsLoaded =
            @(Effective-List $node 'enableWhenPackageIdsLoaded' $parents)
        timeoutTicks = Effective-Int $node 'timeoutTicks' -1 $parents
        dedupTicks = Effective-Int $node 'dedupTicks' 2500 $parents
        restartOnStart = Effective-Bool $node 'restartOnStart' $false $parents
        keepActive = Effective-Bool $node 'keepActive' $true $parents
        recordScope = Effective-Text $node 'recordScope' 'Map' $parents
        recordStartEvent = Effective-Bool $node 'recordStartEvent' $true $parents
        recordEndEvent = Effective-Bool $node 'recordEndEvent' $true $parents
        recordEndWithoutActive =
            Effective-Bool $node 'recordEndWithoutActive' $true $parents
        recordTimeoutEvent = Effective-Bool $node 'recordTimeoutEvent' $true $parents
        promptEnabled = Effective-Bool $node 'promptEnabled' $true $parents
        startSignals = @(Window-Triggers $node 'startSignals' $parents)
        endSignals = @(Window-Triggers $node 'endSignals' $parents)
        stillPresentThingDefNames =
            @(Effective-List $node 'stillPresentThingDefNames' $parents)
        stillPresentFactionDefNames =
            @(Effective-List $node 'stillPresentFactionDefNames' $parents)
    }
    if (-not [string]::IsNullOrWhiteSpace($sourceFile)) {
        $metadata.sourceFile = $sourceFile
    }
    return [pscustomobject]$metadata
}

function Template-Metadata([System.Xml.XmlNode]$node) {
    $fields = @()
    foreach ($field in @($node.SelectNodes('fields/li'))) {
        $fields += [pscustomobject][ordered]@{
            enabled = -not [string]::Equals(
                (Direct-Text $field 'enabled' 'true'),
                'false',
                [System.StringComparison]::OrdinalIgnoreCase)
            label = Direct-Text $field 'label' ''
            source = Direct-Text $field 'source' ''
            contextKey = Direct-Text $field 'contextKey' ''
        }
    }
    return [pscustomobject][ordered]@{
        defName = Direct-Text $node 'defName' ''
        templateKey = Direct-Text $node 'templateKey' ''
        includePersona = -not [string]::Equals(
            (Direct-Text $node 'includePersona' 'true'),
            'false',
            [System.StringComparison]::OrdinalIgnoreCase)
        includePromptEnchantment = -not [string]::Equals(
            (Direct-Text $node 'includePromptEnchantment' 'true'),
            'false',
            [System.StringComparison]::OrdinalIgnoreCase)
        appendDirectSpeechInstruction = -not [string]::Equals(
            (Direct-Text $node 'appendDirectSpeechInstruction' 'true'),
            'false',
            [System.StringComparison]::OrdinalIgnoreCase)
        maxTokens = [int](Direct-Text $node 'maxTokens' '0')
        systemPromptSource =
            if ([string]::IsNullOrWhiteSpace((Direct-Text $node 'systemPrompt' ''))) {
                'fallback'
            }
            else {
                'xml'
            }
        finalInstructionSource =
            if ([string]::IsNullOrWhiteSpace((Direct-Text $node 'finalInstruction' ''))) {
                'fallback'
            }
            else {
                'xml'
            }
        recipientFinalInstructionSource =
            if ([string]::IsNullOrWhiteSpace(
                (Direct-Text $node 'recipientFinalInstruction' ''))) {
                'fallback'
            }
            else {
                'xml'
            }
        fields = $fields
    }
}

function Prompt-Policy-Metadata([System.Xml.XmlNode]$node) {
    $prompt = One-Line (Direct-Text $node 'prompt' '')
    $enhancement = One-Line (Direct-Text $node 'enhancement' '')
    return [pscustomobject][ordered]@{
        defName = Direct-Text $node 'defName' ''
        eventType = Direct-Text $node 'eventType' ''
        enableWhenPackageIdsLoaded =
            @($node.SelectNodes('enableWhenPackageIdsLoaded/li') | ForEach-Object {
                $_.InnerText.Trim()
            })
        forcedModel = Direct-Text $node 'forcedModel' ''
        guidanceHash = Sha256 ($prompt + "`n" + $enhancement)
    }
}

function Enchantment-Metadata([System.Xml.XmlNode]$node) {
    $tiers = @()
    foreach ($tier in @($node.SelectNodes('hediffSeverityTiers/li'))) {
        $tiers += [pscustomobject][ordered]@{
            level = Direct-Text $tier 'level' ''
            chance = [double](Direct-Text $tier 'chance' '-1')
            frequency = [double](Direct-Text $tier 'frequency' '-1')
            weight = [double](Direct-Text $tier 'weight' '-1')
            severity = [double](Direct-Text $tier 'severity' '-1')
        }
    }
    $effectText = @(
        Direct-Text $node 'conditionKey' ''
        Direct-Text $node 'intensityKey' ''
        Direct-Text $node 'priorityKey' ''
        Direct-Text $node 'descriptionOverrideKey' ''
        (@($node.SelectNodes('cueKeys/li') | ForEach-Object {
            $_.InnerText.Trim()
        }) -join "`n")
    ) -join "`n"

    return [pscustomobject][ordered]@{
        defName = Direct-Text $node 'defName' ''
        label = Direct-Text $node 'label' ''
        source = Direct-Text $node 'source' 'Hediff'
        chance = [double](Direct-Text $node 'chance' '1')
        frequency = [double](Direct-Text $node 'frequency' '-1')
        weight = [double](Direct-Text $node 'weight' '1')
        severity = [double](Direct-Text $node 'severity' '1')
        visibleOnly = -not [string]::Equals(
            (Direct-Text $node 'visibleOnly' 'true'),
            'false',
            [System.StringComparison]::OrdinalIgnoreCase)
        minHediffSeverity = [double](Direct-Text $node 'minHediffSeverity' '0')
        hediffDefNames =
            @($node.SelectNodes('hediffDefNames/li') | ForEach-Object {
                $_.InnerText.Trim()
            })
        hediffSeverityTiers = $tiers
        capacityDefName = Direct-Text $node 'capacityDefName' ''
        minCapacity = [double](Direct-Text $node 'minCapacity' '-1')
        maxCapacity = [double](Direct-Text $node 'maxCapacity' '-1')
        effectHash = Sha256 $effectText
    }
}

function Read-Metadata([string]$relativePath, [string]$kind) {
    $path = Wiki-Path $relativePath
    $rows = @()
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure "Missing wiki page: $relativePath"
        return $rows
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        if ($line -notmatch $metadataPattern) {
            continue
        }
        if ($Matches.kind -ne $kind) {
            continue
        }
        try {
            $data = $Matches.json | ConvertFrom-Json
            $rows += [pscustomobject]@{
                Data = $data
                Json = $Matches.json
                Line = $lineNumber
            }
        }
        catch {
            Add-Failure "$relativePath`:$lineNumber has invalid $kind metadata JSON: $($_.Exception.Message)"
        }
    }
    return $rows
}

function Json-Text([object]$value) {
    if ($null -eq $value) {
        return 'null'
    }
    return ConvertTo-Json -InputObject $value -Compress -Depth 30
}

function Changed-Fields([object]$expected, [object]$documented) {
    $names = @(
        @($expected.PSObject.Properties.Name) +
        @($documented.PSObject.Properties.Name) |
            Sort-Object -Unique
    )
    $changed = @()
    foreach ($name in $names) {
        $expectedProperty = $expected.PSObject.Properties[$name]
        $documentedProperty = $documented.PSObject.Properties[$name]
        $expectedValue = if ($null -eq $expectedProperty) { $null } else {
            $expectedProperty.Value
        }
        $documentedValue = if ($null -eq $documentedProperty) { $null } else {
            $documentedProperty.Value
        }
        if ((Json-Text $expectedValue) -cne (Json-Text $documentedValue)) {
            $changed += $name
        }
    }
    return $changed
}

function Validate-Inventory(
    [string]$relativePath,
    [string]$kind,
    [object[]]$expected,
    [string]$keyProperty
) {
    $rows = @(Read-Metadata $relativePath $kind)
    $expectedByKey = @{}
    foreach ($item in @($expected)) {
        $key = [string]$item.$keyProperty
        if ([string]::IsNullOrWhiteSpace($key)) {
            Add-Failure "Source produced a blank key for $kind ($keyProperty)."
            continue
        }
        if ($expectedByKey.ContainsKey($key)) {
            Add-Failure "Source contains duplicate $kind key '$key'."
            continue
        }
        $expectedByKey[$key] = $item
    }

    $documentedByKey = @{}
    foreach ($row in $rows) {
        $key = [string]$row.Data.$keyProperty
        if ([string]::IsNullOrWhiteSpace($key)) {
            Add-Failure "$relativePath`:$($row.Line) has $kind metadata without '$keyProperty'."
            continue
        }
        if (-not $documentedByKey.ContainsKey($key)) {
            $documentedByKey[$key] = @()
        }
        $documentedByKey[$key] = @($documentedByKey[$key]) + @($row)
    }

    foreach ($key in @($expectedByKey.Keys | Sort-Object)) {
        if (-not $documentedByKey.ContainsKey($key)) {
            Add-Failure "$relativePath is missing $kind '$key'."
            continue
        }
        $matchingRows = @($documentedByKey[$key])
        if ($matchingRows.Count -ne 1) {
            Add-Failure "$relativePath documents $kind '$key' $($matchingRows.Count) times; expected exactly once."
            continue
        }
        $expectedJson = Json-Text $expectedByKey[$key]
        if ($expectedJson -cne $matchingRows[0].Json) {
            $fields = @(Changed-Fields $expectedByKey[$key] $matchingRows[0].Data)
            Add-Failure (
                "$relativePath`:$($matchingRows[0].Line) has stale $kind '$key' metadata" +
                " (changed fields: $($fields -join ', ')).")
        }
    }

    foreach ($key in @($documentedByKey.Keys | Sort-Object)) {
        if (-not $expectedByKey.ContainsKey($key)) {
            $lines = @($documentedByKey[$key] | ForEach-Object { $_.Line }) -join ', '
            Add-Failure "$relativePath has stale $kind '$key' at line(s) $lines."
        }
    }

    Write-Host (
        "  {0}: {1} source-backed {2} entr{3}" -f
        $relativePath,
        $expectedByKey.Count,
        $kind,
        $(if ($expectedByKey.Count -eq 1) { 'y' } else { 'ies' }))
}

function Runtime-Inventory() {
    $catalogPath = Repo-Path 'Source/Capture/Catalog/DiaryEventCatalog.cs'
    $catalog = Get-Content -Raw -LiteralPath $catalogPath
    $registeredClasses = @(
        [regex]::Matches(
            $catalog,
            'Register\s*\(\s*new\s+(?<class>[A-Za-z0-9_]+EventSpec)\s*\(\s*\)\s*\)') |
            ForEach-Object { $_.Groups['class'].Value }
    )

    $classToType = @{}
    foreach ($file in Get-ChildItem -LiteralPath (Repo-Path 'Source/Capture/Specs') -Filter '*.cs' -File) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        $matches = [regex]::Matches(
            $text,
            'class\s+(?<class>[A-Za-z0-9_]+EventSpec)\b[\s\S]*?' +
            'override\s+DiaryEventType\s+EventType\s*=>\s*DiaryEventType\.(?<type>[A-Za-z0-9_]+)')
        foreach ($match in $matches) {
            $classToType[$match.Groups['class'].Value] = $match.Groups['type'].Value
        }
    }

    $result = @()
    foreach ($className in $registeredClasses) {
        if (-not $classToType.ContainsKey($className)) {
            Add-Failure "Cannot resolve DiaryEventType for registered spec '$className'."
            continue
        }
        $result += [pscustomobject][ordered]@{ id = $classToType[$className] }
    }
    return $result
}

function Core-Group-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryInteractionGroupDefs.xml'
    $nodes = Def-Nodes $document 'PawnDiary.DiaryInteractionGroupDef'
    $parents = Parent-Map $nodes
    return @($nodes | Where-Object {
        -not [string]::Equals(
            $_.GetAttribute('Abstract'),
            'True',
            [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        Group-Metadata $_ $parents
    })
}

function Core-Condition-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryObservedConditionDefs.xml'
    $nodes = Def-Nodes $document 'PawnDiary.DiaryObservedConditionDef'
    $parents = Parent-Map $nodes
    return @($nodes | Where-Object {
        -not [string]::Equals(
            $_.GetAttribute('Abstract'),
            'True',
            [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        Condition-Metadata $_ $parents
    })
}

function Core-Condition-Base-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryObservedConditionDefs.xml'
    $nodes = Def-Nodes $document 'PawnDiary.DiaryObservedConditionDef'
    return @($nodes | Where-Object {
        [string]::Equals(
            $_.GetAttribute('Abstract'),
            'True',
            [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        [pscustomobject][ordered]@{
            name = $_.GetAttribute('Name')
            abstract = $true
        }
    })
}

function Core-Window-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryEventWindowDefs.xml'
    $nodes = Def-Nodes $document 'PawnDiary.DiaryEventWindowDef'
    $parents = Parent-Map $nodes
    return @($nodes | Where-Object {
        -not [string]::Equals(
            $_.GetAttribute('Abstract'),
            'True',
            [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        Window-Metadata $_ $parents
    })
}

function Template-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryPromptTemplateDefs.xml'
    return @(Def-Nodes $document 'PawnDiary.DiaryPromptTemplateDef' | ForEach-Object {
        Template-Metadata $_
    })
}

function Prompt-Policy-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryEventPromptDefs.xml'
    return @(Def-Nodes $document 'PawnDiary.DiaryEventPromptDef' | ForEach-Object {
        Prompt-Policy-Metadata $_
    })
}

function Enchantment-Inventory() {
    $document = Read-DefXml '1.6/Defs/DiaryPromptEnchantmentDefs.xml'
    return @(Def-Nodes $document 'PawnDiary.DiaryPromptEnchantmentDef' | ForEach-Object {
        Enchantment-Metadata $_
    })
}

function Adapter-Inventory() {
    $result = @()
    foreach ($directory in @(
        Get-ChildItem -LiteralPath (Repo-Path 'integrations') -Directory |
            Where-Object {
                Test-Path -LiteralPath (Join-Path $_.FullName 'About/About.xml')
            } |
            Sort-Object Name
    )) {
        $about = [xml](Get-Content -Raw -LiteralPath (
            Join-Path $directory.FullName 'About/About.xml'))
        $dependencies = @($about.ModMetaData.modDependencies.li | ForEach-Object {
            [string]$_.packageId
        } | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        })
        $result += [pscustomobject][ordered]@{
            directory = $directory.Name
            packageId = [string]$about.ModMetaData.packageId
            dependencies = $dependencies
        }
    }
    return $result
}

function Compatibility-Inventory() {
    $files = @(
        Get-ChildItem -LiteralPath (Repo-Path '1.6/Defs/Compat') -Filter '*.xml' -File
    ) + @(
        Get-ChildItem -LiteralPath (Repo-Path 'integrations') -Recurse -Filter '*.xml' -File |
            Where-Object { $_.FullName -match '\\1\.6\\Defs\\' }
    )

    $result = @()
    foreach ($file in @($files | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        try {
            $document = [xml](Get-Content -Raw -LiteralPath $file.FullName)
        }
        catch {
            Add-Failure "Cannot parse compatibility XML '$relative': $($_.Exception.Message)"
            continue
        }
        $nodes = Def-Nodes $document
        $parents = Parent-Map $nodes
        foreach ($node in $nodes) {
            if ([string]::Equals(
                $node.GetAttribute('Abstract'),
                'True',
                [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            switch ($node.LocalName) {
                'PawnDiary.DiaryInteractionGroupDef' {
                    $result += [pscustomobject]@{
                        Kind = 'group'
                        Data = Group-Metadata $node $parents $relative
                    }
                }
                'PawnDiary.DiaryEventWindowDef' {
                    $result += [pscustomobject]@{
                        Kind = 'window'
                        Data = Window-Metadata $node $parents $relative
                    }
                }
                'PawnDiary.DiaryObservedConditionDef' {
                    $result += [pscustomobject]@{
                        Kind = 'condition'
                        Data = Condition-Metadata $node $parents $relative
                    }
                }
            }
        }
    }
    return $result
}

function Markdown-Links([string]$content) {
    $result = @()
    $pattern = '(?<!!)\[[^\]\r\n]+\]\((?<inside><[^>\r\n]+>|[^)\r\n]+)\)'
    foreach ($match in [regex]::Matches($content, $pattern)) {
        $inside = $match.Groups['inside'].Value.Trim()
        if ($inside.StartsWith('<') -and $inside.EndsWith('>')) {
            $target = $inside.Substring(1, $inside.Length - 2)
        }
        elseif ($inside -match '^(?<target>\S+)(?:\s+["''][\s\S]*["''])?$') {
            $target = $Matches.target
        }
        else {
            continue
        }
        $line = 1 + ([regex]::Matches(
            $content.Substring(0, $match.Index),
            "`n")).Count
        $result += [pscustomobject]@{ Target = $target; Line = $line }
    }
    return $result
}

function Heading-Slug([string]$heading) {
    $text = [System.Net.WebUtility]::HtmlDecode($heading)
    $text = [regex]::Replace($text, '<[^>]+>', '')
    $text = [regex]::Replace($text, '!\[([^\]]*)\]\([^)]+\)', '$1')
    $text = [regex]::Replace($text, '\[([^\]]+)\]\([^)]+\)', '$1')
    $text = $text.Replace('`', '').ToLowerInvariant()
    $text = [regex]::Replace($text, '[^\p{L}\p{Nd}\p{M} _-]', '')
    $text = [regex]::Replace($text.Trim(), '\s', '-')
    return $text
}

$anchorCache = @{}
function File-Anchors([string]$path) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if ($anchorCache.ContainsKey($fullPath)) {
        return $anchorCache[$fullPath]
    }

    $anchors = @{}
    $slugCounts = @{}
    foreach ($line in Get-Content -LiteralPath $fullPath) {
        if ($line -match '^#{1,6}\s+(?<heading>.+?)\s*#*\s*$') {
            $slug = Heading-Slug $Matches.heading
            if ([string]::IsNullOrWhiteSpace($slug)) {
                continue
            }
            $anchor = $slug
            if ($slugCounts.ContainsKey($slug)) {
                $slugCounts[$slug] = [int]$slugCounts[$slug] + 1
                $anchor = "$slug-$($slugCounts[$slug])"
            }
            else {
                $slugCounts[$slug] = 0
            }
            $anchors[$anchor] = $true
        }
        foreach ($match in [regex]::Matches(
            $line,
            '<a\s+(?:id|name)=["''](?<anchor>[^"'']+)["'']')) {
            $anchors[$match.Groups['anchor'].Value.ToLowerInvariant()] = $true
        }
    }
    $anchorCache[$fullPath] = $anchors
    return $anchors
}

function Validate-Link(
    [string]$sourcePath,
    [string]$displaySource,
    [int]$line,
    [string]$rawTarget
) {
    if ([string]::IsNullOrWhiteSpace($rawTarget) -or
        $rawTarget -match '^[a-z][a-z0-9+.-]*:' -or
        $rawTarget.StartsWith('//')) {
        return
    }

    $target = [System.Net.WebUtility]::HtmlDecode($rawTarget)
    $fragment = ''
    $hashIndex = $target.IndexOf('#')
    if ($hashIndex -ge 0) {
        $fragment = $target.Substring($hashIndex + 1)
        $target = $target.Substring(0, $hashIndex)
    }
    $queryIndex = $target.IndexOf('?')
    if ($queryIndex -ge 0) {
        $target = $target.Substring(0, $queryIndex)
    }
    $target = [Uri]::UnescapeDataString($target)

    if ([string]::IsNullOrWhiteSpace($target)) {
        $resolved = $sourcePath
    }
    elseif ($target.StartsWith('/')) {
        $resolved = Join-Path $repoRoot $target.TrimStart('/')
    }
    else {
        $resolved = Join-Path (Split-Path -Parent $sourcePath) ($target -replace '/', '\')
    }
    $resolved = [System.IO.Path]::GetFullPath($resolved)

    if (-not (Test-Path -LiteralPath $resolved)) {
        Add-Failure "$displaySource`:$line has a broken link target '$rawTarget'."
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($fragment)) {
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf) -or
            [System.IO.Path]::GetExtension($resolved) -ne '.md') {
            Add-Failure "$displaySource`:$line links to anchor '#$fragment' on a non-Markdown target."
            return
        }
        $anchor = [Uri]::UnescapeDataString($fragment).ToLowerInvariant()
        $anchors = File-Anchors $resolved
        if (-not $anchors.ContainsKey($anchor)) {
            Add-Failure "$displaySource`:$line has an unresolved anchor '#$fragment' in '$rawTarget'."
        }
    }
}

function Validate-Navigation() {
    if (-not (Test-Path -LiteralPath $WikiRoot -PathType Container)) {
        Add-Failure "Wiki root does not exist: $WikiRoot"
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $WikiRoot -Recurse -Filter '*.md' -File) {
        $relative = $file.FullName.Substring($WikiRoot.Length).TrimStart('\').Replace('\', '/')
        $content = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($link in @(Markdown-Links $content)) {
            Validate-Link $file.FullName $relative $link.Line $link.Target
        }
    }

    $tracked = @(& git -c core.quotepath=false -C $repoRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        Add-Failure 'git ls-files failed; tracked incoming wiki links were not checked.'
        return
    }
    foreach ($relative in $tracked) {
        if ([System.IO.Path]::GetExtension($relative) -ne '.md') {
            continue
        }
        $path = Repo-Path $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        $content = Get-Content -Raw -LiteralPath $path
        foreach ($link in @(Markdown-Links $content)) {
            $normalizedTarget = $link.Target.Replace('\', '/')
            if ($normalizedTarget -notmatch '(^|/)repowiki/') {
                continue
            }
            Validate-Link $path $relative $link.Line $link.Target
        }
    }
}

function Validate-Retirement() {
    # These strings are deliberately assembled so the validator does not itself preserve the
    # retired references it is checking for.
    $oldWikiReference = 'repowiki/en/' + 'content/'
    $oldGeneratorReference = 'tools/' + 'generate-xml-def-wiki.ps1'
    $oldWikiPath = Repo-Path $oldWikiReference
    $oldGeneratorPath = Repo-Path $oldGeneratorReference
    if (Test-Path -LiteralPath $oldWikiPath) {
        Add-Failure "Retired wiki tree still exists: $oldWikiReference"
    }
    if (Test-Path -LiteralPath $oldGeneratorPath) {
        Add-Failure "Retired wiki generator still exists: $oldGeneratorReference"
    }

    $tracked = @(& git -c core.quotepath=false -C $repoRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        Add-Failure 'git ls-files failed; retired-reference checks were incomplete.'
        return
    }
    $textExtensions = @(
        '.md', '.ps1', '.cs', '.xml', '.txt', '.json', '.yml', '.yaml', '.props', '.targets'
    )
    foreach ($relative in $tracked) {
        if ($relative.Replace('\', '/') -eq 'design/WIKI_REDESIGN_PLAN.md') {
            # The approved historical plan necessarily names the artifacts it ordered removed.
            continue
        }
        if ($textExtensions -notcontains [System.IO.Path]::GetExtension($relative)) {
            continue
        }
        $path = Repo-Path $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }
        $text = Get-Content -Raw -LiteralPath $path
        if ($text.Contains($oldWikiReference)) {
            Add-Failure "$relative still references the retired wiki tree."
        }
        if ($text.Contains($oldGeneratorReference)) {
            Add-Failure "$relative still references the retired wiki generator."
        }
    }

    $generatedMarker =
        '<!-- Generated by tools/' + 'generate-xml-def-wiki.ps1; do not edit by hand. -->'
    foreach ($file in Get-ChildItem -LiteralPath $WikiRoot -Recurse -Filter '*.md' -File) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        if ($text.Contains($generatedMarker)) {
            $relative = $file.FullName.Substring($WikiRoot.Length).TrimStart('\').Replace('\', '/')
            Add-Failure "$relative still contains the obsolete generated-wiki marker."
        }
    }
}

function Validate-Required-Shape() {
    $requiredPages = @(
        'README.md'
        'en/How-It-Works.md'
        'en/Captured-Events.md'
        'en/Player-Settings.md'
        'en/XML-Customization.md'
        'en/Prompt-Building.md'
        'en/reference/Event-Catalog.md'
        'en/reference/Observed-Conditions-and-Windows.md'
        'en/reference/Prompt-Reference.md'
        'en/reference/Compatibility.md'
    )
    foreach ($relative in $requiredPages) {
        if (-not (Test-Path -LiteralPath (Wiki-Path $relative) -PathType Leaf)) {
            Add-Failure "Required wiki page is missing: $relative"
        }
    }

    $compatibilityPath = Wiki-Path 'en/reference/Compatibility.md'
    if (Test-Path -LiteralPath $compatibilityPath -PathType Leaf) {
        $compatibilityText = Get-Content -Raw -LiteralPath $compatibilityPath
        if ($compatibilityText -notmatch '(?i)closed list' -or
            $compatibilityText -notmatch '(?i)unknown third-party') {
            Add-Failure (
                'en/reference/Compatibility.md must state that shipped coverage is not a closed ' +
                'inventory of unknown third-party API users.')
        }
    }

    $referenceRoot = Wiki-Path 'en/reference'
    if (Test-Path -LiteralPath $referenceRoot -PathType Container) {
        foreach ($directory in Get-ChildItem -LiteralPath $referenceRoot -Directory -Recurse) {
            $relative = $directory.FullName.Substring($WikiRoot.Length).TrimStart('\').Replace('\', '/')
            Add-Failure "Reference nesting is deeper than the agreed single level: $relative"
        }
    }
}

function Report-Metrics() {
    if (-not (Test-Path -LiteralPath $WikiRoot -PathType Container)) {
        return
    }
    $pages = @(Get-ChildItem -LiteralPath $WikiRoot -Recurse -Filter '*.md' -File)
    $wordCount = 0
    $mermaidCount = 0
    foreach ($page in $pages) {
        $text = Get-Content -Raw -LiteralPath $page.FullName
        $withoutMetadata = [regex]::Replace(
            $text,
            '(?m)^<!-- repowiki:[^\r\n]+-->\s*$',
            '')
        $wordCount += [regex]::Matches(
            $withoutMetadata,
            "[\p{L}\p{Nd}][\p{L}\p{Nd}'’_-]*").Count
        $mermaidCount += [regex]::Matches(
            $text,
            '(?m)^```mermaid\s*$').Count
    }
    Write-Host ''
    Write-Host 'Informational wiki metrics:'
    Write-Host "  Markdown pages: $($pages.Count)"
    Write-Host "  Words: $wordCount"
    Write-Host "  Mermaid blocks: $mermaidCount"
    Write-Host (
        '  Method: recursive *.md files; Unicode word-token regex after removing repowiki ' +
        'metadata comments; Mermaid opening fences counted.')
}

Write-Host "Verifying Pawn Diary wiki at $WikiRoot"
Write-Host ''
Write-Host 'Source-backed inventories:'

$runtime = @(Runtime-Inventory)
Validate-Inventory 'en/reference/Event-Catalog.md' 'runtime' $runtime 'id'

$groups = @(Core-Group-Inventory)
Validate-Inventory 'en/reference/Event-Catalog.md' 'group' $groups 'defName'

$conditionBases = @(Core-Condition-Base-Inventory)
Validate-Inventory (
    'en/reference/Observed-Conditions-and-Windows.md'
) 'condition-base' $conditionBases 'name'

$conditions = @(Core-Condition-Inventory)
Validate-Inventory (
    'en/reference/Observed-Conditions-and-Windows.md'
) 'condition' $conditions 'defName'

$windows = @(Core-Window-Inventory)
Validate-Inventory (
    'en/reference/Observed-Conditions-and-Windows.md'
) 'window' $windows 'defName'

$templates = @(Template-Inventory)
Validate-Inventory 'en/reference/Prompt-Reference.md' 'template' $templates 'defName'

$promptPolicies = @(Prompt-Policy-Inventory)
Validate-Inventory 'en/reference/Prompt-Reference.md' 'event-policy' $promptPolicies 'defName'

$enchantments = @(Enchantment-Inventory)
Validate-Inventory 'en/reference/Prompt-Reference.md' 'enchantment' $enchantments 'defName'

$adapters = @(Adapter-Inventory)
Validate-Inventory 'en/reference/Compatibility.md' 'adapter' $adapters 'directory'

$compatibility = @(Compatibility-Inventory)
Validate-Inventory 'en/reference/Compatibility.md' 'compat-group' @(
    $compatibility | Where-Object { $_.Kind -eq 'group' } | ForEach-Object { $_.Data }
) 'defName'
Validate-Inventory 'en/reference/Compatibility.md' 'compat-window' @(
    $compatibility | Where-Object { $_.Kind -eq 'window' } | ForEach-Object { $_.Data }
) 'defName'
Validate-Inventory 'en/reference/Compatibility.md' 'compat-condition' @(
    $compatibility | Where-Object { $_.Kind -eq 'condition' } | ForEach-Object { $_.Data }
) 'defName'

Write-Host ''
Write-Host 'Navigation and retirement:'
Validate-Required-Shape
Validate-Navigation
if ([string]::Equals(
    $WikiRoot,
    $primaryWikiRoot,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    Validate-Retirement
    Write-Host '  Checked local wiki links, tracked incoming wiki links, anchors, and retired artifacts.'
}
else {
    Write-Host '  Checked local and tracked incoming wiki links and anchors.'
    Write-Host '  Retirement checks apply only to the primary repowiki root and were skipped for this fixture.'
}

Report-Metrics

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "Wiki verification failed with $($failures.Count) problem(s):" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host ''
Write-Host 'Wiki verification passed.' -ForegroundColor Green
exit 0
