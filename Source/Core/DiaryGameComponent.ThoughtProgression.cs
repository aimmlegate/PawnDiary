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

        private int nextThoughtProgressionScanTick;
        private bool baselineThoughtProgressionsOnNextScan;

        /// <summary>
        /// Clears the transient situational-thought snapshot. Loaded saves baseline once so already
        /// active hunger/exhaustion/etc. does not immediately duplicate an old diary page.
        /// </summary>
        private void ResetThoughtProgressionState(bool baselineNextScan)
        {
            activeThoughtProgressions.Clear();
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
                if (!IsDiaryEligible(pawn) || pawn.needs?.mood?.thoughts == null)
                {
                    continue;
                }

                thoughts.Clear();
                activeByStateKey.Clear();
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
                string pawnId = pawn.GetUniqueLoadID();

                for (int j = 0; j < thoughts.Count; j++)
                {
                    ThoughtProgressionMatch match = MatchThoughtProgression(thoughts[j], rules);
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

                foreach (KeyValuePair<string, ThoughtProgressionMatch> pair in activeByStateKey)
                {
                    seenStateKeys.Add(pair.Key);
                    UpdateThoughtProgressionState(pawn, pair.Key, pair.Value, snapshotOnly);
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
            bool recorded = Dispatch(new ThoughtProgressionSignal(
                pawn, match.thoughtDef, match.thoughtDefName, match.categoryKey, match.label,
                match.stageIndex, match.severity, match.moodOffset, worsened, stageAlreadyRecorded));
            if (recorded)
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
