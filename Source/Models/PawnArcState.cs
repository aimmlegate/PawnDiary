// Small persisted bookkeeping for pawn progression and rare arc-reflection cadence. These classes do
// not store a separate history database; they only remember scanner baselines, highest observed
// milestones, yearly arc counts, and recently used memory IDs so prompts do not repeat themselves.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
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
        // Snapshot of the pawn's trait keys ("<defName>|<degree>") at the last scan. The trait-gain
        // scanner diffs the live traits against this to find newly gained traits; the first scan
        // baselines it silently so traits present at pawn creation never generate a page.
        public List<string> knownTraitKeys = new List<string>();
        // Trait gain has its OWN baseline flag (not the shared one below): this field was added after
        // the scalar scanners, so a save made before it has no knownTraitKeys AND an already-false
        // baselineProgressionOnNextScan. Defaulting this to true means the first scan after upgrading
        // baselines the pawn's existing traits silently instead of spamming a page for each one.
        public bool baselineTraitGainOnNextScan = true;
        public bool baselineProgressionOnNextScan = true;
        // Additive nested Biotech state. Old/no-DLC saves load a harmless empty row; live DLC reads
        // remain in DlcContext and never occur from this save model.
        public BiotechPawnProgressionState biotechProgressionState;
        // Royalty-specific initialization is separate from the older shared progression baseline.
        // A missing version-zero row means "baseline once"; version one plus an empty title list is
        // a legitimate observed titleless pawn.
        public RoyaltyPawnProgressionState royaltyObservationState;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref skillMilestones, "skillMilestones", LookMode.Deep);
            Scribe_Values.Look(ref highestPsylinkLevelRecorded, "highestPsylinkLevelRecorded", 0);
            Scribe_Values.Look(ref lastObservedXenotypeDefName, "lastObservedXenotypeDefName");
            Scribe_Values.Look(ref lastObservedXenotypeLabel, "lastObservedXenotypeLabel");
            Scribe_Values.Look(ref lastObservedRoyalTitleDefName, "lastObservedRoyalTitleDefName");
            Scribe_Values.Look(ref lastObservedRoyalTitleLabel, "lastObservedRoyalTitleLabel");
            Scribe_Collections.Look(ref knownTraitKeys, "knownTraitKeys", LookMode.Value);
            Scribe_Values.Look(ref baselineTraitGainOnNextScan, "baselineTraitGainOnNextScan", true);
            Scribe_Values.Look(ref baselineProgressionOnNextScan, "baselineProgressionOnNextScan", true);
            Scribe_Deep.Look(ref biotechProgressionState, BiotechSaveKeys.PawnProgressionState);
            Scribe_Deep.Look(ref royaltyObservationState, RoyaltySaveKeys.PawnObservationState);

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

            if (knownTraitKeys == null)
            {
                knownTraitKeys = new List<string>();
            }

            if (biotechProgressionState == null)
            {
                biotechProgressionState = new BiotechPawnProgressionState();
            }
            biotechProgressionState.Normalize();

            if (royaltyObservationState == null)
            {
                royaltyObservationState = new RoyaltyPawnProgressionState();
            }
            royaltyObservationState.Normalize();

            HashSet<string> seenTraitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < knownTraitKeys.Count; i++)
            {
                string key = knownTraitKeys[i];
                if (string.IsNullOrWhiteSpace(key) || !seenTraitKeys.Add(key.Trim()))
                {
                    knownTraitKeys.RemoveAt(i);
                    i--;
                    continue;
                }

                knownTraitKeys[i] = key.Trim();
            }
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

        /// <summary>Returns the normalized nested Biotech bookkeeping row.</summary>
        public BiotechPawnProgressionState EnsureBiotechState()
        {
            if (biotechProgressionState == null)
            {
                biotechProgressionState = new BiotechPawnProgressionState();
            }

            biotechProgressionState.Normalize();
            return biotechProgressionState;
        }

        /// <summary>Returns the normalized nested Royalty title/psylink observation row.</summary>
        public RoyaltyPawnProgressionState EnsureRoyaltyState()
        {
            if (royaltyObservationState == null)
            {
                royaltyObservationState = new RoyaltyPawnProgressionState();
            }

            royaltyObservationState.Normalize();
            return royaltyObservationState;
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
        // Last annual forced attempt that reached memory selection but found too little evidence. This
        // throttles resting-pawn retries without marking the year's forced arc as permanently done.
        public int lastArcMemoryShortfallTick = -1;
        public int lastArcMemoryShortfallYear = int.MinValue;
        public List<string> recentlyUsedEventIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref lastArcEntryTick, "lastArcEntryTick", -1);
            Scribe_Values.Look(ref lastArcEntryYear, "lastArcEntryYear", int.MinValue);
            Scribe_Values.Look(ref arcEntriesThisYear, "arcEntriesThisYear", 0);
            Scribe_Values.Look(ref forcedArcYear, "forcedArcYear", int.MinValue);
            Scribe_Values.Look(ref lastArcMemoryShortfallTick, "lastArcMemoryShortfallTick", -1);
            Scribe_Values.Look(ref lastArcMemoryShortfallYear, "lastArcMemoryShortfallYear", int.MinValue);
            Scribe_Collections.Look(ref recentlyUsedEventIds, "recentlyUsedEventIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize(DefaultRecentMemoryCap);
            }
        }

        public void Normalize(int recentMemoryCap)
        {
            lastArcEntryTick = Math.Max(-1, lastArcEntryTick);
            lastArcMemoryShortfallTick = Math.Max(-1, lastArcMemoryShortfallTick);
            arcEntriesThisYear = Math.Max(0, arcEntriesThisYear);
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

            if (lastArcMemoryShortfallYear != currentYear)
            {
                lastArcMemoryShortfallTick = -1;
            }
        }

        /// <summary>
        /// True when an annual forced arc attempt recently failed because there were too few memories.
        /// </summary>
        public bool IsMemoryShortfallBackoffActive(int currentTick, int currentYear, int retryTicks)
        {
            if (retryTicks <= 0
                || lastArcMemoryShortfallTick < 0
                || lastArcMemoryShortfallYear != currentYear
                || currentTick < lastArcMemoryShortfallTick)
            {
                return false;
            }

            return currentTick - lastArcMemoryShortfallTick < retryTicks;
        }

        /// <summary>
        /// Records a retryable memory shortfall so the sleep scanner backs off before trying again.
        /// </summary>
        public void MarkMemoryShortfall(int tick, int year)
        {
            lastArcMemoryShortfallTick = Math.Max(-1, tick);
            lastArcMemoryShortfallYear = year;
        }

        /// <summary>
        /// Clears any pending memory-shortfall retry guard after a successful arc entry.
        /// </summary>
        public void ClearMemoryShortfall()
        {
            lastArcMemoryShortfallTick = -1;
            lastArcMemoryShortfallYear = int.MinValue;
        }

        public void MarkArcEntry(int tick, int year, bool forced, IList<string> usedEventIds, int recentMemoryCap)
        {
            if (lastArcEntryYear != year)
            {
                arcEntriesThisYear = 0;
            }

            lastArcEntryTick = tick;
            lastArcEntryYear = year;
            arcEntriesThisYear = Math.Max(0, arcEntriesThisYear) + 1;
            if (forced)
            {
                forcedArcYear = year;
            }

            ClearMemoryShortfall();

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
