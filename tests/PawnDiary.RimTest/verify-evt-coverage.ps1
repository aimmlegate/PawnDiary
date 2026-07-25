# Validates the tracked EVT-01..EVT-26 coverage manifest against real RimTest methods.
#
# This intentionally checks source structure, not loaded-run pass totals. Every manifest row must
# name one concrete line-anchored [Test] method plus its required mod profile and evidence level, so
# a requirement token in a comment or exception message cannot masquerade as coverage.
$ErrorActionPreference = "Stop"

$rimTestRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $rimTestRoot "evt-coverage.json"
$expectedIds = @(1..26 | ForEach-Object { "EVT-{0:D2}" -f $_ })
$allowedEvidence = @(
    "vanilla-trigger",
    "production-flow",
    "downstream-flow",
    "public-api",
    "loaded-contract"
)

function Get-RimTestDefinedSymbols {
    $projectPath = Join-Path $rimTestRoot "PawnDiary.RimTest.csproj"
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Missing RimTest project: $projectPath"
    }

    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $nodes = $project.SelectNodes(
        "//*[local-name()='PropertyGroup'][not(@Condition) or contains(@Condition, 'Debug')]" +
        "/*[local-name()='DefineConstants']")
    $symbols = New-Object "System.Collections.Generic.List[string]"
    foreach ($node in $nodes) {
        foreach ($candidate in ([string]$node.InnerText -split ";")) {
            $symbol = $candidate.Trim()
            if ($symbol -match "^[A-Za-z_][A-Za-z0-9_]*$" -and -not $symbols.Contains($symbol)) {
                $symbols.Add($symbol)
            }
        }
    }
    return $symbols
}

function Test-CSharpPreprocessorExpression {
    param(
        [Parameter(Mandatory = $true)][string]$Expression,
        [Parameter(Mandatory = $true)]$DefinedSymbols
    )

    $tokens = New-Object "System.Collections.Generic.List[string]"
    $scan = 0
    while ($scan -lt $Expression.Length) {
        $remaining = $Expression.Substring($scan)
        if ([string]::IsNullOrWhiteSpace($remaining)) {
            break
        }

        $match = [regex]::Match(
            $remaining,
            "^\s*(\|\||&&|==|!=|!|\(|\)|true\b|false\b|[A-Za-z_][A-Za-z0-9_]*)")
        if (-not $match.Success) {
            throw "Unsupported C# preprocessor expression '$Expression'."
        }
        $tokens.Add($match.Groups[1].Value)
        $scan += $match.Length
    }

    if ($tokens.Count -eq 0) {
        throw "Empty C# preprocessor expression."
    }

    # C# preprocessor expressions use the same small boolean grammar everywhere. A recursive-descent
    # parser keeps validation deterministic without evaluating source text as PowerShell code.
    $state = [pscustomobject]@{
        Tokens = $tokens
        Index = 0
    }
    $parseOr = $null
    $parseAnd = $null
    $parseEquality = $null
    $parseUnary = $null
    $parsePrimary = $null

    $parsePrimary = {
        if ($state.Index -ge $state.Tokens.Count) {
            throw "Incomplete C# preprocessor expression '$Expression'."
        }

        $token = $state.Tokens[$state.Index]
        $state.Index = $state.Index + 1
        if ($token -ceq "(") {
            $value = [bool](& $parseOr)
            if ($state.Index -ge $state.Tokens.Count -or
                $state.Tokens[$state.Index] -cne ")") {
                throw "Unbalanced C# preprocessor expression '$Expression'."
            }
            $state.Index = $state.Index + 1
            return $value
        }
        if ($token -ceq "true") {
            return $true
        }
        if ($token -ceq "false") {
            return $false
        }
        if ($token -ceq ")") {
            throw "Unexpected ')' in C# preprocessor expression '$Expression'."
        }
        return [bool]$DefinedSymbols.Contains($token)
    }

    $parseUnary = {
        if ($state.Index -lt $state.Tokens.Count -and
            $state.Tokens[$state.Index] -ceq "!") {
            $state.Index = $state.Index + 1
            return -not [bool](& $parseUnary)
        }
        return [bool](& $parsePrimary)
    }

    $parseEquality = {
        $value = [bool](& $parseUnary)
        while ($state.Index -lt $state.Tokens.Count) {
            $operator = $state.Tokens[$state.Index]
            if ($operator -cne "==" -and $operator -cne "!=") {
                break
            }
            $state.Index = $state.Index + 1
            $right = [bool](& $parseUnary)
            $value = if ($operator -ceq "==") { $value -eq $right } else { $value -ne $right }
        }
        return $value
    }

    $parseAnd = {
        $value = [bool](& $parseEquality)
        while ($state.Index -lt $state.Tokens.Count -and
            $state.Tokens[$state.Index] -ceq "&&") {
            $state.Index = $state.Index + 1
            $right = [bool](& $parseEquality)
            $value = $value -and $right
        }
        return $value
    }

    $parseOr = {
        $value = [bool](& $parseAnd)
        while ($state.Index -lt $state.Tokens.Count -and
            $state.Tokens[$state.Index] -ceq "||") {
            $state.Index = $state.Index + 1
            $right = [bool](& $parseAnd)
            $value = $value -or $right
        }
        return $value
    }

    $result = [bool](& $parseOr)
    if ($state.Index -ne $state.Tokens.Count) {
        throw "Unexpected token in C# preprocessor expression '$Expression'."
    }
    return $result
}

