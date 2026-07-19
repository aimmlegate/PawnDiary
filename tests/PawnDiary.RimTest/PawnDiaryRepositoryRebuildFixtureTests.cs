// In-game save/load fixture for Pawn Diary's repository index rebuilds and retention
// (TEST_COVERAGE_PLAN.md §6.4, "repository/diary/archive index rebuilds ... retention"). This suite
// needs NO colony and creates NO pawns: it builds DiaryEventRepository / DiaryArchiveRepository /
// PawnMemoryRepository model objects directly, round-trips their SAVED lists through RimWorld's real
// Scribe to a temp file, and proves transient indexes rebuild correctly, memory registration remains
// idempotent across the lazy post-load path, loaded rows repair, and retention drops the right rows.
//
// Why a real Scribe round-trip and not a whole-game save: the two repositories serialize only their
// master list (events.ExposeEvents "diaryEvents" / archive.ExposeArchive "diaryArchiveEntries"); the
// lookup indexes are rebuilt after load. DiaryGameComponent.ExposeData drives this in the live game —
// ExposeEvents/ExposeArchive run in both Scribe passes, then the component's own PostLoadInit calls
// events.RebuildIndex(), while the archive rebuilds itself inside ExposeArchive's PostLoadInit branch
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
// component does. DiaryEvent/ArchivedDiaryEntry persist purely by string/value (no LookMode.Reference
// to live Pawns), so this object-level round-trip is valid without a loaded colony.
using System;
using System.Collections.Generic;
using System.IO;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the never-serialized indexes of <see cref="DiaryEventRepository"/>,
    /// <see cref="DiaryArchiveRepository"/>, and <see cref="PawnMemoryRepository"/> rebuild after a
    /// real Scribe load, that memory replay stays idempotent, that retention prunes and re-indexes
    /// correctly, and that a reload drops duplicate archive rows.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRepositoryRebuildFixtureTests
    {
        private const string EventsLabel = "diaryEvents";
        private const string ArchiveLabel = "diaryArchiveEntries";
        private const string MemoryLabel = "pawnMemoryFragments";

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
        /// The inert memory repository persists only its master list. A real Scribe load must run
        /// MemoryFragment.PostLoadInit repair, while the first ForPawn read lazily reconstructs the
        /// unsaved pawn/deposit indexes. RemoveByIds must then keep those indexes synchronized.
        /// </summary>
        [Test]
        public static void MemoryRepositoryRoundTripsRepairsAndReindexes()
        {
            PawnMemoryRepository source = new PawnMemoryRepository();
            MemoryFragment repairMe = NewMemory("pd-memory-1", "PawnA", "pd-source-1", 200);
            repairMe.tags = null;
            repairMe.keywords = null;
            repairMe.importance = 2f;
            repairMe.lastRecalledTick = 100;
            source.Register(repairMe);
            source.Register(NewMemory("pd-memory-2", "PawnB", "pd-source-2", 300));

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => source.ExposeMemories(MemoryLabel));

                PawnMemoryRepository loaded = new PawnMemoryRepository();
                LoadVarsWithScribe(path, () => loaded.ExposeMemories(MemoryLabel));

                Require(loaded.Count == 2,
                    "Loaded memory count " + loaded.Count + " did not match the saved 2.");
                IReadOnlyList<MemoryFragment> pawnA = loaded.ForPawn("PawnA");
                IReadOnlyList<MemoryFragment> pawnB = loaded.ForPawn("PawnB");
                Require(pawnA.Count == 1 && pawnB.Count == 1,
                    "The lazy memory index rebuild must restore one row for each owner.");
                Require(pawnA[0].tags != null && pawnA[0].tags.Count == 0
                        && pawnA[0].keywords != null && pawnA[0].keywords.Count == 0,
                    "MemoryFragment PostLoadInit must repair null tag/keyword lists.");
                Require(Math.Abs(pawnA[0].importance - 1f) < 0.0001f,
                    "MemoryFragment PostLoadInit must clamp importance to 1.");
                Require(pawnA[0].lastRecalledTick == pawnA[0].createdTick,
                    "MemoryFragment PostLoadInit must repair a recall tick older than creation.");
                Require(loaded.HasDeposit("PawnA", "pd-source-1")
                        && loaded.HasDeposit("PawnB", "pd-source-2"),
                    "The rebuilt deposit-key index must contain both saved deposits.");

                int removed = loaded.RemoveByIds(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "pd-memory-1",
                });
                Require(removed == 1 && loaded.Count == 1 && loaded.ForPawn("PawnA").Count == 0,
                    "RemoveByIds must remove the row and rebuild the owner index.");
                Require(!loaded.HasDeposit("PawnA", "pd-source-1")
                        && loaded.HasDeposit("PawnB", "pd-source-2"),
                    "RemoveByIds must rebuild deposit keys without disturbing surviving owners.");
            });
        }

        /// <summary>
        /// Register owns the final pawn+source-event idempotency guarantee even if future wiring calls
        /// it before DiaryGameComponent's normal PostLoadInit RebuildIndex. This pins the defensive
        /// lazy-order contract that protects staged/replayed signals.
        /// </summary>
        [Test]
        public static void MemoryRegisterIsIdempotentBeforeExplicitPostLoadRebuild()
        {
            PawnMemoryRepository source = new PawnMemoryRepository();
            source.Register(NewMemory("pd-memory-original", "PawnA", "pd-source-same", 100));

            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () => source.ExposeMemories(MemoryLabel));

                PawnMemoryRepository loaded = new PawnMemoryRepository();
                LoadVarsWithScribe(path, () => loaded.ExposeMemories(MemoryLabel));

                // Do NOT call RebuildIndex/ForPawn/HasDeposit first: Register itself must initialize
                // the lazy indexes before it checks whether this deposit already exists.
                loaded.Register(NewMemory("pd-memory-duplicate", "PawnA", "pd-source-same", 200));

                IReadOnlyList<MemoryFragment> owned = loaded.ForPawn("PawnA");
                Require(loaded.Count == 1 && owned.Count == 1,
                    "A duplicate first registration after load must not append a second fragment.");
                Require(string.Equals(owned[0].memoryId, "pd-memory-original", StringComparison.Ordinal),
                    "The original loaded deposit must win over its replay.");
                Require(loaded.HasDeposit("PawnA", "pd-source-same"),
                    "The lazy rebuild must retain the original deposit key.");
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

        private static MemoryFragment NewMemory(
            string memoryId,
            string pawnId,
            string sourceEventId,
            int createdTick)
        {
            return new MemoryFragment
            {
                memoryId = memoryId,
                pawnId = pawnId,
                sourceEventId = sourceEventId,
                text = "memory text for " + memoryId,
                tags = new List<string> { MemoryTagTokens.Social },
                keywords = new List<string> { "fixture" },
                importance = 0.5f,
                createdTick = createdTick,
                lastRecalledTick = createdTick,
            };
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
