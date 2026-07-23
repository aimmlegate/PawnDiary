// Feature 1 outbound: pushes a short, curated "colony situation" line into RimTalk prompts as the
// {{colony_events}} environment variable (and an optional zero-config section after Weather), so a
// colonist's chat reflects what is happening around the colony right now — a raid in progress, toxic
// fallout, an anomaly presence, an open quest. Default OFF (opt-in): it overlaps RimTalk's own event
// mods, so Pawn Diary only contributes a curated, atmospheric summary.
//
// TWO THREADS meet here, exactly like DiaryContextInjector:
//   • RefreshFor(map) runs on the MAIN thread (the bridge tick pass). It is the ONLY place that reads
//     live map state or calls .Translate() — both are main-thread-only.
//   • SectionFor(map) is the delegate RimTalk invokes while assembling a prompt, possibly on a
//     background Task. It does NO map scans and NO translation: it only reads the cache RefreshFor
//     filled. A lock guards the shared cache across the two threads.
//
// RimTalk-type isolation: RegisterAll() names RimTalk.API types, so it is [NoInlining] and is only
// reached after the mod's RimTalkActive guard (see the plan / RIMTALK_BRIDGE_PLAN Step 0).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.API;
using RimWorld;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Maintains a per-map cache of the colony-situation block RimTalk reads as {{colony_events}} and
    /// registers the environment variable + optional injected section.
    /// </summary>
    internal static class ColonyContextInjector
    {
        // Defensive hard cap on the injected block. Tighter than the per-pawn diary block because this
        // rides on every prompt on the map and RimTalk targets small models. Parser-style safety limit,
        // not player tuning, so it stays in code (AGENTS.md).
        private const int MaxSectionChars = 400;

        // Priority passed to RimTalk. Mid-range so other mods can order before/after us if they care.
        private const int HookPriority = 100;

        // Situation-line weights: higher is surfaced first when the block is trimmed. Parser-style
        // ordering constants (how important each kind of situation is), not player settings.
        private const int WeightThreatHigh = 100;
        private const int WeightThreatLow = 80;
        private const int WeightAnomaly = 70;
        private const int WeightCondition = 50;
        private const int WeightQuest = 30;

        // How many active game conditions / ongoing quests we are willing to list before the block's
        // own line/char caps trim further. Keeps one flooding category (e.g. many conditions) from
        // crowding out the others entirely. Parser-style caps, not player tuning.
        private const int MaxConditionLines = 3;
        private const int MaxQuestLines = 2;

        // Guards the cache: RefreshFor (main thread) writes it, SectionFor (possibly a RimTalk
        // background task) reads it.
        private static readonly object Gate = new object();

        // map.uniqueID -> cached block text + freshness bookkeeping.
        private static readonly Dictionary<int, CachedSection> Cache = new Dictionary<int, CachedSection>();

        /// <summary>
        /// Registers the {{colony_events}} environment variable and the zero-config injected section.
        /// Process-global and idempotent; call once from the mod constructor, only when RimTalk is
        /// active. Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RegisterAll()
        {
            // Expose it as an environment Scriban variable so template editors can place {{colony_events}}.
            ContextHookRegistry.RegisterEnvironmentVariable(
                BridgeIds.ColonyEventsVariableName,
                BridgeIds.ModId,
                SectionFor,
                "PawnDiaryRimTalkBridge.Prompt.ColonyEventsVariableDesc".Translate(),
                HookPriority);

            // Zero-config: inject the block right after the Weather line of the environment section.
            // ⚠️ U1 — verify in-game that RimTalk renders injected environment sections into the default
            // prompt; if not, switch to RegisterEnvironmentHook(Environment.Weather, Append, ...). Same
            // open question the shipped Feature 2 carries for InjectPawnSection (see repowiki/README.md).
            ContextHookRegistry.InjectEnvironmentSection(
                BridgeIds.ColonyEventsVariableName,
                BridgeIds.ModId,
                ContextCategories.Environment.Weather,
                ContextHookRegistry.InjectPosition.After,
                SectionFor,
                HookPriority);
        }

        /// <summary>
        /// Rebuilds and caches one map's colony-situation block. MAIN THREAD ONLY (reads live map state
        /// and calls .Translate()). Empty/ineligible maps cache "" so SectionFor can serve them cheaply.
        /// </summary>
        public static void RefreshFor(Map map)
        {
            if (map == null)
            {
                return;
            }

            string section = BuildSectionFor(map);
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            lock (Gate)
            {
                CachedSection cached;
                if (!Cache.TryGetValue(map.uniqueID, out cached))
                {
                    cached = new CachedSection();
                    Cache[map.uniqueID] = cached;
                }

                cached.Text = section ?? string.Empty;
                cached.Tick = now;
            }
        }

        /// <summary>
        /// The delegate RimTalk calls while building a prompt. Cache read only — never scans the map or
        /// translates. Returns "" when the bridge is off, the colony-context toggle is off, the master
        /// switch is off, or the map's block has not been built yet.
        /// </summary>
        public static string SectionFor(Map map)
        {
            if (map == null || !FeatureActive())
            {
                return string.Empty;
            }

            lock (Gate)
            {
                CachedSection cached;
                if (Cache.TryGetValue(map.uniqueID, out cached))
                {
                    return cached.Text ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// True when the map has no cached block yet or its cache is older than <paramref name="ttl"/>
        /// ticks. Drives the throttled refresh pass in the game component.
        /// </summary>
        public static bool NeedsRefresh(Map map, int now, int ttl)
        {
            if (map == null)
            {
                return false;
            }

            lock (Gate)
            {
                CachedSection cached;
                if (!Cache.TryGetValue(map.uniqueID, out cached))
                {
                    return true;
                }

                return (now - cached.Tick) >= ttl;
            }
        }

        /// <summary>
        /// Clears the cache. Static caches leak across exit-to-menu + load, so the game component calls
        /// this from FinalizeInit (see the "Persistence &amp; ticking" gotcha in SKILL.md).
        /// </summary>
        public static void ResetForNewGame()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        /// <summary>Drops one map's cached block (e.g. a map that no longer has colonists to talk) so
        /// <see cref="SectionFor"/> stops serving a stale situation line for it. The read path has no
        /// other expiry, so the refresh pass must clear maps it stops refreshing. Main thread.</summary>
        public static void ClearFor(int mapId)
        {
            lock (Gate)
            {
                Cache.Remove(mapId);
            }
        }

        /// <summary>Evicts cache entries for maps that no longer exist (map ids are not reused within a
        /// game, so any key not in <paramref name="liveMapIds"/> is gone for good). Main thread.</summary>
        public static void RetainOnly(ICollection<int> liveMapIds)
        {
            lock (Gate)
            {
                if (Cache.Count == 0)
                {
                    return;
                }

                List<int> drop = null;
                foreach (int id in Cache.Keys)
                {
                    if (liveMapIds == null || !liveMapIds.Contains(id))
                    {
                        (drop ?? (drop = new List<int>())).Add(id);
                    }
                }

                if (drop != null)
                {
                    for (int i = 0; i < drop.Count; i++)
                    {
                        Cache.Remove(drop[i]);
                    }
                }
            }
        }

        // True when the colony-context feature should contribute anything at all. Read from the
        // main-thread build, the background provider, and the game component's refresh gate; all reads
        // are cheap settings/flag reads.
        internal static bool FeatureActive()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                && settings != null
                && settings.injectColonyContext
                && PawnDiaryApi.IsExternalApiEnabled;
        }

        /// <summary>Builds the block text on the main thread, or "" when the map contributes nothing.</summary>
        private static string BuildSectionFor(Map map)
        {
            if (!FeatureActive())
            {
                return string.Empty;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int maxLines = settings != null ? settings.colonyEventCount : 3;
            if (maxLines <= 0)
            {
                return string.Empty;
            }

            List<ColonyEventLine> lines = new List<ColonyEventLine>();
            AddThreatLine(map, lines);
            AddConditionLines(map, lines);
            if (ModsConfig.AnomalyActive)
            {
                AddAnomalyLine(lines);
            }
            AddQuestLines(lines);

            return ColonyEventsFormat.BuildColonySituation(
                lines,
                "PawnDiaryRimTalkBridge.Prompt.ColonyEventsHeader".Translate(),
                maxLines,
                MaxSectionChars);
        }

        // Recent/active threat, summarized from the map's danger rating (never a unit list).
        private static void AddThreatLine(Map map, List<ColonyEventLine> lines)
        {
            if (map.dangerWatcher == null)
            {
                return;
            }

            StoryDanger danger = map.dangerWatcher.DangerRating;
            if (danger == StoryDanger.High)
            {
                lines.Add(new ColonyEventLine
                {
                    Text = "PawnDiaryRimTalkBridge.Colony.ThreatHigh".Translate(),
                    Weight = WeightThreatHigh
                });
            }
            else if (danger == StoryDanger.Low)
            {
                lines.Add(new ColonyEventLine
                {
                    Text = "PawnDiaryRimTalkBridge.Colony.ThreatLow".Translate(),
                    Weight = WeightThreatLow
                });
            }
        }

        // Active game conditions (toxic fallout, eclipse, cold snap, ...), labelled by the game's own
        // condition label. Capped so a busy sky does not crowd out everything else.
        private static void AddConditionLines(Map map, List<ColonyEventLine> lines)
        {
            if (map.gameConditionManager == null)
            {
                return;
            }

            List<GameCondition> active = map.gameConditionManager.ActiveConditions;
            if (active == null)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < active.Count && added < MaxConditionLines; i++)
            {
                GameCondition condition = active[i];
                if (condition == null)
                {
                    continue;
                }

                string label = condition.LabelCap;
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                lines.Add(new ColonyEventLine { Text = label, Weight = WeightCondition });
                added++;
            }
        }

        // Anomaly (DLC-gated by the caller): a vague, atmospheric note that something unnatural looms —
        // never spoilery, never naming specifics. Kept in its own method so the Anomaly type is only
        // touched after the ModsConfig.AnomalyActive gate (DLC-safety, AGENTS.md).
        private static void AddAnomalyLine(List<ColonyEventLine> lines)
        {
            GameComponent_Anomaly anomaly = Find.Anomaly;
            if (anomaly == null)
            {
                return;
            }

            // Only once the monolith has been disturbed (level > 0) is there a presence worth sensing.
            if (anomaly.Level > 0)
            {
                lines.Add(new ColonyEventLine
                {
                    Text = "PawnDiaryRimTalkBridge.Colony.Anomaly".Translate(),
                    Weight = WeightAnomaly
                });
            }
        }

        // Open quests: the top few ongoing, non-hidden quests, labelled by the quest's own name.
        private static void AddQuestLines(List<ColonyEventLine> lines)
        {
            if (Find.QuestManager == null)
            {
                return;
            }

            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            if (quests == null)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < quests.Count && added < MaxQuestLines; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.hidden || quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                string name = quest.name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                lines.Add(new ColonyEventLine
                {
                    Text = "PawnDiaryRimTalkBridge.Colony.Quest".Translate(name),
                    Weight = WeightQuest
                });
                added++;
            }
        }

        /// <summary>One map's cached colony-situation block plus the freshness bookkeeping.</summary>
        private sealed class CachedSection
        {
            /// <summary>Pre-built block text (may be "").</summary>
            public string Text = string.Empty;

            /// <summary>Tick this block was last rebuilt.</summary>
            public int Tick;
        }
    }
}
