// In-game reaction tests for Pawn Diary's real RimWorld event hooks. Each test calls a vanilla game
// API that Pawn Diary observes through Harmony, then verifies the resulting saved DiaryEvent. The
// temporary pawns never join the map, have LLM generation disabled, and are fully removed afterward.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that representative vanilla event choke points reach Pawn Diary's persisted event store.
    /// These tests require a loaded game because the production capture pipeline intentionally ignores
    /// events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryEventReactionTests
    {
        private static readonly BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly FieldInfo DiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", PrivateInstance);
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo CommandStatusField =
            typeof(DiaryGameComponent).GetField("commandStatusByPawnId", PrivateInstance);

        private static DiaryGameComponent component;
        private static Pawn firstPawn;
        private static Pawn secondPawn;
        private static Dictionary<string, bool> originalGroupEnabled;
        private static readonly HashSet<LogEntry> AddedPlayLogEntries = new HashSet<LogEntry>();

        /// <summary>
        /// Creates isolated adult humanlike pawns and enables only the event groups required by this
        /// suite. Per-pawn generation is disabled before they become colonists, so no API request can
        /// leave the game while a test is running.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            if (!DiaryGameComponent.GamePlaying || Verse.Current.Game == null)
            {
                throw new AssertionException(
                    "PawnDiaryEventReactionTests require a loaded RimWorld game.");
            }

            component = DiaryGameComponent.Instance;
            if (component == null || PawnDiaryMod.Settings == null)
            {
                throw new AssertionException(
                    "Pawn Diary's game component and settings must be loaded before this suite runs.");
            }

            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null)
            {
                throw new AssertionException("The loaded game has no player faction.");
            }

            RequireReflectionField(EventsField, "events");
            RequireReflectionField(ArchiveField, "archive");
            RequireReflectionField(DiariesField, "diaries");
            RequireReflectionField(DiariesByIdField, "diariesById");

            originalGroupEnabled = new Dictionary<string, bool>(
                PawnDiaryMod.Settings.groupEnabled,
                StringComparer.OrdinalIgnoreCase);
            PawnDiaryMod.Settings.SetGroupEnabled("heartfelt", true);
            PawnDiaryMod.Settings.SetGroupEnabled("romance_relation", true);
            PawnDiaryMod.Settings.SetGroupEnabled("mentalbreakViolent", true);

            firstPawn = CreateIsolatedAdultColonist(playerFaction);
            secondPawn = CreateIsolatedAdultColonist(playerFaction);
            AddedPlayLogEntries.Clear();
        }

        /// <summary>
        /// Removes all test-owned events, diary indexes, Social-log rows, transient dedup keys, pawn
        /// relations, mental state, and pawns. RimTest Redux guarantees this runs even after a failure.
        /// </summary>
        [AfterEach]
        public static void TearDown()
        {
            Exception cleanupFailure = null;
            TryCleanup(() =>
            {
                if (firstPawn?.MentalState != null)
                {
                    firstPawn.MentalState.RecoverFromState();
                }
            }, ref cleanupFailure);

            TryCleanup(() =>
            {
                if (firstPawn?.relations != null && secondPawn != null)
                {
                    firstPawn.relations.TryRemoveDirectRelation(PawnRelationDefOf.Lover, secondPawn);
                }
            }, ref cleanupFailure);

            TryCleanup(RemoveTestPlayLogEntries, ref cleanupFailure);
            TryCleanup(RemoveTestDiaryState, ref cleanupFailure);
            TryCleanup(RemoveTransientKeysForTestPawns, ref cleanupFailure);
            TryCleanup(RestoreGroupSettings, ref cleanupFailure);
            TryCleanup(() => DestroyPawn(firstPawn), ref cleanupFailure);
            TryCleanup(() => DestroyPawn(secondPawn), ref cleanupFailure);

            firstPawn = null;
            secondPawn = null;
            component = null;
            AddedPlayLogEntries.Clear();

            if (cleanupFailure != null)
            {
                throw new AssertionException(
                    "Pawn Diary event-test cleanup failed after attempting every cleanup step: "
                    + cleanupFailure);
            }
        }

        /// <summary>
        /// Adds a normal vanilla social-log row and verifies that the PlayLog Harmony listener records
        /// one pairwise diary event linked back to that exact row.
        /// </summary>
        [Test]
        public static void SocialPlayLogEntryCreatesLinkedPairEvent()
        {
            InteractionDef interactionDef = RequireDef<InteractionDef>("DeepTalk");
            HashSet<string> before = SnapshotEventIds();

            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interactionDef,
                firstPawn,
                secondPawn);
            if (entry == null)
            {
                throw new AssertionException("Could not construct the vanilla DeepTalk PlayLog row.");
            }

            AddedPlayLogEntries.Add(entry);
            Find.PlayLog.Add(entry);

            DiaryEvent diaryEvent = RequireSingleNewEvent(
                before,
                "DeepTalk",
                firstPawn,
                secondPawn);
            Require(!diaryEvent.solo, "DeepTalk should create a pairwise diary event.");
            Require(
                string.Equals(diaryEvent.playLogInteractionDefName, "DeepTalk", StringComparison.Ordinal),
                "The diary event did not retain DeepTalk as its Social-log interaction Def.");
            Require(
                diaryEvent.playLogEntryIds != null && diaryEvent.playLogEntryIds.Contains(entry.LogID),
                "The diary event was not linked to the PlayLog row that triggered it.");
        }

        /// <summary>
        /// Adds a vanilla Lover relation and verifies that the relation Harmony listener records one
        /// pairwise romance milestone for the two pawns.
        /// </summary>
        [Test]
        public static void LoverRelationCreatesPairEvent()
        {
            HashSet<string> before = SnapshotEventIds();

            firstPawn.relations.AddDirectRelation(PawnRelationDefOf.Lover, secondPawn);

            DiaryEvent diaryEvent = RequireSingleNewEvent(
                before,
                "Lover",
                firstPawn,
                secondPawn);
            Require(!diaryEvent.solo, "A Lover relation should create a pairwise diary event.");
            Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("kind=lover", StringComparison.OrdinalIgnoreCase) >= 0,
                "The romance event context did not identify the new Lover relation.");
        }

        /// <summary>
        /// Starts a real vanilla mental state and verifies that the mental-state Harmony listener
        /// records a solo break for the affected pawn.
        /// </summary>
        [Test]
        public static void BerserkMentalStateCreatesSoloEvent()
        {
            MentalStateDef stateDef = RequireDef<MentalStateDef>("Berserk");
            HashSet<string> before = SnapshotEventIds();

            bool started = firstPawn.mindState.mentalStateHandler.TryStartMentalState(
                stateDef,
                "Pawn Diary RimTest event reaction",
                forced: true);
            Require(started, "Vanilla refused to start the forced Berserk mental state.");

            DiaryEvent diaryEvent = RequireSingleNewEvent(
                before,
                "Berserk",
                firstPawn,
                null);
            Require(diaryEvent.solo, "Berserk should create a solo diary event.");
            Require(
                string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId),
                "A solo mental-break event should not have a recipient pawn.");
            Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("mental_state=Berserk", StringComparison.OrdinalIgnoreCase) >= 0,
                "The mental-break context did not identify the Berserk state.");
        }

        private static Pawn CreateIsolatedAdultColonist(Faction playerFaction)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                faction: null,
                context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                allowPregnant: false,
                allowAddictions: false,
                fixedBiologicalAge: 30f,
                developmentalStages: DevelopmentalStage.Adult,
                forceNoGear: true);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                throw new AssertionException("RimWorld failed to generate an isolated test pawn.");
            }

            // Create the test pawn's diary record while it is still factionless, disable generation,
            // then make it an eligible colonist. Any SetFaction/arrival hook is now unable to call an API.
            component.SetDiaryGenerationEnabled(pawn, false);
            pawn.SetFaction(playerFaction);
            if (!DiaryGameComponent.IsDiaryEligible(pawn))
            {
                throw new AssertionException(
                    "Generated test pawn did not satisfy Pawn Diary's colonist eligibility rules.");
            }

            return pawn;
        }

        private static HashSet<string> SnapshotEventIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null && !string.IsNullOrWhiteSpace(diaryEvent.eventId))
                {
                    ids.Add(diaryEvent.eventId);
                }
            }

            return ids;
        }

        private static DiaryEvent RequireSingleNewEvent(
            HashSet<string> before,
            string defName,
            Pawn expectedInitiator,
            Pawn expectedRecipient)
        {
            string initiatorId = expectedInitiator?.GetUniqueLoadID() ?? string.Empty;
            string recipientId = expectedRecipient?.GetUniqueLoadID() ?? string.Empty;
            List<DiaryEvent> matches = new List<DiaryEvent>();
            IReadOnlyList<DiaryEvent> allEvents = EventRepository().AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null
                    || before.Contains(diaryEvent.eventId)
                    || !string.Equals(diaryEvent.interactionDefName, defName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(diaryEvent.initiatorPawnId, initiatorId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (expectedRecipient != null
                    && !string.Equals(diaryEvent.recipientPawnId, recipientId, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(diaryEvent);
            }

            if (matches.Count != 1)
            {
                throw new AssertionException(
                    "Expected exactly one new '" + defName + "' diary event for the test pawn(s), but found "
                    + matches.Count + ".");
            }

            return matches[0];
        }

        private static void RemoveTestPlayLogEntries()
        {
            List<LogEntry> allEntries = Find.PlayLog?.AllEntries;
            if (allEntries == null || AddedPlayLogEntries.Count == 0)
            {
                return;
            }

            allEntries.RemoveAll(entry => entry != null && AddedPlayLogEntries.Contains(entry));
        }

        private static void RemoveTestDiaryState()
        {
            if (component == null)
            {
                return;
            }

            HashSet<string> pawnIds = TestPawnIds();
            HashSet<string> eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DiaryEventRepository repository = EventRepository();
            IReadOnlyList<DiaryEvent> allEvents = repository.AllEvents;
            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent != null
                    && (pawnIds.Contains(diaryEvent.initiatorPawnId)
                        || pawnIds.Contains(diaryEvent.recipientPawnId)))
                {
                    eventIds.Add(diaryEvent.eventId);
                }
            }

            repository.RemoveEvents(eventIds);
            DiaryArchiveRepository archive =
                ArchiveField?.GetValue(component) as DiaryArchiveRepository;
            archive?.RemoveForEventIds(eventIds);

            List<PawnDiaryRecord> diaries =
                DiariesField?.GetValue(component) as List<PawnDiaryRecord>;
            if (diaries != null)
            {
                for (int i = diaries.Count - 1; i >= 0; i--)
                {
                    PawnDiaryRecord diary = diaries[i];
                    if (diary?.eventIds != null)
                    {
                        diary.eventIds.RemoveAll(id => eventIds.Contains(id));
                    }

                    if (diary != null && pawnIds.Contains(diary.pawnId))
                    {
                        diaries.RemoveAt(i);
                    }
                }
            }

            Dictionary<string, PawnDiaryRecord> diariesById =
                DiariesByIdField?.GetValue(component) as Dictionary<string, PawnDiaryRecord>;
            foreach (string pawnId in pawnIds)
            {
                diariesById?.Remove(pawnId);
            }

            DiaryStateVersion.Bump();
        }

        private static void RemoveTransientKeysForTestPawns()
        {
            if (component == null)
            {
                return;
            }

            HashSet<string> pawnIds = TestPawnIds();
            RemoveDictionaryKeysContaining(
                RecentEventsField?.GetValue(component) as IDictionary,
                pawnIds);

            IDictionary commandStatus = CommandStatusField?.GetValue(component) as IDictionary;
            foreach (string pawnId in pawnIds)
            {
                commandStatus?.Remove(pawnId);
            }
        }

        private static void RemoveDictionaryKeysContaining(
            IDictionary dictionary,
            HashSet<string> pawnIds)
        {
            if (dictionary == null || dictionary.Count == 0)
            {
                return;
            }

            List<object> remove = new List<object>();
            foreach (object key in dictionary.Keys)
            {
                string text = key as string;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                foreach (string pawnId in pawnIds)
                {
                    if (text.IndexOf(pawnId, StringComparison.Ordinal) >= 0)
                    {
                        remove.Add(key);
                        break;
                    }
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                dictionary.Remove(remove[i]);
            }
        }

        private static HashSet<string> TestPawnIds()
        {
            HashSet<string> pawnIds = new HashSet<string>(StringComparer.Ordinal);
            if (firstPawn != null)
            {
                pawnIds.Add(firstPawn.GetUniqueLoadID());
            }

            if (secondPawn != null)
            {
                pawnIds.Add(secondPawn.GetUniqueLoadID());
            }

            return pawnIds;
        }

        private static void RestoreGroupSettings()
        {
            if (PawnDiaryMod.Settings == null || originalGroupEnabled == null)
            {
                return;
            }

            PawnDiaryMod.Settings.groupEnabled.Clear();
            foreach (KeyValuePair<string, bool> pair in originalGroupEnabled)
            {
                PawnDiaryMod.Settings.groupEnabled[pair.Key] = pair.Value;
            }

            originalGroupEnabled = null;
        }

        private static void DestroyPawn(Pawn pawn)
        {
            if (pawn != null && !pawn.Destroyed)
            {
                pawn.Destroy(DestroyMode.Vanish);
            }
        }

        private static DiaryEventRepository EventRepository()
        {
            DiaryEventRepository repository =
                EventsField?.GetValue(component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException(
                    "Could not read Pawn Diary's event repository for the integration assertion.");
            }

            return repository;
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required vanilla " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }

        private static void RequireReflectionField(FieldInfo field, string fieldName)
        {
            if (field == null)
            {
                throw new AssertionException(
                    "Pawn Diary test cleanup could not locate private field '" + fieldName + "'.");
            }
        }

        // Cleanup must keep going after one failure so a broken assertion never strands a test pawn,
        // relation, Social-log row, or saved diary reference in the developer's loaded colony.
        private static void TryCleanup(Action action, ref Exception firstFailure)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                if (firstFailure == null)
                {
                    firstFailure = exception;
                }
            }
        }
    }
}
