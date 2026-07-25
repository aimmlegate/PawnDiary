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
    /// One remembered bonded death (Quality Wave H2). Stored as a list item, like
    /// <see cref="SkillMilestoneState"/>, so the save schema stays additive.
    /// </summary>
    public class BondedDeathMemoryState : IExposable
    {
        public string victimId;
        public string victimName;
        // Stable relation defName ("Spouse", "Child", "Bond"). Drives the strongest-bond retention
        // order; never displayed, so it must not be localized.
        public string relationDefName;
        // Already-localized relation label for the prompt ("husband", "bonded animal").
        public string relationLabel;
        public int deathTick;
        // Highest whole anniversary year already evaluated. Marked even when the recall sample fails,
        // so reloading the save cannot reroll a year that was already decided.
        public int lastProcessedAnniversaryYear;

        public void ExposeData()
        {
            Scribe_Values.Look(ref victimId, "victimId");
            Scribe_Values.Look(ref victimName, "victimName");
            Scribe_Values.Look(ref relationDefName, "relationDefName");
            Scribe_Values.Look(ref relationLabel, "relationLabel");
            Scribe_Values.Look(ref deathTick, "deathTick", 0);
            Scribe_Values.Look(ref lastProcessedAnniversaryYear, "lastProcessedAnniversaryYear", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        public void Normalize()
        {
            victimId = (victimId ?? string.Empty).Trim();
            victimName = (victimName ?? string.Empty).Trim();
            relationDefName = (relationDefName ?? string.Empty).Trim();
            relationLabel = (relationLabel ?? string.Empty).Trim();
            deathTick = Math.Max(0, deathTick);
            lastProcessedAnniversaryYear = Math.Max(0, lastProcessedAnniversaryYear);
        }

        /// <summary>True for a row that can still be matched to a victim and a death moment.</summary>
        public bool IsUsable()
        {
            return !string.IsNullOrWhiteSpace(victimId) && !string.IsNullOrWhiteSpace(relationDefName);
        }
    }

    /// <summary>
    /// One personal record's highest value ever observed (Quality Wave H2). Monotonic: a modded record
    /// reset lowers the live value but never this mark, so the same milestone cannot be awarded twice.
    /// </summary>
    public class RecordHighWaterState : IExposable
    {
        public string recordDefName;
        public float highestValue;

        public void ExposeData()
        {
            Scribe_Values.Look(ref recordDefName, "recordDefName");
            Scribe_Values.Look(ref highestValue, "highestValue", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                recordDefName = (recordDefName ?? string.Empty).Trim();
                highestValue = SafeValue(highestValue);
            }
        }

        internal static float SafeValue(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
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

        // ---- Quality Wave H2: anniversaries and personal records ---------------------------------
        // Highest biological age already accounted for. -1 means "never observed": the first scan
        // records the pawn's current age and writes nothing, so an old save gets no missed birthdays.
        public int lastObservedBiologicalAgeYears = -1;
        // Highest whole arrival year already evaluated (0 = none). Advanced for EVERY year the scanner
        // considers, including non-milestone ones, so year 4 cannot resurface as a page later.
        public int lastArrivalAnniversaryYear;
        // Remembered bonded deaths, strongest bond first, capped by XML (default 16).
        public List<BondedDeathMemoryState> bondedDeathMemories = new List<BondedDeathMemoryState>();
        // Death-discovery cursor over saved diary history. -1 means "never scanned". Monotonic: a
        // memory evicted by the cap is forgotten for good instead of being rediscovered every scan.
        public int lastBondedDeathDiscoveryTick = -1;
        // Absolute world day of the pawn's most recent bonded-death page. Enforces the locked
        // "at most one combined bonded-death page per pawn per day" rule across scans.
        public int lastBondedDeathPageDay = int.MinValue;
        // Monotonic highest value ever seen per record defName.
        public List<RecordHighWaterState> recordHighWater = new List<RecordHighWaterState>();
        // H2 has its OWN baseline flag, like trait gain: a save made before this feature has empty H2
        // state AND an already-false baselineProgressionOnNextScan, so the shared flag cannot protect
        // it. Defaulting to true makes the first scan after upgrading silently record where each pawn
        // already is instead of emitting every anniversary they ever passed.
        public bool baselineAnniversariesOnNextScan = true;

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
            // H2 additive rows. Every default matches "nothing observed yet", so an old save loads
            // silent state and the first scan baselines it.
            Scribe_Values.Look(ref lastObservedBiologicalAgeYears, "lastObservedBiologicalAgeYears", -1);
            Scribe_Values.Look(ref lastArrivalAnniversaryYear, "lastArrivalAnniversaryYear", 0);
            Scribe_Collections.Look(ref bondedDeathMemories, "bondedDeathMemories", LookMode.Deep);
            Scribe_Values.Look(ref lastBondedDeathDiscoveryTick, "lastBondedDeathDiscoveryTick", -1);
            Scribe_Values.Look(ref lastBondedDeathPageDay, "lastBondedDeathPageDay", int.MinValue);
            Scribe_Collections.Look(ref recordHighWater, "recordHighWater", LookMode.Deep);
            Scribe_Values.Look(ref baselineAnniversariesOnNextScan, "baselineAnniversariesOnNextScan", true);

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

            NormalizeAnniversaryState();
        }

        /// <summary>
        /// Repairs the Quality Wave H2 rows. Absence normalizes to "never observed" rather than to
        /// zero-as-a-real-value, which is what keeps an old save from receiving retroactive pages.
        /// </summary>
        private void NormalizeAnniversaryState()
        {
            lastObservedBiologicalAgeYears = Math.Max(-1, lastObservedBiologicalAgeYears);
            lastArrivalAnniversaryYear = Math.Max(0, lastArrivalAnniversaryYear);
            lastBondedDeathDiscoveryTick = Math.Max(-1, lastBondedDeathDiscoveryTick);

            if (bondedDeathMemories == null)
            {
                bondedDeathMemories = new List<BondedDeathMemoryState>();
            }

            HashSet<string> seenVictims = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bondedDeathMemories.Count; i++)
            {
                BondedDeathMemoryState memory = bondedDeathMemories[i];
                memory?.Normalize();
                if (memory == null || !memory.IsUsable() || !seenVictims.Add(memory.victimId))
                {
                    bondedDeathMemories.RemoveAt(i);
                    i--;
                }
            }

            if (recordHighWater == null)
            {
                recordHighWater = new List<RecordHighWaterState>();
            }

            HashSet<string> seenRecords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recordHighWater.Count; i++)
            {
                RecordHighWaterState record = recordHighWater[i];
                if (record == null || string.IsNullOrWhiteSpace(record.recordDefName))
                {
                    recordHighWater.RemoveAt(i);
                    i--;
                    continue;
                }

                record.recordDefName = record.recordDefName.Trim();
                record.highestValue = RecordHighWaterState.SafeValue(record.highestValue);
                if (!seenRecords.Add(record.recordDefName))
                {
                    recordHighWater.RemoveAt(i);
                    i--;
                }
            }
        }

        /// <summary>Returns the remembered bonded death for this victim, or null.</summary>
        public BondedDeathMemoryState FindBondedDeathMemory(string victimId)
        {
            if (string.IsNullOrWhiteSpace(victimId) || bondedDeathMemories == null)
            {
                return null;
            }

            for (int i = 0; i < bondedDeathMemories.Count; i++)
            {
                BondedDeathMemoryState memory = bondedDeathMemories[i];
                if (memory != null && string.Equals(memory.victimId, victimId, StringComparison.Ordinal))
                {
                    return memory;
                }
            }

            return null;
        }

        /// <summary>Highest value ever observed for one record defName; 0 when never observed.</summary>
        public float HighestRecordValue(string recordDefName)
        {
            if (string.IsNullOrWhiteSpace(recordDefName) || recordHighWater == null)
            {
                return 0f;
            }

            for (int i = 0; i < recordHighWater.Count; i++)
            {
                RecordHighWaterState record = recordHighWater[i];
                if (record != null
                    && string.Equals(record.recordDefName, recordDefName, StringComparison.OrdinalIgnoreCase))
                {
                    return RecordHighWaterState.SafeValue(record.highestValue);
                }
            }

            return 0f;
        }

        /// <summary>
        /// Raises one record's high-water mark. Never lowers it: a modded record reset must not make an
        /// already-awarded milestone available again.
        /// </summary>
        public void SetRecordHighWater(string recordDefName, float value)
        {
            if (string.IsNullOrWhiteSpace(recordDefName))
            {
                return;
            }

            if (recordHighWater == null)
            {
                recordHighWater = new List<RecordHighWaterState>();
            }

            string key = recordDefName.Trim();
            float safeValue = RecordHighWaterState.SafeValue(value);
            for (int i = 0; i < recordHighWater.Count; i++)
            {
                RecordHighWaterState record = recordHighWater[i];
                if (record != null
                    && string.Equals(record.recordDefName, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (safeValue > record.highestValue)
                    {
                        record.highestValue = safeValue;
                    }

                    return;
                }
            }

            recordHighWater.Add(new RecordHighWaterState
            {
                recordDefName = key,
                highestValue = safeValue
            });
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
