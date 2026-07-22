// Pure policy for the exact completed Ideology conversion ritual. XML supplies the installed ritual,
// worker, role, evidence-mode, token, timing, and output limits; this file matches and formats only
// detached strings and mutation DTOs. It never reads a Pawn, Def, Verse, Unity, or DLC object.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PawnDiary
{
    /// <summary>Stable XML evidence modes for each completed conversion-ritual perspective.</summary>
    internal static class ConversionRitualEvidenceModeTokens
    {
        public const string OrganizerRole = "organizer_role";
        public const string TargetMutation = "target_mutation";
        public const string CurrentBelief = "current_belief";
        public const string None = "none";

        public static bool IsKnown(string value)
        {
            return value == OrganizerRole || value == TargetMutation
                || value == CurrentBelief || value == None;
        }
    }

    /// <summary>Mutable XML/test boundary for one exact completed conversion-ritual family.</summary>
    internal sealed class ConversionRitualPolicyBuilder
    {
        public bool enabled;
        public string ritualDefName = string.Empty;
        public string behaviorWorkerClassName = string.Empty;
        public string outcomeWorkerClassName = string.Empty;
        public string downstreamGroupDefName = string.Empty;
        public string organizerRoleId = string.Empty;
        public string targetRoleId = string.Empty;
        public string organizerIdeologyRoleDefName = string.Empty;
        public string organizerEvidenceMode = string.Empty;
        public string targetEvidenceMode = string.Empty;
        public string participantEvidenceMode = string.Empty;
        public string spectatorEvidenceMode = string.Empty;
        public string evidenceGroupKey = string.Empty;
        public string organizerRoleToken = string.Empty;
        public string targetRoleToken = string.Empty;
        public string participantRoleToken = string.Empty;
        public string spectatorRoleToken = string.Empty;
        public string convertedResultToken = string.Empty;
        public string certaintyDecreasedResultToken = string.Empty;
        public string certaintyIncreasedResultToken = string.Empty;
        public List<string> allowedMutationCauseTokens = new List<string>();
        public int mutationCorrelationWindowTicks;
        public float certaintyDeltaEpsilon = 0.0001f;
        public int maximumAdditionalContextCharacters = 192;

        /// <summary>Missing XML is deliberately inert: it cannot guess an installed DLC identity.</summary>
        public static ConversionRitualPolicyBuilder CreateDefault()
        {
            return new ConversionRitualPolicyBuilder();
        }

        public ConversionRitualPolicySnapshot Build()
        {
            return new ConversionRitualPolicySnapshot(this);
        }
    }

    /// <summary>Immutable, deeply copied conversion-ritual policy consumed by pure/runtime adapters.</summary>
    internal sealed class ConversionRitualPolicySnapshot
    {
        public readonly bool enabled;
        public readonly string ritualDefName;
        public readonly string behaviorWorkerClassName;
        public readonly string outcomeWorkerClassName;
        public readonly string downstreamGroupDefName;
        public readonly string organizerRoleId;
        public readonly string targetRoleId;
        public readonly string organizerIdeologyRoleDefName;
        public readonly string organizerEvidenceMode;
        public readonly string targetEvidenceMode;
        public readonly string participantEvidenceMode;
        public readonly string spectatorEvidenceMode;
        public readonly string evidenceGroupKey;
        public readonly string organizerRoleToken;
        public readonly string targetRoleToken;
        public readonly string participantRoleToken;
        public readonly string spectatorRoleToken;
        public readonly string convertedResultToken;
        public readonly string certaintyDecreasedResultToken;
        public readonly string certaintyIncreasedResultToken;
        public readonly IReadOnlyList<string> allowedMutationCauseTokens;
        public readonly int mutationCorrelationWindowTicks;
        public readonly float certaintyDeltaEpsilon;
        public readonly int maximumAdditionalContextCharacters;

        internal ConversionRitualPolicySnapshot(ConversionRitualPolicyBuilder source)
        {
            ConversionRitualPolicyBuilder value = source ?? ConversionRitualPolicyBuilder.CreateDefault();
            enabled = value.enabled;
            ritualDefName = CleanIdentity(value.ritualDefName);
            behaviorWorkerClassName = CleanIdentity(value.behaviorWorkerClassName);
            outcomeWorkerClassName = CleanIdentity(value.outcomeWorkerClassName);
            downstreamGroupDefName = CleanIdentity(value.downstreamGroupDefName);
            organizerRoleId = CleanIdentity(value.organizerRoleId);
            targetRoleId = CleanIdentity(value.targetRoleId);
            organizerIdeologyRoleDefName = CleanIdentity(value.organizerIdeologyRoleDefName);
            organizerEvidenceMode = KnownMode(value.organizerEvidenceMode);
            targetEvidenceMode = KnownMode(value.targetEvidenceMode);
            participantEvidenceMode = KnownMode(value.participantEvidenceMode);
            spectatorEvidenceMode = KnownMode(value.spectatorEvidenceMode);
            evidenceGroupKey = SafeToken(value.evidenceGroupKey);
            organizerRoleToken = SafeToken(value.organizerRoleToken);
            targetRoleToken = SafeToken(value.targetRoleToken);
            participantRoleToken = SafeToken(value.participantRoleToken);
            spectatorRoleToken = SafeToken(value.spectatorRoleToken);
            convertedResultToken = SafeToken(value.convertedResultToken);
            certaintyDecreasedResultToken = SafeToken(value.certaintyDecreasedResultToken);
            certaintyIncreasedResultToken = SafeToken(value.certaintyIncreasedResultToken);
            allowedMutationCauseTokens = CopyCauses(value.allowedMutationCauseTokens);
            mutationCorrelationWindowTicks = Clamp(value.mutationCorrelationWindowTicks, 0, 10, 0);
            certaintyDeltaEpsilon = Finite(value.certaintyDeltaEpsilon)
                && value.certaintyDeltaEpsilon >= 0.000001f && value.certaintyDeltaEpsilon <= 0.1f
                    ? value.certaintyDeltaEpsilon
                    : 0.0001f;
            maximumAdditionalContextCharacters = Clamp(
                value.maximumAdditionalContextCharacters, 32, 512, 192);
        }

        public static ConversionRitualPolicySnapshot CreateDefault()
        {
            return ConversionRitualPolicyBuilder.CreateDefault().Build();
        }

        private static IReadOnlyList<string> CopyCauses(IList<string> values)
        {
            List<string> copy = new List<string>();
            if (values != null)
            {
                for (int i = 0; i < values.Count && copy.Count < 8; i++)
                {
                    string token = SafeToken(values[i]);
                    if (!BeliefMutationCauseTokens.IsKnown(token) || copy.Contains(token)) continue;
                    copy.Add(token);
                }
            }
            return new ReadOnlyCollection<string>(copy);
        }

        private static string KnownMode(string value)
        {
            string token = SafeToken(value);
            return ConversionRitualEvidenceModeTokens.IsKnown(token) ? token : string.Empty;
        }

        private static string CleanIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 160) return string.Empty;
            for (int i = 0; i < trimmed.Length; i++)
                if (char.IsControl(trimmed[i]) || char.IsWhiteSpace(trimmed[i])
                    || trimmed[i] == ';') return string.Empty;
            return trimmed;
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 64) return string.Empty;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    return string.Empty;
            }
            return trimmed;
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Pure exact matching, mutation correlation, POV evidence, and context formatting.</summary>
    internal static class ConversionRitualPolicy
    {
        public const string GroupDefName = "ritualConversion";
        public const string LegacyGroupDefName = "ritualFinished";
        public const string PerspectiveOrganizer = "organizer";
        public const string PerspectiveTarget = "target";
        public const string PerspectiveParticipant = "participant";
        public const string PerspectiveSpectator = "spectator";

        /// <summary>Matches only the reviewed ritual, fully-qualified worker, and group identity.</summary>
        public static bool Matches(
            string ritualDefName,
            string behaviorWorkerClassName,
            string outcomeWorkerClassName,
            string effectiveGroupDefName,
            bool ideologyActive,
            ConversionRitualPolicySnapshot policy)
        {
            return ideologyActive && Complete(policy)
                && string.Equals(ritualDefName, policy.ritualDefName, StringComparison.Ordinal)
                && string.Equals(behaviorWorkerClassName, policy.behaviorWorkerClassName,
                    StringComparison.Ordinal)
                && string.Equals(outcomeWorkerClassName, policy.outcomeWorkerClassName,
                    StringComparison.Ordinal)
                && string.Equals(effectiveGroupDefName, policy.downstreamGroupDefName,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns an immutable copy only when the newest target mutation is exact, post-outcome,
        /// same-family evidence. Quality is intentionally absent, so it cannot manufacture success.
        /// </summary>
        public static BeliefMutationSnapshot SelectTargetMutation(
            string targetPawnId,
            string organizerIdeologyId,
            int eventTick,
            BeliefMutationSnapshot newestMutation,
            ConversionRitualPolicySnapshot policy)
        {
            if (!Complete(policy) || eventTick < 0 || newestMutation == null
                || !ExactId(targetPawnId, newestMutation.pawnId)
                || string.IsNullOrWhiteSpace(organizerIdeologyId)
                || !newestMutation.observedMutation || newestMutation.startedSequence <= 0
                || newestMutation.completedSequence < newestMutation.startedSequence
                || !AllowedCausesOnly(newestMutation, policy)) return null;

            long age = (long)eventTick - newestMutation.capturedTick;
            if (age < 0 || age > policy.mutationCorrelationWindowTicks) return null;

            bool changedIntoOrganizer = newestMutation.ideologyChanged
                && NonEmptyDifferent(newestMutation.beforeIdeologyId, newestMutation.afterIdeologyId)
                && ExactId(newestMutation.afterIdeologyId, organizerIdeologyId)
                && ContainsCause(newestMutation, BeliefMutationCauseTokens.SetIdeology);
            bool certaintyOnly = !newestMutation.ideologyChanged && newestMutation.certaintyChanged
                && ExactId(newestMutation.beforeIdeologyId, newestMutation.afterIdeologyId)
                && newestMutation.hasBeforeCertainty && newestMutation.hasAfterCertainty
                && Finite(newestMutation.beforeCertainty) && Finite(newestMutation.afterCertainty)
                && Math.Abs(newestMutation.afterCertainty - newestMutation.beforeCertainty)
                    >= policy.certaintyDeltaEpsilon
                && ContainsCause(newestMutation, BeliefMutationCauseTokens.CertaintyOffset);
            if (!changedIntoOrganizer && !certaintyOnly) return null;

            BeliefMutationSnapshot copy = Clone(newestMutation);
            // Vanilla's ritual worker does not expose a conversion-result bool. The exact ideology
            // transition supplies success; a verified certainty-only outcome supplies non-conversion.
            copy.conversionSucceeded = changedIntoOrganizer;
            return copy;
        }

        /// <summary>Builds only the evidence allowed for this perspective by the XML policy.</summary>
        public static BeliefEventEvidence EvidenceFor(
            string pawnId,
            int eventTick,
            string ritualDefName,
            string pawnLabel,
            string perspective,
            BeliefSourcePreceptFact organizerRolePrecept,
            BeliefMutationSnapshot targetMutation,
            ConversionRitualPolicySnapshot policy)
        {
            if (!Complete(policy)) return null;
            string mode = EvidenceModeFor(perspective, policy);
            if (mode == ConversionRitualEvidenceModeTokens.None || mode.Length == 0) return null;

            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                pawnId, eventTick, "ritual", ritualDefName, "initiator", pawnLabel,
                policy.evidenceGroupKey);
            evidence.narrative.phase = "completed";
            evidence.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
            evidence.narrative.subjectId = pawnId ?? string.Empty;

            if (mode == ConversionRitualEvidenceModeTokens.OrganizerRole)
            {
                if (organizerRolePrecept == null
                    || string.IsNullOrWhiteSpace(organizerRolePrecept.instanceId)
                    || !string.Equals(organizerRolePrecept.defName,
                        policy.organizerIdeologyRoleDefName, StringComparison.Ordinal)) return null;
                evidence.sourcePreceptInstanceId = organizerRolePrecept.instanceId;
                evidence.sourcePreceptDefName = organizerRolePrecept.defName;
                evidence.currentBeliefFactsRelevant = true;
            }
            else if (mode == ConversionRitualEvidenceModeTokens.TargetMutation)
            {
                if (targetMutation == null || !ExactId(targetMutation.pawnId, pawnId)) return null;
                evidence.mutation = Clone(targetMutation);
            }
            else if (mode == ConversionRitualEvidenceModeTokens.CurrentBelief)
            {
                evidence.currentBeliefFactsRelevant = true;
            }

            return BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence) ? evidence : null;
        }

        /// <summary>Adds one bounded role marker and, for the target only, one verified result.</summary>
        public static string AppendGameContext(
            string gameContext,
            string perspective,
            BeliefMutationSnapshot targetMutation,
            ConversionRitualPolicySnapshot policy)
        {
            string current = gameContext ?? string.Empty;
            if (!Complete(policy) || current.IndexOf("conversion_ritual_role=",
                StringComparison.Ordinal) >= 0) return current;
            string role = RoleTokenFor(perspective, policy);
            if (role.Length == 0) return current;

            string suffix = "conversion_ritual_role=" + role;
            if (perspective == PerspectiveTarget && targetMutation != null)
            {
                string result = ResultToken(targetMutation, policy);
                if (result.Length > 0) suffix += "; conversion_ritual_result=" + result;
            }
            if (suffix.Length > policy.maximumAdditionalContextCharacters) return current;
            string prefix = current.Trim().TrimEnd(';');
            return prefix.Length == 0 ? suffix : prefix + "; " + suffix;
        }

        private static bool Complete(ConversionRitualPolicySnapshot policy)
        {
            return policy != null && policy.enabled
                && policy.ritualDefName.Length > 0 && policy.behaviorWorkerClassName.Length > 0
                && policy.outcomeWorkerClassName.Length > 0
                && policy.downstreamGroupDefName == GroupDefName
                && policy.organizerRoleId.Length > 0 && policy.targetRoleId.Length > 0
                && policy.organizerIdeologyRoleDefName.Length > 0
                && ConversionRitualEvidenceModeTokens.IsKnown(policy.organizerEvidenceMode)
                && ConversionRitualEvidenceModeTokens.IsKnown(policy.targetEvidenceMode)
                && ConversionRitualEvidenceModeTokens.IsKnown(policy.participantEvidenceMode)
                && ConversionRitualEvidenceModeTokens.IsKnown(policy.spectatorEvidenceMode)
                && policy.evidenceGroupKey.Length > 0
                && policy.organizerRoleToken.Length > 0 && policy.targetRoleToken.Length > 0
                && policy.participantRoleToken.Length > 0 && policy.spectatorRoleToken.Length > 0
                && policy.convertedResultToken.Length > 0
                && policy.certaintyDecreasedResultToken.Length > 0
                && policy.certaintyIncreasedResultToken.Length > 0
                && policy.allowedMutationCauseTokens.Count > 0;
        }

        private static string EvidenceModeFor(
            string perspective, ConversionRitualPolicySnapshot policy)
        {
            if (perspective == PerspectiveOrganizer) return policy.organizerEvidenceMode;
            if (perspective == PerspectiveTarget) return policy.targetEvidenceMode;
            if (perspective == PerspectiveParticipant) return policy.participantEvidenceMode;
            if (perspective == PerspectiveSpectator) return policy.spectatorEvidenceMode;
            return string.Empty;
        }

        private static string RoleTokenFor(
            string perspective, ConversionRitualPolicySnapshot policy)
        {
            if (perspective == PerspectiveOrganizer) return policy.organizerRoleToken;
            if (perspective == PerspectiveTarget) return policy.targetRoleToken;
            if (perspective == PerspectiveParticipant) return policy.participantRoleToken;
            if (perspective == PerspectiveSpectator) return policy.spectatorRoleToken;
            return string.Empty;
        }

        private static string ResultToken(
            BeliefMutationSnapshot mutation, ConversionRitualPolicySnapshot policy)
        {
            // SelectTargetMutation re-derived this nullable value from the exact transition into the
            // organizer's ideoligion; it is not trusting an adjacent conversion-attempt result.
            if (mutation.conversionSucceeded == true) return policy.convertedResultToken;
            if (!mutation.certaintyChanged || !mutation.hasBeforeCertainty
                || !mutation.hasAfterCertainty) return string.Empty;
            if (mutation.afterCertainty < mutation.beforeCertainty)
                return policy.certaintyDecreasedResultToken;
            if (mutation.afterCertainty > mutation.beforeCertainty)
                return policy.certaintyIncreasedResultToken;
            return string.Empty;
        }

        private static bool AllowedCausesOnly(
            BeliefMutationSnapshot mutation, ConversionRitualPolicySnapshot policy)
        {
            if (mutation.causeTokens == null || mutation.causeTokens.Count == 0) return false;
            for (int i = 0; i < mutation.causeTokens.Count; i++)
            {
                string cause = mutation.causeTokens[i];
                bool allowed = false;
                for (int j = 0; j < policy.allowedMutationCauseTokens.Count; j++)
                    if (string.Equals(cause, policy.allowedMutationCauseTokens[j],
                        StringComparison.Ordinal)) { allowed = true; break; }
                if (!allowed) return false;
            }
            return true;
        }

        private static bool ContainsCause(BeliefMutationSnapshot mutation, string cause)
        {
            if (mutation?.causeTokens == null) return false;
            for (int i = 0; i < mutation.causeTokens.Count; i++)
                if (string.Equals(mutation.causeTokens[i], cause, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool NonEmptyDifferent(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
                && !string.Equals(first, second, StringComparison.Ordinal);
        }

        private static bool ExactId(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
                && string.Equals(first, second, StringComparison.Ordinal);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static BeliefMutationSnapshot Clone(BeliefMutationSnapshot source)
        {
            if (source == null) return null;
            BeliefMutationSnapshot copy = new BeliefMutationSnapshot
            {
                pawnId = source.pawnId ?? string.Empty,
                capturedTick = source.capturedTick,
                beforeIdeologyId = source.beforeIdeologyId ?? string.Empty,
                beforeIdeologyName = source.beforeIdeologyName ?? string.Empty,
                afterIdeologyId = source.afterIdeologyId ?? string.Empty,
                afterIdeologyName = source.afterIdeologyName ?? string.Empty,
                attemptedIdeologyId = source.attemptedIdeologyId ?? string.Empty,
                attemptedIdeologyName = source.attemptedIdeologyName ?? string.Empty,
                hasBeforeCertainty = source.hasBeforeCertainty,
                beforeCertainty = source.beforeCertainty,
                hasAfterCertainty = source.hasAfterCertainty,
                afterCertainty = source.afterCertainty,
                ideologyChanged = source.ideologyChanged,
                certaintyChanged = source.certaintyChanged,
                conversionSucceeded = source.conversionSucceeded,
                startedSequence = source.startedSequence,
                completedSequence = source.completedSequence,
                observedMutation = source.observedMutation
            };
            if (source.causeTokens != null)
                for (int i = 0; i < source.causeTokens.Count && copy.causeTokens.Count < 8; i++)
                    copy.causeTokens.Add(source.causeTokens[i] ?? string.Empty);
            return copy;
        }
    }

    /// <summary>Pure save-compatibility policy for the new exact ritual group setting.</summary>
    internal static class ConversionRitualSettingsInheritance
    {
        public static bool Enabled(bool? exactOverride, bool? legacyOverride, bool exactDefault)
        {
            if (exactOverride.HasValue) return exactOverride.Value;
            if (legacyOverride.HasValue) return legacyOverride.Value;
            return exactDefault;
        }

        public static bool ShouldStoreOverride(
            bool desiredValue, bool exactDefault, bool? legacyOverride)
        {
            return desiredValue != exactDefault
                || legacyOverride.HasValue && desiredValue != legacyOverride.Value;
        }
    }
}