function Get-CSharpLexStateAfterLine {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line,
        [Parameter(Mandatory = $true)][string]$InitialState
    )

    $state = $InitialState
    for ($i = 0; $i -lt $Line.Length; $i++) {
        $character = $Line[$i]
        $next = if ($i + 1 -lt $Line.Length) { $Line[$i + 1] } else { [char]0 }

        if ($state -eq "BlockComment") {
            if ($character -eq "*" -and $next -eq "/") {
                $state = "Code"
                $i++
            }
            continue
        }
        if ($state -eq "VerbatimString") {
            if ($character -eq '"') {
                if ($next -eq '"') {
                    $i++
                } else {
                    $state = "Code"
                }
            }
            continue
        }
        if ($state -eq "String" -or $state -eq "Character") {
            if ($character -eq "\") {
                $i++
                continue
            }
            if (($state -eq "String" -and $character -eq '"') -or
                ($state -eq "Character" -and $character -eq "'")) {
                $state = "Code"
            }
            continue
        }

        if ($character -eq "/" -and $next -eq "/") {
            return "Code"
        }
        if ($character -eq "/" -and $next -eq "*") {
            $state = "BlockComment"
            $i++
            continue
        }
        if ($character -eq '"') {
            $isVerbatim = ($i -ge 1 -and $Line[$i - 1] -eq "@") -or
                ($i -ge 2 -and $Line[$i - 2] -eq "@" -and $Line[$i - 1] -eq '$')
            $state = if ($isVerbatim) { "VerbatimString" } else { "String" }
            continue
        }
        if ($character -eq "'") {
            $state = "Character"
        }
    }
    return $state
}

