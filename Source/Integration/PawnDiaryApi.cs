// The PUBLIC integration facade — the ONE class other mods are allowed to call. Everything else in
// this assembly is an internal implementation detail and may change between versions; this surface
// only ever evolves additively (new members, never renames/removals). See INTEGRATIONS.md for the
// full contract, eventKey conventions, and the group-XML an adapter must ship.
//
// Design rules for this file:
//   • Never throw into a caller: an adapter bug or a Pawn in a weird state must not break the
//     game loop, so every entry point is wrapped and failures are logged once, attributed to the
//     submitting mod's sourceId.
//   • Main-thread only: the pipeline reads DefDatabase/settings/tick state and .Translate() is not
//     thread-safe, so off-thread calls are rejected with a log instead of racing.
//   • Reflection-friendly: static methods with simple parameter types, so a mod that wants a soft
//     (no compile-time reference) integration can call via AccessTools without gymnastics.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Public entry point for other mods. Current surface: readiness, inbound events with optional
    /// handles, direct caller-authored entries, lifecycle status listeners, eligibility probes,
    /// writing-style catalog reads and overrides, per-pawn generation controls, prompt previews,
    /// wrapped prompt-entry submissions, read-only snapshots/status reads, single-entry reads, cheap
    /// entry stats, compact prose context bundles, prompt fragments/enchantment candidates on
    /// external events, and pawn-context providers for prompt-summary context.
    /// </summary>
    public static class PawnDiaryApi
    {
        /// <summary>
        /// Contract version of this API surface. Bumped only when members are ADDED; existing
        /// members never change behavior incompatibly. Adapters that need a newer member can check
        /// this at load time and degrade gracefully on older Pawn Diary builds.
        /// </summary>
        public const int ApiVersion = 23;

        /// <summary>
        /// True while a game is loaded and the diary component is alive — the only time
        /// <see cref="SubmitEvent"/> can record anything. Safe to call at any moment (menus, loading
        /// screens); it simply returns false outside play.
        /// </summary>
        public static bool IsReady
        {
            get { return DiaryGameComponent.GamePlaying && DiaryGameComponent.Instance != null; }
        }

        /// <summary>
        /// Returns whether this pawn can currently own normal first-person diary entries through the
        /// public integration surface. This includes Pawn Diary's base owner eligibility rules and the
        /// saved per-pawn generation-enabled flag controlled by the Diary tab / dev panel.
        /// </summary>
        public static bool IsDiaryEligible(Pawn pawn)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: IsDiaryEligible was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.IsDiaryEligible.OffThread".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.IsIntegrationEligibleFor(pawn);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: IsDiaryEligible for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.IsDiaryEligible.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Returns whether the pawn's saved diary-generation toggle is enabled. This is the same
        /// per-pawn flag controlled by the Diary tab / dev panel and defaults to true for an
        /// otherwise eligible pawn with no saved diary record yet.
        /// </summary>
        public static bool IsDiaryGenerationEnabled(Pawn pawn)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: IsDiaryGenerationEnabled was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.IsDiaryGenerationEnabled.OffThread".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.DiaryGenerationEnabledForIntegration(pawn);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: IsDiaryGenerationEnabled for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.IsDiaryGenerationEnabled.Exception." + pawnForLog + "."
                        + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Sets the pawn's saved diary-generation toggle. Disabling blocks future generated diary
        /// prose for that pawn; re-enabling queues any pending generation work for the pawn again.
        /// </summary>
        public static bool SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetDiaryGenerationEnabled was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.SetDiaryGenerationEnabled.OffThread".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.TrySetDiaryGenerationEnabledForIntegration(pawn, enabled);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetDiaryGenerationEnabled for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.SetDiaryGenerationEnabled.Exception." + pawnForLog + "."
                        + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Builds a side-effect-free preview of the assembled prompt for one ordinary external event
        /// request. No diary event is saved, no generation is queued, no tokens are spent, and RNG
        /// state is restored after live prompt policy is sampled.
        /// </summary>
        public static DiaryPromptPreviewSnapshot PreviewPrompt(ExternalEventRequest request, string povRole = null)
        {
            string sourceId = SourceIdFor(request);
            string eventKey = EventKeyFor(request);
            try
            {
                if (!TryPrepareExternalEventRequest(request, "PreviewPrompt", out sourceId, out eventKey))
                {
                    return null;
                }

                return DiaryGameComponent.Instance.PreviewExternalEventPrompt(request, povRole);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: PreviewPrompt from '" + sourceId
                    + "' for eventKey '" + eventKey + "' failed: " + e,
                    ("PawnDiary.Api.PreviewPrompt.Exception." + sourceId + "."
                        + eventKey + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Builds a side-effect-free preview for a wrapped prompt-entry request. The caller supplies
        /// the entry instruction, but Pawn Diary still assembles the normal persona, safety, style,
        /// context, and response wrapper around it.
        /// </summary>
        public static DiaryPromptPreviewSnapshot PreviewPrompt(ExternalPromptEntryRequest request, string povRole = null)
        {
            string sourceId = SourceIdFor(request);
            string eventKey = EventKeyFor(request);
            try
            {
                if (!TryPrepareExternalPromptEntryRequest(request, "PreviewPromptEntry", out sourceId, out eventKey))
                {
                    return null;
                }

                return DiaryGameComponent.Instance.PreviewExternalEventPrompt(request, povRole);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: PreviewPromptEntry from '" + sourceId
                    + "' for eventKey '" + eventKey + "' failed: " + e,
                    ("PawnDiary.Api.PreviewPromptEntry.Exception." + sourceId + "."
                        + eventKey + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Submits one external event into the diary pipeline. Returns true when the request passed
        /// validation and was handed to the pipeline; the pipeline may still decline it afterwards
        /// (group disabled in XML, ineligible pawn, dedup window) exactly like a native event.
        /// Returns false — never throws — for: null/incomplete request, no game loaded, off-thread
        /// call, or an eventKey no External-domain DiaryInteractionGroupDef claims.
        /// </summary>
        public static bool SubmitEvent(ExternalEventRequest request)
        {
            return SubmitEvent(request, out SubmitEventOutcome _);
        }

        /// <summary>
        /// Same as <see cref="SubmitEvent(ExternalEventRequest)"/> but reports a public outcome so an
        /// adapter can tell apart the distinct reasons a submission did not record (invalid request,
        /// off-thread call, ineligible, budget-exhausted, or dropped by the pipeline) instead of
        /// collapsing them into one boolean. Returns true when the event was recorded; otherwise sets
        /// <paramref name="outcome"/> and returns false. Never throws. API v23.
        /// </summary>
        public static bool SubmitEvent(ExternalEventRequest request, out SubmitEventOutcome outcome)
        {
            outcome = SubmitEventOutcome.InvalidRequest;
            try
            {
                if (!TryPrepareExternalEventRequest(request, "SubmitEvent", out _, out _, out outcome))
                {
                    return false;
                }

                if (!DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                    request, "SubmitEvent", out ExternalApiBudgetReservation reservation))
                {
                    outcome = SubmitEventOutcome.DroppedBudget;
                    return false;
                }

                // Dispatch directly (not fire-and-forget DiaryEvents.Submit) so a deduped/policy-dropped
                // event refunds its budget reservation instead of burning the adapter's window. The
                // outcome distinguishes "dispatched and recorded" from "handed to the pipeline, which
                // then declined it (group disabled / dedup / pawn state)" — the latter mirrors
                // SubmitEventWithHandle's recorded=false branch.
                bool emitted = DiaryGameComponent.Instance.Dispatch(new ExternalEventSignal(request));
                if (!emitted)
                {
                    DiaryGameComponent.Instance.ReleaseExternalApiBudgetReservation(reservation);
                    outcome = SubmitEventOutcome.DroppedByPipeline;
                    return false;
                }

                outcome = SubmitEventOutcome.Recorded;
                return true;
            }
            catch (Exception e)
            {
                string sourceForLog = request != null && !string.IsNullOrWhiteSpace(request.sourceId)
                    ? request.sourceId
                    : "unknown-source";
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitEvent from '" + sourceForLog + "' failed: " + e,
                    ("PawnDiary.Api.Exception." + sourceForLog).GetHashCode());
                outcome = SubmitEventOutcome.InvalidRequest;
                return false;
            }
        }

        /// <summary>
        /// Submits one external event and returns stable handles when the pipeline actually creates an
        /// entry. Unlike <see cref="SubmitEvent"/>, a valid-but-deduped or policy-dropped request
        /// returns a result with <c>recorded=false</c> instead of only saying it was handed off.
        /// </summary>
        public static DiaryEventSubmissionResult SubmitEventWithHandle(ExternalEventRequest request)
        {
            string sourceId = SourceIdFor(request);
            string eventKey = EventKeyFor(request);
            try
            {
                if (!TryPrepareExternalEventRequest(request, "SubmitEventWithHandle", out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                    request, "SubmitEventWithHandle", out ExternalApiBudgetReservation reservation))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                ExternalEventSignal signal = new ExternalEventSignal(request);
                bool emitted = DiaryGameComponent.Instance.Dispatch(signal);
                if (!emitted)
                {
                    DiaryGameComponent.Instance.ReleaseExternalApiBudgetReservation(reservation);
                }

                return SubmissionResultFor(sourceId, eventKey, emitted, signal);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitEventWithHandle from '" + sourceId + "' failed: " + e,
                    ("PawnDiary.Api.HandleSubmit.Exception." + sourceId).GetHashCode());
                return EmptySubmissionResult(sourceId, eventKey);
            }
        }

        /// <summary>
        /// Submits one wrapped prompt-entry request and returns stable handles when the pipeline
        /// actually creates an entry. This queues normal Pawn Diary generation with the supplied
        /// instruction placed inside protected prompt context; it is not a raw system-prompt escape.
        /// </summary>
        public static DiaryEventSubmissionResult SubmitPromptEntry(ExternalPromptEntryRequest request)
        {
            string sourceId = SourceIdFor(request);
            string eventKey = EventKeyFor(request);
            try
            {
                if (!TryPrepareExternalPromptEntryRequest(request, "SubmitPromptEntry", out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                    request, "SubmitPromptEntry", out ExternalApiBudgetReservation reservation))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                ExternalEventSignal signal = new ExternalEventSignal(request, false);
                bool emitted = DiaryGameComponent.Instance.Dispatch(signal);
                if (!emitted)
                {
                    DiaryGameComponent.Instance.ReleaseExternalApiBudgetReservation(reservation);
                }

                return SubmissionResultFor(sourceId, eventKey, emitted, signal);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitPromptEntry from '" + sourceId + "' failed: " + e,
                    ("PawnDiary.Api.SubmitPromptEntry.Exception." + sourceId).GetHashCode());
                return EmptySubmissionResult(sourceId, eventKey);
            }
        }

        /// <summary>
        /// Submits caller-authored final diary prose and returns handles when an entry was actually
        /// saved. This does not queue the main LLM rewrite; optional title generation may still run
        /// when requested and enabled by the player.
        /// </summary>
        public static DiaryEventSubmissionResult SubmitDirectEntry(ExternalDirectEntryRequest request)
        {
            string sourceId = SourceIdFor(request);
            string eventKey = EventKeyFor(request);
            try
            {
                if (!TryPrepareExternalDirectEntryRequest(request, out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!DiaryGameComponent.Instance.TryReserveExternalApiBudgetForDirectEntry(
                    request, out ExternalApiBudgetReservation reservation))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                ExternalDirectEntrySignal signal = new ExternalDirectEntrySignal(request);
                bool emitted = DiaryGameComponent.Instance.Dispatch(signal);
                if (!emitted)
                {
                    DiaryGameComponent.Instance.ReleaseExternalApiBudgetReservation(reservation);
                }

                return SubmissionResultFor(sourceId, eventKey, emitted, signal);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitDirectEntry from '" + sourceId + "' failed: " + e,
                    ("PawnDiary.Api.DirectEntry.Exception." + sourceId).GetHashCode());
                return EmptySubmissionResult(sourceId, eventKey);
            }
        }

        /// <summary>
        /// Registers or replaces a process-global listener for entry lifecycle snapshots. The listener
        /// runs on the main thread after Pawn Diary changes a saved POV's main or title status.
        /// Registration is safe before a game loads; invalid/off-thread calls are logged and ignored.
        /// </summary>
        public static void RegisterEntryStatusListener(string id, Action<DiaryEntryStatusSnapshot> listener)
        {
            try
            {
                string listenerId = string.IsNullOrWhiteSpace(id) ? "unknown-listener" : id.Trim();
                if (string.IsNullOrWhiteSpace(id) || listener == null)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterEntryStatusListener was called with a missing id or listener.",
                        ("PawnDiary.Api.EntryStatusListener.Invalid." + listenerId).GetHashCode());
                    return;
                }

                // Registration mutates a process-global registry. Keep the same main-thread rule as
                // pawn-context providers so later notification walks never race modifications.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterEntryStatusListener for '" + listenerId
                        + "' was called off the main thread; the listener was ignored.",
                        ("PawnDiary.Api.EntryStatusListener.OffThread." + listenerId).GetHashCode());
                    return;
                }

                if (!EntryStatusListeners.Register(listenerId, listener))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterEntryStatusListener for '" + listenerId
                        + "' could not be registered; the listener registry may be full.",
                        ("PawnDiary.Api.EntryStatusListener.Rejected." + listenerId).GetHashCode());
                }
            }
            catch (Exception e)
            {
                string listenerForLog = string.IsNullOrWhiteSpace(id) ? "unknown-listener" : id.Trim();
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: RegisterEntryStatusListener for '" + listenerForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.EntryStatusListener.Exception." + listenerForLog).GetHashCode());
            }
        }

        /// <summary>
        /// Removes a previously registered entry-status listener id. Missing ids are a no-op.
        /// </summary>
        public static void UnregisterEntryStatusListener(string id)
        {
            try
            {
                string listenerId = string.IsNullOrWhiteSpace(id) ? "unknown-listener" : id.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: UnregisterEntryStatusListener was called with a missing id.",
                        "PawnDiary.Api.EntryStatusListener.Unregister.Invalid".GetHashCode());
                    return;
                }

                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: UnregisterEntryStatusListener for '" + listenerId
                        + "' was called off the main thread; the call was ignored.",
                        ("PawnDiary.Api.EntryStatusListener.Unregister.OffThread." + listenerId).GetHashCode());
                    return;
                }

                EntryStatusListeners.Unregister(listenerId);
            }
            catch (Exception e)
            {
                string listenerForLog = string.IsNullOrWhiteSpace(id) ? "unknown-listener" : id.Trim();
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: UnregisterEntryStatusListener for '" + listenerForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.EntryStatusListener.Unregister.Exception." + listenerForLog).GetHashCode());
            }
        }

        /// <summary>
        /// Returns the current generation status for a handled entry, or null when the handle is
        /// missing, no game is loaded, the entry was pruned, or the call is off-thread.
        /// </summary>
        public static DiaryEntryStatusSnapshot GetEntryStatus(DiaryEntryHandle handle)
        {
            if (handle == null)
            {
                return null;
            }

            return GetEntryStatus(handle.eventId, handle.povRole);
        }

        /// <summary>
        /// Returns the current generation status for one event id and POV role. The id/role pair is the
        /// stable public handle; the returned snapshot still omits prompts and raw provider responses.
        /// </summary>
        public static DiaryEntryStatusSnapshot GetEntryStatus(string eventId, string povRole)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetEntryStatus was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.EntryStatus.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady
                    || string.IsNullOrWhiteSpace(eventId)
                    || string.IsNullOrWhiteSpace(povRole))
                {
                    return null;
                }

                return DiaryGameComponent.Instance.EntryStatusFor(eventId, povRole);
            }
            catch (Exception e)
            {
                string entryForLog = (eventId ?? "unknown-event") + "|" + (povRole ?? "unknown-role");
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetEntryStatus for '" + entryForLog + "' failed: " + e,
                    ("PawnDiary.Api.EntryStatus.Exception." + entryForLog + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns one diary entry snapshot by integration handle. The snapshot exposes completed
        /// player-visible prose and compact metadata only; prompts and raw provider responses stay
        /// internal. Returns null when the handle is missing, no game is loaded, the entry was pruned,
        /// or the call is off-thread.
        /// </summary>
        public static DiaryEntrySnapshot GetEntrySnapshot(DiaryEntryHandle handle)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetEntrySnapshot was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.EntrySnapshot.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || handle == null
                    || string.IsNullOrWhiteSpace(handle.eventId)
                    || string.IsNullOrWhiteSpace(handle.povRole))
                {
                    return null;
                }

                return DiaryGameComponent.Instance.EntrySnapshotFor(handle);
            }
            catch (Exception e)
            {
                string entryForLog = handle == null
                    ? "unknown-entry"
                    : (handle.eventId ?? "unknown-event") + "|" + (handle.povRole ?? "unknown-role");
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetEntrySnapshot for '" + entryForLog + "' failed: " + e,
                    ("PawnDiary.Api.EntrySnapshot.Exception." + entryForLog + "."
                        + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns one diary entry snapshot by event id and POV role. This mirrors
        /// <see cref="GetEntryStatus(string, string)"/> but includes completed player-visible prose.
        /// </summary>
        public static DiaryEntrySnapshot GetEntrySnapshot(string eventId, string povRole)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetEntrySnapshot was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.EntrySnapshot.ById.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady
                    || string.IsNullOrWhiteSpace(eventId)
                    || string.IsNullOrWhiteSpace(povRole))
                {
                    return null;
                }

                return DiaryGameComponent.Instance.EntrySnapshotFor(eventId, povRole);
            }
            catch (Exception e)
            {
                string entryForLog = (eventId ?? "unknown-event") + "|" + (povRole ?? "unknown-role");
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetEntrySnapshot for '" + entryForLog + "' failed: " + e,
                    ("PawnDiary.Api.EntrySnapshot.ById.Exception." + entryForLog + "."
                        + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns newest completed diary-page titles for one pawn, newest first. This is intentionally
        /// a narrow snapshot: no prompts, raw responses, or mutable game objects cross the integration
        /// boundary. Returns an empty list for invalid input, no game, off-thread calls, or failures.
        /// </summary>
        public static List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn pawn, int maxCount)
        {
            try
            {
                // The reader walks saved game state and display helpers, so keep the same main-thread
                // rule as SubmitEvent. Adapters that listen on a worker thread should marshal first.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetRecentEntryTitles was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.RecentTitles.OffThread".GetHashCode());
                    return new List<DiaryEntryTitleSnapshot>();
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null || maxCount <= 0)
                {
                    return new List<DiaryEntryTitleSnapshot>();
                }

                return DiaryGameComponent.Instance.RecentEntryTitleSnapshotsFor(pawn, maxCount);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetRecentEntryTitles failed: " + e,
                    "PawnDiary.Api.RecentTitles.Exception".GetHashCode());
                return new List<DiaryEntryTitleSnapshot>();
            }
        }

        /// <summary>
        /// Returns a pawn's base saved diary writing style as a small read-only snapshot, or null.
        /// This publishes the diary's own voice instruction (the <c>rule</c> field) so a chat/context
        /// mod can, if its player chooses, align its own voice with how the pawn writes — Pawn Diary
        /// only exposes the style, it never reads or drives another mod's persona. Returns null —
        /// never throws — for a null/ineligible pawn, no game loaded, or an off-thread call. The
        /// snapshot is the base saved style and does not include temporary hediff style overrides.
        /// </summary>
        public static DiaryWritingStyleSnapshot GetWritingStyle(Pawn pawn)
        {
            try
            {
                // Same main-thread rule as the other readers: it walks saved state and Def text.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetWritingStyle was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.WritingStyle.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.WritingStyleSnapshotFor(pawn);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetWritingStyle failed: " + e,
                    "PawnDiary.Api.WritingStyle.Exception".GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns the effective writing-style catalog as read-only snapshots. The list includes XML
        /// styles plus settings-backed edits/custom rows, matching the choices Pawn Diary itself can
        /// assign. It never creates pawn diary state.
        /// </summary>
        public static List<DiaryWritingStyleSnapshot> GetAvailableWritingStyles()
        {
            try
            {
                // The catalog walks DefDatabase and settings-backed custom rows, so keep the same
                // main-thread rule as the other writing-style reads.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetAvailableWritingStyles was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.AvailableWritingStyles.OffThread".GetHashCode());
                    return new List<DiaryWritingStyleSnapshot>();
                }

                if (!ExternalIntegrationsAllowed || !IsReady)
                {
                    return new List<DiaryWritingStyleSnapshot>();
                }

                IReadOnlyList<DiaryPersonaDef> personas = DiaryPersonas.All;
                List<DiaryWritingStyleSnapshot> snapshots = new List<DiaryWritingStyleSnapshot>(
                    personas == null ? 0 : personas.Count);
                if (personas == null)
                {
                    return snapshots;
                }

                for (int i = 0; i < personas.Count; i++)
                {
                    DiaryPersonaDef persona = personas[i];
                    if (persona == null)
                    {
                        continue;
                    }

                    snapshots.Add(new DiaryWritingStyleSnapshot
                    {
                        styleDefName = persona.defName ?? string.Empty,
                        label = persona.label ?? string.Empty,
                        rule = persona.rule ?? string.Empty
                    });
                }

                return snapshots;
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetAvailableWritingStyles failed: " + e,
                    "PawnDiary.Api.AvailableWritingStyles.Exception".GetHashCode());
                return new List<DiaryWritingStyleSnapshot>();
            }
        }

        /// <summary>
        /// Saves a source-owned free-form writing-style rule for a pawn. The override sits above the
        /// pawn's base style and temporary hediff style until the same source resets it.
        /// </summary>
        public static bool SetWritingStyleOverride(Pawn pawn, string sourceId, string rule)
        {
            string cleanedSource = ExternalWritingStyleOverrideText.CleanSourceId(sourceId);
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetWritingStyleOverride was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.SetWritingStyleOverride.OffThread".GetHashCode());
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cleanedSource))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetWritingStyleOverride was called with a missing sourceId.",
                        "PawnDiary.Api.SetWritingStyleOverride.MissingSource".GetHashCode());
                    return false;
                }

                string cleanedRule = ExternalWritingStyleOverrideText.CleanRule(rule);
                if (string.IsNullOrWhiteSpace(cleanedRule))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetWritingStyleOverride from '" + cleanedSource
                        + "' cleaned to a blank rule; the call was ignored.",
                        ("PawnDiary.Api.SetWritingStyleOverride.BlankRule." + cleanedSource).GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.SetExternalWritingStyleOverride(
                    pawn,
                    cleanedSource,
                    cleanedRule);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetWritingStyleOverride from '" + cleanedSource
                    + "' for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.SetWritingStyleOverride.Exception." + cleanedSource + "."
                        + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Clears a source-owned writing-style override. Returns true when no override remains, false
        /// when the call is invalid or another source owns the active override.
        /// </summary>
        public static bool ResetWritingStyleOverride(Pawn pawn, string sourceId)
        {
            string cleanedSource = ExternalWritingStyleOverrideText.CleanSourceId(sourceId);
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: ResetWritingStyleOverride was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.ResetWritingStyleOverride.OffThread".GetHashCode());
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cleanedSource))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: ResetWritingStyleOverride was called with a missing sourceId.",
                        "PawnDiary.Api.ResetWritingStyleOverride.MissingSource".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.ResetExternalWritingStyleOverride(pawn, cleanedSource);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: ResetWritingStyleOverride from '" + cleanedSource
                    + "' for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.ResetWritingStyleOverride.Exception." + cleanedSource + "."
                        + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Returns newest completed diary-page titles for one pawn, filtered by a plain query. The
        /// original v2 overload remains unchanged; this v5 overload only narrows which snapshots are
        /// returned and still never exposes prompts, raw responses, or live game objects.
        /// </summary>
        public static List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(
            Pawn pawn,
            int maxCount,
            DiaryEntryTitleQuery query)
        {
            try
            {
                // The reader walks saved game state and display helpers, so keep the same main-thread
                // rule as SubmitEvent. Adapters that listen on a worker thread should marshal first.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: filtered GetRecentEntryTitles was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.RecentTitles.Filtered.OffThread".GetHashCode());
                    return new List<DiaryEntryTitleSnapshot>();
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null || maxCount <= 0)
                {
                    return new List<DiaryEntryTitleSnapshot>();
                }

                return DiaryGameComponent.Instance.RecentEntryTitleSnapshotsFor(pawn, maxCount, query);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: filtered GetRecentEntryTitles failed: " + e,
                    "PawnDiary.Api.RecentTitles.Filtered.Exception".GetHashCode());
                return new List<DiaryEntryTitleSnapshot>();
            }
        }

        /// <summary>
        /// Returns aggregate counts for one pawn's diary entries without materializing title/prose
        /// snapshot rows. Returns null for invalid input, no game, off-thread calls, or failures.
        /// </summary>
        public static DiaryEntryStatsSnapshot GetEntryStats(Pawn pawn)
        {
            return GetEntryStats(pawn, null);
        }

        /// <summary>
        /// Returns aggregate counts for one pawn's diary entries after applying the same query fields
        /// supported by recent-title and context reads.
        /// </summary>
        public static DiaryEntryStatsSnapshot GetEntryStats(Pawn pawn, DiaryEntryTitleQuery query)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetEntryStats was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.EntryStats.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.EntryStatsFor(pawn, query);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetEntryStats for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.EntryStats.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns recent completed diary prose summaries for one pawn, newest first. This is the
        /// fuller read-only memory surface for chat/context adapters: each entry carries the stored
        /// title plus a one-sentence summary of the player-visible generated prose. Returns null —
        /// never throws — for invalid input, no game, off-thread calls, or failures.
        /// </summary>
        public static DiaryContextSnapshot GetContextSnapshot(Pawn pawn, int maxEntries)
        {
            return GetContextSnapshot(pawn, maxEntries, null);
        }

        /// <summary>
        /// Returns recent completed diary prose summaries for one pawn, filtered by the same query
        /// fields supported by the title-read API. It never exposes prompts, raw responses, or
        /// fallback facts.
        /// </summary>
        public static DiaryContextSnapshot GetContextSnapshot(
            Pawn pawn,
            int maxEntries,
            DiaryEntryTitleQuery query)
        {
            try
            {
                // The reader walks saved diary state and display facts, so keep the same main-thread
                // rule as the other public reads.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetContextSnapshot was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.ContextSnapshot.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null || maxEntries <= 0)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.ContextSnapshotFor(pawn, maxEntries, query);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetContextSnapshot for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.ContextSnapshot.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns the main Pawn Diary context reads for one pawn in one DTO: base writing style,
        /// structured pawn summary, prompt-enchantment candidates, and recent generated-entry context.
        /// </summary>
        public static DiaryContextBundleSnapshot GetContextBundle(Pawn pawn, int maxEntries)
        {
            return GetContextBundle(pawn, maxEntries, null, false);
        }

        /// <summary>
        /// Returns the context bundle while asking the prompt-enchantment export to include the
        /// important-event candidate set.
        /// </summary>
        public static DiaryContextBundleSnapshot GetContextBundle(
            Pawn pawn,
            int maxEntries,
            bool includeImportantEventContext)
        {
            return GetContextBundle(pawn, maxEntries, null, includeImportantEventContext);
        }

        /// <summary>
        /// Returns the context bundle with the recent generated-entry context filtered by the same
        /// query fields supported by the standalone context read.
        /// </summary>
        public static DiaryContextBundleSnapshot GetContextBundle(
            Pawn pawn,
            int maxEntries,
            DiaryEntryTitleQuery query)
        {
            return GetContextBundle(pawn, maxEntries, query, false);
        }

        /// <summary>
        /// Returns the context bundle with filtered recent generated-entry context and explicit
        /// prompt-enchantment candidate scope.
        /// </summary>
        public static DiaryContextBundleSnapshot GetContextBundle(
            Pawn pawn,
            int maxEntries,
            DiaryEntryTitleQuery query,
            bool includeImportantEventContext)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetContextBundle was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.ContextBundle.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null || maxEntries <= 0)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.ContextBundleSnapshotFor(
                    pawn,
                    maxEntries,
                    query,
                    includeImportantEventContext);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetContextBundle for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.ContextBundle.Exception." + pawnForLog + "."
                        + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Registers one external pawn-context provider. The provider is invoked later, on the main
        /// thread during prompt context collection, and may return one <c>key=value</c> line or null.
        /// Re-registering the same id replaces the provider. Registration is safe before a game loads.
        /// </summary>
        public static void RegisterPawnContextProvider(string id, Func<Pawn, string> provider)
        {
            try
            {
                string providerId = string.IsNullOrWhiteSpace(id) ? "unknown-provider" : id.Trim();
                if (string.IsNullOrWhiteSpace(id) || provider == null)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterPawnContextProvider was called with a missing id or provider.",
                        ("PawnDiary.Api.ContextProvider.Invalid." + providerId).GetHashCode());
                    return;
                }

                // Registration mutates a process-global registry and the provider may later touch
                // DefDatabase/Translate-backed data, so keep the same main-thread rule as all API
                // methods that interact with game state.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterPawnContextProvider for '" + providerId
                        + "' was called off the main thread; the provider was ignored.",
                        ("PawnDiary.Api.ContextProvider.OffThread." + providerId).GetHashCode());
                    return;
                }

                PawnContextProviders.Register(providerId, provider);
            }
            catch (Exception e)
            {
                string providerForLog = string.IsNullOrWhiteSpace(id) ? "unknown-provider" : id.Trim();
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: RegisterPawnContextProvider for '" + providerForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.ContextProvider.Exception." + providerForLog).GetHashCode());
            }
        }

        /// <summary>
        /// Returns the structured pawn-summary context Pawn Diary would feed to one of its own
        /// prompts, or null. This is the "machinery as a service" read (capability C-CTX-2): a chat
        /// or context mod can read our understanding of the pawn — sex, life stage, DLC identity,
        /// mood, health, low capacities, top thoughts, and external provider lines — as named DTO
        /// fields rather than the internal <c>key=value</c> blob, so the assembly can keep evolving
        /// the prompt text without breaking the contract. Returns null — never throws — for a
        /// null/ineligible pawn, no game loaded, off-thread call, or when the master integration
        /// toggle is off. Side-effect free: it never creates a diary record, queues generation, or
        /// spends tokens.
        /// </summary>
        public static DiaryPawnSummarySnapshot GetPawnSummary(Pawn pawn)
        {
            try
            {
                // Same main-thread rule as the other readers: it walks live pawn state and Def text.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetPawnSummary was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.PawnSummary.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.PawnSummarySnapshotFor(pawn);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetPawnSummary for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.PawnSummary.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Returns the prompt-enchantment candidates Pawn Diary prepared for a pawn right now
        /// (capability C-CTX-3). This exports the candidate SET the planner chooses among after
        /// suppression, live event/condition candidates, and weight multipliers, but before the
        /// single rolled winner. Pass <paramref name="includeImportantEventContext"/> true to also
        /// collect the DLC social-status candidates (royal title / ideology role) that only enter the
        /// pool for important events, mirroring the prompt-time collection.
        /// Returns an empty list — never throws — for a null pawn, no game, off-thread call, when
        /// the master integration toggle is off, when the player has disabled prompt enchantments
        /// in settings, when the pawn is not diary-eligible, or when no candidates match. Side-effect
        /// free: it preserves global RNG state, does not roll the planner winner, and does not feed a
        /// prompt.
        /// </summary>
        public static List<DiaryPromptEnchantmentCandidateSnapshot> GetPromptEnchantments(
            Pawn pawn,
            bool includeImportantEventContext = false)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetPromptEnchantments was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.PromptEnchantments.OffThread".GetHashCode());
                    return new List<DiaryPromptEnchantmentCandidateSnapshot>();
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return new List<DiaryPromptEnchantmentCandidateSnapshot>();
                }

                return DiaryGameComponent.Instance.PromptEnchantmentCandidatesFor(pawn, includeImportantEventContext);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetPromptEnchantments for pawn '" + pawnForLog
                    + "' failed: " + e,
                    ("PawnDiary.Api.PromptEnchantments.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return new List<DiaryPromptEnchantmentCandidateSnapshot>();
            }
        }

        private static bool TryPrepareExternalEventRequest(
            ExternalEventRequest request,
            string operation,
            out string sourceId,
            out string eventKey)
        {
            return TryPrepareExternalEventRequest(request, operation, out sourceId, out eventKey, out _);
        }

        /// <summary>
        /// Same validation as the 4-param overload, but also reports a public outcome so the v23
        /// <see cref="SubmitEvent(ExternalEventRequest, out SubmitEventOutcome)"/> overload can tell
        /// adapters apart: invalid request, off-thread call, ineligible (no game / master toggle off /
        /// pawn fails owner eligibility), or no External-domain group claiming the key (also folded
        /// into InvalidRequest, since fixing it requires an adapter-side change).
        /// </summary>
        private static bool TryPrepareExternalEventRequest(
            ExternalEventRequest request,
            string operation,
            out string sourceId,
            out string eventKey,
            out SubmitEventOutcome outcome)
        {
            sourceId = SourceIdFor(request);
            eventKey = EventKeyFor(request);
            outcome = SubmitEventOutcome.InvalidRequest;

            if (request == null)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " called with a null request.",
                    ("PawnDiary.Api." + operation + ".NullRequest").GetHashCode());
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.eventKey) || request.subject == null)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " from '" + sourceId
                    + "' is missing a required field (eventKey and subject are mandatory).",
                    ("PawnDiary.Api." + operation + ".Invalid." + sourceId).GetHashCode());
                return false;
            }

            // The pipeline reads DefDatabase/settings/tick state and later .Translate()s text, none of
            // which is safe off the main thread. Reject instead of racing.
            if (!UnityData.IsInMainThread)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " from '" + sourceId
                    + "' was called off the main thread; the call was ignored. Queue the work yourself "
                    + "and drain it from a main-thread hook such as GameComponentUpdate or OnGUI.",
                    ("PawnDiary.Api." + operation + ".OffThread." + sourceId).GetHashCode());
                outcome = SubmitEventOutcome.OffThread;
                return false;
            }

            if (!ExternalIntegrationsAllowed || !IsReady)
            {
                outcome = SubmitEventOutcome.Ineligible;
                return false;
            }

            // Fail loudly-but-once when nobody claims the key: the most common adapter mistake is
            // shipping the C# call without the External-domain group XML, and a silent drop would make
            // that miserable to debug.
            if (InteractionGroups.ClassifyExternal(eventKey) == null)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Integration API: no External-domain DiaryInteractionGroupDef "
                    + "claims eventKey '" + eventKey + "' (submitted by '" + sourceId + "'). "
                    + "The adapter must ship a group def that matches this key; the event was ignored.",
                    ("PawnDiary.Api.UnclaimedKey." + eventKey).GetHashCode());
                // Stays InvalidRequest: an unclaimed key is an adapter-side fix, not a runtime state.
                return false;
            }

            outcome = SubmitEventOutcome.Recorded;
            return true;
        }

        private static bool TryPrepareExternalPromptEntryRequest(
            ExternalPromptEntryRequest request,
            string operation,
            out string sourceId,
            out string eventKey)
        {
            sourceId = SourceIdFor(request);
            eventKey = EventKeyFor(request);

            if (request == null)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " called with a null request.",
                    ("PawnDiary.Api." + operation + ".NullRequest").GetHashCode());
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.eventKey)
                || request.subject == null
                || string.IsNullOrWhiteSpace(request.promptInstruction))
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " from '" + sourceId
                    + "' is missing a required field (eventKey, subject, and promptInstruction are mandatory).",
                    ("PawnDiary.Api." + operation + ".Invalid." + sourceId).GetHashCode());
                return false;
            }

            // Wrapped prompt entries still touch Def/settings/tick state and translated fallback
            // text, so adapters must marshal calls back to RimWorld's main thread.
            if (!UnityData.IsInMainThread)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " from '" + sourceId
                    + "' was called off the main thread; the call was ignored. Queue the work yourself "
                    + "and drain it from a main-thread hook such as GameComponentUpdate or OnGUI.",
                    ("PawnDiary.Api." + operation + ".OffThread." + sourceId).GetHashCode());
                return false;
            }

            if (!ExternalIntegrationsAllowed || !IsReady)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(ExternalEventRequestText.CleanPromptInstruction(request.promptInstruction)))
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: " + operation + " from '" + sourceId
                    + "' cleaned to a blank promptInstruction; the entry was ignored.",
                    ("PawnDiary.Api." + operation + ".BlankPromptInstruction." + sourceId).GetHashCode());
                return false;
            }

            return true;
        }

        private static bool TryPrepareExternalDirectEntryRequest(
            ExternalDirectEntryRequest request,
            out string sourceId,
            out string eventKey)
        {
            sourceId = SourceIdFor(request);
            eventKey = EventKeyFor(request);

            if (request == null)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitDirectEntry called with a null request.",
                    "PawnDiary.Api.SubmitDirectEntry.NullRequest".GetHashCode());
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.eventKey)
                || request.subject == null
                || string.IsNullOrWhiteSpace(request.text))
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitDirectEntry from '" + sourceId
                    + "' is missing a required field (eventKey, subject, and text are mandatory).",
                    ("PawnDiary.Api.SubmitDirectEntry.Invalid." + sourceId).GetHashCode());
                return false;
            }

            // The signal writes into saved diary state, reads Def/settings/tick state, and may use
            // translated fallback text, so keep the same main-thread rule as SubmitEvent.
            if (!UnityData.IsInMainThread)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitDirectEntry from '" + sourceId
                    + "' was called off the main thread; the call was ignored. Queue the work yourself "
                    + "and drain it from a main-thread hook such as GameComponentUpdate or OnGUI.",
                    ("PawnDiary.Api.SubmitDirectEntry.OffThread." + sourceId).GetHashCode());
                return false;
            }

            if (!ExternalIntegrationsAllowed || !IsReady)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                ExternalDirectEntryText.CleanProse(request.text, DiaryTuning.IntegrationDirectTextMaxChars)))
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SubmitDirectEntry from '" + sourceId
                    + "' cleaned to blank text; the entry was ignored.",
                    ("PawnDiary.Api.SubmitDirectEntry.BlankText." + sourceId).GetHashCode());
                return false;
            }

            return true;
        }

        private static DiaryEventSubmissionResult SubmissionResultFor(
            string sourceId,
            string eventKey,
            bool emitted,
            ExternalEventSignal signal)
        {
            DiaryEventSubmissionResult result = EmptySubmissionResult(sourceId, eventKey);
            DiaryEvent diaryEvent = signal?.CreatedEvent;
            if (!emitted || diaryEvent == null)
            {
                return result;
            }

            result.primary = DiaryGameComponent.BuildEntryHandle(diaryEvent, DiaryEvent.InitiatorRole);
            if (signal.CreatedPairwise)
            {
                result.partner = DiaryGameComponent.BuildEntryHandle(diaryEvent, DiaryEvent.RecipientRole);
            }

            result.recorded = result.primary != null;
            result.pairwise = result.partner != null;
            return result;
        }

        private static DiaryEventSubmissionResult SubmissionResultFor(
            string sourceId,
            string eventKey,
            bool emitted,
            ExternalDirectEntrySignal signal)
        {
            DiaryEventSubmissionResult result = EmptySubmissionResult(sourceId, eventKey);
            DiaryEvent diaryEvent = signal?.CreatedEvent;
            if (!emitted || diaryEvent == null)
            {
                return result;
            }

            result.primary = DiaryGameComponent.BuildEntryHandle(diaryEvent, DiaryEvent.InitiatorRole);
            if (signal.CreatedPairwise)
            {
                result.partner = DiaryGameComponent.BuildEntryHandle(diaryEvent, DiaryEvent.RecipientRole);
            }

            result.recorded = result.primary != null;
            result.pairwise = result.partner != null;
            return result;
        }

        private static DiaryEventSubmissionResult EmptySubmissionResult(string sourceId, string eventKey)
        {
            return new DiaryEventSubmissionResult
            {
                sourceId = sourceId ?? string.Empty,
                eventKey = eventKey ?? string.Empty,
                recorded = false,
                pairwise = false
            };
        }

        private static string SourceIdFor(ExternalEventRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.sourceId)
                ? "unknown-source"
                : request.sourceId.Trim();
        }

        private static string EventKeyFor(ExternalEventRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.eventKey)
                ? string.Empty
                : request.eventKey.Trim();
        }

        private static string SourceIdFor(ExternalDirectEntryRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.sourceId)
                ? "unknown-source"
                : request.sourceId.Trim();
        }

        private static string EventKeyFor(ExternalDirectEntryRequest request)
        {
            return request == null || string.IsNullOrWhiteSpace(request.eventKey)
                ? string.Empty
                : request.eventKey.Trim();
        }

        private static bool ExternalIntegrationsAllowed
        {
            get
            {
                return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.allowExternalIntegrations;
            }
        }

        // A misbehaving adapter can call any of these entry points from a worker thread. RimWorld's
        // Log.* is main-thread only — it mutates the message queue the in-game log window enumerates
        // during OnGUI, so using it to report the very off-thread call we are rejecting would race that
        // enumeration ("Collection was modified"), the same reason LlmClient marshals its debug lines.
        // On the main thread we keep Log.ErrorOnce (in-game log entry + built-in de-dup); off it we
        // fall back to the thread-safe UnityEngine.Debug and keep our own once-per-key guard.
        private static readonly HashSet<int> offThreadLoggedKeys = new HashSet<int>();

        private static void ApiLogErrorOnce(string message, int key)
        {
            if (UnityData.IsInMainThread)
            {
                Log.ErrorOnce(message, key);
                return;
            }

            lock (offThreadLoggedKeys)
            {
                if (!offThreadLoggedKeys.Add(key))
                {
                    return;
                }
            }

            UnityEngine.Debug.LogError(message);
        }

        private static string PawnIdForLog(Pawn pawn)
        {
            if (pawn == null)
            {
                return "null-pawn";
            }

            try
            {
                string pawnId = pawn.GetUniqueLoadID();
                return string.IsNullOrWhiteSpace(pawnId) ? "unknown-pawn" : pawnId;
            }
            catch
            {
                return "unknown-pawn";
            }
        }
    }
}
