// Pure Biotech Phase-8 contracts and policy. Runtime adapters copy psychic-bond and deathrest
// facts into these detached rows; this file deliberately has no Verse, RimWorld, Harmony, settings,
// translation, or save dependency so ordering, causes, cooldowns, and old-save repair stay testable.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Stable synthetic Def names for the two psychic-bond phases and interrupted deathrest.</summary>
    internal static class BiotechBondDeathrestEventDefNames
    {
        public const string PsychicBondFormed = "BiotechPsychicBondFormed";
        public const string PsychicBondRuptured = "BiotechPsychicBondRuptured";
        public const string DeathrestInterrupted = "BiotechDeathrestInterrupted";
    }

    /// <summary>Stable psychic-bond lifecycle phase tokens copied into event-time context.</summary>
    internal static class PsychicBondPhaseTokens
    {
        public const string Formed = "formed";
        public const string Ruptured = "ruptured";

        public static bool IsKnown(string value)
        {
            return value == Formed || value == Ruptured;
        }
    }

    /// <summary>Exact rupture causes. Unknown is internal and must be omitted from prompt context.</summary>
    internal static class PsychicBondCauseTokens
    {
        public const string Death = "death";
        public const string GeneRemoved = "gene_removed";
        public const string Unknown = "unknown";

        public static bool IsPromptSafe(string value)
        {
            return value == Death || value == GeneRemoved;
        }
    }

    /// <summary>Stable semicolon-context keys; these are schema labels, not localized prose.</summary>
    internal static class BiotechBondDeathrestContextKeys
    {
        public const string BondPhase = "psychic_bond";
        public const string BondPartnerId = "bond_partner_id";
        public const string BondPartnerName = "bond_partner_name";
        public const string BondFirstPawnId = "bond_first_pawn_id";
        public const string BondFirstPawnName = "bond_first_pawn_name";
        public const string BondSecondPawnId = "bond_second_pawn_id";
        public const string BondSecondPawnName = "bond_second_pawn_name";
        public const string BondEpoch = "bond_epoch";
        public const string Cause = "cause";
        public const string DeathrestPhase = "deathrest";
        public const string CompletionBand = "completion_band";
    }

    /// <summary>A canonical pair of distinct stable pawn IDs, sorted with ordinal comparison.</summary>
    internal sealed class PsychicBondPair
    {
        public string firstPawnId = string.Empty;
        public string secondPawnId = string.Empty;

        public string Key
        {
            get { return firstPawnId + "|" + secondPawnId; }
        }
    }

    /// <summary>Creates one deterministic pair identity without retaining either live pawn.</summary>
    internal static class PsychicBondPairPolicy
    {
        public static PsychicBondPair Create(string firstPawnId, string secondPawnId)
        {
            string first = CleanId(firstPawnId);
            string second = CleanId(secondPawnId);
            if (first.Length == 0 || second.Length == 0
                || string.Equals(first, second, StringComparison.Ordinal))
            {
                return null;
            }

            if (string.CompareOrdinal(first, second) > 0)
            {
                string swap = first;
                first = second;
                second = swap;
            }
            return new PsychicBondPair { firstPawnId = first, secondPawnId = second };
        }

        public static string ArcKey(PsychicBondPair pair, int epoch)
        {
            return pair == null || epoch < 1
                ? string.Empty
                : "biotech-psychic-bond|" + pair.firstPawnId + "|" + pair.secondPawnId + "|" + epoch;
        }

        private static string CleanId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.IndexOf('|') >= 0 ? string.Empty : cleaned;
        }
    }

    /// <summary>One saved partner history row; a rupture keeps the epoch for truthful re-formation.</summary>
    internal partial class PsychicBondObservationRow
    {
        public string partnerPawnId = string.Empty;
        public int bondEpoch;
        public bool bonded;
        public int lastTransitionTick;
    }

    /// <summary>Saved per-pawn interrupted-deathrest lifetime and cooldown markers.</summary>
    internal partial class DeathrestObservationState
    {
        public int observationVersion;
        public int severeInterruptionsRecorded;
        public int lastRecordedTick = -1;
    }

    /// <summary>Detached facts frozen around one recursive psychic-bond lifecycle call.</summary>
    internal sealed class PsychicBondMutationSnapshot
    {
        public string ownerPawnId = string.Empty;
        public string partnerPawnId = string.Empty;
        public string firstPawnId = string.Empty;
        public string firstPawnName = string.Empty;
        public string secondPawnId = string.Empty;
        public string secondPawnName = string.Empty;
        public int bondEpoch;
        public string phase = string.Empty;
        public string cause = PsychicBondCauseTokens.Unknown;
        public bool mutuallyBondedBefore;
        public bool mutuallyBondedAfter;
        public bool firstPawnEligible;
        public bool secondPawnEligible;
        public int observedTick;

        public PsychicBondPair Pair
        {
            get { return PsychicBondPairPolicy.Create(firstPawnId, secondPawnId); }
        }
    }

    /// <summary>Detached facts frozen before and verified after one Gene_Deathrest.Wake call.</summary>
    internal sealed class DeathrestMutationSnapshot
    {
        public string pawnId = string.Empty;
        public string pawnName = string.Empty;
        public bool activeDeathrestBefore;
        public float deathrestPercentBefore;
        public bool interruptedHediffAfter;
        public bool pawnEligible;
        public int observedTick;
    }

    /// <summary>XML-owned Phase-8 thresholds copied into pure policy with safe code fallbacks.</summary>
    internal sealed class BiotechBondDeathrestPolicySnapshot
    {
        public float deathrestSevereCompletionThreshold = 0.5f;
        public int deathrestCooldownTicks = 900000;
        public int deathrestLifetimePageLimit = 1;
        public int psychicBondCorrelationExpiryTicks = 2500;
        public int maximumBondObservationRows = 16;

        public static BiotechBondDeathrestPolicySnapshot CreateDefault()
        {
            return new BiotechBondDeathrestPolicySnapshot();
        }
    }

    /// <summary>Pure lifecycle ownership, cause, epoch, and saved-row normalization decisions.</summary>
    internal static class PsychicBondLifecyclePolicy
    {
        public const int CurrentObservationVersion = 1;
        public const int HardMaximumObservationRows = 64;

        public static bool ShouldOwnFormation(PsychicBondMutationSnapshot snapshot)
        {
            return HasValidPair(snapshot)
                && snapshot.phase == PsychicBondPhaseTokens.Formed
                && !snapshot.mutuallyBondedBefore
                && snapshot.mutuallyBondedAfter
                && snapshot.bondEpoch > 0;
        }

        public static bool ShouldOwnRupture(PsychicBondMutationSnapshot snapshot)
        {
            return HasValidPair(snapshot)
                && snapshot.phase == PsychicBondPhaseTokens.Ruptured
                && snapshot.mutuallyBondedBefore
                && !snapshot.mutuallyBondedAfter
                && snapshot.bondEpoch > 0;
        }

        public static string ExactRuptureCause(bool eitherPawnDead, bool owningGeneRemovalScope)
        {
            if (eitherPawnDead) return PsychicBondCauseTokens.Death;
            if (owningGeneRemovalScope) return PsychicBondCauseTokens.GeneRemoved;
            return PsychicBondCauseTokens.Unknown;
        }

        /// <summary>Recognizes only the same sorted pair and phase as recursive secondary work.</summary>
        public static bool IsRecursiveSecondary(
            PsychicBondPair candidate,
            string phase,
            string activePairKey,
            string activePhase)
        {
            return candidate != null
                && PsychicBondPhaseTokens.IsKnown(phase)
                && string.Equals(candidate.Key, activePairKey, StringComparison.Ordinal)
                && string.Equals(phase, activePhase, StringComparison.Ordinal);
        }

        /// <summary>Allows only the exact nested generic signal Def for a lifecycle phase.</summary>
        public static bool OwnsNestedSignalDef(string phase, string sourceDefName)
        {
            if (phase == PsychicBondPhaseTokens.Formed)
                return string.Equals(sourceDefName, "PsychicBond", StringComparison.Ordinal);
            if (phase == PsychicBondPhaseTokens.Ruptured)
                return string.Equals(sourceDefName, "PsychicBondTorn", StringComparison.Ordinal);
            return false;
        }

        public static int NextEpoch(
            string firstPawnId,
            string secondPawnId,
            IList<PsychicBondObservationRow> firstRows,
            IList<PsychicBondObservationRow> secondRows)
        {
            PsychicBondPair pair = PsychicBondPairPolicy.Create(firstPawnId, secondPawnId);
            if (pair == null) return 1;
            int maximum = Math.Max(
                EpochForPartner(firstRows, pair.secondPawnId),
                EpochForPartner(secondRows, pair.firstPawnId));
            return maximum >= int.MaxValue ? int.MaxValue : Math.Max(1, maximum + 1);
        }

        public static void NormalizeRows(
            List<PsychicBondObservationRow> rows,
            string ownerPawnId,
            int nowTick,
            int maximumRows)
        {
            if (rows == null) return;
            string owner = (ownerPawnId ?? string.Empty).Trim();
            int now = Math.Max(0, nowTick);
            int cap = maximumRows < 1 || maximumRows > HardMaximumObservationRows
                ? 16
                : maximumRows;
            Dictionary<string, PsychicBondObservationRow> newest =
                new Dictionary<string, PsychicBondObservationRow>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                PsychicBondObservationRow row = rows[i];
                if (row == null) continue;
                string partner = (row.partnerPawnId ?? string.Empty).Trim();
                if (partner.Length == 0 || partner.IndexOf('|') >= 0
                    || string.Equals(partner, owner, StringComparison.Ordinal))
                {
                    continue;
                }
                row.partnerPawnId = partner;
                row.bondEpoch = Math.Max(1, row.bondEpoch);
                row.lastTransitionTick = Math.Max(0, Math.Min(now, row.lastTransitionTick));
                PsychicBondObservationRow previous;
                if (!newest.TryGetValue(partner, out previous)
                    || row.lastTransitionTick > previous.lastTransitionTick
                    || (row.lastTransitionTick == previous.lastTransitionTick
                        && row.bondEpoch > previous.bondEpoch))
                {
                    newest[partner] = row;
                }
            }

            rows.Clear();
            rows.AddRange(newest.Values);
            rows.Sort(CompareNewestFirst);
            if (rows.Count > cap) rows.RemoveRange(cap, rows.Count - cap);
            rows.Sort((left, right) => string.CompareOrdinal(
                left?.partnerPawnId ?? string.Empty,
                right?.partnerPawnId ?? string.Empty));
        }

        private static bool HasValidPair(PsychicBondMutationSnapshot snapshot)
        {
            return snapshot != null && snapshot.Pair != null;
        }

        private static int EpochForPartner(IList<PsychicBondObservationRow> rows, string partnerPawnId)
        {
            int maximum = 0;
            if (rows == null || partnerPawnId.Length == 0) return maximum;
            for (int i = 0; i < rows.Count; i++)
            {
                PsychicBondObservationRow row = rows[i];
                if (row != null
                    && string.Equals(row.partnerPawnId, partnerPawnId, StringComparison.Ordinal))
                {
                    maximum = Math.Max(maximum, row.bondEpoch);
                }
            }
            return maximum;
        }

        private static int CompareNewestFirst(
            PsychicBondObservationRow left,
            PsychicBondObservationRow right)
        {
            int tick = (right?.lastTransitionTick ?? 0).CompareTo(left?.lastTransitionTick ?? 0);
            if (tick != 0) return tick;
            int epoch = (right?.bondEpoch ?? 0).CompareTo(left?.bondEpoch ?? 0);
            if (epoch != 0) return epoch;
            return string.CompareOrdinal(
                left?.partnerPawnId ?? string.Empty,
                right?.partnerPawnId ?? string.Empty);
        }
    }

    internal enum DeathrestInterruptionDecision
    {
        Drop,
        OwnSilently,
        OwnAndRecord
    }

    /// <summary>Pure severe-band, cooldown, lifetime, and routine-silence policy.</summary>
    internal static class DeathrestInterruptionPolicy
    {
        public const int CurrentObservationVersion = 1;

        public static DeathrestInterruptionDecision Decide(
            DeathrestMutationSnapshot snapshot,
            DeathrestObservationState state,
            BiotechBondDeathrestPolicySnapshot policy)
        {
            if (snapshot == null || !snapshot.activeDeathrestBefore
                || !snapshot.interruptedHediffAfter
                || string.IsNullOrWhiteSpace(snapshot.pawnId)
                || float.IsNaN(snapshot.deathrestPercentBefore)
                || float.IsInfinity(snapshot.deathrestPercentBefore)
                || snapshot.deathrestPercentBefore < 0f
                || snapshot.deathrestPercentBefore >= 1f)
            {
                return DeathrestInterruptionDecision.Drop;
            }

            BiotechBondDeathrestPolicySnapshot effective =
                policy ?? BiotechBondDeathrestPolicySnapshot.CreateDefault();
            float threshold = effective.deathrestSevereCompletionThreshold;
            if (threshold <= 0f || threshold >= 1f) threshold = 0.5f;
            if (snapshot.deathrestPercentBefore > threshold
                || !snapshot.pawnEligible
                || state == null)
            {
                return DeathrestInterruptionDecision.OwnSilently;
            }

            int lifetimeLimit = effective.deathrestLifetimePageLimit < 1
                ? 1
                : effective.deathrestLifetimePageLimit;
            if (state.severeInterruptionsRecorded >= lifetimeLimit)
                return DeathrestInterruptionDecision.OwnSilently;

            int cooldown = Math.Max(0, effective.deathrestCooldownTicks);
            int now = Math.Max(0, snapshot.observedTick);
            if (state.lastRecordedTick >= 0
                && now - state.lastRecordedTick < cooldown)
            {
                return DeathrestInterruptionDecision.OwnSilently;
            }
            return DeathrestInterruptionDecision.OwnAndRecord;
        }

        public static void Record(
            DeathrestObservationState state,
            int observedTick)
        {
            if (state == null) return;
            state.observationVersion = CurrentObservationVersion;
            if (state.severeInterruptionsRecorded < int.MaxValue)
                state.severeInterruptionsRecorded++;
            state.lastRecordedTick = Math.Max(0, observedTick);
        }

        public static void Normalize(DeathrestObservationState state, int nowTick)
        {
            if (state == null) return;
            state.observationVersion = Math.Max(0, state.observationVersion);
            state.severeInterruptionsRecorded = Math.Max(0, state.severeInterruptionsRecorded);
            int now = Math.Max(0, nowTick);
            state.lastRecordedTick = state.lastRecordedTick < 0
                ? -1
                : Math.Min(now, state.lastRecordedTick);
        }
    }
}
