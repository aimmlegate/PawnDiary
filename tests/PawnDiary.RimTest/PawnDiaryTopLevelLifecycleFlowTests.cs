// Loaded-game boundary tests for Pawn Diary sources whose existing RimTest suites stop one layer
// below the real vanilla/Harmony or component-scanner trigger.
//
// Each fixture object is deliberately inert and reversible: abilities have no comps/effects, quests
// are accepted but never ended or registered with the colony, and the work scanner receives one
// unspawned pawn through its existing one-tick cache seam. The suite never starts an incident or
// broadcasts a condition to live-map subscribers. Optional error reporting is disabled/restored around
// every test; only the Arrival test briefly replaces the LLM endpoint list because its neutral role
// bypasses the per-pawn generation gate.
//
// New to C#/RimWorld? See AGENTS.md. Reflection is used only to reach private top-level component
// scanners/transient stores; a renamed production member fails SetUp loudly instead of silently
// reducing coverage.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves representative Ability, Work, Quest, and Arrival events cross their real
    /// loaded-game trigger boundaries without modifying the player's colony.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryTopLevelLifecycleFlowTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic =
            BindingFlags.Static | BindingFlags.NonPublic;

        private const float ForcedWorkPassChance = 1f;
        private const string LocalAbilityDefName =
            "PawnDiary_RimTest_TopLevelLocalAbility";
        private const string GlobalAbilityDefName =
            "PawnDiary_RimTest_TopLevelGlobalAbility";
        private const string WorkTypeDefName =
            "PawnDiary_RimTest_TopLevelChore";
        private static readonly MethodInfo ScanPawnWorkMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ScanPawnWorkForDiaryEvents", PrivateInstance);
        private static readonly FieldInfo CachedFreeColonistsField =
            typeof(DiaryGameComponent).GetField(
                "cachedFreeColonists", PrivateStatic);
        private static readonly FieldInfo CachedFreeColonistsTickField =
            typeof(DiaryGameComponent).GetField(
                "cachedFreeColonistsTick", PrivateStatic);
        private static readonly FieldInfo KnownAcceptedQuestIdsField =
            typeof(DiaryGameComponent).GetField(
                "knownAcceptedQuestIds", PrivateInstance);
        private static PawnDiaryRimTestScope scope;

        /// <summary>
        /// Opens a loaded-game scope, enables the catch-all groups used below, and suppresses optional
        /// error-report transport. Test pawns remain generation-disabled through the shared harness.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            RequireReflectionSurface();
            scope = PawnDiaryRimTestScope.Begin(
                "abilityUsed",
                "workStrain",
                "questAccepted",
                "arrival");
            ForceFrequencyToOne("abilityUsed", "workStrain");
            SuppressErrorReporting();
        }

        /// <summary>
        /// Restores every fixture-owned setting/store and audits that no test page survived.
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
            }
        }

        /// <summary>
        /// Calls the real local-target <see cref="Ability.Activate(LocalTargetInfo, LocalTargetInfo)"/>
        /// overload and proves its Harmony postfix submits exactly one ability page.
        /// </summary>
        [Test]
        public static void RealLocalAbilityActivateCrossesHarmonyBoundary()
        {
            Pawn caster = scope.CreateAdultColonist();
            Pawn target = scope.CreateAdultColonist();
            ForceAbilityChanceToPass();
            Ability ability = new Ability(
                caster, BuildInertAbilityDef(LocalAbilityDefName));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => ability.Activate(
                    new LocalTargetInfo(target), LocalTargetInfo.Invalid),
                LocalAbilityDefName,
                caster,
                null,
                true);

            scope.RequireSoloRef(diaryEvent, caster);
            RequireContextContains(
                diaryEvent, "ability=" + LocalAbilityDefName);
            RequireContextContains(diaryEvent, "ability_target=");
        }

        /// <summary>
        /// Calls the real world-target Ability.Activate overload and proves the separate global Harmony
        /// postfix reaches the same persisted ability pipeline.
        /// </summary>
        [Test]
        public static void RealGlobalAbilityActivateCrossesHarmonyBoundary()
        {
            Pawn caster = scope.CreateAdultColonist();
            Pawn target = scope.CreateAdultColonist();
            ForceAbilityChanceToPass();
            Ability ability = new Ability(
                caster, BuildInertAbilityDef(GlobalAbilityDefName));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => ability.Activate(new GlobalTargetInfo(target)),
                GlobalAbilityDefName,
                caster,
                null,
                true);

            scope.RequireSoloRef(diaryEvent, caster);
            RequireContextContains(
                diaryEvent, "ability=" + GlobalAbilityDefName);
            RequireContextContains(diaryEvent, "ability_target=");
        }

        /// <summary>
        /// Invokes the component's actual periodic Work scan. Its shared one-tick colonist cache is
        /// replaced with one fixture pawn for the synchronous call, then restored byte-for-byte.
        /// </summary>
        [Test]
        public static void TopLevelWorkScannerDispatchesItsCachedFixtureColonist()
        {
            Pawn worker = scope.CreateAdultColonist();
            WorkTypeDef workType = new WorkTypeDef
            {
                defName = WorkTypeDefName,
                workTags = WorkTags.ManualDumb,
                relevantSkills = null,
                gerundLabel = "testing chores",
                labelShort = "testing chores"
            };
            SetCurrentWork(worker, workType);
            ForceWorkPolicyToPass();
            IsolateFreeColonistCacheTo(worker);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => ScanPawnWorkMethod.Invoke(scope.Component, null),
                WorkEventData.StrainDefName,
                worker,
                null,
                true);

            scope.RequireSoloRef(diaryEvent, worker);
            RequireContextContains(
                diaryEvent, "work=" + WorkTypeDefName);
            RequireContextContains(diaryEvent, "dumb_or_cleaning=true");
        }

        /// <summary>
        /// Calls Quest.Accept on an unregistered, part-free quest and proves the Harmony hook updates the
        /// component's accepted-quest scanner baseline.
        /// </summary>
        [Test]
        public static void RealQuestAcceptUpdatesBookkeepingThroughHarmony()
        {
            PawnDiaryMod.Settings.SetGroupEnabled("questAccepted", false);
            Quest quest = BuildInertQuest(
                "PawnDiary_RimTest_TopLevelQuestAccept",
                "A reversible quest acceptance");
            HashSet<int> knownQuestIds = KnownAcceptedQuestIds();
            scope.RegisterCleanup(() => knownQuestIds.Remove(quest.id));
            PawnDiaryRimTestScope.Require(
                !knownQuestIds.Contains(quest.id),
                "The fresh fixture quest was already in accepted-quest bookkeeping.");
            long pagesBefore = CountOutcome(
                DiaryTelemetry.Snapshot(),
                DiaryTelemetryOutcome.EventRecorded);
            scope.OwnDiaryEventsCreatedAfterThisPoint();

            quest.Accept(null);

            PawnDiaryRimTestScope.Require(
                quest.EverAccepted,
                "Vanilla did not transition the fixture quest through Accept.");
            PawnDiaryRimTestScope.Require(
                knownQuestIds.Contains(quest.id),
                "Quest.Accept did not reach Pawn Diary's accepted-quest Harmony bookkeeping.");
            PawnDiaryRimTestScope.Require(
                CountOutcome(
                    DiaryTelemetry.Snapshot(),
                    DiaryTelemetryOutcome.EventRecorded)
                    == pagesBefore,
                "The bookkeeping-only accepted quest unexpectedly recorded a diary page.");
        }

        /// <summary>
        /// Moves one disposable pawn from factionless to the player faction through Pawn.SetFaction and
        /// proves the arrival Harmony patch creates its neutral first page.
        /// </summary>
        [Test]
        public static void RealSetFactionCreatesArrivalThroughHarmony()
        {
            NeutralizeLlmDispatch();
            Pawn arrivalPawn = scope.CreateAdultColonist();
            arrivalPawn.SetFaction(null);
            PawnDiaryRimTestScope.Require(
                arrivalPawn.Faction == null,
                "Vanilla did not make the disposable arrival pawn factionless.");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => arrivalPawn.SetFaction(scope.PlayerFaction),
                ArrivalSignal.ArrivalDefName,
                arrivalPawn,
                null,
                true);

            PawnDiaryRimTestScope.Require(
                diaryEvent.HasArrivalDescription()
                    && diaryEvent.IsArrivalDescriptionFor(
                        arrivalPawn.GetUniqueLoadID()),
                "Pawn.SetFaction did not create the pawn's neutral arrival description.");
            RequireContextContains(
                diaryEvent, "arrival_source=set_faction");
        }

        // ----- construction and isolation helpers -------------------------------------------------

        private static AbilityDef BuildInertAbilityDef(string defName)
        {
            return new AbilityDef
            {
                defName = defName,
                label = "inert lifecycle test ability",
                abilityClass = typeof(Ability),
                comps = new List<AbilityCompProperties>(),
                cooldownTicksRange = new IntRange(0, 0),
                hostile = false,
                verbProperties = new VerbProperties
                {
                    verbClass = typeof(Verb_CastAbility),
                    isPrimary = true
                }
            };
        }

        private static void ForceAbilityChanceToPass()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            float originalMinChance = tuning.abilityUseMinChance;
            float originalMaxChance = tuning.abilityUseMaxChance;
            scope.RegisterCleanup(() =>
            {
                tuning.abilityUseMinChance = originalMinChance;
                tuning.abilityUseMaxChance = originalMaxChance;
            });

            tuning.abilityUseMinChance = 1f;
            tuning.abilityUseMaxChance = 1f;
        }

        private static void SetCurrentWork(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.jobs == null)
            {
                throw new AssertionException(
                    "The work-scanner fixture pawn has no job tracker.");
            }

            WorkGiverDef workGiver = new WorkGiverDef
            {
                defName = "PawnDiary_RimTest_TopLevelWorkGiver",
                label = "performing fixture chores",
                workType = workType
            };
            Job previousJob = pawn.jobs.curJob;
            pawn.jobs.curJob = new Job(JobDefOf.Wait)
            {
                workGiverDef = workGiver
            };
            scope.RegisterCleanup(() =>
            {
                if (pawn.jobs != null)
                {
                    pawn.jobs.curJob = previousJob;
                }
            });
        }

        private static void ForceWorkPolicyToPass()
        {
            DiarySignalPolicyDef policy =
                DiarySignalPolicies.ForKey(DiarySignalPolicies.Work);
            bool originalEnabled = policy.enabled;
            float originalBaseChance = policy.baseChance;
            float originalPassionMultiplier =
                policy.passionChanceMultiplier;
            float originalNegativeMultiplier =
                policy.negativeChanceMultiplier;
            float originalDarkStudyMultiplier =
                policy.darkStudyChanceMultiplier;
            float originalRecentDifferentMultiplier =
                policy.recentDifferentTypeMultiplier;
            int originalSameTypeCooldown =
                policy.sameTypeCooldownTicks;
            int originalLowSkillThreshold = policy.lowSkillThreshold;

            scope.RegisterCleanup(() =>
            {
                policy.enabled = originalEnabled;
                policy.baseChance = originalBaseChance;
                policy.passionChanceMultiplier =
                    originalPassionMultiplier;
                policy.negativeChanceMultiplier =
                    originalNegativeMultiplier;
                policy.darkStudyChanceMultiplier =
                    originalDarkStudyMultiplier;
                policy.recentDifferentTypeMultiplier =
                    originalRecentDifferentMultiplier;
                policy.sameTypeCooldownTicks =
                    originalSameTypeCooldown;
                policy.lowSkillThreshold = originalLowSkillThreshold;
            });

            policy.enabled = true;
            policy.baseChance = ForcedWorkPassChance;
            policy.passionChanceMultiplier = 1f;
            policy.negativeChanceMultiplier = 1f;
            policy.darkStudyChanceMultiplier = 1f;
            policy.recentDifferentTypeMultiplier = 1f;
            policy.sameTypeCooldownTicks = 0;
            policy.lowSkillThreshold = 0;
        }

        /// <summary>
        /// Pins only the frequency rows exercised by this suite, then restores the exact sparse map.
        /// This keeps the boundary tests deterministic under any developer-selected preset or overrides.
        /// </summary>
        private static void ForceFrequencyToOne(params string[] groupKeys)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            Dictionary<string, float> original = settings.groupFrequencyOverrides == null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(
                    settings.groupFrequencyOverrides,
                    StringComparer.OrdinalIgnoreCase);
            scope.RegisterCleanup(() =>
            {
                settings.groupFrequencyOverrides = new Dictionary<string, float>(
                    original,
                    StringComparer.OrdinalIgnoreCase);
            });

            for (int i = 0; i < groupKeys.Length; i++)
            {
                settings.SetGroupFrequencyOverride(groupKeys[i], 1f);
            }
        }

        private static void IsolateFreeColonistCacheTo(Pawn pawn)
        {
            object originalColonists =
                CachedFreeColonistsField.GetValue(null);
            int originalTick =
                (int)CachedFreeColonistsTickField.GetValue(null);
            scope.RegisterCleanup(() =>
            {
                CachedFreeColonistsField.SetValue(
                    null, originalColonists);
                CachedFreeColonistsTickField.SetValue(
                    null, originalTick);
            });

            CachedFreeColonistsField.SetValue(
                null, new List<Pawn> { pawn });
            CachedFreeColonistsTickField.SetValue(
                null, Find.TickManager.TicksGame);
        }

        private static Quest BuildInertQuest(
            string rootDefName, string name)
        {
            return new Quest
            {
                id = Find.UniqueIDsManager.GetNextQuestID(),
                name = name,
                description =
                    "A controlled, unregistered loaded-game lifecycle fixture.",
                root = new QuestScriptDef
                {
                    defName = rootDefName,
                    label = name
                }
            };
        }

        private static HashSet<int> KnownAcceptedQuestIds()
        {
            HashSet<int> known =
                KnownAcceptedQuestIdsField.GetValue(scope.Component)
                    as HashSet<int>;
            if (known == null)
            {
                throw new AssertionException(
                    "Pawn Diary's accepted-quest bookkeeping set was unavailable.");
            }

            return known;
        }

        private static void SuppressErrorReporting()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException(
                    "Pawn Diary settings were unavailable for error-report isolation.");
            }

            bool originalErrorReporting = settings.enableErrorReporting;
            settings.enableErrorReporting = false;
            scope.RegisterCleanup(
                () => settings.enableErrorReporting =
                    originalErrorReporting);
        }

        private static void NeutralizeLlmDispatch()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException(
                    "Pawn Diary settings were unavailable for Arrival transport isolation.");
            }

            List<ApiEndpointConfig> originalEndpoints =
                settings.apiEndpoints;
            settings.apiEndpoints = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig
                {
                    enabled = false,
                    url = string.Empty,
                    model = string.Empty
                }
            };
            scope.RegisterCleanup(
                () => settings.apiEndpoints = originalEndpoints);
        }

        private static long CountOutcome(
            DiaryTelemetrySnapshot snapshot,
            DiaryTelemetryOutcome outcome)
        {
            long count = 0;
            if (snapshot == null)
            {
                return count;
            }

            for (int i = 0; i < snapshot.counters.Count; i++)
            {
                DiaryTelemetryCounter counter = snapshot.counters[i];
                if (counter != null && counter.outcome == outcome)
                {
                    count += counter.count;
                }
            }

            return count;
        }

        private static void RequireContextContains(
            DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent != null
                    && diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(
                        expectedFragment,
                        StringComparison.OrdinalIgnoreCase) >= 0,
                "The lifecycle event context did not contain '"
                    + expectedFragment + "'.");
        }

        private static void RequireReflectionSurface()
        {
            if (ScanPawnWorkMethod == null
                || CachedFreeColonistsField == null
                || CachedFreeColonistsTickField == null
                || KnownAcceptedQuestIdsField == null)
            {
                throw new AssertionException(
                    "Pawn Diary's top-level lifecycle fixture could not resolve a required private member.");
            }
        }
    }
}
