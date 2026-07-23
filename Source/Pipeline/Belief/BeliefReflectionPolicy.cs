// Assembly-free certainty and reflection decisions. Phase 3 adds the pure saved-state reducer used
// by the elapsed main-thread scanner; event creation remains deliberately reserved for Phase 4.
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

    /// <summary>Detached minimal tracker reading supplied by the DLC-safe game adapter.</summary>
    internal sealed class BeliefTrackerObservation
    {
        public bool hasCurrent;
        public string ideologyId = string.Empty;
        public string ideologyName = string.Empty;
        public float certainty;
    }

    /// <summary>
    /// Plain persisted observation fields. The RimWorld-facing PawnBeliefState maps to this DTO so
    /// accumulation and threshold behavior stay testable without Verse or a running game.
    /// </summary>
    internal sealed class BeliefScanState
    {
        public bool baselineOnNextScan = true;
        public bool hasLastObservation;
        public string lastIdeologyId = string.Empty;
        public string lastIdeologyName = string.Empty;
        public float lastCertainty;
        public int lastScanTick = -1;
        public bool hasPendingCertainty;
        public float pendingCertaintyBefore;
        public float pendingCertaintyAfter;
        public int pendingCertaintyFirstTick = -1;
        public int pendingCertaintyLastTick = -1;
        public bool pendingIdeologyChange;
        public string pendingPreviousIdeologyId = string.Empty;
        public string pendingPreviousIdeologyName = string.Empty;
        public string pendingCurrentIdeologyId = string.Empty;
        public string pendingCurrentIdeologyName = string.Empty;
    }

    /// <summary>One pure transition containing the next saved fields and diagnostic decision.</summary>
    internal sealed class BeliefObservationTransition
    {
        public BeliefScanState state = new BeliefScanState();
        public BeliefObservationDecision decision = new BeliefObservationDecision();
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
        /// Advances detached state by one observation. Small same-ideology movements accumulate from
        /// the first pending certainty, so several sub-threshold scans can become meaningful without
        /// creating per-scan debt. Missing/disabled doctrine clears debt and requires a fresh baseline.
        /// </summary>
        public static BeliefObservationTransition Advance(
            BeliefScanState saved,
            BeliefTrackerObservation observation,
            bool featureAvailable,
            int nowTick,
            BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            int now = Math.Max(0, nowTick);
            BeliefScanState next = Normalize(saved, now, effective);
            BeliefObservationDecision decision = new BeliefObservationDecision();
            BeliefObservationTransition transition = new BeliefObservationTransition
            {
                state = next,
                decision = decision
            };

            if (!effective.enabled || !featureAvailable || observation == null
                || !observation.hasCurrent || string.IsNullOrWhiteSpace(observation.ideologyId))
            {
                next.baselineOnNextScan = true;
                next.hasLastObservation = false;
                next.lastScanTick = now;
                ClearPending(next);
                decision.action = BeliefObservationActionTokens.ResetPending;
                decision.clearPending = true;
                return transition;
            }

            string currentId = Bounded(observation.ideologyId, effective.maximumIdentifierCharacters);
            if (currentId.Length == 0)
            {
                next.baselineOnNextScan = true;
                next.hasLastObservation = false;
                next.lastScanTick = now;
                ClearPending(next);
                decision.action = BeliefObservationActionTokens.ResetPending;
                decision.clearPending = true;
                return transition;
            }
            string currentName = Bounded(observation.ideologyName, effective.maximumFieldCharacters);
            float currentCertainty = Clamp01(observation.certainty);

            if (next.baselineOnNextScan || !next.hasLastObservation || next.lastIdeologyId.Length == 0)
            {
                RecordLast(next, currentId, currentName, currentCertainty, now);
                next.baselineOnNextScan = false;
                ClearPending(next);
                decision.action = BeliefObservationActionTokens.Baseline;
                decision.recordBaseline = true;
                decision.clearPending = true;
                return transition;
            }

            if (!string.Equals(next.lastIdeologyId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                if (!next.pendingIdeologyChange)
                {
                    next.pendingPreviousIdeologyId = next.lastIdeologyId;
                    next.pendingPreviousIdeologyName = next.lastIdeologyName;
                }
                next.pendingIdeologyChange = true;
                next.pendingCurrentIdeologyId = currentId;
                next.pendingCurrentIdeologyName = currentName;
                // A pawn can change A -> B -> A before Phase 4 consumes the pending reflection.
                // That is a real latest transition, but the accumulated story debt is net-zero:
                // retaining it would later fabricate an "A to A" ideology-change reflection.
                if (string.Equals(
                        next.pendingPreviousIdeologyId,
                        next.pendingCurrentIdeologyId,
                        StringComparison.OrdinalIgnoreCase))
                    ClearPendingIdeology(next);
                ClearPendingCertainty(next);
                RecordLast(next, currentId, currentName, currentCertainty, now);
                decision.action = BeliefObservationActionTokens.IdeologyChanged;
                decision.createReflectionDebt = next.pendingIdeologyChange;
                decision.trigger = next.pendingIdeologyChange
                    ? BeliefReflectionTriggerTokens.IdeologyChange
                    : BeliefReflectionTriggerTokens.None;
                return transition;
            }

            if (next.hasPendingCertainty && next.pendingCertaintyLastTick >= 0
                && (long)now - next.pendingCertaintyLastTick > effective.pendingBeliefEvidenceMaxAgeTicks)
                ClearPendingCertainty(next);

            float before = next.hasPendingCertainty
                ? next.pendingCertaintyBefore
                : next.lastCertainty;
            float delta = currentCertainty - before;
            if (Math.Abs(delta) < 0.000001f)
            {
                ClearPendingCertainty(next);
                RecordLast(next, currentId, currentName, currentCertainty, now);
                decision.certaintyTrend = BeliefCertaintyTrendTokens.Stable;
                decision.certaintyMagnitude = BeliefCertaintyMagnitudeTokens.Minor;
                return transition;
            }

            if (!next.hasPendingCertainty)
            {
                next.hasPendingCertainty = true;
                next.pendingCertaintyBefore = next.lastCertainty;
                next.pendingCertaintyFirstTick = now;
            }
            next.pendingCertaintyAfter = currentCertainty;
            next.pendingCertaintyLastTick = now;
            BeliefCertaintyPolicy.Trend(next.pendingCertaintyBefore, currentCertainty, effective,
                out decision.certaintyTrend, out decision.certaintyMagnitude);
            if (decision.certaintyMagnitude == BeliefCertaintyMagnitudeTokens.Meaningful
                || decision.certaintyMagnitude == BeliefCertaintyMagnitudeTokens.Major)
            {
                decision.action = BeliefObservationActionTokens.CertaintyChanged;
                decision.createReflectionDebt = true;
                decision.trigger = BeliefReflectionTriggerTokens.CertaintyShift;
            }
            RecordLast(next, currentId, currentName, currentCertainty, now);
            return transition;
        }

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

        private static BeliefScanState Normalize(
            BeliefScanState source, int now, BeliefPolicySnapshot policy)
        {
            BeliefScanState value = source ?? new BeliefScanState();
            BeliefScanState result = new BeliefScanState
            {
                baselineOnNextScan = value.baselineOnNextScan,
                hasLastObservation = value.hasLastObservation,
                lastIdeologyId = Bounded(value.lastIdeologyId, policy.maximumIdentifierCharacters),
                lastIdeologyName = Bounded(value.lastIdeologyName, policy.maximumFieldCharacters),
                lastCertainty = Clamp01(value.lastCertainty),
                lastScanTick = value.lastScanTick,
                hasPendingCertainty = value.hasPendingCertainty,
                pendingCertaintyBefore = Clamp01(value.pendingCertaintyBefore),
                pendingCertaintyAfter = Clamp01(value.pendingCertaintyAfter),
                pendingCertaintyFirstTick = value.pendingCertaintyFirstTick,
                pendingCertaintyLastTick = value.pendingCertaintyLastTick,
                pendingIdeologyChange = value.pendingIdeologyChange,
                pendingPreviousIdeologyId = Bounded(
                    value.pendingPreviousIdeologyId, policy.maximumIdentifierCharacters),
                pendingPreviousIdeologyName = Bounded(
                    value.pendingPreviousIdeologyName, policy.maximumFieldCharacters),
                pendingCurrentIdeologyId = Bounded(
                    value.pendingCurrentIdeologyId, policy.maximumIdentifierCharacters),
                pendingCurrentIdeologyName = Bounded(
                    value.pendingCurrentIdeologyName, policy.maximumFieldCharacters)
            };
            if (result.lastScanTick > now)
            {
                result.lastScanTick = -1;
                result.hasLastObservation = false;
                result.baselineOnNextScan = true;
                ClearPending(result);
            }
            if (!result.hasLastObservation || result.lastIdeologyId.Length == 0)
            {
                result.hasLastObservation = false;
                result.baselineOnNextScan = true;
            }
            if (!result.hasPendingCertainty || result.pendingCertaintyFirstTick < 0
                || result.pendingCertaintyLastTick < result.pendingCertaintyFirstTick
                || result.pendingCertaintyFirstTick > now || result.pendingCertaintyLastTick > now
                || (long)now - result.pendingCertaintyLastTick
                    > policy.pendingBeliefEvidenceMaxAgeTicks)
                ClearPendingCertainty(result);
            if (!result.pendingIdeologyChange || result.pendingPreviousIdeologyId.Length == 0
                || result.pendingCurrentIdeologyId.Length == 0
                || string.Equals(
                    result.pendingPreviousIdeologyId,
                    result.pendingCurrentIdeologyId,
                    StringComparison.OrdinalIgnoreCase))
                ClearPendingIdeology(result);
            return result;
        }

        private static void RecordLast(
            BeliefScanState state, string id, string name, float certainty, int tick)
        {
            state.hasLastObservation = true;
            state.lastIdeologyId = id;
            state.lastIdeologyName = name;
            state.lastCertainty = certainty;
            state.lastScanTick = tick;
        }

        private static void ClearPending(BeliefScanState state)
        {
            ClearPendingCertainty(state);
            ClearPendingIdeology(state);
        }

        private static void ClearPendingCertainty(BeliefScanState state)
        {
            state.hasPendingCertainty = false;
            state.pendingCertaintyBefore = 0f;
            state.pendingCertaintyAfter = 0f;
            state.pendingCertaintyFirstTick = -1;
            state.pendingCertaintyLastTick = -1;
        }

        private static void ClearPendingIdeology(BeliefScanState state)
        {
            state.pendingIdeologyChange = false;
            state.pendingPreviousIdeologyId = string.Empty;
            state.pendingPreviousIdeologyName = string.Empty;
            state.pendingCurrentIdeologyId = string.Empty;
            state.pendingCurrentIdeologyName = string.Empty;
        }

        private static string Bounded(string value, int maximum)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length <= maximum ? cleaned : cleaned.Substring(0, maximum);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
