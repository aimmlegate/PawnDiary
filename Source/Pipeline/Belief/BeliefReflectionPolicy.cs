// Assembly-free certainty and future reflection decisions. Phase 0 exposes only pure policy shells:
// no ticking, save fields, event creation, or RimWorld scheduling is wired here.
using System;

namespace PawnDiary
{
    /// <summary>Stable observation outcomes for the future passive belief-state adapter.</summary>
    internal static class BeliefObservationActionTokens
    {
        public const string Baseline = "baseline";
        public const string ResetPending = "reset_pending";
        public const string NoChange = "no_change";
        public const string IdeologyChanged = "ideology_changed";
        public const string CertaintyChanged = "certainty_changed";
    }

    /// <summary>Stable standalone-reflection trigger tokens reserved by the Ideology plan.</summary>
    internal static class BeliefReflectionTriggerTokens
    {
        public const string None = "none";
        public const string IdeologyChange = "ideology_change";
        public const string CertaintyShift = "certainty_shift";
        public const string RecentEvent = "recent_event";
        public const string PassiveDrift = "passive_drift";
        public const string Quiet = "quiet";
    }

    /// <summary>Pure input for first-scan and passive-delta classification.</summary>
    internal sealed class BeliefObservationRequest
    {
        public bool featureAvailable;
        public bool baselineOnNextScan = true;
        public bool hasCurrent;
        public string currentIdeologyId = string.Empty;
        public float currentCertainty;
        public bool hasPrevious;
        public string previousIdeologyId = string.Empty;
        public float previousCertainty;
    }

    /// <summary>Pure future-state instruction; adapters decide how and when to persist it.</summary>
    internal sealed class BeliefObservationDecision
    {
        public string action = BeliefObservationActionTokens.NoChange;
        public bool recordBaseline;
        public bool clearPending;
        public bool createReflectionDebt;
        public string trigger = BeliefReflectionTriggerTokens.None;
        public string certaintyTrend = BeliefCertaintyTrendTokens.Unknown;
        public string certaintyMagnitude = BeliefCertaintyMagnitudeTokens.Unknown;
    }

    /// <summary>Pure input for the future belief-reflection opportunity shell.</summary>
    internal sealed class BeliefReflectionRequest
    {
        public bool groupEnabled = true;
        public bool signalEnabled = true;
        public bool hasBeliefContext;
        public int nowTick;
        public int lastReflectionTick = -1;
        public int reflectionsThisQuadrum;
        public bool pendingIdeologyChange;
        public bool pendingMajorCertaintyShift;
        public bool hasRecentRelevantEvent;
        public bool recentSourceAlreadyReflected;
        public bool hasPendingCertaintyDrift;
        public bool allowQuietReflection;
        public float quietRoll = 1f;
    }

    /// <summary>Pure reflection plan shell. It never creates or consumes a diary event.</summary>
    internal sealed class BeliefReflectionDecision
    {
        public bool allowed;
        public string kind = NarrativeReflectionKindTokens.Belief;
        public string trigger = BeliefReflectionTriggerTokens.None;
        public string blockReason = string.Empty;
    }

    /// <summary>Certainty boundary logic shared by the resolver, formatter, and future scanner.</summary>
    internal static class BeliefCertaintyPolicy
    {
        /// <summary>Returns the XML-owned band whose lower bound contains the clamped value.</summary>
        public static BeliefCertaintyBand BandFor(float certainty, BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            float value = Clamp01(certainty);
            for (int i = 0; i < effective.certaintyBands.Count; i++)
                if (value >= effective.certaintyBands[i].minimum) return effective.certaintyBands[i];
            return new BeliefCertaintyBand(string.Empty, 0f, string.Empty);
        }

        /// <summary>Classifies direction and magnitude without inferring emotion or approval.</summary>
        public static void Trend(
            float before,
            float after,
            BeliefPolicySnapshot policy,
            out string trend,
            out string magnitude)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            float delta = Clamp01(after) - Clamp01(before);
            float absolute = Math.Abs(delta);
            trend = absolute < 0.000001f
                ? BeliefCertaintyTrendTokens.Stable
                : delta > 0f ? BeliefCertaintyTrendTokens.Rising : BeliefCertaintyTrendTokens.Falling;
            magnitude = absolute >= effective.certaintyMajorDelta
                ? BeliefCertaintyMagnitudeTokens.Major
                : absolute >= effective.certaintyMeaningfulDelta
                    ? BeliefCertaintyMagnitudeTokens.Meaningful
                    : BeliefCertaintyMagnitudeTokens.Minor;
        }

