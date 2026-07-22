// XML boundary for the exact completed Ideology conversion ritual. RimWorld loads strings and
// numeric limits only; no DLC Def reference crosses this boundary. The adapter freezes a deep plain
// snapshot before pure matching, so missing or malformed XML leaves ordinary ritual pages untouched.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>XML-authored identities, roles, evidence modes, tokens, and bounds for one ritual.</summary>
    public class DiaryConversionRitualPolicyDef : Def
    {
        public bool enabled = true;
        public string ritualDefName;
        public string behaviorWorkerClassName;
        public string outcomeWorkerClassName;
        public string downstreamGroupDefName;
        public string organizerRoleId;
        public string targetRoleId;
        public string organizerIdeologyRoleDefName;
        public string organizerEvidenceMode;
        public string targetEvidenceMode;
        public string participantEvidenceMode;
        public string spectatorEvidenceMode;
        public string evidenceGroupKey;
        public string organizerRoleToken;
        public string targetRoleToken;
        public string participantRoleToken;
        public string spectatorRoleToken;
        public string convertedResultToken;
        public string certaintyDecreasedResultToken;
        public string certaintyIncreasedResultToken;
        public List<string> allowedMutationCauseTokens;
        public int mutationCorrelationWindowTicks;
        public float certaintyDeltaEpsilon = 0.0001f;
        public int maximumAdditionalContextCharacters = 192;
    }

    /// <summary>Copies the live singleton Def into an immutable pure snapshot with inert fallbacks.</summary>
    internal static class DiaryConversionRitualPolicy
    {
        private const string DefName = "Diary_ConversionRitualPolicy";

        public static ConversionRitualPolicySnapshot Snapshot()
        {
            ConversionRitualPolicyBuilder builder = ConversionRitualPolicyBuilder.CreateDefault();
            DiaryConversionRitualPolicyDef source =
                DefDatabase<DiaryConversionRitualPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null) return builder.Build();

            builder.enabled = source.enabled;
            builder.ritualDefName = source.ritualDefName;
            builder.behaviorWorkerClassName = source.behaviorWorkerClassName;
            builder.outcomeWorkerClassName = source.outcomeWorkerClassName;
            builder.downstreamGroupDefName = source.downstreamGroupDefName;
            builder.organizerRoleId = source.organizerRoleId;
            builder.targetRoleId = source.targetRoleId;
            builder.organizerIdeologyRoleDefName = source.organizerIdeologyRoleDefName;
            builder.organizerEvidenceMode = source.organizerEvidenceMode;
            builder.targetEvidenceMode = source.targetEvidenceMode;
            builder.participantEvidenceMode = source.participantEvidenceMode;
            builder.spectatorEvidenceMode = source.spectatorEvidenceMode;
            builder.evidenceGroupKey = source.evidenceGroupKey;
            builder.organizerRoleToken = source.organizerRoleToken;
            builder.targetRoleToken = source.targetRoleToken;
            builder.participantRoleToken = source.participantRoleToken;
            builder.spectatorRoleToken = source.spectatorRoleToken;
            builder.convertedResultToken = source.convertedResultToken;
            builder.certaintyDecreasedResultToken = source.certaintyDecreasedResultToken;
            builder.certaintyIncreasedResultToken = source.certaintyIncreasedResultToken;
            builder.allowedMutationCauseTokens = source.allowedMutationCauseTokens;
            builder.mutationCorrelationWindowTicks = source.mutationCorrelationWindowTicks;
            builder.certaintyDeltaEpsilon = source.certaintyDeltaEpsilon;
            builder.maximumAdditionalContextCharacters = source.maximumAdditionalContextCharacters;
            return builder.Build();
        }
    }
}
