// Pure title/psylink cause correlation and duplicate arbitration. Later adapters may stage mutations
// around rituals, succession, neuroformers, and scanner fallback; this file owns no cache or page.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Plans one canonical owner for a bounded batch of exact Royalty mutations.</summary>
    internal static class RoyalMutationOwnershipPolicy
    {
        public static RoyalMutationFact FromTitle(RoyalTitleMutationSnapshot source)
        {
            if (source == null) return null;
            return new RoyalMutationFact
            {
                kindToken = RoyalMutationKindTokens.Title,
                pawnId = Clean(source.pawnId),
                factionId = Clean(source.factionId),
                previousValue = Clean(source.previousTitle == null ? null : source.previousTitle.titleDefName),
                newValue = Clean(source.newTitle == null ? null : source.newTitle.titleDefName),
                tick = source.tick,
                correlationId = Clean(source.correlationId)
            };
        }

        public static RoyalMutationFact FromPsylink(RoyalPsychicMutationSnapshot source)
        {
            if (source == null) return null;
            return new RoyalMutationFact
            {
                kindToken = RoyalMutationKindTokens.Psylink,
                pawnId = Clean(source.pawnId),
                previousValue = source.previousPsylinkLevel.ToString(),
                newValue = source.newPsylinkLevel.ToString(),
                tick = source.tick,
                correlationId = Clean(source.correlationId)
            };
        }

        /// <summary>
        /// Correlates exact mutations with one cause scope. A richer ritual/succession owner claims the
        /// whole matching batch; an expired unclaimed batch consumes at most one fallback page.
        /// </summary>
        public static RoyalMutationBatchPlan Plan(
            IList<RoyalMutationFact> mutations,
            RoyalMutationCauseScope scope,
            int nowTick,
            bool richerOwnerAccepted,
            bool groupEnabled,
            bool fallbackAlreadyConsumed,
            RoyaltyPolicySnapshot policy)
        {
            RoyalMutationBatchPlan plan = new RoyalMutationBatchPlan
            {
                fallbackConsumed = fallbackAlreadyConsumed
            };
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (mutations == null || nowTick < 0) return plan;

            // The policy switch controls page creation, not observation/ownership bookkeeping. An
            // expired batch must still become consumed while disabled or it can remain pending forever
            // and replay after a later settings change.
            bool outputEnabled = effective.enabled && groupEnabled;

            bool ownerEmissionAssigned = false;
            bool fallbackEmissionAssigned = false;
            for (int i = 0; i < mutations.Count; i++)
            {
                RoyalMutationFact mutation = Normalize(mutations[i]);
                if (!ValidMutation(mutation)) continue;
                RoyalMutationDecision decision = new RoyalMutationDecision
                {
                    mutation = mutation,
                    advanceObservation = true
                };
                if (nowTick < mutation.tick)
                {
                    // A caller clock earlier than an observed mutation is malformed. Do not turn it
                    // into an expired fallback or advance a scanner baseline from impossible timing.
                    decision.advanceObservation = false;
                    plan.mutations.Add(decision);
                    continue;
                }
                bool exact = MatchesScope(mutation, scope, effective);
                decision.exactCauseMatch = exact;
                decision.causeToken = exact ? scope.causeToken : RoyalMutationCauseTokens.Unknown;

                if (!exact)
                {
                    decision.ownerToken = RoyalMutationOwnerTokens.Progression;
                    if (!ownerEmissionAssigned && outputEnabled)
                    {
                        plan.shouldEmitOwnerPage = true;
                        plan.ownerToken = decision.ownerToken;
                        ownerEmissionAssigned = true;
                    }
                    plan.mutations.Add(decision);
                    continue;
                }

                string exactOwner = ExactOwner(scope.causeToken);
                if (exactOwner == RoyalMutationOwnerTokens.Progression)
                {
                    decision.ownerToken = exactOwner;
                    if (!ownerEmissionAssigned && outputEnabled)
                    {
                        plan.shouldEmitOwnerPage = true;
                        plan.ownerToken = exactOwner;
                        ownerEmissionAssigned = true;
                    }
                    plan.mutations.Add(decision);
                    continue;
                }

                if (richerOwnerAccepted)
                {
                    decision.ownerToken = exactOwner;
                    decision.suppressProgressionDuplicate = true;
                    if (!ownerEmissionAssigned && outputEnabled)
                    {
                        plan.shouldEmitOwnerPage = true;
                        plan.ownerToken = exactOwner;
                        ownerEmissionAssigned = true;
                    }
                    plan.mutations.Add(decision);
                    continue;
                }

                int window = WindowFor(mutation.kindToken, effective);
                bool expired = mutation.tick < 0 || (long)nowTick - mutation.tick > window;
                if (!expired)
                {
                    decision.ownerToken = RoyalMutationOwnerTokens.Pending;
                    decision.suppressProgressionDuplicate = true;
                    if (plan.ownerToken == RoyalMutationOwnerTokens.None)
                        plan.ownerToken = RoyalMutationOwnerTokens.Pending;
                    plan.mutations.Add(decision);
                    continue;
                }

                decision.suppressProgressionDuplicate = true;
                if (!plan.fallbackConsumed)
                {
                    decision.ownerToken = RoyalMutationOwnerTokens.FallbackProgression;
                    plan.fallbackConsumed = true;
                    plan.ownerToken = decision.ownerToken;
                    if (!fallbackEmissionAssigned && outputEnabled)
                    {
                        plan.shouldEmitFallbackPage = true;
                        fallbackEmissionAssigned = true;
                    }
                }
                else
                {
                    decision.ownerToken = RoyalMutationOwnerTokens.None;
                }
                plan.mutations.Add(decision);
            }
            return plan;
        }

        private static bool MatchesScope(
            RoyalMutationFact mutation,
            RoyalMutationCauseScope scope,
            RoyaltyPolicySnapshot policy)
        {
            if (scope == null || !RoyalMutationCauseTokens.IsKnown(scope.causeToken)
                || scope.causeToken == RoyalMutationCauseTokens.Unknown
                || !Same(mutation.pawnId, scope.pawnId)
                || scope.openedTick < 0 || mutation.tick < scope.openedTick
                || (long)mutation.tick - scope.openedTick > WindowFor(mutation.kindToken, policy))
                return false;

            if (Clean(scope.correlationId).Length > 0 && Clean(mutation.correlationId).Length > 0
                && !Same(scope.correlationId, mutation.correlationId))
                return false;

            if (mutation.kindToken == RoyalMutationKindTokens.Title)
            {
                if (scope.causeToken == RoyalMutationCauseTokens.AnimaLinking
                    || scope.causeToken == RoyalMutationCauseTokens.Neuroformer
                    || !Same(mutation.factionId, scope.factionId))
                    return false;
                return SameOrBothEmpty(mutation.previousValue, scope.previousTitleDefName)
                    && SameOrBothEmpty(mutation.newValue, scope.newTitleDefName);
            }

            if (mutation.kindToken == RoyalMutationKindTokens.Psylink)
            {
                if (scope.causeToken == RoyalMutationCauseTokens.SuccessionRelated) return false;
                int before;
                int after;
                return int.TryParse(mutation.previousValue, out before)
                    && int.TryParse(mutation.newValue, out after)
                    && before == scope.previousPsylinkLevel && after == scope.newPsylinkLevel;
            }
            return false;
        }

        private static string ExactOwner(string cause)
        {
            if (cause == RoyalMutationCauseTokens.ImperialBestowing
                || cause == RoyalMutationCauseTokens.AnimaLinking)
                return RoyalMutationOwnerTokens.Ritual;
            if (cause == RoyalMutationCauseTokens.SuccessionRelated)
                return RoyalMutationOwnerTokens.Succession;
            return RoyalMutationOwnerTokens.Progression;
        }

        private static int WindowFor(string kind, RoyaltyPolicySnapshot policy)
        {
            int value = kind == RoyalMutationKindTokens.Title
                ? policy.titleCorrelationTicks
                : policy.psylinkCorrelationTicks;
            return Math.Max(1, value);
        }

        private static RoyalMutationFact Normalize(RoyalMutationFact source)
        {
            if (source == null) return null;
            return new RoyalMutationFact
            {
                kindToken = Clean(source.kindToken),
                pawnId = Clean(source.pawnId),
                factionId = Clean(source.factionId),
                previousValue = Clean(source.previousValue),
                newValue = Clean(source.newValue),
                tick = source.tick,
                correlationId = Clean(source.correlationId)
            };
        }

        private static bool ValidMutation(RoyalMutationFact mutation)
        {
            if (mutation == null || mutation.tick < 0 || !SafeId(mutation.pawnId)
                || mutation.previousValue == mutation.newValue)
                return false;
            if (mutation.kindToken == RoyalMutationKindTokens.Title)
                return SafeId(mutation.factionId)
                    && (mutation.previousValue.Length > 0 || mutation.newValue.Length > 0);
            if (mutation.kindToken == RoyalMutationKindTokens.Psylink)
            {
                int before;
                int after;
                return int.TryParse(mutation.previousValue, out before)
                    && int.TryParse(mutation.newValue, out after)
                    && before >= 0 && after >= 0;
            }
            return false;
        }

        private static bool SafeId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0;
        }

        private static bool Same(string left, string right)
        {
            string a = Clean(left);
            string b = Clean(right);
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static bool SameOrBothEmpty(string left, string right)
        {
            string a = Clean(left);
            string b = Clean(right);
            return (a.Length == 0 && b.Length == 0) || (a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal));
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
