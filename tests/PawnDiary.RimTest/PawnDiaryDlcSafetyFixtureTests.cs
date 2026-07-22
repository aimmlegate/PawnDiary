// DLC + optional-mod safety fixture for Pawn Diary (TEST_COVERAGE_PLAN.md §7.3).
//
// The base-game-only run is release-blocking: a player who owns none of the paid DLCs (Royalty,
// Ideology, Biotech, Anomaly, Odyssey) and runs no optional compatibility mods must never hit a crash or a
// spurious diary entry from DLC/mod-only code paths. This suite proves the principal seams that carry
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
//   (d) Installed DLC paths use real pawn state: non-Baseliner xenotype, royal title, ideoligion/
//       precepts and, where the colony's requirements permit it, an ideoligion role; a disposable
//       pawn temporarily carrying a real loaded creepjoiner form/tracker exercises the positive adapter.
//   (e) The complete official-DLC interaction-group/event-window catalog agrees with ModsConfig and
//       settings visibility, and fragile DLC Harmony/reflection targets still match RimWorld 1.6.
//   (f) Optional capture capability readiness suppresses its XML fallback only while ready, restoring
//       fail-open capture immediately when the adapter reports unavailable.
//
// Every DLC-conditional case REPORTS a result rather than skipping silently: when the owning DLC is
// active in the current run the positive behavior is asserted (the real DLC path runs without a
// crash and the accessor is non-null); when it is inactive the clean empty / no-op result is asserted
// and announced as "not applicable" via Log.Message so the run record shows the branch was reached.
//
// Determinism / safety: every pawn is generation-DISABLED (no LLM request can ever leave the game).
// Positive fixtures mutate only disposable test pawns; an unoccupied Ideology role may be assigned
// briefly and is registered for guaranteed cleanup. The suite records no requested diary event. The
// prompt-enchantment and capability-fallback Defs are constructed in memory and never registered, so
// the DefDatabase stays untouched; process-global capability readiness is cleared in a finally block.
//
// Coverage-matrix area (TEST_COVERAGE_PLAN.md §7.3): DLC + optional-mod base-game safety.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Integration;
using RimTestRedux;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves §7.3 DLC compatibility: guarded accessors and final summary adapters omit absent DLC,
    /// real installed-DLC pawn state reaches the expected context/enchantment seams, official package
    /// gates match settings visibility, fragile hook targets retain their runtime signatures, and
    /// optional adapters fail open. Requires a loaded game; no per-pawn generation is ever enabled,
    /// so no LLM request can leave the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDlcSafetyFixtureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest §7.3] ";
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const string RoyaltyPackageId = "Ludeon.RimWorld.Royalty";
        private const string IdeologyPackageId = "Ludeon.RimWorld.Ideology";
        private const string BiotechPackageId = "Ludeon.RimWorld.Biotech";
        private const string AnomalyPackageId = "Ludeon.RimWorld.Anomaly";
        private const string OdysseyPackageId = "Ludeon.RimWorld.Odyssey";

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
        /// real guarded path runs without crashing. Each of the five DLC families reports its branch.
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
            bool isGhoul;
            AnomalyContainmentEscapeCapture containmentEscape;
            bool containmentEscapeCaptured;
            GhoulTransformationCapture ghoulTransformation;
            bool ghoulTransformationCaptured;
            string ideoligion;
            string ideologicalRole;
            List<string> preceptDefNames;
            BeliefSnapshot beliefSnapshot;
            BeliefSourcePreceptFact nullThoughtSource;
            GrowthPawnSnapshot growthSnapshot;
            bool growthCaptured;
            HashSet<string> disabledGrowthWorkTypes;
            FamilyChildSnapshot familyChildSnapshot;
            FamilyActivityObservation familyActivity;
            FamilyHediffSnapshot familyHediff;
            bool familyChildCaptured;
            bool familyActivityCaptured;
            bool familyHediffCaptured;
            BirthMutationSnapshot birthStartSnapshot;
            bool birtherAliveBefore;
            bool birthStartCaptured;
            Pawn completedBirthChild;
            bool birthCompletionCaptured;
            BirthChildNamingState birthNamingState;
            Pawn namedBirthChild;
            bool birthNamingCaptured;
            OdysseyLocationSnapshot odysseyLocation;
            OdysseyMobileHomeSnapshot odysseyMobileHome;
            bool odysseyLocationCaptured;
            bool odysseyMobileHomeCaptured;
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
                isGhoul = DlcContext.IsGhoul(pawn);
                containmentEscapeCaptured = DlcContext.TryCaptureAnomalyContainmentBefore(
                    null, 60, out containmentEscape);
                ghoulTransformationCaptured = DlcContext.TryCaptureGhoulTransformation(
                    pawn, pawn, out ghoulTransformation);
                ideoligion = DlcContext.Ideoligion(pawn);
                ideologicalRole = DlcContext.IdeologicalRole(pawn);
                preceptDefNames = DlcContext.IdeologyPreceptDefNames(pawn);
                beliefSnapshot = DlcContext.CaptureBeliefSnapshot(pawn);
                nullThoughtSource = DlcContext.CaptureThoughtSourcePrecept(null);
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
                birthStartCaptured = DlcContext.TryCaptureBirthStart(
                    pawn,
                    pawn,
                    null,
                    null,
                    ritualBirth: false,
                    out birthStartSnapshot,
                    out birtherAliveBefore);
                birthCompletionCaptured = DlcContext.TryCompleteBirthSnapshot(
                    null,
                    pawn,
                    pawn,
                    positivityIndex: 1,
                    birtherAliveBefore: true,
                    out completedBirthChild);
                birthNamingCaptured = DlcContext.TryCaptureBirthChildNaming(
                    pawn.GetUniqueLoadID(),
                    out birthNamingState,
                    out namedBirthChild);
                odysseyLocationCaptured = DlcContext.TryCaptureOdysseyLocation(
                    pawn,
                    out odysseyLocation);
                odysseyMobileHomeCaptured = DlcContext.TryCaptureOdysseyMobileHome(
                    pawn,
                    out odysseyMobileHome);
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
                    && beliefSnapshot != null && nullThoughtSource != null
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
                    && !familyHediffCaptured && familyHediff == null
                    && !birthStartCaptured && birthStartSnapshot == null && !birtherAliveBefore
                    && !birthCompletionCaptured && completedBirthChild == null
                    && !birthNamingCaptured && birthNamingState != null && namedBirthChild == null,
                emptyMessage: "Without Biotech, xenotype, growth, family, and birth accessors must be empty.");

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
                emptyExpected: !isCreepJoiner && !isHaunted && !isGhoul
                    && !containmentEscapeCaptured && containmentEscape == null
                    && !ghoulTransformationCaptured && ghoulTransformation == null,
                emptyMessage: "Without Anomaly, creepjoiner, unnatural-corpse, and ghoul accessors must be false.");

            // Ideology (ideoligion / role / precepts). Inactive -> empty strings + empty list; active ->
            // non-null (already asserted above); the pawn may or may not carry an ideo, so only emptiness
            // is asserted on the inactive branch.
            RequireDlcBranch(
                ModsConfig.IdeologyActive,
                "Ideology",
                emptyExpected: ideoligion.Length == 0 && ideologicalRole.Length == 0
                    && preceptDefNames.Count == 0 && !beliefSnapshot.ideologyActive
                    && beliefSnapshot.precepts.Count == 0 && beliefSnapshot.memes.Count == 0
                    && nullThoughtSource.instanceId.Length == 0 && nullThoughtSource.defName.Length == 0,
                emptyMessage: "Without Ideology, the ideoligion / role / precept and Phase 1 snapshot accessors must be empty.");

            RequireDlcBranch(
                ModsConfig.OdysseyActive,
                "Odyssey",
                emptyExpected: !odysseyLocationCaptured && odysseyLocation == null
                    && !odysseyMobileHomeCaptured && odysseyMobileHome == null,
                emptyMessage: "Without Odyssey, location and mobile-home accessors must return false/null.");
        }

        /// <summary>
        /// The null-pawn contract must hold even in an all-DLC process. This catches accessors that only
        /// appear safe in a base-only run because their ModsConfig guard short-circuits before a missing
        /// pawn dereference. Summary builders and optional adapters legitimately ask about a pawn that
        /// disappeared between event capture and prompt generation, so every result must stay empty.
        /// </summary>
        [Test]
        public static void EveryDlcAccessorRejectsNullPawnEvenWhenItsDlcIsActive()
        {
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(DlcContext.Xenotype(null))
                    && string.IsNullOrEmpty(DlcContext.XenotypeLabel(null))
                    && string.IsNullOrEmpty(DlcContext.XenotypeDefName(null)),
                "Biotech xenotype accessors must return empty for a null pawn.");
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(DlcContext.RoyalTitle(null))
                    && string.IsNullOrEmpty(DlcContext.RoyalTitleLabel(null))
                    && string.IsNullOrEmpty(DlcContext.RoyalTitleDefName(null)),
                "Royalty title accessors must return empty for a null pawn.");
            PawnDiaryRimTestScope.Require(!DlcContext.IsCreepJoiner(null)
                    && !DlcContext.IsHauntedByUnnaturalCorpse(null)
                    && !DlcContext.IsGhoul(null),
                "Anomaly accessors must return false for a null pawn.");
            AnomalyContainmentEscapeCapture containmentEscape;
            GhoulTransformationCapture ghoulTransformation;
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCaptureAnomalyContainmentBefore(null, 60, out containmentEscape)
                    && containmentEscape == null,
                "Anomaly containment capture must return false/null for a null target.");
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCaptureGhoulTransformation(null, null, out ghoulTransformation)
                    && ghoulTransformation == null,
                "Anomaly ghoul capture must return false/null for missing role pawns.");
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(DlcContext.Ideoligion(null))
                    && string.IsNullOrEmpty(DlcContext.IdeologicalRole(null))
                    && DlcContext.IdeologyPreceptDefNames(null).Count == 0
                    && !DlcContext.CaptureBeliefSnapshot(null).ideologyActive
                    && DlcContext.CaptureThoughtSourcePrecept(null).instanceId.Length == 0,
                "Ideology accessors must return empty for a null pawn.");
            OdysseyLocationSnapshot odysseyLocation;
            OdysseyMobileHomeSnapshot odysseyMobileHome;
            PawnDiaryRimTestScope.Require(!DlcContext.TryCaptureOdysseyLocation((Pawn)null, out odysseyLocation)
                    && odysseyLocation == null
                    && !DlcContext.TryCaptureOdysseyMobileHome(null, out odysseyMobileHome)
                    && odysseyMobileHome == null,
                "Odyssey accessors must return false/null for a null pawn.");
            PawnDiaryRimTestScope.Require(DiaryContextBuilder.BuildPawnSummarySnapshot(null) == null,
                "The public pawn-summary adapter must preserve its documented null-pawn result.");
        }

        /// <summary>
        /// Drives real installed Biotech, Royalty, and Ideology pawn state through DlcContext, the
        /// public structured snapshot, the prompt-summary blob, and the title enchantment collector.
        /// Inactive DLCs prove their fields are omitted at the final adapter boundary instead. The test
        /// pawn is disposable, so all mutations disappear with the ordinary scope teardown.
        /// </summary>
        [Test]
        public static void InstalledDlcPawnStateFlowsThroughSummaryAndPromptAdapters()
        {
            bool biotechStateExpected = false;
            bool royaltyStateExpected = false;
            bool ideologyStateExpected = false;
            if (ModsConfig.BiotechActive)
            {
                PawnDiaryRimTestScope.Require(pawn.genes != null,
                    "A generated human pawn must have a gene tracker when Biotech is active.");
                XenotypeDef xenotype = DefDatabase<XenotypeDef>.AllDefsListForReading
                    .FirstOrDefault(def => def != null && def != XenotypeDefOf.Baseliner
                        && !string.IsNullOrWhiteSpace(def.defName));
                PawnDiaryRimTestScope.Require(xenotype != null,
                    "Biotech is active but no non-Baseliner xenotype Def was loaded.");
                pawn.genes.SetXenotypeDirect(xenotype);

                string expectedLabel = PromptTextSanitizer.LocalizedPromptText(pawn.genes.XenotypeLabelCap);
                PawnDiaryRimTestScope.Require(DlcContext.XenotypeDefName(pawn) == xenotype.defName
                        && DlcContext.XenotypeLabel(pawn) == expectedLabel
                        && DlcContext.Xenotype(pawn) == expectedLabel,
                    "A real non-Baseliner xenotype did not survive the guarded DlcContext adapter.");
                biotechStateExpected = true;
            }

            if (ModsConfig.RoyaltyActive)
            {
                PawnDiaryRimTestScope.Require(pawn.royalty != null,
                    "A Royalty-active game must provide the pawn royalty tracker.");
                RoyalTitleDef titleDef = DefDatabase<RoyalTitleDef>.AllDefsListForReading
                    .Where(def => def != null && !string.IsNullOrWhiteSpace(def.defName))
                    .OrderByDescending(def => def.seniority)
                    .FirstOrDefault();
                PawnDiaryRimTestScope.Require(titleDef != null,
                    "Royalty is active but no RoyalTitleDef was loaded.");

                // Add a title row directly to the disposable pawn instead of invoking SetTitle, which
                // would grant permits/abilities and send letters unrelated to this read-only adapter test.
                pawn.royalty.AllTitlesForReading.Add(new RoyalTitle
                {
                    def = titleDef,
                    // A scenario/mod may remove the Empire faction even with Royalty active. The
                    // guarded reader needs only the title Def; use the player faction as a safe owner.
                    faction = Faction.OfEmpire ?? scope.PlayerFaction,
                    pawn = pawn,
                    receivedTick = Find.TickManager?.TicksGame ?? 0
                });
                string currentTitleDef = pawn.royalty.MostSeniorTitle?.def?.defName ?? string.Empty;
                PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(currentTitleDef)
                        && DlcContext.RoyalTitleDefName(pawn) == currentTitleDef
                        && !string.IsNullOrWhiteSpace(DlcContext.RoyalTitle(pawn)),
                    "A real royal title did not survive the guarded DlcContext adapter.");
                PawnDiaryRimTestScope.Require(CollectSingleSourceCount("RoyalTitle") == 1,
                    "A titled pawn must produce exactly one RoyalTitle enchantment candidate.");
                royaltyStateExpected = true;
            }

            if (ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(pawn.ideo != null && scope.PlayerFaction.ideos != null,
                    "An Ideology-active player pawn must expose ideology trackers.");
                // Classic/no-ideology mode may leave the player faction without a primary Ideo. An
                // NPC Ideo is equally valid for this adapter fixture; if the loaded scenario has none
                // at all, retain the truthful empty state instead of making the suite scenario-dependent.
                Ideo fixtureIdeo = scope.PlayerFaction.ideos.PrimaryIdeo
                    ?? Find.IdeoManager?.IdeosListForReading?.FirstOrDefault();
                if (fixtureIdeo == null)
                {
                    Log.Message(LogPrefix + "Ideology positive fixture: no Ideo exists in this loaded "
                        + "scenario; the empty guarded path remains asserted.");
                }
                else
                {
                    pawn.ideo.SetIdeo(fixtureIdeo);

                    List<string> expectedPrecepts = fixtureIdeo.PreceptsListForReading
                        .Where(precept => precept?.def != null && !string.IsNullOrWhiteSpace(precept.def.defName))
                        .Select(precept => precept.def.defName)
                        .ToList();
                    PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(DlcContext.Ideoligion(pawn))
                            && DlcContext.IdeologyPreceptDefNames(pawn).SequenceEqual(expectedPrecepts),
                        "A real ideoligion/precept set did not survive the guarded DlcContext adapter.");
                    BeliefSnapshot belief = DlcContext.CaptureBeliefSnapshot(pawn);
                    PawnDiaryRimTestScope.Require(belief.ideologyActive
                            && belief.pawnId == pawn.GetUniqueLoadID()
                            && belief.ideologyId == fixtureIdeo.GetUniqueLoadID()
                            && belief.precepts.Count > 0
                            && belief.precepts.Count <= DiaryBeliefPolicy.Snapshot().maximumPreceptCandidates,
                        "The Phase 1 belief adapter did not return a bounded detached live-doctrine snapshot.");
                    PawnDiaryRimTestScope.Require(belief.precepts.All(precept => precept != null
                            && !string.IsNullOrWhiteSpace(precept.instanceId)
                            && !string.IsNullOrWhiteSpace(precept.defName)),
                        "The Phase 1 belief snapshot retained an incomplete precept identity.");
                    ideologyStateExpected = true;

                    // A colony's authored role requirements can legitimately reject this randomly
                    // generated pawn, and every role may already belong to a real colonist. Use only an
                    // unoccupied role: RimTests must never disturb a player's pawn for positive coverage.
                    Precept_Role role = fixtureIdeo.RolesListForReading
                        .FirstOrDefault(candidate => candidate != null
                            && candidate.ChosenPawnSingle() == null
                            && candidate.RequirementsMet(pawn));
                    if (role != null)
                    {
                        scope.RegisterCleanup(() =>
                        {
                            if (role.IsAssigned(pawn)) role.Unassign(pawn, false);
                        });
                        role.Assign(pawn, false);
                        PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(DlcContext.IdeologicalRole(pawn))
                                && CollectSingleSourceCount("IdeologyRole") == 1,
                            "An assigned ideoligion role did not produce its guarded context/enchantment candidate.");
                    }
                    else
                    {
                        Log.Message(LogPrefix + "Ideology role positive fixture: no unoccupied loaded role "
                            + "accepted the isolated pawn; faith/precept positive paths still ran.");
                    }
                }
            }

            DiaryPawnSummarySnapshot snapshot = DiaryContextBuilder.BuildPawnSummarySnapshot(pawn);
            string summary = DiaryContextBuilder.BuildPawnSummary(pawn);
            PawnDiaryRimTestScope.Require(snapshot != null && !string.IsNullOrWhiteSpace(summary),
                "The final pawn-summary adapters returned no result for a valid colonist.");
            RequireSummaryDlcField(
                ModsConfig.BiotechActive,
                biotechStateExpected,
                "Biotech",
                DlcContext.Xenotype(pawn),
                snapshot.xenotype,
                "xenotype=",
                summary);
            RequireSummaryDlcField(
                ModsConfig.RoyaltyActive,
                royaltyStateExpected,
                "Royalty",
                DlcContext.RoyalTitle(pawn),
                snapshot.royalTitle,
                "title=",
                summary);
            RequireSummaryDlcField(
                ModsConfig.IdeologyActive,
                ideologyStateExpected,
                "Ideology",
                DlcContext.Ideoligion(pawn),
                snapshot.faith,
                "faith=",
                summary);
        }

        /// <summary>
        /// Temporarily gives the disposable fixture pawn a vanilla creepjoiner tracker backed by one
        /// real loaded Anomaly form. This is the positive counterpart to the ordinary-colonist false
        /// branch and proves the adapter recognizes the exact state used by Pawn.IsCreepJoiner. The
        /// original tracker is restored even if the assertion fails; without Anomaly the test reports
        /// and asserts the normal no-op path.
        /// </summary>
        [Test]
        public static void AnomalyCreepjoinerPositivePathIsPackageGated()
        {
            if (!ModsConfig.AnomalyActive)
            {
                PawnDiaryRimTestScope.Require(!DlcContext.IsCreepJoiner(pawn),
                    "A normal base-game pawn must not be classified as a creepjoiner.");
                Log.Message(LogPrefix + "Anomaly creepjoiner positive fixture: not applicable (Anomaly inactive). ");
                return;
            }

            CreepJoinerFormKindDef form = DefDatabase<CreepJoinerFormKindDef>.AllDefsListForReading
                .FirstOrDefault(def => def != null);
            PawnDiaryRimTestScope.Require(form != null,
                "Anomaly is active but no CreepJoinerFormKindDef is loaded.");
            Pawn_CreepJoinerTracker originalTracker = pawn.creepjoiner;
            try
            {
                // Vanilla's IsCreepJoiner predicate checks tracker presence, not race or pawn kind.
                // Populate the tracker with a real package-owned form, but never tick it or invoke its
                // downside behavior; this isolated state exists only for the two assertions below.
                pawn.creepjoiner = new Pawn_CreepJoinerTracker(pawn) { form = form };
                PawnDiaryRimTestScope.Require(pawn.IsCreepJoiner,
                    "A vanilla creepjoiner tracker no longer satisfies Pawn.IsCreepJoiner.");
                PawnDiaryRimTestScope.Require(DlcContext.IsCreepJoiner(pawn),
                    "DlcContext did not recognize a pawn carrying a real loaded Anomaly creepjoiner form.");
            }
            finally
            {
                pawn.creepjoiner = originalTracker;
            }
        }

        /// <summary>
        /// Guards the exact RimWorld 1.6 family-correlation target. DLC types exist in the shared game
        /// assembly even on a base-only run, so this signature check is safe without Biotech active.
        /// </summary>
        [Test]
        public static void BiotechFamilyAndBirthSignaturesMatchRuntime()
        {
            System.Reflection.MethodInfo setParents = typeof(HediffWithParents).GetMethod(
                "SetParents",
                new[] { typeof(Pawn), typeof(Pawn), typeof(GeneSet) });
            PawnDiaryRimTestScope.Require(
                setParents != null,
                "RimWorld no longer exposes HediffWithParents.SetParents(Pawn, Pawn, GeneSet); "
                + "the Biotech family hook must be updated before release.");

            System.Reflection.MethodInfo applyBirth = typeof(PregnancyUtility).GetMethod(
                "ApplyBirthOutcome",
                new[]
                {
                    typeof(RitualOutcomePossibility),
                    typeof(float),
                    typeof(Precept_Ritual),
                    typeof(List<GeneDef>),
                    typeof(Pawn),
                    typeof(Thing),
                    typeof(Pawn),
                    typeof(Pawn),
                    typeof(LordJob_Ritual),
                    typeof(RitualRoleAssignments),
                    typeof(bool)
                });
            PawnDiaryRimTestScope.Require(
                applyBirth != null && applyBirth.ReturnType == typeof(Thing),
                "RimWorld no longer exposes the expected PregnancyUtility.ApplyBirthOutcome signature; "
                + "the canonical birth owner must fail open until it is updated.");

            System.Reflection.MethodInfo miscarry = typeof(Hediff_Pregnant).GetMethod(
                "Miscarry",
                Type.EmptyTypes);
            PawnDiaryRimTestScope.Require(
                miscarry != null && miscarry.ReturnType == typeof(void),
                "RimWorld no longer exposes Hediff_Pregnant.Miscarry(); exact family-loss enrichment "
                + "must be updated before release.");
        }

        /// <summary>
        /// Pins every fragile DLC hook target and reflection member used by the shipped compatibility
        /// layer. These types exist in Assembly-CSharp even without owning the DLC, so signature drift can
        /// be detected in the release-blocking base-only run instead of surfacing as a partial Harmony
        /// registration in a player's startup log.
        /// </summary>
        [Test]
        public static void FragileDlcHookTargetsMatchTheRimWorldRuntime()
        {
            Type growthLetter = typeof(ChoiceLetter_GrowthMoment);
            RequireMethod(growthLetter, "ConfigureGrowthLetter", typeof(void), new[]
            {
                typeof(Pawn), typeof(int), typeof(int), typeof(int), typeof(List<string>), typeof(Name)
            });
            RequireMethod(growthLetter, "MakeChoices", typeof(void), new[]
            {
                typeof(List<SkillDef>), typeof(Trait)
            });
            RequireField(growthLetter, "pawn", typeof(Pawn));
            RequireField(growthLetter, "growthTier", typeof(int));
            RequireField(growthLetter, "choiceMade", typeof(bool));

            Type monolith = typeof(Building_VoidMonolith);
            RequireMethod(monolith, "Activate", typeof(void), new[] { typeof(Pawn) });
            RequireMethod(monolith, "CanActivate", typeof(bool), new[]
            {
                typeof(string).MakeByRefType(), typeof(string).MakeByRefType()
            });
            RequireField(monolith, "autoActivateTick", typeof(int));
            RequireProperty(typeof(Find), "Anomaly", typeof(GameComponent_Anomaly), AnyStatic);
            RequireProperty(typeof(GameComponent_Anomaly), "LevelDef", typeof(MonolithLevelDef), AnyInstance);
            RequireProperty(typeof(CompStudyUnlocks), "Progress", typeof(int), AnyInstance);
            RequireProperty(typeof(CompStudyUnlocks), "Completed", typeof(bool), AnyInstance);
            RequireMethod(typeof(CompStudyUnlocks), "OnStudied", typeof(void),
                new[] { typeof(Pawn), typeof(float), typeof(KnowledgeCategoryDef) });
            MethodInfo escape = typeof(CompHoldingPlatformTarget).GetMethod(
                "Escape", AnyInstance, null, new[] { typeof(bool) }, null);
            RequireMethod(typeof(CompHoldingPlatformTarget), "Escape", typeof(void),
                new[] { typeof(bool) });
            PawnDiaryRimTestScope.Require(escape != null
                    && escape.GetParameters().Length == 1
                    && escape.GetParameters()[0].Name == "initiator",
                "CompHoldingPlatformTarget.Escape must retain the exact bool parameter name 'initiator' "
                    + "used by the defensive Harmony registration.");
            RequireProperty(typeof(CompHoldingPlatformTarget), "HeldPlatform",
                typeof(Building_HoldingPlatform), AnyInstance);
            RequireProperty(typeof(CompHoldingPlatformTarget), "CurrentlyHeldOnPlatform",
                typeof(bool), AnyInstance);
            RequireField(typeof(CompHoldingPlatformTarget), "isEscaping", typeof(bool));
            RequireMethod(typeof(CompHoldingPlatformTarget), "Notify_HeldOnPlatform", typeof(void),
                new[] { typeof(ThingOwner) });
            RequireMethod(typeof(CompHoldingPlatformTarget), "Notify_ReleasedFromPlatform", typeof(void),
                Type.EmptyTypes);
            RequireProperty(typeof(Building_HoldingPlatform), "HeldPawn", typeof(Pawn), AnyInstance);
            RequireField(typeof(Building_HoldingPlatform), "innerContainer", typeof(ThingOwner));
            RequireMethod(typeof(Building_HoldingPlatform), "EjectContents", typeof(void), Type.EmptyTypes);
            RequireMethod(typeof(Building_HoldingPlatform), "Notify_PawnDied", typeof(void),
                new[] { typeof(Pawn), typeof(DamageInfo?) });
            RequireMethod(typeof(Job), "GetUniqueLoadID", typeof(string), Type.EmptyTypes);
            RequireField(typeof(MonolithLevelDef), "levelInspectText", typeof(string));
            RequireField(typeof(MonolithLevelDef), "monolithLabel", typeof(string));
            RequireMethod(typeof(GameComponent_Anomaly), "PawnHasUnnaturalCorpse", typeof(bool),
                new[] { typeof(Pawn) });

            RequireMethod(typeof(LordJob_Ritual), "ApplyOutcome", typeof(void),
                new[] { typeof(float), typeof(bool), typeof(bool), typeof(bool) });
            RequireMethod(typeof(LordToil_PsychicRitual), "RitualCompleted", typeof(void), Type.EmptyTypes);

            Type gravshipController = typeof(WorldComponent_GravshipController);
            RequireMethod(gravshipController, "InitiateTakeoff", typeof(void),
                new[] { typeof(Building_GravEngine), typeof(PlanetTile) });
            RequireMethod(typeof(GravshipUtility), "TravelTo", typeof(void),
                new[] { typeof(Gravship), typeof(PlanetTile), typeof(PlanetTile) });
            RequireMethod(gravshipController, "InitiateLanding", typeof(void),
                new[] { typeof(Gravship), typeof(Map), typeof(IntVec3), typeof(Rot4) });
            RequireMethod(gravshipController, "LandingEnded", typeof(void), Type.EmptyTypes);
            RequireField(gravshipController, "gravship", typeof(Gravship));
            RequireField(gravshipController, "map", typeof(Map));
            RequireField(typeof(Building_GravEngine), "launchInfo", typeof(LaunchInfo));
            RequireField(typeof(LaunchInfo), "pilot", typeof(Pawn));
            RequireField(typeof(LaunchInfo), "copilot", typeof(Pawn));
            RequireField(typeof(LaunchInfo), "quality", typeof(float));
            RequireField(typeof(LaunchInfo), "doNegativeOutcome", typeof(bool));
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
        /// Freezes the complete official-DLC catalog currently shipped by Pawn Diary: every package-gated
        /// interaction group and event window must name the right official package, agree with ModsConfig,
        /// and appear in the settings list exactly when available. Exact set equality makes a newly added
        /// DLC row update this test deliberately instead of silently escaping the compatibility matrix.
        /// </summary>
        [Test]
        public static void OfficialDlcCatalogMatchesPackageFlagsAndSettingsVisibility()
        {
            Dictionary<string, string> expectedGroups = new Dictionary<string, string>
            {
                { "ritualGravship", OdysseyPackageId },
                { "odysseyGravshipLanding", OdysseyPackageId },
                { "ritualRoyal", RoyaltyPackageId },
                { "progressionRoyalTitle", RoyaltyPackageId },
                { "progressionPsylink", RoyaltyPackageId },
                { "personaWeaponLifecycle", RoyaltyPackageId },
                { "personaWeaponMilestone", RoyaltyPackageId },
                { "royalPermitDramatic", RoyaltyPackageId },
                { "questRoyalAscent", RoyaltyPackageId },
                { "beliefCrisis", IdeologyPackageId },
                { "counsel", IdeologyPackageId },
                { "ritualConversion", IdeologyPackageId },
                { "eventWindowVoidMonolith", AnomalyPackageId },
                { "anomalyStudyBreakthrough", AnomalyPackageId },
                { "anomalyContainmentBreach", AnomalyPackageId },
                { "anomalyCreepJoinerOutcome", AnomalyPackageId },
                { "anomalyGhoulTransformation", AnomalyPackageId },
                { "anomalyVoidOutcome", AnomalyPackageId },
                { "biotechFamilyBirth", BiotechPackageId },
                { "ritualAnomalyInvitation", AnomalyPackageId },
                { "ritualAnomalyFleshAndWeather", AnomalyPackageId },
                { "ritualAnomalyPredation", AnomalyPackageId },
                { "ritualAnomalyMind", AnomalyPackageId },
                { "ritualAnomalyAbduction", AnomalyPackageId },
                { "ritualAnomalyDeathRefusal", AnomalyPackageId },
                { "ritualAnomalyPsychic", AnomalyPackageId },
                { "progressionGrowthMoment", BiotechPackageId },
                { "progressionMechanitorLifecycle", BiotechPackageId },
                { "progressionXenotype", BiotechPackageId }
            };
            Dictionary<string, string> expectedWindows = new Dictionary<string, string>
            {
                { "VoidMonolithDiscovery", AnomalyPackageId },
                { "VoidMonolithActivation", AnomalyPackageId },
                { "VoidMonolithWaking", AnomalyPackageId },
                { "VoidMonolithVoidAwakened", AnomalyPackageId },
                { "RoyalAscent", RoyaltyPackageId }
            };

            RequireOfficialPackageFlagAgreement();

            List<DiaryInteractionGroupDef> officialGroups =
                DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading
                    .Where(group => group != null && ContainsOfficialPackage(group.enableWhenPackageIdsLoaded))
                    .ToList();
            PawnDiaryRimTestScope.Require(officialGroups.Count == expectedGroups.Count
                    && new HashSet<string>(officialGroups.Select(group => group.defName),
                        StringComparer.Ordinal).SetEquals(expectedGroups.Keys),
                "The loaded official-DLC interaction-group set drifted; update the matrix with every new, "
                + "removed, or renamed package-gated row.");

            foreach (KeyValuePair<string, string> expected in expectedGroups)
            {
                DiaryInteractionGroupDef group = officialGroups.First(row => row.defName == expected.Key);
                bool active = OfficialDlcActive(expected.Value);
                PawnDiaryRimTestScope.Require(group.enableWhenPackageIdsLoaded.Count == 1
                        && string.Equals(group.enableWhenPackageIdsLoaded[0], expected.Value,
                            StringComparison.OrdinalIgnoreCase),
                    "Official DLC group '" + expected.Key + "' is gated by the wrong package.");
                PawnDiaryRimTestScope.Require(group.MissingRequiredPackage() == !active
                        && group.UnavailableForCurrentRuntime() == !active,
                    "Official DLC group '" + expected.Key + "' disagrees with its ModsConfig flag.");
                PawnDiaryRimTestScope.Require(PawnDiaryMod.IsSettingsEventFilterGroup(group) == active,
                    "Official DLC group '" + expected.Key + "' settings visibility disagrees with availability.");
                if (string.Equals(expected.Key, "ritualAnomalyPsychic", StringComparison.Ordinal))
                {
                    PawnDiaryRimTestScope.Require(
                        (group.matchDefNames == null || group.matchDefNames.Count == 0)
                        && group.matchTokens != null
                        && group.matchTokens.Count == 1
                        && string.Equals(group.matchTokens[0], "PsychicRitual", StringComparison.Ordinal),
                        "The generic Anomaly psychic-ritual fallback must remain narrowly token-keyed by "
                        + "'PsychicRitual'; exact ritual families belong in the specialized groups above it.");
                }
                else if (string.Equals(expected.Key, "ritualRoyal", StringComparison.Ordinal))
                {
                    PawnDiaryRimTestScope.Require(
                        (group.matchDefNames == null || group.matchDefNames.Count == 0)
                        && group.matchTokens != null
                        && group.matchTokens.Count == 6
                        && new HashSet<string>(group.matchTokens, StringComparer.Ordinal).SetEquals(new[]
                        {
                            "ThroneSpeech",
                            "BestowingCeremony",
                            "AnimaTreeLinking",
                            "RitualOutcomeEffectWorker_Bestowing",
                            "RitualBehaviorWorker_ThroneSpeech",
                            "RitualBehaviorWorker_AnimaLinking"
                        }),
                        "The Royalty ritual family must retain its six narrow throne-speech, bestowing, "
                        + "and anima-linking runtime tokens; it intentionally has no exact ritual defName "
                        + "classifier.");
                }
                else if (string.Equals(expected.Key, "ritualGravship", StringComparison.Ordinal))
                {
                    PawnDiaryRimTestScope.Require(
                        (group.matchDefNames == null || group.matchDefNames.Count == 0)
                        && group.matchTokens != null
                        && new HashSet<string>(group.matchTokens, StringComparer.Ordinal).SetEquals(
                            new[] { "GravshipLaunch", "RitualBehaviorWorker_GravshipLaunch" }),
                        "The Odyssey gravship-launch ritual must retain its two narrow runtime tokens; "
                        + "it intentionally has no exact ritual defName classifier.");
                }
                else
                {
                    PawnDiaryRimTestScope.Require(group.matchDefNames != null && group.matchDefNames.Count > 0,
                        "Official DLC group '" + expected.Key + "' has no exact classifier keys.");
                }
            }

            List<DiaryEventWindowDef> officialWindows =
                DefDatabase<DiaryEventWindowDef>.AllDefsListForReading
                    .Where(window => window != null && ContainsOfficialPackage(window.enableWhenPackageIdsLoaded))
                    .ToList();
            PawnDiaryRimTestScope.Require(officialWindows.Count == expectedWindows.Count
                    && new HashSet<string>(officialWindows.Select(window => window.defName),
                        StringComparer.Ordinal).SetEquals(expectedWindows.Keys),
                "The loaded official-DLC event-window set drifted; update the matrix with every new, "
                + "removed, or renamed package-gated row.");
            foreach (KeyValuePair<string, string> expected in expectedWindows)
            {
                DiaryEventWindowDef window = officialWindows.First(row => row.defName == expected.Key);
                bool active = OfficialDlcActive(expected.Value);
                PawnDiaryRimTestScope.Require(window.enableWhenPackageIdsLoaded.Count == 1
                        && string.Equals(window.enableWhenPackageIdsLoaded[0], expected.Value,
                            StringComparison.OrdinalIgnoreCase)
                        && window.MissingRequiredPackage() == !active,
                    "Official DLC window '" + expected.Key + "' disagrees with its package/ModsConfig gate.");
                PawnDiaryRimTestScope.Require(window.startSignals != null && window.startSignals.Count > 0,
                    "Official DLC window '" + expected.Key + "' has no start trigger.");
            }
        }

        /// <summary>
        /// Optional adapters suppress generic XML capture only while their hook reports ready. This test
        /// drives the public registry and the exact group availability boundary, then clears the stable
        /// fixture id in a finally block so process-global readiness cannot leak into another suite.
        /// </summary>
        [Test]
        public static void OptionalCaptureCapabilitySuppressesFallbackOnlyWhileReady()
        {
            const string capabilityId = "pawndiary.rimtest.dlc-compat.ready";
            DiaryInteractionGroupDef fallback = new DiaryInteractionGroupDef
            {
                defName = "rimtestDlcCompatFallback",
                disableWhenCaptureCapabilitiesReady = new List<string> { capabilityId }
            };

            try
            {
                PawnDiaryApi.SetCaptureCapabilityReady(capabilityId, false);
                PawnDiaryRimTestScope.Require(!fallback.DisabledByReadyCaptureCapability()
                        && !fallback.UnavailableForCurrentRuntime(),
                    "A not-ready optional adapter incorrectly suppressed the XML fallback.");
                PawnDiaryRimTestScope.Require(PawnDiaryApi.SetCaptureCapabilityReady(capabilityId, true)
                        && PawnDiaryApi.IsCaptureCapabilityReady(capabilityId),
                    "The public integration API could not publish a ready capture capability.");
                PawnDiaryRimTestScope.Require(fallback.DisabledByReadyCaptureCapability()
                        && fallback.UnavailableForCurrentRuntime(),
                    "A ready optional adapter did not suppress its XML fallback.");
            }
            finally
            {
                PawnDiaryApi.SetCaptureCapabilityReady(capabilityId, false);
            }

            PawnDiaryRimTestScope.Require(!PawnDiaryApi.IsCaptureCapabilityReady(capabilityId)
                    && !fallback.UnavailableForCurrentRuntime(),
                "Clearing optional-adapter readiness did not restore fail-open fallback capture.");
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

        private static void RequireSummaryDlcField(
            bool dlcActive,
            bool positiveStateExpected,
            string dlcName,
            string accessorValue,
            string snapshotValue,
            string summaryPrefix,
            string summary)
        {
            PawnDiaryRimTestScope.Require(snapshotValue == accessorValue,
                dlcName + " DlcContext and public pawn-summary snapshot values drifted.");
            if (!dlcActive)
            {
                PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(accessorValue)
                        && summary.IndexOf(summaryPrefix, StringComparison.Ordinal) < 0,
                    "Without " + dlcName + ", the final prompt-summary blob must omit '" + summaryPrefix + "'.");
                return;
            }

            if (positiveStateExpected)
            {
                PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(accessorValue)
                        && summary.IndexOf(summaryPrefix + accessorValue, StringComparison.Ordinal) >= 0,
                    dlcName + " is active, but its positive pawn state did not reach the prompt-summary blob.");
            }
            else
            {
                PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(accessorValue)
                        && summary.IndexOf(summaryPrefix, StringComparison.Ordinal) < 0,
                    dlcName + " has no fixture state, so its final summary field must remain omitted.");
            }
        }

        private static void RequireOfficialPackageFlagAgreement()
        {
            string[] packageIds =
            {
                RoyaltyPackageId,
                IdeologyPackageId,
                BiotechPackageId,
                AnomalyPackageId,
                OdysseyPackageId
            };
            for (int i = 0; i < packageIds.Length; i++)
            {
                bool helperActive = InteractionGroups.AnyPackageLoaded(new List<string> { packageIds[i] });
                PawnDiaryRimTestScope.Require(helperActive == OfficialDlcActive(packageIds[i]),
                    "Package lookup for '" + packageIds[i] + "' disagrees with ModsConfig.");
            }
        }

        private static bool ContainsOfficialPackage(List<string> packageIds)
        {
            if (packageIds == null)
            {
                return false;
            }

            for (int i = 0; i < packageIds.Count; i++)
            {
                string id = packageIds[i];
                if (string.Equals(id, RoyaltyPackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, IdeologyPackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, BiotechPackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, AnomalyPackageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, OdysseyPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OfficialDlcActive(string packageId)
        {
            if (string.Equals(packageId, RoyaltyPackageId, StringComparison.OrdinalIgnoreCase))
                return ModsConfig.RoyaltyActive;
            if (string.Equals(packageId, IdeologyPackageId, StringComparison.OrdinalIgnoreCase))
                return ModsConfig.IdeologyActive;
            if (string.Equals(packageId, BiotechPackageId, StringComparison.OrdinalIgnoreCase))
                return ModsConfig.BiotechActive;
            if (string.Equals(packageId, AnomalyPackageId, StringComparison.OrdinalIgnoreCase))
                return ModsConfig.AnomalyActive;
            if (string.Equals(packageId, OdysseyPackageId, StringComparison.OrdinalIgnoreCase))
                return ModsConfig.OdysseyActive;
            return false;
        }

        private static void RequireMethod(Type type, string name, Type returnType, Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(name, AnyInstance | AnyStatic, null, parameterTypes, null);
            PawnDiaryRimTestScope.Require(method != null && method.ReturnType == returnType,
                type.FullName + "." + name + " no longer matches the signature expected by Pawn Diary.");
        }

        private static void RequireField(Type type, string name, Type fieldType)
        {
            FieldInfo field = type.GetField(name, AnyInstance | AnyStatic);
            PawnDiaryRimTestScope.Require(field != null && field.FieldType == fieldType,
                type.FullName + "." + name + " no longer matches the field expected by Pawn Diary.");
        }

        private static void RequireProperty(Type type, string name, Type propertyType, BindingFlags flags)
        {
            PropertyInfo property = type.GetProperty(name, flags);
            PawnDiaryRimTestScope.Require(property != null && property.PropertyType == propertyType
                    && property.GetGetMethod(true) != null,
                type.FullName + "." + name + " no longer matches the property expected by Pawn Diary.");
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