function Remove-CSharpCommentsAndLiterals {
    param([Parameter(Mandatory = $true)][string]$Source)

    $builder = New-Object System.Text.StringBuilder
    $state = "Code"
    for ($i = 0; $i -lt $Source.Length; $i++) {
        $character = $Source[$i]
        $next = if ($i + 1 -lt $Source.Length) { $Source[$i + 1] } else { [char]0 }
        $isNewline = $character -eq "`r" -or $character -eq "`n"

        if ($state -eq "LineComment") {
            if ($isNewline) {
                [void]$builder.Append($character)
                $state = "Code"
            } else {
                [void]$builder.Append(" ")
            }
            continue
        }
        if ($state -eq "BlockComment") {
            if ($character -eq "*" -and $next -eq "/") {
                [void]$builder.Append("  ")
                $state = "Code"
                $i++
            } elseif ($isNewline) {
                [void]$builder.Append($character)
            } else {
                [void]$builder.Append(" ")
            }
            continue
        }
        if ($state -eq "VerbatimString") {
            if ($character -eq '"') {
                [void]$builder.Append(" ")
                if ($next -eq '"') {
                    [void]$builder.Append(" ")
                    $i++
                } else {
                    $state = "Code"
                }
            } elseif ($isNewline) {
                [void]$builder.Append($character)
            } else {
                [void]$builder.Append(" ")
            }
            continue
        }
        if ($state -eq "String" -or $state -eq "Character") {
            if ($character -eq "\" -and $i + 1 -lt $Source.Length) {
                [void]$builder.Append(" ")
                $i++
                $escaped = $Source[$i]
                if ($escaped -eq "`r" -or $escaped -eq "`n") {
                    [void]$builder.Append($escaped)
                } else {
                    [void]$builder.Append(" ")
                }
                continue
            }

            if (($state -eq "String" -and $character -eq '"') -or
                ($state -eq "Character" -and $character -eq "'")) {
                [void]$builder.Append(" ")
                $state = "Code"
            } elseif ($isNewline) {
                [void]$builder.Append($character)
            } else {
                [void]$builder.Append(" ")
            }
            continue
        }

        if ($character -eq "/" -and $next -eq "/") {
            [void]$builder.Append("  ")
            $state = "LineComment"
            $i++
            continue
        }
        if ($character -eq "/" -and $next -eq "*") {
            [void]$builder.Append("  ")
            $state = "BlockComment"
            $i++
            continue
        }
        if ($character -eq '"') {
            $isVerbatim = ($i -ge 1 -and $Source[$i - 1] -eq "@") -or
                ($i -ge 2 -and $Source[$i - 2] -eq "@" -and $Source[$i - 1] -eq '$')
            [void]$builder.Append(" ")
            $state = if ($isVerbatim) { "VerbatimString" } else { "String" }
            continue
        }
        if ($character -eq "'") {
            [void]$builder.Append(" ")
            $state = "Character"
            continue
        }
        [void]$builder.Append($character)
    }
    return $builder.ToString()
}

