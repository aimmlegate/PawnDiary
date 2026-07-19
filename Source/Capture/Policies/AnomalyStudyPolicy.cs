// Pure Anomaly study-milestone policy. It distinguishes observation/history from page generation,
// so disabled output or an ineligible researcher cannot manufacture a later false "first" event.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Plans one study transition without touching RimWorld state, settings, or saves.</summary>
    internal static class AnomalyStudyPolicy
    {
        /// <summary>Returns the page decision and additive history mutation for committed facts.</summary>
        public static AnomalyStudyPlan Plan(
            AnomalyStudyFacts facts,
            AnomalyStudyHistorySnapshot history,
            AnomalyPolicySnapshot policy)
        {
            AnomalyStudyPlan plan = new AnomalyStudyPlan
            {
                disposition = AnomalyStudyDisposition.DropInvalid
            };
            if (facts == null || string.IsNullOrWhiteSpace(facts.studiedDefName)
                || facts.oldProgress < 0 || facts.newProgress < 0
                || facts.noteThresholdsCrossed < 0)
            {
                return plan;
            }

            plan.disposition = AnomalyStudyDisposition.StateOnly;
            plan.monolithBecameActivatable = facts.isMonolith
                && !facts.monolithActivatableBefore
                && facts.monolithActivatableAfter;
            if (facts.newProgress <= facts.oldProgress)
            {
                return plan;
            }

            history = history ?? new AnomalyStudyHistorySnapshot();
            policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            string studiedDefName = facts.studiedDefName.Trim();
            bool crossedNote = facts.noteThresholdsCrossed > 0;
            bool first = crossedNote && !history.firstBreakthroughObserved;
            if (first)
            {
                plan.historyMutation.observeFirstBreakthrough = true;
            }

            bool completed = !facts.completedBefore && facts.completedAfter
                && !Contains(history.completedStudyDefNames, studiedDefName);
            if (completed)
            {
                plan.historyMutation.completedStudyDefName = studiedDefName;
            }

            List<AnomalyStudyMilestoneRule> promotions = FindPromotions(
                policy.promotedStudyMilestones,
                studiedDefName,
                facts.oldProgress,
                facts.newProgress,
                history.observedPromotionKeys);
            AnomalyStudyMilestoneRule promotion = promotions.Count == 0 ? null : promotions[0];
            if (promotion != null)
            {
                // Observe every threshold crossed by this one committed jump. Only the earliest is
                // the page's semantic promotion; marking all prevents a retry from replaying a
                // second page for the same before/after transition.
                for (int i = 0; i < promotions.Count; i++)
                {
                    plan.historyMutation.observedPromotionKeys.Add(PromotionKey(promotions[i]));
                }
            }

            // A plain progress increase which crossed no observed/promoted threshold is useful only
            // to the later adapter's baseline state; it is never itself a narrative milestone.
            if (!first && !completed && promotion == null)
            {
                return plan;
            }

            // Monolith study deliberately enriches the next exact activation instead of competing
            // with it for a second page. History still advances above, preventing retroactive firsts.
            if (!policy.studyEnabled || facts.isMonolith || !facts.studierEligible
                || string.IsNullOrWhiteSpace(facts.studierPawnId))
            {
                return plan;
            }

            if (first && policy.recordFirstStudyBreakthrough)
            {
                plan.stageToken = AnomalyStudyStageTokens.FirstBreakthrough;
            }
            else if (completed && policy.recordCompletedEntityKind)
            {
                plan.stageToken = AnomalyStudyStageTokens.CompletedKind;
            }
            else if (promotion != null)
            {
                plan.stageToken = AnomalyStudyStageTokens.Promoted;
                plan.promotionToken = promotion.token.Trim();
            }

            if (plan.stageToken.Length > 0)
            {
                plan.disposition = AnomalyStudyDisposition.Generate;
            }

            return plan;
        }

        /// <summary>Builds the stable key persisted when an exact XML promotion is observed.</summary>
        public static string PromotionKey(AnomalyStudyMilestoneRule rule)
        {
            return AnomalyPolicyNormalization.PromotionKey(rule);
        }

        private static List<AnomalyStudyMilestoneRule> FindPromotions(
            List<AnomalyStudyMilestoneRule> rules,
            string studiedDefName,
            int oldProgress,
            int newProgress,
            List<string> observedKeys)
        {
            List<AnomalyStudyMilestoneRule> matches = new List<AnomalyStudyMilestoneRule>();
            if (rules == null) return matches;
            HashSet<string> foundKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rules.Count; i++)
            {
                AnomalyStudyMilestoneRule candidate = rules[i];
                if (!ValidPromotion(candidate)
                    || !string.Equals(candidate.studiedDefName.Trim(), studiedDefName, StringComparison.Ordinal)
                    || oldProgress >= candidate.minimumProgress
                    || newProgress < candidate.minimumProgress)
                {
                    continue;
                }

                string key = PromotionKey(candidate);
                if (Contains(observedKeys, key) || !foundKeys.Add(key))
                {
                    continue;
                }

                matches.Add(candidate);
            }

            matches.Sort((left, right) =>
            {
                int comparison = left.minimumProgress.CompareTo(right.minimumProgress);
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(left.token.Trim(), right.token.Trim());
            });
            return matches;
        }

        private static bool ValidPromotion(AnomalyStudyMilestoneRule rule)
        {
            return AnomalyPolicyNormalization.ValidPromotion(rule);
        }

        private static bool Contains(List<string> values, string value)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals((values[i] ?? string.Empty).Trim(), value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
