// Loaded-game fixture for anniversaries and personal records (Quality Wave §8, H2).
//
// The flow suite proves the scanner writes the right pages. This fixture pins everything AROUND it:
//   (a) the saved H2 rows survive a Scribe round-trip and normalize an old save to silence;
//   (b) the RimWorld members the discovery pass reaches by name still exist with the expected shapes;
//   (c) live relation resolution really ranks a real colonist pair through the shipped bond priority;
//   (d) the memory cap evicts the weakest bond and the discovery cursor keeps it forgotten;
//   (e) the four XML groups, prompt Defs, and every localized string load and resolve.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" for the save round-trip idiom below).
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
    /// Pins the live-game half of H2: save schema, the patched-free RimWorld surface it reads, live
    /// relation ranking, retention/eviction, and the Def/localization wiring.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryAnniversaryFixtureTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "progressionBirthday",
                "progressionArrivalAnniversary",
                "progressionDeathAnniversary",
                "progressionRecordMilestone");
            pawn = scope.CreateAdultColonist();
        }

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
        /// The new saved rows must survive save/load, and an old save (every field absent) must load as
        /// "nothing observed yet" rather than as zeroes that read like real observations.
        /// </summary>
        [Test]
        public static void SavedStateRoundTripsAndOldSavesNormalizeToSilence()
        {
            PawnProgressionState fresh = new PawnProgressionState();
            fresh.Normalize();
            PawnDiaryRimTestScope.Require(fresh.lastObservedBiologicalAgeYears == -1,
                "An old save must load as 'age never observed' (-1), not as age zero.");
            PawnDiaryRimTestScope.Require(fresh.lastBondedDeathDiscoveryTick == -1,
                "An old save must load with the death-discovery cursor unset.");
            PawnDiaryRimTestScope.Require(fresh.lastArrivalAnniversaryYear == 0,
                "An old save must load with no evaluated arrival years.");
            PawnDiaryRimTestScope.Require(fresh.baselineAnniversariesOnNextScan,
                "An old save must arm the H2 baseline flag, or it receives retroactive pages.");
            PawnDiaryRimTestScope.Require(
                fresh.bondedDeathMemories != null && fresh.bondedDeathMemories.Count == 0
                    && fresh.recordHighWater != null && fresh.recordHighWater.Count == 0,
                "An old save must load empty H2 collections rather than nulls.");

            // Junk rows a corrupted or hand-edited save could carry must be dropped, not carried.
            PawnProgressionState dirty = new PawnProgressionState
            {
                lastObservedBiologicalAgeYears = -9,
                lastArrivalAnniversaryYear = -4,
                lastBondedDeathDiscoveryTick = -20,
                bondedDeathMemories = new List<BondedDeathMemoryState>
                {
                    new BondedDeathMemoryState
                    {
                        victimId = " Thing_Victim ",
                        victimName = " Ada ",
                        relationDefName = " Spouse ",
                        relationLabel = " wife ",
                        deathTick = -5,
                        lastProcessedAnniversaryYear = -3
                    },
                    // Duplicate victim, no relation, and null: all unusable.
                    new BondedDeathMemoryState { victimId = "Thing_Victim", relationDefName = "Lover" },
                    new BondedDeathMemoryState { victimId = "Thing_Other", relationDefName = " " },
                    null
                },
                recordHighWater = new List<RecordHighWaterState>
                {
                    new RecordHighWaterState { recordDefName = " Kills ", highestValue = -12f },
                    new RecordHighWaterState { recordDefName = "Kills", highestValue = 99f },
                    new RecordHighWaterState { recordDefName = " ", highestValue = 5f }
                }
            };
            dirty.Normalize();
            PawnDiaryRimTestScope.Require(dirty.lastObservedBiologicalAgeYears == -1,
                "A negative saved age must normalize to 'never observed'.");
            PawnDiaryRimTestScope.Require(dirty.lastArrivalAnniversaryYear == 0,
                "A negative saved arrival year must normalize to zero.");
            PawnDiaryRimTestScope.Require(dirty.lastBondedDeathDiscoveryTick == -1,
                "A negative saved discovery cursor must normalize to 'never scanned'.");
            PawnDiaryRimTestScope.Require(dirty.bondedDeathMemories.Count == 1,
                "Normalization kept " + dirty.bondedDeathMemories.Count
                    + " bonded-death rows; only the one usable, de-duplicated row should survive.");
            BondedDeathMemoryState kept = dirty.bondedDeathMemories[0];
            PawnDiaryRimTestScope.Require(
                kept.victimId == "Thing_Victim" && kept.victimName == "Ada"
                    && kept.relationDefName == "Spouse" && kept.relationLabel == "wife"
                    && kept.deathTick == 0 && kept.lastProcessedAnniversaryYear == 0,
                "The surviving bonded-death row was not trimmed and clamped.");
            PawnDiaryRimTestScope.Require(dirty.recordHighWater.Count == 1,
                "Only one de-duplicated record high-water row should survive normalization.");
            PawnDiaryRimTestScope.Require(dirty.HighestRecordValue("Kills") == 0f,
                "A negative saved record value must normalize to zero.");
            PawnDiaryRimTestScope.Require(dirty.HighestRecordValue("kills") == 0f,
                "Record lookup must ignore case, matching the rest of the def-name comparisons.");

            // The monotonic setter is the whole no-double-award guarantee.
            dirty.SetRecordHighWater("Kills", 50f);
            PawnDiaryRimTestScope.Require(dirty.HighestRecordValue("Kills") == 50f,
                "The record high-water mark did not rise.");
            dirty.SetRecordHighWater("Kills", 10f);
            PawnDiaryRimTestScope.Require(dirty.HighestRecordValue("Kills") == 50f,
                "The record high-water mark must never fall.");
            dirty.SetRecordHighWater("Kills", float.NaN);
            PawnDiaryRimTestScope.Require(dirty.HighestRecordValue("Kills") == 50f,
                "A NaN record value must not corrupt the high-water mark.");

            // The live pawn's own row is the one that actually round-trips through Scribe.
            PawnProgressionState live = scope.Component.AnniversaryStateFor(pawn);
            PawnDiaryRimTestScope.Require(live != null,
                "The fixture pawn has no saved progression state to round-trip.");
            live.lastObservedBiologicalAgeYears = 31;
            live.lastArrivalAnniversaryYear = 5;
            live.lastBondedDeathDiscoveryTick = 12345;
            live.lastBondedDeathPageDay = 678;
            live.SetRecordHighWater("Kills", 42f);
            live.Normalize();
            PawnDiaryRimTestScope.Require(
                live.lastObservedBiologicalAgeYears == 31 && live.lastArrivalAnniversaryYear == 5
                    && live.lastBondedDeathDiscoveryTick == 12345 && live.lastBondedDeathPageDay == 678
                    && live.HighestRecordValue("Kills") == 42f,
                "Normalization damaged legitimate saved H2 values.");
        }

        /// <summary>
        /// Every RimWorld member the discovery and record passes reach by name must still exist. None of
        /// these are Harmony-patched, so a rename would show up as a silently dead feature rather than a
        /// startup error — which is exactly why they are pinned here.
        /// </summary>
        [Test]
        public static void ReadRimWorldSurfaceStillExists()
        {
            PawnDiaryRimTestScope.Require(
                typeof(Pawn_RecordsTracker).GetMethod(
                    nameof(Pawn_RecordsTracker.GetValue),
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(RecordDef) }, null) != null,
                "Pawn_RecordsTracker.GetValue(RecordDef) is gone; record milestones cannot be read.");
            PawnDiaryRimTestScope.Require(
                typeof(Pawn_RelationsTracker).GetProperty(
                    nameof(Pawn_RelationsTracker.RelatedPawns),
                    BindingFlags.Instance | BindingFlags.Public) != null,
                "Pawn_RelationsTracker.RelatedPawns is gone; bonded deaths cannot be discovered.");
            PawnDiaryRimTestScope.Require(
                typeof(PawnRelationUtility).GetMethod(
                    "GetRelations",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(Pawn), typeof(Pawn) }, null) != null,
                "Pawn.GetRelations(Pawn) is gone; bond strength cannot be resolved.");
            PawnDiaryRimTestScope.Require(
                typeof(PawnRelationDef).GetMethod(
                    nameof(PawnRelationDef.GetGenderSpecificLabelCap),
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(Pawn) }, null) != null,
                "PawnRelationDef.GetGenderSpecificLabelCap(Pawn) is gone; a remembered loss would lose "
                + "its localized relation label.");
            PawnDiaryRimTestScope.Require(
                typeof(Pawn_AgeTracker).GetProperty(
                    nameof(Pawn_AgeTracker.AgeBiologicalYears),
                    BindingFlags.Instance | BindingFlags.Public) != null,
                "Pawn_AgeTracker.AgeBiologicalYears is gone; birthdays cannot be observed.");

            // Every shipped record rule must resolve against BASE RimWorld, so a no-DLC install sees
            // all three. GetNamedSilentFail (never GetNamed) keeps a modded/removed record harmless.
            Dictionary<string, RecordDef> resolved =
                DiaryGameComponent.SnapshotAnniversaryRecordDefs();
            List<RecordMilestoneRule> rules = DiaryTuning.Current.recordMilestones;
            PawnDiaryRimTestScope.Require(rules != null && rules.Count > 0,
                "The shipped record-milestone rules did not load.");
            for (int i = 0; i < rules.Count; i++)
            {
                PawnDiaryRimTestScope.Require(
                    resolved.ContainsKey(rules[i].recordDefName),
                    "The shipped record rule '" + rules[i].recordDefName
                        + "' does not resolve to a loaded RecordDef, so that milestone is dead.");
            }

            PawnDiaryRimTestScope.Require(
                DiaryGameComponent.SnapshotAnniversaryRecordDefs() != null,
                "The record-def snapshot must never return null.");
        }

        /// <summary>
        /// Live relation ranking: a real colonist pair resolves through the shipped strongest-first bond
        /// list, and a relation the list does not name is never remembered.
        /// </summary>
        [Test]
        public static void LiveRelationsRankThroughTheShippedBondPriority()
        {
            Pawn other = scope.CreateAdultColonist();
            List<string> priority = DiaryTuning.Current.bondedDeathRelationPriority;
            PawnDiaryRimTestScope.Require(priority != null && priority.Count > 0,
                "The shipped bond-priority list did not load.");

            // Unrelated pawns rank nowhere.
            PawnDiaryRimTestScope.Require(
                AnniversaryPolicy.BondPriority(
                    StrongestRelationDefName(pawn, other), priority) < 0,
                "Two unrelated colonists were treated as a remembered bond.");

            // Spouse is a direct relation and the closest bond the shipped list names.
            pawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse, other);
            string resolved = StrongestRelationDefName(pawn, other);
            PawnDiaryRimTestScope.Require(
                string.Equals(resolved, PawnRelationDefOf.Spouse.defName, StringComparison.Ordinal),
                "A live spouse relation resolved as '" + resolved + "' instead of Spouse.");
            PawnDiaryRimTestScope.Require(
                AnniversaryPolicy.BondPriority(resolved, priority) == 0,
                "Spouse is not the first (closest) row of the shipped bond-priority list.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(
                    PawnRelationDefOf.Spouse.GetGenderSpecificLabelCap(other)),
                "The spouse relation produced no localized label for a real pawn.");

            // ExSpouse is a real direct relation that the shipped list deliberately does NOT name.
            Pawn stranger = scope.CreateAdultColonist();
            pawn.relations.AddDirectRelation(PawnRelationDefOf.ExSpouse, stranger);
            PawnDiaryRimTestScope.Require(
                AnniversaryPolicy.BondPriority(
                    StrongestRelationDefName(pawn, stranger), priority) < 0,
                "An ex-spouse was admitted as a remembered bond; the shipped list excludes it.");

            // RelatedPawns must actually surface the related pawn, or discovery finds nothing.
            bool found = false;
            foreach (Pawn related in pawn.relations.RelatedPawns)
            {
                if (related == other)
                {
                    found = true;
                    break;
                }
            }
            PawnDiaryRimTestScope.Require(found,
                "Pawn_RelationsTracker.RelatedPawns did not surface a directly related pawn, so "
                + "bonded-death discovery would never see it.");
        }

        /// <summary>
        /// The memory cap keeps the closest bonds, and the monotonic discovery cursor is what makes the
        /// eviction permanent instead of re-admitting the same victim on the next scan.
        /// </summary>
        [Test]
        public static void MemoryCapEvictsWeakestBondsAndTheCursorAdvances()
        {
            PawnProgressionState state = scope.Component.AnniversaryStateFor(pawn);
            PawnDiaryRimTestScope.Require(state != null, "The fixture pawn has no progression state.");
            List<string> priority = DiaryTuning.Current.bondedDeathRelationPriority;
            int cap = DiaryTuning.Current.bondedDeathMemoryCap;
            PawnDiaryRimTestScope.Require(cap > 0, "The shipped bonded-death memory cap must be positive.");

            // One more candidate than the cap allows, weakest bond last.
            List<BondedDeathCandidate> rows = new List<BondedDeathCandidate>();
            for (int i = 0; i <= cap; i++)
            {
                string relationDefName = i == cap
                    ? priority[priority.Count - 1]
                    : priority[0];
                rows.Add(new BondedDeathCandidate
                {
                    victimId = "PawnDiaryRimTest_Cap_" + i.ToString("00"),
                    victimName = "Victim " + i,
                    relationDefName = relationDefName,
                    relationLabel = "relation",
                    bondPriority = AnniversaryPolicy.BondPriority(relationDefName, priority),
                    deathTick = 1000 + i,
                    lastProcessedAnniversaryYear = 0
                });
            }

            List<BondedDeathCandidate> retained = AnniversaryPolicy.RetainStrongestBonds(rows, cap);
            PawnDiaryRimTestScope.Require(retained.Count == cap,
                "Retention kept " + retained.Count + " rows against a cap of " + cap + ".");
            for (int i = 0; i < retained.Count; i++)
            {
                PawnDiaryRimTestScope.Require(
                    !string.Equals(
                        retained[i].victimId,
                        "PawnDiaryRimTest_Cap_" + cap.ToString("00"),
                        StringComparison.Ordinal),
                    "The weakest bond survived the memory cap instead of the closest ones.");
            }

            // The cursor must move forward on every scan, so an evicted victim whose death is older
            // than the cursor can never be rediscovered.
            int before = Find.TickManager.TicksGame;
            state.lastBondedDeathDiscoveryTick = -1;
            scope.Component.ScanAnniversariesForPawn(
                pawn, before, DiaryGameComponent.SnapshotAnniversaryRecordDefs());
            PawnDiaryRimTestScope.Require(state.lastBondedDeathDiscoveryTick >= before,
                "The bonded-death discovery cursor did not advance, so evicted memories would come back.");

            int advanced = state.lastBondedDeathDiscoveryTick;
            scope.Component.ScanAnniversariesForPawn(
                pawn, before - 5000, DiaryGameComponent.SnapshotAnniversaryRecordDefs());
            PawnDiaryRimTestScope.Require(state.lastBondedDeathDiscoveryTick >= advanced,
                "An earlier scan tick moved the discovery cursor backwards.");
        }

        /// <summary>
        /// The four XML groups, their prompt Defs, and every localized string must load. A missing Keyed
        /// row would render its raw key straight into a diary page.
        /// </summary>
        [Test]
        public static void GroupsPromptsAndLocalizationAreWired()
        {
            string[] groupDefNames =
            {
                "progressionBirthday",
                "progressionArrivalAnniversary",
                "progressionDeathAnniversary",
                "progressionRecordMilestone"
            };
            string[] sourceDefNames =
            {
                ProgressionEventData.PawnBirthdayDefName,
                ProgressionEventData.ArrivalAnniversaryDefName,
                ProgressionEventData.BondedDeathAnniversaryDefName,
                ProgressionEventData.RecordMilestoneDefName
            };

            for (int i = 0; i < groupDefNames.Length; i++)
            {
                DiaryInteractionGroupDef group =
                    InteractionGroups.ClassifyProgression(sourceDefNames[i]);
                PawnDiaryRimTestScope.Require(
                    group != null
                        && string.Equals(group.defName, groupDefNames[i], StringComparison.Ordinal),
                    sourceDefNames[i] + " does not classify to '" + groupDefNames[i]
                        + "' (it fell through to " + (group == null ? "nothing" : group.defName)
                        + "), so its settings row would control nothing.");
                PawnDiaryRimTestScope.Require(group.defaultEnabled,
                    groupDefNames[i] + " must ship enabled.");
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(InteractionGroups.InstructionForProgression(group)),
                    groupDefNames[i] + " has no loaded instruction.");
                PawnDiaryRimTestScope.Require(
                    DefDatabase<DiaryEventPromptDef>.GetNamedSilentFail(
                        "DiaryEventPrompt_" + sourceDefNames[i]) != null,
                    "The event prompt Def for " + sourceDefNames[i] + " was not loaded.");
            }

            // Only the remembered loss is important; a birthday or a tally must never outrank a real
            // event when a reflection picks its highlights.
            PawnDiaryRimTestScope.Require(
                InteractionGroups.ClassifyProgression(
                    ProgressionEventData.BondedDeathAnniversaryDefName).important,
                "A remembered loss must stay important.");
            PawnDiaryRimTestScope.Require(
                !InteractionGroups.ClassifyProgression(
                    ProgressionEventData.PawnBirthdayDefName).important,
                "A birthday must stay non-important.");
            PawnDiaryRimTestScope.Require(
                !InteractionGroups.ClassifyProgression(
                    ProgressionEventData.ArrivalAnniversaryDefName).important,
                "A colony anniversary must stay non-important.");
            PawnDiaryRimTestScope.Require(
                !InteractionGroups.ClassifyProgression(
                    ProgressionEventData.RecordMilestoneDefName).important,
                "A personal record must stay non-important.");

            RequireLocalized("PawnDiary.Event.AnniversaryBirthdayLabel",
                "PawnDiary.Event.AnniversaryBirthdayLabel".Translate(35).Resolve(), "35");
            RequireLocalized("PawnDiary.Event.AnniversaryBirthdayText",
                "PawnDiary.Event.AnniversaryBirthdayText".Translate("Nell", 35).Resolve(), "Nell", "35");
            RequireLocalized("PawnDiary.Event.AnniversaryArrivalLabel",
                "PawnDiary.Event.AnniversaryArrivalLabel".Translate(5).Resolve(), "5");
            RequireLocalized("PawnDiary.Event.AnniversaryArrivalText",
                "PawnDiary.Event.AnniversaryArrivalText".Translate("Nell", 5).Resolve(), "Nell", "5");
            RequireLocalized("PawnDiary.Event.AnniversaryDeathLabel",
                "PawnDiary.Event.AnniversaryDeathLabel".Translate("Ada").Resolve(), "Ada");
            RequireLocalized("PawnDiary.Event.AnniversaryDeathText",
                "PawnDiary.Event.AnniversaryDeathText".Translate("Nell", 3, "Ada", "wife").Resolve(),
                "Nell", "3", "Ada", "wife");
            RequireLocalized("PawnDiary.Event.AnniversaryDeathCombinedEntry",
                "PawnDiary.Event.AnniversaryDeathCombinedEntry".Translate("Ada", "wife", 3).Resolve(),
                "Ada", "wife", "3");
            RequireLocalized("PawnDiary.Event.AnniversaryDeathCombinedText",
                "PawnDiary.Event.AnniversaryDeathCombinedText".Translate("Nell", "Ada (wife)").Resolve(),
                "Nell", "Ada");
            RequireLocalized("PawnDiary.Event.AnniversaryRecordLabel",
                "PawnDiary.Event.AnniversaryRecordLabel".Translate("Kills", 50).Resolve(), "Kills", "50");
            RequireLocalized("PawnDiary.Event.AnniversaryRecordText",
                "PawnDiary.Event.AnniversaryRecordText".Translate("Nell", 50, "Kills").Resolve(),
                "Nell", "50", "Kills");
        }

        /// <summary>The shipped tuning must still be usable and match the plan's locked values.</summary>
        [Test]
        public static void ShippedTuningIsUsable()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            PawnDiaryRimTestScope.Require(tuning != null, "The Pawn Diary tuning Def was not loaded.");
            PawnDiaryRimTestScope.Require(tuning.anniversaryScanIntervalTicks >= 250,
                "The anniversary scan interval must stay above the shared 250-tick floor.");
            PawnDiaryRimTestScope.Require(tuning.bondedDeathMemoryCap == 16,
                "The shipped bonded-death memory cap drifted from the locked 16.");
            PawnDiaryRimTestScope.Require(tuning.bondedDeathGuaranteedYears == 3,
                "The shipped guaranteed grief window drifted from the locked 3 years.");
            PawnDiaryRimTestScope.Require(tuning.bondedDeathMaxCombinedNames == 3,
                "The shipped combined-name cap drifted from the locked 3 names.");
            PawnDiaryRimTestScope.Require(
                Math.Abs(tuning.bondedDeathFirstDecayChance - 0.60f) < 0.0001f
                    && Math.Abs(tuning.bondedDeathDecayMultiplier - 0.65f) < 0.0001f
                    && Math.Abs(tuning.bondedDeathFloorChance - 0.05f) < 0.0001f,
                "The shipped grief decay schedule drifted from the locked 0.60 / ×0.65 / floor 0.05.");
            PawnDiaryRimTestScope.Require(
                tuning.arrivalAnniversaryMilestoneYears != null
                    && tuning.arrivalAnniversaryMilestoneYears.Count == 5
                    && tuning.arrivalAnniversaryRecurringIntervalYears == 5,
                "The shipped arrival milestone years drifted from the locked [1,2,3,5,10] + 5.");
        }

        // ----- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// Mirrors the scanner's own resolution: the closest relation between two pawns that the shipped
        /// bond-priority list names, as a stable defName.
        /// </summary>
        private static string StrongestRelationDefName(Pawn from, Pawn to)
        {
            List<string> priority = DiaryTuning.Current.bondedDeathRelationPriority;
            string best = string.Empty;
            int bestPriority = int.MaxValue;
            foreach (PawnRelationDef def in from.GetRelations(to))
            {
                int rank = AnniversaryPolicy.BondPriority(def?.defName, priority);
                if (rank >= 0 && rank < bestPriority)
                {
                    bestPriority = rank;
                    best = def.defName;
                }
            }

            return best;
        }

        private static void RequireLocalized(string key, string resolved, params string[] expectedArguments)
        {
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(resolved)
                    && resolved.IndexOf(key, StringComparison.Ordinal) < 0,
                "Keyed string '" + key + "' did not resolve in the loaded language.");
            for (int i = 0; i < expectedArguments.Length; i++)
            {
                PawnDiaryRimTestScope.Require(
                    resolved.IndexOf(expectedArguments[i], StringComparison.Ordinal) >= 0,
                    "Keyed string '" + key + "' dropped argument '" + expectedArguments[i]
                        + "' in the loaded language: " + resolved);
            }
        }
    }
}
