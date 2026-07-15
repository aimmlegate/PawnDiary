// DLC + optional-mod safety fixture for Pawn Diary (TEST_COVERAGE_PLAN.md §7.3).
//
// The base-game-only run is release-blocking: a player who owns none of the paid DLCs (Royalty,
// Ideology, Biotech, Anomaly) and runs no optional compatibility mods must never hit a crash or a
// spurious diary entry from DLC/mod-only code paths. This suite proves the three seams that carry
// that guarantee, and is written to be SAFE and MEANINGFUL on a base install:
//
//   (a) DlcContext accessors are double-guarded (ModsConfig.<Dlc>Active AND a null tracker check),
//       so when a DLC is inactive every accessor returns string.Empty / an empty list / false and
//       never throws (the guard short-circuits BEFORE it could touch a DLC-owned tracker or ask the
//       DefDatabase for a DLC def). Source/Generation/DlcContext.cs.
//   (b) The DLC/optional-mod compatibility interaction groups (the "DLC string-matchers" that ship
//       inside the core mod) sit fully inert while their target package is absent: a group gated by
//       <enableWhenPackageIdsLoaded> whose packages are not loaded reports MissingRequiredPackage()
//       -> UnavailableForCurrentRuntime()==true, so the settings/capture/API boundary never lets it
//       classify a base-game defName. Source/Defs/InteractionGroups.cs.
//   (c) The representative DLC-gated prompt-enchantment collectors (source="RoyalTitle" / source=
//       "IdeologyRole") produce NO candidate for a pawn whose DLC status is empty and never crash:
//       the collector reads the value through the same guarded DlcContext accessor, so without the
//       DLC the value is empty and the candidate is skipped. Source/Generation/PromptEnchantmentCollector.cs.
//
// Every DLC-conditional case REPORTS a result rather than skipping silently: when the owning DLC is
// active in the current run the positive behavior is asserted (the real DLC path runs without a
// crash and the accessor is non-null); when it is inactive the clean empty / no-op result is asserted
// and announced as "not applicable" via Log.Message so the run record shows the branch was reached.
//
// Determinism / safety: this suite creates one generation-DISABLED colonist (no LLM request can ever
// leave the game) and records NO diary event — it only READS accessors, enumerates loaded Defs, and
// runs the pure collector against in-memory Defs it never registers. It mutates no settings and no
// game state, so the harness's own snapshot/restore + no-leak audit is sufficient; nothing extra to
// clean up. The prompt-enchantment Defs are constructed in memory and passed straight to Collect, so
// the DefDatabase is never touched by them.
//
// Coverage-matrix area (TEST_COVERAGE_PLAN.md §7.3): DLC + optional-mod base-game safety.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves §7.3 base-game safety: DlcContext accessors are DLC-gated and never throw, the
    /// DLC/optional-mod compatibility interaction groups sit inert without their package, and the
    /// DLC-gated prompt-enchantment collectors yield no candidate without the DLC. Requires a loaded
    /// game (the harness builds an isolated colonist); no per-pawn generation is ever enabled, so no
    /// LLM request can leave the game. Every DLC branch asserts a concrete result on both the active
    /// and inactive path.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDlcSafetyFixtureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest §7.3] ";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a scope and creates one isolated, generation-disabled colonist. In a base-game run the
        /// pawn simply has null genes/royalty/ideo trackers, which is exactly the state the DlcContext
        /// guards must survive.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            pawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every harness mutation and audits that no test-owned state survived, even when a test
        /// above threw partway through. This suite mutates nothing itself, so the harness owns all cleanup.
        /// </summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// §7.3(a). Every DlcContext accessor is DLC-gated and base-game-safe: it never throws and, when
        /// the owning DLC is inactive, returns an empty string / empty list / false without reaching a
        /// DLC-owned tracker or the DefDatabase. When the DLC IS active the accessor still returns a
        /// non-null value (the isolated test pawn simply holds no title/role/custom xenotype), proving the
        /// real guarded path runs without crashing. Each of the four DLC families reports its branch.
        /// </summary>
        [Test]
        public static void DlcContextAccessorsAreGatedAndBaseGameSafe()
        {
            // Collect every accessor result inside one guard so any throw surfaces as a focused failure
            // rather than a raw RimTest stack trace. A base-game pawn drives the null-tracker path.
            string xenotype;
            string xenotypeLabel;
            string xenotypeDefName;
            string royalTitle;
            string royalTitleLabel;
            string royalTitleDefName;
            bool isCreepJoiner;
            bool isHaunted;
            string ideoligion;
            string ideologicalRole;
            List<string> preceptDefNames;
            GrowthPawnSnapshot growthSnapshot;
            bool growthCaptured;
            HashSet<string> disabledGrowthWorkTypes;
            FamilyChildSnapshot familyChildSnapshot;
            FamilyActivityObservation familyActivity;
            FamilyHediffSnapshot familyHediff;
            bool familyChildCaptured;
            bool familyActivityCaptured;
            bool familyHediffCaptured;
            try
            {
                xenotype = DlcContext.Xenotype(pawn);
                xenotypeLabel = DlcContext.XenotypeLabel(pawn);
                xenotypeDefName = DlcContext.XenotypeDefName(pawn);
                royalTitle = DlcContext.RoyalTitle(pawn);
                royalTitleLabel = DlcContext.RoyalTitleLabel(pawn);
                royalTitleDefName = DlcContext.RoyalTitleDefName(pawn);
                isCreepJoiner = DlcContext.IsCreepJoiner(pawn);
                isHaunted = DlcContext.IsHauntedByUnnaturalCorpse(pawn);
                ideoligion = DlcContext.Ideoligion(pawn);
                ideologicalRole = DlcContext.IdeologicalRole(pawn);
                preceptDefNames = DlcContext.IdeologyPreceptDefNames(pawn);
                growthCaptured = DlcContext.TryCaptureGrowthPawn(
                    pawn,
                    birthdayAge: 13,
                    growthTier: 4,
                    hasNewResponsibilities: false,
                    out growthSnapshot);
                disabledGrowthWorkTypes = DlcContext.GrowthDisabledWorkTypeDefNames(pawn);
                familyChildCaptured = DlcContext.TryCaptureFamilyChild(pawn, out familyChildSnapshot);
                familyActivityCaptured = DlcContext.TryCaptureFamilyActivity(
                    pawn,
                    pawn,
                    BiotechFamilyActivityKindTokens.Lesson,
                    out familyActivity);
                familyHediffCaptured = DlcContext.TryCaptureFamilyHediff(
                    null,
                    BiotechFamilyHediffKindTokens.Pregnancy,
                    out familyHediff);
            }
            catch (Exception exception)
            {
                throw new AssertionException(
                    "A DlcContext accessor threw on a base-game-safe colonist (the DLC guard is not "
                    + "protecting the caller): " + exception);
            }

            // Contract shared by every string accessor and the list accessor: never null.
            PawnDiaryRimTestScope.Require(
                xenotype != null && xenotypeLabel != null && xenotypeDefName != null
                    && royalTitle != null && royalTitleLabel != null && royalTitleDefName != null
                    && ideoligion != null && ideologicalRole != null && preceptDefNames != null
                    && disabledGrowthWorkTypes != null,
                "A DlcContext accessor returned null; callers append these unconditionally and rely on "
                + "an empty string / empty list instead.");

            // Biotech (xenotype). Inactive -> all three empty; active -> non-null (baseliner test pawn).
            RequireDlcBranch(
                ModsConfig.BiotechActive,
                "Biotech",
                emptyExpected: xenotype.Length == 0 && xenotypeLabel.Length == 0
                    && xenotypeDefName.Length == 0 && !growthCaptured && growthSnapshot == null
                    && disabledGrowthWorkTypes.Count == 0
                    && !familyChildCaptured && familyChildSnapshot == null
                    && !familyActivityCaptured && familyActivity == null
                    && !familyHediffCaptured && familyHediff == null,
                emptyMessage: "Without Biotech, xenotype and growth-snapshot accessors must be empty.");

            // Royalty (royal title). Inactive -> all three empty; active -> non-null (titleless test pawn).
            RequireDlcBranch(
                ModsConfig.RoyaltyActive,
                "Royalty",
                emptyExpected: royalTitle.Length == 0 && royalTitleLabel.Length == 0 && royalTitleDefName.Length == 0,
                emptyMessage: "Without Royalty, every royal-title accessor must be empty.");

            // Anomaly (creepjoiner / unnatural corpse). Inactive -> both false; active -> both false for a
            // normal test pawn, and crucially the GameComponent_Anomaly lookup must not crash.
            RequireDlcBranch(
                ModsConfig.AnomalyActive,
                "Anomaly",
                emptyExpected: !isCreepJoiner && !isHaunted,
                emptyMessage: "Without Anomaly, the creepjoiner / unnatural-corpse accessors must be false.");

            // Ideology (ideoligion / role / precepts). Inactive -> empty strings + empty list; active ->
            // non-null (already asserted above); the pawn may or may not carry an ideo, so only emptiness
            // is asserted on the inactive branch.
            RequireDlcBranch(
                ModsConfig.IdeologyActive,
                "Ideology",
                emptyExpected: ideoligion.Length == 0 && ideologicalRole.Length == 0 && preceptDefNames.Count == 0,
                emptyMessage: "Without Ideology, the ideoligion / role / precept accessors must be empty.");
        }

        /// <summary>
        /// §7.3(b). The DLC/optional-mod compatibility interaction groups ship inside the core mod and are
        /// gated by <c>enableWhenPackageIdsLoaded</c>. This asserts the inertness invariant every one of
        /// them depends on: whenever a group's required packages are all absent it reports
        /// MissingRequiredPackage() and therefore UnavailableForCurrentRuntime()==true, so the settings /
        /// capture / public-API boundary can never let its match tokens claim a base-game defName. Groups
        /// whose package IS present are confirmed not blocked by the missing-package gate. On a base
        /// install this examines every shipped compatibility group and passes with all of them inert.
        /// </summary>
        [Test]
        public static void DlcAndOptionalModCompatGroupsSitInertWithoutTheirPackage()
        {
            List<DiaryInteractionGroupDef> groups = DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading;
            PawnDiaryRimTestScope.Require(
                groups != null && groups.Count > 0,
                "No DiaryInteractionGroupDefs were loaded; the classification catalog would be empty.");

            int inertGated = 0;
            int activeGated = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                PawnDiaryRimTestScope.Require(group != null, "A null DiaryInteractionGroupDef was loaded.");

                bool isGated = group.enableWhenPackageIdsLoaded != null
                    && group.enableWhenPackageIdsLoaded.Count > 0;
                if (!isGated)
                {
                    // Ungated core groups must never spuriously report a missing required package.
                    PawnDiaryRimTestScope.Require(
                        !group.MissingRequiredPackage(),
                        "Ungated group '" + group.defName + "' reported a missing required package.");
                    continue;
                }

                // Independently re-derive package presence through the gate helper so the check below is
                // not just a restatement of MissingRequiredPackage()'s own return value.
                bool packagePresent = InteractionGroups.AnyPackageLoaded(group.enableWhenPackageIdsLoaded);
                PawnDiaryRimTestScope.Require(
                    group.MissingRequiredPackage() == !packagePresent,
                    "Compat group '" + group.defName + "' MissingRequiredPackage() disagreed with whether "
                    + "any required package is loaded.");

                if (!packagePresent)
                {
                    // The package is absent: the group MUST be held inert at the availability boundary,
                    // no matter what match tokens it carries. UnavailableForCurrentRuntime() is the single
                    // gate every settings / capture / public-API consumer checks before classifying.
                    PawnDiaryRimTestScope.Require(
                        group.UnavailableForCurrentRuntime(),
                        "Compat group '" + group.defName + "' is missing its required package but was not "
                        + "reported unavailable, so its DLC/mod match tokens could claim a base-game defName.");
                    inertGated++;
                }
                else
                {
                    // The package is present, so the missing-package gate must not fire for this group.
                    PawnDiaryRimTestScope.Require(
                        !group.MissingRequiredPackage(),
                        "Compat group '" + group.defName + "' has its required package loaded but still "
                        + "reported a missing required package.");
                    activeGated++;
                }
            }

            Log.Message(LogPrefix + "compat interaction groups: " + inertGated + " inert (package absent), "
                + activeGated + " active (package present).");
        }

        /// <summary>
        /// §7.3(c). The DLC-gated prompt-enchantment collectors (source="RoyalTitle" / "IdeologyRole")
        /// never crash and produce a candidate only when the pawn's DLC status string is non-empty — which
        /// without the owning DLC it can never be, because the collector reads it through the guarded
        /// DlcContext accessor. Each source is run in isolation (a single in-memory Def passed to Collect)
        /// so the returned candidate count is attributable to exactly that DLC source. Both the Royalty and
        /// Ideology branches report their result.
        /// </summary>
        [Test]
        public static void DlcGatedEnchantmentCollectorsYieldNoCandidateWithoutDlc()
        {
            // RoyalTitle source (Royalty). The candidate is added iff DlcContext.RoyalTitle is non-empty.
            string royalTitle = DlcContext.RoyalTitle(pawn);
            int royalCandidates = CollectSingleSourceCount("RoyalTitle");
            PawnDiaryRimTestScope.Require(
                royalCandidates == (string.IsNullOrEmpty(royalTitle) ? 0 : 1),
                "The RoyalTitle enchantment candidate count did not match the DlcContext royal-title value.");
            if (!ModsConfig.RoyaltyActive)
            {
                PawnDiaryRimTestScope.Require(
                    royalCandidates == 0,
                    "Without Royalty, the RoyalTitle enchantment source must yield no candidate.");
                Log.Message(LogPrefix + "RoyalTitle enchantment source: not applicable (Royalty inactive) — 0 candidates.");
            }
            else
            {
                Log.Message(LogPrefix + "RoyalTitle enchantment source: Royalty active — real DlcContext path ran, "
                    + royalCandidates + " candidate(s) for the titleless test pawn.");
            }

            // IdeologyRole source (Ideology). Same shape against DlcContext.IdeologicalRole.
            string ideologicalRole = DlcContext.IdeologicalRole(pawn);
            int ideoCandidates = CollectSingleSourceCount("IdeologyRole");
            PawnDiaryRimTestScope.Require(
                ideoCandidates == (string.IsNullOrEmpty(ideologicalRole) ? 0 : 1),
                "The IdeologyRole enchantment candidate count did not match the DlcContext ideological-role value.");
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(
                    ideoCandidates == 0,
                    "Without Ideology, the IdeologyRole enchantment source must yield no candidate.");
                Log.Message(LogPrefix + "IdeologyRole enchantment source: not applicable (Ideology inactive) — 0 candidates.");
            }
            else
            {
                Log.Message(LogPrefix + "IdeologyRole enchantment source: Ideology active — real DlcContext path ran, "
                    + ideoCandidates + " candidate(s) for the roleless test pawn.");
            }
        }

        // ----- helpers -----------------------------------------------------------------------------

        /// <summary>
        /// Runs the real prompt-enchantment collector against a single in-memory DLC-source Def (chance=1,
        /// so the roll never gates it) and returns how many candidates it produced. A throw is turned into
        /// a focused assertion failure — the whole point of the DLC guard is that this never crashes.
        /// The Def is never registered, so the DefDatabase is untouched and no cleanup is required.
        /// </summary>
        private static int CollectSingleSourceCount(string source)
        {
            DiaryPromptEnchantmentDef def = new DiaryPromptEnchantmentDef { source = source };
            List<DiaryPromptEnchantmentDef> defs = new List<DiaryPromptEnchantmentDef> { def };
            try
            {
                List<PromptEnchantmentCandidate> candidates = PromptEnchantmentCollector.Collect(
                    pawn, defs, includeImportantEventContext: true, tuning: DiaryTuning.PromptEnchantmentTuning);
                return candidates == null ? 0 : candidates.Count;
            }
            catch (Exception exception)
            {
                throw new AssertionException(
                    "Collecting a '" + source + "' DLC-gated enchantment candidate threw on a base-game pawn: "
                    + exception);
            }
        }

        /// <summary>
        /// Asserts one DLC family's branch and reports it. When the DLC is inactive the empty/no-op result
        /// must hold and the branch is announced as "not applicable"; when active the positive path (the
        /// guarded accessor ran without a crash) is announced. Never skips silently.
        /// </summary>
        private static void RequireDlcBranch(bool dlcActive, string dlcName, bool emptyExpected, string emptyMessage)
        {
            if (dlcActive)
            {
                Log.Message(LogPrefix + dlcName + ": active — guarded DlcContext path ran without a crash.");
                return;
            }

            PawnDiaryRimTestScope.Require(emptyExpected, emptyMessage);
            Log.Message(LogPrefix + dlcName + ": not applicable (DLC inactive) — accessors returned empty.");
        }
    }
}
