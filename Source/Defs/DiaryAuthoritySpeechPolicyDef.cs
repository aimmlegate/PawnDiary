// XML boundary for exact completed throne and Ideology leader speeches. All installed identities are
// plain strings, so a no-DLC game never resolves a gated Def and malformed policy simply enriches none.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One XML-authored exact route; this is data, not a reference to a DLC Def.</summary>
    public class DiaryAuthoritySpeechRouteDef
    {
        public string ritualDefName;
        public string behaviorWorkerClassName;
        public string outcomeWorkerClassName;
        public string downstreamGroupDefName;
        public string speakerRoleId;
        public bool requiresRoyalty;
    }

    /// <summary>XML-owned routes, POV evidence modes, output bounds, and localized prompt policy.</summary>
    public class DiaryAuthoritySpeechPolicyDef : Def
    {
        public bool enabled = true;
        public List<DiaryAuthoritySpeechRouteDef> routes;
        public string evidenceGroupKey;
        public string topicToken;
        public string speakerEvidenceMode;
        public string targetEvidenceMode;
        public string participantEvidenceMode;
        public string spectatorEvidenceMode;
        public int speakerMaximumSelectedStances = 1;
        public int speakerMaximumSupportingMemes = 1;
        public int speakerMaximumContextCharacters = 640;
        public bool speakerIncludeRole = true;
        public bool speakerIncludeCertainty = true;
        public bool speakerIncludeStructure = true;
        public bool speakerIncludeDeity;
        public bool speakerIncludeNarrativeInterpretation = true;
        public int witnessMaximumSelectedStances = 1;
        public int witnessMaximumSupportingMemes;
        public int witnessMaximumContextCharacters = 320;
        public bool witnessIncludeRole;
        public bool witnessIncludeCertainty;
        public bool witnessIncludeStructure;
        public bool witnessIncludeDeity;
        public bool witnessIncludeNarrativeInterpretation;
        public string speakerPromptInstruction;
        public string witnessPromptInstruction;
    }

    /// <summary>Copies the singleton Def into a deep plain snapshot once per ritual fanout.</summary>
    internal static class DiaryAuthoritySpeechPolicy
    {
        private const string DefName = "Diary_AuthoritySpeechPolicy";

        public static AuthoritySpeechPolicySnapshot Snapshot()
        {
            AuthoritySpeechPolicyBuilder builder = AuthoritySpeechPolicyBuilder.CreateDefault();
            DiaryAuthoritySpeechPolicyDef source =
                DefDatabase<DiaryAuthoritySpeechPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null) return builder.Build();

            builder.enabled = source.enabled;
            if (source.routes != null)
                for (int i = 0; i < source.routes.Count; i++)
                {
                    DiaryAuthoritySpeechRouteDef route = source.routes[i];
                    if (route == null) continue;
                    builder.routes.Add(new AuthoritySpeechRouteBuilder
                    {
                        ritualDefName = route.ritualDefName,
                        behaviorWorkerClassName = route.behaviorWorkerClassName,
                        outcomeWorkerClassName = route.outcomeWorkerClassName,
                        downstreamGroupDefName = route.downstreamGroupDefName,
                        speakerRoleId = route.speakerRoleId,
                        requiresRoyalty = route.requiresRoyalty
                    });
                }
            builder.evidenceGroupKey = source.evidenceGroupKey;
            builder.topicToken = source.topicToken;
            builder.speakerEvidenceMode = source.speakerEvidenceMode;
            builder.targetEvidenceMode = source.targetEvidenceMode;
            builder.participantEvidenceMode = source.participantEvidenceMode;
            builder.spectatorEvidenceMode = source.spectatorEvidenceMode;
            builder.speakerMaximumSelectedStances = source.speakerMaximumSelectedStances;
            builder.speakerMaximumSupportingMemes = source.speakerMaximumSupportingMemes;
            builder.speakerMaximumContextCharacters = source.speakerMaximumContextCharacters;
            builder.speakerIncludeRole = source.speakerIncludeRole;
            builder.speakerIncludeCertainty = source.speakerIncludeCertainty;
            builder.speakerIncludeStructure = source.speakerIncludeStructure;
            builder.speakerIncludeDeity = source.speakerIncludeDeity;
            builder.speakerIncludeNarrativeInterpretation = source.speakerIncludeNarrativeInterpretation;
            builder.witnessMaximumSelectedStances = source.witnessMaximumSelectedStances;
            builder.witnessMaximumSupportingMemes = source.witnessMaximumSupportingMemes;
            builder.witnessMaximumContextCharacters = source.witnessMaximumContextCharacters;
            builder.witnessIncludeRole = source.witnessIncludeRole;
            builder.witnessIncludeCertainty = source.witnessIncludeCertainty;
            builder.witnessIncludeStructure = source.witnessIncludeStructure;
            builder.witnessIncludeDeity = source.witnessIncludeDeity;
            builder.witnessIncludeNarrativeInterpretation = source.witnessIncludeNarrativeInterpretation;
            builder.speakerPromptInstruction = source.speakerPromptInstruction;
            builder.witnessPromptInstruction = source.witnessPromptInstruction;
            return builder.Build();
        }
    }
}
