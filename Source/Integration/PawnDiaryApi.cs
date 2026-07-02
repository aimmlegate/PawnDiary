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
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Public entry point for other mods. v1 surface: version/readiness probes plus
    /// <see cref="SubmitEvent"/>, which pushes one external event into the diary pipeline.
    /// </summary>
    public static class PawnDiaryApi
    {
        /// <summary>
        /// Contract version of this API surface. Bumped only when members are ADDED; existing
        /// members never change behavior incompatibly. Adapters that need a newer member can check
        /// this at load time and degrade gracefully on older Pawn Diary builds.
        /// </summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// True while a game is loaded and the diary component is alive — the only time
        /// <see cref="SubmitEvent"/> can record anything. Safe to call at any moment (menus, loading
        /// screens); it simply returns false outside play.
        /// </summary>
        public static bool IsReady
        {
            get { return DiaryGameComponent.GamePlaying && DiaryGameComponent.Current != null; }
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
            try
            {
                if (request == null)
                {
                    Log.ErrorOnce("[Pawn Diary] Integration API: SubmitEvent called with a null request.",
                        "PawnDiary.Api.NullRequest".GetHashCode());
                    return false;
                }

                string sourceId = string.IsNullOrWhiteSpace(request.sourceId)
                    ? "unknown-source"
                    : request.sourceId.Trim();

                if (string.IsNullOrWhiteSpace(request.eventKey) || request.subject == null)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Integration API: SubmitEvent from '" + sourceId
                        + "' is missing a required field (eventKey and subject are mandatory).",
                        ("PawnDiary.Api.Invalid." + sourceId).GetHashCode());
                    return false;
                }

                // The pipeline reads DefDatabase/settings/tick state and later .Translate()s text,
                // none of which is safe off the main thread. Reject instead of racing.
                if (!UnityData.IsInMainThread)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Integration API: SubmitEvent from '" + sourceId
                        + "' was called off the main thread; the call was ignored. Submit from the "
                        + "main thread (e.g. via LongEventHandler.ExecuteWhenFinished).",
                        ("PawnDiary.Api.OffThread." + sourceId).GetHashCode());
                    return false;
                }

                if (!IsReady)
                {
                    return false;
                }

                // Fail loudly-but-once when nobody claims the key: the most common adapter mistake
                // is shipping the C# call without the External-domain group XML, and a silent drop
                // would make that miserable to debug.
                string eventKey = request.eventKey.Trim();
                if (InteractionGroups.ClassifyExternal(eventKey) == null)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] Integration API: no External-domain DiaryInteractionGroupDef "
                        + "claims eventKey '" + eventKey + "' (submitted by '" + sourceId + "'). "
                        + "The adapter must ship a group def that matches this key; the event was ignored.",
                        ("PawnDiary.Api.UnclaimedKey." + eventKey).GetHashCode());
                    return false;
                }

                DiaryEvents.Submit(new Ingestion.ExternalEventSignal(request));
                return true;
            }
            catch (Exception e)
            {
                string sourceForLog = request != null && !string.IsNullOrWhiteSpace(request.sourceId)
                    ? request.sourceId
                    : "unknown-source";
                Log.ErrorOnce(
                    "[Pawn Diary] Integration API: SubmitEvent from '" + sourceForLog + "' failed: " + e,
                    ("PawnDiary.Api.Exception." + sourceForLog).GetHashCode());
                return false;
            }
        }
    }
}
