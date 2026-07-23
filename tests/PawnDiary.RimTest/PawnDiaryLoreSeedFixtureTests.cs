// In-game lore-seed lifecycle tests (design/LORE_MEMORY_SEED_PLAN.md L2 §8.1/§8.2/§14.3).
//
// Proves the L2 behaviors inside a real loaded game with a temporary DiaryLoreSeedDef injected
// into the DefDatabase:
//   1. Lazy plan + deposit: the first eligible EventFactory page plans the roster, persists it,
//      and deposits the sentinel fragment BEFORE recall; a second page deposits nothing new.
//   2. Toggle semantics: enableLoreSeeds=false suppresses planning/deposit entirely and preserves
//      already-deposited rows (disabled is never deleted).
//   3. Removed target: a roster name whose Def disappeared is skipped without replacement.
//   4. Scribe round-trip: PawnLoreSeedState survives a real save/load cycle and PostLoadInit
//      cleans blank/duplicate entries.
//
// All fragile scaffolding — isolated pawns, settings snapshot/restore, cleanup — lives in the
// shared PawnDiaryRimTestScope harness.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded-game verification of the L2 lore-seed plan/deposit/toggle lifecycle.</summary>
    [TestSuite]
    public static class PawnDiaryLoreSeedFixtureTests
    {
        private const string TestSeedDefName = "PDTest_LoreSeed_Generic";

        private static readonly FieldInfo MemoriesField =
            typeof(DiaryGameComponent).GetField("memories",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo LoreStatesField =
            typeof(DiaryGameComponent).GetField("pawnLoreSeedStates",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo LoreStateIndexField =
            typeof(DiaryGameComponent).GetField("loreSeedStateByPawnId",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private const string TestProgressionSeedDefName = "PDTest_LoreSeed_Progression";

        private static readonly MethodInfo DepositMethod =
            typeof(DiaryGameComponent).GetMethod("DepositMemoryFragments",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawnA;
        private static Pawn pawnB;
        private static DiaryLoreSeedDef testSeedDef;
        private static List<DiaryLoreSeedDef> hiddenCatalog;
        private static List<string> registeredTestDefNames;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();

            if (Find.CurrentMap == null)
            {
                throw new AssertionException("Lore seed tests require a loaded map with a colony.");
            }

            if (MemoriesField == null || LoreStatesField == null || LoreStateIndexField == null)
            {
                throw new AssertionException(
                    "Reflection handle for a DiaryGameComponent memory/lore field is null — a field was renamed.");
            }

            // ORDER MATTERS: CreateAdultColonist fires a real ARRIVAL event through the funnel
            // (SetFaction patch -> ArrivalSignal -> AddSoloEvent), which runs the lazy lore-seed
            // lifecycle. The shipped L3 catalog must be hidden and the settings pinned BEFORE any
            // pawn exists, or SetUp itself plans rosters from the 40 shipped seeds and every
            // exact roster/count assertion below goes non-deterministic. The test Def registers
            // AFTER pawn creation so the arrival events plan nothing (empty catalog) and each
            // test body's first event is genuinely the pawn's first eligible one.
            PawnDiaryMod.Settings.enableMemorySystem = true;
            PawnDiaryMod.Settings.enableLoreSeeds = true;
            HideShippedCatalog();
            registeredTestDefNames = new List<string>();

            pawnA = scope.CreateAdultColonist();
            pawnB = scope.CreateAdultColonist();

            RegisterTestSeedDef();
        }

        [AfterEach]
        public static void TearDown()
        {
            // Each restore step runs even if an earlier one throws: leaving the shipped catalog
            // hidden or the scope un-torn-down would poison every later suite in the run.
            try
            {
                try
                {
                    CleanupLoreForTestPawns();
                }
                finally
                {
                    try
                    {
                        RemoveDefIfPresent(TestSeedDefName);
                        RemoveDefIfPresent(TestProgressionSeedDefName);
                        if (registeredTestDefNames != null)
                        {
                            for (int i = 0; i < registeredTestDefNames.Count; i++)
                            {
                                RemoveDefIfPresent(registeredTestDefNames[i]);
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            RestoreShippedCatalog();
                        }
                        finally
                        {
                            scope?.TearDown();
                        }
                    }
                }
            }
            finally
            {
                scope = null;
                pawnA = null;
                pawnB = null;
                testSeedDef = null;
                hiddenCatalog = null;
                registeredTestDefNames = null;
            }
        }

        // ── Test 1: lazy plan + deposit + idempotency ────────────────────────────────────────────────

        /// <summary>
        /// The first eligible page plans and persists the roster and deposits the lore sentinel
        /// fragment with correct provenance, the implied lore tag, tier importance, and a
        /// biological-age-clamped narrative offset; a second page deposits nothing new.
        /// </summary>
        [Test]
        public static void FirstEventPlansRosterAndDepositsSeedIdempotently()
        {
            string pawnAId = pawnA.GetUniqueLoadID();

            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");

            PawnLoreSeedState state = LoreStateFor(pawnAId);
            Require(state != null, "The first eligible event must persist a lore roster for pawn A.");
            Require(state.initialTargetDefNames.Contains(TestSeedDefName),
                "The persisted roster must contain the test seed Def name.");

            MemoryFragment seed = FindLoreFragment(pawnAId);
            Require(seed != null, "The first eligible event must deposit the lore seed fragment.");
            Require(seed.sourceEventId == "loreseed:" + TestSeedDefName,
                "Lore deposit identity must be the stable per-Def sentinel, got '" + seed.sourceEventId + "'.");
            Require(seed.loreSeedDefName == TestSeedDefName,
                "The fragment must carry its lore Def provenance.");
            Require(seed.tags != null && seed.tags.Contains("lore"),
                "Every lore fragment must carry the implied 'lore' tag.");
            Require(seed.text == testSeedDef.text,
                "The deposited prose must be the Def's current text.");
            Require(Math.Abs(seed.importance - 0.35f) < 0.001f,
                "An ordinary lore seed deposits at the ordinary importance (0.35), got " + seed.importance + ".");
            Require(seed.createdTick == seed.lastRecalledTick,
                "A fresh lore deposit must have lastRecalledTick == createdTick (never backdated).");
            long bioAge = pawnA.ageTracker.AgeBiologicalTicks;
            Require(seed.narrativeAgeOffsetTicks > 0
                && seed.narrativeAgeOffsetTicks <= Math.Min(7200000L, bioAge),
                "The narrative offset must be positive and clamped to min(policy, biological age), got "
                + seed.narrativeAgeOffsetTicks + " for age " + bioAge + ".");

            int countAfterFirst = CountLoreFragments(pawnAId);
            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            Require(CountLoreFragments(pawnAId) == countAfterFirst,
                "A second event must not duplicate the lore deposit (sentinel idempotency).");
            Require(LoreStateFor(pawnAId).initialTargetDefNames.Count
                == state.initialTargetDefNames.Count,
                "A second event must not grow the persisted roster.");
        }

        // ── Test 2: toggle semantics ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Disabling lore seeds suppresses planning and deposits without touching existing rows;
        /// re-enabling resumes the lazy lifecycle.
        /// </summary>
        [Test]
        public static void DisabledToggleSuppressesDepositAndPreservesRows()
        {
            string pawnAId = pawnA.GetUniqueLoadID();

            PawnDiaryMod.Settings.enableLoreSeeds = false;
            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            Require(LoreStateFor(pawnAId) == null,
                "With lore seeds disabled, no roster may be planned.");
            Require(CountLoreFragments(pawnAId) == 0,
                "With lore seeds disabled, no lore fragment may be deposited.");

            PawnDiaryMod.Settings.enableLoreSeeds = true;
            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            Require(CountLoreFragments(pawnAId) == 1,
                "Re-enabling lore seeds must resume the lazy deposit.");

            PawnDiaryMod.Settings.enableLoreSeeds = false;
            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            Require(CountLoreFragments(pawnAId) == 1,
                "Disabling again must neither deposit more nor delete the existing lore row.");
        }

        // ── Test 3: removed target ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// A persisted roster name whose Def was removed is skipped without replacement: the
        /// roster keeps the name, no new fragment appears, and no error is thrown.
        /// </summary>
        [Test]
        public static void RemovedTargetIsSkippedWithoutReplacement()
        {
            string pawnAId = pawnA.GetUniqueLoadID();

            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            MemoryFragment seed = FindLoreFragment(pawnAId);
            Require(seed != null, "Setup: the lore seed must deposit first.");

            // Simulate a later eviction of the row plus removal of the authored Def.
            GetMemories().RemoveByIds(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                seed.memoryId
            });
            RemoveTestSeedDef();

            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            Require(CountLoreFragments(pawnAId) == 0,
                "A removed target Def must never re-deposit or be replaced.");
            PawnLoreSeedState state = LoreStateFor(pawnAId);
            Require(state != null && state.initialTargetDefNames.Contains(TestSeedDefName),
                "The roster must keep the removed Def name as history (no resampling).");
        }

        // ── Test 3b: partial-deposit retry ───────────────────────────────────────────────────────────

        /// <summary>
        /// A roster target whose fragment row was later removed (eviction, save surgery) while the
        /// Def still exists re-deposits under the SAME sentinel on the next eligible event — the
        /// retry path deposits missing targets only and never resamples the roster (§6/§7.1).
        /// </summary>
        [Test]
        public static void EvictedTargetRedepositsWithoutResampling()
        {
            string pawnAId = pawnA.GetUniqueLoadID();

            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            MemoryFragment seed = FindLoreFragment(pawnAId);
            Require(seed != null, "Setup: the lore seed must deposit first.");
            List<string> rosterBefore = new List<string>(
                LoreStateFor(pawnAId).initialTargetDefNames);

            GetMemories().RemoveByIds(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                seed.memoryId
            });
            Require(CountLoreFragments(pawnAId) == 0, "Setup: the lore row must be removed.");

            AddPairwiseLoreEvent(pawnA, pawnB, "DeepTalk");
            MemoryFragment redeposited = FindLoreFragment(pawnAId);
            Require(redeposited != null,
                "A missing roster target with a live Def must re-deposit on the next event.");
            Require(redeposited.sourceEventId == seed.sourceEventId,
                "The retry must reuse the stable per-Def sentinel identity.");
            List<string> rosterAfter = LoreStateFor(pawnAId).initialTargetDefNames;
            Require(rosterAfter.Count == rosterBefore.Count
                && rosterAfter.Contains(TestSeedDefName),
                "The retry must never resample or grow the persisted roster.");
        }

        // ── Test 3c: L5 progression attachment ───────────────────────────────────────────────────────

        /// <summary>
        /// LORE_MEMORY_SEED_PLAN §8.3 (L5): a registered identity-changing event attaches at most
        /// one owner-only progression seed with the synthetic non-colliding sentinel, zero
        /// narrative offset, and a lifetime-history record; the witness never receives one, and a
        /// second event of the same family cannot reuse the exhausted Def.
        /// </summary>
        [Test]
        public static void ProgressionEventAttachesOwnerOnlySeed()
        {
            RegisterProgressionSeedDef();
            string pawnAId = pawnA.GetUniqueLoadID();
            string pawnBId = pawnB.GetUniqueLoadID();

            DiaryEvent progressionEvent = AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift");

            MemoryFragment seed = FindProgressionFragment(pawnAId);
            Require(seed != null,
                "A registered XenotypeChanged event must attach a progression lore seed to the owner.");
            Require(seed.sourceEventId ==
                "loreseed-progression:" + progressionEvent.eventId + ":" + TestProgressionSeedDefName,
                "The progression sentinel must be eventId + seed Def, got '" + seed.sourceEventId + "'.");
            Require(seed.loreSeedDefName == TestProgressionSeedDefName,
                "The fragment must carry its progression Def provenance.");
            Require(seed.narrativeAgeOffsetTicks == 0,
                "A progression memory is a memory OF the change: zero narrative offset (§8.3).");
            Require(seed.createdTick == seed.lastRecalledTick,
                "A fresh progression deposit must never be backdated.");

            Require(FindProgressionFragment(pawnBId) == null,
                "The witness (recipient) must never receive a progression lore seed.");

            PawnLoreSeedState state = LoreStateFor(pawnAId);
            Require(state != null
                && state.progressionDefNamesEverDeposited.Contains(TestProgressionSeedDefName),
                "The progression lifetime history must record the actual deposit.");

            int countAfterFirst = CountProgressionFragments(pawnAId);
            AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift again");
            Require(CountProgressionFragments(pawnAId) == countAfterFirst,
                "Exact-Def lifetime uniqueness must block a second deposit of the same seed.");
        }

        // ── Test 3d: replay barrier ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// §8.3: the progression attachment fires only when the ordinary lived memory FRESHLY
        /// deposited. Re-running the deposit funnel for the same event (a staged replay) finds
        /// the lived sentinel already present and never attaches a second progression seed.
        /// </summary>
        [Test]
        public static void ReplayedDepositNeverAttachesProgressionSeedTwice()
        {
            if (DepositMethod == null)
            {
                throw new AssertionException(
                    "Reflection handle for DepositMemoryFragments is null — method was renamed.");
            }

            RegisterProgressionSeedDef();
            string pawnAId = pawnA.GetUniqueLoadID();

            DiaryEvent progressionEvent = AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift");
            Require(CountProgressionFragments(pawnAId) == 1,
                "Setup: the first pass must attach exactly one progression seed.");

            DepositMethod.Invoke(scope.Component, new object[] { progressionEvent, pawnA });
            Require(CountProgressionFragments(pawnAId) == 1,
                "A replayed deposit pass must never attach a second progression seed.");
        }

        // ── Test 3e: toggle gate ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// With lore seeds disabled, a matching identity-changing event still deposits its
        /// ordinary lived memory but never attaches a progression seed; re-enabling does not
        /// retroactively attach one for the already-consumed event.
        /// </summary>
        [Test]
        public static void DisabledToggleSuppressesProgressionAttachment()
        {
            RegisterProgressionSeedDef();
            string pawnAId = pawnA.GetUniqueLoadID();

            PawnDiaryMod.Settings.enableLoreSeeds = false;
            DiaryEvent progressionEvent = AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift");
            Require(GetMemories().HasDeposit(pawnAId, progressionEvent.eventId),
                "The ordinary lived memory must still deposit while lore is disabled.");
            Require(CountProgressionFragments(pawnAId) == 0,
                "Disabled lore seeds must suppress the progression attachment.");

            PawnDiaryMod.Settings.enableLoreSeeds = true;
            Require(CountProgressionFragments(pawnAId) == 0,
                "Re-enabling must not retroactively attach a seed for a consumed event.");
        }

        // ── Test 3f: progression lifetime cap ────────────────────────────────────────────────────────

        /// <summary>
        /// §6: at most four progression seeds per pawn lifetime. With five matching candidate
        /// Defs and five events, exactly four deposit and the history stops at four.
        /// </summary>
        [Test]
        public static void ProgressionLifetimeCapStopsAtFour()
        {
            for (int i = 0; i < 5; i++)
            {
                AddProgressionTestDef("PDTest_LoreSeed_ProgCap" + i,
                    LoreSeedTokens.TierOrdinary, hediffDefNames: null);
            }

            string pawnAId = pawnA.GetUniqueLoadID();
            for (int i = 0; i < 5; i++)
            {
                AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift " + i);
            }

            Require(CountProgressionFragments(pawnAId) == 4,
                "The progression lifetime cap must stop deposits at four, got "
                + CountProgressionFragments(pawnAId) + ".");
            PawnLoreSeedState state = LoreStateFor(pawnAId);
            Require(state != null && state.progressionDefNamesEverDeposited.Count == 4,
                "The lifetime history must record exactly four progression deposits.");
        }

        // ── Test 3g: core progression seeds ──────────────────────────────────────────────────────────

        /// <summary>
        /// §8.3/§4: a core progression seed (exact hediff evidence) deposits at core importance
        /// and records the core lifetime history; the two-core lifetime cap blocks a third even
        /// though progression capacity remains.
        /// </summary>
        [Test]
        public static void CoreProgressionSeedsRespectCoreLifetimeCap()
        {
            HediffDef flu = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            if (flu == null)
            {
                throw new AssertionException("Base-game hediff 'Flu' not found for the core fixture.");
            }

            pawnA.health.AddHediff(flu);
            for (int i = 0; i < 3; i++)
            {
                AddProgressionTestDef("PDTest_LoreSeed_ProgCore" + i,
                    LoreSeedTokens.TierCore, new List<string> { "Flu" });
            }

            string pawnAId = pawnA.GetUniqueLoadID();
            for (int i = 0; i < 3; i++)
            {
                AddRawPairEvent(pawnA, pawnB, "XenotypeChanged", "identity shift " + i);
            }

            Require(CountProgressionFragments(pawnAId) == 2,
                "The two-core lifetime cap must stop core progression deposits at two, got "
                + CountProgressionFragments(pawnAId) + ".");
            PawnLoreSeedState state = LoreStateFor(pawnAId);
            Require(state != null && state.coreDefNamesEverDeposited.Count == 2,
                "The core lifetime history must record exactly two deposits.");

            MemoryFragment coreSeed = FindProgressionFragment(pawnAId);
            Require(coreSeed != null && coreSeed.importance >= 0.8f,
                "A core progression seed must deposit at core importance, got "
                + (coreSeed == null ? "none" : coreSeed.importance.ToString()) + ".");
        }

        // ── Test 4: Scribe round-trip ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The additive MemoryFragment lore keys survive a real Scribe save/load, and PostLoadInit
        /// repairs hand-edited rows: whitespace provenance trims to empty (a lived memory) and a
        /// negative narrative offset clamps to zero (§16 G1/G2).
        /// </summary>
        [Test]
        public static void FragmentLoreFieldsScribeRoundTripAndRepair()
        {
            MemoryFragment fragment = new MemoryFragment
            {
                memoryId = "lore-rt-1",
                pawnId = "Thing_Human99999",
                sourceEventId = "loreseed:LoreSeed_RoundTrip",
                text = "A remembered story.",
                tags = new List<string> { "lore", "dread" },
                keywords = new List<string> { "mechanoid" },
                importance = 0.35f,
                createdTick = 1000,
                lastRecalledTick = 1000,
                loreSeedDefName = "LoreSeed_RoundTrip",
                narrativeAgeOffsetTicks = 7200000
            };

            MemoryFragment loaded = ScribeRoundTrip(fragment);
            Require(loaded.loreSeedDefName == "LoreSeed_RoundTrip",
                "loreSeedDefName must round-trip, got '" + loaded.loreSeedDefName + "'.");
            Require(loaded.narrativeAgeOffsetTicks == 7200000,
                "narrativeAgeOffsetTicks must round-trip, got " + loaded.narrativeAgeOffsetTicks + ".");

            MemoryFragment dirty = new MemoryFragment
            {
                memoryId = "lore-rt-2",
                pawnId = "Thing_Human99999",
                sourceEventId = "evt-lived",
                text = "A lived memory.",
                importance = 0.5f,
                createdTick = 1000,
                lastRecalledTick = 1000,
                loreSeedDefName = "   ",
                narrativeAgeOffsetTicks = -500000
            };

            MemoryFragment repaired = ScribeRoundTrip(dirty);
            Require(repaired.loreSeedDefName == string.Empty,
                "Whitespace lore provenance must repair to empty (a lived memory).");
            Require(repaired.narrativeAgeOffsetTicks == 0,
                "A negative narrative offset must clamp to zero on load.");
        }

        // ── Test 5: Def validation + candidate normalization ────────────────────────────────────────

        /// <summary>
        /// The §5 authoring contract fails loudly: empty/oversized prose, unknown closed tokens,
        /// non-positive weight, unknown tags, keyword overflow, progression usage without event
        /// tokens, and a core tier without exact high-confidence evidence all yield config errors,
        /// while a well-formed seed yields none.
        /// </summary>
        [Test]
        public static void LoreSeedDefValidationCatchesAuthoringErrors()
        {
            // Baseline-relative: base Def.ConfigErrors may emit unrelated noise for a
            // hand-constructed Def, so every bad case must add errors ON TOP of the valid seed.
            int baseline = ErrorCount(ValidSeed());

            DiaryLoreSeedDef blank = ValidSeed();
            blank.text = "  ";
            Require(ErrorCount(blank) > baseline, "Empty text must be a config error.");

            DiaryLoreSeedDef oversized = ValidSeed();
            oversized.text = new string('x', DiaryLoreSeedDef.MaxTextChars + 1);
            Require(ErrorCount(oversized) > baseline, "Oversized text must be a config error.");

            DiaryLoreSeedDef badUsage = ValidSeed();
            badUsage.usage = "Initial";
            Require(ErrorCount(badUsage) > baseline, "Usage tokens are closed and case-sensitive.");

            DiaryLoreSeedDef badTier = ValidSeed();
            badTier.retentionTier = "legendary";
            Require(ErrorCount(badTier) > baseline, "Retention tier tokens are closed.");

            DiaryLoreSeedDef badWeight = ValidSeed();
            badWeight.weight = 0f;
            Require(ErrorCount(badWeight) > baseline, "Weight must be positive and finite.");

            DiaryLoreSeedDef badTag = ValidSeed();
            badTag.tags = new List<string> { "dread", "spooky" };
            Require(ErrorCount(badTag) > baseline, "Unknown memory tags must be reported.");

            DiaryLoreSeedDef tooManyKeywords = ValidSeed();
            tooManyKeywords.keywords = new List<string>
            {
                "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9"
            };
            Require(ErrorCount(tooManyKeywords) > baseline, "More than eight keywords must be reported.");

            DiaryLoreSeedDef progressionless = ValidSeed();
            progressionless.usage = LoreSeedTokens.UsageProgression;
            Require(ErrorCount(progressionless) > baseline,
                "Progression usage without progressionEventDefNames must be a config error.");

            DiaryLoreSeedDef vagueCore = ValidSeed();
            vagueCore.retentionTier = LoreSeedTokens.TierCore;
            vagueCore.backstoryCategories = new List<string> { "Tribal" };
            Require(ErrorCount(vagueCore) > baseline,
                "A core seed with only broad category matchers must be a config error (§16 G5).");

            DiaryLoreSeedDef exactCore = ValidSeed();
            exactCore.retentionTier = LoreSeedTokens.TierCore;
            exactCore.xenotypeDefNames = new List<string> { "Hussar" };
            Require(ErrorCount(exactCore) == baseline,
                "A core seed with exact xenotype evidence must add no config errors.");
        }

        /// <summary>
        /// ToCandidate copies a Def into the plain planner contract: unknown tags are dropped,
        /// authored keywords normalize through the exact deposit/query identity tokenizer, and
        /// matcher lists are trimmed copies.
        /// </summary>
        [Test]
        public static void ToCandidateNormalizesTagsAndKeywords()
        {
            DiaryLoreSeedDef def = ValidSeed();
            def.tags = new List<string> { "dread", "spooky", " combat " };
            def.keywords = new List<string> { "MechanoidCluster", "Psychic-Drone", "MechanoidCluster" };
            def.backstoryCategories = new List<string> { " Tribal ", "  " };

            LoreSeedCandidate candidate = def.ToCandidate(null);
            Require(candidate.tags.Count == 2
                && candidate.tags.Contains("dread") && candidate.tags.Contains("combat"),
                "Unknown tags must be dropped and known ones trimmed, got "
                + string.Join(",", candidate.tags) + ".");
            Require(candidate.keywords.Count == 3
                && candidate.keywords[0] == "mechanoidcluster"
                && candidate.keywords[1] == "psychic"
                && candidate.keywords[2] == "drone",
                "Keywords must normalize through the identity tokenizer, got "
                + string.Join(",", candidate.keywords) + ".");
            Require(candidate.backstoryCategories.Count == 1
                && candidate.backstoryCategories[0] == "Tribal",
                "Matcher lists must be trimmed, blank-free copies.");
        }

        private static DiaryLoreSeedDef ValidSeed()
        {
            return new DiaryLoreSeedDef
            {
                defName = "PDTest_LoreSeed_Validation",
                label = "validation seed",
                text = "The elders said the old machines sleep under the mountains.",
                usage = LoreSeedTokens.UsageInitial,
                retentionTier = LoreSeedTokens.TierOrdinary,
                weight = 1f,
                tags = new List<string> { "dread" },
                keywords = new List<string> { "Mechanoid" }
            };
        }

        private static int ErrorCount(DiaryLoreSeedDef def)
        {
            int count = 0;
            foreach (string error in def.ConfigErrors())
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// PawnLoreSeedState survives a real Scribe save/load and PostLoadInit trims blanks and
        /// duplicates from every list.
        /// </summary>
        [Test]
        public static void LoreSeedStateScribeRoundTrips()
        {
            PawnLoreSeedState state = new PawnLoreSeedState
            {
                pawnId = "  Thing_Human12345  ",
                initialTargetDefNames = new List<string> { "LoreSeed_A", "  ", "LoreSeed_A", "LoreSeed_B" },
                progressionDefNamesEverDeposited = new List<string> { "LoreSeed_P" },
                coreDefNamesEverDeposited = new List<string> { "LoreSeed_C", "LoreSeed_C" }
            };

            PawnLoreSeedState loaded = ScribeRoundTrip(state);
            Require(loaded.pawnId == "Thing_Human12345",
                "pawnId must round-trip trimmed, got '" + loaded.pawnId + "'.");
            Require(loaded.initialTargetDefNames.Count == 2
                && loaded.initialTargetDefNames[0] == "LoreSeed_A"
                && loaded.initialTargetDefNames[1] == "LoreSeed_B",
                "Initial targets must round-trip deduped and blank-free.");
            Require(loaded.progressionDefNamesEverDeposited.Count == 1
                && loaded.progressionDefNamesEverDeposited[0] == "LoreSeed_P",
                "Progression history must round-trip.");
            Require(loaded.coreDefNamesEverDeposited.Count == 1
                && loaded.coreDefNamesEverDeposited[0] == "LoreSeed_C",
                "Core history must round-trip deduped.");
            Require(loaded.HasSeedDefName("loreseed_b"),
                "HasSeedDefName must match case-insensitively across all histories.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

        private static void RegisterTestSeedDef()
        {
            if (DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(TestSeedDefName) != null)
            {
                testSeedDef = DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(TestSeedDefName);
                return;
            }

            testSeedDef = new DiaryLoreSeedDef
            {
                defName = TestSeedDefName,
                label = "test lore seed",
                text = "The elders said the old machines sleep under the mountains.",
                usage = LoreSeedTokens.UsageInitial,
                retentionTier = LoreSeedTokens.TierOrdinary,
                weight = 1f,
                tags = new List<string> { "dread" },
                keywords = new List<string> { "Mechanoid" }
            };
            DefDatabase<DiaryLoreSeedDef>.Add(testSeedDef);
        }

        private static void RemoveTestSeedDef()
        {
            RemoveDefIfPresent(TestSeedDefName);
        }

        private static void RemoveDefIfPresent(string defName)
        {
            DiaryLoreSeedDef def = DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                RemoveDef(def);
            }
        }

        private static void RemoveDef(DiaryLoreSeedDef def)
        {
            MethodInfo remove = typeof(DefDatabase<DiaryLoreSeedDef>).GetMethod(
                "Remove", BindingFlags.Static | BindingFlags.NonPublic);
            if (remove == null)
            {
                throw new AssertionException(
                    "DefDatabase<DiaryLoreSeedDef>.Remove reflection handle is null — RimWorld API changed.");
            }

            remove.Invoke(null, new object[] { def });
        }

        /// <summary>Temporarily removes every shipped lore seed Def so fixtures stay exact.</summary>
        private static void HideShippedCatalog()
        {
            hiddenCatalog = new List<DiaryLoreSeedDef>(
                DefDatabase<DiaryLoreSeedDef>.AllDefsListForReading);
            for (int i = 0; i < hiddenCatalog.Count; i++)
            {
                RemoveDef(hiddenCatalog[i]);
            }
        }

        private static void RestoreShippedCatalog()
        {
            if (hiddenCatalog == null)
            {
                return;
            }

            for (int i = 0; i < hiddenCatalog.Count; i++)
            {
                if (DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(hiddenCatalog[i].defName) == null)
                {
                    DefDatabase<DiaryLoreSeedDef>.Add(hiddenCatalog[i]);
                }
            }
        }

        private static void RegisterProgressionSeedDef()
        {
            AddProgressionTestDef(TestProgressionSeedDefName,
                LoreSeedTokens.TierOrdinary, hediffDefNames: null);
        }

        private static void AddProgressionTestDef(string defName, string tier,
            List<string> hediffDefNames)
        {
            if (DefDatabase<DiaryLoreSeedDef>.GetNamedSilentFail(defName) != null)
            {
                return;
            }

            registeredTestDefNames?.Add(defName);
            DefDatabase<DiaryLoreSeedDef>.Add(new DiaryLoreSeedDef
            {
                defName = defName,
                label = "test progression seed " + defName,
                text = "The day my blood changed, it kept a before and an after. (" + defName + ")",
                usage = LoreSeedTokens.UsageProgression,
                retentionTier = tier,
                weight = 1f,
                tags = new List<string> { "body" },
                hediffDefNames = hediffDefNames,
                progressionEventDefNames = new List<string> { "XenotypeChanged" }
            });
        }

        private static PawnMemoryRepository GetMemories()
        {
            PawnMemoryRepository memories =
                MemoriesField?.GetValue(scope.Component) as PawnMemoryRepository;
            if (memories == null)
            {
                throw new AssertionException(
                    "Could not read DiaryGameComponent.memories — field missing or wrong type.");
            }

            return memories;
        }

        private static PawnLoreSeedState LoreStateFor(string pawnId)
        {
            Dictionary<string, PawnLoreSeedState> index =
                LoreStateIndexField.GetValue(scope.Component) as Dictionary<string, PawnLoreSeedState>;
            PawnLoreSeedState state = null;
            index?.TryGetValue(pawnId, out state);
            return state;
        }

        /// <summary>
        /// Creates one raw pair page with an arbitrary progression event defName. The gameContext
        /// carries the production "progression=" marker so the event classifies into the real
        /// Progression domain group (important, non-quiet cue) and the ordinary lived deposit
        /// clears the noise gate — a bare defName with empty context lands in the default quiet
        /// interaction group and deposits nothing, which would mask the progression attachment.
        /// </summary>
        private static DiaryEvent AddRawPairEvent(Pawn initiator, Pawn recipient, string defName, string label)
        {
            string gameContext = "progression=" + defName
                + "; progression_kind=test; previous_value=Baseliner; new_value=Sanguophage";
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                defName,
                label,
                initiator.LabelShortCap + " went through " + label + ".",
                recipient.LabelShortCap + " watched " + label + ".",
                string.Empty,
                gameContext);
            if (diaryEvent == null)
            {
                throw new AssertionException(
                    "EventFactory did not create the raw " + defName + " fixture page.");
            }

            return diaryEvent;
        }

        private static MemoryFragment FindProgressionFragment(string pawnId)
        {
            IReadOnlyList<MemoryFragment> owned = GetMemories().ForPawn(pawnId);
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i].sourceEventId != null
                    && owned[i].sourceEventId.StartsWith("loreseed-progression:", StringComparison.Ordinal))
                {
                    return owned[i];
                }
            }

            return null;
        }

        private static int CountProgressionFragments(string pawnId)
        {
            int count = 0;
            IReadOnlyList<MemoryFragment> owned = GetMemories().ForPawn(pawnId);
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i].sourceEventId != null
                    && owned[i].sourceEventId.StartsWith("loreseed-progression:", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static MemoryFragment FindLoreFragment(string pawnId)
        {
            IReadOnlyList<MemoryFragment> owned = GetMemories().ForPawn(pawnId);
            for (int i = 0; i < owned.Count; i++)
            {
                if (!string.IsNullOrEmpty(owned[i].loreSeedDefName))
                {
                    return owned[i];
                }
            }

            return null;
        }

        private static int CountLoreFragments(string pawnId)
        {
            int count = 0;
            IReadOnlyList<MemoryFragment> owned = GetMemories().ForPawn(pawnId);
            for (int i = 0; i < owned.Count; i++)
            {
                if (!string.IsNullOrEmpty(owned[i].loreSeedDefName))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Creates one pair page through the production EventFactory funnel (the L2 seam).</summary>
        private static DiaryEvent AddPairwiseLoreEvent(Pawn initiator, Pawn recipient, string interactionDefName)
        {
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
            if (interaction == null)
            {
                throw new AssertionException(
                    "InteractionDef '" + interactionDefName + "' not found in the loaded game.");
            }

            string label = interaction.LabelCap.Resolve();
            string initiatorText = "PawnDiary.Event.Interaction".Translate(
                initiator.LabelShortCap, label, recipient.LabelShortCap).Resolve();
            string recipientText = "PawnDiary.Event.Interaction".Translate(
                recipient.LabelShortCap, label, initiator.LabelShortCap).Resolve();

            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                initiator,
                recipient,
                interaction.defName,
                label,
                initiatorText,
                recipientText,
                InteractionGroups.InstructionFor(interaction),
                DiaryContextBuilder.BuildGameContextSummary(interaction, label));
            if (diaryEvent == null)
            {
                throw new AssertionException(
                    "EventFactory did not create the " + interactionDefName + " lore fixture page.");
            }

            return diaryEvent;
        }

        private static void CleanupLoreForTestPawns()
        {
            if (scope == null)
            {
                return;
            }

            PawnMemoryRepository memories =
                MemoriesField?.GetValue(scope.Component) as PawnMemoryRepository;
            List<PawnLoreSeedState> states =
                LoreStatesField?.GetValue(scope.Component) as List<PawnLoreSeedState>;
            Dictionary<string, PawnLoreSeedState> index =
                LoreStateIndexField?.GetValue(scope.Component) as Dictionary<string, PawnLoreSeedState>;

            RemovePawnState(memories, states, index, pawnA);
            RemovePawnState(memories, states, index, pawnB);
        }

        private static void RemovePawnState(
            PawnMemoryRepository memories,
            List<PawnLoreSeedState> states,
            Dictionary<string, PawnLoreSeedState> index,
            Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            memories?.RemoveOwner(pawnId);
            if (index != null && index.TryGetValue(pawnId, out PawnLoreSeedState state))
            {
                index.Remove(pawnId);
                states?.Remove(state);
            }
        }

        private static T ScribeRoundTrip<T>(T toSave) where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_rimtest_lore_" + Guid.NewGuid().ToString("N") + ".xml");

            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = toSave;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }

                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // A locked temp file must not fail the assertion path.
                }
            }

            if (loaded == null)
            {
                throw new AssertionException("Scribe round-trip returned a null " + typeof(T).Name + ".");
            }

            return loaded;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}
