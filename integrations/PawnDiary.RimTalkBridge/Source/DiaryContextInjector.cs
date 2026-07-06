// Level 1 outbound: pushes each pawn's recent Pawn Diary memories (and optionally their diary
// writing-voice) into RimTalk's chat prompts, so a colonist "remembers" what they wrote about.
//
// TWO THREADS meet here, which drives the whole design:
//   • RefreshFor(...) runs on the MAIN thread (the bridge's tick pass). It is the ONLY place that
//     calls PawnDiaryApi or .Translate() — both are main-thread-only.
//   • SectionFor(...) is the delegate RimTalk invokes while assembling a prompt, which can happen on
//     a background Task. It therefore does NO API calls and NO translation: it only reads the cache
//     that RefreshFor filled. A lock guards the shared cache dictionary across the two threads.
//
// RimTalk-type isolation: RegisterAll() names RimTalk.API types, so it is [NoInlining] and is only
// ever reached after PawnDiaryRimTalkBridgeMod's RimTalkActive guard (see RIMTALK_BRIDGE_PLAN Step 0).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.API;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Registers the bridge's RimTalk prompt hooks and maintains a per-pawn cache of the diary-memory
    /// section RimTalk reads during prompt assembly.
    /// </summary>
    internal static class DiaryContextInjector
    {
        // Defensive hard cap on the injected section size. RimTalk prompts have their own budgets; this
        // keeps one pawn's diary block from crowding out the rest of the prompt. Parser-style safety
        // limit, not player tuning, so it stays in code (AGENTS.md).
        private const int MaxSectionChars = 700;

        // Priority passed to RimTalk. Mid-range so other mods can order before/after us if they care.
        private const int HookPriority = 100;

        // Guards the cache dictionary: RefreshFor (main thread) writes it, SectionFor (possibly a
        // RimTalk background task) reads it. Uncontended in practice — refresh is throttled — so the
        // lock is cheap, but it prevents a torn read of the Dictionary during a concurrent write.
        private static readonly object Gate = new object();

        // pawn.GetUniqueLoadID() -> cached section text + freshness bookkeeping.
        private static readonly Dictionary<string, CachedSection> Cache =
            new Dictionary<string, CachedSection>();

        /// <summary>
        /// Registers the diary section/variable inside RimTalk's prompt builder and the Pawn Diary
        /// status listener that keeps the cache fresh. Process-global and idempotent; call once from
        /// the mod constructor, only when RimTalk is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RegisterAll()
        {
            // Primary mechanism (⚠️ U1 — verify in-game that RimTalk renders injected sections into the
            // default prompt; if not, switch to RegisterPawnHook(Pawn.Thoughts, Append) — see the plan).
            ContextHookRegistry.InjectPawnSection(
                BridgeIds.DiarySectionName,
                BridgeIds.ModId,
                ContextCategories.Pawn.Thoughts,
                ContextHookRegistry.InjectPosition.After,
                SectionFor,
                HookPriority);

            // Also expose it as a Scriban variable so template editors can place {{pawn1.diary}} by hand.
            ContextHookRegistry.RegisterPawnVariable(
                BridgeIds.DiarySectionName,
                BridgeIds.ModId,
                SectionFor,
                "PawnDiaryRimTalkBridge.Prompt.DiaryVariableDesc".Translate(),
                HookPriority);

            // When Pawn Diary finishes (or changes) an entry, mark that pawn's cache stale so the fresh
            // memory shows up in chat within one refresh pass instead of a full TTL later.
            PawnDiaryApi.RegisterEntryStatusListener(BridgeIds.StatusListenerId, OnEntryStatus);
        }

        /// <summary>
        /// Rebuilds and caches one pawn's diary section. MAIN THREAD ONLY (calls PawnDiaryApi and
        /// .Translate()). Ineligible or empty pawns cache "" so SectionFor can serve them cheaply.
        /// </summary>
        public static void RefreshFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string section = BuildSectionFor(pawn);
            string id = pawn.GetUniqueLoadID();
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            lock (Gate)
            {
                CachedSection cached;
                if (!Cache.TryGetValue(id, out cached))
                {
                    cached = new CachedSection();
                    Cache[id] = cached;
                }

                cached.Text = section ?? string.Empty;
                cached.Tick = now;
                cached.Stale = false;
            }
        }

        /// <summary>
        /// The delegate RimTalk calls while building a prompt. Cache read only — never calls the API or
        /// translates. Returns "" when the bridge is off, the master switch is off, or the pawn's
        /// section has not been built yet.
        /// </summary>
        public static string SectionFor(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) || !PawnDiaryApi.IsExternalApiEnabled)
            {
                return string.Empty;
            }

            string id = pawn.GetUniqueLoadID();
            lock (Gate)
            {
                CachedSection cached;
                if (Cache.TryGetValue(id, out cached))
                {
                    return cached.Text ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// True when the pawn has no cached section yet, was invalidated, or its cache is older than
        /// <paramref name="ttl"/> ticks. Drives the throttled refresh pass in the game component.
        /// </summary>
        public static bool NeedsRefresh(Pawn pawn, int now, int ttl)
        {
            if (pawn == null)
            {
                return false;
            }

            string id = pawn.GetUniqueLoadID();
            lock (Gate)
            {
                CachedSection cached;
                if (!Cache.TryGetValue(id, out cached))
                {
                    return true;
                }

                return cached.Stale || (now - cached.Tick) >= ttl;
            }
        }

        /// <summary>
        /// Marks one pawn's cache stale (keeps serving the old text until the next refresh). Safe to
        /// call from the status listener; touches no RimTalk types.
        /// </summary>
        public static void InvalidateFor(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            lock (Gate)
            {
                CachedSection cached;
                if (Cache.TryGetValue(pawnId, out cached))
                {
                    cached.Stale = true;
                }
            }
        }

        /// <summary>
        /// Clears the cache. Static caches leak across exit-to-menu + load, so the game component calls
        /// this from FinalizeInit (see the "Persistence & ticking" gotcha in SKILL.md).
        /// </summary>
        public static void ResetForNewGame()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        /// <summary>Builds the section text on the main thread, or "" when the pawn contributes nothing.</summary>
        private static string BuildSectionFor(Pawn pawn)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) || !PawnDiaryApi.IsExternalApiEnabled)
            {
                return string.Empty;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int entryCount = settings != null ? settings.contextEntryCount : 3;
            if (entryCount <= 0)
            {
                return string.Empty;
            }

            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(pawn, entryCount);
            List<DiaryMemoryLine> lines = new List<DiaryMemoryLine>();
            if (snapshot != null && snapshot.entries != null)
            {
                for (int i = 0; i < snapshot.entries.Count; i++)
                {
                    DiaryEntryProseSnapshot entry = snapshot.entries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    // Prefer the LLM title; fall back to the event group label when no title exists yet.
                    string title = !string.IsNullOrWhiteSpace(entry.title) ? entry.title : entry.groupLabel;
                    lines.Add(new DiaryMemoryLine
                    {
                        Title = title,
                        Summary = entry.summary,
                        Date = entry.date
                    });
                }
            }

            bool includeStyle = settings == null || settings.includeDiaryVoiceLine;
            string styleRule = string.Empty;
            if (includeStyle)
            {
                DiaryWritingStyleSnapshot style = PawnDiaryApi.GetWritingStyle(pawn);
                styleRule = style != null ? style.rule : string.Empty;
            }

            return ContextFormat.BuildDiarySection(
                lines,
                "PawnDiaryRimTalkBridge.Prompt.DiaryMemoriesHeader".Translate(),
                "PawnDiaryRimTalkBridge.Prompt.MemoryLine".Translate(),
                "PawnDiaryRimTalkBridge.Prompt.DiaryVoiceLine".Translate(),
                styleRule,
                includeStyle,
                MaxSectionChars);
        }

        /// <summary>Status listener: a changed entry means the speaker's cached memories are out of date.</summary>
        private static void OnEntryStatus(DiaryEntryStatusSnapshot snapshot)
        {
            if (snapshot == null || snapshot.handle == null)
            {
                return;
            }

            InvalidateFor(snapshot.handle.pawnId);
        }

        /// <summary>One pawn's cached diary section plus the freshness bookkeeping.</summary>
        private sealed class CachedSection
        {
            /// <summary>Pre-built section text (may be "").</summary>
            public string Text = string.Empty;

            /// <summary>Tick this section was last rebuilt.</summary>
            public int Tick;

            /// <summary>Set when an entry changed for this pawn; forces a rebuild next pass.</summary>
            public bool Stale;
        }
    }
}