function Get-ActiveCSharpCode {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string[]]$DefinedSymbols
    )

    $symbols = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    foreach ($symbol in $DefinedSymbols) {
        [void]$symbols.Add($symbol)
    }

    $builder = New-Object System.Text.StringBuilder
    $frames = New-Object System.Collections.Stack
    $currentActive = $true
    $lexState = "Code"
    $lines = [regex]::Split($Source, "(?<=`n)")
    foreach ($line in $lines) {
        $canContainDirective = -not $currentActive -or $lexState -eq "Code"
        $directive = if ($canContainDirective) {
            [regex]::Match($line, "^\s*#\s*(if|elif|else|endif|define|undef)\b(.*)$")
        } else {
            $null
        }

        if ($directive -ne $null -and $directive.Success) {
            $name = $directive.Groups[1].Value
            $argument = [regex]::Replace($directive.Groups[2].Value, "//.*$", "").Trim()
            if ($name -ceq "if") {
                $condition = Test-CSharpPreprocessorExpression $argument $symbols
                $frame = [pscustomobject]@{
                    ParentActive = $currentActive
                    AnyBranchTaken = $condition
                    CurrentActive = $currentActive -and $condition
                }
                $frames.Push($frame)
                $currentActive = $frame.CurrentActive
            } elseif ($name -ceq "elif") {
                if ($frames.Count -eq 0) {
                    throw "Unexpected #elif in C# source."
                }
                $frame = $frames.Peek()
                $condition = Test-CSharpPreprocessorExpression $argument $symbols
                $takeBranch = -not $frame.AnyBranchTaken -and $condition
                $frame.CurrentActive = $frame.ParentActive -and $takeBranch
                $frame.AnyBranchTaken = $frame.AnyBranchTaken -or $condition
                $currentActive = $frame.CurrentActive
            } elseif ($name -ceq "else") {
                if ($frames.Count -eq 0) {
                    throw "Unexpected #else in C# source."
                }
                $frame = $frames.Peek()
                $frame.CurrentActive = $frame.ParentActive -and -not $frame.AnyBranchTaken
                $frame.AnyBranchTaken = $true
                $currentActive = $frame.CurrentActive
            } elseif ($name -ceq "endif") {
                if ($frames.Count -eq 0) {
                    throw "Unexpected #endif in C# source."
                }
                $frame = $frames.Pop()
                $currentActive = $frame.ParentActive
            } elseif ($currentActive -and $name -ceq "define") {
                if ($argument -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
                    throw "Invalid #define symbol '$argument'."
                }
                [void]$symbols.Add($argument)
            } elseif ($currentActive -and $name -ceq "undef") {
                if ($argument -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
                    throw "Invalid #undef symbol '$argument'."
                }
                [void]$symbols.Remove($argument)
            }

            [void]$builder.Append([regex]::Replace($line, "[^`r`n]", " "))
            $lexState = "Code"
            continue
        }

        if ($currentActive) {
            [void]$builder.Append($line)
            $lexState = Get-CSharpLexStateAfterLine $line $lexState
        } else {
            [void]$builder.Append([regex]::Replace($line, "[^`r`n]", " "))
            $lexState = "Code"
        }
    }

    if ($frames.Count -ne 0) {
        throw "Unterminated conditional-compilation block in C# source."
    }
    return Remove-CSharpCommentsAndLiterals $builder.ToString()
}

function Test-CSharpTestMethodBinding {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$MethodName,
        [Parameter(Mandatory = $true)][string[]]$DefinedSymbols
    )

    $activeCode = Get-ActiveCSharpCode $Source $DefinedSymbols
    $declarationPattern = "(?ms)(?<attributes>(?:^\s*\[[^\]\r\n]+\]\s*)+)"
    $declarationPattern += "^\s*public\s+static\s+void\s+"
    $declarationPattern += [regex]::Escape($MethodName)
    $declarationPattern += "\s*\("
    $attributeNamePrefix = "(?:[A-Za-z_][A-Za-z0-9_]*\.)*"
    $testAttributePattern = "(?i)(?:^|[,\[])\s*" + $attributeNamePrefix +
        "Test(?:Attribute)?\s*(?:\(|,|\])"
    $disabledAttributePattern = "(?i)(?:^|[,\[])\s*" + $attributeNamePrefix +
        "(?:Ignore|Explicit)(?:Attribute)?\s*(?:\(|,|\])"

    foreach ($match in [regex]::Matches($activeCode, $declarationPattern)) {
        $attributes = $match.Groups["attributes"].Value
        if ([regex]::IsMatch($attributes, $testAttributePattern) -and
            -not [regex]::IsMatch($attributes, $disabledAttributePattern)) {
            return $true
        }
    }
    return $false
}

