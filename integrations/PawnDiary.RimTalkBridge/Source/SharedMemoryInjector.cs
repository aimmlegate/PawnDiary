// Feature 3 outbound: when two colonists talk, injects the diary memories they SHARE — entries where
// one is the subject and the other the partner — as the {{diary_shared}} context variable, so their
// chat can reference "previous interactions". Which shared moments show is picked weighted-randomly
// (SharedMemorySelection), biased toward recent and notable ones.
//
// TWO THREADS meet here, like DiaryContextInjector, but a context variable sees ALL participants of a
// conversation (not one pawn), and pairs are O(n^2) and only known at prompt time — so instead of
// precomputing, we use a LAZY REQUEST QUEUE:
//   • SharedFor(ctx) is the delegate RimTalk invokes while assembling a prompt, possibly on a
//     background Task. It is CACHE-READ-ONLY: it resolves the speaking pair, serves the cached block
//     if present, and otherwise enqueues the pair for the main-thread pass and returns "" this once.
//     No PawnDiaryApi calls, no .Translate(), no map scans.
//   • ProcessQueue(now) runs on the MAIN thread (the bridge tick pass): it drains the queue and, for
//     each pair not already fresh, reads the shared diary entries, runs the weighted pick, builds the
//     block, and stores it under a lock. A status listener marks a pair stale when either pawn's diary
//     changes.
//
// Optional zero-config delivery: RimTalk has no "inject context section" (only pawn/env sections, and
// neither sees the second participant), so a template author must place {{diary_shared}} themselves —
// OR the bridge can auto-register a system PROMPT ENTRY that embeds it (autoInjectSharedMemory, on by
// default). Prompt entries PERSIST in the user's active RimTalk preset, so the remove-then-add sync is
// idempotent and cleanup on toggle-off is mandatory (see the plan, Step 4 / §2 #4).
//
// RimTalk-type isolation: every method that names a RimTalk type (SharedFor, RegisterAll,
// SyncAutoInject) is [NoInlining] and only reached after the mod's RimTalkActive guard.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.API;
using RimTalk.Prompt;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Maintains a per-pair cache of the shared-memory block RimTalk reads as {{diary_shared}} and
    /// registers the context variable, the invalidation listener, and the optional auto prompt-entry.
    /// </summary>
    internal static class SharedMemoryInjector
    {
        // Defensive hard cap on the injected block. Tight because it rides on a two-pawn conversation
        // and RimTalk targets small models. Parser-style safety limit, not player tuning (AGENTS.md).
        private const int MaxSectionChars = 500;

        // Priority passed to RimTalk. Mid-range so other mods can order before/after us if they care.
        private const int HookPriority = 100;

        // How many shared entries we fetch as candidates before the weighted pick trims to the player's
        // sharedMemoryCount. A small pool larger than the pick so recency/importance weighting has room
        // to work. Parser-style constant, not player tuning.
        private const int CandidateFetch = 8;

        // In-game ticks per day, used to derive the daily seed component so a pair's shown memories
        // rotate day to day but stay stable within a day.
        private const int TicksPerDay = 60000;

        // Guards the pair cache: ProcessQueue (main thread) writes it, SharedFor (possibly a RimTalk
        // background task) reads it, OnEntryStatus (status-listener thread) flips Stale flags.
        private static readonly object Gate = new object();

        // pairKey -> cached block text + freshness bookkeeping.
        private static readonly Dictionary<string, CachedPair> PairCache = new Dictionary<string, CachedPair>();

        // Pairs seen by the background provider but not yet built. Thread-safe; drained by the pass.
        private static readonly ConcurrentQueue<PairRequest> Requests = new ConcurrentQueue<PairRequest>();

        // Pre-translated preview line for RimTalk's settings preview (IsPreview). Built on the main
        // thread at registration; read by the background provider, so kept volatile.
        private static volatile string previewSample = string.Empty;

        // Last applied auto-inject desired-state, so SyncAutoInject only touches the preset on change.
        // null = never synced this process. NOT reset per-game: the prompt entry lives in RimTalk's
        // preset, which outlives any single colony.
        private static bool? autoInjectApplied;

        /// <summary>
        /// Registers the {{diary_shared}} context variable and the entry-status listener, and captures
        /// the localized preview sample. Process-global and idempotent; call once from the mod
        /// constructor, only when RimTalk is active. Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RegisterAll()
        {
            // Main thread here (mod ctor), so .Translate() is safe; the background provider only ever
            // reads this cached string.
            previewSample = "PawnDiaryRimTalkBridge.Prompt.SharedMemoriesSample".Translate();

            // The provider is a Func<PromptContext,string>; RegisterContextVariable's param is typed
            // System.Delegate (name/section-first order — NOT the modId-first façade). See the plan §1.
            ContextHookRegistry.RegisterContextVariable(
                BridgeIds.SharedMemoryVariableName,
                BridgeIds.ModId,
                (Func<PromptContext, string>)SharedFor,
                "PawnDiaryRimTalkBridge.Prompt.SharedMemoryVariableDesc".Translate(),
                HookPriority);

            // A second status listener id (the diary-section injector owns the first): a changed entry
            // for pawn X marks every cached pair containing X stale.
            PawnDiaryApi.RegisterEntryStatusListener(BridgeIds.SharedStatusListenerId, OnEntryStatus);
        }

        /// <summary>
        /// The delegate RimTalk calls while building a prompt, possibly on a background thread. Cache
        /// read only: serves the pair's built block, or enqueues the pair and returns "" the first
        /// time. Returns a localized sample for RimTalk's settings preview. Never calls the API or
        /// translates. Names RimTalk types (PromptContext).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string SharedFor(PromptContext ctx)
        {
            try
            {
                if (ctx == null)
                {
                    return string.Empty;
                }

                // Settings-menu preview: cheap localized sample, no cache/API (plan §2 #6).
                if (ctx.IsPreview)
                {
                    return previewSample ?? string.Empty;
                }

                if (!FeatureActive() || ctx.IsMonologue)
                {
                    return string.Empty;
                }

                Pawn current = ctx.CurrentPawn;
                if (current == null)
                {
                    return string.Empty;
                }

                Pawn other = FirstOtherColonist(ctx.AllPawns ?? ctx.Pawns, current);
                if (other == null)
                {
                    return string.Empty;
                }

                string idA = current.GetUniqueLoadID();
                string idB = other.GetUniqueLoadID();
                if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB) || idA == idB)
                {
                    return string.Empty;
                }

                string key = SharedMemorySelection.PairKey(idA, idB);
                string text;
                bool needsBuild;
                lock (Gate)
                {
                    CachedPair cached;
                    if (PairCache.TryGetValue(key, out cached))
                    {
                        text = cached.Text ?? string.Empty;
                        needsBuild = cached.Stale; // serve old text, refresh async if stale
                    }
                    else
                    {
                        text = string.Empty;
                        needsBuild = true;
                    }
                }

                if (needsBuild)
                {
                    Requests.Enqueue(new PairRequest { Key = key, A = current, B = other, IdA = idA, IdB = idB });
                }

                return text;
            }
            catch
            {
                // A prompt provider must never throw into RimTalk's build; degrade to "no block".
                return string.Empty;
            }
        }

        /// <summary>
        /// Drains queued pair requests and builds the ones not already fresh. MAIN THREAD ONLY (calls
        /// PawnDiaryApi and .Translate()). Names no RimTalk types. Always drains the queue (even when
        /// the feature is off) so it cannot grow unbounded; only rebuilds when the feature is on.
        /// </summary>
        public static void ProcessQueue(int now)
        {
            bool active = FeatureActive();

            HashSet<string> processed = new HashSet<string>();
            PairRequest req;
            while (Requests.TryDequeue(out req))
            {
                if (req == null || !active || !processed.Add(req.Key))
                {
                    continue;
                }

                lock (Gate)
                {
                    CachedPair cached;
                    if (PairCache.TryGetValue(req.Key, out cached) && !cached.Stale)
                    {
                        continue; // already fresh; skip the read
                    }
                }

                BuildPair(req, now);
            }
        }

        /// <summary>
        /// Reconciles the optional auto-injected prompt entry with the current settings: removes any
        /// bridge entry, then re-adds one embedding {{diary_shared}} when the feature is on. Idempotent
        /// (remove-then-add) and only touches the preset on a state change. Cleanup on toggle-off is
        /// mandatory because prompt entries persist in the user's active preset. Guarded so an older
        /// RimTalk without the prompt-entry API disables just this convenience. Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SyncAutoInject()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;

            // FeatureActive() already folds in level + master toggle + external-API enabled, so the auto
            // entry is torn down whenever the feature itself is off. (This gate previously omitted the
            // external-API check, which could leave the prompt entry registered while output was off.)
            bool desired = FeatureActive()
                && settings != null
                && settings.autoInjectSharedMemory;

            if (autoInjectApplied.HasValue && autoInjectApplied.Value == desired)
            {
                return; // no change since last sync
            }

            try
            {
                // Always clear ours first (idempotent + self-healing across process restarts).
                RimTalkPromptAPI.RemovePromptEntriesByModId(BridgeIds.ModId);

                if (desired)
                {
                    // Content is exactly the variable token so an empty block contributes nothing. The
                    // {{...}} token is a machine key, not prose, so it is not localized (carve-out).
                    string content = "{{" + BridgeIds.SharedMemoryVariableName + "}}";
                    PromptEntry entry = RimTalkPromptAPI.CreatePromptEntry(
                        BridgeIds.SharedMemoryPromptEntryName,
                        content,
                        PromptRole.System,
                        PromptPosition.Relative,
                        0,
                        BridgeIds.ModId);

                    // If RimTalk could not build the entry (rejected the params / returned null rather
                    // than throwing), we have already removed the old one but added nothing back. Do NOT
                    // record the desired state: leaving autoInjectApplied unchanged makes a later pass
                    // retry, instead of the feature going silently dead for the rest of the process.
                    if (entry == null)
                    {
                        Log.WarningOnce(
                            PawnDiaryRimTalkBridgeMod.LogPrefix + " RimTalk returned no {{diary_shared}} prompt "
                            + "entry; will retry. Place the variable in a template manually if this persists.",
                            "PawnDiaryRimTalkBridge.AutoInjectNullEntry".GetHashCode());
                        return;
                    }

                    RimTalkPromptAPI.AddPromptEntry(entry);
                }

                autoInjectApplied = desired;
            }
            catch (Exception e)
            {
                // Leave autoInjectApplied unchanged so a later pass retries. One warning, not a spam.
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " could not sync the {{diary_shared}} prompt entry "
                    + "(RimTalk prompt-entry API unavailable?); use the variable manually in a template instead: " + e,
                    "PawnDiaryRimTalkBridge.AutoInjectFailed".GetHashCode());
            }
        }

        /// <summary>Marks every cached pair containing <paramref name="pawnId"/> stale. Touches no RimTalk types.</summary>
        public static void InvalidateForPawn(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            lock (Gate)
            {
                foreach (KeyValuePair<string, CachedPair> kv in PairCache)
                {
                    if (kv.Value.ContainsPawn(pawnId))
                    {
                        kv.Value.Stale = true;
                    }
                }
            }
        }

        /// <summary>
        /// Marks every cached pair stale so the next prompt rebuilds it. Called when settings change
        /// (e.g. <c>sharedMemoryCount</c>): the cache is keyed only by pair, not by the settings that
        /// shaped the block, so without this a changed count keeps serving the previously built block
        /// until an unrelated diary edit happens to invalidate it. Touches no RimTalk types.
        /// </summary>
        public static void InvalidateAllPairs()
        {
            lock (Gate)
            {
                foreach (KeyValuePair<string, CachedPair> kv in PairCache)
                {
                    kv.Value.Stale = true;
                }
            }
        }

        /// <summary>Clears the pair cache and the request queue on new-game load (SKILL.md gotcha).</summary>
        public static void ResetForNewGame()
        {
            lock (Gate)
            {
                PairCache.Clear();
            }

            PairRequest discard;
            while (Requests.TryDequeue(out discard))
            {
            }

            // autoInjectApplied is intentionally NOT reset: the prompt entry lives in RimTalk's preset,
            // which persists across colonies within one process.
        }

        // True when the shared-memory feature should contribute anything. Cheap settings/flag reads,
        // safe from both the main-thread pass and the background provider.
        private static bool FeatureActive()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                && settings != null
                && settings.injectSharedMemory
                && PawnDiaryApi.IsExternalApiEnabled;
        }

        // First participant that is not the current pawn and is a colonist. Runs on the background
        // provider thread, so every live read is defensive.
        private static Pawn FirstOtherColonist(List<Pawn> participants, Pawn current)
        {
            if (participants == null || current == null)
            {
                return null;
            }

            string currentId = current.GetUniqueLoadID();
            for (int i = 0; i < participants.Count; i++)
            {
                Pawn p = participants[i];
                if (p == null || p == current || p.GetUniqueLoadID() == currentId)
                {
                    continue;
                }

                if (SafeIsColonist(p))
                {
                    return p;
                }
            }

            return null;
        }

        private static bool SafeIsColonist(Pawn pawn)
        {
            try
            {
                return pawn.IsColonist;
            }
            catch
            {
                return false;
            }
        }

        // Builds one pair's block on the main thread and stores it. The block is symmetric (the same
        // shared history whichever pawn speaks) and keyed once per pair. We prefer the deterministic
        // lower-id pawn as the subject, but if that pawn cannot be read right now (unspawned, or
        // diary-ineligible — e.g. a prisoner/guest), we fall back to the other pawn, so a colonist's
        // genuine shared memories still surface for a colonist↔non-colonist pair (the diary event is
        // stored once and viewable from either participant, so a readable-but-empty primary means the
        // other pawn is empty too — we only retry the other pawn when the primary was unreadable).
        private static void BuildPair(PairRequest req, int now)
        {
            string minId = string.CompareOrdinal(req.IdA, req.IdB) <= 0 ? req.IdA : req.IdB;
            Pawn primary = req.IdA == minId ? req.A : req.B;
            string primaryPartnerId = primary == req.A ? req.IdB : req.IdA;
            Pawn secondary = primary == req.A ? req.B : req.A;
            string secondaryPartnerId = primary == req.A ? req.IdA : req.IdB;

            bool primaryUnreadable;
            string text = BuildBlock(primary, primaryPartnerId, req.Key, now, out primaryUnreadable);
            if (string.IsNullOrEmpty(text) && primaryUnreadable)
            {
                bool secondaryUnreadable;
                string alt = BuildBlock(secondary, secondaryPartnerId, req.Key, now, out secondaryUnreadable);
                if (!string.IsNullOrEmpty(alt))
                {
                    text = alt;
                }
                else if (secondaryUnreadable)
                {
                    // Neither pawn could be read this pass (both unspawned/ineligible — e.g. the colonist
                    // is away in a caravan). Do not cache a permanent empty; leave the pair to retry on a
                    // later pass rather than serving "" until an unrelated diary change invalidates it.
                    return;
                }
            }

            lock (Gate)
            {
                CachedPair cached;
                if (!PairCache.TryGetValue(req.Key, out cached))
                {
                    cached = new CachedPair();
                    PairCache[req.Key] = cached;
                }

                cached.Text = text ?? string.Empty;
                cached.Stale = false;
                cached.IdA = req.IdA;
                cached.IdB = req.IdB;
            }
        }

        // Reads the pair's shared diary entries (subject's entries partnered with partnerId), runs the
        // weighted pick, and formats the block. MAIN THREAD. Returns "" when there is nothing to show;
        // sets <paramref name="unreadable"/> when the subject could not be read at all (unspawned or
        // diary-ineligible) as opposed to readable-but-sharing-nothing, so the caller can decide between
        // trying the other pawn / retrying later and caching a genuine empty.
        private static string BuildBlock(Pawn subject, string partnerId, string key, int now, out bool unreadable)
        {
            unreadable = false;
            if (subject == null || !subject.Spawned)
            {
                unreadable = true;
                return string.Empty;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int count = settings != null ? settings.sharedMemoryCount : 3;
            if (count <= 0)
            {
                return string.Empty;
            }

            DiaryEntryTitleQuery query = new DiaryEntryTitleQuery { partnerPawnId = partnerId };
            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(subject, CandidateFetch, query);
            if (snapshot == null)
            {
                // A null snapshot means the pawn is diary-ineligible (the humanlike-colonist gate); the
                // OTHER pawn may still hold the linked entries, so treat this as unreadable, not empty.
                unreadable = true;
                return string.Empty;
            }

            if (snapshot.entries == null || snapshot.entries.Count == 0)
            {
                return string.Empty;
            }

            List<SharedMemoryCandidate> candidates = new List<SharedMemoryCandidate>();
            for (int i = 0; i < snapshot.entries.Count; i++)
            {
                DiaryEntryProseSnapshot entry = snapshot.entries[i];
                if (entry == null)
                {
                    continue;
                }

                // Prefer the LLM title; fall back to the event group label. Skip rows with neither a
                // title nor a summary — they would not produce a usable line.
                string title = !string.IsNullOrWhiteSpace(entry.title) ? entry.title : entry.groupLabel;
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(entry.summary))
                {
                    continue;
                }

                candidates.Add(new SharedMemoryCandidate
                {
                    Title = title,
                    Summary = entry.summary,
                    Date = entry.date,
                    Tick = entry.tick,
                    HasAtmosphereCue = !string.IsNullOrWhiteSpace(entry.atmosphereCue),
                    IsConversationEntry = !string.IsNullOrEmpty(entry.externalSourceId)
                        && string.Equals(entry.externalSourceId, BridgeIds.ModId, StringComparison.OrdinalIgnoreCase)
                });
            }

            if (candidates.Count == 0)
            {
                return string.Empty;
            }

            // Stable per-pair, per-day seed so the shown memories are steady within a day and reshuffle
            // across days — never Verse.Rand (banned from provider-reachable code; see the plan).
            int seed = unchecked(key.GetHashCode() * 397 + (now / TicksPerDay));
            List<SharedMemoryCandidate> picked = SharedMemorySelection.Select(candidates, count, seed);

            List<DiaryMemoryLine> lines = new List<DiaryMemoryLine>();
            for (int i = 0; i < picked.Count; i++)
            {
                lines.Add(new DiaryMemoryLine
                {
                    Title = picked[i].Title,
                    Summary = picked[i].Summary,
                    Date = picked[i].Date
                });
            }

            // Reuse the memory-line format; no writing-voice line here (that is a per-pawn concept).
            return ContextFormat.BuildDiarySection(
                lines,
                "PawnDiaryRimTalkBridge.Prompt.SharedMemoriesHeader".Translate(),
                "PawnDiaryRimTalkBridge.Prompt.MemoryLine".Translate(),
                null,
                null,
                false,
                MaxSectionChars);
        }

        // Status listener: a changed entry for pawn X means any cached pair that includes X is stale.
        private static void OnEntryStatus(DiaryEntryStatusSnapshot snapshot)
        {
            if (snapshot == null || snapshot.handle == null)
            {
                return;
            }

            InvalidateForPawn(snapshot.handle.pawnId);
        }

        /// <summary>A pair request awaiting the main-thread build. Holds live pawn refs + their ids.</summary>
        private sealed class PairRequest
        {
            public string Key;
            public Pawn A;
            public Pawn B;
            public string IdA;
            public string IdB;
        }

        /// <summary>One pair's cached shared-memory block plus freshness bookkeeping.</summary>
        private sealed class CachedPair
        {
            /// <summary>Pre-built block text (may be "").</summary>
            public string Text = string.Empty;

            /// <summary>Set when either pawn's diary changed; forces a rebuild next pass.</summary>
            public bool Stale;

            /// <summary>The two pawn ids in this pair, for status-listener invalidation.</summary>
            public string IdA;
            public string IdB;

            public bool ContainsPawn(string pawnId)
            {
                return pawnId == IdA || pawnId == IdB;
            }
        }
    }
}
