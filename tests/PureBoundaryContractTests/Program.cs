// Standalone architecture tripwires for Pawn Diary's pure/impure boundary. This harness inspects
// project and source text; it deliberately links no production assembly and therefore runs without
// RimWorld, Verse, Unity, Harmony, Steam, a save, or a network connection.
//
// This is a conservative lexical audit, not a C# compiler/analyzer. It masks comments and literals
// before dependency checks, resolves explicit <Compile Include="..."> links from every standalone
// test project, and reports exact file/line evidence. Code review and the compiler still own alias
// resolution, reflection, generated code, interpolated-string expressions, and object-flow proofs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PureBoundaryContractTests
{
    /// <summary>
    /// Runs the repository's executable pure-boundary and structured-context source contracts.
    /// </summary>
    internal static class Program
    {
        private const string ThisProject =
            "tests/PureBoundaryContractTests/PureBoundaryContractTests.csproj";

        private static readonly Dictionary<string, string> IntentionallyImpureProjects =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "tests/PawnDiary.RimTest/PawnDiary.RimTest.csproj",
                    "Loaded-game adapter fixtures intentionally reference RimWorld, Verse, Unity, Harmony, and RimTest."
                },
            };

        // A pure harness occasionally links one mixed adapter so it can exercise a pure method beside
        // the adapter method. Exempt only the exact file/symbol/rule combination; every other dependency
        // in that file still fails. "<file>" denotes a using directive before any method declaration.
        private static readonly Dictionary<string, string> AllowedLinkedDependencySymbols =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "integrations/PawnDiary.RimTalkBridge/Source/Pure/ConversationAssessmentResponseParser.cs#<file>#filesystem namespace",
                    "The parser uses an in-memory MemoryStream for DataContractJsonSerializer; it never opens a path."
                },
                {
                    "Source/Settings/ApiRequestAuth.cs#<file>#network namespace",
                    "This mixed auth adapter owns the System.Net.Http using directives; query-URL policy remains pure."
                },
                {
                    "Source/Settings/ApiRequestAuth.cs#ApplyHeaders#network request/header type",
                    "ApplyHeaders is the intentional impure edge that mutates an HttpRequestMessage."
                },
            };

        // These are exact, symbol-level formatter exemptions rather than directory exemptions. A row
        // means the named method owns a typed context contract whose values are schema tokens, numbers,
        // booleans, or values sanitized before the final join. New methods do not inherit an exemption.
        private static readonly Dictionary<string, string> TrustedContextFormatters =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Source/Pipeline/OdysseyContextFormatter.cs#ProjectPairRoleForPov",
                    "The role is admitted only when OdysseyJourneyRoleTokens.Rank recognizes the closed token."
                },
            };

        private static readonly HashSet<string> UsedImpureProjectRows =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UsedDependencyRows =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UsedContextFormatterRows =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly DependencyRule[] ForbiddenSourceRules =
        {
            Rule("Verse namespace/type", @"\bVerse\b"),
            Rule("RimWorld namespace/type", @"\bRimWorld\b"),
            Rule("UnityEngine namespace/type", @"\bUnityEngine\b"),
            Rule("Scribe persistence API", @"\bScribe(?:_[A-Za-z0-9_]+)?\b"),
            Rule("DefDatabase live Def lookup", @"\bDefDatabase\b"),
            Rule("main-thread translation", @"\.\s*Translate\s*\("),
            Rule("filesystem namespace", @"\bSystem\s*\.\s*IO\b"),
            Rule("filesystem File API", @"\bFile\s*\."),
            Rule("filesystem Directory API", @"\bDirectory\s*\."),
            Rule("filesystem path API", @"\bPath\s*\."),
            Rule("filesystem stream/type", @"\b(?:FileInfo|DirectoryInfo|FileStream|StreamReader|StreamWriter|DriveInfo)\b"),
            Rule("network namespace", @"\bSystem\s*\.\s*Net\b"),
            Rule(
                "network request/header type",
                @"\b(?:HttpClient|HttpRequestMessage|AuthenticationHeaderValue|HttpWebRequest|WebRequest|WebClient|TcpClient|UdpClient|Socket)\b"),
            Rule("RimWorld global RNG", @"\bRand\s*\."),
            Rule("System.Random construction/type", @"\b(?:System\s*\.\s*)?Random\b"),
            Rule("live Pawn Diary settings", @"\bPawnDiaryMod\s*\.\s*Settings\b"),
            Rule("settings model dependency", @"\b(?:PawnDiarySettings|DiarySettings|ModSettings)\b"),
        };

        private static readonly Regex MethodDeclaration = new Regex(
            @"(?m)^[ \t]*(?:(?:public|private|internal|protected|static|virtual|override|sealed|async|extern|new|unsafe|partial)\s+)+"
                + @"(?:[A-Za-z_][A-Za-z0-9_<>,.\[\]? \t]*\s+)?"
                + @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex ContextFieldLiteral = new Regex(
            "\"(?:;\\s*)?[A-Za-z_][A-Za-z0-9_]*=",
            RegexOptions.Compiled);

        private static readonly Regex ContextJoin = new Regex(
            @"\bstring\s*\.\s*Join\s*\(\s*"";\s*""",
            RegexOptions.Compiled);

        private static readonly Regex SanitizerCall = new Regex(
            @"\bGameContextValue\s*\.\s*Sanitize\s*\(",
            RegexOptions.Compiled);

        private static int assertions;

        private static int Main()
        {
            try
            {
                TestLexicalMasking();
                TestDependencyExamples();
                TestContextWriterExamples();
                ValidateAllowlists();

                string root = FindRepositoryRoot();
                List<Violation> violations = new List<Violation>();
                ProjectAuditResult projects = AuditPureProjects(root, violations);
                int contextWriters = AuditStructuredContextWriters(root, violations);
                ValidateAllowlistUsage();

                if (violations.Count > 0)
                {
                    Console.Error.WriteLine(
                        "PureBoundaryContractTests found " + violations.Count + " violation(s):");
                    foreach (Violation violation in violations
                        .OrderBy(value => value.path, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(value => value.line)
                        .ThenBy(value => value.rule, StringComparer.Ordinal))
                    {
                        Console.Error.WriteLine(
                            "  " + violation.path + ":" + violation.line
                                + " [" + violation.rule + "] " + violation.detail);
                    }

                    return 1;
                }

                Console.WriteLine(
                    "PureBoundaryContractTests passed " + assertions + " self-check assertions; audited "
                        + projects.projectCount + " standalone projects, "
                        + projects.productionFileCount + " linked production files, and "
                        + contextWriters + " structured-context writer statements.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("PureBoundaryContractTests failed: " + exception);
                return 1;
            }
        }

        /// <summary>
        /// Scans every test project, including isolated monorepo modules, except exact documented
        /// loaded-game projects. Default inclusion keeps new pure projects under this contract.
        /// </summary>
        private static ProjectAuditResult AuditPureProjects(
            string root,
            List<Violation> violations)
        {
            string testsRoot = Path.Combine(root, "tests");
            List<string> projectPaths = Directory.GetFiles(
                testsRoot,
                "*.csproj",
                SearchOption.AllDirectories).ToList();
            string helpfulTextTests = Path.Combine(root, "HelpfulTextEngine", "tests");
            if (Directory.Exists(helpfulTextTests))
            {
                projectPaths.AddRange(Directory.GetFiles(
                    helpfulTextTests,
                    "*.csproj",
                    SearchOption.AllDirectories));
            }
            HashSet<string> productionFiles =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int auditedProjects = 0;

            foreach (string projectPath in projectPaths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string relativeProject = RelativePath(root, projectPath);
                string impureReason;
                if (IntentionallyImpureProjects.TryGetValue(relativeProject, out impureReason))
                {
                    Require(
                        "impure project allowlist has a rationale",
                        !string.IsNullOrWhiteSpace(impureReason));
                    UsedImpureProjectRows.Add(relativeProject);
                    continue;
                }

                auditedProjects++;
                XDocument document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
                AuditProjectReferences(relativeProject, document, violations);

                string projectDirectory = Path.GetDirectoryName(projectPath);
                foreach (XElement compile in document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "Compile"))
                {
                    XAttribute includeAttribute = compile.Attribute("Include");
                    if (includeAttribute == null
                        || string.IsNullOrWhiteSpace(includeAttribute.Value))
                    {
                        continue;
                    }

                    if (HasWildcard(includeAttribute.Value))
                    {
                        if (LooksLikeProductionLink(includeAttribute.Value))
                        {
                            AddXmlViolation(
                                relativeProject,
                                compile,
                                "wildcard production compile link",
                                "Link exact production files so every pure dependency is auditable.",
                                violations);
                        }

                        continue;
                    }

                    string linkedPath = Path.GetFullPath(Path.Combine(
                        projectDirectory,
                        includeAttribute.Value.Replace('\\', Path.DirectorySeparatorChar)));
                    if (!File.Exists(linkedPath))
                    {
                        if (LooksLikeProductionLink(includeAttribute.Value))
                        {
                            AddXmlViolation(
                                relativeProject,
                                compile,
                                "unresolved production compile link",
                                "'" + includeAttribute.Value
                                    + "' could not be resolved, so its dependencies cannot be audited.",
                                violations);
                        }

                        continue;
                    }

                    if (!linkedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Test-owned sources may use IO/assert helpers. The boundary being guarded is the
                    // production source explicitly linked into a no-game test assembly.
                    if (!IsWithinDirectory(linkedPath, projectDirectory))
                    {
                        productionFiles.Add(linkedPath);
                    }
                }
            }

            foreach (string productionFile in productionFiles
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                AuditLinkedProductionSource(root, productionFile, violations);
            }

            Require("at least twenty standalone projects were audited", auditedProjects >= 20);
            Require("linked production inventory is substantial", productionFiles.Count >= 100);
            Require(
                "this contract project is covered by default project discovery",
                projectPaths.Any(path => RelativePath(root, path).Equals(
                    ThisProject,
                    StringComparison.OrdinalIgnoreCase)));

            return new ProjectAuditResult(auditedProjects, productionFiles.Count);
        }

        private static void AuditProjectReferences(
            string relativeProject,
            XDocument document,
            List<Violation> violations)
        {
            foreach (XElement element in document.Descendants())
            {
                string localName = element.Name.LocalName;
                if (localName == "ProjectReference")
                {
                    AddXmlViolation(
                        relativeProject,
                        element,
                        "project reference",
                        "Standalone pure harnesses must link the exact production files they audit, not a compiled project.",
                        violations);
                    continue;
                }

                if (localName != "Reference" && localName != "PackageReference")
                {
                    continue;
                }

                XAttribute include = element.Attribute("Include");
                string name = include == null ? string.Empty : include.Value;
                if (Regex.IsMatch(
                    name,
                    @"(?:Assembly-CSharp|Verse|RimWorld|UnityEngine|Harmony|RimTest|PawnDiary|System\.Net|HttpClient|FileSystem)",
                    RegexOptions.IgnoreCase))
                {
                    AddXmlViolation(
                        relativeProject,
                        element,
                        "forbidden assembly/package reference",
                        "'" + name + "' would make the standalone harness depend on an impure runtime assembly.",
                        violations);
                }
            }

            foreach (XElement hintPath in document
                .Descendants()
                .Where(element => element.Name.LocalName == "HintPath"))
            {
                if (Regex.IsMatch(
                    hintPath.Value,
                    @"(?:RimWorldWin64_Data|Assembly-CSharp|UnityEngine|0Harmony|RimTest)",
                    RegexOptions.IgnoreCase))
                {
                    AddXmlViolation(
                        relativeProject,
                        hintPath,
                        "forbidden assembly hint path",
                        "'" + hintPath.Value.Trim() + "' resolves an impure game/runtime dependency.",
                        violations);
                }
            }
        }

        private static void AuditLinkedProductionSource(
            string root,
            string filePath,
            List<Violation> violations)
        {
            string source = File.ReadAllText(filePath);
            string masked = CSharpLexicalMask.MaskCommentsAndLiterals(source);
            string relativePath = RelativePath(root, filePath);
            foreach (DependencyRule rule in ForbiddenSourceRules)
            {
                foreach (Match match in rule.pattern.Matches(masked))
                {
                    string method = MethodNameAt(masked, match.Index);
                    string symbol = string.IsNullOrWhiteSpace(method) ? "<file>" : method;
                    string allowlistKey = relativePath + "#" + symbol + "#" + rule.name;
                    string rationale;
                    if (AllowedLinkedDependencySymbols.TryGetValue(allowlistKey, out rationale))
                    {
                        Require(
                            "linked dependency allowlist has a rationale",
                            !string.IsNullOrWhiteSpace(rationale));
                        UsedDependencyRows.Add(allowlistKey);
                        continue;
                    }

                    violations.Add(new Violation(
                        relativePath,
                        LineAt(masked, match.Index),
                        rule.name,
                        "Linked pure production code contains '" + OneLine(source, match.Index, match.Length) + "'."));
                }
            }
        }

        /// <summary>
        /// Audits likely structured-context owners. It catches direct field literals and semicolon joins
        /// in typed event/formatter files, plus statements that explicitly write a gameContext variable.
        /// Values must use GameContextValue.Sanitize, a file-local wrapper that delegates to it, a
        /// conservative scalar expression, or an exact method allowlist row with a boundary rationale.
        /// </summary>
        private static int AuditStructuredContextWriters(
            string root,
            List<Violation> violations)
        {
            List<string> roots = new List<string>
            {
                Path.Combine(root, "Source"),
            };
            string integrations = Path.Combine(root, "integrations");
            if (Directory.Exists(integrations))
            {
                roots.Add(integrations);
            }

            int auditedStatements = 0;
            foreach (string filePath in roots
                .SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string relativePath = RelativePath(root, filePath);
                if (relativePath.StartsWith("Source/UI/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string source = File.ReadAllText(filePath);
                string commentsMasked = CSharpLexicalMask.MaskComments(source);
                HashSet<string> sanitizerWrappers = FindSanitizerWrappers(commentsMasked);
                HashSet<string> sanitizedLocals = FindSanitizedLocals(
                    commentsMasked,
                    sanitizerWrappers);
                foreach (SourceStatement statement in CSharpLexicalMask.Statements(commentsMasked))
                {
                    if (!LooksLikeStructuredContextWriter(relativePath, statement.text))
                    {
                        continue;
                    }

                    auditedStatements++;
                    Match fieldEvidence = ContextFieldLiteral.Match(statement.text);
                    Match joinEvidence = ContextJoin.Match(statement.text);
                    int evidenceOffset = fieldEvidence.Success
                        ? fieldEvidence.Index
                        : joinEvidence.Success ? joinEvidence.Index : 0;
                    string method = MethodNameAt(
                        commentsMasked,
                        statement.start + evidenceOffset);
                    if (IsTrustedContextStatement(
                        relativePath,
                        method,
                    statement.text,
                    source,
                    sanitizerWrappers,
                    sanitizedLocals,
                    statement.start + evidenceOffset))
                    {
                        continue;
                    }

                    violations.Add(new Violation(
                        relativePath,
                        LineAt(source, statement.start),
                        "structured gameContext writer",
                        "Route dynamic values through GameContextValue.Sanitize (or add an exact typed-formatter "
                            + "allowlist row with a rationale). Statement: "
                            + OneLine(statement.text, 0, statement.text.Length)));
                }
            }

            Require("structured-context inventory is non-trivial", auditedStatements >= 40);
            return auditedStatements;
        }

        private static bool LooksLikeStructuredContextWriter(
            string relativePath,
            string statement)
        {
            bool typedContextOwner =
                relativePath.IndexOf("/Capture/Events/", StringComparison.OrdinalIgnoreCase) >= 0
                || relativePath.EndsWith("ContextFormatter.cs", StringComparison.OrdinalIgnoreCase);
            bool namesSavedContext = Regex.IsMatch(
                statement,
                @"\bgameContext\b",
                RegexOptions.IgnoreCase);
            bool directField = ContextFieldLiteral.IsMatch(statement);
            bool join = ContextJoin.IsMatch(statement);
            bool explicitSavedContextWrite = Regex.IsMatch(
                statement,
                @"(?:\bstring\s+(?:gameContext|context)\s*=|\b(?:gameContext|context)\s*\+=)",
                RegexOptions.IgnoreCase);
            bool typedWriterShape = Regex.IsMatch(
                statement,
                @"\b(?:return\s+|(?:parts|fields|projected|builder|context)\s*\.\s*(?:Add|Append)\s*\()",
                RegexOptions.IgnoreCase);

            if (namesSavedContext
                && explicitSavedContextWrite
                && (directField || join))
            {
                return true;
            }

            return typedContextOwner && typedWriterShape && (directField || join);
        }

        private static bool IsTrustedContextStatement(
            string relativePath,
            string method,
            string statement,
            string wholeSource,
            HashSet<string> sanitizerWrappers,
            HashSet<string> sanitizedLocals,
            int statementPosition = -1)
        {
            HashSet<string> provenLocals = new HashSet<string>(
                sanitizedLocals,
                StringComparer.Ordinal);
            foreach (string scalar in FindScalarIdentifiers(
                wholeSource,
                method,
                statementPosition))
            {
                provenLocals.Add(scalar);
            }

            if (SanitizerCall.IsMatch(statement)
                && UsesOnlyProvenContextValues(
                    statement,
                    sanitizerWrappers,
                    provenLocals))
            {
                return true;
            }

            foreach (string wrapper in sanitizerWrappers)
            {
                if (Regex.IsMatch(statement, @"\b" + Regex.Escape(wrapper) + @"\s*\(")
                    && UsesOnlyProvenContextValues(
                        statement,
                        sanitizerWrappers,
                        provenLocals))
                {
                    return true;
                }
            }

            // A join is trusted only when this formatter demonstrably sanitizes fields while filling
            // the joined collection. The individual unsafe Add/Append statement would also be audited.
            if (ContextJoin.IsMatch(statement)
                && (SanitizerCall.IsMatch(wholeSource)
                    || HasLocalDelimiterFirewall(wholeSource)))
            {
                return true;
            }

            if (UsesOnlyProvenContextValues(
                statement,
                sanitizerWrappers,
                provenLocals))
            {
                return true;
            }

            if (IsConservativeScalarContextStatement(statement))
            {
                return true;
            }

            string allowlistKey = relativePath + "#" + method;
            string rationale;
            if (TrustedContextFormatters.TryGetValue(allowlistKey, out rationale))
            {
                Require("trusted context formatter has a rationale", !string.IsNullOrWhiteSpace(rationale));
                UsedContextFormatterRows.Add(allowlistKey);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Accepts schema literals and numeric/boolean expressions. It intentionally does not accept an
        /// arbitrary string identifier: free text must visibly pass through the sanitizer.
        /// </summary>
        private static bool IsConservativeScalarContextStatement(string statement)
        {
            // Interpolated expressions are deliberately outside this lightweight lexer's proof.
            // Fail closed instead of accepting an opaque "$\"...{value}...\"" as a literal-only row.
            if (Regex.IsMatch(statement, @"(?:\$@|@\$|\$)\s*"""))
            {
                return false;
            }

            string withoutStrings = CSharpLexicalMask.MaskLiterals(statement);
            if (withoutStrings.IndexOf('+') < 0)
            {
                return true;
            }

            if (Regex.IsMatch(
                withoutStrings,
                @"\b(?:GameContextValue|Fallback|Clean|Safe|Label|Name|Description|Reason|Source|Target|Title|Text)\b",
                RegexOptions.IgnoreCase))
            {
                return false;
            }

            // Numeric conversions and boolean/schema-token ternaries cannot inject a field delimiter.
            return Regex.IsMatch(
                    withoutStrings,
                    @"\b(?:ToString|Math\s*\.|Count|Length|Tick|Day|Year|Index|Level|Age|true|false)\b",
                    RegexOptions.IgnoreCase)
                && !Regex.IsMatch(
                    withoutStrings,
                    @"\+\s*[A-Za-z_][A-Za-z0-9_]*(?:\s*[;,)])");
        }

        private static HashSet<string> FindSanitizerWrappers(string source)
        {
            HashSet<string> wrappers = new HashSet<string>(StringComparer.Ordinal);
            List<Match> methods = MethodDeclaration.Matches(source)
                .Cast<Match>()
                .ToList();
            Dictionary<string, string> bodies =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < methods.Count; index++)
            {
                Match method = methods[index];
                int end = index + 1 < methods.Count ? methods[index + 1].Index : source.Length;
                string body = source.Substring(method.Index, end - method.Index);
                bodies[method.Groups["name"].Value] = body;
                if (SanitizerCall.IsMatch(body) || HasLocalDelimiterFirewall(body))
                {
                    wrappers.Add(method.Groups["name"].Value);
                }
            }

            bool added;
            do
            {
                added = false;
                foreach (KeyValuePair<string, string> method in bodies)
                {
                    if (wrappers.Contains(method.Key))
                    {
                        continue;
                    }

                    if (wrappers.Any(wrapper => Regex.IsMatch(
                        method.Value,
                        @"\b" + Regex.Escape(wrapper) + @"\s*\(")))
                    {
                        wrappers.Add(method.Key);
                        added = true;
                    }
                }
            }
            while (added);

            return wrappers;
        }

        private static HashSet<string> FindScalarIdentifiers(
            string source,
            string method,
            int statementPosition)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(method))
            {
                return result;
            }

            List<Match> declarations = MethodDeclaration.Matches(source)
                .Cast<Match>()
                .ToList();
            int ownerIndex = -1;
            for (int index = 0; index < declarations.Count; index++)
            {
                Match declaration = declarations[index];
                if (statementPosition >= 0)
                {
                    if (declaration.Index <= statementPosition)
                    {
                        ownerIndex = index;
                        continue;
                    }

                    break;
                }

                if (declaration.Groups["name"].Value.Equals(
                    method,
                    StringComparison.Ordinal))
                {
                    ownerIndex = index;
                    break;
                }
            }

            if (ownerIndex < 0)
            {
                return result;
            }

            Match owner = declarations[ownerIndex];
            int ownerEnd = ownerIndex + 1 < declarations.Count
                ? declarations[ownerIndex + 1].Index
                : source.Length;
            AddScalarDeclarations(
                source.Substring(owner.Index, ownerEnd - owner.Index),
                result);
            return result;
        }

        private static void AddScalarDeclarations(
            string source,
            HashSet<string> destination)
        {
            foreach (Match match in Regex.Matches(
                source,
                @"\b(?:bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal)\s+"
                    + @"(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant))
            {
                destination.Add(match.Groups["name"].Value);
            }
        }

        private static HashSet<string> FindSanitizedLocals(
            string source,
            HashSet<string> sanitizerWrappers)
        {
            List<string> calls = new List<string>
            {
                @"GameContextValue\s*\.\s*Sanitize",
            };
            calls.AddRange(sanitizerWrappers.Select(Regex.Escape));
            string callPattern = "(?:" + string.Join("|", calls.ToArray()) + ")";
            Regex assignment = new Regex(
                @"\b(?:string\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
                    + callPattern + @"\s*\(",
                RegexOptions.CultureInvariant);
            HashSet<string> locals = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in assignment.Matches(source))
            {
                locals.Add(match.Groups["name"].Value);
            }

            // A closed-token predicate followed by normalized input is a typed formatter, not free
            // text. PollutionContextFormatter is the canonical shape.
            foreach (Match match in Regex.Matches(
                source,
                @"\bstring\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*Known[A-Za-z0-9_]*\s*\(",
                RegexOptions.CultureInvariant))
            {
                locals.Add(match.Groups["name"].Value);
            }

            return locals;
        }

        private static bool UsesOnlyProvenContextValues(
            string statement,
            HashSet<string> sanitizerWrappers,
            HashSet<string> sanitizedLocals)
        {
            string withoutStrings = CSharpLexicalMask.MaskLiterals(statement);
            MatchCollection appendedValues = Regex.Matches(
                withoutStrings,
                @"\+\s*(?<value>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);
            if (appendedValues.Count == 0)
            {
                return false;
            }

            foreach (Match match in appendedValues)
            {
                string value = match.Groups["value"].Value;
                if (sanitizedLocals.Contains(value)
                    || Regex.IsMatch(
                        value,
                        @"(?:Count|Length|Tick|Day|Year|Index|Level|Age|Duration|Chance|Percent|Number|Total|Selected|Candidate|Entries|Memory|Major|Forced)$",
                        RegexOptions.IgnoreCase)
                    || IsScalarMemberAfter(withoutStrings, match.Groups["value"])
                    || IsQualifiedSchemaMember(withoutStrings, match.Groups["value"])
                    || (value == "GameContextValue"
                        && CallsAfter(withoutStrings, match.Groups["value"], "Sanitize"))
                    || (sanitizerWrappers.Contains(value)
                        && CallsAfter(withoutStrings, match.Groups["value"], null)))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsScalarMemberAfter(string source, Group identifier)
        {
            int index = identifier.Index + identifier.Length;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index >= source.Length || source[index] != '.')
            {
                return false;
            }

            index++;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            Match member = Regex.Match(
                source.Substring(index),
                @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);
            return member.Success
                && Regex.IsMatch(
                    member.Groups["name"].Value,
                    @"(?:Count|Length|Tick|Day|Year|Index|Level|Age|Duration|Chance|Percent|Number|Total|Offset)$",
                    RegexOptions.IgnoreCase);
        }

        private static bool IsQualifiedSchemaMember(string source, Group identifier)
        {
            int index = identifier.Index + identifier.Length;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            return char.IsUpper(identifier.Value[0])
                && index < source.Length
                && source[index] == '.';
        }

        private static bool CallsAfter(string source, Group identifier, string member)
        {
            int index = identifier.Index + identifier.Length;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (member != null)
            {
                string suffix = "." + member;
                if (index + suffix.Length > source.Length
                    || !source.Substring(index, suffix.Length).Equals(
                        suffix,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                index += suffix.Length;
                while (index < source.Length && char.IsWhiteSpace(source[index]))
                {
                    index++;
                }
            }

            return index < source.Length && source[index] == '(';
        }

        private static bool HasLocalDelimiterFirewall(string source)
        {
            return Regex.IsMatch(
                    source,
                    @"Replace\s*\(\s*';'\s*,\s*','\s*\)",
                    RegexOptions.CultureInvariant)
                && Regex.IsMatch(
                    source,
                    @"Replace\s*\(\s*'='\s*,\s*'-'\s*\)",
                    RegexOptions.CultureInvariant);
        }

        private static string MethodNameAt(string source, int position)
        {
            string name = string.Empty;
            foreach (Match match in MethodDeclaration.Matches(source))
            {
                if (match.Index > position)
                {
                    break;
                }

                name = match.Groups["name"].Value;
            }

            return name;
        }

        private static void TestLexicalMasking()
        {
            string sample =
                "using System;\n"
                + "// Verse.Rand.Value\n"
                + "string text = \"RimWorld DefDatabase<Pawn>\";\n"
                + "char c = 'V';\n"
                + "/* UnityEngine and Scribe */\n"
                + "PawnDiaryMod.Settings.ToString();\n";
            string masked = CSharpLexicalMask.MaskCommentsAndLiterals(sample);
            Require("comments are masked", masked.IndexOf("Verse", StringComparison.Ordinal) < 0);
            Require("string literals are masked", masked.IndexOf("RimWorld", StringComparison.Ordinal) < 0);
            Require("block comments are masked", masked.IndexOf("UnityEngine", StringComparison.Ordinal) < 0);
            Require("real code remains", masked.IndexOf("PawnDiaryMod", StringComparison.Ordinal) >= 0);
            Require(
                "mask preserves line count",
                masked.Count(character => character == '\n') == sample.Count(character => character == '\n'));
        }

        private static void TestDependencyExamples()
        {
            Require("Verse example is forbidden", MatchesForbidden("using Verse;"));
            Require("Scribe example is forbidden", MatchesForbidden("Scribe_Values.Look(ref value, \"x\");"));
            Require("filesystem example is forbidden", MatchesForbidden("File.ReadAllText(path);"));
            Require("network example is forbidden", MatchesForbidden("new HttpClient();"));
            Require("global RNG example is forbidden", MatchesForbidden("Rand.Value"));
            Require("System.Random construction is forbidden", MatchesForbidden("new Random(42);"));
            Require(
                "System.Random parameter type is forbidden",
                MatchesForbidden("int Pick(System.Random random) { return 0; }"));
            Require("global settings example is forbidden", MatchesForbidden("PawnDiaryMod.Settings.foo"));
            Require("plain DTO code is accepted", !MatchesForbidden("return Math.Max(0, snapshot.count);"));
        }

        private static void TestContextWriterExamples()
        {
            HashSet<string> noWrappers = new HashSet<string>(StringComparer.Ordinal);
            Require(
                "unsanitized context writer is rejected",
                !IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += \"; label=\" + label;",
                    string.Empty,
                    noWrappers,
                    noWrappers));
            Require(
                "shared sanitizer is accepted",
                IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += \"; label=\" + GameContextValue.Sanitize(label);",
                    string.Empty,
                    noWrappers,
                    noWrappers));
            Require(
                "one sanitizer cannot bless a sibling raw value",
                !IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += \"; label=\" + GameContextValue.Sanitize(label) + \"; source=\" + rawSource;",
                    string.Empty,
                    noWrappers,
                    noWrappers));
            Require(
                "scalar context writer is accepted",
                IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += \"; day=\" + day.ToString();",
                    string.Empty,
                    noWrappers,
                    noWrappers));
            Require(
                "interpolated dynamic context writer fails closed",
                !IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += $\"; label={label}\";",
                    string.Empty,
                    noWrappers,
                    noWrappers));
            string crossMethodSource =
                "private void ScalarOwner() { int label = 1; }\n"
                + "private void BuildGameContext() { string label = GetLabel(); "
                + "context += \"; label=\" + label; }";
            int writerPosition = crossMethodSource.IndexOf(
                "context +=",
                StringComparison.Ordinal);
            Require(
                "a scalar name in another method cannot bless a raw string local",
                !IsTrustedContextStatement(
                    "Source/Capture/Events/ExampleEventData.cs",
                    "BuildGameContext",
                    "context += \"; label=\" + label;",
                    crossMethodSource,
                    noWrappers,
                    noWrappers,
                    writerPosition));
            Require(
                "unrelated prose is not a context writer",
                !LooksLikeStructuredContextWriter(
                    "Source/Pipeline/Example.cs",
                    "return text + \"; reason=\" + reason;"));
        }

        private static void ValidateAllowlists()
        {
            foreach (KeyValuePair<string, string> row in IntentionallyImpureProjects)
            {
                Require("impure project allowlist is exact", !HasWildcard(row.Key));
                Require("impure project allowlist rationale is present", !string.IsNullOrWhiteSpace(row.Value));
            }

            foreach (KeyValuePair<string, string> row in AllowedLinkedDependencySymbols)
            {
                Require(
                    "dependency allowlist is file/symbol/rule specific",
                    row.Key.Split('#').Length == 3 && !HasWildcard(row.Key));
                Require("dependency allowlist rationale is present", !string.IsNullOrWhiteSpace(row.Value));
            }

            foreach (KeyValuePair<string, string> row in TrustedContextFormatters)
            {
                Require(
                    "context allowlist is file/method specific",
                    row.Key.Split('#').Length == 2 && !HasWildcard(row.Key));
                Require("context allowlist rationale is present", !string.IsNullOrWhiteSpace(row.Value));
            }
        }

        private static void ValidateAllowlistUsage()
        {
            RequireSetEquals(
                "every impure-project exemption is exercised",
                UsedImpureProjectRows,
                IntentionallyImpureProjects.Keys);
            RequireSetEquals(
                "every dependency exemption is exercised",
                UsedDependencyRows,
                AllowedLinkedDependencySymbols.Keys);
            RequireSetEquals(
                "every typed-context exemption is exercised",
                UsedContextFormatterRows,
                TrustedContextFormatters.Keys);
        }

        private static void RequireSetEquals(
            string name,
            HashSet<string> actual,
            IEnumerable<string> expectedValues)
        {
            assertions++;
            HashSet<string> expected = new HashSet<string>(
                expectedValues,
                StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(expected))
            {
                string missing = string.Join(
                    ", ",
                    expected.Except(actual, StringComparer.OrdinalIgnoreCase).OrderBy(value => value));
                string unexpected = string.Join(
                    ", ",
                    actual.Except(expected, StringComparer.OrdinalIgnoreCase).OrderBy(value => value));
                throw new InvalidOperationException(
                    "Self-check failed: " + name + ". Missing=[" + missing
                        + "], unexpected=[" + unexpected + "].");
            }
        }

        private static bool MatchesForbidden(string source)
        {
            string masked = CSharpLexicalMask.MaskCommentsAndLiterals(source);
            return ForbiddenSourceRules.Any(rule => rule.pattern.IsMatch(masked));
        }

        private static DependencyRule Rule(string name, string pattern)
        {
            return new DependencyRule(
                name,
                new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));
        }

        private static void AddXmlViolation(
            string relativeProject,
            XElement element,
            string rule,
            string detail,
            List<Violation> violations)
        {
            IXmlLineInfo lineInfo = (IXmlLineInfo)element;
            violations.Add(new Violation(
                relativeProject,
                lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                rule,
                detail));
        }

        private static bool HasWildcard(string path)
        {
            return path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0;
        }

        private static bool LooksLikeProductionLink(string include)
        {
            string normalized = (include ?? string.Empty).Replace('\\', '/');
            return normalized.StartsWith("../", StringComparison.Ordinal)
                || normalized.IndexOf("/Source/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.StartsWith("Source/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("integrations/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWithinDirectory(string filePath, string directory)
        {
            string normalizedDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(filePath).StartsWith(
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Source", "PawnDiary.csproj"))
                    && Directory.Exists(Path.Combine(current.FullName, "tests")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Source", "PawnDiary.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate the Pawn Diary repository root.");
        }

        private static string RelativePath(string root, string path)
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }

        private static int LineAt(string text, int position)
        {
            int line = 1;
            int bounded = Math.Min(Math.Max(0, position), text.Length);
            for (int index = 0; index < bounded; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string OneLine(string text, int start, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int boundedStart = Math.Min(Math.Max(0, start), text.Length);
            int boundedLength = Math.Min(Math.Max(0, length), text.Length - boundedStart);
            string value = text.Substring(boundedStart, boundedLength);
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value.Length <= 220 ? value : value.Substring(0, 217) + "...";
        }

        private static void Require(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("Self-check failed: " + name + ".");
            }
        }

        private sealed class DependencyRule
        {
            public readonly string name;
            public readonly Regex pattern;

            public DependencyRule(string name, Regex pattern)
            {
                this.name = name;
                this.pattern = pattern;
            }
        }

        private sealed class Violation
        {
            public readonly string path;
            public readonly int line;
            public readonly string rule;
            public readonly string detail;

            public Violation(string path, int line, string rule, string detail)
            {
                this.path = path;
                this.line = line;
                this.rule = rule;
                this.detail = detail;
            }
        }

        private sealed class ProjectAuditResult
        {
            public readonly int projectCount;
            public readonly int productionFileCount;

            public ProjectAuditResult(int projectCount, int productionFileCount)
            {
                this.projectCount = projectCount;
                this.productionFileCount = productionFileCount;
            }
        }
    }

    /// <summary>
    /// Minimal lexer used only to distinguish executable C# from comments/literals and to split source
    /// into semicolon-terminated statements. It intentionally does not try to parse preprocessor branches,
    /// raw string literals, or interpolated-string expressions; those limitations are reported above.
    /// </summary>
    internal static class CSharpLexicalMask
    {
        public static string MaskCommentsAndLiterals(string source)
        {
            return Transform(source, true, true);
        }

        public static string MaskComments(string source)
        {
            return Transform(source, true, false);
        }

        public static string MaskLiterals(string source)
        {
            return Transform(source, false, true);
        }

        public static IEnumerable<SourceStatement> Statements(string source)
        {
            int start = 0;
            LexState state = LexState.Code;
            for (int index = 0; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : '\0';
                UpdateState(source, ref index, current, next, ref state);
                if (state == LexState.Code && current == ';')
                {
                    yield return new SourceStatement(start, source.Substring(start, index - start + 1));
                    start = index + 1;
                }
            }
        }

        private static string Transform(
            string source,
            bool maskComments,
            bool maskLiterals)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            StringBuilder output = new StringBuilder(source);
            LexState state = LexState.Code;
            for (int index = 0; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : '\0';
                LexState before = state;
                int originalIndex = index;
                UpdateState(source, ref index, current, next, ref state);

                bool comment = before == LexState.LineComment || before == LexState.BlockComment
                    || (before == LexState.Code
                        && (state == LexState.LineComment || state == LexState.BlockComment));
                bool literal = before == LexState.String || before == LexState.VerbatimString
                    || before == LexState.Character
                    || (before == LexState.Code
                        && (state == LexState.String
                            || state == LexState.VerbatimString
                            || state == LexState.Character));
                if ((maskComments && comment) || (maskLiterals && literal))
                {
                    for (int masked = originalIndex; masked <= index; masked++)
                    {
                        if (output[masked] != '\r' && output[masked] != '\n')
                        {
                            output[masked] = ' ';
                        }
                    }
                }
            }

            return output.ToString();
        }

        private static void UpdateState(
            string source,
            ref int index,
            char current,
            char next,
            ref LexState state)
        {
            switch (state)
            {
                case LexState.Code:
                    if (current == '/' && next == '/')
                    {
                        state = LexState.LineComment;
                        index++;
                    }
                    else if (current == '/' && next == '*')
                    {
                        state = LexState.BlockComment;
                        index++;
                    }
                    else if (current == '@' && next == '"')
                    {
                        state = LexState.VerbatimString;
                        index++;
                    }
                    else if (current == '"')
                    {
                        state = LexState.String;
                    }
                    else if (current == '\'')
                    {
                        state = LexState.Character;
                    }
                    break;
                case LexState.LineComment:
                    if (current == '\n')
                    {
                        state = LexState.Code;
                    }
                    break;
                case LexState.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        state = LexState.Code;
                        index++;
                    }
                    break;
                case LexState.String:
                    if (current == '\\')
                    {
                        index = Math.Min(index + 1, source.Length - 1);
                    }
                    else if (current == '"')
                    {
                        state = LexState.Code;
                    }
                    break;
                case LexState.VerbatimString:
                    if (current == '"' && next == '"')
                    {
                        index++;
                    }
                    else if (current == '"')
                    {
                        state = LexState.Code;
                    }
                    break;
                case LexState.Character:
                    if (current == '\\')
                    {
                        index = Math.Min(index + 1, source.Length - 1);
                    }
                    else if (current == '\'')
                    {
                        state = LexState.Code;
                    }
                    break;
            }
        }

        private enum LexState
        {
            Code,
            LineComment,
            BlockComment,
            String,
            VerbatimString,
            Character,
        }
    }

    internal sealed class SourceStatement
    {
        public readonly int start;
        public readonly string text;

        public SourceStatement(int start, string text)
        {
            this.start = start;
            this.text = text;
        }
    }
}
