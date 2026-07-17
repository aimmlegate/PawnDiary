// In-game flow tests for Pawn Diary's pawn-progression scanner (EVT-15, TEST_COVERAGE_PLAN.md §3).
//
// EVT-15 is the skill/trait milestone tracker — distinct from EVT-04 (situational thought-stage
// progression). The component periodically scans each free colonist and, for a genuine forward step
// (a passion skill crossing a configured milestone, a newly gained trait, a gene-identity change),
// writes one solo diary page. First scan of a pawn BASELINES current state silently so loading an old
// save never bursts a page for every long-standing skill/trait.
//
// Why this drives the per-pawn updater and not the top-level scan (same reason as EVT-04):
// The top-level scanner (DiaryGameComponent.ScanPawnProgressionsForDiaryEvents) enumerates
// SnapshotFreeColonists() — the player's REAL loaded colony. It cannot see the harness's isolated,
// unspawned test colonist, and invoking it would record real progression pages for the developer's
// actual colonists (state this suite could never clean up). The genuine EVT-15 contract
// (baseline / only-upward / exact-context / major-change arc request) lives entirely in the private
// per-pawn updaters ScanPassionSkillMilestones / ScanTraitGain / ObserveGeneIdentity /
// ScanPsylinkLevel / ScanRoyalTitleChange, so this suite drives those directly against the isolated
// pawn — exactly what the top-level scan does per colonist, minus the live-colony enumeration. They
// are private today, so the suite reaches them by reflection; see productionSeamNeeded in the
// integration report for the internal seam that would remove it.
//
// Determinism: the milestone list (DiaryTuningDef.progressionSkillMilestones) and the major-arc
// xenotype/gene tuning are XML-backed, so each test snapshots and forces the exact values it needs (a
// single skill milestone, a pawn's own xenotype marked "major") and restores them in teardown instead
// of depending on shipped defaults or looping until a roll passes. To keep the multi-skill scan
// single-valued, every OTHER skill's passion is cleared so only the one target skill can emit.
//
// No LLM safety: generation is disabled on the harness pawn, and the major-change test forces
// arcReflectionEnabled=false so the arc-request path is entered but declines cleanly — no reflection
// page is generated and no request can leave the game.
//
// The per-pawn PawnProgressionState this suite hands each updater is a test-owned local object, so the
// scanners never mutate any shared/persisted store: the only persistent state the emit touches is the
// pawn's PawnDiaryRecord (its default progressionState + arcSchedule live there and are dropped when
// the harness removes the test pawn's diary). No separate store is dirtied — see the integration report.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-15 Pawn progression.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that the pawn-progression scanner baselines the first observed state without a page,
    /// emits exactly one solo page carrying the exact progression context when a passion skill crosses
    /// a milestone, a new trait is gained, or installed Royalty supplies a psylink/title change; never
    /// re-emits a repeated/non-upward milestone; and marks a major change so it requests the rare
    /// arc-reflection path. Requires a loaded game (the capture pipeline ignores the main menu).
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPawnProgressionFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // A single forced skill milestone keeps the multi-skill scan single-valued and its emitted
        // context exact: a passion skill at level >= 10 records/emits milestone 10 and nothing else.
        private const int ForcedMilestone = 10;
        private const int AbovemilestoneLevel = 20;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Reflection handles for the production per-pawn progression updaters (private today).
        private static MethodInfo scanSkillMethod;     // void ScanPassionSkillMilestones(Pawn, PawnProgressionState, bool baseline)
        private static MethodInfo scanTraitMethod;     // void ScanTraitGain(Pawn, PawnProgressionState)
        private static MethodInfo scanXenotypeMethod;  // void ScanXenotypeChange(Pawn, PawnProgressionState, bool baseline)
        private static MethodInfo scanPsylinkMethod;   // void ScanPsylinkLevel(Pawn, PawnProgressionState, bool baseline)
        private static MethodInfo scanRoyalTitleMethod; // void ScanRoyalTitleChange(Pawn, PawnProgressionState, bool baseline)
        private static MethodInfo observeGeneIdentityMethod; // void ObserveGeneIdentity(Pawn, PawnProgressionState, bool)

        /// <summary>
        /// Opens a scope, enables the progression interaction groups and the Progression signal policy
        /// (so a skill/trait/xenotype page counts as user- and signal-enabled), binds the private
        /// updater reflection handles, and creates one isolated non-generating colonist.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();

            EnableProgressionGroups();
            ForceProgressionSignalEnabled();
            BindReflectionHandles();

            pawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits for leaks even if a test threw partway through.
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
        /// EVT-15. The first scan of a pawn baselines its current skill milestones: the progression
        /// state records the highest reached milestone but the production sink emits NO page (loading a
        /// save with a long-standing passion skill must not burst a catch-up page).
        /// </summary>
        [Test]
        public static void FirstScanBaselinesSkillMilestoneWithoutPage()
        {
            ForceSkillMilestones(ForcedMilestone);
            SkillRecord target = IsolateSinglePassionSkill(AbovemilestoneLevel);
            PawnProgressionState state = new PawnProgressionState();

            scope.RequireNoNewEvent(() => InvokeScanSkill(state, baseline: true));

            PawnDiaryRimTestScope.Require(
                state.HighestSkillMilestone(target.def.defName) == ForcedMilestone,
                "The baseline scan should have recorded the reached skill milestone without emitting a page.");
        }

        /// <summary>
        /// EVT-15. A non-baseline scan of a passion skill that has crossed a milestone above its
        /// previously recorded value emits exactly one solo "SkillMilestone" page whose context carries
        /// the exact skill / level / previous-milestone / passion facts, and advances the saved value.
        /// </summary>
        [Test]
        public static void UpwardSkillMilestoneEmitsOnePageWithExactContext()
        {
            ForceSkillMilestones(ForcedMilestone);
            SkillRecord target = IsolateSinglePassionSkill(AbovemilestoneLevel);
            PawnProgressionState state = new PawnProgressionState();

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeScanSkill(state, baseline: false),
                ProgressionEventData.SkillMilestoneDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent, "progression=" + ProgressionEventData.SkillMilestoneDefName);
            RequireContextContains(diaryEvent, "progression_kind=skill");
            RequireContextContains(diaryEvent, "skill_level=" + ForcedMilestone);
            RequireContextContains(diaryEvent, "previous_skill_milestone=0");
            RequireContextContains(diaryEvent, "passion=major");
            PawnDiaryRimTestScope.Require(
                state.HighestSkillMilestone(target.def.defName) == ForcedMilestone,
                "The emitted milestone should have advanced the saved highest-milestone value.");
        }

        /// <summary>
        /// EVT-15. Once a milestone has emitted, scanning the SAME (non-upward) milestone again on the
        /// next scan records nothing: only a strictly higher milestone is a new progression page.
        /// </summary>
        [Test]
        public static void RepeatedSkillMilestoneEmitsNoPage()
        {
            ForceSkillMilestones(ForcedMilestone);
            IsolateSinglePassionSkill(AbovemilestoneLevel);
            PawnProgressionState state = new PawnProgressionState();

            // First scan records the milestone page.
            scope.FireAndRequireEvent(
                () => InvokeScanSkill(state, baseline: false),
                ProgressionEventData.SkillMilestoneDefName,
                pawn,
                null);

            // Same milestone observed again: not upward, so the next scan emits nothing.
            scope.RequireNoNewEvent(() => InvokeScanSkill(state, baseline: false));
        }

        /// <summary>
        /// EVT-15. The trait-gain scanner baselines the pawn's starting traits silently, then emits
        /// exactly one solo "TraitGained" page for a genuinely new trait (with the trait's identifying
        /// context), and does not re-emit that trait on a repeat scan.
        /// </summary>
        [Test]
        public static void NewTraitGainEmitsOnePageThenRepeatSuppressed()
        {
            PawnProgressionState state = new PawnProgressionState();

            // First scan baselines whatever traits the pawn was generated with — no page.
            scope.RequireNoNewEvent(() => InvokeScanTrait(state));

            // Add one trait the pawn does not already have. Added directly to the live trait list (what
            // the scanner reads) so no GainTrait side effects/conflicts run; removed in teardown.
            TraitDef traitDef = PickUnownedTraitDef();
            int degree = traitDef.degreeDatas[0].degree;
            Trait addedTrait = null;
            scope.RegisterCleanup(() =>
            {
                if (addedTrait != null && pawn?.story?.traits?.allTraits != null)
                {
                    pawn.story.traits.allTraits.Remove(addedTrait);
                }
            });
            addedTrait = new Trait(traitDef, degree, false);
            pawn.story.traits.allTraits.Add(addedTrait);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeScanTrait(state),
                ProgressionEventData.TraitGainedDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent, "progression=" + ProgressionEventData.TraitGainedDefName);
            RequireContextContains(diaryEvent, "progression_kind=trait");
            RequireContextContains(diaryEvent, "trait_def=" + traitDef.defName);

            // The trait is now part of the saved snapshot, so scanning again emits nothing.
            scope.RequireNoNewEvent(() => InvokeScanTrait(state));
        }

        /// <summary>
        /// EVT-15. A MAJOR progression change requests the rare arc-reflection path. The xenotype
        /// scanner is the observable seam: it stamps <c>major_xenotype=true</c> into the emitted page
        /// context — the exact boolean it passes as the arc-candidate flag to the dispatcher, which then
        /// calls ConsiderArcReflectionAfterMajorEvent. This test forces the pawn's own xenotype to count
        /// as major and asserts that marker. Biotech-gated (without it a pawn carries no xenotype defName
        /// to change); a clean no-op when the DLC is absent. Arc reflection itself is forced off so the
        /// request path is entered but declines without generating a page. See gotchas/productionSeamNeeded
        /// for what a full downstream arc-emission assertion additionally needs.
        /// </summary>
        [Test]
        public static void MajorXenotypeChangeMarksArcCandidacy()
        {
            if (!ModsConfig.BiotechActive)
            {
                // No Biotech: XenotypeDefName is always empty, so there is no major xenotype change to
                // observe. Pass cleanly rather than assert on absent DLC state.
                return;
            }

            string currentXenotypeDefName = pawn.genes?.Xenotype?.defName;
            if (string.IsNullOrEmpty(currentXenotypeDefName))
            {
                // Defensive: a pawn with Biotech but no resolvable xenotype cannot exercise this path.
                return;
            }

            ForceMajorXenotype(currentXenotypeDefName);

            PawnProgressionState state = new PawnProgressionState();
            GeneIdentitySnapshot liveIdentity;
            PawnDiaryRimTestScope.Require(DlcContext.TryCaptureGeneIdentity(pawn, out liveIdentity),
                "The Biotech fixture could not project the test pawn's live gene identity.");
            // Simulate a current-version prior observation with different stable xenotype identity but
            // identical membership. This avoids mutating live genes merely to exercise fallback ownership.
            GeneIdentityObservationState observation = state.EnsureBiotechState()
                .EnsureGeneIdentityObservation();
            observation.geneObservationVersion = GeneIdentityObservationPolicy.CurrentVersion;
            observation.xenotypeDefName = "PawnDiaryTest_PreviousXenotype";
            observation.xenotypeLabel = "PawnDiaryTest_PreviousXenotype";
            observation.geneDefNames = new List<string>(liveIdentity.installedGeneDefNames);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeScanXenotype(state, baseline: false),
                ProgressionEventData.GeneIdentityChangedDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent,
                "progression=" + ProgressionEventData.GeneIdentityChangedDefName);
            RequireContextContains(diaryEvent, "gene_identity_transition=true");
            RequireContextContains(diaryEvent,
                "gene_change_cause=" + GeneChangeCauseTokens.ObservedChange);
            RequireContextContains(diaryEvent, "major_xenotype=true");
        }

        /// <summary>
        /// Biotech Phase 5 exact ownership. The real vanilla ReimplantXenogerm body mutates a disposable
        /// recipient, installed Harmony receives both Pawns, one canonical gene page commits, the outer
        /// ability scope is claimed, and replaying the same membership produces no second page.
        /// </summary>
        [Test]
        public static void RealReimplantHookEmitsOnceAndClaimsAbilityScope()
        {
            if (!ModsConfig.BiotechActive) return;
            Pawn caster = scope.CreateAdultColonist();
            if (caster?.genes == null || pawn?.genes == null) return;
            GeneDef added = PickVisibleUninstalledGene(caster, pawn);
            if (added == null) throw new AssertionException(
                "Biotech is active but no visible uninstalled GeneDef was available for reimplant testing.");
            caster.genes.AddGene(added, xenogene: true);

            BiotechGeneAbilityScope abilityScope = BiotechGeneMutationCorrelation.BeginAbility(pawn);
            scope.RegisterCleanup(() => BiotechGeneMutationCorrelation.CloseAbility(abilityScope));
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GeneUtility.ReimplantXenogerm(caster, pawn),
                ProgressionEventData.GeneIdentityChangedDefName,
                pawn,
                null);
            bool claimed = BiotechGeneMutationCorrelation.CloseAbility(abilityScope);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent,
                "gene_change_cause=" + GeneChangeCauseTokens.XenogermReimplant);
            RequireContextContains(diaryEvent, "other_pawn_id=" + caster.GetUniqueLoadID());
            RequireContextContains(diaryEvent, "gene_theme_1=");
            PawnDiaryRimTestScope.Require(claimed,
                "The canonical reimplant page did not claim its enclosing ability scope.");
            scope.RequireNoNewEvent(() => GeneUtility.ReimplantXenogerm(caster, pawn));
        }

        /// <summary>Biotech Phase 5 exact item implantation enters the real vanilla method once.</summary>
        [Test]
        public static void RealImplantItemHookEmitsOneBoundedGenePage()
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null) return;
            GeneDef added = PickVisibleUninstalledGene(null, pawn);
            if (added == null) throw new AssertionException(
                "Biotech is active but no visible uninstalled GeneDef was available for implant testing.");
            Xenogerm xenogerm = ThingMaker.MakeThing(ThingDefOf.Xenogerm) as Xenogerm;
            if (xenogerm == null) throw new AssertionException("Could not create a disposable Xenogerm.");
            FieldInfo geneSetField = typeof(GeneSetHolderBase).GetField(
                "geneSet",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (geneSetField == null) throw new AssertionException(
                "RimWorld 1.6 GeneSetHolderBase.geneSet changed; update the fixture defensively.");
            GeneSet geneSet = new GeneSet();
            geneSet.AddGene(added);
            geneSetField.SetValue(xenogerm, geneSet);
            xenogerm.xenotypeName = "Pawn Diary RimTest";
            scope.RegisterCleanup(() =>
            {
                if (xenogerm != null && !xenogerm.Destroyed) xenogerm.Destroy();
            });

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GeneUtility.ImplantXenogermItem(pawn, xenogerm),
                ProgressionEventData.GeneIdentityChangedDefName,
                pawn,
                null);
            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent,
                "gene_change_cause=" + GeneChangeCauseTokens.XenogermImplant);
            RequireContextContains(diaryEvent, "gene_identity_transition=true");
            RequireContextContains(diaryEvent, "narrative_facets=identity_transition");
        }

        /// <summary>
        /// Biotech Phase 5. The guarded live projector returns detached current membership only with
        /// Biotech, and the observation adapter establishes a versioned silent baseline. A later
        /// observation can advance the retained scalar keys when output is disabled without a page.
        /// </summary>
        [Test]
        public static void GeneIdentityProjectionAndVersionedBaselineAreSilent()
        {
            GeneIdentitySnapshot identity;
            bool captured = DlcContext.TryCaptureGeneIdentity(pawn, out identity);
            if (!ModsConfig.BiotechActive)
            {
                PawnDiaryRimTestScope.Require(!captured && identity == null,
                    "Biotech is inactive but live gene projection returned a snapshot.");
                PawnProgressionState inactiveState = new PawnProgressionState();
                scope.RequireNoNewEvent(() => InvokeObserveGeneIdentity(inactiveState, true));
                PawnDiaryRimTestScope.Require(inactiveState.EnsureBiotechState()
                        .EnsureGeneIdentityObservation().geneObservationVersion == 0,
                    "Biotech-inactive observation should not invent a baseline version.");
                return;
            }

            PawnDiaryRimTestScope.Require(captured && identity != null,
                "Biotech is active but the guarded gene projector returned no snapshot.");
            PawnDiaryRimTestScope.Require(
                identity.xenotypeDefName == DlcContext.XenotypeDefName(pawn)
                    && identity.xenotypeLabel == DlcContext.XenotypeLabel(pawn),
                "The detached identity snapshot did not preserve the live xenotype identity.");
            HashSet<string> uniqueDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> installedDefNames = new HashSet<string>(
                identity.installedGeneDefNames, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < identity.genes.Count; i++)
            {
                GeneFact fact = identity.genes[i];
                PawnDiaryRimTestScope.Require(fact != null
                        && !string.IsNullOrWhiteSpace(fact.defName)
                        && !string.IsNullOrWhiteSpace(fact.label)
                        && uniqueDefNames.Add(fact.defName),
                    "Live gene projection returned a blank or duplicate detached fact.");
                PawnDiaryRimTestScope.Require(
                    fact.label.IndexOf('\n') < 0 && fact.label.IndexOf('\r') < 0
                        && fact.description.IndexOf('\n') < 0 && fact.description.IndexOf('\r') < 0,
                    "Live gene projection leaked multi-line label/description text.");
            }
            PawnDiaryRimTestScope.Require(
                uniqueDefNames.IsSubsetOf(installedDefNames),
                "An active projected gene fact was absent from installed membership.");

            PawnProgressionState state = new PawnProgressionState
            {
                baselineProgressionOnNextScan = false,
                lastObservedXenotypeDefName = "Legacy_Previous",
                lastObservedXenotypeLabel = "legacy previous"
            };
            scope.RequireNoNewEvent(() => InvokeObserveGeneIdentity(state, false));
            GeneIdentityObservationState observed = state.EnsureBiotechState()
                .EnsureGeneIdentityObservation();
            PawnDiaryRimTestScope.Require(
                observed.geneObservationVersion == GeneIdentityObservationPolicy.CurrentVersion,
                "First live gene observation did not set the frozen current version.");
            PawnDiaryRimTestScope.Require(
                observed.xenotypeDefName == identity.xenotypeDefName
                    && observed.xenotypeLabel == identity.xenotypeLabel,
                "First live gene observation did not copy xenotype identity.");
            PawnDiaryRimTestScope.Require(
                new HashSet<string>(observed.geneDefNames, StringComparer.OrdinalIgnoreCase)
                    .SetEquals(installedDefNames),
                "First live gene observation did not copy exact installed-gene membership.");
            PawnDiaryRimTestScope.Require(
                state.lastObservedXenotypeDefName == identity.xenotypeDefName,
                "Old-save migration did not silently advance the retained xenotype scalar.");

            state.lastObservedXenotypeDefName = "Disabled_Output_Previous";
            state.lastObservedXenotypeLabel = "disabled output previous";
            scope.RequireNoNewEvent(() => InvokeObserveGeneIdentity(state, true));
            PawnDiaryRimTestScope.Require(
                state.lastObservedXenotypeDefName == identity.xenotypeDefName
                    && state.lastObservedXenotypeLabel == identity.xenotypeLabel,
                "Observation while output is disabled did not advance the retained scalar baseline.");
        }

        /// <summary>
        /// EVT-15 / Royalty. A real loaded psylink hediff is read through the production scanner and
        /// becomes one upward-only progression page. Without Royalty the fixture cleanly reports not
        /// applicable because the package-owned hediff Def is absent.
        /// </summary>
        [Test]
        public static void RoyaltyPsylinkScannerEmitsOnceFromRealHediff()
        {
            if (!ModsConfig.RoyaltyActive)
            {
                // A compatibility mod may legitimately supply its own configured psylink-style hediff,
                // so absence of Royalty alone is not proof that the generic scanner must read zero.
                return;
            }

            HediffDef psylinkDef = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicAmplifier");
            if (psylinkDef == null)
            {
                throw new AssertionException("Royalty is active but PsychicAmplifier was not loaded.");
            }

            Hediff psylink = pawn.health.AddHediff(psylinkDef);
            scope.RegisterCleanup(() =>
            {
                if (psylink != null && pawn?.health?.hediffSet?.hediffs?.Contains(psylink) == true)
                {
                    pawn.health.RemoveHediff(psylink);
                }
            });

            PawnProgressionState state = new PawnProgressionState();
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeScanPsylink(state, baseline: false),
                ProgressionEventData.PsylinkLevelDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent, "progression=" + ProgressionEventData.PsylinkLevelDefName);
            RequireContextContains(diaryEvent, "progression_kind=psylink");
            RequireContextContains(diaryEvent, "previous_psylink_level=0");
            RequireContextContains(diaryEvent, "psylink_level=" + state.highestPsylinkLevelRecorded);
            PawnDiaryRimTestScope.Require(state.highestPsylinkLevelRecorded > 0,
                "The real PsychicAmplifier hediff did not advance the saved psylink level.");

            scope.RequireNoNewEvent(() => InvokeScanPsylink(state, baseline: false));
        }

        /// <summary>
        /// EVT-15 / Royalty. A real loaded royal title flows through the guarded DLC accessor and the
        /// private title-change scanner into one exact progression page; the unchanged title is then
        /// suppressed on the next scan. The disposable title row is removed during teardown.
        /// </summary>
        [Test]
        public static void RoyaltyTitleScannerEmitsPromotionOnce()
        {
            if (!ModsConfig.RoyaltyActive)
            {
                PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(DlcContext.RoyalTitleDefName(pawn)),
                    "Royalty is inactive but the test pawn exposed a royal title.");
                return;
            }

            if (pawn.royalty == null)
            {
                throw new AssertionException("Royalty is active but the test pawn has no royalty tracker.");
            }

            RoyalTitleDef titleDef = null;
            List<RoyalTitleDef> titleDefs = DefDatabase<RoyalTitleDef>.AllDefsListForReading;
            for (int i = 0; i < titleDefs.Count; i++)
            {
                RoyalTitleDef candidate = titleDefs[i];
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.defName)
                    && (titleDef == null || candidate.seniority > titleDef.seniority))
                {
                    titleDef = candidate;
                }
            }

            if (titleDef == null)
            {
                throw new AssertionException("Royalty is active but no RoyalTitleDef was loaded.");
            }

            RoyalTitle fixtureTitle = new RoyalTitle
            {
                def = titleDef,
                faction = Faction.OfEmpire ?? scope.PlayerFaction,
                pawn = pawn,
                receivedTick = Find.TickManager?.TicksGame ?? 0
            };
            pawn.royalty.AllTitlesForReading.Add(fixtureTitle);
            scope.RegisterCleanup(() => pawn?.royalty?.AllTitlesForReading?.Remove(fixtureTitle));

            PawnProgressionState state = new PawnProgressionState
            {
                lastObservedRoyalTitleDefName = "PawnDiaryTest_PreviousTitle",
                lastObservedRoyalTitleLabel = "previous title"
            };
            string currentTitleDef = DlcContext.RoyalTitleDefName(pawn);
            string currentTitleLabel = DlcContext.RoyalTitleLabel(pawn);
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeScanRoyalTitle(state, baseline: false),
                ProgressionEventData.RoyalTitleChangedDefName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContextContains(diaryEvent, "progression=" + ProgressionEventData.RoyalTitleChangedDefName);
            RequireContextContains(diaryEvent, "progression_kind=royal_title");
            RequireContextContains(diaryEvent, "previous_title=previous title");
            RequireContextContains(diaryEvent, "title=" + currentTitleLabel);
            RequireContextContains(diaryEvent, "title_def=" + currentTitleDef);
            PawnDiaryRimTestScope.Require(state.lastObservedRoyalTitleDefName == currentTitleDef,
                "The title scanner did not persist the newly observed royal title.");

            scope.RequireNoNewEvent(() => InvokeScanRoyalTitle(state, baseline: false));
        }

        // ----- production-driver helpers ----------------------------------------------------------

        private static void InvokeScanSkill(PawnProgressionState state, bool baseline)
        {
            Invoke(scanSkillMethod, new object[] { pawn, state, baseline });
        }

        private static void InvokeScanTrait(PawnProgressionState state)
        {
            Invoke(scanTraitMethod, new object[] { pawn, state });
        }

        private static void InvokeScanXenotype(PawnProgressionState state, bool baseline)
        {
            Invoke(scanXenotypeMethod, new object[] { pawn, state, baseline });
        }

        private static void InvokeScanPsylink(PawnProgressionState state, bool baseline)
        {
            Invoke(scanPsylinkMethod, new object[] { pawn, state, baseline });
        }

        private static void InvokeScanRoyalTitle(PawnProgressionState state, bool baseline)
        {
            Invoke(scanRoyalTitleMethod, new object[] { pawn, state, baseline });
        }

        private static void InvokeObserveGeneIdentity(
            PawnProgressionState state,
            bool advanceLegacyXenotypeBaseline)
        {
            Invoke(observeGeneIdentityMethod,
                new object[] { pawn, state, advanceLegacyXenotypeBaseline });
        }

        private static void Invoke(MethodInfo method, object[] args)
        {
            try
            {
                method.Invoke(scope.Component, args);
            }
            catch (TargetInvocationException e)
            {
                // Surface the real failure, not the reflection wrapper.
                throw e.InnerException ?? e;
            }
        }

        // ----- state-shaping helpers --------------------------------------------------------------

        /// <summary>
        /// Makes exactly one enabled skill a Major-passion skill at a level well above the forced
        /// milestone and clears the passion on every other skill, so the whole-skills scan can only emit
        /// for this one skill. Every skill's original passion and level is restored in teardown.
        /// </summary>
        private static SkillRecord IsolateSinglePassionSkill(int targetLevel)
        {
            if (pawn?.skills?.skills == null)
            {
                throw new AssertionException("EVT-15 test pawn has no skill tracker.");
            }

            SkillRecord target = null;
            List<SkillRecord> skills = pawn.skills.skills;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord skill = skills[i];
                if (skill == null)
                {
                    continue;
                }

                Passion originalPassion = skill.passion;
                int originalLevel = skill.levelInt;
                scope.RegisterCleanup(() =>
                {
                    skill.passion = originalPassion;
                    skill.levelInt = originalLevel;
                });

                if (target == null && !skill.TotallyDisabled)
                {
                    target = skill;
                }
                else
                {
                    skill.passion = Passion.None;
                }
            }

            if (target == null)
            {
                throw new AssertionException("EVT-15 found no enabled skill on the test pawn.");
            }

            target.passion = Passion.Major;
            target.levelInt = targetLevel;
            return target;
        }

        /// <summary>Returns the first loaded TraitDef (with degree data) the pawn does not already hold.</summary>
        private static TraitDef PickUnownedTraitDef()
        {
            List<TraitDef> defs = DefDatabase<TraitDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                TraitDef def = defs[i];
                if (def?.degreeDatas == null || def.degreeDatas.Count == 0)
                {
                    continue;
                }

                if (pawn.story?.traits != null && pawn.story.traits.HasTrait(def))
                {
                    continue;
                }

                return def;
            }

            throw new AssertionException("EVT-15 could not find a TraitDef the test pawn lacks.");
        }

        // ----- tuning / settings forcing ----------------------------------------------------------

        /// <summary>
        /// Replaces the XML-backed skill milestone list with a single known value for the duration of a
        /// test, restoring the original list reference in teardown.
        /// </summary>
        private static void ForceSkillMilestones(params int[] milestones)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            List<int> original = tuning.progressionSkillMilestones;
            tuning.progressionSkillMilestones = new List<int>(milestones);
            scope.RegisterCleanup(() => tuning.progressionSkillMilestones = original);
        }

        /// <summary>
        /// Forces the arc tuning so the given xenotype defName counts as a major-arc change, and turns
        /// arc reflection off so the request path is entered but declines without generating a page.
        /// Every mutated field is snapshotted and restored in teardown.
        /// </summary>
        private static void ForceMajorXenotype(string xenotypeDefName)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            List<string> originalMajor = tuning.arcReflectionMajorXenotypeDefNames;
            int originalThreshold = tuning.arcReflectionMajorSeverityThreshold;
            bool originalArcEnabled = tuning.arcReflectionEnabled;

            tuning.arcReflectionMajorXenotypeDefNames = new List<string> { xenotypeDefName };
            tuning.arcReflectionMajorSeverityThreshold = 0;
            tuning.arcReflectionEnabled = false;

            scope.RegisterCleanup(() =>
            {
                tuning.arcReflectionMajorXenotypeDefNames = originalMajor;
                tuning.arcReflectionMajorSeverityThreshold = originalThreshold;
                tuning.arcReflectionEnabled = originalArcEnabled;
            });
        }

        // ----- setup helpers ----------------------------------------------------------------------

        /// <summary>
        /// Enables the diary interaction group each progression token classifies into, so
        /// PawnDiarySettings.IsGroupEnabled returns true for skill / trait / xenotype pages. The harness
        /// snapshotted groupEnabled in Begin and restores it verbatim in teardown.
        /// </summary>
        private static void EnableProgressionGroups()
        {
            EnableProgressionGroup(ProgressionEventData.SkillMilestoneDefName);
            EnableProgressionGroup(ProgressionEventData.TraitGainedDefName);
            EnableProgressionGroup(ProgressionEventData.XenotypeChangedDefName);
            EnableProgressionGroup(ProgressionEventData.GeneIdentityChangedDefName);
            EnableProgressionGroup(ProgressionEventData.PsylinkLevelDefName);
            EnableProgressionGroup(ProgressionEventData.RoyalTitleChangedDefName);
        }

        private static void EnableProgressionGroup(string progressionDefName)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyProgression(progressionDefName);
            if (group != null && PawnDiaryMod.Settings != null)
            {
                PawnDiaryMod.Settings.SetGroupEnabled(group.defName, true);
            }
        }

        /// <summary>Forces the Progression signal policy on, restoring its original state in teardown.</summary>
        private static void ForceProgressionSignalEnabled()
        {
            DiarySignalPolicyDef policy = DiarySignalPolicies.ForKey(DiarySignalPolicies.Progression);
            bool original = policy.enabled;
            policy.enabled = true;
            scope.RegisterCleanup(() => policy.enabled = original);
        }

        private static void BindReflectionHandles()
        {
            scanSkillMethod = typeof(DiaryGameComponent).GetMethod("ScanPassionSkillMilestones", PrivateInstance);
            scanTraitMethod = typeof(DiaryGameComponent).GetMethod("ScanTraitGain", PrivateInstance);
            scanXenotypeMethod = typeof(DiaryGameComponent).GetMethod("ScanXenotypeChange", PrivateInstance);
            scanPsylinkMethod = typeof(DiaryGameComponent).GetMethod("ScanPsylinkLevel", PrivateInstance);
            scanRoyalTitleMethod = typeof(DiaryGameComponent).GetMethod("ScanRoyalTitleChange", PrivateInstance);
            observeGeneIdentityMethod = typeof(DiaryGameComponent).GetMethod("ObserveGeneIdentity", PrivateInstance);

            RequireHandle(scanSkillMethod, "ScanPassionSkillMilestones");
            RequireHandle(scanTraitMethod, "ScanTraitGain");
            RequireHandle(scanXenotypeMethod, "ScanXenotypeChange");
            RequireHandle(scanPsylinkMethod, "ScanPsylinkLevel");
            RequireHandle(scanRoyalTitleMethod, "ScanRoyalTitleChange");
            RequireHandle(observeGeneIdentityMethod, "ObserveGeneIdentity");
        }

        private static void RequireHandle(object handle, string name)
        {
            if (handle == null)
            {
                throw new AssertionException(
                    "EVT-15 could not bind the production progression member '" + name
                    + "'. If it was renamed or made internal, update this suite (see productionSeamNeeded).");
            }
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The progression event context did not contain the expected fact '" + expectedFragment + "'.");
        }

        private static GeneDef PickVisibleUninstalledGene(Pawn first, Pawn second)
        {
            List<GeneDef> defs = DefDatabase<GeneDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                GeneDef def = defs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.defName) || def.displayCategory == null)
                    continue;
                if (HasGene(first, def) || HasGene(second, def)) continue;
                return def;
            }
            return null;
        }

        private static bool HasGene(Pawn candidate, GeneDef def)
        {
            List<Gene> genes = candidate?.genes?.GenesListForReading;
            if (genes == null) return false;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i]?.def == def) return true;
            }
            return false;
        }
    }
}
