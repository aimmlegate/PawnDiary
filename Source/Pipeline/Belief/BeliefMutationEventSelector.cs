// Pure correlation of one newest detached mutation row to an exact already-authorized event route.
// This helper cannot inspect game objects, settings, caches, or create a diary page. XML supplies the
// exact source/group/role/mechanical-shape rules; malformed or ambiguous inputs fail closed.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Matches XML event rules and turns one exact nearby mutation into event evidence.</summary>
    internal static class BeliefMutationEventSelector
    {
        /// <summary>
        /// Finds one exact source/group rule. Duplicate matching rows are ambiguous and return null.
        /// </summary>
        public static BeliefMutationEventRule RuleFor(
            string sourceDomain,
            string sourceDefName,
            string downstreamGroupDefName,
            bool ideologyActive,
            bool policyEnabled,
            IReadOnlyList<BeliefMutationEventRule> rules)
        {
            if (!ideologyActive || !policyEnabled || string.IsNullOrWhiteSpace(sourceDomain)
                || string.IsNullOrWhiteSpace(sourceDefName)
                || string.IsNullOrWhiteSpace(downstreamGroupDefName) || rules == null)
                return null;

            BeliefMutationEventRule match = null;
            for (int i = 0; i < rules.Count; i++)
            {
                BeliefMutationEventRule row = rules[i];
                if (row == null
                    || !string.Equals(sourceDomain, row.sourceDomain, StringComparison.Ordinal)
                    || !string.Equals(sourceDefName, row.sourceDefName, StringComparison.Ordinal)
                    || !string.Equals(downstreamGroupDefName, row.downstreamGroupDefName,
                        StringComparison.Ordinal))
                    continue;
                if (match != null) return null;
                match = row;
            }
            return match;
        }

        /// <summary>Returns the exact participant ID named by a validated XML rule.</summary>
        public static string SubjectPawnId(
            BeliefMutationEventRule rule,
            string initiatorPawnId,
            string recipientPawnId)
        {
            if (rule == null) return string.Empty;
            if (rule.subjectRole == BeliefMutationSubjectRoleTokens.Initiator)
                return SafeId(initiatorPawnId);
            if (rule.subjectRole == BeliefMutationSubjectRoleTokens.Recipient)
                return SafeId(recipientPawnId);
            return string.Empty;
        }

        /// <summary>
        /// Validates exact pawn identity, one-way tick age, cache ordering, and the XML mechanical shape,
        /// then returns detached evidence for an already-authorized page. It never scans back to an older
        /// sequential row when the newest row mismatches, preventing one action from borrowing another.
        /// </summary>
        public static BeliefEventEvidence Select(
            BeliefMutationEventRule rule,
            string subjectPawnId,
            int eventTick,
            int correlationWindowTicks,
            BeliefMutationSnapshot newestMutation)
        {
            string subject = SafeId(subjectPawnId);
            int window = Math.Max(0, Math.Min(600, correlationWindowTicks));
            if (rule == null || subject.Length == 0 || eventTick < 0 || newestMutation == null
                || newestMutation.capturedTick < 0
                || !string.Equals(subject, newestMutation.pawnId, StringComparison.Ordinal)
                || newestMutation.startedSequence <= 0
                || newestMutation.completedSequence < newestMutation.startedSequence
                || !newestMutation.HasUsefulFact)
                return null;

            long age = (long)eventTick - newestMutation.capturedTick;
            // Cache maintenance is symmetric so clock rollback can prune stranded rows. Event
            // correlation is deliberately one-way: a future mutation cannot explain an earlier page.
            if (age < 0 || age > window || !ContainsCause(newestMutation, rule.requiredCauseToken)
                || !MatchesConversionResult(newestMutation, rule.conversionResult)
                || !MatchesCertaintyDirection(newestMutation, rule.certaintyDirection)
                || !MatchesIdeologyChange(newestMutation, rule.ideologyChange)
                || rule.requireAttemptedIdeology
                    && SafeId(newestMutation.attemptedIdeologyId).Length == 0)
                return null;

            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                subject,
                eventTick,
                rule.sourceDomain,
                rule.sourceDefName,
                rule.subjectRole,
                string.Empty,
                rule.evidenceGroupKey);
            evidence.mutation = newestMutation;
            return evidence;
        }

        private static bool ContainsCause(BeliefMutationSnapshot mutation, string wanted)
        {
            if (!BeliefMutationCauseTokens.IsKnown(wanted) || mutation?.causeTokens == null)
                return false;
            for (int i = 0; i < mutation.causeTokens.Count; i++)
                if (string.Equals(mutation.causeTokens[i], wanted, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool MatchesConversionResult(BeliefMutationSnapshot mutation, string required)
        {
            if (required == BeliefMutationConversionResultTokens.Known)
                return mutation.conversionSucceeded.HasValue;
            if (required == BeliefMutationConversionResultTokens.Success)
                return mutation.conversionSucceeded == true;
            if (required == BeliefMutationConversionResultTokens.Failure)
                return mutation.conversionSucceeded == false;
            return required == BeliefMutationConversionResultTokens.None
                && !mutation.conversionSucceeded.HasValue;
        }

        private static bool MatchesCertaintyDirection(BeliefMutationSnapshot mutation, string required)
        {
            if (required == BeliefMutationCertaintyDirectionTokens.Any) return true;
            if (!mutation.certaintyChanged || !mutation.hasBeforeCertainty
                || !mutation.hasAfterCertainty || !Finite(mutation.beforeCertainty)
                || !Finite(mutation.afterCertainty)) return false;
            if (required == BeliefMutationCertaintyDirectionTokens.Increase)
                return mutation.afterCertainty > mutation.beforeCertainty;
            return required == BeliefMutationCertaintyDirectionTokens.Decrease
                && mutation.afterCertainty < mutation.beforeCertainty;
        }

        private static bool MatchesIdeologyChange(BeliefMutationSnapshot mutation, string required)
        {
            if (required == BeliefMutationIdeologyChangeTokens.Any) return true;
            if (required == BeliefMutationIdeologyChangeTokens.Changed) return mutation.ideologyChanged;
            return required == BeliefMutationIdeologyChangeTokens.Unchanged && !mutation.ideologyChanged;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 160) return string.Empty;
            for (int i = 0; i < trimmed.Length; i++)
                if (char.IsControl(trimmed[i]) || char.IsWhiteSpace(trimmed[i])) return string.Empty;
            return trimmed;
        }
    }
}
