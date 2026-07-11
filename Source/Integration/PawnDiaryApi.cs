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
    /// external events, pawn-context providers for prompt-summary context, LLM API setup reads
    /// plus add-lane writes, automatic-capture event-filter reads plus per-group toggles, editable
    /// base/custom psychotype setters, one-shot LLM completions on a chosen lane, and an external
    /// psychotype-generator hook that gives the voice editor a Regenerate button + loading status.
    /// </summary>
    public static class PawnDiaryApi
    {
        /// <summary>
        /// Contract version of this API surface. Bumped only when members are ADDED; existing
        /// members never change behavior incompatibly. Adapters that need a newer member can check
        /// this at load time and degrade gracefully on older Pawn Diary builds.
        /// </summary>
        public const int ApiVersion = 5;

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
        /// True when the player has enabled Pawn Diary's public integration API in mod settings.
        /// This is separate from <see cref="IsReady"/>: menus/loading screens can be "enabled but not
        /// ready", while an active game can be ready but globally disabled by the player.
        /// </summary>
        public static bool IsExternalApiEnabled
        {
            get { return ExternalIntegrationsAllowed; }
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
        /// (pawn state or dedup window) exactly like a native event.
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
        /// <paramref name="outcome"/> and returns false. Never throws. Requests can set
        /// <see cref="ExternalEventRequest.forceRecord"/> for adapter-controlled moments that should
        /// bypass soft budget/dedup drops.
        /// </summary>
        public static bool SubmitEvent(ExternalEventRequest request, out SubmitEventOutcome outcome)
        {
            outcome = SubmitEventOutcome.InvalidRequest;
            // Declared outside try so the catch block can refund it if Dispatch throws. The reservation
            // is committed before Dispatch; an exception from the dispatch path must not leak the
            // reservation for the rest of the rolling window.
            ExternalApiBudgetReservation reservation = null;
            try
            {
                if (!TryPrepareExternalEventRequest(request, "SubmitEvent", out _, out _, out outcome))
                {
                    return false;
                }

                if (!request.forceRecord
                    && !DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                        request, "SubmitEvent", out reservation))
                {
                    outcome = SubmitEventOutcome.DroppedBudget;
                    return false;
                }

                // Dispatch directly (not fire-and-forget DiaryEvents.Submit) so a deduped/policy-dropped
                // event refunds its budget reservation instead of burning the adapter's window. The
                // outcome distinguishes "dispatched and recorded" from "handed to the pipeline, which
                // then declined it (dedup / pawn state)" — the latter mirrors
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
                // Refund any reservation taken before the throw so a failing dispatch path cannot
                // falsely consume per-source/global budget until the rolling window expires. The
                // release is a no-op on null (forceRecord path), and Instance can be in any state after
                // a throw, so guard both.
                DiaryGameComponent.Instance?.ReleaseExternalApiBudgetReservation(reservation);

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
            // Hoisted out of try so the catch block can refund it if Dispatch throws.
            ExternalApiBudgetReservation reservation = null;
            try
            {
                if (!TryPrepareExternalEventRequest(request, "SubmitEventWithHandle", out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!request.forceRecord
                    && !DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                        request, "SubmitEventWithHandle", out reservation))
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
                // Refund any reservation taken before the throw; no-op on null and Instance-guarded.
                DiaryGameComponent.Instance?.ReleaseExternalApiBudgetReservation(reservation);
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
            // Hoisted out of try so the catch block can refund it if Dispatch throws.
            ExternalApiBudgetReservation reservation = null;
            try
            {
                if (!TryPrepareExternalPromptEntryRequest(request, "SubmitPromptEntry", out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!request.forceRecord
                    && !DiaryGameComponent.Instance.TryReserveExternalApiBudgetForEvent(
                        request, "SubmitPromptEntry", out reservation))
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
                // Refund any reservation taken before the throw; no-op on null and Instance-guarded.
                DiaryGameComponent.Instance?.ReleaseExternalApiBudgetReservation(reservation);
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
            // Hoisted out of try so the catch block can refund it if Dispatch throws.
            ExternalApiBudgetReservation reservation = null;
            try
            {
                if (!TryPrepareExternalDirectEntryRequest(request, out sourceId, out eventKey))
                {
                    return EmptySubmissionResult(sourceId, eventKey);
                }

                if (!request.forceRecord
                    && !DiaryGameComponent.Instance.TryReserveExternalApiBudgetForDirectEntry(
                        request, out reservation))
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
                // Refund any reservation taken before the throw; no-op on null and Instance-guarded.
                DiaryGameComponent.Instance?.ReleaseExternalApiBudgetReservation(reservation);
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
        /// Returns a pawn's effective diary PSYCHOTYPE (outlook) as a small read-only snapshot, or null.
        /// The sibling of <see cref="GetWritingStyle"/>: publishes the outlook lens (the <c>rule</c>) so a
        /// chat/context mod can align with what the pawn notices and how they judge. Empty rule when the
        /// psychotype layer is off or resolves to Neutral. Returns null — never throws — for a
        /// null/ineligible pawn, no game loaded, or an off-thread call.
        /// </summary>
        public static DiaryPsychotypeSnapshot GetPsychotype(Pawn pawn)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetPsychotype was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.Psychotype.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return null;
                }

                return DiaryGameComponent.Instance.PsychotypeSnapshotFor(pawn);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetPsychotype failed: " + e,
                    "PawnDiary.Api.Psychotype.Exception".GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Saves a source-owned free-form PSYCHOTYPE (outlook) override for a pawn. The override sits
        /// above the pawn's base/custom psychotype until the same source resets it, and colors what the
        /// entry notices and how it judges — it does not change the pawn's writing style. Mirrors
        /// <see cref="SetWritingStyleOverride"/> (main-thread only, source-owned, sanitized).
        /// </summary>
        public static bool SetPsychotypeOverride(Pawn pawn, string sourceId, string rule)
        {
            string cleanedSource = PsychotypeText.CleanSourceId(sourceId);
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotypeOverride was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.SetPsychotypeOverride.OffThread".GetHashCode());
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cleanedSource))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotypeOverride was called with a missing sourceId.",
                        "PawnDiary.Api.SetPsychotypeOverride.MissingSource".GetHashCode());
                    return false;
                }

                string cleanedRule = PsychotypeText.CleanExternalRule(rule);
                if (string.IsNullOrWhiteSpace(cleanedRule))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotypeOverride from '" + cleanedSource
                        + "' cleaned to a blank rule; the call was ignored.",
                        ("PawnDiary.Api.SetPsychotypeOverride.BlankRule." + cleanedSource).GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.SetExternalPsychotypeOverride(pawn, cleanedSource, cleanedRule);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetPsychotypeOverride from '" + cleanedSource
                    + "' for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.SetPsychotypeOverride.Exception." + cleanedSource + "."
                        + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Clears a source-owned psychotype override. Returns true when no override remains, false when
        /// the call is invalid or another source owns the active override.
        /// </summary>
        public static bool ResetPsychotypeOverride(Pawn pawn, string sourceId)
        {
            string cleanedSource = PsychotypeText.CleanSourceId(sourceId);
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: ResetPsychotypeOverride was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.ResetPsychotypeOverride.OffThread".GetHashCode());
                    return false;
                }

                if (string.IsNullOrWhiteSpace(cleanedSource))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: ResetPsychotypeOverride was called with a missing sourceId.",
                        "PawnDiary.Api.ResetPsychotypeOverride.MissingSource".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.ResetExternalPsychotypeOverride(pawn, cleanedSource);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: ResetPsychotypeOverride from '" + cleanedSource
                    + "' for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.ResetPsychotypeOverride.Exception." + cleanedSource + "."
                        + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Sets the pawn's BASE psychotype to a built-in Pawn Diary type by defName — the same editable
        /// layer the Psychotype Studio's picker writes — and, when <paramref name="pin"/> is true, pins it
        /// so Pawn Diary's automatic roll never overwrites the integration's choice. Unlike
        /// <see cref="SetPsychotypeOverride"/> (a source-locked layer), this writes the player-visible,
        /// swappable base type; the player can freely re-pick or unpin it afterward. An unknown defName
        /// resolves to Neutral rather than failing. Main-thread only; never throws. Honors the player's
        /// global psychotype-layer toggle at prompt time, since it uses the normal editable layer.
        /// </summary>
        public static bool SetPsychotype(Pawn pawn, string psychotypeDefName, bool pin = true)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotype was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.SetPsychotype.OffThread".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                if (!DiaryGameComponent.Instance.SetPsychotype(pawn, psychotypeDefName))
                {
                    return false;
                }

                if (pin)
                {
                    DiaryGameComponent.Instance.SetPsychotypePinned(pawn, true);
                }

                return true;
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetPsychotype for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.SetPsychotype.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Seeds the pawn's PLAYER-OWNED custom psychotype rule — the same editable per-pawn layer the
        /// Psychotype Studio writes. Unlike <see cref="SetPsychotypeOverride"/> (a source-locked layer the
        /// player cannot edit), this writes directly into the player's custom slot: an integration can
        /// pre-fill it from external personality data, and the player is then free to edit or clear it.
        /// There is no source ownership and no arbitration — the last writer wins — so callers should
        /// write sparingly (once, or only when their upstream data changes) rather than every tick, to
        /// avoid stomping an edit the player just made. Main-thread only; sanitized; returns false when
        /// the call is invalid, the pawn is ineligible, or the rule cleans to blank.
        /// </summary>
        public static bool SetPsychotypeCustomRule(Pawn pawn, string rule)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotypeCustomRule was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.SetPsychotypeCustomRule.OffThread".GetHashCode());
                    return false;
                }

                string cleanedRule = PsychotypeText.CleanRule(rule);
                if (string.IsNullOrWhiteSpace(cleanedRule))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetPsychotypeCustomRule cleaned to a blank rule; the call was ignored.",
                        "PawnDiary.Api.SetPsychotypeCustomRule.BlankRule".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady || pawn == null)
                {
                    return false;
                }

                return DiaryGameComponent.Instance.SetCustomPsychotypeRule(pawn, cleanedRule);
            }
            catch (Exception e)
            {
                string pawnForLog = PawnIdForLog(pawn);
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetPsychotypeCustomRule for pawn '" + pawnForLog + "' failed: " + e,
                    ("PawnDiary.Api.SetPsychotypeCustomRule.Exception." + pawnForLog + "." + e.GetType().FullName).GetHashCode());
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
        /// Registers (or replaces, by sourceId) an external psychotype GENERATOR — an adapter that produces
        /// a pawn's outlook asynchronously (e.g. an LLM transform). It gives the per-pawn voice editor a
        /// "Regenerate" button and a "generating…" status for pawns the generator owns, without the adapter
        /// needing any UI code. The three callbacks (all optional except <c>reroll</c>) run on the main
        /// thread while the editor is open: <c>canReroll</c> gates whether the button shows, <c>isBusy</c>
        /// drives the loading status, and <c>reroll</c> starts a fresh generation. Main-thread only; safe
        /// before a game loads; never throws (a generator that later throws is disabled for the session).
        /// </summary>
        public static void RegisterExternalPsychotypeGenerator(ExternalPsychotypeGenerator generator)
        {
            string sourceForLog = generator == null || string.IsNullOrWhiteSpace(generator.sourceId)
                ? "unknown-source"
                : generator.sourceId.Trim();
            try
            {
                if (generator == null || string.IsNullOrWhiteSpace(generator.sourceId) || generator.reroll == null)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterExternalPsychotypeGenerator needs a sourceId and a reroll callback; the call was ignored.",
                        ("PawnDiary.Api.PsychotypeGenerator.Invalid." + sourceForLog).GetHashCode());
                    return;
                }

                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RegisterExternalPsychotypeGenerator for '" + sourceForLog
                        + "' was called off the main thread; the generator was ignored.",
                        ("PawnDiary.Api.PsychotypeGenerator.OffThread." + sourceForLog).GetHashCode());
                    return;
                }

                ExternalPsychotypeGenerators.Register(generator);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: RegisterExternalPsychotypeGenerator for '" + sourceForLog + "' failed: " + e,
                    ("PawnDiary.Api.PsychotypeGenerator.Exception." + sourceForLog).GetHashCode());
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

        /// <summary>
        /// Returns a prompt-free snapshot of the player's current LLM API setup: the global routing
        /// mode and request knobs, plus one lane row per configured endpoint/model (see
        /// <see cref="DiaryApiLaneSnapshot"/>). Unlike the diary reads, this does NOT require a loaded
        /// game — the API lanes are global mod settings, valid at the main menu too — so it is gated
        /// only by the main thread and the master integration toggle. Returns null — never throws —
        /// off the main thread, when the master toggle is off, or when settings are unavailable.
        /// SECURITY: each lane's <c>apiKey</c> is returned in full (the player's real key). It is
        /// exposed so an adapter can reuse the player's provider; never log or forward it.
        /// </summary>
        public static DiaryApiSetupSnapshot GetApiSetup()
        {
            try
            {
                // Reads global mod settings and Def-free lane data. Settings reads are not thread-safe,
                // so keep the same main-thread rule as the other API members.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetApiSetup was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.GetApiSetup.OffThread".GetHashCode());
                    return null;
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return null;
                }

                return IntegrationApiSettings.BuildSetupSnapshot(PawnDiaryMod.Settings);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetApiSetup failed: " + e,
                    "PawnDiary.Api.GetApiSetup.Exception".GetHashCode());
                return null;
            }
        }

        /// <summary>
        /// Adds a new LLM API lane (endpoint + model + auth) to Pawn Diary's connection settings from
        /// an external request. A lane added with <see cref="ExternalApiLaneRequest.enabled"/> true
        /// (the default) is "active": it participates in generation/failover immediately and is
        /// persisted like a lane the player added by hand. Like <see cref="GetApiSetup"/> this does
        /// NOT require a loaded game; it is gated by the main thread and the master integration toggle.
        /// Never throws — the outcome (added / duplicate / missing field / off-thread / ineligible) is
        /// reported on <see cref="AddApiLaneResult.reason"/>.
        /// </summary>
        public static AddApiLaneResult AddApiLane(ExternalApiLaneRequest request)
        {
            try
            {
                // Mutates + persists global settings and pushes lanes to the shared client; all of that
                // is main-thread-only, so reject off-thread calls instead of racing.
                if (!UnityData.IsInMainThread)
                {
                    string sourceForLog = request != null && !string.IsNullOrWhiteSpace(request.sourceId)
                        ? request.sourceId.Trim()
                        : "unknown-source";
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: AddApiLane from '" + sourceForLog
                        + "' was called off the main thread; the call was ignored. Queue the work "
                        + "yourself and drain it from a main-thread hook such as GameComponentUpdate or OnGUI.",
                        ("PawnDiary.Api.AddApiLane.OffThread." + sourceForLog).GetHashCode());
                    return new AddApiLaneResult { index = -1, reason = "offThread" };
                }

                if (request == null)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: AddApiLane called with a null request.",
                        "PawnDiary.Api.AddApiLane.NullRequest".GetHashCode());
                    return new AddApiLaneResult { index = -1, reason = "invalidRequest" };
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return new AddApiLaneResult { index = -1, reason = "ineligible" };
                }

                return IntegrationApiSettings.AddLane(PawnDiaryMod.Settings, request);
            }
            catch (Exception e)
            {
                string sourceForLog = request != null && !string.IsNullOrWhiteSpace(request.sourceId)
                    ? request.sourceId
                    : "unknown-source";
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: AddApiLane from '" + sourceForLog + "' failed: " + e,
                    ("PawnDiary.Api.AddApiLane.Exception." + sourceForLog).GetHashCode());
                return new AddApiLaneResult { index = -1, reason = "invalidRequest" };
            }
        }

        /// <summary>
        /// Starts a one-shot LLM completion on the player's configured lane and returns a poll handle
        /// (&gt; 0), or 0 when rejected. The adapter supplies an instruction + input text; Pawn Diary runs
        /// it on the requested lane (or the first active lane) and the adapter reads the outcome later via
        /// <see cref="GetLlmCompletionResult"/>. This spends the player's tokens, so it is gated by the
        /// master integration switch and attributed to the caller's sourceId; it is one-shot (no
        /// failover), input- and output-capped, main-thread only, and never throws.
        /// </summary>
        public static int RequestLlmCompletion(ExternalLlmCompletionRequest request)
        {
            string cleanedSource = request != null ? PsychotypeText.CleanSourceId(request.sourceId) : string.Empty;
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RequestLlmCompletion was called off the main thread; the call was ignored. "
                        + "Queue the work yourself and start it from a main-thread hook such as GameComponentTick.",
                        "PawnDiary.Api.RequestLlmCompletion.OffThread".GetHashCode());
                    return 0;
                }

                if (request == null || string.IsNullOrWhiteSpace(cleanedSource))
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: RequestLlmCompletion needs a request with a sourceId; the call was ignored.",
                        "PawnDiary.Api.RequestLlmCompletion.MissingSource".GetHashCode());
                    return 0;
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return 0;
                }

                return ExternalLlmCompletionService.Begin(request, PawnDiaryMod.Settings);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: RequestLlmCompletion from '" + cleanedSource + "' failed: " + e,
                    ("PawnDiary.Api.RequestLlmCompletion.Exception." + cleanedSource + "." + e.GetType().FullName).GetHashCode());
                return 0;
            }
        }

        /// <summary>
        /// Reads the current state of a handle from <see cref="RequestLlmCompletion"/>. Poll each frame
        /// until the status is Succeeded or Failed; a terminal result is dropped after it is read once, and
        /// an unknown/expired handle reports <see cref="LlmCompletionStatus.Unknown"/>. Never throws.
        /// </summary>
        public static LlmCompletionResult GetLlmCompletionResult(int handle)
        {
            try
            {
                if (handle <= 0 || !ExternalIntegrationsAllowed)
                {
                    return new LlmCompletionResult { status = LlmCompletionStatus.Unknown };
                }

                return ExternalLlmCompletionService.Poll(handle);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetLlmCompletionResult failed: " + e,
                    "PawnDiary.Api.GetLlmCompletionResult.Exception".GetHashCode());
                return new LlmCompletionResult { status = LlmCompletionStatus.Unknown };
            }
        }

        /// <summary>
        /// Returns the automatic-capture event filters — the same per-interaction-group on/off toggles
        /// the player edits on Pawn Diary's settings "Events" tab (all non-External, non-package-gated
        /// groups), in the same order, each with its current saved state. Like the other settings reads
        /// this does NOT require a loaded game; it is gated by the main thread and the master integration
        /// toggle. Returns an empty list — never throws — off the main thread, when the master toggle is
        /// off, or when settings are unavailable. Side-effect free.
        /// </summary>
        public static List<DiaryEventFilterSnapshot> GetEventFilters()
        {
            try
            {
                // Walks DefDatabase group data and translates labels — main-thread only, like the other reads.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: GetEventFilters was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.GetEventFilters.OffThread".GetHashCode());
                    return new List<DiaryEventFilterSnapshot>();
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return new List<DiaryEventFilterSnapshot>();
                }

                return IntegrationApiSettings.BuildEventFilterSnapshots(PawnDiaryMod.Settings);
            }
            catch (Exception e)
            {
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: GetEventFilters failed: " + e,
                    "PawnDiary.Api.GetEventFilters.Exception".GetHashCode());
                return new List<DiaryEventFilterSnapshot>();
            }
        }

        /// <summary>
        /// Returns whether Pawn Diary currently captures the event kind identified by one event-filter
        /// group defName (from <see cref="GetEventFilters"/>). Returns false — never throws — for an
        /// unknown key, a group outside the settings Events list (External / package-gated), an
        /// off-thread call, the master toggle off, or no settings. Does not require a loaded game.
        /// </summary>
        public static bool IsEventFilterEnabled(string key)
        {
            try
            {
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: IsEventFilterEnabled was called off the main thread; the call was ignored.",
                        "PawnDiary.Api.IsEventFilterEnabled.OffThread".GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return false;
                }

                return IntegrationApiSettings.IsEventFilterEnabled(PawnDiaryMod.Settings, key);
            }
            catch (Exception e)
            {
                string keyForLog = string.IsNullOrWhiteSpace(key) ? "unknown-key" : key.Trim();
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: IsEventFilterEnabled for '" + keyForLog + "' failed: " + e,
                    ("PawnDiary.Api.IsEventFilterEnabled.Exception." + keyForLog).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Enables or disables automatic capture for one event-filter group (by defName), using the
        /// exact same saved flag as the settings "Events" tab, then persists. Returns true on success;
        /// false — never throws — for an unknown key, a group outside the settings Events list, an
        /// off-thread call, the master toggle off, or no settings. Does not require a loaded game. The
        /// change takes effect for future captured events immediately (filters are read per event).
        /// </summary>
        public static bool SetEventFilterEnabled(string key, bool enabled)
        {
            try
            {
                // Mutates + persists global settings — main-thread only, like the other settings writes.
                if (!UnityData.IsInMainThread)
                {
                    string keyForLog = string.IsNullOrWhiteSpace(key) ? "unknown-key" : key.Trim();
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SetEventFilterEnabled for '" + keyForLog
                        + "' was called off the main thread; the call was ignored.",
                        ("PawnDiary.Api.SetEventFilterEnabled.OffThread." + keyForLog).GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || PawnDiaryMod.Settings == null)
                {
                    return false;
                }

                return IntegrationApiSettings.TrySetEventFilter(PawnDiaryMod.Settings, key, enabled);
            }
            catch (Exception e)
            {
                string keyForLog = string.IsNullOrWhiteSpace(key) ? "unknown-key" : key.Trim();
                ApiLogErrorOnce(
                    "[Pawn Diary] Integration API: SetEventFilterEnabled for '" + keyForLog + "' failed: " + e,
                    ("PawnDiary.Api.SetEventFilterEnabled.Exception." + keyForLog).GetHashCode());
                return false;
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
        /// Same validation as the 4-param overload, but also reports a public outcome so the
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
            // that miserable to debug. ClassifyExternal returns the first matching group but does NOT
            // apply package gates, so a compatibility group that is inert without its target mod
            // (enableWhenPackageIdsLoaded / disableWhenPackageIdsLoaded) must be treated as absent here
            // too — otherwise its key would be accepted and dispatched while the group's prompt policy
            // is supposed to be dormant. Mirrors how IsGroupEnabled / EventFilterGroupsForSettings
            // already treat such groups.
            DiaryInteractionGroupDef claimingGroup = InteractionGroups.ClassifyExternal(eventKey);
            if (claimingGroup == null
                || claimingGroup.DisabledByLoadedPackage()
                || claimingGroup.MissingRequiredPackage())
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
