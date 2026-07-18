// Pure mechanitor-lifecycle contracts and decisions. Runtime adapters copy guarded Biotech state
// into these plain rows; this file intentionally has no Verse, RimWorld, Harmony, or settings
// dependency so tenure/name/combat decisions remain deterministic and standalone-testable.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Stable synthetic progression names for the Phase-6 mechanitor chapters.</summary>
    internal static class MechanitorEventDefNames
    {
        public const string MechlinkInstalled = "BiotechMechlinkInstalled";
        public const string MechlinkRemoved = "BiotechMechlinkRemoved";
        public const string FirstControlledMech = "BiotechFirstControlledMech";
        public const string FirstControlledMechCombat = "BiotechFirstControlledMechCombat";
        public const string SignificantMechLoss = "BiotechSignificantMechLoss";
        public const string BossCalled = "BiotechBossCalled";
        public const string BossDefeated = "BiotechBossDefeated";
    }

    /// <summary>Stable mechanitor context labels; these are schema tokens, not localized prose.</summary>
    internal static class MechanitorContextKeys
    {
        public const string MechanitorMoment = "mechanitor_moment";
        public const string MechId = "mech_id";
        public const string MechName = "mech_name";
        public const string MechKind = "mech_kind";
        public const string PlayerNamed = "player_named";
        public const string ServiceTicks = "service_ticks";
        public const string LongServing = "long_serving";
        public const string CombatTale = "combat_tale";
        public const string CombatTarget = "combat_target";
        public const string BossgroupDef = "bossgroup_def";
        public const string BossDef = "boss_def";
        public const string BossLabel = "boss_label";
        public const string CalledTick = "called_tick";
        public const string ThreatCalled = "threat_called";
        public const string ThreatDefeated = "threat_defeated";
    }

    /// <summary>Plain exact mech facts copied while an Overseer relation still exists.</summary>
    internal class MechanitorMechSnapshot
    {
        public string mechId = string.Empty;
        public string displayName = string.Empty;
        public string kindDefName = string.Empty;
        public string kindLabel = string.Empty;
        public int relationStartTick;
        public bool controlled;
        public bool hasExplicitName;
        public bool numericalName;
    }

    /// <summary>Detached current controller baseline used by old-save-safe observation.</summary>
    internal class MechanitorControllerSnapshot
    {
        public string controllerId = string.Empty;
        public bool hasMechlink;
        public List<MechanitorMechSnapshot> overseenMechs = new List<MechanitorMechSnapshot>();
    }

    /// <summary>Saved bounded tenure row for one exact overseen mech.</summary>
    internal partial class MechanitorMechObservationState
    {
        public string mechId = string.Empty;
        public string lastDisplayName = string.Empty;
        public string kindDefName = string.Empty;
        public int firstObservedTick;
        public bool lossObserved;
    }

    /// <summary>Saved exact caller ownership for one boss threat until its boss is defeated.</summary>
    internal partial class MechanitorBossCallObservationState
    {
        public string bossgroupDefName = string.Empty;
        public string bossDefName = string.Empty;
        public string bossKindDefName = string.Empty;
        public string bossLabel = string.Empty;
        public int calledTick;
        public bool defeatedObserved;
    }

    /// <summary>Saved per-controller observation; pages remain the narrative history.</summary>
    internal partial class MechanitorObservationState
    {
        public const int CurrentVersion = 1;
        public const int HardMaximumMechs = 512;
        public const int HardMaximumBossCalls = 128;

        public int observationVersion;
        public bool mechlinkPresent;
        public bool firstControlledPageConsumed;
        public bool firstControlledCombatPageConsumed;
        public List<MechanitorMechObservationState> observedMechs =
            new List<MechanitorMechObservationState>();
        public List<MechanitorBossCallObservationState> bossCalls =
            new List<MechanitorBossCallObservationState>();

        /// <summary>True after a current-version baseline or exact hook has initialized the row.</summary>
        public bool IsInitialized()
        {
            return observationVersion >= CurrentVersion;
        }

        /// <summary>
        /// Establishes an old-save baseline without emitting. Existing control proves that the first
        /// controlled-mech and first-combat milestones pre-date observation, so both are consumed.
        /// </summary>
        public void Baseline(MechanitorControllerSnapshot snapshot, int currentTick, int maximumMechs)
        {
            observationVersion = CurrentVersion;
            mechlinkPresent = snapshot != null && snapshot.hasMechlink;
            observedMechs.Clear();
            List<MechanitorMechSnapshot> mechs = snapshot?.overseenMechs;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                    ObserveMech(mechs[i], currentTick, maximumMechs);
            }

            bool hadExistingOverseer = observedMechs.Count > 0;
            bool hadExistingControlledMech = false;
            if (mechs != null)
            {
                for (int i = 0; i < mechs.Count; i++)
                    if (mechs[i]?.controlled == true) hadExistingControlledMech = true;
            }
            firstControlledPageConsumed = hadExistingOverseer;
            firstControlledCombatPageConsumed = hadExistingControlledMech;
            Normalize(maximumMechs, HardMaximumBossCalls);
        }

        /// <summary>Adds/refreshes one exact mech without changing first-event consumption.</summary>
        public MechanitorMechObservationState ObserveMech(
            MechanitorMechSnapshot mech,
            int currentTick,
            int maximumMechs)
        {
            string id = CleanId(mech?.mechId);
            if (id.Length == 0) return null;
            Normalize(maximumMechs, HardMaximumBossCalls);
            for (int i = 0; i < observedMechs.Count; i++)
            {
                MechanitorMechObservationState existing = observedMechs[i];
                if (!string.Equals(existing.mechId, id, StringComparison.Ordinal)) continue;
                existing.lastDisplayName = CleanText(mech.displayName);
                existing.kindDefName = CleanId(mech.kindDefName);
                if (existing.firstObservedTick <= 0)
                    existing.firstObservedTick = StartTick(mech, currentTick);
                return existing;
            }

            int cap = NormalizeCap(maximumMechs, HardMaximumMechs, 64);
            if (observedMechs.Count >= cap) return null;
            MechanitorMechObservationState created = new MechanitorMechObservationState
            {
                mechId = id,
                lastDisplayName = CleanText(mech.displayName),
                kindDefName = CleanId(mech.kindDefName),
                firstObservedTick = StartTick(mech, currentTick)
            };
            observedMechs.Add(created);
            return created;
        }

        /// <summary>Repairs malformed/unbounded save rows deterministically.</summary>
        public void Normalize(int maximumMechs, int maximumBossCalls)
        {
            observationVersion = Math.Max(0, observationVersion);
            if (observedMechs == null) observedMechs = new List<MechanitorMechObservationState>();
            if (bossCalls == null) bossCalls = new List<MechanitorBossCallObservationState>();

            HashSet<string> seenMechs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = observedMechs.Count - 1; i >= 0; i--)
            {
                MechanitorMechObservationState row = observedMechs[i];
                string id = CleanId(row?.mechId);
                if (row == null || id.Length == 0 || !seenMechs.Add(id))
                {
                    observedMechs.RemoveAt(i);
                    continue;
                }
                row.mechId = id;
                row.lastDisplayName = CleanText(row.lastDisplayName);
                row.kindDefName = CleanId(row.kindDefName);
                row.firstObservedTick = Math.Max(0, row.firstObservedTick);
            }
            TrimOldest(observedMechs, NormalizeCap(maximumMechs, HardMaximumMechs, 64));

            HashSet<string> seenBossCalls = new HashSet<string>(StringComparer.Ordinal);
            for (int i = bossCalls.Count - 1; i >= 0; i--)
            {
                MechanitorBossCallObservationState row = bossCalls[i];
                string group = CleanId(row?.bossgroupDefName);
                string boss = CleanId(row?.bossDefName);
                string key = group + "|" + boss + "|" + Math.Max(0, row?.calledTick ?? 0);
                if (row == null || group.Length == 0 || boss.Length == 0 || !seenBossCalls.Add(key))
                {
                    bossCalls.RemoveAt(i);
                    continue;
                }
                row.bossgroupDefName = group;
                row.bossDefName = boss;
                row.bossKindDefName = CleanId(row.bossKindDefName);
                row.bossLabel = CleanText(row.bossLabel);
                row.calledTick = Math.Max(0, row.calledTick);
            }
            TrimOldest(bossCalls, NormalizeCap(maximumBossCalls, HardMaximumBossCalls, 16));
        }

        private static int StartTick(MechanitorMechSnapshot mech, int currentTick)
        {
            int now = Math.Max(0, currentTick);
            int relation = Math.Max(0, mech?.relationStartTick ?? 0);
            return relation > 0 && relation <= now ? relation : now;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 ? string.Empty : Limit(cleaned, 160);
        }

        private static string CleanText(string value)
        {
            return Limit((value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim(), 160);
        }

        private static string Limit(string value, int maximum)
        {
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static int NormalizeCap(int requested, int hardMaximum, int fallback)
        {
            return requested < 1 || requested > hardMaximum ? fallback : requested;
        }

        private static void TrimOldest<T>(List<T> rows, int maximum)
        {
            if (rows.Count > maximum) rows.RemoveRange(0, rows.Count - maximum);
        }
    }

    /// <summary>Pure Phase-6 truth and salience decisions.</summary>
    internal static class MechanitorLifecyclePolicy
    {
        /// <summary>Vanilla numerical names such as “Lifter 1” are not player-named identity.</summary>
        public static bool IsPlayerNamed(MechanitorMechSnapshot mech)
        {
            return mech != null && mech.hasExplicitName && !mech.numericalName;
        }

        /// <summary>Inclusive tenure boundary; clock rollback/malformed ticks never become long service.</summary>
        public static bool IsLongServing(int firstObservedTick, int currentTick, int minimumTicks)
        {
            return firstObservedTick >= 0 && currentTick >= firstObservedTick
                && minimumTicks > 0 && currentTick - firstObservedTick >= minimumTicks;
        }

        /// <summary>A mech loss is salient only through explicit identity or observed long service.</summary>
        public static bool ShouldRecordLoss(
            MechanitorMechSnapshot mech,
            int firstObservedTick,
            int currentTick,
            int minimumTicks)
        {
            return IsPlayerNamed(mech)
                || IsLongServing(firstObservedTick, currentTick, minimumTicks);
        }

        /// <summary>Returns one only for first-pawn killer Tales, two for second-pawn killer Tales.</summary>
        public static int CombatInstigatorRole(
            string taleDefName,
            IList<string> firstPawnDefNames,
            IList<string> secondPawnDefNames)
        {
            if (Contains(firstPawnDefNames, taleDefName)) return 1;
            return Contains(secondPawnDefNames, taleDefName) ? 2 : 0;
        }

        private static bool Contains(IList<string> values, string candidate)
        {
            if (values == null || string.IsNullOrWhiteSpace(candidate)) return false;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i]?.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
