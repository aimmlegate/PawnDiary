// Loaded-game tests for the remaining safe, non-network PUBLIC integration facade.
//
// PawnDiaryApi has several useful adapter-facing routes beyond the ordinary SubmitEvent boolean:
// stable entry handles, direct/prompt entry submission, compact memory snapshots, structured pawn
// context, voice reads/writes, event-filter settings, prompt-enchantment candidates, and an API-lane
// setup snapshot. Pure tests cover the DTO builders, but only a loaded game can prove these public
// methods reach real saved diary state and obey RimWorld's main-thread/readiness gates.
//
// Safety / isolation:
//   - CreateAdultColonist leaves per-pawn generation DISABLED. SubmitEventWithHandle and
//     SubmitPromptEntry can therefore persist their factual event, but QueueLlmRewrite exits before
//     prompt construction or transport. Direct entries already bypass the main prose LLM path, and
//     every request below supplies a title with generateTitleIfMissing=false.
//   - forceRecord is used only to bypass the test pawn's generation toggle plus budget/dedup soft
//     gates. It never bypasses base pawn eligibility or the integration master switch.
//   - Voice fields, event-filter settings, prompt-enchantment settings/Defs, and the temporary hediff
//     are all snapshotted and restored through PawnDiaryRimTestScope cleanups.
//   - This suite never registers a process-global provider/generator, never changes LlmClient, and
//     never launches RimWorld. The user runs it through RimTest Redux in an already-loaded save.
//
// New to C#/RimWorld? See AGENTS.md and tests/PawnDiary.RimTest/README.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Ingestion;
using PawnDiary.Integration;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the public API facade's loaded-game persistence, read, settings, voice, context, and
    /// fail-closed contracts without ever allowing a real LLM request.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPublicApiGapFlowTests
    {
        private const string DevTestEventKey = "pawndiary_dev_test";
        private const string PromptEventKey = "pawndiary_rimtest_prompt_entry";
        private const string DirectEventKey = "pawndiary_rimtest_direct_entry";
        private const string TestSourceId = "pawndiary.rimtest.publicapigaps";
        private const string PromptInstruction =
            "Describe the quiet decision to keep going after a difficult morning.";
        private const string DirectText =
            "I kept the small lamp burning. It did not solve the day, but it made the room feel possible.";
        private const string DirectTitle = "The Small Lamp";
        private const string StyleOverrideRule =
            "Use compact sentences and end on one concrete image.";
        private const string CustomPsychotypeRule =
            "Looks for practical signs that tomorrow can still improve.";
        private const string TargetPsychotypeDefName = "DiaryPsychotype_Content";
        private const string EnchantmentDefName = "DiaryEnchant_AmbrosiaHigh";
        private const string EnchantmentHediffDefName = "AmbrosiaHigh";

        private static PawnDiaryRimTestScope scope;
        private static Pawn testPawn;
        private static Pawn ineligiblePawn;
        private static PawnDiarySettings settings;

        /// <summary>
        /// Creates one base-eligible, generation-disabled colonist and one factionless ineligible
        /// humanlike pawn, then enables the public integration master switch for the test.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException(
                    "Pawn Diary settings must be loaded before the public API gap suite runs.");
            }

            bool originalAllowExternalIntegrations = settings.allowExternalIntegrations;
            settings.allowExternalIntegrations = true;
            scope.RegisterCleanup(
                () => settings.allowExternalIntegrations = originalAllowExternalIntegrations);

            testPawn = scope.CreateAdultColonist();
            ineligiblePawn = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, null);

            PawnDiaryRimTestScope.Require(
                DiaryGameComponent.IsDiaryEligible(testPawn),
                "The test colonist must satisfy base diary-owner eligibility.");
            PawnDiaryRimTestScope.Require(
                !DiaryGameComponent.IsDiaryEligible(ineligiblePawn),
                "The factionless test pawn must fail base diary-owner eligibility.");
            PawnDiaryRimTestScope.Require(
                !PawnDiaryApi.IsDiaryGenerationEnabled(testPawn),
                "The public API fixture pawn must keep diary generation disabled so no LLM call can start.");
        }

        /// <summary>
        /// Restores every per-test mutation and proves the harness removed all test-owned diary/pawn
        /// state, even when the test body failed partway through.
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
                testPawn = null;
                ineligiblePawn = null;
                settings = null;
            }
        }

        /// <summary>
        /// A valid SubmitEventWithHandle call persists a real event and its opaque handle resolves
        /// through both handle-based and event-id/role status and snapshot overloads.
        /// </summary>
        [Test]
        public static void SubmitEventWithHandlePersistsAndBothReadOverloadsResolveIt()
        {
            DiaryEventSubmissionResult result = null;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => result = PawnDiaryApi.SubmitEventWithHandle(BuildEventRequest(testPawn)),
                DevTestEventKey,
                testPawn,
                null);

            RequireRecordedSoloResult(result, diaryEvent, DevTestEventKey);

            DiaryEntryStatusSnapshot statusByHandle = PawnDiaryApi.GetEntryStatus(result.primary);
            DiaryEntryStatusSnapshot statusById = PawnDiaryApi.GetEntryStatus(
                result.primary.eventId,
                result.primary.povRole);
            RequireStatusFor(statusByHandle, result.primary, "handle status read");
            RequireStatusFor(statusById, result.primary, "id/role status read");
            PawnDiaryRimTestScope.Require(
                string.Equals(statusByHandle.status, statusById.status, StringComparison.Ordinal),
                "Handle and id/role status reads disagreed on the normalized entry status.");

            DiaryEntrySnapshot entryByHandle = PawnDiaryApi.GetEntrySnapshot(result.primary);
            DiaryEntrySnapshot entryById = PawnDiaryApi.GetEntrySnapshot(
                result.primary.eventId,
                result.primary.povRole);
            RequireEntryFor(entryByHandle, result.primary, "handle entry read");
            RequireEntryFor(entryById, result.primary, "id/role entry read");
            PawnDiaryRimTestScope.Require(
                entryByHandle.externallyAuthored
                    && string.Equals(entryByHandle.externalSourceId, TestSourceId, StringComparison.Ordinal),
                "The persisted external entry snapshot lost its source attribution.");
        }

        /// <summary>
        /// SubmitPromptEntry accepts an unclaimed adapter event key, persists the protected instruction
        /// in saved context, and leaves prose absent because the test pawn's generation flag is off.
        /// </summary>
        [Test]
        public static void SubmitPromptEntryPersistsProtectedInstructionWithoutTransport()
        {
            DiaryEventSubmissionResult result = null;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => result = PawnDiaryApi.SubmitPromptEntry(BuildPromptRequest(testPawn)),
                PromptEventKey,
                testPawn,
                null);

            RequireRecordedSoloResult(result, diaryEvent, PromptEventKey);
            string savedInstruction = DiaryContextFields.Value(
                diaryEvent.gameContext,
                "external_prompt_instruction");
            PawnDiaryRimTestScope.Require(
                string.Equals(savedInstruction, PromptInstruction, StringComparison.Ordinal),
                "SubmitPromptEntry did not persist the sanitized instruction in protected event context.");

            DiaryEntrySnapshot snapshot = PawnDiaryApi.GetEntrySnapshot(result.primary);
            RequireEntryFor(snapshot, result.primary, "prompt-entry snapshot");
            PawnDiaryRimTestScope.Require(
                !snapshot.hasGeneratedText && string.IsNullOrEmpty(snapshot.generatedText),
                "A generation-disabled prompt entry unexpectedly acquired generated prose.");
        }

        /// <summary>
        /// SubmitDirectEntry saves caller-authored final prose and title synchronously, reports a stable
        /// handle, and exposes a complete entry without running either prose or title transport.
        /// </summary>
        [Test]
        public static void SubmitDirectEntryPersistsCompletedProseAndTitleWithoutTransport()
        {
            DiaryEventSubmissionResult result;
            DiaryEvent diaryEvent = SubmitDirectAndRequire(out result);

            DiaryEntryStatusSnapshot status = PawnDiaryApi.GetEntryStatus(result.primary);
            RequireStatusFor(status, result.primary, "direct-entry status");
            PawnDiaryRimTestScope.Require(
                status.complete && status.hasGeneratedText && status.titleComplete,
                "The direct entry was not exposed as completed prose with a completed title.");
            PawnDiaryRimTestScope.Require(
                string.Equals(status.title, DirectTitle, StringComparison.Ordinal),
                "The direct entry status did not retain its caller-authored title.");

            DiaryEntrySnapshot snapshot = PawnDiaryApi.GetEntrySnapshot(
                diaryEvent.eventId,
                result.primary.povRole);
            RequireEntryFor(snapshot, result.primary, "direct-entry id/role snapshot");
            PawnDiaryRimTestScope.Require(
                string.Equals(snapshot.generatedText, DirectText, StringComparison.Ordinal),
                "The direct entry snapshot did not retain the caller-authored prose.");
            PawnDiaryRimTestScope.Require(
                string.Equals(snapshot.title, DirectTitle, StringComparison.Ordinal),
                "The direct entry snapshot did not retain the caller-authored title.");
        }

        /// <summary>
        /// The typed dispatcher can know that persistence began after a fault, but the existing public
        /// submission APIs must retain their pre-v9 completed-emission contract. Both handle factories
        /// therefore report failure and never fabricate an event id for that exceptional path.
        /// </summary>
        [Test]
        public static void PostCommitSubmissionResultPreservesLegacyFailureContract()
        {
            bool pageRegistered = DiaryDispatchOutcomePolicy.PageRegistered(
                DiaryDispatchOutcome.ExceptionAfterCommit);
            bool emitted = DiaryDispatchOutcomePolicy.EmissionRan(
                DiaryDispatchOutcome.ExceptionAfterCommit);
            PawnDiaryRimTestScope.Require(
                pageRegistered && !emitted,
                "ExceptionAfterCommit must remain durable internally but false under the legacy emission predicate.");

            int checkedFactories = 0;
            MethodInfo[] methods = typeof(PawnDiaryApi).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (!string.Equals(method.Name, "SubmissionResultFor", StringComparison.Ordinal)
                    || parameters.Length != 4
                    || parameters[0].ParameterType != typeof(string)
                    || parameters[1].ParameterType != typeof(string)
                    || parameters[2].ParameterType != typeof(bool))
                {
                    continue;
                }

                const string eventKey = "pawndiary_rimtest_post_commit";
                DiaryEventSubmissionResult result = method.Invoke(
                    null,
                    new object[] { TestSourceId, eventKey, emitted, null })
                    as DiaryEventSubmissionResult;
                PawnDiaryRimTestScope.Require(
                    result != null
                        && !result.recorded
                        && !result.pairwise
                        && result.primary == null
                        && result.partner == null
                        && string.Equals(result.sourceId, TestSourceId, StringComparison.Ordinal)
                        && string.Equals(result.eventKey, eventKey, StringComparison.Ordinal),
                    "A post-commit fault changed the legacy failure result or fabricated a handle.");
                checkedFactories++;
            }

            PawnDiaryRimTestScope.Require(
                checkedFactories == 2,
                "The legacy post-commit contract must cover generated/prompt and direct-entry result factories.");
        }

        /// <summary>
        /// A completed direct entry appears through unfiltered/filtered recent-title reads, aggregate
        /// stats, compact prose context, and the composed context bundle; a wrong source filter matches none.
        /// </summary>
        [Test]
        public static void RecentTitlesFiltersStatsContextAndBundleReflectDirectEntry()
        {
            DiaryEventSubmissionResult result;
            SubmitDirectAndRequire(out result);

            List<DiaryEntryTitleSnapshot> titles = PawnDiaryApi.GetRecentEntryTitles(testPawn, 10);
            DiaryEntryTitleSnapshot title = FindTitle(titles, result.primary.eventId);
            PawnDiaryRimTestScope.Require(
                title != null && string.Equals(title.title, DirectTitle, StringComparison.Ordinal),
                "The unfiltered recent-title read did not expose the completed direct entry.");

            DiaryEntryTitleQuery matching = DirectEntryQuery(TestSourceId);
            List<DiaryEntryTitleSnapshot> filtered =
                PawnDiaryApi.GetRecentEntryTitles(testPawn, 10, matching);
            PawnDiaryRimTestScope.Require(
                filtered.Count == 1
                    && string.Equals(filtered[0].eventId, result.primary.eventId, StringComparison.Ordinal),
                "The matching recent-title query should return exactly the direct entry.");

            DiaryEntryTitleQuery missing = DirectEntryQuery("pawndiary.rimtest.no-such-source");
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetRecentEntryTitles(testPawn, 10, missing).Count == 0,
                "A recent-title query for a different source should return no rows.");

            DiaryEntryStatsSnapshot stats = PawnDiaryApi.GetEntryStats(testPawn, matching);
            PawnDiaryRimTestScope.Require(
                stats != null
                    && stats.total == 1
                    && stats.complete == 1
                    && stats.withTitle == 1
                    && stats.withGeneratedText == 1,
                "Filtered entry stats did not count the one completed titled direct entry.");
            DiaryEntryStatsSnapshot emptyStats = PawnDiaryApi.GetEntryStats(testPawn, missing);
            PawnDiaryRimTestScope.Require(
                emptyStats != null && emptyStats.total == 0,
                "Filtered entry stats should be empty for a different source.");

            DiaryContextSnapshot context = PawnDiaryApi.GetContextSnapshot(testPawn, 10, matching);
            RequireContextContains(context, result.primary.eventId, "standalone context snapshot");

            DiaryContextBundleSnapshot bundle =
                PawnDiaryApi.GetContextBundle(testPawn, 10, matching, false);
            PawnDiaryRimTestScope.Require(
                bundle != null
                    && bundle.writingStyle != null
                    && bundle.pawnSummary != null
                    && bundle.promptEnchantments != null
                    && bundle.recentContext != null,
                "The context bundle did not populate all four public snapshot sections.");
            RequireContextContains(
                bundle.recentContext,
                result.primary.eventId,
                "context bundle recent-context section");
        }

        /// <summary>
        /// Pawn summary, base writing style, style catalog, and effective psychotype getters return
        /// structured snapshots for a base-eligible generation-disabled colonist without creating work.
        /// </summary>
        [Test]
        public static void PawnSummaryAndVoiceGettersReturnStructuredSnapshots()
        {
            DiaryPawnSummarySnapshot summary = PawnDiaryApi.GetPawnSummary(testPawn);
            PawnDiaryRimTestScope.Require(
                summary != null
                    && !string.IsNullOrWhiteSpace(summary.sex)
                    && summary.health != null
                    && summary.lowCapacities != null
                    && summary.topThoughts != null
                    && summary.providerLines != null,
                "GetPawnSummary did not return its required structured fields.");

            DiaryWritingStyleSnapshot writingStyle = PawnDiaryApi.GetWritingStyle(testPawn);
            PawnDiaryRimTestScope.Require(
                writingStyle != null && !string.IsNullOrWhiteSpace(writingStyle.styleDefName),
                "GetWritingStyle did not return the test pawn's saved base style.");

            List<DiaryWritingStyleSnapshot> catalog = PawnDiaryApi.GetAvailableWritingStyles();
            PawnDiaryRimTestScope.Require(
                catalog != null && catalog.Count > 0,
                "GetAvailableWritingStyles returned no loaded writing-style rows.");
            PawnDiaryRimTestScope.Require(
                ContainsStyle(catalog, writingStyle.styleDefName),
                "The test pawn's base writing style was absent from the public style catalog.");

            DiaryPsychotypeSnapshot psychotype = PawnDiaryApi.GetPsychotype(testPawn);
            PawnDiaryRimTestScope.Require(
                psychotype != null
                    && psychotype.psychotypeDefName != null
                    && psychotype.label != null
                    && psychotype.rule != null
                    && psychotype.savedCustomRule != null,
                "GetPsychotype did not return a well-formed snapshot.");
        }

        /// <summary>
        /// Source-owned writing-style override, base psychotype selection, and player-owned custom
        /// psychotype rule persist through the public setters; the exact original voice record is restored.
        /// </summary>
        [Test]
        public static void VoiceSettersPersistAndRestoreExactOriginalRecord()
        {
            PawnDiaryRecord record = scope.RequireDiaryRecord(testPawn);
            VoiceRecordState original = VoiceRecordState.Capture(record);
            scope.RegisterCleanup(() => original.Restore(record));

            DiaryWritingStyleSnapshot baseStyle = PawnDiaryApi.GetWritingStyle(testPawn);
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetWritingStyleOverride(testPawn, TestSourceId, StyleOverrideRule),
                "SetWritingStyleOverride rejected a valid source-owned rule.");
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    record.externalWritingStyleOverrideSourceId,
                    TestSourceId,
                    StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        record.externalWritingStyleOverrideRule,
                        StyleOverrideRule,
                        StringComparison.Ordinal),
                "The writing-style override setter did not persist both owner and rule.");

            // GetWritingStyle intentionally reports the base style, not temporary/source-owned overrides.
            DiaryWritingStyleSnapshot stillBase = PawnDiaryApi.GetWritingStyle(testPawn);
            PawnDiaryRimTestScope.Require(
                baseStyle != null
                    && stillBase != null
                    && string.Equals(
                        baseStyle.styleDefName,
                        stillBase.styleDefName,
                        StringComparison.Ordinal),
                "A source-owned override unexpectedly changed the public base-style snapshot.");
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.ResetWritingStyleOverride(testPawn, TestSourceId),
                "ResetWritingStyleOverride failed for the source that owned the test override.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetPsychotype(testPawn, TargetPsychotypeDefName, true),
                "SetPsychotype rejected a loaded built-in psychotype.");
            DiaryPsychotypeSnapshot selected = PawnDiaryApi.GetPsychotype(testPawn);
            PawnDiaryRimTestScope.Require(
                selected != null
                    && string.Equals(
                        selected.psychotypeDefName,
                        TargetPsychotypeDefName,
                        StringComparison.Ordinal)
                    && record.psychotypePinned,
                "The public psychotype setter did not save and pin the selected built-in type.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetPsychotypeCustomRule(testPawn, CustomPsychotypeRule),
                "SetPsychotypeCustomRule rejected a valid player-owned custom rule.");
            DiaryPsychotypeSnapshot customized = PawnDiaryApi.GetPsychotype(testPawn);
            PawnDiaryRimTestScope.Require(
                customized != null
                    && string.Equals(
                        customized.savedCustomRule,
                        CustomPsychotypeRule,
                        StringComparison.Ordinal),
                "The psychotype getter did not expose the saved custom layer.");

            original.Restore(record);
            original.RequireMatches(record, "inline voice restoration");
        }

        /// <summary>
        /// Event-filter snapshots agree with IsEventFilterEnabled, a valid key can be toggled through the
        /// public setter, unknown keys fail closed, and the original effective/override state is restored.
        /// </summary>
        [Test]
        public static void EventFilterGetIsSetRoundTripsAndRestoresOriginalState()
        {
            List<DiaryEventFilterSnapshot> filters = PawnDiaryApi.GetEventFilters();
            PawnDiaryRimTestScope.Require(
                filters != null && filters.Count > 0,
                "GetEventFilters returned no settings-visible interaction groups.");

            DiaryEventFilterSnapshot original = SelectOrdinaryFilter(filters);
            string key = original.key;
            bool originalEnabled = original.enabled;
            bool originalHasOverride = original.hasOverride;
            scope.RegisterCleanup(() =>
            {
                // This cleanup runs before SetUp restores the master switch, so the public write remains
                // available even if the test failed while checking the gated branch elsewhere.
                settings.allowExternalIntegrations = true;
                PawnDiaryApi.SetEventFilterEnabled(key, originalEnabled);
            });

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.IsEventFilterEnabled(key) == originalEnabled,
                "IsEventFilterEnabled disagreed with the corresponding GetEventFilters row.");
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetEventFilterEnabled(key, !originalEnabled),
                "SetEventFilterEnabled rejected a valid settings-visible filter key.");
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.IsEventFilterEnabled(key) == !originalEnabled,
                "The event-filter toggle did not take effect immediately.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetEventFilterEnabled(key, originalEnabled),
                "The public event-filter setter could not restore the original effective value.");
            DiaryEventFilterSnapshot restored = FindFilter(PawnDiaryApi.GetEventFilters(), key);
            PawnDiaryRimTestScope.Require(
                restored != null
                    && restored.enabled == originalEnabled
                    && restored.hasOverride == originalHasOverride,
                "Restoring the event filter did not recover its original effective/override state.");

            PawnDiaryRimTestScope.Require(
                !PawnDiaryApi.IsEventFilterEnabled("pawndiary_rimtest_unknown_filter")
                    && !PawnDiaryApi.SetEventFilterEnabled(
                        "pawndiary_rimtest_unknown_filter",
                        true),
                "Unknown event-filter keys should fail closed.");
        }

        /// <summary>
        /// API v9 exposes the selected frequency preset once, enriches the existing filter rows without
        /// changing enable semantics, and round-trips sparse frequency writes independently. Invalid
        /// future tokens and corrupt numbers fail without mutating the player's settings.
        /// </summary>
        [Test]
        public static void EventFrequencySnapshotSetResetAndPresetSelectionAreAdditive()
        {
            int originalFrequencySchemaVersion = settings.frequencySettingsSchemaVersion;
            string originalPreset = settings.frequencyPresetDefName;
            bool originalMigrationNoticePending = settings.frequencyMigrationNoticePending;
            Dictionary<string, float> originalFrequencyOverrides = settings.groupFrequencyOverrides == null
                ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, float>(
                    settings.groupFrequencyOverrides,
                    StringComparer.OrdinalIgnoreCase);
            scope.RegisterCleanup(() =>
            {
                settings.frequencySettingsSchemaVersion = originalFrequencySchemaVersion;
                settings.frequencyPresetDefName = originalPreset;
                settings.frequencyMigrationNoticePending = originalMigrationNoticePending;
                if (settings.groupFrequencyOverrides == null)
                {
                    settings.groupFrequencyOverrides = new Dictionary<string, float>(
                        StringComparer.OrdinalIgnoreCase);
                }

                settings.groupFrequencyOverrides.Clear();
                foreach (KeyValuePair<string, float> pair in originalFrequencyOverrides)
                {
                    settings.groupFrequencyOverrides[pair.Key] = pair.Value;
                }

                settings.Write();
            });

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.ApiVersion >= 9,
                "The additive event-frequency API requires contract version 9 or newer.");

            DiaryEventFrequencySettingsSnapshot initial = PawnDiaryApi.GetEventFrequencySettings();
            List<DiaryEventFilterSnapshot> legacyRows = PawnDiaryApi.GetEventFilters();
            PawnDiaryRimTestScope.Require(
                initial != null
                    && initial.filters != null
                    && initial.filters.Count == legacyRows.Count
                    && string.Equals(
                        initial.selectedPresetDefName,
                        settings.FrequencyPresetSnapshot().presetKey,
                        StringComparison.Ordinal),
                "The frequency settings snapshot did not expose the selected preset and legacy row set.");

            DiaryEventFrequencySettingsSnapshot detachedProbe =
                PawnDiaryApi.GetEventFrequencySettings();
            string detachedFirstKey = detachedProbe.filters[0].key;
            detachedProbe.selectedPresetDefName = "mutated_snapshot_only";
            detachedProbe.filters[0].key = "mutated_row_only";
            detachedProbe.filters.Clear();
            DiaryEventFrequencySettingsSnapshot afterDetachedMutation =
                PawnDiaryApi.GetEventFrequencySettings();
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    afterDetachedMutation.selectedPresetDefName,
                    initial.selectedPresetDefName,
                    StringComparison.Ordinal)
                    && afterDetachedMutation.filters.Count == initial.filters.Count
                    && string.Equals(
                        afterDetachedMutation.filters[0].key,
                        detachedFirstKey,
                        StringComparison.Ordinal),
                "Mutating the returned DTO changed live settings instead of remaining detached.");

            for (int i = 0; i < initial.filters.Count; i++)
            {
                DiaryEventFilterSnapshot row = initial.filters[i];
                DiaryEventFilterSnapshot legacyRow = legacyRows[i];
                DiaryInteractionGroupDef group = InteractionGroups.ByKey(row?.key);
                PawnDiaryRimTestScope.Require(
                    row != null
                        && legacyRow != null
                        && group != null
                        && string.Equals(row.key, legacyRow.key, StringComparison.Ordinal)
                        && row.enabled == legacyRow.enabled
                        && row.defaultEnabled == legacyRow.defaultEnabled
                        && row.hasOverride == legacyRow.hasOverride
                        && string.Equals(
                            row.frequencyTier,
                            DiaryFrequencyTiers.Normalize(group.frequencyTier),
                            StringComparison.Ordinal)
                        && Math.Abs(
                            row.presetFrequencyMultiplier
                            - settings.PresetGroupFrequencyMultiplier(group)) < 0.0001f
                        && Math.Abs(
                            row.effectiveFrequencyMultiplier
                            - settings.EffectiveGroupFrequencyMultiplier(group)) < 0.0001f
                        && row.hasFrequencyOverride
                            == settings.HasGroupFrequencyOverride(group.defName),
                    "Frequency snapshot row " + i + " disagreed with the saved settings policy.");
            }

            DiaryEventFilterSnapshot original = SelectOrdinaryFilter(initial.filters);
            string key = original.key;
            bool originalEnabled = original.enabled;
            bool originalEnableOverride = original.hasOverride;
            float customMultiplier = Math.Abs(original.presetFrequencyMultiplier) > 0.0001f
                ? 0f
                : 1f;

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetEventFrequencyMultiplier(key, customMultiplier),
                "SetEventFrequencyMultiplier rejected a valid settings-visible group and multiplier.");
            DiaryEventFrequencySettingsSnapshot customized = PawnDiaryApi.GetEventFrequencySettings();
            DiaryEventFilterSnapshot customRow = FindFilter(customized.filters, key);
            PawnDiaryRimTestScope.Require(
                customized.hasCustomOverrides
                    && customRow != null
                    && customRow.hasFrequencyOverride
                    && Math.Abs(customRow.effectiveFrequencyMultiplier - customMultiplier) < 0.0001f
                    && customRow.enabled == originalEnabled
                    && customRow.hasOverride == originalEnableOverride,
                "A frequency override did not remain independent from the existing enable setting.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.ResetEventFrequencyMultiplier(key),
                "ResetEventFrequencyMultiplier rejected a valid settings-visible group.");
            DiaryEventFilterSnapshot reset = FindFilter(
                PawnDiaryApi.GetEventFrequencySettings().filters,
                key);
            PawnDiaryRimTestScope.Require(
                reset != null
                    && !reset.hasFrequencyOverride
                    && Math.Abs(
                        reset.effectiveFrequencyMultiplier
                        - reset.presetFrequencyMultiplier) < 0.0001f
                    && reset.enabled == originalEnabled
                    && reset.hasOverride == originalEnableOverride,
                "Resetting frequency did not restore preset inheritance independently of enable state.");

            string selectedBeforeInvalidWrites =
                PawnDiaryApi.GetEventFrequencySettings().selectedPresetDefName;
            int overridesBeforeInvalidWrites = settings.GroupFrequencyOverrideCount();
            PawnDiaryRimTestScope.Require(
                !PawnDiaryApi.SetEventFrequencyPreset("PawnDiary_Frequency_Future")
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(
                        "pawndiary_rimtest_unknown_filter",
                        1f)
                    && !PawnDiaryApi.ResetEventFrequencyMultiplier(
                        "pawndiary_rimtest_unknown_filter")
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(key, float.NaN)
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(key, float.PositiveInfinity)
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(key, -0.01f)
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(
                        key,
                        DiaryFrequencyPolicy.MaximumMultiplier + 0.01f)
                    && string.Equals(
                        PawnDiaryApi.GetEventFrequencySettings().selectedPresetDefName,
                        selectedBeforeInvalidWrites,
                        StringComparison.Ordinal)
                    && settings.GroupFrequencyOverrideCount() == overridesBeforeInvalidWrites,
                "Unknown frequency tokens or corrupt multipliers should fail without mutation.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetEventFrequencyMultiplier(key, customMultiplier),
                "Could not seed the preset-clear independence check.");
            string alternatePreset = string.Equals(
                selectedBeforeInvalidWrites,
                DiaryFrequencyPresets.LiteDefName,
                StringComparison.Ordinal)
                    ? DiaryFrequencyPresets.StandardDefName
                    : DiaryFrequencyPresets.LiteDefName;
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.SetEventFrequencyPreset(alternatePreset.ToLowerInvariant()),
                "SetEventFrequencyPreset rejected a loaded shipped preset.");
            DiaryEventFrequencySettingsSnapshot changedPreset =
                PawnDiaryApi.GetEventFrequencySettings();
            DiaryEventFilterSnapshot presetRow = FindFilter(changedPreset.filters, key);
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    changedPreset.selectedPresetDefName,
                    alternatePreset,
                    StringComparison.Ordinal)
                    && !changedPreset.hasCustomOverrides
                    && presetRow != null
                    && !presetRow.hasFrequencyOverride
                    && presetRow.enabled == originalEnabled
                    && presetRow.hasOverride == originalEnableOverride,
                "Preset selection did not clear only frequency overrides while preserving enable state.");
        }

        /// <summary>
        /// GetApiSetup returns a self-consistent lane snapshot and withholds every raw key unless the
        /// player's separate key-sharing opt-in is enabled; the test never logs or copies a real key.
        /// </summary>
        [Test]
        public static void ApiSetupSnapshotIsConsistentAndDoesNotLeakKeysByDefault()
        {
            // Do not let the developer's existing opt-in turn this security assertion into a
            // conditional no-op. Force the protected state without reading or copying any live key,
            // then restore the exact opt-in bit during teardown.
            bool originalKeySharing = settings.enableExternalKeySharing;
            settings.enableExternalKeySharing = false;
            scope.RegisterCleanup(
                () => settings.enableExternalKeySharing = originalKeySharing);

            DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
            PawnDiaryRimTestScope.Require(
                setup != null && setup.lanes != null && !setup.keySharingEnabled,
                "GetApiSetup returned no setup/lane snapshot while integrations were enabled.");
            PawnDiaryRimTestScope.Require(
                setup.laneCount == setup.lanes.Count,
                "GetApiSetup laneCount did not match the copied lane list.");
            PawnDiaryRimTestScope.Require(
                setup.retryAttempts
                    == LlmTransportPolicy.NormalizeRetryAttempts(settings.retryAttempts)
                    && Math.Abs(
                        setup.retryBaseDelaySeconds
                        - LlmTransportPolicy.NormalizeRetryDelaySeconds(
                            settings.retryBaseDelaySeconds)) < 0.0001d,
                "GetApiSetup did not expose the effective global retry policy.");

            int countedActive = 0;
            for (int i = 0; i < setup.lanes.Count; i++)
            {
                DiaryApiLaneSnapshot lane = setup.lanes[i];
                PawnDiaryRimTestScope.Require(
                    lane != null && lane.index == i,
                    "GetApiSetup did not preserve a non-null, ordered lane row at index " + i + ".");
                if (lane.active)
                {
                    countedActive++;
                }

                PawnDiaryRimTestScope.Require(
                    string.IsNullOrEmpty(lane.apiKey),
                    "GetApiSetup exposed a raw lane key while key sharing was disabled.");
            }

            PawnDiaryRimTestScope.Require(
                setup.activeLaneCount == countedActive,
                "GetApiSetup activeLaneCount did not match the active flags in its lane rows.");
        }

        /// <summary>
        /// A deterministic temporary AmbrosiaHigh condition appears through GetPromptEnchantments and
        /// the context bundle's copied candidate list, then every live/Def/settings mutation is restored.
        /// </summary>
        [Test]
        public static void PromptEnchantmentGetterAndBundleExportLiveCandidate()
        {
            SeedForcedAmbrosiaEnchantment();

            List<DiaryPromptEnchantmentCandidateSnapshot> candidates =
                PawnDiaryApi.GetPromptEnchantments(testPawn, false);
            DiaryPromptEnchantmentCandidateSnapshot candidate =
                FindEnchantment(candidates, EnchantmentHediffDefName);
            PawnDiaryRimTestScope.Require(
                candidate != null
                    && candidate.weight >= 0f
                    && candidate.impactCues != null
                    && candidate.configuredCues != null,
                "GetPromptEnchantments did not export the forced live AmbrosiaHigh candidate.");

            DiaryContextBundleSnapshot bundle =
                PawnDiaryApi.GetContextBundle(testPawn, 1, null, true);
            PawnDiaryRimTestScope.Require(
                bundle != null
                    && FindEnchantment(
                        bundle.promptEnchantments,
                        EnchantmentHediffDefName) != null,
                "The context bundle did not include the same forced prompt-enchantment candidate.");
        }

        /// <summary>
        /// Null handles/pawns and a base-ineligible pawn return null/empty/not-recorded results across
        /// submission, entry-read, context, voice, summary, enchantment, and setter families.
        /// </summary>
        [Test]
        public static void NullAndIneligibleInputsFailClosedWithoutCreatingState()
        {
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetEntryStatus((DiaryEntryHandle)null) == null
                    && PawnDiaryApi.GetEntryStatus(string.Empty, string.Empty) == null
                    && PawnDiaryApi.GetEntrySnapshot((DiaryEntryHandle)null) == null
                    && PawnDiaryApi.GetEntrySnapshot(string.Empty, string.Empty) == null,
                "Null/blank entry handles should resolve to no status or snapshot.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetRecentEntryTitles(null, 5).Count == 0
                    && PawnDiaryApi.GetRecentEntryTitles(ineligiblePawn, 5).Count == 0
                    && PawnDiaryApi.GetEntryStats(null) == null
                    && PawnDiaryApi.GetEntryStats(ineligiblePawn) == null
                    && PawnDiaryApi.GetContextSnapshot(null, 5) == null
                    && PawnDiaryApi.GetContextSnapshot(ineligiblePawn, 5) == null
                    && PawnDiaryApi.GetContextBundle(null, 5) == null
                    && PawnDiaryApi.GetContextBundle(ineligiblePawn, 5) == null,
                "Null/ineligible recent-memory readers should return empty or null.");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetWritingStyle(null) == null
                    && PawnDiaryApi.GetWritingStyle(ineligiblePawn) == null
                    && PawnDiaryApi.GetPsychotype(null) == null
                    && PawnDiaryApi.GetPsychotype(ineligiblePawn) == null
                    && PawnDiaryApi.GetPawnSummary(null) == null
                    && PawnDiaryApi.GetPawnSummary(ineligiblePawn) == null
                    && PawnDiaryApi.GetPromptEnchantments(null).Count == 0
                    && PawnDiaryApi.GetPromptEnchantments(ineligiblePawn).Count == 0,
                "Null/ineligible voice, summary, and enchantment readers should fail closed.");

            PawnDiaryRimTestScope.Require(
                !PawnDiaryApi.SetWritingStyleOverride(
                    ineligiblePawn,
                    TestSourceId,
                    StyleOverrideRule)
                    && !PawnDiaryApi.SetPsychotype(
                        ineligiblePawn,
                        TargetPsychotypeDefName,
                        true)
                    && !PawnDiaryApi.SetPsychotypeCustomRule(
                        ineligiblePawn,
                        CustomPsychotypeRule),
                "Voice setters should reject a base-ineligible pawn.");

            DiaryEventSubmissionResult eventResult = null;
            DiaryEventSubmissionResult promptResult = null;
            DiaryEventSubmissionResult directResult = null;
            scope.RequireNoNewEvent(() =>
            {
                eventResult = PawnDiaryApi.SubmitEventWithHandle(BuildEventRequest(ineligiblePawn));
                promptResult = PawnDiaryApi.SubmitPromptEntry(BuildPromptRequest(ineligiblePawn));
                directResult = PawnDiaryApi.SubmitDirectEntry(BuildDirectRequest(ineligiblePawn));
            });
            RequireNotRecorded(eventResult, "ineligible SubmitEventWithHandle");
            RequireNotRecorded(promptResult, "ineligible SubmitPromptEntry");
            RequireNotRecorded(directResult, "ineligible SubmitDirectEntry");
        }

        /// <summary>
        /// Turning off the player's master switch gates valid submissions, every representative
        /// loaded-game reader, global setup/filter reads and writes, and voice writes without mutation.
        /// </summary>
        [Test]
        public static void MasterIntegrationSwitchGatesFacadeReadsAndWrites()
        {
            List<DiaryEventFilterSnapshot> filtersBeforeGate = PawnDiaryApi.GetEventFilters();
            PawnDiaryRimTestScope.Require(
                filtersBeforeGate.Count > 0,
                "A real event-filter key is required to prove the master-switch write gate.");
            string realFilterKey = filtersBeforeGate[0].key;

            settings.allowExternalIntegrations = false;

            DiaryEventSubmissionResult eventResult = null;
            DiaryEventSubmissionResult promptResult = null;
            DiaryEventSubmissionResult directResult = null;
            scope.RequireNoNewEvent(() =>
            {
                eventResult = PawnDiaryApi.SubmitEventWithHandle(BuildEventRequest(testPawn));
                promptResult = PawnDiaryApi.SubmitPromptEntry(BuildPromptRequest(testPawn));
                directResult = PawnDiaryApi.SubmitDirectEntry(BuildDirectRequest(testPawn));
            });
            RequireNotRecorded(eventResult, "gated SubmitEventWithHandle");
            RequireNotRecorded(promptResult, "gated SubmitPromptEntry");
            RequireNotRecorded(directResult, "gated SubmitDirectEntry");

            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetRecentEntryTitles(testPawn, 5).Count == 0
                    && PawnDiaryApi.GetEntryStats(testPawn) == null
                    && PawnDiaryApi.GetContextSnapshot(testPawn, 5) == null
                    && PawnDiaryApi.GetContextBundle(testPawn, 5) == null
                    && PawnDiaryApi.GetWritingStyle(testPawn) == null
                    && PawnDiaryApi.GetPsychotype(testPawn) == null
                    && PawnDiaryApi.GetPawnSummary(testPawn) == null
                    && PawnDiaryApi.GetPromptEnchantments(testPawn).Count == 0,
                "The master switch did not gate the loaded-game public readers.");
            PawnDiaryRimTestScope.Require(
                PawnDiaryApi.GetApiSetup() == null
                    && PawnDiaryApi.GetEventFilters().Count == 0
                    && PawnDiaryApi.GetEventFrequencySettings() == null
                    && !PawnDiaryApi.IsEventFilterEnabled(realFilterKey)
                    && !PawnDiaryApi.SetEventFilterEnabled(realFilterKey, true)
                    && !PawnDiaryApi.SetEventFrequencyPreset(
                        filtersBeforeGate[0].hasFrequencyOverride
                            ? DiaryFrequencyPresets.StandardDefName
                            : DiaryFrequencyPresets.LiteDefName)
                    && !PawnDiaryApi.SetEventFrequencyMultiplier(realFilterKey, 0.5f)
                    && !PawnDiaryApi.ResetEventFrequencyMultiplier(realFilterKey),
                "The master switch did not gate global setup/event-filter access.");
            PawnDiaryRimTestScope.Require(
                !PawnDiaryApi.SetWritingStyleOverride(
                    testPawn,
                    TestSourceId,
                    StyleOverrideRule)
                    && !PawnDiaryApi.SetPsychotype(
                        testPawn,
                        TargetPsychotypeDefName,
                        true)
                    && !PawnDiaryApi.SetPsychotypeCustomRule(
                        testPawn,
                        CustomPsychotypeRule),
                "The master switch did not gate the public voice setters.");

            settings.allowExternalIntegrations = true;
        }

        // ----- request / assertion helpers ---------------------------------------------------------

        private static ExternalEventRequest BuildEventRequest(Pawn pawn)
        {
            return new ExternalEventRequest
            {
                sourceId = TestSourceId,
                eventKey = DevTestEventKey,
                subject = pawn,
                summaryText = "A test-owned external moment was recorded.",
                eventLabel = "Public API handle test",
                forceRecord = true,
                dedupKey = "public-api-handle"
            };
        }

        private static ExternalPromptEntryRequest BuildPromptRequest(Pawn pawn)
        {
            return new ExternalPromptEntryRequest
            {
                sourceId = TestSourceId,
                eventKey = PromptEventKey,
                subject = pawn,
                summaryText = "A test-owned wrapped prompt moment was recorded.",
                eventLabel = "Public API prompt-entry test",
                promptInstruction = PromptInstruction,
                forceRecord = true,
                dedupKey = "public-api-prompt"
            };
        }

        private static ExternalDirectEntryRequest BuildDirectRequest(Pawn pawn)
        {
            return new ExternalDirectEntryRequest
            {
                sourceId = TestSourceId,
                eventKey = DirectEventKey,
                subject = pawn,
                text = DirectText,
                title = DirectTitle,
                summaryText = "The pawn kept a small lamp burning.",
                eventLabel = "Public API direct-entry test",
                forceRecord = true,
                dedupKey = "public-api-direct",
                generateTitleIfMissing = false
            };
        }

        private static DiaryEvent SubmitDirectAndRequire(out DiaryEventSubmissionResult result)
        {
            DiaryEventSubmissionResult captured = null;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => captured = PawnDiaryApi.SubmitDirectEntry(BuildDirectRequest(testPawn)),
                DirectEventKey,
                testPawn,
                null);
            result = captured;
            RequireRecordedSoloResult(result, diaryEvent, DirectEventKey);
            return diaryEvent;
        }

        private static void RequireRecordedSoloResult(
            DiaryEventSubmissionResult result,
            DiaryEvent diaryEvent,
            string expectedEventKey)
        {
            PawnDiaryRimTestScope.Require(
                result != null
                    && result.recorded
                    && !result.pairwise
                    && result.primary != null
                    && result.partner == null,
                "The valid " + expectedEventKey + " submission did not return one recorded solo handle.");
            PawnDiaryRimTestScope.Require(
                string.Equals(result.sourceId, TestSourceId, StringComparison.Ordinal)
                    && string.Equals(result.eventKey, expectedEventKey, StringComparison.Ordinal)
                    && string.Equals(result.primary.eventId, diaryEvent.eventId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(result.primary.pawnId)
                    && !string.IsNullOrWhiteSpace(result.primary.entryKey),
                "The " + expectedEventKey + " submission handle did not match the persisted event.");
        }

        private static void RequireNotRecorded(DiaryEventSubmissionResult result, string operation)
        {
            PawnDiaryRimTestScope.Require(
                result != null
                    && !result.recorded
                    && !result.pairwise
                    && result.primary == null
                    && result.partner == null,
                operation + " should return a non-recorded result with no handles.");
        }

        private static void RequireStatusFor(
            DiaryEntryStatusSnapshot snapshot,
            DiaryEntryHandle handle,
            string operation)
        {
            PawnDiaryRimTestScope.Require(
                snapshot != null
                    && snapshot.handle != null
                    && string.Equals(
                        snapshot.handle.eventId,
                        handle.eventId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        snapshot.handle.povRole,
                        handle.povRole,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(snapshot.status),
                "The " + operation + " did not resolve the submitted entry.");
        }

        private static void RequireEntryFor(
            DiaryEntrySnapshot snapshot,
            DiaryEntryHandle handle,
            string operation)
        {
            PawnDiaryRimTestScope.Require(
                snapshot != null
                    && snapshot.handle != null
                    && string.Equals(
                        snapshot.handle.eventId,
                        handle.eventId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        snapshot.handle.povRole,
                        handle.povRole,
                        StringComparison.OrdinalIgnoreCase),
                "The " + operation + " did not resolve the submitted entry.");
        }

        private static DiaryEntryTitleQuery DirectEntryQuery(string sourceId)
        {
            return new DiaryEntryTitleQuery
            {
                sourceId = sourceId,
                eventKey = DirectEventKey,
                hasTitle = 1,
                hasGeneratedText = 1
            };
        }

        private static DiaryEntryTitleSnapshot FindTitle(
            List<DiaryEntryTitleSnapshot> titles,
            string eventId)
        {
            if (titles == null)
            {
                return null;
            }

            for (int i = 0; i < titles.Count; i++)
            {
                DiaryEntryTitleSnapshot title = titles[i];
                if (title != null
                    && string.Equals(title.eventId, eventId, StringComparison.Ordinal))
                {
                    return title;
                }
            }

            return null;
        }

        private static void RequireContextContains(
            DiaryContextSnapshot context,
            string eventId,
            string operation)
        {
            PawnDiaryRimTestScope.Require(
                context != null
                    && context.entries != null
                    && context.entryCount == context.entries.Count
                    && context.entryCount == 1
                    && context.entries[0] != null
                    && string.Equals(
                        context.entries[0].eventId,
                        eventId,
                        StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(context.entries[0].summary),
                "The " + operation + " did not expose exactly the completed direct entry.");
        }

        private static bool ContainsStyle(
            List<DiaryWritingStyleSnapshot> styles,
            string defName)
        {
            for (int i = 0; i < styles.Count; i++)
            {
                DiaryWritingStyleSnapshot style = styles[i];
                if (style != null
                    && string.Equals(style.styleDefName, defName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static DiaryEventFilterSnapshot SelectOrdinaryFilter(
            List<DiaryEventFilterSnapshot> filters)
        {
            for (int i = 0; i < filters.Count; i++)
            {
                DiaryEventFilterSnapshot filter = filters[i];
                string key = filter?.key ?? string.Empty;
                // Split migration rows have inheritance rules that intentionally preserve an explicit
                // default-valued override. Pick an ordinary row so toggling back proves the common
                // "matching XML default removes the override" path as well.
                if (!string.IsNullOrWhiteSpace(key)
                    && key.IndexOf("counsel", StringComparison.OrdinalIgnoreCase) < 0
                    && key.IndexOf("conversion", StringComparison.OrdinalIgnoreCase) < 0
                    && key.IndexOf("reflection", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return filter;
                }
            }

            throw new AssertionException(
                "No ordinary settings-visible event filter was available for the round-trip test.");
        }

        private static DiaryEventFilterSnapshot FindFilter(
            List<DiaryEventFilterSnapshot> filters,
            string key)
        {
            if (filters == null)
            {
                return null;
            }

            for (int i = 0; i < filters.Count; i++)
            {
                DiaryEventFilterSnapshot filter = filters[i];
                if (filter != null
                    && string.Equals(filter.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return filter;
                }
            }

            return null;
        }

        // ----- prompt-enchantment fixture ----------------------------------------------------------

        private static void SeedForcedAmbrosiaEnchantment()
        {
            bool originalEnabled = settings.enablePromptEnchantments;
            settings.enablePromptEnchantments = true;
            scope.RegisterCleanup(() => settings.enablePromptEnchantments = originalEnabled);

            DiaryPromptEnchantmentDef def =
                DefDatabase<DiaryPromptEnchantmentDef>.GetNamedSilentFail(EnchantmentDefName);
            PawnDiaryRimTestScope.Require(
                def != null,
                "The base-game AmbrosiaHigh prompt-enchantment Def was not loaded.");
            float originalChance = def.chance;
            float originalFrequency = def.frequency;
            bool originalVisibleOnly = def.visibleOnly;
            def.chance = 1f;
            def.frequency = -1f;
            def.visibleOnly = false;
            scope.RegisterCleanup(() =>
            {
                def.chance = originalChance;
                def.frequency = originalFrequency;
                def.visibleOnly = originalVisibleOnly;
            });

            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(EnchantmentHediffDefName);
            PawnDiaryRimTestScope.Require(
                hediffDef != null,
                "The base-game AmbrosiaHigh HediffDef was not loaded.");
            Hediff hediff = HediffMaker.MakeHediff(hediffDef, testPawn);
            hediff.Severity = 0.5f;
            testPawn.health.AddHediff(hediff);
            scope.RegisterCleanup(() => RemoveHediffIfPresent(testPawn, hediff));
        }

        private static DiaryPromptEnchantmentCandidateSnapshot FindEnchantment(
            List<DiaryPromptEnchantmentCandidateSnapshot> candidates,
            string hediffDefName)
        {
            if (candidates == null)
            {
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                DiaryPromptEnchantmentCandidateSnapshot candidate = candidates[i];
                if (candidate != null
                    && string.Equals(
                        candidate.sourceHediffDefName,
                        hediffDefName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void RemoveHediffIfPresent(Pawn pawn, Hediff hediff)
        {
            if (pawn?.health?.hediffSet?.hediffs != null
                && hediff != null
                && pawn.health.hediffSet.hediffs.Contains(hediff))
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        /// <summary>
        /// Exact mutable voice-record snapshot used only for test cleanup. Public setters are what the
        /// test exercises; direct field restoration is necessary because the public custom-rule setter
        /// deliberately rejects blank text and therefore cannot restore a previously empty custom slot.
        /// </summary>
        private sealed class VoiceRecordState
        {
            private string personaDefName;
            private string externalWritingStyleOverrideRule;
            private string externalWritingStyleOverrideSourceId;
            private string customWritingStyleRule;
            private string psychotypeDefName;
            private string externalPsychotypeOverrideRule;
            private string externalPsychotypeOverrideSourceId;
            private string customPsychotypeRule;
            private string voiceStageBand;
            private bool psychotypePinned;
            private bool writingStylePinned;

            public static VoiceRecordState Capture(PawnDiaryRecord record)
            {
                return new VoiceRecordState
                {
                    personaDefName = record.personaDefName,
                    externalWritingStyleOverrideRule = record.externalWritingStyleOverrideRule,
                    externalWritingStyleOverrideSourceId =
                        record.externalWritingStyleOverrideSourceId,
                    customWritingStyleRule = record.customWritingStyleRule,
                    psychotypeDefName = record.psychotypeDefName,
                    externalPsychotypeOverrideRule = record.externalPsychotypeOverrideRule,
                    externalPsychotypeOverrideSourceId =
                        record.externalPsychotypeOverrideSourceId,
                    customPsychotypeRule = record.customPsychotypeRule,
                    voiceStageBand = record.voiceStageBand,
                    psychotypePinned = record.psychotypePinned,
                    writingStylePinned = record.writingStylePinned
                };
            }

            public void Restore(PawnDiaryRecord record)
            {
                record.personaDefName = personaDefName;
                record.externalWritingStyleOverrideRule = externalWritingStyleOverrideRule;
                record.externalWritingStyleOverrideSourceId =
                    externalWritingStyleOverrideSourceId;
                record.customWritingStyleRule = customWritingStyleRule;
                record.psychotypeDefName = psychotypeDefName;
                record.externalPsychotypeOverrideRule = externalPsychotypeOverrideRule;
                record.externalPsychotypeOverrideSourceId =
                    externalPsychotypeOverrideSourceId;
                record.customPsychotypeRule = customPsychotypeRule;
                record.voiceStageBand = voiceStageBand;
                record.psychotypePinned = psychotypePinned;
                record.writingStylePinned = writingStylePinned;
            }

            public void RequireMatches(PawnDiaryRecord record, string operation)
            {
                PawnDiaryRimTestScope.Require(
                    string.Equals(record.personaDefName, personaDefName, StringComparison.Ordinal)
                        && string.Equals(
                            record.externalWritingStyleOverrideRule,
                            externalWritingStyleOverrideRule,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.externalWritingStyleOverrideSourceId,
                            externalWritingStyleOverrideSourceId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.customWritingStyleRule,
                            customWritingStyleRule,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.psychotypeDefName,
                            psychotypeDefName,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.externalPsychotypeOverrideRule,
                            externalPsychotypeOverrideRule,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.externalPsychotypeOverrideSourceId,
                            externalPsychotypeOverrideSourceId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.customPsychotypeRule,
                            customPsychotypeRule,
                            StringComparison.Ordinal)
                        && string.Equals(
                            record.voiceStageBand,
                            voiceStageBand,
                            StringComparison.Ordinal)
                        && record.psychotypePinned == psychotypePinned
                        && record.writingStylePinned == writingStylePinned,
                    "The " + operation + " did not recover every mutable voice-record field.");
            }
        }
    }
}
