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
    /// Public entry point for other mods. Current surface: readiness, inbound events, read-only
    /// snapshots and filters, and pawn-context providers for prompt-summary context.
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
                    ApiLogErrorOnce("[Pawn Diary] Integration API: SubmitEvent called with a null request.",
                        "PawnDiary.Api.NullRequest".GetHashCode());
                    return false;
                }

                string sourceId = string.IsNullOrWhiteSpace(request.sourceId)
                    ? "unknown-source"
                    : request.sourceId.Trim();

                if (string.IsNullOrWhiteSpace(request.eventKey) || request.subject == null)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SubmitEvent from '" + sourceId
                        + "' is missing a required field (eventKey and subject are mandatory).",
                        ("PawnDiary.Api.Invalid." + sourceId).GetHashCode());
                    return false;
                }

                // The pipeline reads DefDatabase/settings/tick state and later .Translate()s text,
                // none of which is safe off the main thread. Reject instead of racing.
                if (!UnityData.IsInMainThread)
                {
                    ApiLogErrorOnce(
                        "[Pawn Diary] Integration API: SubmitEvent from '" + sourceId
                        + "' was called off the main thread; the call was ignored. Submit from the "
                        + "main thread (e.g. via LongEventHandler.ExecuteWhenFinished).",
                        ("PawnDiary.Api.OffThread." + sourceId).GetHashCode());
                    return false;
                }

                if (!ExternalIntegrationsAllowed || !IsReady)
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

                DiaryEvents.Submit(new ExternalEventSignal(request));
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
                return false;
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

                return DiaryGameComponent.Current.RecentEntryTitleSnapshotsFor(pawn, maxCount);
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

                return DiaryGameComponent.Current.WritingStyleSnapshotFor(pawn);
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

                return DiaryGameComponent.Current.RecentEntryTitleSnapshotsFor(pawn, maxCount, query);
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
    }
}
