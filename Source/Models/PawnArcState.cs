// Small persisted bookkeeping for pawn progression and rare arc-reflection cadence. These classes do
// not store a separate history database; they only remember scanner baselines, highest observed
// milestones, yearly arc counts, and recently used memory IDs so prompts do not repeat themselves.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Per-skill progression milestone state. Stored as a list item for save compatibility.
    /// </summary>
    public class SkillMilestoneState : IExposable
    {
        public string skillDefName;
        public int highestMilestone;

        public void ExposeData()
        {
            Scribe_Values.Look(ref skillDefName, "skillDefName");
            Scribe_Values.Look(ref highestMilestone, "highestMilestone", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                skillDefName = skillDefName ?? string.Empty;
                highestMilestone = Math.Max(0, highestMilestone);
            }
        }
    }

    /// <summary>
    /// Scanner bookkeeping for progression entries. Baseline mode suppresses old-save catch-up spam.
    /// </summary>
    public class PawnProgressionState : IExposable
    {
        public List<SkillMilestoneState> skillMilestones = new List<SkillMilestoneState>();
        public int highestPsylinkLevelRecorded;
        public string lastObservedXenotypeDefName;
        public string lastObservedXenotypeLabel;
        public string lastObservedRoyalTitleDefName;
        public string lastObservedRoyalTitleLabel;
        public bool baselineProgressionOnNextScan = true;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref skillMilestones, "skillMilestones", LookMode.Deep);
            Scribe_Values.Look(ref highestPsylinkLevelRecorded, "highestPsylinkLevelRecorded", 0);
            Scribe_Values.Look(ref lastObservedXenotypeDefName, "lastObservedXenotypeDefName");
            Scribe_Values.Look(ref lastObservedXenotypeLabel, "lastObservedXenotypeLabel");
            Scribe_Values.Look(ref lastObservedRoyalTitleDefName, "lastObservedRoyalTitleDefName");
            Scribe_Values.Look(ref lastObservedRoyalTitleLabel, "lastObservedRoyalTitleLabel");
            Scribe_Values.Look(ref baselineProgressionOnNextScan, "baselineProgressionOnNextScan", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        public void Normalize()
        {
            if (skillMilestones == null)
            {
                skillMilestones = new List<SkillMilestoneState>();
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < skillMilestones.Count; i++)
            {
                SkillMilestoneState state = skillMilestones[i];
                if (state == null || string.IsNullOrWhiteSpace(state.skillDefName))
                {
                    skillMilestones.RemoveAt(i);
                    i--;
                    continue;
                }

                state.skillDefName = state.skillDefName.Trim();
                state.highestMilestone = Math.Max(0, state.highestMilestone);
                if (!seen.Add(state.skillDefName))
                {
                    skillMilestones.RemoveAt(i);
                    i--;
                }
            }

            highestPsylinkLevelRecorded = Math.Max(0, highestPsylinkLevelRecorded);
            lastObservedXenotypeDefName = lastObservedXenotypeDefName ?? string.Empty;
            lastObservedXenotypeLabel = lastObservedXenotypeLabel ?? string.Empty;
            lastObservedRoyalTitleDefName = lastObservedRoyalTitleDefName ?? string.Empty;
            lastObservedRoyalTitleLabel = lastObservedRoyalTitleLabel ?? string.Empty;
        }

        public int HighestSkillMilestone(string skillDefName)
        {
            if (string.IsNullOrWhiteSpace(skillDefName))
            {
                return 0;
            }

            for (int i = 0; i < skillMilestones.Count; i++)
            {
                SkillMilestoneState state = skillMilestones[i];
                if (state != null && string.Equals(state.skillDefName, skillDefName, StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Max(0, state.highestMilestone);
                }
            }

            return 0;
        }

        public void SetSkillMilestone(string skillDefName, int highestMilestone)
        {
            if (string.IsNullOrWhiteSpace(skillDefName))
            {
                return;
            }

            string key = skillDefName.Trim();
            for (int i = 0; i < skillMilestones.Count; i++)
            {
                SkillMilestoneState state = skillMilestones[i];
                if (state != null && string.Equals(state.skillDefName, key, StringComparison.OrdinalIgnoreCase))
                {
                    state.highestMilestone = Math.Max(0, highestMilestone);
                    return;
                }
            }

            skillMilestones.Add(new SkillMilestoneState
            {
                skillDefName = key,
                highestMilestone = Math.Max(0, highestMilestone)
            });
        }
    }

    /// <summary>
    /// Per-pawn cadence state for rare arc reflections. Recent memory IDs prevent immediate reuse.
    /// </summary>
    public class PawnArcScheduleState : IExposable
    {
        public const int DefaultRecentMemoryCap = 16;

        public int lastArcEntryTick = -1;
        public int lastArcEntryYear = int.MinValue;
        public int arcEntriesThisYear;
        public int forcedArcYear = int.MinValue;
        public List<string> recentlyUsedEventIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref lastArcEntryTick, "lastArcEntryTick", -1);
            Scribe_Values.Look(ref lastArcEntryYear, "lastArcEntryYear", int.MinValue);
            Scribe_Values.Look(ref arcEntriesThisYear, "arcEntriesThisYear", 0);
            Scribe_Values.Look(ref forcedArcYear, "forcedArcYear", int.MinValue);
            Scribe_Collections.Look(ref recentlyUsedEventIds, "recentlyUsedEventIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize(DefaultRecentMemoryCap);
            }
        }

        public void Normalize(int recentMemoryCap)
        {
            lastArcEntryTick = Math.Max(-1, lastArcEntryTick);
            arcEntriesThisYear = Math.Max(0, Math.Min(2, arcEntriesThisYear));
            if (recentlyUsedEventIds == null)
            {
                recentlyUsedEventIds = new List<string>();
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recentlyUsedEventIds.Count; i++)
            {
                string id = recentlyUsedEventIds[i];
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    recentlyUsedEventIds.RemoveAt(i);
                    i--;
                }
            }

            int cap = Math.Max(0, recentMemoryCap);
            while (recentlyUsedEventIds.Count > cap)
            {
                recentlyUsedEventIds.RemoveAt(0);
            }
        }

        public void NormalizeForYear(int currentYear, int recentMemoryCap)
        {
            Normalize(recentMemoryCap);
            if (lastArcEntryYear != currentYear)
            {
                arcEntriesThisYear = 0;
            }
        }

        public void MarkArcEntry(int tick, int year, bool forced, IList<string> usedEventIds, int recentMemoryCap)
        {
            if (lastArcEntryYear != year)
            {
                arcEntriesThisYear = 0;
            }

            lastArcEntryTick = tick;
            lastArcEntryYear = year;
            arcEntriesThisYear = Math.Min(2, Math.Max(0, arcEntriesThisYear) + 1);
            if (forced)
            {
                forcedArcYear = year;
            }

            if (usedEventIds != null)
            {
                for (int i = 0; i < usedEventIds.Count; i++)
                {
                    string id = usedEventIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    recentlyUsedEventIds.RemoveAll(existing =>
                        string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
                    recentlyUsedEventIds.Add(id);
                }
            }

            Normalize(recentMemoryCap);
        }
    }
}
