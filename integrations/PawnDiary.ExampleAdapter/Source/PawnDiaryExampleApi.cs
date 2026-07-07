// Copyable Pawn Diary integration layer for the example adapter.
//
// This is the only file in the example adapter that calls PawnDiaryApi directly. If you are writing
// a real adapter, start here: copy this file, change SourceId / ExampleEventKey, keep the status
// checks, and replace the demo hook callbacks with hooks from your target mod.
//
// The rest of the API Explorer is UI/test harness code. Keeping every core API touch in this file
// makes the contract easy to audit and gives other mod authors one clear example to copy.
//
// New to C#/RimWorld? See AGENTS.md. For the public contract, see INTEGRATIONS.md / EXTERNAL_API.md.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using RimWorld;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Facade over <see cref="PawnDiaryApi"/> used by the example adapter. Every method documents
    /// the required arguments and the safe return value a caller should expect.
    /// </summary>
    internal static class PawnDiaryExampleApi
    {
        /// <summary>Stable source id used for log attribution and source-owned overrides.</summary>
        public const string SourceId = "aimmlegate.pawndiary.adapter.example";

        /// <summary>Stable event key claimed by this adapter's External group XML.</summary>
        public const string ExampleEventKey = "exampleadapter_quiet_moment";

        /// <summary>Stable hook id for the example pawn-context provider registration.</summary>
        private const string ProviderId = SourceId + ".trait_context";

        /// <summary>Stable hook id for the example entry-status listener registration.</summary>
        private const string ListenerId = SourceId + ".status_listener";

        /// <summary>Tracks whether the process-global example hooks have already been registered.</summary>
        private static bool hooksRegistered;

        /// <summary>
        /// Gets the public Pawn Diary integration API version.
        /// </summary>
        /// <returns>The integer contract version exposed by Pawn Diary.</returns>
        public static int ApiVersion
        {
            get { return PawnDiaryApi.ApiVersion; }
        }

        /// <summary>
        /// Checks whether a game is loaded and Pawn Diary's game component is alive.
        /// </summary>
        /// <returns>True while public API calls can reach a live game component; false in menus or loading.</returns>
        public static bool IsReady
        {
            get { return PawnDiaryApi.IsReady; }
        }

        /// <summary>
        /// Checks the player-controlled master switch for external integrations.
        /// </summary>
        /// <returns>True when Pawn Diary settings allow external API behavior; false when disabled.</returns>
        public static bool IsExternalApiEnabled
        {
            get { return PawnDiaryApi.IsExternalApiEnabled; }
        }

        /// <summary>
        /// Checks both runtime readiness and the player master switch before adapter work.
        /// </summary>
        /// <returns>True when request/read calls should be attempted; false when the adapter should skip.</returns>
        public static bool CanUseExternalApi
        {
            get { return IsReady && IsExternalApiEnabled; }
        }

        /// <summary>
        /// Registers the example entry-status listener and pawn-context provider once per process.
        /// </summary>
        /// <remarks>
        /// Call from a main-thread game-load hook, such as a GameComponent constructor. Registration
        /// is safe while the player master switch is off; Pawn Diary gates later invocation.
        /// </remarks>
        public static void RegisterHooksOnce()
        {
            if (hooksRegistered)
            {
                return;
            }

            PawnDiaryApi.RegisterPawnContextProvider(ProviderId, TraitContextLine);
            PawnDiaryApi.RegisterEntryStatusListener(ListenerId, OnEntryStatus);
            hooksRegistered = true;
        }

        /// <summary>
        /// Supplies one compact provider line for Pawn Diary's pawn-summary context.
        /// </summary>
        /// <param name="pawn">Pawn being summarized. Pawn Diary supplies this on the main thread.</param>
        /// <returns>A cleaned "example_traits=..." line, or null when there is nothing useful to add.</returns>
        private static string TraitContextLine(Pawn pawn)
        {
            ExplorerState.providerInvocations++;

            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits == null || traits.Count == 0)
            {
                return null;
            }

            StringBuilder labels = new StringBuilder();
            int kept = 0;
            for (int i = 0; i < traits.Count && kept < 2; i++)
            {
                Trait trait = traits[i];
                if (trait == null)
                {
                    continue;
                }

                string label = trait.LabelCap.ToString();
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (kept > 0)
                {
                    labels.Append(", ");
                }

                labels.Append(label);
                kept++;
            }

            return labels.Length == 0 ? null : "example_traits=" + labels;
        }

        /// <summary>
        /// Receives lifecycle snapshots after Pawn Diary changes an entry POV's status.
        /// </summary>
        /// <param name="snapshot">Public status snapshot from Pawn Diary; null is ignored defensively.</param>
        private static void OnEntryStatus(DiaryEntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            string description = (snapshot.date ?? "?") + "  " + (snapshot.groupLabel ?? "?")
                + "  status=" + (snapshot.status ?? "?")
                + "  title=" + (string.IsNullOrEmpty(snapshot.title) ? "(none)" : snapshot.title);

            ExplorerState.RecordListenerEvent(description);
        }

        /// <summary>
        /// Builds the minimal sample external event request used by the quick dev actions.
        /// </summary>
        /// <param name="subject">Pawn whose diary should receive the event. Required for recording.</param>
        /// <returns>A request with sourceId, eventKey, subject, localized summary text, label, and context.</returns>
        public static ExternalEventRequest BuildQuietMomentRequest(Pawn subject)
        {
            string subjectLabel = subject == null ? "the pawn" : subject.LabelShortCap.ToString();
            return new ExternalEventRequest
            {
                sourceId = SourceId,
                eventKey = ExampleEventKey,
                subject = subject,
                summaryText = "PawnDiaryExampleAdapter.QuietMomentSummary".Translate(subjectLabel).Resolve(),
                eventLabel = "PawnDiaryExampleAdapter.QuietMomentLabel".Translate().Resolve(),
                extraContext = new List<string> { "origin=example_dev_action" }
            };
        }

        /// <summary>
        /// Submits the sample quiet-moment event for a pawn.
        /// </summary>
        /// <param name="subject">Pawn whose diary should receive the event. Required for recording.</param>
        /// <param name="outcome">Receives the reason the event recorded or failed to record.</param>
        /// <returns>True when Pawn Diary recorded an entry; false for invalid, disabled, budgeted, or dropped calls.</returns>
        public static bool SubmitQuietMoment(Pawn subject, out SubmitEventOutcome outcome)
        {
            return PawnDiaryApi.SubmitEvent(BuildQuietMomentRequest(subject), out outcome);
        }

        /// <summary>
        /// Builds a side-effect-free prompt preview for the sample quiet-moment event.
        /// </summary>
        /// <param name="subject">Pawn whose prompt should be previewed. Required for a non-null result.</param>
        /// <returns>A prompt preview snapshot, or null when the request is invalid or disabled.</returns>
        public static DiaryPromptPreviewSnapshot PreviewQuietMoment(Pawn subject)
        {
            ExternalEventRequest req = BuildQuietMomentRequest(subject);
            req.extraContext = null;
            return PawnDiaryApi.PreviewPrompt(req);
        }

        /// <summary>
        /// Reads the all-in-one context bundle used by the quick debug action.
        /// </summary>
        /// <param name="pawn">Pawn to read context for. Required for a non-null result.</param>
        /// <returns>A context bundle snapshot, or null when the API is disabled, not ready, or the pawn is invalid.</returns>
        public static DiaryContextBundleSnapshot GetQuickContextBundle(Pawn pawn)
        {
            return PawnDiaryApi.GetContextBundle(pawn, 5);
        }

        /// <summary>
        /// Checks whether a pawn can currently own normal first-person diary entries.
        /// </summary>
        /// <param name="pawn">Pawn to check. Required; null returns false.</param>
        /// <returns>True when Pawn Diary considers the pawn eligible through the public API.</returns>
        public static bool IsDiaryEligible(Pawn pawn)
        {
            return PawnDiaryApi.IsDiaryEligible(pawn);
        }

        /// <summary>
        /// Reads the pawn's saved per-pawn diary generation toggle.
        /// </summary>
        /// <param name="pawn">Pawn to inspect. Required; null returns false.</param>
        /// <returns>True when generation is enabled for the pawn; false when disabled or invalid.</returns>
        public static bool IsDiaryGenerationEnabled(Pawn pawn)
        {
            return PawnDiaryApi.IsDiaryGenerationEnabled(pawn);
        }

        /// <summary>
        /// Sets the pawn's saved diary generation toggle.
        /// </summary>
        /// <param name="pawn">Pawn to update. Required.</param>
        /// <param name="enabled">True to enable future generation; false to disable it.</param>
        /// <returns>True when Pawn Diary accepted the change; false when invalid, disabled, or failed.</returns>
        public static bool SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            return PawnDiaryApi.SetDiaryGenerationEnabled(pawn, enabled);
        }

        /// <summary>
        /// Submits a factual external event and asks Pawn Diary to record/write it.
        /// </summary>
        /// <param name="request">External event request. Requires eventKey and subject.</param>
        /// <param name="outcome">Receives the public record/drop reason.</param>
        /// <returns>True when an entry was recorded; false when validation, settings, budget, or pipeline dropped it.</returns>
        public static bool SubmitEvent(ExternalEventRequest request, out SubmitEventOutcome outcome)
        {
            return PawnDiaryApi.SubmitEvent(request, out outcome);
        }

        /// <summary>
        /// Submits a factual external event and returns handles when an entry is created.
        /// </summary>
        /// <param name="request">External event request. Requires eventKey and subject.</param>
        /// <returns>A submission result; recorded is true only when at least the primary handle exists.</returns>
        public static DiaryEventSubmissionResult SubmitEventWithHandle(ExternalEventRequest request)
        {
            return PawnDiaryApi.SubmitEventWithHandle(request);
        }

        /// <summary>
        /// Previews the prompt for an ordinary external event without saving or spending tokens.
        /// </summary>
        /// <param name="request">External event request. Requires eventKey and subject.</param>
        /// <param name="povRole">Optional POV role, usually initiator or recipient; null lets Pawn Diary choose.</param>
        /// <returns>A prompt preview snapshot, or null when the request cannot produce a prompt.</returns>
        public static DiaryPromptPreviewSnapshot PreviewPrompt(ExternalEventRequest request, string povRole = null)
        {
            return PawnDiaryApi.PreviewPrompt(request, povRole);
        }

        /// <summary>
        /// Submits an adapter-owned prompt instruction for Pawn Diary to write as a normal entry.
        /// </summary>
        /// <param name="request">Prompt entry request. Requires eventKey, subject, and promptInstruction.</param>
        /// <returns>A submission result; recorded is true only when an entry was created.</returns>
        public static DiaryEventSubmissionResult SubmitPromptEntry(ExternalPromptEntryRequest request)
        {
            return PawnDiaryApi.SubmitPromptEntry(request);
        }

        /// <summary>
        /// Previews a prompt-entry request without saving or spending tokens.
        /// </summary>
        /// <param name="request">Prompt entry request. Requires eventKey, subject, and promptInstruction.</param>
        /// <param name="povRole">Optional POV role, usually initiator or recipient; null lets Pawn Diary choose.</param>
        /// <returns>A prompt preview snapshot, or null when the request cannot produce a prompt.</returns>
        public static DiaryPromptPreviewSnapshot PreviewPrompt(ExternalPromptEntryRequest request, string povRole = null)
        {
            return PawnDiaryApi.PreviewPrompt(request, povRole);
        }

        /// <summary>
        /// Saves caller-authored final prose without the main LLM rewrite.
        /// </summary>
        /// <param name="request">Direct entry request. Requires eventKey, subject, and text.</param>
        /// <returns>A submission result; recorded is true only when the diary event was saved.</returns>
        public static DiaryEventSubmissionResult SubmitDirectEntry(ExternalDirectEntryRequest request)
        {
            return PawnDiaryApi.SubmitDirectEntry(request);
        }

        /// <summary>
        /// Reads current lifecycle status for a handled entry POV.
        /// </summary>
        /// <param name="handle">Handle returned by a submit call. Required for a non-null result.</param>
        /// <returns>Status snapshot, or null when missing, pruned, disabled, or invalid.</returns>
        public static DiaryEntryStatusSnapshot GetEntryStatus(DiaryEntryHandle handle)
        {
            return PawnDiaryApi.GetEntryStatus(handle);
        }

        /// <summary>
        /// Reads one entry's metadata and completed player-visible prose.
        /// </summary>
        /// <param name="handle">Handle returned by a submit call. Required for a non-null result.</param>
        /// <returns>Entry snapshot, or null when missing, pruned, disabled, or invalid.</returns>
        public static DiaryEntrySnapshot GetEntrySnapshot(DiaryEntryHandle handle)
        {
            return PawnDiaryApi.GetEntrySnapshot(handle);
        }

        /// <summary>
        /// Reads recent completed diary titles for one pawn.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be read. Required.</param>
        /// <param name="maxCount">Maximum rows requested. Must be positive.</param>
        /// <returns>Newest-first title snapshots; empty when no rows or the call is not allowed.</returns>
        public static List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn pawn, int maxCount)
        {
            return PawnDiaryApi.GetRecentEntryTitles(pawn, maxCount);
        }

        /// <summary>
        /// Reads recent completed diary titles for one pawn using query filters.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be read. Required.</param>
        /// <param name="maxCount">Maximum rows requested. Must be positive.</param>
        /// <param name="query">Optional title query filter; null behaves like the simpler overload.</param>
        /// <returns>Newest-first title snapshots; empty when no rows or the call is not allowed.</returns>
        public static List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn pawn, int maxCount, DiaryEntryTitleQuery query)
        {
            return PawnDiaryApi.GetRecentEntryTitles(pawn, maxCount, query);
        }

        /// <summary>
        /// Reads recent diary prose summaries for one pawn.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be read. Required.</param>
        /// <param name="maxEntries">Maximum rows requested. Must be positive.</param>
        /// <returns>Context snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryContextSnapshot GetContextSnapshot(Pawn pawn, int maxEntries)
        {
            return PawnDiaryApi.GetContextSnapshot(pawn, maxEntries);
        }

        /// <summary>
        /// Reads recent diary prose summaries for one pawn using query filters.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be read. Required.</param>
        /// <param name="maxEntries">Maximum rows requested. Must be positive.</param>
        /// <param name="query">Optional title/prose query filter.</param>
        /// <returns>Context snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryContextSnapshot GetContextSnapshot(Pawn pawn, int maxEntries, DiaryEntryTitleQuery query)
        {
            return PawnDiaryApi.GetContextSnapshot(pawn, maxEntries, query);
        }

        /// <summary>
        /// Counts one pawn's diary entries without reading full prose rows.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be counted. Required.</param>
        /// <returns>Stats snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryEntryStatsSnapshot GetEntryStats(Pawn pawn)
        {
            return PawnDiaryApi.GetEntryStats(pawn);
        }

        /// <summary>
        /// Counts one pawn's diary entries after applying query filters.
        /// </summary>
        /// <param name="pawn">Pawn whose diary should be counted. Required.</param>
        /// <param name="query">Optional title/prose query filter.</param>
        /// <returns>Stats snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryEntryStatsSnapshot GetEntryStats(Pawn pawn, DiaryEntryTitleQuery query)
        {
            return PawnDiaryApi.GetEntryStats(pawn, query);
        }

        /// <summary>
        /// Reads the structured pawn summary Pawn Diary would feed into prompts.
        /// </summary>
        /// <param name="pawn">Pawn to summarize. Required.</param>
        /// <returns>Pawn summary snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryPawnSummarySnapshot GetPawnSummary(Pawn pawn)
        {
            return PawnDiaryApi.GetPawnSummary(pawn);
        }

        /// <summary>
        /// Reads prompt-enchantment candidates Pawn Diary would consider for the pawn.
        /// </summary>
        /// <param name="pawn">Pawn whose candidates should be read. Required.</param>
        /// <param name="includeImportantEventContext">True to include important-event-only candidates.</param>
        /// <returns>Candidate snapshots; empty when disabled, invalid, not ready, or no candidates match.</returns>
        public static List<DiaryPromptEnchantmentCandidateSnapshot> GetPromptEnchantments(
            Pawn pawn,
            bool includeImportantEventContext)
        {
            return PawnDiaryApi.GetPromptEnchantments(pawn, includeImportantEventContext);
        }

        /// <summary>
        /// Reads style, pawn summary, prompt enchantments, and recent context in one bundle.
        /// </summary>
        /// <param name="pawn">Pawn whose context should be read. Required.</param>
        /// <param name="maxEntries">Maximum recent-context rows. Must be positive.</param>
        /// <returns>Context bundle snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryContextBundleSnapshot GetContextBundle(Pawn pawn, int maxEntries)
        {
            return PawnDiaryApi.GetContextBundle(pawn, maxEntries);
        }

        /// <summary>
        /// Reads the context bundle while including optional important-event candidate context.
        /// </summary>
        /// <param name="pawn">Pawn whose context should be read. Required.</param>
        /// <param name="maxEntries">Maximum recent-context rows. Must be positive.</param>
        /// <param name="includeImportantEventContext">True to include important-event-only candidates.</param>
        /// <returns>Context bundle snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryContextBundleSnapshot GetContextBundle(
            Pawn pawn,
            int maxEntries,
            bool includeImportantEventContext)
        {
            return PawnDiaryApi.GetContextBundle(pawn, maxEntries, includeImportantEventContext);
        }

        /// <summary>
        /// Reads the context bundle with both recent-context filters and important-event candidate scope.
        /// </summary>
        /// <param name="pawn">Pawn whose context should be read. Required.</param>
        /// <param name="maxEntries">Maximum recent-context rows. Must be positive.</param>
        /// <param name="query">Optional title/prose query filter for recent context.</param>
        /// <param name="includeImportantEventContext">True to include important-event-only candidates.</param>
        /// <returns>Context bundle snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryContextBundleSnapshot GetContextBundle(
            Pawn pawn,
            int maxEntries,
            DiaryEntryTitleQuery query,
            bool includeImportantEventContext)
        {
            return PawnDiaryApi.GetContextBundle(pawn, maxEntries, query, includeImportantEventContext);
        }

        /// <summary>
        /// Reads the pawn's base saved diary writing style.
        /// </summary>
        /// <param name="pawn">Pawn whose style should be read. Required.</param>
        /// <returns>Writing-style snapshot, or null when disabled, invalid, or not ready.</returns>
        public static DiaryWritingStyleSnapshot GetWritingStyle(Pawn pawn)
        {
            return PawnDiaryApi.GetWritingStyle(pawn);
        }

        /// <summary>
        /// Reads the available writing-style catalog.
        /// </summary>
        /// <returns>Style snapshots; empty when disabled, not ready, or no styles are available.</returns>
        public static List<DiaryWritingStyleSnapshot> GetAvailableWritingStyles()
        {
            return PawnDiaryApi.GetAvailableWritingStyles();
        }

        /// <summary>
        /// Saves a source-owned writing-style override for a pawn.
        /// </summary>
        /// <param name="pawn">Pawn whose style should be overridden. Required.</param>
        /// <param name="sourceId">Adapter source id that owns the override. Required.</param>
        /// <param name="rule">One-line style rule. Required and cleaned by Pawn Diary.</param>
        /// <returns>True when the override was saved; false when disabled, invalid, or rejected.</returns>
        public static bool SetWritingStyleOverride(Pawn pawn, string sourceId, string rule)
        {
            return PawnDiaryApi.SetWritingStyleOverride(pawn, sourceId, rule);
        }

        /// <summary>
        /// Clears a source-owned writing-style override for a pawn.
        /// </summary>
        /// <param name="pawn">Pawn whose override should be cleared. Required.</param>
        /// <param name="sourceId">Adapter source id that owns the override. Required.</param>
        /// <returns>True when cleared or already absent for this source; false when disabled, invalid, or owned by another source.</returns>
        public static bool ResetWritingStyleOverride(Pawn pawn, string sourceId)
        {
            return PawnDiaryApi.ResetWritingStyleOverride(pawn, sourceId);
        }

        /// <summary>Endpoint used by the demo API lane the explorer can add: a local, keyless
        /// OpenAI-compatible server (e.g. Ollama), so the lane is harmless even if it is never used.</summary>
        public const string DemoLaneUrl = "http://localhost:11434/v1";

        /// <summary>Model name used by the demo API lane. Purely illustrative.</summary>
        public const string DemoLaneModel = "pawndiary-demo-model";

        /// <summary>
        /// Reads the player's current LLM API setup: routing mode, global request knobs, and each
        /// configured lane (endpoint, model, auth, and — sensitively — the real API key).
        /// </summary>
        /// <returns>A setup snapshot, or null when the master switch is off or the call is off-thread.</returns>
        public static DiaryApiSetupSnapshot GetApiSetup()
        {
            return PawnDiaryApi.GetApiSetup();
        }

        /// <summary>
        /// Builds the sample "add a new active lane" request used by the explorer's demo action. A real
        /// adapter fills in its own url/model/apiKey; only url and model are required.
        /// </summary>
        /// <returns>A request that adds a local, keyless, enabled demo lane.</returns>
        public static ExternalApiLaneRequest BuildDemoApiLaneRequest()
        {
            return new ExternalApiLaneRequest
            {
                sourceId = SourceId,
                url = DemoLaneUrl,
                model = DemoLaneModel,
                apiKey = string.Empty,
                // authMode/apiMode default to bearer/chatCompletions; enabled=true makes the lane active.
                enabled = true,
                avoidDuplicate = true
            };
        }

        /// <summary>
        /// Adds a new active LLM API lane to Pawn Diary's connection settings. This edits real player
        /// settings and persists immediately; the lane can be removed in Pawn Diary's Connection settings.
        /// </summary>
        /// <param name="request">Lane request; url and model are required, other fields optional.</param>
        /// <returns>The result: whether it was added, was a duplicate, or was rejected and why.</returns>
        public static AddApiLaneResult AddApiLane(ExternalApiLaneRequest request)
        {
            return PawnDiaryApi.AddApiLane(request);
        }

        /// <summary>
        /// Reads the automatic-capture event filters — the same per-group on/off toggles the player
        /// edits on Pawn Diary's settings "Events" tab.
        /// </summary>
        /// <returns>Filter snapshots (empty when the master switch is off or the call is off-thread).</returns>
        public static List<DiaryEventFilterSnapshot> GetEventFilters()
        {
            return PawnDiaryApi.GetEventFilters();
        }

        /// <summary>
        /// Reads whether Pawn Diary currently captures one event kind, by event-filter group defName.
        /// </summary>
        /// <param name="key">Event-filter group defName (from <see cref="GetEventFilters"/>).</param>
        /// <returns>True when the group is captured; false when disabled, unknown, or not allowed.</returns>
        public static bool IsEventFilterEnabled(string key)
        {
            return PawnDiaryApi.IsEventFilterEnabled(key);
        }

        /// <summary>
        /// Enables or disables automatic capture for one event-filter group, using the same saved flag
        /// as the settings Events tab. Persists immediately.
        /// </summary>
        /// <param name="key">Event-filter group defName (from <see cref="GetEventFilters"/>).</param>
        /// <param name="enabled">True to capture this event kind; false to stop capturing it.</param>
        /// <returns>True when applied; false for an unknown key or a call that is not allowed.</returns>
        public static bool SetEventFilterEnabled(string key, bool enabled)
        {
            return PawnDiaryApi.SetEventFilterEnabled(key, enabled);
        }
    }
}