        /// <summary>Copies bounded certainty diagnostics into a useful resolution.</summary>
        public static void Apply(
            BeliefCertaintyFact certainty,
            BeliefMutationSnapshot mutation,
            BeliefPolicySnapshot policy,
            BeliefStanceResolution destination)
        {
            if (destination == null || certainty == null) return;
            float current = certainty.hasCurrent ? certainty.current
                : certainty.hasAfter ? certainty.after
                : mutation != null && mutation.hasAfterCertainty ? mutation.afterCertainty : 0f;
            destination.hasCertainty = certainty.hasCurrent || certainty.hasAfter
                || mutation != null && mutation.hasAfterCertainty;
            if (!destination.hasCertainty) return;
            destination.certainty = Clamp01(current);
            BeliefCertaintyBand band = BandFor(destination.certainty, policy);
            destination.certaintyBand = band.token;
            destination.certaintyPhrase = band.phrase;

            bool hasTrend = mutation != null && mutation.hasBeforeCertainty && mutation.hasAfterCertainty;
            float before = hasTrend ? mutation.beforeCertainty : certainty.before;
            float after = hasTrend ? mutation.afterCertainty : certainty.after;
            if (!hasTrend) hasTrend = certainty.hasBefore && certainty.hasAfter;
            if (hasTrend) Trend(before, after, policy, out destination.certaintyTrend, out destination.certaintyMagnitude);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    /// <summary>Pure baseline, passive-delta, and reflection-trigger policy reserved for later adapters.</summary>
    internal static class BeliefReflectionPolicy
    {
        /// <summary>
        /// First observation always baselines and emits nothing. Missing/inactive doctrine clears debt
        /// and requests another baseline when the tracker later becomes available.
        /// </summary>
        public static BeliefObservationDecision Observe(
            BeliefObservationRequest request,
            BeliefPolicySnapshot policy)
        {
            BeliefObservationDecision result = new BeliefObservationDecision();
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (request == null || !effective.enabled || !request.featureAvailable || !request.hasCurrent
                || string.IsNullOrWhiteSpace(request.currentIdeologyId))
            {
                result.action = BeliefObservationActionTokens.ResetPending;
                result.clearPending = true;
                result.recordBaseline = false;
                return result;
            }
            if (request.baselineOnNextScan || !request.hasPrevious
                || string.IsNullOrWhiteSpace(request.previousIdeologyId))
            {
                result.action = BeliefObservationActionTokens.Baseline;
                result.recordBaseline = true;
                result.clearPending = true;
                return result;
            }
            if (!string.Equals(request.currentIdeologyId.Trim(), request.previousIdeologyId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                result.action = BeliefObservationActionTokens.IdeologyChanged;
                result.createReflectionDebt = true;
                result.trigger = BeliefReflectionTriggerTokens.IdeologyChange;
                return result;
            }

            BeliefCertaintyPolicy.Trend(request.previousCertainty, request.currentCertainty, effective,
                out result.certaintyTrend, out result.certaintyMagnitude);
            if (result.certaintyMagnitude == BeliefCertaintyMagnitudeTokens.Meaningful
                || result.certaintyMagnitude == BeliefCertaintyMagnitudeTokens.Major)
            {
                result.action = BeliefObservationActionTokens.CertaintyChanged;
                result.createReflectionDebt = true;
                result.trigger = BeliefReflectionTriggerTokens.CertaintyShift;
            }
            return result;
        }

        /// <summary>Plans at most one future belief opportunity in the required trigger order.</summary>
        public static BeliefReflectionDecision Plan(
            BeliefReflectionRequest request,
            BeliefPolicySnapshot policy)
        {
            BeliefReflectionDecision result = new BeliefReflectionDecision();
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (request == null || !effective.enabled || !request.groupEnabled || !request.signalEnabled)
            {
                result.blockReason = "disabled";
                return result;
            }
            if (!request.hasBeliefContext)
            {
                result.blockReason = "no_context";
                return result;
            }
            if (effective.maximumBeliefReflectionsPerQuadrum <= 0
                || request.reflectionsThisQuadrum >= effective.maximumBeliefReflectionsPerQuadrum)
            {
                result.blockReason = "quadrum_cap";
                return result;
            }
            if (request.lastReflectionTick >= 0
                && (long)request.nowTick - request.lastReflectionTick < effective.beliefReflectionCooldownTicks)
            {
                result.blockReason = "cooldown";
                return result;
            }

            if (request.pendingIdeologyChange)
                return Allow(BeliefReflectionTriggerTokens.IdeologyChange);
            if (request.pendingMajorCertaintyShift)
                return Allow(BeliefReflectionTriggerTokens.CertaintyShift);
            if (request.hasRecentRelevantEvent && !request.recentSourceAlreadyReflected)
                return Allow(BeliefReflectionTriggerTokens.RecentEvent);
            if (request.hasPendingCertaintyDrift)
                return Allow(BeliefReflectionTriggerTokens.PassiveDrift);
            if (request.allowQuietReflection && SafeRoll(request.quietRoll) < effective.quietReflectionChance)
                return Allow(BeliefReflectionTriggerTokens.Quiet);

            result.blockReason = request.hasRecentRelevantEvent && request.recentSourceAlreadyReflected
                ? "source_reused"
                : "not_due";
            return result;
        }

        private static BeliefReflectionDecision Allow(string trigger)
        {
            return new BeliefReflectionDecision { allowed = true, trigger = trigger };
        }

        private static float SafeRoll(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
