// Pure Quality Wave H2 policy: which anniversaries and personal records are worth one diary page,
// and when. The impure half (Source/Core/DiaryGameComponent.Anniversaries.cs) reads live pawn state
// — age, relations, records, saved diary history — copies plain facts here, and emits whatever this
// file says should be written. Nothing below touches RimWorld, so the pure harness covers every
// boundary year, decay step, and ordering rule.
//
// Four rules carry the feature:
//   * Nothing is retroactive. The first scan for a pawn only records where they already are; an old
//     save therefore never receives a burst of missed birthdays or milestones.
//   * A story is owned once. If a direct page already covers this exact birthday (Biotech's growth
//     letter, the ordinary birthday event window), the anniversary scanner stays silent.
//   * Records only ever go up. Every threshold is compared against a monotonic high-water mark, so a
//     modded record reset cannot re-award the same milestone.
//   * Sampling is deterministic. Bonded-death recall past the guaranteed years is a stable hash of
//     (pawn, victim, anniversary year), so a reload cannot reroll it and the mod never perturbs
//     RimWorld's own seeded Rand stream (Quality Wave locked decision 17).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>
    /// One XML-authored personal record and the thresholds worth a page. <see cref="recordDefName"/>
    /// is a plain string, never a Def reference, so a record from an absent mod is simply never found.
    /// </summary>
    public sealed class RecordMilestoneRule
    {
        public string recordDefName;
        public List<int> thresholds = new List<int>();
    }

    /// <summary>
    /// One remembered bonded death, as plain facts. <see cref="bondPriority"/> is the row's index in
    /// the XML strongest-first relation list, so a lower number is a closer bond; -1 means the
    /// relation is not one this feature remembers at all.
    /// </summary>
    internal sealed class BondedDeathCandidate
    {
        public string victimId;
        public string victimName;

        /// <summary>Stable relation defName ("Spouse", "Bond"…). Used for priority, never displayed.</summary>
        public string relationDefName;

        /// <summary>Already-localized relation label for the prompt ("husband", "bonded animal").</summary>
        public string relationLabel;

        public int bondPriority;
        public int deathTick;

        /// <summary>Highest whole anniversary year already evaluated; 0 means none yet.</summary>
        public int lastProcessedAnniversaryYear;
    }

    /// <summary>One newly crossed record threshold, plus the high-water value to persist.</summary>
    internal sealed class RecordMilestoneCrossing
    {
        public string recordDefName;
        public int threshold;
        public float newHighWater;
        public bool shouldEmit;
    }

    /// <summary>
    /// Owns every pure H2 decision so the scanner, the tests, and the in-game suite all agree on the
    /// same boundaries, ordering, and ownership keys.
    /// </summary>
    internal static class AnniversaryPolicy
    {
        // ---- Stable progression kinds. Save tokens: never localize, never renumber. ----
        public const string KindBirthday = "birthday";
        public const string KindArrivalAnniversary = "arrival_anniversary";
        public const string KindDeathAnniversary = "death_anniversary";
        public const string KindRecord = "record";

        // ---- Stable context keys. Schema labels, not prompt prose. ----
        // The exact-ownership key one anniversary page writes about itself, so a later scan (or a
        // reload) can see the story is already told without matching translated wording.
        public const string OwnershipContextKey = "anniversary_key";
        public const string AnniversaryYearContextKey = "anniversary_year";
        public const string RememberedContextKey = "remembered";
        public const string RecordNameContextKey = "record_name";
        public const string RecordValueContextKey = "record_value";

        // Shared with Biotech growth (BiotechContextKeys.BirthdayAge) on purpose: both mean "the age
        // this page is about", and reusing the token keeps one required prompt fact instead of two.
        public const string BirthdayAgeContextKey = "birthday_age";

        // The window key RimWorld's ordinary birthday event window records under, and the fields it
        // stores its subject and age in. Matching these is how the anniversary scanner recognizes that
        // the existing birthday page already owns this age.
        public const string EventWindowBirthdayKey = "Birthday";
        public const string EventWindowKeyContextKey = "event_window";
        public const string EventWindowSubjectIdContextKey = "subject_id";
        public const string EventWindowLabelContextKey = "label";

        /// <summary>
        /// Whole years elapsed between two ticks on the same timeline. Negative spans and a
        /// nonsensical year length both return 0, so a corrupt saved tick can never invent an
        /// anniversary. Widened to <c>long</c> so adversarial test ticks cannot overflow.
        /// </summary>
        public static int YearsBetween(int fromTick, int toTick, int ticksPerYear)
        {
            if (ticksPerYear <= 0)
            {
                return 0;
            }

            long span = (long)toTick - fromTick;
            return span <= 0L ? 0 : (int)(span / ticksPerYear);
        }

        /// <summary>
        /// True when a whole arrival year is one the player asked to hear about: any explicitly
        /// configured milestone, or — past the highest configured one — every
        /// <paramref name="recurringIntervalYears"/> step measured from that highest milestone.
        /// With the shipped [1,2,3,5,10] + 5 that means 1, 2, 3, 5, 10, 15, 20, … and never 4 or 6.
        /// </summary>
        public static bool IsArrivalMilestoneYear(
            int year,
            IList<int> milestoneYears,
            int recurringIntervalYears)
        {
            if (year <= 0)
            {
                return false;
            }

            int highestConfigured = 0;
            if (milestoneYears != null)
            {
                for (int i = 0; i < milestoneYears.Count; i++)
                {
                    int milestone = milestoneYears[i];
                    if (milestone <= 0)
                    {
                        continue;
                    }

                    if (milestone == year)
                    {
                        return true;
                    }

                    if (milestone > highestConfigured)
                    {
                        highestConfigured = milestone;
                    }
                }
            }

            return recurringIntervalYears > 0
                && year > highestConfigured
                && (year - highestConfigured) % recurringIntervalYears == 0;
        }

        /// <summary>
        /// Decides the birthday to consider. Returns the age to write about, or 0 for "nothing new".
        /// <paramref name="newObservedAge"/> is always the value to persist, so a baseline pass or a
        /// suppressed page still advances the pawn past this birthday for good.
        /// </summary>
        public static int BirthdayToConsider(
            int currentAgeYears,
            int lastObservedAgeYears,
            bool baselineMode,
            out int newObservedAge)
        {
            newObservedAge = Math.Max(lastObservedAgeYears, currentAgeYears);
            if (currentAgeYears <= 0 || baselineMode || lastObservedAgeYears < 0)
            {
                // A never-observed pawn (-1) baselines silently, exactly like the skill scanner: their
                // existing age is where they start, not a birthday they just had.
                return 0;
            }

            return currentAgeYears > lastObservedAgeYears ? currentAgeYears : 0;
        }

        /// <summary>
        /// True when RimWorld's ordinary birthday event-window page already owns this exact birthday.
        /// (The other owner — a Biotech growth page — is recognized by the existing
        /// <c>GrowthRecordPolicy.Matches</c>, which the scanner calls directly.)
        ///
        /// All three parts are stable schema values — the window key, the subject's stable pawn ID, and
        /// the age as a plain number — so this never degrades into matching translated prose.
        /// </summary>
        public static bool EventWindowPageOwnsBirthday(
            string recordedWindowKey,
            string recordedSubjectId,
            string recordedLabel,
            string pawnId,
            int age)
        {
            return age > 0
                && !string.IsNullOrWhiteSpace(pawnId)
                && string.Equals(recordedWindowKey, EventWindowBirthdayKey, StringComparison.Ordinal)
                && string.Equals(recordedSubjectId, pawnId, StringComparison.Ordinal)
                && string.Equals(
                    recordedLabel,
                    age.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// The relation's strength rank: its index in the XML strongest-first list, or -1 when the
        /// relation is not one this feature remembers. Only ranked relations are ever admitted, which
        /// is also what keeps the feature DLC-safe — the list is plain defName strings, so a relation
        /// from content the player does not own simply never matches.
        /// </summary>
        public static int BondPriority(string relationDefName, IList<string> strongestFirstDefNames)
        {
            if (string.IsNullOrWhiteSpace(relationDefName) || strongestFirstDefNames == null)
            {
                return -1;
            }

            string defName = relationDefName.Trim();
            for (int i = 0; i < strongestFirstDefNames.Count; i++)
            {
                if (string.Equals(strongestFirstDefNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Keeps at most <paramref name="cap"/> memories: closest bonds first, then the most recent
        /// deaths, then the lower victim ID. The order is total, so eviction cannot depend on the
        /// iteration order the caller happened to discover relations in. Rows the caller could not
        /// rank (priority &lt; 0) or identify are dropped.
        /// </summary>
        public static List<BondedDeathCandidate> RetainStrongestBonds(
            IList<BondedDeathCandidate> candidates,
            int cap)
        {
            List<BondedDeathCandidate> retained = new List<BondedDeathCandidate>();
            if (candidates == null || cap <= 0)
            {
                return retained;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                BondedDeathCandidate candidate = candidates[i];
                if (candidate != null
                    && candidate.bondPriority >= 0
                    && !string.IsNullOrWhiteSpace(candidate.victimId))
                {
                    retained.Add(candidate);
                }
            }

            retained.Sort(CompareRetentionOrder);
            while (retained.Count > cap)
            {
                retained.RemoveAt(retained.Count - 1);
            }

            return retained;
        }

        /// <summary>
        /// Closest bond wins; equal bonds prefer the more recent death; an exact tie falls back to the
        /// ordinal victim ID. List.Sort is not stable, so this comparison is deliberately total.
        /// </summary>
        public static int CompareRetentionOrder(BondedDeathCandidate left, BondedDeathCandidate right)
        {
            if (left == null)
            {
                return right == null ? 0 : 1;
            }

            if (right == null)
            {
                return -1;
            }

            if (left.bondPriority != right.bondPriority)
            {
                return left.bondPriority < right.bondPriority ? -1 : 1;
            }

            if (left.deathTick != right.deathTick)
            {
                return left.deathTick > right.deathTick ? -1 : 1;
            }

            return string.CompareOrdinal(left.victimId ?? string.Empty, right.victimId ?? string.Empty);
        }

        /// <summary>
        /// The chance a bonded death is remembered on a given anniversary. The first
        /// <paramref name="guaranteedThroughYear"/> years are certain — grief that fresh does not need
        /// a die roll. The next year uses <paramref name="firstDecayChance"/>, and every year after
        /// multiplies by <paramref name="decayMultiplier"/> until it settles on
        /// <paramref name="floorChance"/>, so an old loss becomes rare but never impossible.
        /// </summary>
        public static float RecallChance(
            int anniversaryYear,
            int guaranteedThroughYear,
            float firstDecayChance,
            float decayMultiplier,
            float floorChance)
        {
            if (anniversaryYear <= 0)
            {
                return 0f;
            }

            int guaranteed = Math.Max(0, guaranteedThroughYear);
            if (anniversaryYear <= guaranteed)
            {
                return 1f;
            }

            float floor = ClampChance(floorChance);
            float chance = ClampChance(firstDecayChance);
            float multiplier = ClampChance(decayMultiplier);
            // Bounded loop: the chance reaches the floor after a handful of steps, and the guard keeps
            // a hostile config (multiplier 1.0) from spinning on a far-future year.
            for (int year = guaranteed + 1; year < anniversaryYear && chance > floor; year++)
            {
                chance *= multiplier;
            }

            return chance < floor ? floor : chance;
        }

        /// <summary>Half-open comparison, matching every other deterministic sample in this repo.</summary>
        public static bool ShouldRecall(float roll, float chance)
        {
            if (float.IsNaN(roll) || float.IsInfinity(roll) || roll < 0f || roll >= 1f)
            {
                return false;
            }

            float probability = ClampChance(chance);
            if (probability <= 0f)
            {
                return false;
            }

            return probability >= 1f || roll < probability;
        }

        /// <summary>
        /// A process-stable FNV-1a roll in [0, 1) for one pawn remembering one victim on one
        /// anniversary. Each (pawn, victim, year) triple samples independently, and re-running the
        /// scanner or reloading the save cannot change the answer. Deliberately does not touch
        /// Verse.Rand.
        /// </summary>
        public static float DeterministicRoll(string pawnId, string victimId, int anniversaryYear)
        {
            uint hash = 2166136261u;
            AddStableText(ref hash, pawnId);
            AddStableText(ref hash, victimId);
            AddStableInt(ref hash, anniversaryYear);
            // Twenty-four retained bits map exactly into a float and cannot round the maximum to 1.
            return (hash >> 8) / 16777216f;
        }

        /// <summary>
        /// Picks which of today's due memories appear in the combined page: closest bonds first, up to
        /// <paramref name="displayCap"/>. The caller still marks EVERY due memory processed, including
        /// the ones past the cap — a year that was evaluated must never be evaluated again.
        /// </summary>
        public static List<BondedDeathCandidate> SelectCoincident(
            IList<BondedDeathCandidate> dueMemories,
            int displayCap)
        {
            return RetainStrongestBonds(dueMemories, Math.Max(0, displayCap));
        }

        /// <summary>
        /// Evaluates one record against its thresholds. Emits only the single highest threshold newly
        /// passed, and always reports the monotonic high-water value to persist. Baseline mode records
        /// where the pawn already is without emitting.
        /// </summary>
        public static RecordMilestoneCrossing EvaluateRecord(
            string recordDefName,
            float currentValue,
            float previousHighWater,
            IList<int> thresholds,
            bool baselineMode)
        {
            RecordMilestoneCrossing crossing = new RecordMilestoneCrossing
            {
                recordDefName = (recordDefName ?? string.Empty).Trim(),
                threshold = 0,
                newHighWater = HighestValue(previousHighWater, currentValue),
                shouldEmit = false
            };

            if (baselineMode || thresholds == null || crossing.recordDefName.Length == 0)
            {
                return crossing;
            }

            int best = 0;
            for (int i = 0; i < thresholds.Count; i++)
            {
                int threshold = thresholds[i];
                // "Newly crossed" is measured against the high-water mark, not the live value, so a
                // record that a mod reset and then rebuilt cannot award the same milestone twice.
                if (threshold > 0 && threshold <= currentValue && threshold > previousHighWater
                    && threshold > best)
                {
                    best = threshold;
                }
            }

            if (best > 0)
            {
                crossing.threshold = best;
                crossing.shouldEmit = true;
            }

            return crossing;
        }

        /// <summary>Highest of the two values, treating NaN/infinity as "no information".</summary>
        public static float HighestValue(float previous, float current)
        {
            float safePrevious = SafeValue(previous);
            float safeCurrent = SafeValue(current);
            return safeCurrent > safePrevious ? safeCurrent : safePrevious;
        }

        // ---- Exact-ownership keys ----------------------------------------------------------------
        // Each anniversary page stores its own key so a later scan can see the story is already told.
        // Blank parts return an empty key, which callers treat as "cannot claim ownership" and skip.

        public static string BirthdayOwnershipKey(string pawnId, int age)
        {
            return Compose(KindBirthday, pawnId, age.ToString(CultureInfo.InvariantCulture));
        }

        public static string ArrivalOwnershipKey(string pawnId, int year)
        {
            return Compose(KindArrivalAnniversary, pawnId, year.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The combined bonded-death key. Victim IDs are sorted so the key does not depend on the
        /// order the memories happened to be discovered in.
        /// </summary>
        public static string DeathOwnershipKey(string pawnId, int year, IList<string> victimIds)
        {
            List<string> ids = new List<string>();
            if (victimIds != null)
            {
                for (int i = 0; i < victimIds.Count; i++)
                {
                    string id = SafeKeyPart(victimIds[i]);
                    if (id.Length > 0 && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            if (ids.Count == 0)
            {
                return string.Empty;
            }

            ids.Sort(StringComparer.Ordinal);
            return Compose(
                KindDeathAnniversary,
                pawnId,
                year.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", ids.ToArray()));
        }

        public static string RecordOwnershipKey(string pawnId, string recordDefName, int threshold)
        {
            string record = SafeKeyPart(recordDefName);
            if (record.Length == 0)
            {
                return string.Empty;
            }

            return Compose(
                KindRecord,
                pawnId,
                record + "|" + threshold.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Formats the remembered dead for the prompt: "Brik (husband), Ash (bonded animal)". Already
        /// localized labels in, one prompt-safe line out; no IDs, ticks, or year numbers leak.
        /// </summary>
        public static string FormatRemembered(IList<BondedDeathCandidate> memories)
        {
            if (memories == null || memories.Count == 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < memories.Count; i++)
            {
                BondedDeathCandidate memory = memories[i];
                string name = SafeFieldText(memory?.victimName);
                if (name.Length == 0)
                {
                    continue;
                }

                string relation = SafeFieldText(memory.relationLabel);
                parts.Add(relation.Length == 0 ? name : name + " (" + relation + ")");
            }

            // Commas, never "; ". This line is written into the "; key=value" context string as both
            // label= and remembered=, so a semicolon separator reads back as the start of a new field
            // and silently truncates the list to its first name. Every other multi-value context field
            // joins with JoinComma for exactly this reason.
            return DiaryListText.JoinComma(parts);
        }

        private static string Compose(string kind, string pawnId, string suffix)
        {
            string pawn = SafeKeyPart(pawnId);
            return pawn.Length == 0 ? string.Empty : kind + "|" + pawn + "|" + suffix;
        }

        // Key parts must survive the "; key=value" context grammar untouched, and '|' is our own
        // separator. A part carrying any of those characters is rejected rather than silently mangled,
        // because a corrupted ownership key is a key that could claim the wrong story.
        private static string SafeKeyPart(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0
                || text.IndexOf(';') >= 0
                || text.IndexOf('=') >= 0
                || text.IndexOf('|') >= 0
                || text.IndexOf(',') >= 0)
            {
                return string.Empty;
            }

            return text;
        }

        // Display text only needs the context grammar protected; commas and pipes are fine in a name.
        private static string SafeFieldText(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Replace(';', ',').Replace('=', '-');
        }

        private static float SafeValue(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        private static float ClampChance(float chance)
        {
            if (float.IsNaN(chance) || float.IsInfinity(chance))
            {
                return 0f;
            }

            if (chance <= 0f) return 0f;
            return chance >= 1f ? 1f : chance;
        }

        private static void AddStableInt(ref uint hash, int value)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= 16777619u;
                }
            }
        }

        private static void AddStableText(ref uint hash, string value)
        {
            string text = value ?? string.Empty;
            // Hash the length first so component boundaries stay unambiguous without allocating a
            // formatted number string.
            AddStableInt(ref hash, text.Length);
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    // Hash both bytes of the UTF-16 code unit so non-ASCII IDs remain stable.
                    char current = text[i];
                    hash ^= (byte)(current & 0xFF);
                    hash *= 16777619u;
                    hash ^= (byte)(current >> 8);
                    hash *= 16777619u;
                }
            }
        }
    }
}
