// In-game save/load fixture for Pawn Diary's repository index rebuilds and retention
// (design/TEST_COVERAGE_PLAN.md §6.4, "repository/diary/archive index rebuilds ... retention"). This suite
// mostly builds DiaryEventRepository / DiaryArchiveRepository / PawnKnowledgeState model objects
// directly, round-trips their SAVED data through RimWorld's real Scribe to a temp file, and proves
// transient indexes rebuild correctly, loaded rows repair (incl. knowledge-record dedup), and retention
// drops the right rows. One focused integration case uses an uninitialized, detached component owner
// whose repositories/lists/indexes are all fixture-only, then invokes the production load-maintenance
// methods in their real repair -> retention -> reference-prune order.
//
// Why a real Scribe round-trip and not a whole-game save: the two repositories serialize only their
// master list (events.ExposeEvents "diaryEvents" / archive.ExposeArchive "diaryArchiveEntries"); the
// lookup indexes are rebuilt after load. DiaryGameComponent.ExposeData drives this in the live game —
// ExposeEvents/ExposeArchive run in both Scribe passes, then the component's own PostLoadInit calls
// events.RepairLoadedEvents(), while the archive rebuilds itself inside ExposeArchive's PostLoadInit branch
// (RepairLoadedEntries + RebuildIndex). We reproduce exactly that sequence standalone:
//   - SAVE: Scribe.saver.InitSaving(path,"root"); <repo>.ExposeX(label); FinalizeSaving().
//   - LOAD (vars): Scribe.loader.InitLoading(path); Scribe.mode=LoadingVars; <repo>.ExposeX(label);
//     FinalizeLoading() -> runs each loaded row's ExposeData PostLoadInit (real normalization).
//   - LOAD (post): for the archive, we then re-invoke ExposeArchive with Scribe.mode=PostLoadInit,
//     faithfully mirroring the component's second ExposeData pass so RepairLoadedEntries fires. In
//     PostLoadInit mode Scribe_Collections.Look is a no-op on the list, so this only runs the
//     repair+rebuild, never re-reads the (now closed) loader.
//
// Every Scribe block restores Scribe.mode and finalizes in finally so a failure can never leave the
// global Scribe state dirty for the player's game, and every temp file is deleted in finally.
//
// Standalone-Scribe caveat: the repositories are not IExposable, so nothing auto-invokes their
// ExposeX during FinalizeLoading; we call it ourselves (once per pass), which is exactly what the
// component does. Loaded hot events use RepairLoadedEvents so duplicate ids collapse and old blank
// ids are reported for referential repair. DiaryEvent/ArchivedDiaryEntry persist by string/value (no LookMode.Reference
// to live Pawns), so this object-level round-trip is valid without a loaded colony.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the never-serialized indexes of <see cref="DiaryEventRepository"/> and
    /// <see cref="DiaryArchiveRepository"/> rebuild after a real Scribe load, that
    /// <see cref="PawnKnowledgeState"/> round-trips with normalization repair, that retention
    /// prunes and re-indexes correctly, that the component's detached load-maintenance sequence preserves
    /// reidentified old rows, and that a reload drops duplicate archive rows.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRepositoryRebuildFixtureTests
    {
        private const string EventsLabel = "diaryEvents";
        private const string ArchiveLabel = "diaryArchiveEntries";
        private const string KnowledgeLabel = "pawnKnowledge";
        private const int DetachedRetentionLimit = 4;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // The integration fixture owns an uninitialized component whose complete mutable graph is
        // detached from DiaryGameComponent.Instance. These handles initialize only the fields touched by
        // the production repair -> retention -> prune sequence.
        private static readonly FieldInfo ComponentEventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo RepositoryEventsField =
            typeof(DiaryEventRepository).GetField("diaryEvents", PrivateInstance);
        private static readonly FieldInfo ComponentDiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly FieldInfo ComponentDiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", PrivateInstance);
        private static readonly FieldInfo ComponentArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly MethodInfo RebuildDiaryIndexMethod =
            typeof(DiaryGameComponent).GetMethod("RebuildDiaryIndex", PrivateInstance);
        private static readonly MethodInfo RepairReidentifiedEventRefsMethod =
            typeof(DiaryGameComponent).GetMethod("RepairReidentifiedEventRefs", PrivateInstance);
        private static readonly MethodInfo TrimDiariesToPerPawnLimitMethod =
            typeof(DiaryGameComponent).GetMethod("TrimDiariesToPerPawnLimit", PrivateInstance);
        private static readonly MethodInfo PruneDiaryEventRefsMethod =
            typeof(DiaryGameComponent).GetMethod("PruneDiaryEventRefs", PrivateInstance);

        /// <summary>
        /// The event repository's id index is not saved: after a Scribe round-trip every id is unknown
        /// until <see cref="DiaryEventRepository.RebuildIndex"/> runs, after which FindEvent/ContainsEvent
        /// resolve every registered id (and an unknown id still returns null/false).
        /// </summary>
        [Test]
        public static void EventIndexRebuildsFromLoadedListViaRebuildIndex()
        {
            string[] ids = { "pd-rebuild-evt-1", "pd-rebuild-evt-2", "pd-rebuild-evt-3", "pd-rebuild-evt-4" };
            DiaryEventRepository source = new DiaryEventRepository();
            source.Register(NewEvent(ids[0], "PawnA", solo: false, tick: 100));
            source.Register(NewEvent(ids[1], "PawnA", solo: false, tick: 200));
            source.Register(NewEvent(ids[2], "PawnB", solo: true, tick: 300));
            source.Register(NewEvent(ids[3], "PawnB", solo: false, tick: 400));

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => source.ExposeEvents(EventsLabel));

                DiaryEventRepository loaded = new DiaryEventRepository();
                LoadVarsWithScribe(path, () => loaded.ExposeEvents(EventsLabel));

                // The master list survives the round-trip, but the index is NOT serialized: every
                // lookup misses until the index is rebuilt.
                Require(loaded.Count == ids.Length,
                    "Loaded event count " + loaded.Count + " did not match the saved " + ids.Length + ".");
                for (int i = 0; i < ids.Length; i++)
                {
                    Require(loaded.FindEvent(ids[i]) == null && !loaded.ContainsEvent(ids[i]),
                        "The id lookup index must be empty before a rebuild, but '" + ids[i] + "' resolved.");
                }

                loaded.RebuildIndex();

                for (int i = 0; i < ids.Length; i++)
                {
                    DiaryEvent found = loaded.FindEvent(ids[i]);
                    Require(found != null && string.Equals(found.eventId, ids[i], StringComparison.OrdinalIgnoreCase),
                        "After RebuildIndex, FindEvent should resolve '" + ids[i] + "' to its event.");
                    Require(loaded.ContainsEvent(ids[i]),
                        "After RebuildIndex, ContainsEvent should be true for '" + ids[i] + "'.");
                }

                Require(loaded.FindEvent("pd-rebuild-missing") == null && !loaded.ContainsEvent("pd-rebuild-missing"),
                    "An unknown id must resolve to null/false after a rebuild.");
            });
        }

        /// <summary>
        /// <see cref="DiaryEventRepository.EnsureIndexReady"/> is the defensive rebuild the prune path
        /// relies on before the normal PostLoadInit rebuild: it populates the empty post-load index from
        /// the loaded list so FindEvent resolves.
        /// </summary>
        [Test]
        public static void EventIndexRebuildsViaEnsureIndexReadyAfterLoad()
        {
            string[] ids = { "pd-ensure-evt-1", "pd-ensure-evt-2" };
            DiaryEventRepository source = new DiaryEventRepository();
            source.Register(NewEvent(ids[0], "PawnA", solo: false, tick: 10));
            source.Register(NewEvent(ids[1], "PawnB", solo: true, tick: 20));

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => source.ExposeEvents(EventsLabel));

                DiaryEventRepository loaded = new DiaryEventRepository();
                LoadVarsWithScribe(path, () => loaded.ExposeEvents(EventsLabel));

                Require(loaded.FindEvent(ids[0]) == null,
                    "Index should be empty immediately after load (never serialized).");

                loaded.EnsureIndexReady();

                for (int i = 0; i < ids.Length; i++)
                {
                    Require(loaded.FindEvent(ids[i]) != null && loaded.ContainsEvent(ids[i]),
                        "EnsureIndexReady should have rebuilt the lookup for '" + ids[i] + "'.");
                }
            });
        }

        /// <summary>
        /// Post-load hot-event repair keeps the earliest row in stable tick order for a duplicate id,
        /// drops the duplicate, rebuilds the index, and reports a legacy blank-id row after
        /// <see cref="DiaryEvent"/> minted its replacement id.
        /// </summary>
        [Test]
        public static void LoadedHotEventsRepairBlankIdsAndDeterministicallyDeduplicate()
        {
            DiaryEventRepository source = RepositoryWithLoadedRows(
                NewEvent("pd-duplicate", "PawnLate", solo: true, tick: 300),
                NewEvent("PD-DUPLICATE", "PawnEarly", solo: true, tick: 100),
                NewEvent(string.Empty, "PawnLegacy", solo: true, tick: 200));

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => source.ExposeEvents(EventsLabel));

                DiaryEventRepository loaded = new DiaryEventRepository();
                LoadVarsWithScribe(path, () => loaded.ExposeEvents(EventsLabel));
                Require(loaded.Count == 3,
                    "The raw loaded list should still contain both duplicate rows before repository repair.");

                IReadOnlyList<DiaryEvent> reidentified = loaded.RepairLoadedEvents();

                Require(loaded.Count == 2,
                    "Loaded hot-event repair should keep one duplicate plus the reidentified legacy row.");
                DiaryEvent duplicateSurvivor = loaded.FindEvent("pd-duplicate");
                Require(duplicateSurvivor != null
                        && duplicateSurvivor.tick == 100
                        && duplicateSurvivor.initiatorPawnId == "PawnEarly",
                    "Duplicate repair did not deterministically keep the earliest stable-tick row.");
                Require(reidentified.Count == 1
                        && reidentified[0].EventIdWasRepairedOnLoad
                        && !string.IsNullOrWhiteSpace(reidentified[0].eventId)
                        && loaded.FindEvent(reidentified[0].eventId) == reidentified[0],
                    "The legacy blank-id row was not reported with an indexed replacement id.");
            });
        }

        /// <summary>
        /// Reidentified events reconnect only to blank placeholders in already-saved owner diaries.
        /// Pair owners, neutral arrival/death markers, exact list position, and case-insensitive
        /// duplicate refs are handled without creating a record for an unknown/later owner.
        /// </summary>
        [Test]
        public static void ReidentifiedEventRefsRestoreExistingOwnersWithoutInventingDiaries()
        {
            PawnDiaryRecord initiator = NewDiary("PawnA", string.Empty, "existing-later");
            PawnDiaryRecord recipient = NewDiary("PawnB", "PD-PAIR-REPAIRED");
            PawnDiaryRecord arrivalOwner = NewDiary(
                "PawnArrival",
                "existing-after-arrival",
                string.Empty);
            PawnDiaryRecord deathOwner = NewDiary("PawnVictim", string.Empty);
            Dictionary<string, PawnDiaryRecord> diaries =
                new Dictionary<string, PawnDiaryRecord>(StringComparer.Ordinal)
                {
                    { initiator.pawnId, initiator },
                    { recipient.pawnId, recipient },
                    { arrivalOwner.pawnId, arrivalOwner },
                    { deathOwner.pawnId, deathOwner },
                };

            DiaryEvent pair = NewEvent("pd-pair-repaired", "PawnA", solo: false, tick: 20);
            pair.recipientPawnId = "PawnB";
            DiaryEvent arrival = NewEvent("pd-arrival-repaired", string.Empty, solo: true, tick: 5);
            arrival.gameContext = "arrival_description=true; arrival_pawn_id=PawnArrival";
            DiaryEvent death = NewEvent("pd-death-repaired", string.Empty, solo: true, tick: 40);
            death.gameContext = "death_description=true; death_victim_id=PawnVictim";
            DiaryEvent orphan = NewEvent("pd-orphan-repaired", "PawnMissing", solo: true, tick: 30);

            DiaryGameComponent.RestoreReidentifiedEventRefs(
                new List<DiaryEvent> { pair, arrival, death, orphan },
                pawnId =>
                {
                    PawnDiaryRecord found;
                    return diaries.TryGetValue(pawnId, out found) ? found : null;
                });

            Require(initiator.eventIds.Count == 2
                    && initiator.eventIds[0] == pair.eventId,
                "The repaired pair ref did not replace its saved blank placeholder.");
            Require(recipient.eventIds.Count == 1
                    && recipient.eventIds[0] == "PD-PAIR-REPAIRED",
                "Case-insensitive ref repair duplicated an existing recipient reference.");
            Require(arrivalOwner.eventIds.Count == 2
                    && arrivalOwner.eventIds[0] == arrival.eventId,
                "A repaired neutral arrival was not restored as the diary's first page.");
            Require(deathOwner.eventIds.Count == 1
                    && deathOwner.eventIds[0] == death.eventId,
                "A repaired neutral death page was not restored to its saved victim diary.");
            Require(!diaries.ContainsKey("PawnMissing"),
                "Referential repair invented a diary record for an unknown owner.");
        }

        /// <summary>
        /// Runs the production load-maintenance methods in their real order on a wholly detached component
        /// owner. Two old blank-id rows receive replacement ids in tick order; the owner's placeholders
        /// reconnect; retention removes an unreferenced control; and reference pruning removes stale ids
        /// without dropping either repaired row. No saved collection belongs to the loaded game.
        /// </summary>
        [Test]
        public static void DetachedLoadMaintenanceKeepsReidentifiedRowsThroughRetentionAndReferencePrune()
        {
            RequireDetachedMaintenanceReflection();
            const string ownerId = "Pawn_RimTest_DetachedRepair";
            PawnDiaryRecord ownerDiary = NewDiary(ownerId);
            DiaryEventRepository repository = new DiaryEventRepository();
            DiaryGameComponent component = NewDetachedMaintenanceOwner(
                ownerDiary,
                repository,
                new DiaryArchiveRepository());
            DiaryGameComponent liveComponent = DiaryGameComponent.Instance;
            DiaryEventRepository liveRepository = liveComponent == null
                ? null
                : ComponentEventsField.GetValue(liveComponent) as DiaryEventRepository;
            Require(!ReferenceEquals(component, liveComponent)
                    && !ReferenceEquals(repository, liveRepository),
                "The load-maintenance fixture accidentally shared the loaded component or repository.");

            int now = Find.TickManager.TicksGame;
            DiaryEvent later = NewEvent(string.Empty, ownerId, solo: true, tick: now + 2);
            DiaryEvent earlier = NewEvent(string.Empty, ownerId, solo: true, tick: now + 1);
            DiaryEvent unreferenced = NewEvent(
                "pd-postload-orphan-" + Guid.NewGuid().ToString("N"),
                ownerId,
                solo: true,
                tick: now + 3);
            RepositoryEventsField.SetValue(
                repository,
                new List<DiaryEvent> { later, earlier, unreferenced });

            // Four stale refs + two blank legacy placeholders exceed the explicit detached cap by two,
            // so production retention removes two stale refs and sweeps the unreferenced control row.
            // PruneDiaryEventRefs later removes the two remaining stale refs.
            for (int i = 0; i < DetachedRetentionLimit; i++)
            {
                ownerDiary.eventIds.Add("pd-postload-stale-" + ownerId + "-" + i);
            }
            ownerDiary.eventIds.Add(string.Empty);
            ownerDiary.eventIds.Add(string.Empty);

            NormalizeLegacyRows(ownerDiary, later, earlier);
            RunDetachedLoadMaintenance(component, repository);

            Require(earlier.EventIdWasRepairedOnLoad
                    && later.EventIdWasRepairedOnLoad
                    && !string.IsNullOrWhiteSpace(earlier.eventId)
                    && !string.IsNullOrWhiteSpace(later.eventId),
                "PostLoadInit did not mint non-blank ids for both legacy rows.");
            Require(repository.FindEvent(unreferenced.eventId) == null,
                "The component retention sweep did not remove the unreferenced control row.");
            Require(repository.FindEvent(earlier.eventId) == earlier
                    && repository.FindEvent(later.eventId) == later,
                "A reidentified row did not survive retention in the rebuilt repository index.");
            Require(ownerDiary.eventIds.Count == 2,
                "Reference pruning should leave only the two repaired refs, not "
                + ownerDiary.eventIds.Count + ".");
            Require(string.Equals(ownerDiary.eventIds[0], earlier.eventId, StringComparison.Ordinal)
                    && string.Equals(ownerDiary.eventIds[1], later.eventId, StringComparison.Ordinal),
                "Multiple blank placeholders did not reconnect in stable tick order.");
        }

        /// <summary>
        /// Reference maintenance must remove an unowned master event even when every diary is below its
        /// retention cap. Previously only the over-cap retention path happened to sweep these rows.
        /// </summary>
        [Test]
        public static void ReferencePruneSweepsUnderCapOrphanMasterRows()
        {
            RequireDetachedMaintenanceReflection();
            const string ownerId = "Pawn_RimTest_UnderCapOrphan";
            PawnDiaryRecord ownerDiary = NewDiary(ownerId);
            DiaryEvent referenced = NewEvent(
                "pd-under-cap-owned-" + Guid.NewGuid().ToString("N"),
                ownerId,
                solo: true,
                tick: Find.TickManager.TicksGame);
            DiaryEvent orphan = NewEvent(
                "pd-under-cap-orphan-" + Guid.NewGuid().ToString("N"),
                ownerId,
                solo: true,
                tick: Find.TickManager.TicksGame + 1);
            ownerDiary.eventIds.Add(referenced.eventId);

            DiaryEventRepository repository = RepositoryWithLoadedRows(referenced, orphan);
            DiaryGameComponent component = NewDetachedMaintenanceOwner(
                ownerDiary,
                repository,
                new DiaryArchiveRepository());

            PruneDiaryEventRefsMethod.Invoke(component, null);

            Require(repository.FindEvent(referenced.eventId) == referenced,
                "Reference maintenance removed the under-cap event that still had a diary owner.");
            Require(repository.FindEvent(orphan.eventId) == null,
                "Reference maintenance left an under-cap orphan master row behind.");
            Require(ownerDiary.eventIds.Count == 1 && ownerDiary.eventIds[0] == referenced.eventId,
                "Under-cap orphan cleanup changed the surviving diary reference.");
        }

        /// <summary>
        /// The production pre-save runner must execute later flush/maintenance actions even when an
        /// earlier source throws, and must report the exact failed action index once.
        /// </summary>
        [Test]
        public static void PreSaveActionFailureCannotSkipLaterFlushes()
        {
            List<int> executed = new List<int>();
            List<int> failed = new List<int>();
            DiaryGameComponent.RunIndependentPreSaveActions(
                new Action[]
                {
                    () => executed.Add(0),
                    () =>
                    {
                        executed.Add(1);
                        throw new InvalidOperationException("fixture failure");
                    },
                    () => executed.Add(2),
                },
                (index, exception) =>
                {
                    Require(exception is InvalidOperationException,
                        "Pre-save failure reporting changed the original exception type.");
                    failed.Add(index);
                });

            Require(executed.Count == 3
                    && executed[0] == 0
                    && executed[1] == 1
                    && executed[2] == 2,
                "A failed pre-save action prevented a later action from running.");
            Require(failed.Count == 1 && failed[0] == 1,
                "The pre-save runner did not report the exact failed action once.");
        }

        /// <summary>
        /// The per-pawn knowledge state (MEMORY_SYSTEM_REDESIGN_PLAN §2.2, §4.1) round-trips
        /// through real Scribe preserving culture provenance, important-event records, and a
        /// player/editor-authored memory-text override. Normalize() repairs a hand-edited save:
        /// null lists/text heal, parallel fact lists realign,
        /// and duplicated dedup keys collapse to one record.
        /// </summary>
        [Test]
        public static void KnowledgeStateRoundTripsPreservesCultureAndDedups()
        {
            PawnKnowledgeState source = new PawnKnowledgeState
            {
                pawnId = "PawnA",
                // Version 1 equals the stable Scribe default, so its schema XML key is omitted. The
                // load assertion below proves a missing legacy key does not inherit the v2 initializer.
                schemaVersion = 1,
                originCultureDefName = "Rustican",
                originCultureSource = "captured",
                adoptedCultureDefName = "Corunan",
            };
            ImportantMemoryRecord overridden = NewKnowledgeRecord("rec-1", "relation.spouse.gained", 200);
            overridden.manualTextOverride = "Brik and I chose each other beneath the stars.";
            source.records.Add(overridden);
            ImportantMemoryRecord newSchemaRow = NewKnowledgeRecord("rec-2", "body.part.lost", 300);
            newSchemaRow.sourceKind = KnowledgeTokens.SourceKindPlayer;
            newSchemaRow.recallScope = KnowledgeTokens.RecallScopeBackground;
            source.records.Add(newSchemaRow);

            RunWithTempFile(path =>
            {
                PawnKnowledgeState saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, KnowledgeLabel));

                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, KnowledgeLabel));
                Require(loaded != null, "The knowledge state must round-trip through Scribe.");
                Require(loaded.schemaVersion == 1,
                    "A missing legacy schema key must load as v1 before normalization, not inherit v2.");
                loaded.Normalize();
                Require(loaded.schemaVersion == PawnKnowledgeState.CurrentSchemaVersion,
                    "Normalize must migrate the per-state schema additively to v2.");

                Require(loaded.originCultureDefName == "Rustican"
                        && loaded.originCultureSource == "captured"
                        && loaded.adoptedCultureDefName == "Corunan",
                    "Culture provenance must survive the round trip unchanged.");
                Require(loaded.records.Count == 2,
                    "Both important-event records must survive; got " + loaded.records.Count + ".");
                ImportantMemoryRecord first = loaded.records[0];
                Require(first.eventKind == "relation.spouse.gained" && first.tick == 200,
                    "Record kind/tick must survive the round trip.");
                Require(first.manualTextOverride == "Brik and I chose each other beneath the stars.",
                    "The editor-authored memory-text override must survive the round trip.");
                Require(first.sourceKind == KnowledgeTokens.SourceKindCaptured
                        && first.recallScope == KnowledgeTokens.RecallScopeContextual,
                    "An old/default record must normalize to captured/contextual.");
                Require(loaded.records[1].sourceKind == KnowledgeTokens.SourceKindPlayer
                        && loaded.records[1].recallScope == KnowledgeTokens.RecallScopeBackground,
                    "The additive player/background fields must survive Scribe.");
                Require(first.participantIds.Count == 1 && first.participantNames[0] == "Brik",
                    "Participant ids and saved display names must survive.");
                Require(first.subjectKeys.Count == 1 && first.factKeys.Count == first.factValues.Count,
                    "Subject keys and parallel fact lists must survive aligned.");
                Require(loaded.HasDedupKey(first.dedupKey),
                    "The dedup index view must see the loaded record.");

                // Hand-edited save repair: a duplicated dedup key collapses, null lists heal.
                loaded.records.Add(NewKnowledgeRecord("rec-1", "relation.spouse.gained", 200));
                loaded.records.Add(new ImportantMemoryRecord
                {
                    recordId = "rec-3",
                    dedupKey = "d3",
                    participantIds = null,
                    participantNames = null,
                    subjectKeys = null,
                    factKeys = null,
                    factValues = null,
                    sourceKind = "future-source",
                    recallScope = null,
                    manualTextOverride = null,
                });
                loaded.Normalize();
                Require(loaded.records.Count == 3,
                    "Normalize must drop the duplicated dedup key (2 originals + repaired rec-3), got "
                    + loaded.records.Count + ".");
                ImportantMemoryRecord repaired = loaded.records[2];
                Require(repaired.participantIds != null && repaired.factKeys != null,
                    "Normalize must heal null record lists.");
                Require(repaired.manualTextOverride == string.Empty,
                    "Normalize must heal a null memory-text override to an empty string.");
                Require(repaired.sourceKind == KnowledgeTokens.SourceKindCaptured
                        && repaired.recallScope == KnowledgeTokens.RecallScopeContextual,
                    "Unknown/missing additive tokens must repair to captured/contextual.");

                // Profile draft seeding is read-only: detached snapshots tolerate raw null lists
                // without first mutating the save model through Normalize().
                ImportantMemoryRecord raw = new ImportantMemoryRecord
                {
                    recordId = "raw",
                    dedupKey = "raw",
                    sourceKind = "PLAYER",
                    recallScope = "BACKGROUND",
                    participantIds = null,
                    participantNames = null,
                    subjectKeys = null,
                    factKeys = null,
                    factValues = null
                };
                ImportantMemoryRecordSnapshot rawSnapshot = raw.ToSnapshot();
                Require(rawSnapshot.sourceKind == KnowledgeTokens.SourceKindPlayer
                        && rawSnapshot.recallScope == KnowledgeTokens.RecallScopeBackground,
                    "Detached conversion must normalize copied tokens without mutating the row.");
                Require(rawSnapshot.participants.Count == 0
                        && rawSnapshot.subjectKeys.Count == 0
                        && rawSnapshot.facts.Count == 0,
                    "Detached conversion must treat raw null lists as empty.");
                Require(raw.participantIds == null && raw.subjectKeys == null && raw.factKeys == null,
                    "Read-only detached conversion must not repair/mutate the saved row.");

                PawnKnowledgeState rawState = new PawnKnowledgeState
                {
                    pawnId = "RawPawn",
                    records = null
                };
                Require(rawState.ToRecordSnapshots().Count == 0 && rawState.records == null,
                    "A no-create profile read must treat a null record list as empty without mutation.");
            });
        }

        /// <summary>
        /// <see cref="DiaryEventRepository.RetainOnly"/> drops master-list events no pawn references
        /// anymore and rebuilds the index so the removed ids no longer resolve while the kept ids do.
        /// </summary>
        [Test]
        public static void RetainOnlyDropsUnreferencedEventsAndRebuildsIndex()
        {
            DiaryEventRepository repo = new DiaryEventRepository();
            repo.Register(NewEvent("pd-retain-1", "PawnA", solo: false, tick: 100));
            repo.Register(NewEvent("pd-retain-2", "PawnA", solo: false, tick: 200));
            repo.Register(NewEvent("pd-retain-3", "PawnB", solo: true, tick: 300));
            repo.Register(NewEvent("pd-retain-4", "PawnB", solo: false, tick: 400));

            HashSet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pd-retain-1",
                "pd-retain-3",
            };

            int removed = repo.RetainOnly(referenced);

            Require(removed == 2, "RetainOnly should have removed the 2 unreferenced events, not " + removed + ".");
            Require(repo.Count == 2, "RetainOnly should have left exactly 2 events, not " + repo.Count + ".");

            // Kept ids resolve through the rebuilt index; dropped ids resolve to null and are absent.
            Require(repo.FindEvent("pd-retain-1") != null && repo.ContainsEvent("pd-retain-1"),
                "A referenced event should survive RetainOnly and stay indexed.");
            Require(repo.FindEvent("pd-retain-3") != null && repo.ContainsEvent("pd-retain-3"),
                "A referenced event should survive RetainOnly and stay indexed.");
            Require(repo.FindEvent("pd-retain-2") == null && !repo.ContainsEvent("pd-retain-2"),
                "An unreferenced event must be dropped from the list and the rebuilt index.");
            Require(repo.FindEvent("pd-retain-4") == null && !repo.ContainsEvent("pd-retain-4"),
                "An unreferenced event must be dropped from the list and the rebuilt index.");

            // A null referenced-set is a no-op guard (returns 0, keeps the store intact).
            int noneRemoved = repo.RetainOnly(null);
            Require(noneRemoved == 0 && repo.Count == 2,
                "RetainOnly(null) must be a no-op that removes nothing.");
        }

        /// <summary>
        /// <see cref="DiaryArchiveRepository"/> resolves EntriesForPawn / Contains / FindByEventAndRole
        /// from its inserted rows, and an explicit <see cref="DiaryArchiveRepository.RebuildIndex"/>
        /// reconstructs the same lookups from the master list.
        /// </summary>
        [Test]
        public static void ArchiveLookupsRebuildFromEntries()
        {
            DiaryArchiveRepository archive = BuildArchiveFixture();

            AssertArchiveLookups(archive, "after AddOrKeep");

            // The three lookup indexes are transient mirrors of the master list; rebuilding must
            // reconstruct every one of them identically.
            archive.RebuildIndex();
            AssertArchiveLookups(archive, "after RebuildIndex");
        }

        /// <summary>
        /// N1's compact narrative references build pawn-scoped arc/subject indexes from cold pages.
        /// Rebuilding and retention must leave the same surviving page set visible and must never let
        /// another pawn's reference satisfy the current POV's lookup.
        /// </summary>
        [Test]
        public static void NarrativeArchiveIndexesRebuildAndRespectRetention()
        {
            DiaryArchiveRepository archive = new DiaryArchiveRepository();
            ArchivedDiaryEntry first = NewArchive("pd-narrative-A1", "PawnA", DiaryEvent.InitiatorRole, 100);
            ArchivedDiaryEntry second = NewArchive("pd-narrative-A2", "PawnA", DiaryEvent.InitiatorRole, 200);
            ArchivedDiaryEntry otherPawn = NewArchive("pd-narrative-B1", "PawnB", DiaryEvent.InitiatorRole, 100);
            first.narrativeReferences = FixtureNarrativeReferences();
            second.narrativeReferences = FixtureNarrativeReferences();
            otherPawn.narrativeReferences = FixtureNarrativeReferences();
            first.narrativeSelectedCandidateKeys = new List<string> { "core-fixture-identity" };
            second.narrativeSelectedCandidateKeys = new List<string> { "core-fixture-identity" };

            RequireAdded(archive, first);
            RequireAdded(archive, second);
            RequireAdded(archive, otherPawn);
            AssertNarrativeArchiveLookups(archive, "after AddOrKeep", expectedPawnARows: 2);

            archive.RebuildIndex();
            AssertNarrativeArchiveLookups(archive, "after RebuildIndex", expectedPawnARows: 2);

            Require(archive.TrimPerPawnLimit(1),
                "Retention should trim PawnA's oldest cold narrative row when capped to one.");
            Require(!archive.Contains("pd-narrative-A1", "PawnA", DiaryEvent.InitiatorRole),
                "Retention should remove the oldest PawnA narrative row.");
            AssertNarrativeArchiveLookups(archive, "after TrimPerPawnLimit", expectedPawnARows: 1);
        }

        /// <summary>
        /// <see cref="DiaryArchiveRepository.RemoveForEventIds"/> prunes every row for the given event
        /// ids and rebuilds the indexes so the removed rows stop resolving while the rest still do.
        /// </summary>
        [Test]
        public static void ArchiveRemoveForEventIdsPrunesAndRebuilds()
        {
            DiaryArchiveRepository archive = BuildArchiveFixture();
            int before = archive.Count;

            int removed = archive.RemoveForEventIds(new HashSet<string>(StringComparer.Ordinal) { "pd-arc-A1" });

            Require(removed == 1, "Removing one event id should have pruned exactly 1 archive row, not " + removed + ".");
            Require(archive.Count == before - 1, "Archive count should drop by exactly the removed row.");
            Require(!archive.Contains("pd-arc-A1", "PawnA", DiaryEvent.InitiatorRole),
                "The pruned row must no longer be Contains()-resolvable.");
            Require(archive.FindByEventAndRole("pd-arc-A1", DiaryEvent.InitiatorRole) == null,
                "The pruned row must no longer resolve via the rebuilt (eventId,role) index.");
            Require(archive.EntriesForPawn("PawnA").Count == 3,
                "PawnA should have 3 rows left after pruning one of its four.");

            // Surviving rows still resolve through the rebuilt indexes.
            Require(archive.Contains("pd-arc-A2", "PawnA", DiaryEvent.InitiatorRole),
                "A surviving row must stay resolvable after the prune+rebuild.");
            Require(archive.FindByEventAndRole("pd-arc-P", DiaryEvent.RecipientRole) != null,
                "The paired recipient row must survive the prune of an unrelated event.");
        }

        /// <summary>
        /// <see cref="DiaryArchiveRepository.TrimPerPawnLimit"/> caps each pawn's rows to the newest N,
        /// dropping the oldest, and returns false when nothing exceeds the cap.
        /// </summary>
        [Test]
        public static void ArchiveTrimPerPawnLimitCapsNewestRows()
        {
            DiaryArchiveRepository archive = BuildArchiveFixture();
            // Fixture: PawnA has 4 rows (ticks 100/200/300/400), PawnB has 2 rows (ticks 100/400).

            bool trimmed = archive.TrimPerPawnLimit(2);

            Require(trimmed, "TrimPerPawnLimit(2) should report that it trimmed PawnA's over-limit rows.");
            Require(archive.EntriesForPawn("PawnA").Count == 2,
                "PawnA should be capped to its newest 2 rows, not " + archive.EntriesForPawn("PawnA").Count + ".");
            Require(archive.EntriesForPawn("PawnB").Count == 2,
                "PawnB was at the cap and must be left untouched.");
            Require(archive.Count == 4, "Total rows after the cap should be 2 + 2 = 4.");

            // The two OLDEST PawnA rows (ticks 100 and 200) are dropped; the newest survive.
            Require(!archive.Contains("pd-arc-A1", "PawnA", DiaryEvent.InitiatorRole),
                "The oldest PawnA row (tick 100) should be trimmed.");
            Require(!archive.Contains("pd-arc-A2", "PawnA", DiaryEvent.InitiatorRole),
                "The next-oldest PawnA row (tick 200) should be trimmed.");
            Require(archive.Contains("pd-arc-A3", "PawnA", DiaryEvent.InitiatorRole),
                "A newest PawnA row (tick 300) should survive the cap.");
            Require(archive.Contains("pd-arc-P", "PawnA", DiaryEvent.InitiatorRole),
                "The newest PawnA row (tick 400) should survive the cap.");
            NoDuplicateArchiveKeys(archive, "after TrimPerPawnLimit");

            // Idempotent: nothing now exceeds the cap, so a second trim is a no-op.
            Require(!archive.TrimPerPawnLimit(2),
                "A second TrimPerPawnLimit(2) must return false when no pawn exceeds the cap.");
        }

        /// <summary>
        /// Reloading a save whose archive list carries a duplicate row (older/corrupt save) drops the
        /// duplicate: <see cref="DiaryArchiveRepository"/>'s PostLoadInit runs RepairLoadedEntries, so no
        /// two rows share an ArchiveKey and every lookup resolves to a single row.
        /// </summary>
        [Test]
        public static void ArchiveReloadDropsDuplicateRows()
        {
            // A hand-built list with a duplicate ArchiveKey (same eventId/pawnId/povRole). AddOrKeep
            // would refuse the duplicate, so we save the list directly to model an older/corrupt save.
            ArchivedDiaryEntry rowA = NewArchive("pd-dup-A", "PawnA", DiaryEvent.InitiatorRole, 100);
            ArchivedDiaryEntry rowB = NewArchive("pd-dup-B", "PawnB", DiaryEvent.InitiatorRole, 200);
            ArchivedDiaryEntry rowADuplicate = NewArchive("pd-dup-A", "PawnA", DiaryEvent.InitiatorRole, 100);
            List<ArchivedDiaryEntry> listWithDup = new List<ArchivedDiaryEntry> { rowA, rowB, rowADuplicate };

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () =>
                    Scribe_Collections.Look(ref listWithDup, ArchiveLabel, LookMode.Deep));

                DiaryArchiveRepository loaded = new DiaryArchiveRepository();
                // Pass 1 (LoadingVars): load the raw list, still holding the duplicate.
                LoadVarsWithScribe(path, () => loaded.ExposeArchive(ArchiveLabel));
                // Pass 2 (PostLoadInit): mirror DiaryGameComponent's second ExposeData pass, which is
                // where ExposeArchive runs RepairLoadedEntries + RebuildIndex.
                RunArchivePostLoadInit(loaded);

                Require(loaded.Count == 2,
                    "RepairLoadedEntries should drop the duplicate row, leaving 2, not " + loaded.Count + ".");
                NoDuplicateArchiveKeys(loaded, "after a reload with a duplicate row");
                Require(loaded.EntriesForPawn("PawnA").Count == 1,
                    "PawnA should keep a single de-duplicated row after the reload.");
                Require(loaded.Contains("pd-dup-A", "PawnA", DiaryEvent.InitiatorRole)
                        && loaded.Contains("pd-dup-B", "PawnB", DiaryEvent.InitiatorRole),
                    "Both distinct rows must stay resolvable after the reload.");
                Require(loaded.FindByEventAndRole("pd-dup-A", DiaryEvent.InitiatorRole) != null,
                    "The de-duplicated row must resolve via the rebuilt (eventId,role) index.");
            });
        }

        // ----- fixtures ---------------------------------------------------------------------------

        // PawnA: four initiator rows (ticks 100/200/300/400 via A1/A2/A3 + paired P).
        // PawnB: one initiator row (B1, tick 100) plus the paired recipient row (P, tick 400).
        // The paired event "pd-arc-P" gives a distinct (eventId, povRole) pair for both roles so
        // FindByEventAndRole has a two-role case to resolve.
        private static DiaryArchiveRepository BuildArchiveFixture()
        {
            DiaryArchiveRepository archive = new DiaryArchiveRepository();
            RequireAdded(archive, NewArchive("pd-arc-A1", "PawnA", DiaryEvent.InitiatorRole, 100));
            RequireAdded(archive, NewArchive("pd-arc-A2", "PawnA", DiaryEvent.InitiatorRole, 200));
            RequireAdded(archive, NewArchive("pd-arc-A3", "PawnA", DiaryEvent.InitiatorRole, 300));
            RequireAdded(archive, NewArchive("pd-arc-B1", "PawnB", DiaryEvent.InitiatorRole, 100));
            RequireAdded(archive, NewArchive("pd-arc-P", "PawnA", DiaryEvent.InitiatorRole, 400));
            RequireAdded(archive, NewArchive("pd-arc-P", "PawnB", DiaryEvent.RecipientRole, 400));
            return archive;
        }

        private static void AssertArchiveLookups(DiaryArchiveRepository archive, string phase)
        {
            Require(archive.Count == 6, "Expected 6 archive rows " + phase + ", not " + archive.Count + ".");
            Require(archive.EntriesForPawn("PawnA").Count == 4,
                "PawnA should resolve to 4 rows " + phase + ".");
            Require(archive.EntriesForPawn("PawnB").Count == 2,
                "PawnB should resolve to 2 rows " + phase + ".");
            Require(archive.EntriesForPawn("PawnMissing").Count == 0,
                "An unknown pawn must resolve to an empty (never null) list " + phase + ".");

            Require(archive.Contains("pd-arc-A1", "PawnA", DiaryEvent.InitiatorRole),
                "Contains should resolve a known row " + phase + ".");
            Require(!archive.Contains("pd-arc-A1", "PawnB", DiaryEvent.InitiatorRole),
                "Contains must not resolve a row for the wrong pawn " + phase + ".");

            // Role lookups: case-insensitive on role, and the paired event resolves to each role's row.
            ArchivedDiaryEntry pInitiator = archive.FindByEventAndRole("pd-arc-P", DiaryEvent.InitiatorRole);
            ArchivedDiaryEntry pRecipient = archive.FindByEventAndRole("pd-arc-P", "RECIPIENT");
            Require(pInitiator != null && string.Equals(pInitiator.pawnId, "PawnA", StringComparison.Ordinal),
                "FindByEventAndRole(P, initiator) should resolve PawnA's row " + phase + ".");
            Require(pRecipient != null && string.Equals(pRecipient.pawnId, "PawnB", StringComparison.Ordinal),
                "FindByEventAndRole(P, recipient) should resolve PawnB's row (case-insensitive) " + phase + ".");
            Require(archive.FindByEventAndRole("pd-arc-missing", DiaryEvent.InitiatorRole) == null,
                "FindByEventAndRole must return null for an unknown event " + phase + ".");
            NoDuplicateArchiveKeys(archive, phase);
        }

        private static void NoDuplicateArchiveKeys(DiaryArchiveRepository archive, string phase)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ArchivedDiaryEntry> all = archive.AllEntries;
            for (int i = 0; i < all.Count; i++)
            {
                ArchivedDiaryEntry entry = all[i];
                Require(entry != null, "A null archive row survived " + phase + ".");
                Require(seen.Add(entry.ArchiveKey),
                    "A duplicate archive key '" + entry.ArchiveKey + "' survived " + phase + ".");
            }
        }

        private static DiaryEvent NewEvent(string eventId, string initiatorPawnId, bool solo, int tick)
        {
            // colorCue is set so PostLoadInit does not derive one via DefDatabase; every other field is
            // a plain saved value, so the round-trip needs no live colony.
            return new DiaryEvent
            {
                eventId = eventId,
                solo = solo,
                tick = tick,
                date = "1st of Aprimay, 5500",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "rimtest_rebuild=1",
                colorCue = DiaryEvent.QuietColorCue,
                initiatorPawnId = initiatorPawnId,
            };
        }

        private static PawnDiaryRecord NewDiary(string pawnId, params string[] eventIds)
        {
            return new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawnId,
                eventIds = eventIds == null
                    ? new List<string>()
                    : new List<string>(eventIds),
            };
        }

        private static ImportantMemoryRecord NewKnowledgeRecord(
            string recordId,
            string eventKind,
            int tick)
        {
            ImportantMemoryRecord record = new ImportantMemoryRecord
            {
                recordId = recordId,
                dedupKey = "PawnA|" + eventKind + "|subject|" + tick,
                sourceEventId = "src-" + recordId,
                eventKind = eventKind,
                topicKey = "relationship",
                tick = tick,
                dateLabel = "1st of Aprimay, 5500",
                fallbackSummary = "fallback for " + recordId,
            };
            record.participantIds.Add("PawnB");
            record.participantNames.Add("Brik");
            record.subjectKeys.Add("part:Leg");
            record.factKeys.Add("victim");
            record.factValues.Add("Brik");
            return record;
        }

        private static ArchivedDiaryEntry NewArchive(string eventId, string pawnId, string povRole, int tick)
        {
            return new ArchivedDiaryEntry
            {
                eventId = eventId,
                pawnId = pawnId,
                povRole = povRole,
                tick = tick,
                date = "1st of Aprimay, 5500",
                text = "raw text for " + eventId,
                generatedText = "generated text for " + eventId,
                status = DiaryEvent.CompleteStatus,
                interactionDefName = "Chat",
                interactionLabel = "chat",
                colorCue = DiaryEvent.QuietColorCue,
            };
        }

        private static List<NarrativeReferenceState> FixtureNarrativeReferences()
        {
            return NarrativeStatePersistence.FromReferences(new List<NarrativeReference>
            {
                new NarrativeReference
                {
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "opened",
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = "Thing_Human_Subject",
                    arcKey = "core|fixture",
                    sourceEventId = "fixture-source",
                    sourceTick = 10,
                }
            });
        }

        private static void AssertNarrativeArchiveLookups(
            DiaryArchiveRepository archive,
            string phase,
            int expectedPawnARows)
        {
            Require(archive.EntriesForNarrativeArc("PawnA", "core|fixture").Count == expectedPawnARows,
                "PawnA's narrative arc count should be " + expectedPawnARows + " " + phase + ".");
            Require(archive.EntriesForNarrativeSubject(
                    "PawnA", NarrativeSubjectKindTokens.Pawn, "Thing_Human_Subject").Count == expectedPawnARows,
                "PawnA's narrative subject count should be " + expectedPawnARows + " " + phase + ".");
            Require(archive.EntriesForNarrativeArc("PawnB", "core|fixture").Count == 1,
                "PawnB's matching arc must remain isolated from PawnA " + phase + ".");
            Require(archive.EntriesForNarrativeSubject(
                    "PawnB", NarrativeSubjectKindTokens.Pawn, "Thing_Human_Subject").Count == 1,
                "PawnB's matching subject must remain isolated from PawnA " + phase + ".");
            Require(archive.EntriesForNarrativeArc("PawnA", "missing|arc").Count == 0
                    && archive.EntriesForNarrativeSubject("PawnA", NarrativeSubjectKindTokens.Pawn, string.Empty).Count == 0,
                "Blank or unknown narrative identities must return empty lists " + phase + ".");
        }

        private static void RequireAdded(DiaryArchiveRepository archive, ArchivedDiaryEntry entry)
        {
            Require(archive.AddOrKeep(entry),
                "AddOrKeep should have accepted a valid archive row for event '" + entry.eventId + "'.");
        }

        // ----- detached component load-maintenance fixture plumbing ---------------------------------

        private static void RequireDetachedMaintenanceReflection()
        {
            if (ComponentEventsField == null
                || RepositoryEventsField == null
                || ComponentDiariesField == null
                || ComponentDiariesByIdField == null
                || ComponentArchiveField == null
                || RebuildDiaryIndexMethod == null
                || RepairReidentifiedEventRefsMethod == null
                || TrimDiariesToPerPawnLimitMethod == null
                || PruneDiaryEventRefsMethod == null)
            {
                throw new AssertionException(
                    "A private field/method required by detached load maintenance was unavailable.");
            }
        }

        private static DiaryGameComponent NewDetachedMaintenanceOwner(
            PawnDiaryRecord ownerDiary,
            DiaryEventRepository repository,
            DiaryArchiveRepository archive)
        {
            DiaryGameComponent component = (DiaryGameComponent)
                FormatterServices.GetUninitializedObject(typeof(DiaryGameComponent));
            ComponentDiariesField.SetValue(
                component,
                new List<PawnDiaryRecord> { ownerDiary });
            ComponentDiariesByIdField.SetValue(
                component,
                new Dictionary<string, PawnDiaryRecord>());
            ComponentEventsField.SetValue(component, repository);
            ComponentArchiveField.SetValue(component, archive);
            return component;
        }

        private static DiaryEventRepository RepositoryWithLoadedRows(params DiaryEvent[] rows)
        {
            if (RepositoryEventsField == null)
            {
                throw new AssertionException(
                    "DiaryEventRepository.diaryEvents was unavailable to the loaded-save fixture.");
            }

            DiaryEventRepository repository = new DiaryEventRepository();
            RepositoryEventsField.SetValue(
                repository,
                rows == null ? new List<DiaryEvent>() : new List<DiaryEvent>(rows));
            return repository;
        }

        // FinalizeLoading normally invokes deep rows' PostLoadInit before component maintenance. Only the
        // detached rows are exposed here; every Scribe Look is a no-op and NormalizeOnLoad mints the ids.
        private static void NormalizeLegacyRows(
            PawnDiaryRecord ownerDiary,
            params DiaryEvent[] legacyEvents)
        {
            LoadSaveMode originalMode = Scribe.mode;
            try
            {
                Scribe.mode = LoadSaveMode.PostLoadInit;
                ownerDiary.ExposeData();
                for (int i = 0; i < legacyEvents.Length; i++)
                {
                    legacyEvents[i]?.ExposeData();
                }
            }
            finally
            {
                Scribe.mode = originalMode;
            }
        }

        // Mirrors the relevant production order without invoking the broader ExposeData branch. The
        // production retention core receives an explicit fixture cap, avoiding settings and UI-version
        // mutation while exercising the exact archive/remove/sweep implementation used after load.
        private static void RunDetachedLoadMaintenance(
            DiaryGameComponent component,
            DiaryEventRepository repository)
        {
            RebuildDiaryIndexMethod.Invoke(component, null);
            IReadOnlyList<DiaryEvent> reidentified = repository.RepairLoadedEvents();
            RepairReidentifiedEventRefsMethod.Invoke(
                component,
                new object[] { reidentified });
            bool trimmed = (bool)TrimDiariesToPerPawnLimitMethod.Invoke(
                component,
                new object[] { DetachedRetentionLimit });
            if (!trimmed)
            {
                throw new AssertionException(
                    "Detached production retention did not enter its mutation path.");
            }
            PruneDiaryEventRefsMethod.Invoke(component, null);
        }

        // ----- Scribe round-trip plumbing ---------------------------------------------------------

        // Saves whatever the expose delegate writes into a fresh root document. FinalizeSaving always
        // runs (to flush/close the file) and Scribe.mode is reset even if the expose delegate throws.
        private static void SaveWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                started = true;
                expose();
            }
            finally
            {
                if (started)
                {
                    Scribe.saver.FinalizeSaving();
                }

                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        // Loads the master list (LoadingVars). FinalizeLoading runs each loaded row's PostLoadInit
        // (real per-row normalization) and resolves cross-references (none here). Scribe.mode is always
        // restored to Inactive so the player's game is never left mid-load.
        private static void LoadVarsWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.loader.InitLoading(path);
                started = true;
                Scribe.mode = LoadSaveMode.LoadingVars;
                expose();
            }
            finally
            {
                if (started)
                {
                    Scribe.loader.FinalizeLoading();
                }

                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        // Reproduces DiaryGameComponent's second ExposeData pass for the archive: ExposeArchive under
        // Scribe.mode == PostLoadInit runs RepairLoadedEntries + RebuildIndex. In PostLoadInit mode
        // Scribe_Collections.Look is a no-op on the list, so this never touches the closed loader.
        private static void RunArchivePostLoadInit(DiaryArchiveRepository archive)
        {
            try
            {
                Scribe.mode = LoadSaveMode.PostLoadInit;
                archive.ExposeArchive(ArchiveLabel);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private static void RunWithTempFile(Action<string> body)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_rimtest_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                body(path);
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // A leftover temp file in the OS temp dir is harmless; never fail a test on cleanup.
            }
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
