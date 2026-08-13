// Standalone source-contract tests for cosmetic RNG ownership. The harness inventories every
// production Verse.Rand and UnityEngine.Random draw by file + containing member, then verifies the
// PushState/PopState or Random.state restoration scopes that protect those draws. It deliberately
// references no RimWorld, Verse, Unity, Harmony, or Pawn Diary assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiaryRngIsolationContractTests
{
    internal static class Program
    {
        private const string SourcePrefix = "Source/";

        private static readonly Regex VerseAccess = new Regex(
            @"(?<![A-Za-z0-9_])(?:(?:Verse\s*\.\s*)?Rand)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex QualifiedUnityAccess = new Regex(
            @"(?<![A-Za-z0-9_.])UnityEngine\s*\.\s*Random\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        // The project currently spells Unity RNG references fully-qualified. Scanning the unqualified
        // spelling too makes a future `using UnityEngine; Random.Range(...)` addition fail closed.
        private static readonly Regex UnqualifiedUnityAccess = new Regex(
            @"(?<![A-Za-z0-9_.])Random\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> VerseNonDrawMembers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "PushState",
                "PopState",
            };

        private static readonly HashSet<string> UnityNonDrawMembers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                // UnityEngine.Random.State is the value type used to hold a snapshot; `state` is the
                // property read/write. Neither consumes a random value.
                "State",
                "state",
            };

        private static readonly VerseDrawContract[] VerseInventory =
        {
            OneShot(
                "Source/Defs/DiaryPersonaDef.cs",
                "RandomStartingPersona",
                MemberKind.Method,
                "Range",
                "new-pawn writing style is persisted as personaDefName"),
            OneShot(
                "Source/Defs/DiaryPersonaDef.cs",
                "WeightedStartingPersona",
                MemberKind.Method,
                "Range",
                "weighted new-pawn writing style is persisted as personaDefName"),
            OneShot(
                "Source/Defs/InteractionGroups.cs",
                "InstructionForGroup",
                MemberKind.Method,
                "Range",
                "chosen instruction variant is frozen in the captured event payload"),
            OneShot(
                "Source/Core/DiaryGameComponent.Dispatch.cs",
                "DecideFrequency",
                MemberKind.Method,
                "Value",
                "shared post-semantic frequency admission is frozen by the emitted-or-omitted event"),
            Stable(
                "Source/Generation/DiaryContextBuilder.cs",
                "ShouldMentionWeather",
                MemberKind.Method,
                "Chance",
                "event/writer-stable transient weather prompt choice"),
            Stable(
                "Source/Generation/HumorCues.cs",
                "CueFor",
                MemberKind.Method,
                "Chance",
                "event/writer/reroll-salt-stable humor gate"),
            StableOwnedBy(
                "Source/Generation/HumorCues.cs",
                "PickWeighted",
                MemberKind.Method,
                "Range",
                "Source/Generation/HumorCues.cs",
                "CueFor",
                MemberKind.Method,
                "event/writer/reroll-salt-stable humor cue selection"),
            Stable(
                "Source/Generation/PromptEnchantments.cs",
                "RuleFor",
                MemberKind.Method,
                "Range",
                "event/writer/reroll-salt-stable prompt-enchantment selection"),
            StableOwnedBy(
                "Source/Generation/PromptEnchantmentCollector.cs",
                "AddHediffCandidate",
                MemberKind.Method,
                "Range",
                "Source/Generation/PromptEnchantments.cs",
                "RuleFor",
                MemberKind.Method,
                "generation candidate gate; the public snapshot route is independently isolated"),
            StableOwnedBy(
                "Source/Generation/PromptEnchantmentCollector.cs",
                "AddImportantEventContextCandidate",
                MemberKind.Method,
                "Range",
                "Source/Generation/PromptEnchantments.cs",
                "RuleFor",
                MemberKind.Method,
                "generation candidate gate; the public snapshot route is independently isolated"),
            StableOwnedBy(
                "Source/Generation/PromptEnchantmentCollector.cs",
                "AddCapacityCandidate",
                MemberKind.Method,
                "Range",
                "Source/Generation/PromptEnchantments.cs",
                "RuleFor",
                MemberKind.Method,
                "generation candidate gate; the public snapshot route is independently isolated"),
            OneShot(
                "Source/Generation/PsychotypeRolls.cs",
                "Roll",
                MemberKind.Method,
                "Value",
                "new-pawn psychotype result is persisted in the diary record"),
            OneShot(
                "Source/Core/DiaryGameComponent.DaySummary.cs",
                "SelectHighlights",
                MemberKind.Method,
                "Value",
                "selected daily highlights are frozen in the dispatched reflection event"),
            OneShot(
                "Source/Core/DiaryGameComponent.DaySummary.cs",
                "SelectQuadrumHighlights",
                MemberKind.Method,
                "Value",
                "selected quadrum highlights are frozen in the dispatched reflection event"),
            OneShot(
                "Source/Core/DiaryGameComponent.InteractionBatching.cs",
                "ShouldPromoteInteraction",
                MemberKind.Method,
                "Chance",
                "promotion route is frozen by the emitted event payload"),
            OneShot(
                "Source/Core/DiaryGameComponent.InteractionBatching.cs",
                "FreezeInteractionAggregateFrequency",
                MemberKind.Method,
                "Value",
                "one aggregate admission result is frozen in its pending batch or pawn/day note"),
        };

        private static readonly UnityDrawContract[] UnityInventory =
        {
            new UnityDrawContract(
                "Source/Generation/DiaryContextBuilder.cs",
                "EquippedWeapon",
                MemberKind.Method,
                "Range",
                "EquippedWeapon snapshots and restores UnityEngine.Random.state around the draw",
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "EquippedWeapon",
                    MemberKind.Method,
                    1)),
            new UnityDrawContract(
                "Source/Generation/DiaryContextBuilder.cs",
                "PickWeightedThought",
                MemberKind.Method,
                "value",
                "both BuildTopThoughtsSummary callers snapshot and restore UnityEngine.Random.state",
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "CollectPawnSummaryFacts",
                    MemberKind.Method,
                    1),
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "CaptureMoodSnapshot",
                    MemberKind.Method,
                    1)),
        };

        private static readonly VerseGuardContract[] VerseGuards =
        {
            OneShotGuard(
                "Source/Defs/DiaryPersonaDef.cs",
                "RandomStartingPersona",
                MemberKind.Method,
                @"\bRand\s*\.\s*Range\s*\("),
            OneShotGuard(
                "Source/Defs/DiaryPersonaDef.cs",
                "WeightedStartingPersona",
                MemberKind.Method,
                @"\bRand\s*\.\s*Range\s*\("),
            OneShotGuard(
                "Source/Defs/InteractionGroups.cs",
                "InstructionForGroup",
                MemberKind.Method,
                @"\bRand\s*\.\s*Range\s*\("),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.Dispatch.cs",
                "DecideFrequency",
                MemberKind.Method,
                @"\bRand\s*\.\s*Value\b"),
            StableGuard(
                "Source/Generation/DiaryContextBuilder.cs",
                "ShouldMentionWeather",
                MemberKind.Method,
                @"\bRand\s*\.\s*Chance\s*\("),
            StableGuard(
                "Source/Generation/HumorCues.cs",
                "CueFor",
                MemberKind.Method,
                @"\bRand\s*\.\s*Chance\s*\(",
                @"\bPickWeighted\s*\("),
            StableGuard(
                "Source/Generation/PromptEnchantments.cs",
                "RuleFor",
                MemberKind.Method,
                @"\bPromptEnchantmentCollector\s*\.\s*Collect\s*\(",
                @"\bRand\s*\.\s*Range\s*\("),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.IntegrationSnapshots.cs",
                "PromptEnchantmentCandidatesFor",
                MemberKind.Method,
                @"\bPromptEnchantmentCollector\s*\.\s*Collect\s*\("),
            OneShotGuard(
                "Source/Generation/PsychotypeRolls.cs",
                "Roll",
                MemberKind.Method,
                @"\bRand\s*\.\s*Value\b"),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.DaySummary.cs",
                "SelectHighlights",
                MemberKind.Method,
                @"\bRand\s*\.\s*Value\b"),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.DaySummary.cs",
                "SelectQuadrumHighlights",
                MemberKind.Method,
                @"\bRand\s*\.\s*Value\b"),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.InteractionBatching.cs",
                "ShouldPromoteInteraction",
                MemberKind.Method,
                @"\bRand\s*\.\s*Chance\s*\("),
            OneShotGuard(
                "Source/Core/DiaryGameComponent.InteractionBatching.cs",
                "FreezeInteractionAggregateFrequency",
                MemberKind.Method,
                @"\bRand\s*\.\s*Value\b"),
        };

        private static readonly UnityGuardContract[] UnityGuards =
        {
            new UnityGuardContract(
                "Source/Generation/DiaryContextBuilder.cs",
                "EquippedWeapon",
                MemberKind.Method,
                @"\bUnityEngine\s*\.\s*Random\s*\.\s*Range\s*\("),
            new UnityGuardContract(
                "Source/Generation/DiaryContextBuilder.cs",
                "CollectPawnSummaryFacts",
                MemberKind.Method,
                @"\bBuildTopThoughtsSummary\s*\("),
            new UnityGuardContract(
                "Source/Generation/DiaryContextBuilder.cs",
                "CaptureMoodSnapshot",
                MemberKind.Method,
                @"\bBuildTopThoughtsSummary\s*\("),
        };

        // These contracts close the gap where a draw helper is protected by its caller. They also
        // ensure a future unguarded caller cannot silently bypass the owner recorded above.
        private static readonly CallRouteContract[] CallRoutes =
        {
            new CallRouteContract(
                "Source/Generation/HumorCues.cs",
                @"\bPickWeighted\s*\(",
                2,
                Use("Source/Generation/HumorCues.cs", "CueFor", MemberKind.Method, 1)),
            new CallRouteContract(
                null,
                @"\bPromptEnchantmentCollector\s*\.\s*Collect\s*\(",
                2,
                Use("Source/Generation/PromptEnchantments.cs", "RuleFor", MemberKind.Method, 1),
                Use(
                    "Source/Core/DiaryGameComponent.IntegrationSnapshots.cs",
                    "PromptEnchantmentCandidatesFor",
                    MemberKind.Method,
                    1)),
            new CallRouteContract(
                "Source/Generation/PromptEnchantmentCollector.cs",
                @"\bAddHediffCandidate\s*\(",
                2,
                Use(
                    "Source/Generation/PromptEnchantmentCollector.cs",
                    "Collect",
                    MemberKind.Method,
                    1)),
            new CallRouteContract(
                "Source/Generation/PromptEnchantmentCollector.cs",
                @"\bAddImportantEventContextCandidate\s*\(",
                2,
                Use(
                    "Source/Generation/PromptEnchantmentCollector.cs",
                    "Collect",
                    MemberKind.Method,
                    1)),
            new CallRouteContract(
                "Source/Generation/PromptEnchantmentCollector.cs",
                @"\bAddCapacityCandidate\s*\(",
                2,
                Use(
                    "Source/Generation/PromptEnchantmentCollector.cs",
                    "Collect",
                    MemberKind.Method,
                    1)),
            new CallRouteContract(
                "Source/Generation/DiaryContextBuilder.cs",
                @"\bBuildTopThoughtsSummary\s*\(",
                3,
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "CollectPawnSummaryFacts",
                    MemberKind.Method,
                    1),
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "CaptureMoodSnapshot",
                    MemberKind.Method,
                    1)),
            new CallRouteContract(
                "Source/Generation/DiaryContextBuilder.cs",
                @"\bPickWeightedThought\s*\(",
                3,
                Use(
                    "Source/Generation/DiaryContextBuilder.cs",
                    "BuildTopThoughtsSummary",
                    MemberKind.Method,
                    2)),
        };

        private static int assertions;

        private static int Main()
        {
            string root = FindRepositoryRoot();
            SourceCorpus corpus = SourceCorpus.Load(root);

            TestProjectHasNoGameAssemblyReferences(root);
            TestScannerIgnoresCommentsAndStrings();
            TestForbiddenRngUsingFormsFailClosed();
            TestProductionHasNoRngUsingBypasses(corpus);
            TestInteractionFrequencyOwnership(corpus);
            TestManifestMetadata(corpus);
            TestDrawInventory(corpus);
            TestVerseRestorationScopes(corpus);
            TestUnityRestorationScopes(corpus);
            TestTransitiveCallRoutes(corpus);

            Console.WriteLine(
                "DiaryRngIsolationContractTests passed " + assertions
                + " assertions; inventoried " + VerseInventory.Length
                + " Verse Rand draws and " + UnityInventory.Length
                + " UnityEngine.Random draws.");
            return 0;
        }

        private static void TestProjectHasNoGameAssemblyReferences(string root)
        {
            string project = File.ReadAllText(Path.Combine(
                root,
                "tests",
                "DiaryRngIsolationContractTests",
                "DiaryRngIsolationContractTests.csproj"));
            AssertTrue(
                "contract project has no assembly/package/project references",
                !Regex.IsMatch(
                    project,
                    @"<(?:Reference|PackageReference|ProjectReference|Compile)\b",
                    RegexOptions.IgnoreCase));
        }

        private static void TestScannerIgnoresCommentsAndStrings()
        {
            const string sample =
                "class Sample {\n"
                + "  void Owned() { var text = \"Rand.Value UnityEngine.Random.value\"; "
                + "/* Rand.Chance(1f); */ Rand.Gaussian(); }\n"
                + "  // UnityEngine.Random.Range(0, 2)\n"
                + "  void UnityDraw() { var value = Random.Range(0, 2); }\n"
                + "}";
            string sanitized = SourceText.Sanitize(sample);
            List<DrawSite> draws = DiscoverDraws("Source/Sample.cs", sanitized);
            AssertEqual("scanner ignores comments and strings", 2, draws.Count);
            AssertEqual("scanner conservatively catches future Verse APIs", "Gaussian", draws[0].Member);
            AssertEqual("scanner catches unqualified Unity API", "Range", draws[1].Member);
        }

        private static void TestForbiddenRngUsingFormsFailClosed()
        {
            string[] forbidden =
            {
                "using static Verse.Rand;",
                "using static UnityEngine.Random;",
                "global using static global::Verse.Rand;",
                "global using static global::UnityEngine.Random;",
                "using DiaryRand = Verse.Rand;",
                "using DiaryRandom = UnityEngine.Random;",
                "global using DiaryRand = global::Verse.Rand;",
                "global using DiaryRandom = global::UnityEngine.Random;",
                // Short targets are rejected conservatively too. They can resolve to the RNG types
                // through another using directive, and proving their semantic target would require
                // compiling against the very game assemblies this project intentionally excludes.
                "using static Rand;",
                "using static Random;",
                "using DiaryRand = Rand;",
                "using DiaryRandom = Random;",
            };

            for (int i = 0; i < forbidden.Length; i++)
            {
                string sample = forbidden[i] + "\ninternal static class Sample { }";
                List<ForbiddenUsing> found =
                    DiscoverForbiddenRngUsings("Source/Sample.cs", SourceText.Sanitize(sample));
                AssertEqual(
                    "RNG using bypass fails closed: " + forbidden[i],
                    1,
                    found.Count);
            }

            const string safe =
                "using System;\n"
                + "using Text = System.String;\n"
                + "using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;\n"
                + "internal static class Sample { }";
            AssertEqual(
                "ordinary imports and non-RNG aliases remain allowed",
                0,
                DiscoverForbiddenRngUsings(
                    "Source/Sample.cs",
                    SourceText.Sanitize(safe)).Count);
        }

        private static void TestProductionHasNoRngUsingBypasses(SourceCorpus corpus)
        {
            List<ForbiddenUsing> forbidden = new List<ForbiddenUsing>();
            for (int i = 0; i < corpus.Files.Count; i++)
            {
                SourceFile file = corpus.Files[i];
                forbidden.AddRange(DiscoverForbiddenRngUsings(file.Path, file.Sanitized));
            }

            AssertTrue(
                "production source has no static RNG imports or RNG type aliases"
                    + FormatForbiddenUsings(forbidden, corpus),
                forbidden.Count == 0);
        }

        private static void TestInteractionFrequencyOwnership(SourceCorpus corpus)
        {
            SourceFile batching = corpus.File(
                "Source/Core/DiaryGameComponent.InteractionBatching.cs");
            string promotion = corpus.Member(
                batching.Path,
                "ShouldPromoteInteraction",
                MemberKind.Method).Text(batching.Sanitized);
            AssertTrue(
                "interaction promotion is native routing, not the retired global weight",
                promotion.IndexOf("generationChanceWeight", StringComparison.Ordinal) < 0
                    && promotion.IndexOf(
                        "ClampGenerationChanceWeight", StringComparison.Ordinal) < 0);

            SourceFile signal = corpus.File(
                "Source/Ingestion/Sources/InteractionSignal.cs");
            string context = corpus.Member(
                signal.Path,
                "BuildContext",
                MemberKind.Method).Text(signal.Sanitized);
            AssertTrue(
                "interaction context names the classified frequency group",
                Regex.IsMatch(context, @"frequencyGroup\s*:\s*classifiedGroup"));
            AssertTrue(
                "interaction context defers aggregate contributor admission",
                Regex.IsMatch(context, @"bypassFrequency\s*:\s*deferredAggregate"));

            AssertTrue(
                "pair and ambient pending state freeze exact aggregate admission",
                Regex.Matches(
                    batching.Sanitized,
                    @"frequencyAdmissionAccepted\s*=\s*FreezeInteractionAggregateFrequency\s*\(")
                    .Count == 2);
        }

        private static void TestManifestMetadata(SourceCorpus corpus)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < VerseInventory.Length; i++)
            {
                VerseDrawContract row = VerseInventory[i];
                AssertTrue("Verse row path is production source: " + row.Key, row.File.StartsWith(SourcePrefix));
                AssertTrue("Verse row owner note is non-blank: " + row.Key, !string.IsNullOrWhiteSpace(row.Note));
                AssertTrue(
                    "Verse row has an explicit owner: " + row.Key,
                    !string.IsNullOrWhiteSpace(row.OwnerFile)
                        && !string.IsNullOrWhiteSpace(row.OwnerSymbol));
                AssertTrue(
                    "Verse owner has a matching restoration contract: " + row.Key,
                    VerseGuards.Any(guard =>
                        guard.File == row.OwnerFile
                        && guard.Symbol == row.OwnerSymbol
                        && guard.Kind == row.OwnerMemberKind
                        && guard.OwnerKind == row.OwnerKind));
                AssertTrue(
                    "Verse owner member resolves: " + row.Key,
                    corpus.Member(row.OwnerFile, row.OwnerSymbol, row.OwnerMemberKind) != null);
                AssertTrue("Verse manifest rows are unique: " + row.Key, keys.Add(row.Key));
            }

            keys.Clear();
            for (int i = 0; i < UnityInventory.Length; i++)
            {
                UnityDrawContract row = UnityInventory[i];
                AssertTrue("Unity row path is production source: " + row.Key, row.File.StartsWith(SourcePrefix));
                AssertTrue(
                    "Unity row restoration note is non-blank: " + row.Key,
                    !string.IsNullOrWhiteSpace(row.Restoration));
                AssertTrue(
                    "Unity row declares one or more restoration owners: " + row.Key,
                    row.RestorationOwners.Length > 0);
                for (int ownerIndex = 0; ownerIndex < row.RestorationOwners.Length; ownerIndex++)
                {
                    AllowedUse owner = row.RestorationOwners[ownerIndex];
                    AssertTrue(
                        "Unity owner has a matching restoration contract: " + row.Key
                            + "::" + owner.Symbol,
                        UnityGuards.Any(guard =>
                            guard.File == owner.File
                            && guard.Symbol == owner.Symbol
                            && guard.Kind == owner.Kind));
                    AssertTrue(
                        "Unity owner member resolves: " + row.Key + "::" + owner.Symbol,
                        corpus.Member(owner.File, owner.Symbol, owner.Kind) != null);
                }

                AssertTrue("Unity manifest rows are unique: " + row.Key, keys.Add(row.Key));
            }
        }

        private static void TestDrawInventory(SourceCorpus corpus)
        {
            List<DrawSite> discovered = new List<DrawSite>();
            foreach (SourceFile file in corpus.Files)
            {
                discovered.AddRange(DiscoverDraws(file.Path, file.Sanitized));
            }

            HashSet<DrawSite> matched = new HashSet<DrawSite>();
            foreach (IGrouping<string, VerseDrawContract> group in VerseInventory.GroupBy(
                row => row.DirectKey,
                StringComparer.Ordinal))
            {
                VerseDrawContract row = group.First();
                MemberScope scope = corpus.Member(row.File, row.Symbol, row.Kind);
                List<DrawSite> actual = discovered.Where(site =>
                    site.Engine == RngEngine.Verse
                    && site.File == row.File
                    && site.Member == row.ApiMember
                    && scope.Contains(site.Index)).ToList();
                AssertEqual(
                    "Verse inventory count for " + row.DirectKey,
                    group.Count(),
                    actual.Count);
                for (int i = 0; i < actual.Count; i++)
                {
                    matched.Add(actual[i]);
                }
            }

            foreach (IGrouping<string, UnityDrawContract> group in UnityInventory.GroupBy(
                row => row.DirectKey,
                StringComparer.Ordinal))
            {
                UnityDrawContract row = group.First();
                MemberScope scope = corpus.Member(row.File, row.Symbol, row.Kind);
                List<DrawSite> actual = discovered.Where(site =>
                    site.Engine == RngEngine.Unity
                    && site.File == row.File
                    && site.Member == row.ApiMember
                    && scope.Contains(site.Index)).ToList();
                AssertEqual(
                    "Unity inventory count for " + row.DirectKey,
                    group.Count(),
                    actual.Count);
                for (int i = 0; i < actual.Count; i++)
                {
                    matched.Add(actual[i]);
                }
            }

            List<DrawSite> unowned = discovered.Where(site => !matched.Contains(site)).ToList();
            AssertTrue(
                "every production RNG draw has an inventory owner"
                    + FormatSites(unowned, corpus),
                unowned.Count == 0);
            AssertEqual(
                "discovered Verse draw count",
                VerseInventory.Length,
                discovered.Count(site => site.Engine == RngEngine.Verse));
            AssertEqual(
                "discovered Unity draw count",
                UnityInventory.Length,
                discovered.Count(site => site.Engine == RngEngine.Unity));
        }

        private static void TestVerseRestorationScopes(SourceCorpus corpus)
        {
            for (int i = 0; i < VerseGuards.Length; i++)
            {
                VerseGuardContract contract = VerseGuards[i];
                SourceFile file = corpus.File(contract.File);
                MemberScope scope = corpus.Member(contract.File, contract.Symbol, contract.Kind);
                string body = scope.Text(file.Sanitized);

                int pushCount = Regex.Matches(body, @"\bRand\s*\.\s*PushState\s*\(").Count;
                int popCount = Regex.Matches(body, @"\bRand\s*\.\s*PopState\s*\(").Count;
                AssertEqual("balanced PushState/PopState in " + contract.Key, pushCount, popCount);
                AssertTrue("at least one Rand state scope in " + contract.Key, pushCount > 0);

                if (contract.OwnerKind == VerseOwnerKind.StableSeededGeneration)
                {
                    AssertTrue(
                        "stable guard uses canonical StableSeed in " + contract.Key,
                        Regex.IsMatch(
                            body,
                            @"\bRand\s*\.\s*PushState\s*\(\s*HumorChancePolicy\s*\.\s*StableSeed\s*\("));
                }
                else
                {
                    AssertTrue(
                        "one-shot guard uses isolated unseeded PushState in " + contract.Key,
                        Regex.IsMatch(body, @"\bRand\s*\.\s*PushState\s*\(\s*\)"));
                }

                for (int patternIndex = 0; patternIndex < contract.ProtectedPatterns.Length; patternIndex++)
                {
                    Regex protectedPattern = new Regex(contract.ProtectedPatterns[patternIndex]);
                    Match protectedSite = protectedPattern.Match(body);
                    AssertTrue(
                        "protected operation exists in " + contract.Key
                            + " (" + contract.ProtectedPatterns[patternIndex] + ")",
                        protectedSite.Success);
                    AssertTrue(
                        "protected operation is inside try/finally restoration in " + contract.Key,
                        protectedSite.Success
                            && IsProtectedByTryFinally(
                                body,
                                protectedSite.Index,
                                @"\bRand\s*\.\s*PushState\s*\(",
                                @"\bRand\s*\.\s*PopState\s*\("));
                }
            }
        }

        private static void TestUnityRestorationScopes(SourceCorpus corpus)
        {
            for (int i = 0; i < UnityGuards.Length; i++)
            {
                UnityGuardContract contract = UnityGuards[i];
                SourceFile file = corpus.File(contract.File);
                MemberScope scope = corpus.Member(contract.File, contract.Symbol, contract.Kind);
                string body = scope.Text(file.Sanitized);
                Match protectedSite = new Regex(contract.ProtectedPattern).Match(body);
                AssertTrue("Unity protected operation exists in " + contract.Key, protectedSite.Success);
                AssertTrue(
                    "Unity operation snapshots/restores Random.state in " + contract.Key,
                    protectedSite.Success
                        && IsProtectedByTryFinally(
                            body,
                            protectedSite.Index,
                            @"\bUnityEngine\s*\.\s*Random\s*\.\s*state\b",
                            @"\bUnityEngine\s*\.\s*Random\s*\.\s*state\s*="));
            }
        }

        private static void TestTransitiveCallRoutes(SourceCorpus corpus)
        {
            for (int i = 0; i < CallRoutes.Length; i++)
            {
                CallRouteContract route = CallRoutes[i];
                Regex pattern = new Regex(route.Pattern);
                int global = 0;
                if (route.File == null)
                {
                    for (int fileIndex = 0; fileIndex < corpus.Files.Count; fileIndex++)
                    {
                        global += pattern.Matches(corpus.Files[fileIndex].Sanitized).Count;
                    }
                }
                else
                {
                    global = pattern.Matches(corpus.File(route.File).Sanitized).Count;
                }

                AssertEqual("transitive route total for " + route.Pattern, route.ExpectedGlobalMatches, global);
                for (int useIndex = 0; useIndex < route.AllowedUses.Length; useIndex++)
                {
                    AllowedUse use = route.AllowedUses[useIndex];
                    SourceFile file = corpus.File(use.File);
                    MemberScope scope = corpus.Member(use.File, use.Symbol, use.Kind);
                    int actual = pattern.Matches(scope.Text(file.Sanitized)).Count;
                    AssertEqual(
                        "transitive route use in " + use.File + "::" + use.Symbol,
                        use.ExpectedMatches,
                        actual);
                }
            }
        }

        private static bool IsProtectedByTryFinally(
            string memberBody,
            int protectedIndex,
            string snapshotPattern,
            string restorePattern)
        {
            MatchCollection tries = Regex.Matches(memberBody, @"\btry\b");
            for (int i = 0; i < tries.Count; i++)
            {
                int tryOpen = SourceText.NextNonWhitespace(memberBody, tries[i].Index + tries[i].Length);
                if (tryOpen < 0 || memberBody[tryOpen] != '{')
                {
                    continue;
                }

                int tryClose = SourceText.MatchingBrace(memberBody, tryOpen);
                if (tryClose < 0 || protectedIndex <= tryOpen || protectedIndex >= tryClose)
                {
                    continue;
                }

                int afterTry = SourceText.NextNonWhitespace(memberBody, tryClose + 1);
                Match finallyMatch = Regex.Match(
                    memberBody.Substring(afterTry < 0 ? memberBody.Length : afterTry),
                    @"\Afinally\b");
                if (!finallyMatch.Success)
                {
                    continue;
                }

                int finallyKeyword = afterTry;
                int finallyOpen = SourceText.NextNonWhitespace(
                    memberBody,
                    finallyKeyword + finallyMatch.Length);
                if (finallyOpen < 0 || memberBody[finallyOpen] != '{')
                {
                    continue;
                }

                int finallyClose = SourceText.MatchingBrace(memberBody, finallyOpen);
                if (finallyClose < 0)
                {
                    continue;
                }

                string beforeTry = memberBody.Substring(0, tries[i].Index);
                string finallyBody = memberBody.Substring(
                    finallyOpen,
                    finallyClose - finallyOpen + 1);
                if (Regex.IsMatch(beforeTry, snapshotPattern)
                    && Regex.IsMatch(finallyBody, restorePattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<DrawSite> DiscoverDraws(string file, string sanitized)
        {
            List<DrawSite> draws = new List<DrawSite>();
            foreach (Match match in VerseAccess.Matches(sanitized))
            {
                string member = match.Groups[1].Value;
                if (!VerseNonDrawMembers.Contains(member))
                {
                    draws.Add(new DrawSite(file, RngEngine.Verse, member, match.Index));
                }
            }

            HashSet<int> unityStarts = new HashSet<int>();
            foreach (Match match in QualifiedUnityAccess.Matches(sanitized))
            {
                string member = match.Groups[1].Value;
                if (!UnityNonDrawMembers.Contains(member))
                {
                    draws.Add(new DrawSite(file, RngEngine.Unity, member, match.Index));
                    unityStarts.Add(match.Index);
                }
            }

            foreach (Match match in UnqualifiedUnityAccess.Matches(sanitized))
            {
                string member = match.Groups[1].Value;
                if (!UnityNonDrawMembers.Contains(member) && !unityStarts.Contains(match.Index))
                {
                    draws.Add(new DrawSite(file, RngEngine.Unity, member, match.Index));
                }
            }

            draws.Sort((left, right) => left.Index.CompareTo(right.Index));
            return draws;
        }

        private static List<ForbiddenUsing> DiscoverForbiddenRngUsings(
            string file,
            string sanitized)
        {
            List<ForbiddenUsing> forbidden = new List<ForbiddenUsing>();
            Regex directives = new Regex(
                @"(?m)^[ \t]*(?:global\s+)?using\s+(?<body>[^;\r\n]+)\s*;");
            foreach (Match match in directives.Matches(sanitized))
            {
                string body = Regex.Replace(match.Groups["body"].Value, @"\s+", string.Empty);
                string target = string.Empty;
                if (body.StartsWith("static", StringComparison.Ordinal))
                {
                    target = body.Substring("static".Length);
                }
                else
                {
                    int equals = body.IndexOf('=');
                    if (equals >= 0 && equals + 1 < body.Length)
                    {
                        target = body.Substring(equals + 1);
                    }
                }

                target = target.Replace("global::", string.Empty);
                string terminal = target;
                int finalDot = terminal.LastIndexOf('.');
                if (finalDot >= 0 && finalDot + 1 < terminal.Length)
                {
                    terminal = terminal.Substring(finalDot + 1);
                }

                // Exact terminal-name rejection intentionally errs on the conservative side. It
                // catches fully-qualified types, global:: types, and short types resolved through
                // another using without needing Verse/Unity metadata in this standalone project.
                if (string.Equals(terminal, "Rand", StringComparison.Ordinal)
                    || string.Equals(terminal, "Random", StringComparison.Ordinal))
                {
                    forbidden.Add(new ForbiddenUsing(file, match.Index, match.Value.Trim()));
                }
            }

            return forbidden;
        }

        private static string FormatSites(List<DrawSite> sites, SourceCorpus corpus)
        {
            if (sites.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(":");
            for (int i = 0; i < sites.Count; i++)
            {
                DrawSite site = sites[i];
                SourceFile file = corpus.File(site.File);
                result.Append("\n  ")
                    .Append(site.File)
                    .Append(':')
                    .Append(SourceText.LineNumber(file.Raw, site.Index))
                    .Append(" ")
                    .Append(site.Engine == RngEngine.Verse ? "Rand." : "UnityEngine.Random.")
                    .Append(site.Member);
            }

            return result.ToString();
        }

        private static string FormatForbiddenUsings(
            List<ForbiddenUsing> forbidden,
            SourceCorpus corpus)
        {
            if (forbidden.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(":");
            for (int i = 0; i < forbidden.Count; i++)
            {
                ForbiddenUsing item = forbidden[i];
                SourceFile file = corpus.File(item.File);
                result.Append("\n  ")
                    .Append(item.File)
                    .Append(':')
                    .Append(SourceText.LineNumber(file.Raw, item.Index))
                    .Append(' ')
                    .Append(item.Directive);
            }

            return result.ToString();
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Source", "PawnDiary.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the Pawn Diary repository root for RNG source-contract checks.");
        }

        private static VerseDrawContract OneShot(
            string file,
            string symbol,
            MemberKind kind,
            string apiMember,
            string note)
        {
            return new VerseDrawContract(
                file,
                symbol,
                kind,
                apiMember,
                VerseOwnerKind.IsolatedOneShotCapture,
                file,
                symbol,
                kind,
                note);
        }

        private static VerseDrawContract Stable(
            string file,
            string symbol,
            MemberKind kind,
            string apiMember,
            string note)
        {
            return StableOwnedBy(file, symbol, kind, apiMember, file, symbol, kind, note);
        }

        private static VerseDrawContract StableOwnedBy(
            string file,
            string symbol,
            MemberKind kind,
            string apiMember,
            string ownerFile,
            string ownerSymbol,
            MemberKind ownerKind,
            string note)
        {
            return new VerseDrawContract(
                file,
                symbol,
                kind,
                apiMember,
                VerseOwnerKind.StableSeededGeneration,
                ownerFile,
                ownerSymbol,
                ownerKind,
                note);
        }

        private static VerseGuardContract OneShotGuard(
            string file,
            string symbol,
            MemberKind kind,
            params string[] protectedPatterns)
        {
            return new VerseGuardContract(
                file,
                symbol,
                kind,
                VerseOwnerKind.IsolatedOneShotCapture,
                protectedPatterns);
        }

        private static VerseGuardContract StableGuard(
            string file,
            string symbol,
            MemberKind kind,
            params string[] protectedPatterns)
        {
            return new VerseGuardContract(
                file,
                symbol,
                kind,
                VerseOwnerKind.StableSeededGeneration,
                protectedPatterns);
        }

        private static AllowedUse Use(
            string file,
            string symbol,
            MemberKind kind,
            int expectedMatches)
        {
            return new AllowedUse(file, symbol, kind, expectedMatches);
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + name);
            }
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + name + "; expected '" + expected
                    + "', got '" + actual + "'.");
            }
        }

        private enum RngEngine
        {
            Verse,
            Unity,
        }

        private enum MemberKind
        {
            Method,
            Property,
        }

        private enum VerseOwnerKind
        {
            StableSeededGeneration,
            IsolatedOneShotCapture,
        }

        private sealed class VerseDrawContract
        {
            public VerseDrawContract(
                string file,
                string symbol,
                MemberKind kind,
                string apiMember,
                VerseOwnerKind ownerKind,
                string ownerFile,
                string ownerSymbol,
                MemberKind ownerMemberKind,
                string note)
            {
                File = file;
                Symbol = symbol;
                Kind = kind;
                ApiMember = apiMember;
                OwnerKind = ownerKind;
                OwnerFile = ownerFile;
                OwnerSymbol = ownerSymbol;
                OwnerMemberKind = ownerMemberKind;
                Note = note;
            }

            public string File { get; }
            public string Symbol { get; }
            public MemberKind Kind { get; }
            public string ApiMember { get; }
            public VerseOwnerKind OwnerKind { get; }
            public string OwnerFile { get; }
            public string OwnerSymbol { get; }
            public MemberKind OwnerMemberKind { get; }
            public string Note { get; }
            public string DirectKey => File + "::" + Symbol + "::Rand." + ApiMember;
            public string Key => DirectKey + "::" + OwnerKind + "::" + OwnerFile + "::" + OwnerSymbol;
        }

        private sealed class UnityDrawContract
        {
            public UnityDrawContract(
                string file,
                string symbol,
                MemberKind kind,
                string apiMember,
                string restoration,
                params AllowedUse[] restorationOwners)
            {
                File = file;
                Symbol = symbol;
                Kind = kind;
                ApiMember = apiMember;
                Restoration = restoration;
                RestorationOwners = restorationOwners;
            }

            public string File { get; }
            public string Symbol { get; }
            public MemberKind Kind { get; }
            public string ApiMember { get; }
            public string Restoration { get; }
            public AllowedUse[] RestorationOwners { get; }
            public string DirectKey => File + "::" + Symbol + "::UnityEngine.Random." + ApiMember;
            public string Key => DirectKey + "::" + Restoration;
        }

        private sealed class VerseGuardContract
        {
            public VerseGuardContract(
                string file,
                string symbol,
                MemberKind kind,
                VerseOwnerKind ownerKind,
                string[] protectedPatterns)
            {
                File = file;
                Symbol = symbol;
                Kind = kind;
                OwnerKind = ownerKind;
                ProtectedPatterns = protectedPatterns;
            }

            public string File { get; }
            public string Symbol { get; }
            public MemberKind Kind { get; }
            public VerseOwnerKind OwnerKind { get; }
            public string[] ProtectedPatterns { get; }
            public string Key => File + "::" + Symbol;
        }

        private sealed class UnityGuardContract
        {
            public UnityGuardContract(
                string file,
                string symbol,
                MemberKind kind,
                string protectedPattern)
            {
                File = file;
                Symbol = symbol;
                Kind = kind;
                ProtectedPattern = protectedPattern;
            }

            public string File { get; }
            public string Symbol { get; }
            public MemberKind Kind { get; }
            public string ProtectedPattern { get; }
            public string Key => File + "::" + Symbol;
        }

        private sealed class CallRouteContract
        {
            public CallRouteContract(
                string file,
                string pattern,
                int expectedGlobalMatches,
                params AllowedUse[] allowedUses)
            {
                File = file;
                Pattern = pattern;
                ExpectedGlobalMatches = expectedGlobalMatches;
                AllowedUses = allowedUses;
            }

            public string File { get; }
            public string Pattern { get; }
            public int ExpectedGlobalMatches { get; }
            public AllowedUse[] AllowedUses { get; }
        }

        private sealed class AllowedUse
        {
            public AllowedUse(
                string file,
                string symbol,
                MemberKind kind,
                int expectedMatches)
            {
                File = file;
                Symbol = symbol;
                Kind = kind;
                ExpectedMatches = expectedMatches;
            }

            public string File { get; }
            public string Symbol { get; }
            public MemberKind Kind { get; }
            public int ExpectedMatches { get; }
        }

        private sealed class DrawSite
        {
            public DrawSite(string file, RngEngine engine, string member, int index)
            {
                File = file;
                Engine = engine;
                Member = member;
                Index = index;
            }

            public string File { get; }
            public RngEngine Engine { get; }
            public string Member { get; }
            public int Index { get; }
        }

        private sealed class ForbiddenUsing
        {
            public ForbiddenUsing(string file, int index, string directive)
            {
                File = file;
                Index = index;
                Directive = directive;
            }

            public string File { get; }
            public int Index { get; }
            public string Directive { get; }
        }

        private sealed class MemberScope
        {
            public MemberScope(string symbol, int start, int end)
            {
                Symbol = symbol;
                Start = start;
                End = end;
            }

            public string Symbol { get; }
            public int Start { get; }
            public int End { get; }

            public bool Contains(int index)
            {
                return index >= Start && index <= End;
            }

            public string Text(string source)
            {
                return source.Substring(Start, End - Start + 1);
            }
        }

        private sealed class SourceFile
        {
            public SourceFile(string path, string raw)
            {
                Path = path;
                Raw = raw;
                Sanitized = SourceText.Sanitize(raw);
            }

            public string Path { get; }
            public string Raw { get; }
            public string Sanitized { get; }
        }

        private sealed class SourceCorpus
        {
            private readonly Dictionary<string, SourceFile> byPath;
            private readonly Dictionary<string, MemberScope> memberCache =
                new Dictionary<string, MemberScope>(StringComparer.Ordinal);

            private SourceCorpus(List<SourceFile> files)
            {
                Files = files;
                byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
            }

            public List<SourceFile> Files { get; }

            public static SourceCorpus Load(string root)
            {
                string sourceRoot = Path.Combine(root, "Source");
                List<SourceFile> files = Directory
                    .GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                    .Select(path => new SourceFile(
                        SourcePrefix + Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                        System.IO.File.ReadAllText(path)))
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ToList();
                return new SourceCorpus(files);
            }

            public SourceFile File(string path)
            {
                SourceFile file;
                if (!byPath.TryGetValue(path, out file))
                {
                    throw new InvalidOperationException("Missing source-contract file: " + path);
                }

                return file;
            }

            public MemberScope Member(string file, string symbol, MemberKind kind)
            {
                string key = file + "::" + kind + "::" + symbol;
                MemberScope scope;
                if (memberCache.TryGetValue(key, out scope))
                {
                    return scope;
                }

                SourceFile source = File(file);
                scope = SourceText.FindMember(source.Sanitized, symbol, kind);
                memberCache[key] = scope;
                return scope;
            }
        }

        private static class SourceText
        {
            public static string Sanitize(string source)
            {
                char[] result = source.ToCharArray();
                int index = 0;
                while (index < result.Length)
                {
                    if (result[index] == '/' && index + 1 < result.Length && result[index + 1] == '/')
                    {
                        BlankUntilLineEnd(result, ref index);
                        continue;
                    }

                    if (result[index] == '/' && index + 1 < result.Length && result[index + 1] == '*')
                    {
                        BlankBlockComment(result, ref index);
                        continue;
                    }

                    if (result[index] == '@' && index + 1 < result.Length && result[index + 1] == '"')
                    {
                        BlankVerbatimString(result, ref index);
                        continue;
                    }

                    if (result[index] == '"')
                    {
                        BlankQuotedLiteral(result, ref index, '"');
                        continue;
                    }

                    if (result[index] == '\'')
                    {
                        BlankQuotedLiteral(result, ref index, '\'');
                        continue;
                    }

                    index++;
                }

                return new string(result);
            }

            public static MemberScope FindMember(string sanitized, string symbol, MemberKind kind)
            {
                List<MemberScope> matches = kind == MemberKind.Property
                    ? FindProperties(sanitized, symbol)
                    : FindMethods(sanitized, symbol);
                if (matches.Count != 1)
                {
                    throw new InvalidOperationException(
                        "Expected exactly one " + kind + " named '" + symbol
                        + "', found " + matches.Count + ".");
                }

                return matches[0];
            }

            public static int MatchingBrace(string source, int openBrace)
            {
                if (openBrace < 0 || openBrace >= source.Length || source[openBrace] != '{')
                {
                    return -1;
                }

                int depth = 0;
                for (int index = openBrace; index < source.Length; index++)
                {
                    if (source[index] == '{')
                    {
                        depth++;
                    }
                    else if (source[index] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return index;
                        }
                    }
                }

                return -1;
            }

            public static int NextNonWhitespace(string source, int start)
            {
                for (int index = Math.Max(0, start); index < source.Length; index++)
                {
                    if (!char.IsWhiteSpace(source[index]))
                    {
                        return index;
                    }
                }

                return -1;
            }

            public static int LineNumber(string source, int index)
            {
                int line = 1;
                int capped = Math.Min(index, source.Length);
                for (int current = 0; current < capped; current++)
                {
                    if (source[current] == '\n')
                    {
                        line++;
                    }
                }

                return line;
            }

            private static List<MemberScope> FindMethods(string source, string symbol)
            {
                List<MemberScope> scopes = new List<MemberScope>();
                Regex names = new Regex(@"\b" + Regex.Escape(symbol) + @"\s*\(");
                foreach (Match match in names.Matches(source))
                {
                    int lineStart = source.LastIndexOf('\n', match.Index);
                    lineStart = lineStart < 0 ? 0 : lineStart + 1;
                    string linePrefix = source.Substring(lineStart, match.Index - lineStart);
                    if (!Regex.IsMatch(
                        linePrefix,
                        @"\b(?:public|private|protected|internal)\b"))
                    {
                        continue;
                    }

                    int openParen = source.IndexOf('(', match.Index);
                    int closeParen = MatchingDelimiter(source, openParen, '(', ')');
                    if (closeParen < 0)
                    {
                        continue;
                    }

                    int openBrace = NextDeclarationBrace(source, closeParen + 1);
                    if (openBrace < 0)
                    {
                        continue;
                    }

                    int closeBrace = MatchingBrace(source, openBrace);
                    if (closeBrace >= 0)
                    {
                        scopes.Add(new MemberScope(symbol, openBrace, closeBrace));
                    }
                }

                return scopes;
            }

            private static List<MemberScope> FindProperties(string source, string symbol)
            {
                List<MemberScope> scopes = new List<MemberScope>();
                Regex declarations = new Regex(
                    @"(?m)^[ \t]*(?:public|private|protected|internal)\b[^\r\n;{}()=]*\b"
                        + Regex.Escape(symbol)
                        + @"\b[^\r\n;{}()=]*[ \t]*\r?$");
                foreach (Match match in declarations.Matches(source))
                {
                    int openBrace = NextNonWhitespace(source, match.Index + match.Length);
                    if (openBrace < 0 || source[openBrace] != '{')
                    {
                        continue;
                    }

                    int closeBrace = MatchingBrace(source, openBrace);
                    if (closeBrace >= 0)
                    {
                        scopes.Add(new MemberScope(symbol, openBrace, closeBrace));
                    }
                }

                return scopes;
            }

            private static int NextDeclarationBrace(string source, int start)
            {
                for (int index = start; index < source.Length; index++)
                {
                    char current = source[index];
                    if (current == '{')
                    {
                        return index;
                    }

                    if (current == ';' || (current == '='
                        && index + 1 < source.Length && source[index + 1] == '>'))
                    {
                        return -1;
                    }
                }

                return -1;
            }

            private static int MatchingDelimiter(
                string source,
                int open,
                char openCharacter,
                char closeCharacter)
            {
                if (open < 0 || open >= source.Length || source[open] != openCharacter)
                {
                    return -1;
                }

                int depth = 0;
                for (int index = open; index < source.Length; index++)
                {
                    if (source[index] == openCharacter)
                    {
                        depth++;
                    }
                    else if (source[index] == closeCharacter)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return index;
                        }
                    }
                }

                return -1;
            }

            private static void BlankUntilLineEnd(char[] text, ref int index)
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                {
                    text[index++] = ' ';
                }
            }

            private static void BlankBlockComment(char[] text, ref int index)
            {
                text[index++] = ' ';
                text[index++] = ' ';
                while (index < text.Length)
                {
                    if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/')
                    {
                        text[index++] = ' ';
                        text[index++] = ' ';
                        return;
                    }

                    if (text[index] != '\r' && text[index] != '\n')
                    {
                        text[index] = ' ';
                    }

                    index++;
                }
            }

            private static void BlankVerbatimString(char[] text, ref int index)
            {
                text[index++] = ' ';
                text[index++] = ' ';
                while (index < text.Length)
                {
                    if (text[index] == '"' && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        text[index++] = ' ';
                        text[index++] = ' ';
                        continue;
                    }

                    if (text[index] == '"')
                    {
                        text[index++] = ' ';
                        return;
                    }

                    if (text[index] != '\r' && text[index] != '\n')
                    {
                        text[index] = ' ';
                    }

                    index++;
                }
            }

            private static void BlankQuotedLiteral(char[] text, ref int index, char quote)
            {
                text[index++] = ' ';
                while (index < text.Length)
                {
                    if (text[index] == '\\')
                    {
                        text[index++] = ' ';
                        if (index < text.Length)
                        {
                            if (text[index] != '\r' && text[index] != '\n')
                            {
                                text[index] = ' ';
                            }

                            index++;
                        }

                        continue;
                    }

                    if (text[index] == quote)
                    {
                        text[index++] = ' ';
                        return;
                    }

                    if (text[index] != '\r' && text[index] != '\n')
                    {
                        text[index] = ' ';
                    }

                    index++;
                }
            }
        }
    }
}