function Assert-CSharpCoverageScanner {
    param([Parameter(Mandatory = $true)][string[]]$DefinedSymbols)

    $checks = @(
        [pscustomobject]@{
            Name = "active method"
            Expected = $true
            Source = "[Test]`npublic static void Covered() { }"
        },
        [pscustomobject]@{
            Name = "line comment"
            Expected = $false
            Source = "// [Test]`n// public static void Covered() { }"
        },
        [pscustomobject]@{
            Name = "block comment"
            Expected = $false
            Source = "/*`n[Test]`npublic static void Covered() { }`n*/"
        },
        [pscustomobject]@{
            Name = "verbatim string"
            Expected = $false
            Source = "private const string Fake = @`"`n[Test]`npublic static void Covered() { }`n`";"
        },
        [pscustomobject]@{
            Name = "disabled literal branch"
            Expected = $false
            Source = "#if false`n[Test]`npublic static void Covered() { }`n#endif"
        },
        [pscustomobject]@{
            Name = "ignored test"
            Expected = $false
            Source = "[Ignore]`n[Test]`npublic static void Covered() { }"
        },
        [pscustomobject]@{
            Name = "explicit test"
            Expected = $false
            Source = "[Test]`n[Explicit]`npublic static void Covered() { }"
        },
        [pscustomobject]@{
            Name = "undefined-symbol branch"
            Expected = $false
            Source = "#if PAWNDIARY_COVERAGE_SCANNER_UNDEFINED`n[Test]`npublic static void Covered() { }`n#endif"
        },
        [pscustomobject]@{
            Name = "active else branch"
            Expected = $true
            Source = "#if false`nprivate static void Other() { }`n#else`n[Test]`npublic static void Covered() { }`n#endif"
        }
    )

    foreach ($check in $checks) {
        $actual = Test-CSharpTestMethodBinding $check.Source "Covered" $DefinedSymbols
        if ($actual -ne $check.Expected) {
            throw "EVT C# scanner self-check failed for '$($check.Name)'."
        }
    }
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing EVT coverage manifest: $manifestPath"
}

$definedSymbols = @(Get-RimTestDefinedSymbols)
Assert-CSharpCoverageScanner $definedSymbols

$parsedRows = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
# Windows PowerShell 5.1 returns a top-level JSON array as one Object[] pipeline item. A foreach
# explicitly unwraps it so the validator behaves the same under Windows PowerShell and PowerShell 7.
$rows = @()
foreach ($parsedRow in $parsedRows) {
    $rows += $parsedRow
}
if ($rows.Count -eq 0) {
    throw "EVT coverage manifest is empty: $manifestPath"
}

foreach ($id in $expectedIds) {
    $matches = @($rows | Where-Object { [string]$_.id -eq $id })
    if ($matches.Count -ne 1) {
        throw "$id must have exactly one manifest row; found $($matches.Count)."
    }

    $row = $matches[0]
    $testFile = [string]$row.testFile
    $testMethod = [string]$row.testMethod
    $profile = [string]$row.profile
    $evidence = [string]$row.evidence

    if ([string]::IsNullOrWhiteSpace($testFile) -or
        [System.IO.Path]::GetFileName($testFile) -ne $testFile -or
        -not $testFile.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase)) {
        throw "$id has an invalid testFile '$testFile'; use one .cs filename in this directory."
    }
    if ($testMethod -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
        throw "$id has an invalid testMethod '$testMethod'."
    }
    if ([string]::IsNullOrWhiteSpace($profile)) {
        throw "$id does not declare its required mod profile."
    }
    if ($allowedEvidence -notcontains $evidence) {
        throw "$id has unsupported evidence '$evidence'."
    }

    $testPath = Join-Path $rimTestRoot $testFile
    if (-not (Test-Path -LiteralPath $testPath)) {
        throw "$id references missing test file '$testFile'."
    }

    $source = Get-Content -LiteralPath $testPath -Raw
    if (-not (Test-CSharpTestMethodBinding $source $testMethod $definedSymbols)) {
        throw "$id does not bind to [Test] method '${testFile}::$testMethod'."
    }
}

$unknownRows = @($rows | Where-Object { $expectedIds -notcontains [string]$_.id })
if ($unknownRows.Count -gt 0) {
    $unknownIds = @($unknownRows | ForEach-Object { [string]$_.id })
    throw "EVT coverage manifest contains unknown rows: $($unknownIds -join ', ')."
}

Write-Host "EVT coverage manifest valid: $($expectedIds.Count)/$($expectedIds.Count)."
