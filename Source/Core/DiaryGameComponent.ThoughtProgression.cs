// Thought progression scanner — situational need thoughts (hunger, exhaustion, outdoors deprivation,
// chemical desire) do not pass through MemoryThoughtHandler.TryGainMemory. RimWorld exposes them as
// currently-active stages on a ThoughtDef, so this file periodically scans each colonist's visible
// mood thoughts and records only configured worsening stages. It remembers the active episode so a
// pawn does not write the same stage over and over while the condition persists.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private readonly Dictionary<string, ActiveThoughtProgressionState> activeThoughtProgressions =
            new Dictionary<string, ActiveThoughtProgressionState>();

        // A Brainwipe normally snapshots the pawn's live thoughts immediately. If a broken modded
        // thought makes GetAllMoodThoughts fail as a whole, remember only that pawn here. Their next
        // successful scan becomes the missing silent baseline; other pawns continue normally.
        private readonly HashSet<string> thoughtProgressionPawnBaselinesPending =
            new HashSet<string>(StringComparer.Ordinal);

        private int nextThoughtProgressionScanTick;
        private bool baselineThoughtProgressionsOnNextScan;

        /// <summary>
        /// Clears the transient situational-thought snapshot. Loaded saves baseline once so already
        /// active hunger/exhaustion/etc. does not immediately duplicate an old diary page.
        /// </summary>
        private void ResetThoughtProgressionState(bool baselineNextScan)
        {
            activeThoughtProgressions.Clear();
            thoughtProgressionPawnBaselinesPending.Clear();
            nextThoughtProgressionScanTick = 0;
            baselineThoughtProgressionsOnNextScan = baselineNextScan;
        }

        /// <summary>
        /// Scans each free colonist for configured situational thought stages and emits a diary page
        /// only when a category first appears or worsens to a not-yet-recorded stage.
        /// </summary>
        private void ScanThoughtProgressionsForDiaryEvents(bool snapshotOnly)
        {
            if (PawnDiaryMod.Settings == null || !DiarySignalPolicies.Enabled(DiarySignalPolicies.ThoughtProgression))
            {
                return;
            }

            List<ThoughtProgressionRule> rules = DiarySignalPolicies.ThoughtProgressionRules;
            if (rules == null || rules.Count == 0)
            {
                activeThoughtProgressions.Clear();
                return;
            }

            HashSet<string> seenStateKeys = new HashSet<string>();
            Dictionary<string, ThoughtProgressionMatch> activeByStateKey = new Dictionary<string, ThoughtProgressionMatch>();
            List<Thought> thoughts = new List<Thought>();
            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                string pawnId = pawn.GetUniqueLoadID();
                bool pawnSnapshotOnly = thoughtProgressionPawnBaselinesPending.Contains(pawnId);
                if (!TryCollectThoughtProgressionsForPawn(
                    pawn, rules, thoughts, activeByStateKey))
                {
                    continue;
                }

                foreach (KeyValuePair<string, ThoughtProgressionMatch> pair in activeByStateKey)
                {
                    seenStateKeys.Add(pair.Key);
                    UpdateThoughtProgressionState(
                        pawn,
                        pair.Key,
                        pair.Value,
                        snapshotOnly || pawnSnapshotOnly);
                }

                // Consume only after collection and every snapshot update succeeded. A later scan
                // may now treat genuinely new/worsened stages as post-wipe events for this pawn.
                if (pawnSnapshotOnly)
                {
                    thoughtProgressionPawnBaselinesPending.Remove(pawnId);
                }
            }

            // If a tracked thought disappeared, clear only the episode state. The user explicitly
            // asked not to generate a "good/recovered" page on disappearance.
            List<string> staleKeys = new List<string>();
            foreach (string key in activeThoughtProgressions.Keys)
            {
                if (!seenStateKeys.Contains(key))
                {
                    staleKeys.Add(key);
                }
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                activeThoughtProgressions.Remove(staleKeys[i]);
            }
        }

        /// <summary>
        /// Replaces only one pawn's active-thought episode snapshot with their state at Brainwipe.
        /// This is deliberately independent from the global next-scan baseline flag: other pawns keep
        /// their episodes, while an unscanned pre-wipe worsening cannot become post-wipe autobiography.
        /// </summary>
        private void RebaselineThoughtProgressionsForPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            List<string> ownedKeys = new List<string>();
            foreach (string key in activeThoughtProgressions.Keys)
            {
                if (PawnScopedTransientKeyPolicy.StartsWithPawnToken(key, pawnId))
                {
                    ownedKeys.Add(key);
                }
            }
            for (int i = 0; i < ownedKeys.Count; i++)
            {
                activeThoughtProgressions.Remove(ownedKeys[i]);
            }

            thoughtProgressionPawnBaselinesPending.Add(pawnId);

            List<ThoughtProgressionRule> rules = DiarySignalPolicies.ThoughtProgressionRules;
            if (rules == null || rules.Count == 0)
            {
                return;
            }

            Dictionary<string, ThoughtProgressionMatch> activeByStateKey =
                new Dictionary<string, ThoughtProgressionMatch>();
            if (!TryCollectThoughtProgressionsForPawn(
                pawn,
                rules,
                new List<Thought>(),
                activeByStateKey,
                isolateFailures: true))
            {
                return;
            }

            foreach (KeyValuePair<string, ThoughtProgressionMatch> pair in activeByStateKey)
            {
                UpdateThoughtProgressionState(pawn, pair.Key, pair.Value, snapshotOnly: true);
            }
            thoughtProgressionPawnBaselinesPending.Remove(pawnId);
        }

        /// <summary>Collects one pawn's strongest configured thought per category without emitting.</summary>
        private static bool TryCollectThoughtProgressionsForPawn(
            Pawn pawn,
            List<ThoughtProgressionRule> rules,
            List<Thought> thoughts,
            Dictionary<string, ThoughtProgressionMatch> activeByStateKey,
            bool isolateFailures = false)
        {
            thoughts.Clear();
            activeByStateKey.Clear();
            if (!IsDiaryEligible(pawn) || pawn.needs?.mood?.thoughts == null)
            {
                return false;
            }

            // GetAllMoodThoughts itself calls MoodOffset() on every thought, and a modded thought
            // class can throw inside its own MoodOffset override before the per-thought guard below.
            try
            {
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            }
            catch (Exception exception)
            {
                if (!isolateFailures)
                {
                    // Preserve the ordinary scanner's all-or-nothing stale-state cleanup. Its caller
                    // must not interpret an unreadable pawn as having recovered from every thought.
                    throw;
                }

                Log.ErrorOnce(
                    "[Pawn Diary] Brainwipe could not read the pawn's live mood thoughts while "
                    + "rebuilding a silent progression baseline; the next successful scan will "
                    + "baseline only this pawn: " + exception,
                    "PawnDiary.Brainwipe.ThoughtRebaselineCollection".GetHashCode());
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < thoughts.Count; i++)
            {
                ThoughtProgressionMatch match;
                try
                {
                    match = MatchThoughtProgression(thoughts[i], rules);
                }
                catch (Exception exception)
                {
                    if (!isolateFailures)
                    {
                        // As above, an ordinary partial snapshot must never drive stale-state removal.
                        throw;
                    }

                    // A partial snapshot is not a safe memory boundary: if this thought becomes
                    // readable next scan, it could otherwise emit its pre-wipe stage as first-seen.
                    // Keep the target-only retry marker until one complete scan can baseline them all.
                    Log.ErrorOnce(
                        "[Pawn Diary] Brainwipe skipped one malformed live thought while rebuilding "
                        + "the pawn's silent progression baseline; the next complete scan will retry "
                        + "only this pawn: " + exception,
                        "PawnDiary.Brainwipe.ThoughtRebaselineRow".GetHashCode());
                    activeByStateKey.Clear();
                    return false;
                }

                if (match == null)
                {
                    continue;
                }

                string stateKey = ThoughtProgressionStateKey(pawnId, match.categoryKey);
                ThoughtProgressionMatch existing;
                if (activeByStateKey.TryGetValue(stateKey, out existing)
                    && existing.severity >= match.severity)
                {
                    continue;
                }
                activeByStateKey[stateKey] = match;
            }

            return true;
        }

        private void UpdateThoughtProgressionState(Pawn pawn, string stateKey, ThoughtProgressionMatch match, bool snapshotOnly)
        {
            ActiveThoughtProgressionState state;
            bool firstSeen = !activeThoughtProgressions.TryGetValue(stateKey, out state);
            if (firstSeen)
            {
                state = new ActiveThoughtProgressionState();
                activeThoughtProgressions[stateKey] = state;
            }

            string stageKey = match.thoughtDefName + "|" + match.stageIndex.ToString();
            bool worsened = firstSeen || match.severity > state.currentSeverity;
            state.currentSeverity = match.severity;
            state.currentStageKey = stageKey;

            if (snapshotOnly)
            {
                state.recordedStageKeys.Add(stageKey);
                return;
            }

            bool stageAlreadyRecorded = state.recordedStageKeys.Contains(stageKey);
            // Dispatch directly (not the void Submit façade) so we learn whether the event actually
            // recorded — the recorded-stage set updates only on success, exactly like the old
            // RecordThoughtProgression bool return.
            DiaryDispatchOutcome outcome = DispatchWithOutcome(new ThoughtProgressionSignal(
                pawn, match.thoughtDef, match.thoughtDefName, match.categoryKey, match.label,
                match.stageIndex, match.severity, match.moodOffset, worsened, stageAlreadyRecorded));
            if (DiaryDispatchOutcomePolicy.SettlesSource(outcome))
            {
                state.recordedStageKeys.Add(stageKey);
            }
        }

        private static ThoughtProgressionMatch MatchThoughtProgression(Thought thought, List<ThoughtProgressionRule> rules)
        {
            if (thought == null || thought.def == null || !thought.VisibleInNeedsTab)
            {
                return null;
            }

            string thoughtDefName = thought.def.defName;
            int stageIndex = thought.CurStageIndex;
            for (int i = 0; i < rules.Count; i++)
            {
                ThoughtProgressionRule rule = rules[i];
                if (rule == null
                    || string.IsNullOrWhiteSpace(rule.thoughtDefName)
                    || !string.Equals(rule.thoughtDefName, thoughtDefName, StringComparison.OrdinalIgnoreCase)
                    || rule.stages == null)
                {
                    continue;
                }

                for (int j = 0; j < rule.stages.Count; j++)
                {
                    ThoughtProgressionStage stage = rule.stages[j];
                    if (stage != null && stage.stageIndex == stageIndex)
                    {
                        return new ThoughtProgressionMatch
                        {
                            categoryKey = string.IsNullOrWhiteSpace(rule.categoryKey) ? thoughtDefName : rule.categoryKey,
                            thoughtDef = thought.def,
                            thoughtDefName = thoughtDefName,
                            stageIndex = stageIndex,
                            severity = stage.severity,
                            label = DiaryLineCleaner.CleanLine(thought.LabelCap),
                            moodOffset = thought.MoodOffset()
                        };
                    }
                }
            }

            return null;
        }

        private static string ThoughtProgressionStateKey(string pawnId, string categoryKey)
        {
            return pawnId + "|" + categoryKey;
        }

        private class ActiveThoughtProgressionState
        {
            public int currentSeverity;
            public string currentStageKey;
            public readonly HashSet<string> recordedStageKeys = new HashSet<string>();
        }

        private class ThoughtProgressionMatch
        {
            public string categoryKey;
            public ThoughtDef thoughtDef;
            public string thoughtDefName;
            public int stageIndex;
            public int severity;
            public string label;
            public float moodOffset;
        }
    }
}
