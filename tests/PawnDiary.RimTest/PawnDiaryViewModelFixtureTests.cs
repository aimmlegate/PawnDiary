// In-game fixture for Pawn Diary's non-visual Diary-tab view-model contracts
// (design/TEST_COVERAGE_PLAN.md §7.2, "UI / view-model deterministic contracts"). These tests drive the
// read-only surface the pawn Diary tab queries every frame — the year index builder
// (DiaryGameComponent.BeginTabYearIndexBuild -> DiaryTabYearIndex), the hot-vs-archived dedup, the
// visibility filters, the per-pawn command-status badge cache, the POV/entry-role selection
// (DiaryEvent.TryGetDisplayRoleForPawn / ToViewFor), the neutral death-page resolution, and the
// Social-log click routing (GeneratedEntryForPlayLogEntry) — WITHOUT touching immediate-mode
// rendering (no FillTab, no layout slicing, no GUI). The heavy scaffolding (isolated non-generating
// pawns, snapshots, failure-safe teardown, no-leak audit) lives in the shared PawnDiaryRimTestScope
// harness.
//
// Determinism / safety notes for this area:
//   - Every event is CONSTRUCTED through the internal AddSoloEvent / AddPairwiseEvent factory (the same
//     path every Harmony hook funnels through), so no map, no vanilla trigger, and no randomness is
//     needed. The test pawns have per-pawn generation DISABLED (CreateAdultColonist), so no event ever
//     reaches an LLM; generated prose is injected synthetically with DiaryEvent.MarkInjectedTextComplete
//     (no network) purely to satisfy the "has generated text -> visible" view-model gate.
//   - Event dates are overwritten with fixed year strings so the year-index buckets are deterministic
//     regardless of the loaded game's calendar date.
//   - The death page is a saved NEUTRAL death-description event; the view-model resolves it purely from
//     the saved gameContext (death_description / death_victim_id), so it needs no actually-dead pawn —
//     that saved-fact resolution is exactly why the page survives the pawn. The death gameContext is
//     built by the production DeathEventData.BuildFallbackGameContext so the field format is not forged.
//   - Two private seams are reached by reflection and reported: the component's private `archive`
//     repository (to seed a cold archive row that must dedup against its hot event) and the private
//     SetCachedCommandUnreadFlag (the exact single-pawn seam RebuildCommandStatusCache uses to flip the
//     "new page" badge). Both mutate only test-pawn-scoped state that the harness restores and audits.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the Diary tab's deterministic view-model contracts: entries bucket by year, a hot event
    /// and its archived copy dedup to one row (hot wins), the visibility filters hide ungenerated rows
    /// unless debug is on, the per-pawn command-status badge flips writing/new-page as documented,
    /// TryGetDisplayRoleForPawn / ToViewFor return the right POV, a dead pawn's neutral death page
    /// resolves, and a Social-log row routes to its generated entry. These require a loaded game because
    /// the view-model reads live saved diary state; they never enable per-pawn generation.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryViewModelFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // The component's private compact-archive store. The Diary-tab index builder reads it through
        // owner.archive.EntriesForPawn; to prove hot-vs-archived dedup a test must seed a cold row here.
        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);

        // The private single-pawn "new page" badge seam (RebuildCommandStatusCache calls it per diary on
        // load). Reached to flip the unread badge deterministically for one test pawn.
        private static readonly MethodInfo SetUnreadFlagMethod =
            typeof(DiaryGameComponent).GetMethod("SetCachedCommandUnreadFlag", PrivateInstance);

        private const string DeathFallbackDefName = "PawnDiary_DeathFallback";
        private const string AbsentPawnId = "pawndiary_rimtest_viewmodel_absent_pawn";

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh scope and creates two isolated adult colonists with generation disabled. No
        /// interaction groups need enabling — every event here is constructed directly, not captured.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, archive row, or command
        /// key survived — even when a test above threw partway through.
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
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// §7.2. The year index buckets each entry under the year parsed from its date, exposes a
        /// descending year list and per-year counts, resolves an event's year by id, and only ever
        /// returns a year's own entries from AppendEntriesForYear.
        /// </summary>
        [Test]
        public static void EntriesBucketByYearWithCountsAndLookup()
        {
            DiaryEvent older = RecordSolo(firstPawn);
            older.date = "1st of Aprimay, 5501";
            older.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "An older page.");

            DiaryEvent newer = RecordSolo(firstPawn);
            newer.date = "5th of Jugust, 5502";
            newer.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "A newer page.");

            DiaryGameComponent.DiaryTabYearIndex index = BuildIndex(firstPawn, false, false, false);

            PawnDiaryRimTestScope.Require(
                index.years.Count == 2, "Expected exactly two indexed years, got " + index.years.Count + ".");
            // The build sorts years newest-first (SortYearsDescending) so the tab's year selector opens
            // on the most recent year.
            PawnDiaryRimTestScope.Require(
                index.years[0] == 5502 && index.years[1] == 5501,
                "Years were not indexed newest-first (got " + JoinYears(index.years) + ").");
            PawnDiaryRimTestScope.Require(
                index.CountForYear(5501) == 1 && index.CountForYear(5502) == 1,
                "Each year should hold exactly one entry.");
            PawnDiaryRimTestScope.Require(
                index.CountForYear(4999) == 0, "An unknown year must report a zero count.");

            int resolvedYear;
            PawnDiaryRimTestScope.Require(
                index.TryGetYearForEvent(older.eventId, out resolvedYear) && resolvedYear == 5501,
                "TryGetYearForEvent did not resolve the older event to 5501.");
            PawnDiaryRimTestScope.Require(
                index.TryGetYearForEvent(newer.eventId, out resolvedYear) && resolvedYear == 5502,
                "TryGetYearForEvent did not resolve the newer event to 5502.");
            PawnDiaryRimTestScope.Require(
                !index.TryGetYearForEvent("pawndiary_rimtest_no_such_event", out resolvedYear),
                "TryGetYearForEvent must miss for an unknown event id.");

            List<DiaryEntryView> newerRows = EntriesForYear(index, firstPawn, 5502);
            PawnDiaryRimTestScope.Require(
                newerRows.Count == 1 && string.Equals(newerRows[0].EventId, newer.eventId, StringComparison.Ordinal),
                "The 5502 bucket did not return only the newer event's row.");

            List<DiaryEntryView> olderRows = EntriesForYear(index, firstPawn, 5501);
            PawnDiaryRimTestScope.Require(
                olderRows.Count == 1 && string.Equals(olderRows[0].EventId, older.eventId, StringComparison.Ordinal),
                "The 5501 bucket did not return only the older event's row.");
        }

        /// <summary>
        /// §7.2. A hot DiaryEvent and a compact archive row that share the same event/pawn/POV key dedup
        /// to a single displayed row, and the HOT copy wins (Archived == false) so the live page — with
        /// its current generated text — is what the tab shows, never the stale archive snapshot.
        /// </summary>
        [Test]
        public static void HotEventAndArchivedCopyDedupToOneRow()
        {
            DiaryEvent hot = RecordSolo(firstPawn);
            hot.date = "3rd of Aprimay, 5500";
            hot.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "The live hot page.");

            // Seed a cold archive row for the SAME (eventId, pawnId, initiator) key. In the real game
            // this is what retention leaves behind after it drops a hot ref; here we inject it directly so
            // both a hot ref and its archived twin exist at once.
            DiaryArchiveRepository archive = Archive();
            string pawnId = firstPawn.GetUniqueLoadID();
            ArchivedDiaryEntry cold = new ArchivedDiaryEntry
            {
                eventId = hot.eventId,
                pawnId = pawnId,
                povRole = DiaryEvent.InitiatorRole,
                tick = hot.tick,
                date = hot.date,
                text = "raw archived text",
                generatedText = "The stale archived page.",
                status = DiaryEvent.CompleteStatus,
                interactionDefName = "Chat",
                interactionLabel = "chat",
                colorCue = DiaryEvent.QuietColorCue,
            };
            PawnDiaryRimTestScope.Require(
                archive.AddOrKeep(cold), "The archive should have accepted the seeded cold row.");
            scope.RegisterCleanup(
                () => archive.RemoveForEventIds(new HashSet<string>(StringComparer.Ordinal) { hot.eventId }));

            DiaryGameComponent.DiaryTabYearIndex index = BuildIndex(firstPawn, false, false, false);

            PawnDiaryRimTestScope.Require(
                index.years.Count == 1 && index.CountForYear(5500) == 1,
                "The shared hot/archived page should collapse to one row in one year.");

            List<DiaryEntryView> rows = EntriesForYear(index, firstPawn, 5500);
            PawnDiaryRimTestScope.Require(rows.Count == 1, "Expected exactly one deduplicated row, got " + rows.Count + ".");
            PawnDiaryRimTestScope.Require(
                !rows[0].Archived, "The surviving row should be the HOT copy, but it was the archived snapshot.");
            PawnDiaryRimTestScope.Require(
                string.Equals(rows[0].GeneratedText, "The live hot page.", StringComparison.Ordinal),
                "The deduplicated row did not carry the live hot page's generated text.");
        }

        /// <summary>
        /// §7.2. The visibility filter hides an ungenerated, non-generating hot event from the ordinary
        /// tab, but the developer "show LLM debug info" toggle reveals it (as a raw, generated-text-less
        /// row). This is the gate that keeps blank not-yet-written pages off the player's tab.
        /// </summary>
        [Test]
        public static void VisibilityFilterHidesUngeneratedRowUnlessDebug()
        {
            DiaryEvent blank = RecordSolo(firstPawn);
            blank.date = "9th of Aprimay, 5500";
            // No injected text: status stays not_generated and it is not pending, so it is invisible.

            DiaryGameComponent.DiaryTabYearIndex hidden = BuildIndex(firstPawn, false, false, false);
            PawnDiaryRimTestScope.Require(
                hidden.years.Count == 0 && hidden.CountForYear(5500) == 0,
                "An ungenerated, non-generating page must be hidden from the ordinary tab.");

            DiaryGameComponent.DiaryTabYearIndex shown = BuildIndex(firstPawn, true, false, false);
            PawnDiaryRimTestScope.Require(
                shown.years.Count == 1 && shown.CountForYear(5500) == 1,
                "The debug toggle should reveal the otherwise-hidden ungenerated page.");

            List<DiaryEntryView> rows = EntriesForYear(shown, firstPawn, 5500);
            PawnDiaryRimTestScope.Require(
                rows.Count == 1 && string.Equals(rows[0].EventId, blank.eventId, StringComparison.Ordinal),
                "The debug view did not surface exactly the blank event.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(rows[0].GeneratedText),
                "The revealed row should have no generated text (it was never written).");
        }

        /// <summary>
        /// §7.2. The per-pawn command-status badge cache flips as documented: writing (pendingCount > 0)
        /// tracks the acknowledged pending count, acknowledgement reports zero new pages, and the unread
        /// "new page" flag flips true then clears when the pawn's tab is opened.
        /// </summary>
        [Test]
        public static void CommandStatusBadgeFlipsWritingAndNewPage()
        {
            PawnDiaryRimTestScope.Require(
                SetUnreadFlagMethod != null,
                "Pawn Diary test harness could not locate DiaryGameComponent.SetCachedCommandUnreadFlag.");

            // A pawn with no cached status reports a quiet badge.
            DiaryGameComponent.DiaryCommandStatus initial = scope.Component.CommandStatusFor(firstPawn);
            PawnDiaryRimTestScope.Require(
                !initial.IsWriting && !initial.HasNewPages && initial.completedCount == 0,
                "A pawn with no cached diary status should report a quiet command badge.");

            // The tab acknowledges a finished sliced build: two completed pages, one still writing.
            var token = scope.Component.RenderTokenFor(firstPawn);
            scope.Component.AcknowledgeGeneratedEntriesFor(firstPawn, 2, 1, token);

            DiaryGameComponent.DiaryCommandStatus writing = scope.Component.CommandStatusFor(firstPawn);
            PawnDiaryRimTestScope.Require(
                writing.IsWriting && writing.pendingCount == 1 && writing.completedCount == 2,
                "Acknowledging a build with one pending page should report a writing badge.");
            PawnDiaryRimTestScope.Require(
                !writing.HasNewPages, "Acknowledging pages must clear the unread 'new page' flag.");

            // The last pending page finishes: writing clears, the completed count stays cached.
            scope.Component.AcknowledgeGeneratedEntriesFor(firstPawn, 2, 0, token);
            DiaryGameComponent.DiaryCommandStatus settled = scope.Component.CommandStatusFor(firstPawn);
            PawnDiaryRimTestScope.Require(
                !settled.IsWriting && settled.completedCount == 2,
                "With no pending pages the badge should stop reporting writing.");

            // A freshly generated page raises the unread badge for this pawn...
            SetUnreadFlagMethod.Invoke(scope.Component, new object[] { firstPawn.GetUniqueLoadID(), true });
            PawnDiaryRimTestScope.Require(
                scope.Component.CommandStatusFor(firstPawn).HasNewPages,
                "Setting the unread flag should raise the 'new page' badge.");

            // ...and opening that pawn's tab clears it.
            scope.Component.AcknowledgeGeneratedEntriesFor(firstPawn);
            PawnDiaryRimTestScope.Require(
                !scope.Component.CommandStatusFor(firstPawn).HasNewPages,
                "Opening the pawn's tab should clear the 'new page' badge.");
        }

        /// <summary>
        /// §7.2. Entry-role/POV selection: a pairwise event resolves each participant to its own POV
        /// (initiator / recipient), an uninvolved pawn resolves to none, and ToViewFor builds the matching
        /// per-POV view (with the linked other-pawn preview) or null for an uninvolved pawn.
        /// </summary>
        [Test]
        public static void DisplayRoleAndViewResolvePerPawnPov()
        {
            DiaryEvent pair = RecordPair(firstPawn, secondPawn);
            pair.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "First pawn's page.");
            pair.MarkInjectedTextComplete(DiaryEvent.RecipientRole, "Second pawn's page.");

            string firstId = firstPawn.GetUniqueLoadID();
            string secondId = secondPawn.GetUniqueLoadID();

            string role;
            PawnDiaryRimTestScope.Require(
                pair.TryGetDisplayRoleForPawn(firstId, out role) && DiaryEvent.RoleEquals(role, DiaryEvent.InitiatorRole),
                "The initiator pawn should resolve to the initiator POV.");
            PawnDiaryRimTestScope.Require(
                pair.TryGetDisplayRoleForPawn(secondId, out role) && DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole),
                "The recipient pawn should resolve to the recipient POV.");
            PawnDiaryRimTestScope.Require(
                !pair.TryGetDisplayRoleForPawn(AbsentPawnId, out role) && role == null,
                "An uninvolved pawn must resolve to no POV.");

            DiaryEntryView initiatorView = pair.ToViewFor(firstId);
            PawnDiaryRimTestScope.Require(
                initiatorView != null && DiaryEvent.RoleEquals(initiatorView.PovRole, DiaryEvent.InitiatorRole),
                "ToViewFor(initiator) did not build the initiator POV view.");
            PawnDiaryRimTestScope.Require(
                initiatorView.LinkedEntry != null
                    && string.Equals(initiatorView.LinkedEntry.OtherPawnId, secondId, StringComparison.Ordinal),
                "The initiator view's linked preview did not point at the recipient pawn.");

            DiaryEntryView recipientView = pair.ToViewFor(secondId);
            PawnDiaryRimTestScope.Require(
                recipientView != null && DiaryEvent.RoleEquals(recipientView.PovRole, DiaryEvent.RecipientRole),
                "ToViewFor(recipient) did not build the recipient POV view.");
            PawnDiaryRimTestScope.Require(
                pair.ToViewFor(AbsentPawnId) == null,
                "ToViewFor must return null for an uninvolved pawn.");
        }

        /// <summary>
        /// §7.2. A dead pawn's death page resolves through the view-model from its saved neutral
        /// death-description context: the victim resolves to the neutral POV, ToViewFor builds a memorial
        /// page ranked as the hard-final entry, and a non-victim pawn resolves to nothing.
        /// </summary>
        [Test]
        public static void DeathPageResolvesAsNeutralMemorial()
        {
            string victimId = firstPawn.GetUniqueLoadID();
            string deathContext = PawnDiary.Capture.DeathEventData.BuildFallbackGameContext(
                DeathFallbackDefName,
                "Death",
                firstPawn.LabelShortCap,
                victimId,
                DiaryEvent.InitiatorRole,
                null);

            DiaryEvent death = scope.Component.AddSoloEvent(
                firstPawn, null, DeathFallbackDefName, "Death", firstPawn.LabelShortCap + " died.", string.Empty, deathContext);
            death.MarkInjectedTextComplete(DiaryEvent.NeutralRole, "The colony gathered to remember them.");

            PawnDiaryRimTestScope.Require(
                death.HasDeathDescription(), "The constructed event should carry a death description.");
            PawnDiaryRimTestScope.Require(
                death.IsDeathDescriptionFor(victimId) && !death.IsDeathDescriptionFor(secondPawn.GetUniqueLoadID()),
                "The death description should identify only the victim.");

            string role;
            PawnDiaryRimTestScope.Require(
                death.TryGetDisplayRoleForPawn(victimId, out role) && DiaryEvent.RoleEquals(role, DiaryEvent.NeutralRole),
                "The victim's death page should resolve to the neutral POV.");
            PawnDiaryRimTestScope.Require(
                !death.TryGetDisplayRoleForPawn(secondPawn.GetUniqueLoadID(), out role) && role == null,
                "A non-victim must not resolve to the death page.");

            DiaryEntryView view = death.ToViewFor(victimId);
            PawnDiaryRimTestScope.Require(
                view != null && DiaryEvent.RoleEquals(view.PovRole, DiaryEvent.NeutralRole),
                "The death page view should be a neutral entry.");
            PawnDiaryRimTestScope.Require(
                view.BoundaryRank == 1, "A death page must be ranked as the hard-final (+1) boundary entry.");
            PawnDiaryRimTestScope.Require(
                string.Equals(view.AtmosphereCue, DiaryEntryView.AtmosphereMemorial, StringComparison.Ordinal),
                "The death page should carry the memorial atmosphere cue.");
            PawnDiaryRimTestScope.Require(
                string.Equals(view.GeneratedText, "The colony gathered to remember them.", StringComparison.Ordinal),
                "The death page did not surface its neutral generated text.");
            PawnDiaryRimTestScope.Require(
                death.ToViewFor(secondPawn.GetUniqueLoadID()) == null,
                "The death page must not build a view for a non-victim pawn.");
        }

        /// <summary>
        /// §7.2. Social-log click routing: a clicked RimWorld social-log row maps back to the generated
        /// diary entry it was folded into, and an unknown row id routes to nothing (so vanilla behavior is
        /// preserved).
        /// </summary>
        [Test]
        public static void SocialLogClickRoutesToGeneratedEntry()
        {
            const int playLogEntryId = 918273;

            DiaryEvent talk = RecordSolo(firstPawn);
            talk.date = "2nd of Aprimay, 5500";
            talk.AddPlayLogEntryId(playLogEntryId);
            talk.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "We spoke by the stove.");

            DiaryEntryView routed = scope.Component.GeneratedEntryForPlayLogEntry(firstPawn, playLogEntryId);
            PawnDiaryRimTestScope.Require(
                routed != null && string.Equals(routed.EventId, talk.eventId, StringComparison.Ordinal),
                "A clicked social-log row did not route to its generated diary entry.");

            PawnDiaryRimTestScope.Require(
                scope.Component.GeneratedEntryForPlayLogEntry(firstPawn, 112233) == null,
                "An unknown social-log row must not route to any diary entry.");
        }

        // ----- helpers ----------------------------------------------------------------------------

        /// <summary>Constructs a solo diary event for a pawn through the internal event factory.</summary>
        private static DiaryEvent RecordSolo(Pawn pawn)
        {
            return scope.Component.AddSoloEvent(
                pawn, null, "Chat", "chat", "A quiet moment passed.", string.Empty, "rimtest_viewmodel=1");
        }

        /// <summary>Constructs a pairwise diary event through the internal event factory.</summary>
        private static DiaryEvent RecordPair(Pawn initiator, Pawn recipient)
        {
            return scope.Component.AddPairwiseEvent(
                initiator, recipient, "DeepTalk", "deep talk",
                "The initiator spoke.", "The recipient answered.", string.Empty, "rimtest_viewmodel=1");
        }

        /// <summary>Drives the frame-sliced year-index build to completion and returns the index.</summary>
        private static DiaryGameComponent.DiaryTabYearIndex BuildIndex(
            Pawn pawn, bool showLlmDebugInfo, bool showGeneratingEntries, bool showPromptOnlyEntries)
        {
            DiaryGameComponent.DiaryTabYearIndexBuild build = scope.Component.BeginTabYearIndexBuild(
                pawn, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries);
            // A generous single slice completes any small test history in one pass; the bounded guard
            // loop is a deterministic backstop, never a probabilistic retry.
            for (int guard = 0; guard < 10000 && !build.IsComplete; guard++)
            {
                build.ProcessSlice(int.MaxValue, 3600f);
            }

            PawnDiaryRimTestScope.Require(build.IsComplete, "The Diary-tab year-index build did not complete.");
            return build.index;
        }

        private static List<DiaryEntryView> EntriesForYear(
            DiaryGameComponent.DiaryTabYearIndex index, Pawn pawn, int year)
        {
            List<DiaryEntryView> rows = new List<DiaryEntryView>();
            index.AppendEntriesForYear(rows, pawn.GetUniqueLoadID(), year);
            return rows;
        }

        private static DiaryArchiveRepository Archive()
        {
            PawnDiaryRimTestScope.Require(
                ArchiveField != null, "Pawn Diary test harness could not locate the private 'archive' field.");
            DiaryArchiveRepository archive = ArchiveField.GetValue(scope.Component) as DiaryArchiveRepository;
            PawnDiaryRimTestScope.Require(archive != null, "The component's archive repository was null.");
            return archive;
        }

        private static string JoinYears(List<int> years)
        {
            string[] parts = new string[years.Count];
            for (int i = 0; i < years.Count; i++)
            {
                parts[i] = years[i].ToString();
            }

            return string.Join(",", parts);
        }
    }
}
