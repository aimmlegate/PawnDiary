// Example-adapter-derived Pawn Diary integration layer for the RimTalk bridge.
//
// This is the only bridge file that calls PawnDiaryApi directly. The current reset keeps it as a
// compile-safe scaffold: it registers the same kind of process-global hooks shown by the example
// adapter, but the hook bodies are inert until the RimTalk bridge plan wires real persona/context
// data into them.
//
// New to C#/RimWorld? See AGENTS.md. For the public contract, see INTEGRATIONS.md / EXTERNAL_API.md.
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Facade over <see cref="PawnDiaryApi"/> used by the RimTalk bridge. Keeping calls here makes the
    /// later RimTalk hook code easier to audit and keeps the adapter boundary explicit.
    /// </summary>
    internal static class PawnDiaryRimTalkBridgeApi
    {
        /// <summary>Stable source id used for log attribution and source-owned overrides.</summary>
        public const string SourceId = "aimmlegate.pawndiary.rimtalkbridge";

        /// <summary>Stable event key planned for important RimTalk conversation submissions.</summary>
        public const string ConversationEventKey = "rimtalkbridge_conversation";

        /// <summary>Stable hook id reserved for RimTalk persona/context provider registration.</summary>
        private const string ProviderId = SourceId + ".context_provider";

        /// <summary>Stable hook id for entry-status listener registration.</summary>
        private const string ListenerId = SourceId + ".status_listener";

        /// <summary>Tracks whether the process-global bridge hooks have already been registered.</summary>
        private static bool hooksRegistered;

        /// <summary>Developer diagnostic count of status snapshots seen by this scaffold.</summary>
        private static int statusSnapshotsSeen;

        /// <summary>Last compact status snapshot description observed by the scaffold listener.</summary>
        private static string lastStatusDescription;

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
        /// Gets how many entry-status snapshots this scaffold listener has seen in the current process.
        /// </summary>
        public static int StatusSnapshotsSeen
        {
            get { return statusSnapshotsSeen; }
        }

        /// <summary>
        /// Gets the last compact entry-status description observed by the scaffold listener.
        /// </summary>
        public static string LastStatusDescription
        {
            get { return lastStatusDescription; }
        }

        /// <summary>
        /// Registers the bridge's entry-status listener and placeholder context provider once per process.
        /// </summary>
        /// <remarks>
        /// Call from a main-thread game-load hook, such as a GameComponent constructor. Registration is
        /// safe while the player master switch is off; Pawn Diary gates later invocation.
        /// </remarks>
        public static void RegisterHooksOnce()
        {
            if (hooksRegistered)
            {
                return;
            }

            PawnDiaryApi.RegisterPawnContextProvider(ProviderId, PlaceholderContextLine);
            PawnDiaryApi.RegisterEntryStatusListener(ListenerId, OnEntryStatus);
            hooksRegistered = true;
        }

        /// <summary>
        /// Placeholder for the planned RimTalk persona/context provider.
        /// </summary>
        /// <param name="pawn">Pawn being summarized. Pawn Diary supplies this on the main thread.</param>
        /// <returns>Null until the RimTalk persona sync step supplies a real provider line.</returns>
        private static string PlaceholderContextLine(Pawn pawn)
        {
            return null;
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

            statusSnapshotsSeen++;
            lastStatusDescription = (snapshot.date ?? "?") + "  " + (snapshot.groupLabel ?? "?")
                + "  status=" + (snapshot.status ?? "?")
                + "  title=" + (string.IsNullOrEmpty(snapshot.title) ? "(none)" : snapshot.title);
        }

        /// <summary>
        /// Builds the planned prompt-entry request shape for an important RimTalk conversation.
        /// </summary>
        /// <param name="subject">Pawn whose diary should receive the entry. Required for recording.</param>
        /// <param name="partner">Optional conversation partner for pairwise POV entries.</param>
        /// <param name="summaryText">Localized factual summary line prepared by the caller.</param>
        /// <param name="promptInstruction">Localized caller instruction for this conversation entry.</param>
        /// <param name="extraContext">Optional compact transcript/context lines.</param>
        /// <param name="dedupKey">Optional stable dedup key for this RimTalk conversation chain.</param>
        /// <returns>A request with the bridge source id and frozen RimTalk conversation event key.</returns>
        public static ExternalPromptEntryRequest BuildConversationPromptRequest(
            Pawn subject,
            Pawn partner,
            string summaryText,
            string promptInstruction,
            List<string> extraContext,
            string dedupKey)
        {
            return new ExternalPromptEntryRequest
            {
                sourceId = SourceId,
                eventKey = ConversationEventKey,
                subject = subject,
                partner = partner,
                summaryText = summaryText,
                promptInstruction = promptInstruction,
                extraContext = extraContext,
                dedupKey = dedupKey
            };
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
    }
}
