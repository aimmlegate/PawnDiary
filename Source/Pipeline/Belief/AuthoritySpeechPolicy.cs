// Pure exact-routing and POV policy for completed throne and Ideology leader speeches. XML owns the
// installed ritual/worker/role identities, topic, perspective modes, prompt guidance, and output caps.
// This file sees only detached strings and DTOs: it never reads a Pawn, Def, Verse, Unity, or DLC object.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PawnDiary
{
    /// <summary>Stable XML modes controlling which completed-speech POV receives belief evidence.</summary>
    internal static class AuthoritySpeechEvidenceModeTokens
    {
        public const string SpeakerAuthority = "speaker_authority";
        public const string SharedAuthority = "shared_authority";
        public const string None = "none";

        public static bool IsKnown(string value)
        {
            return value == SpeakerAuthority || value == SharedAuthority || value == None;
        }
    }

    /// <summary>Mutable XML/test row for one exact installed speech route.</summary>
    internal sealed class AuthoritySpeechRouteBuilder
    {
        public string ritualDefName = string.Empty;
        public string behaviorWorkerClassName = string.Empty;
        public string outcomeWorkerClassName = string.Empty;
        public string downstreamGroupDefName = string.Empty;
        public string speakerRoleId = string.Empty;
        public bool requiresRoyalty;
    }

    /// <summary>Immutable exact identity for one installed speech mechanic.</summary>
    internal sealed class AuthoritySpeechRouteSnapshot
    {
        public readonly string ritualDefName;
        public readonly string behaviorWorkerClassName;
        public readonly string outcomeWorkerClassName;
        public readonly string downstreamGroupDefName;
        public readonly string speakerRoleId;
        public readonly bool requiresRoyalty;

        internal AuthoritySpeechRouteSnapshot(AuthoritySpeechRouteBuilder source)
        {
            AuthoritySpeechRouteBuilder value = source ?? new AuthoritySpeechRouteBuilder();
            ritualDefName = AuthoritySpeechPolicy.CleanIdentity(value.ritualDefName);
            behaviorWorkerClassName = AuthoritySpeechPolicy.CleanIdentity(value.behaviorWorkerClassName);
            outcomeWorkerClassName = AuthoritySpeechPolicy.CleanIdentity(value.outcomeWorkerClassName);
            downstreamGroupDefName = AuthoritySpeechPolicy.CleanIdentity(value.downstreamGroupDefName);
            speakerRoleId = AuthoritySpeechPolicy.CleanIdentity(value.speakerRoleId);
            requiresRoyalty = value.requiresRoyalty;
        }

        public bool Complete
        {
            get
            {
                return ritualDefName.Length > 0 && behaviorWorkerClassName.Length > 0
                    && outcomeWorkerClassName.Length > 0 && downstreamGroupDefName.Length > 0
                    && speakerRoleId.Length > 0;
            }
        }
    }

    /// <summary>Mutable XML/test boundary for exact authority-speech enrichment.</summary>
    internal sealed class AuthoritySpeechPolicyBuilder
    {
        public bool enabled;
        public List<AuthoritySpeechRouteBuilder> routes = new List<AuthoritySpeechRouteBuilder>();
        public string evidenceGroupKey = string.Empty;
        public string topicToken = string.Empty;
        public string speakerEvidenceMode = string.Empty;
        public string targetEvidenceMode = string.Empty;
        public string participantEvidenceMode = string.Empty;
        public string spectatorEvidenceMode = string.Empty;
        public int speakerMaximumSelectedStances;
        public int speakerMaximumSupportingMemes;
        public int speakerMaximumContextCharacters;
        public bool speakerIncludeRole;
        public bool speakerIncludeCertainty;
        public bool speakerIncludeStructure;
        public bool speakerIncludeDeity;
        public bool speakerIncludeNarrativeInterpretation;
        public int witnessMaximumSelectedStances;
        public int witnessMaximumSupportingMemes;
        public int witnessMaximumContextCharacters;
        public bool witnessIncludeRole;
        public bool witnessIncludeCertainty;
        public bool witnessIncludeStructure;
        public bool witnessIncludeDeity;
        public bool witnessIncludeNarrativeInterpretation;
        public string speakerPromptInstruction = string.Empty;
        public string witnessPromptInstruction = string.Empty;

        /// <summary>Missing XML is inert and cannot guess DLC mechanics.</summary>
        public static AuthoritySpeechPolicyBuilder CreateDefault()
        {
            return new AuthoritySpeechPolicyBuilder();
        }

        public AuthoritySpeechPolicySnapshot Build()
        {
            return new AuthoritySpeechPolicySnapshot(this);
        }
    }

    /// <summary>Immutable, deeply copied authority-speech policy used by runtime and pure tests.</summary>
    internal sealed class AuthoritySpeechPolicySnapshot
    {
        public readonly bool enabled;
        public readonly IReadOnlyList<AuthoritySpeechRouteSnapshot> routes;
        public readonly string evidenceGroupKey;
        public readonly string topicToken;
        public readonly string speakerEvidenceMode;
        public readonly string targetEvidenceMode;
        public readonly string participantEvidenceMode;
        public readonly string spectatorEvidenceMode;
        public readonly BeliefContextProjection speakerProjection;
        public readonly BeliefContextProjection witnessProjection;

        internal AuthoritySpeechPolicySnapshot(AuthoritySpeechPolicyBuilder source)
        {
            AuthoritySpeechPolicyBuilder value = source ?? AuthoritySpeechPolicyBuilder.CreateDefault();
            enabled = value.enabled;
            routes = CopyRoutes(value.routes);
            evidenceGroupKey = AuthoritySpeechPolicy.SafeToken(value.evidenceGroupKey);
            topicToken = AuthoritySpeechPolicy.SafeToken(value.topicToken);
            speakerEvidenceMode = KnownMode(value.speakerEvidenceMode);
            targetEvidenceMode = KnownMode(value.targetEvidenceMode);
            participantEvidenceMode = KnownMode(value.participantEvidenceMode);
            spectatorEvidenceMode = KnownMode(value.spectatorEvidenceMode);
            speakerProjection = Projection(
                value.speakerMaximumSelectedStances, value.speakerMaximumSupportingMemes,
                value.speakerMaximumContextCharacters, value.speakerIncludeRole,
                value.speakerIncludeCertainty, value.speakerIncludeStructure,
                value.speakerIncludeDeity, value.speakerIncludeNarrativeInterpretation,
                value.speakerPromptInstruction);
            witnessProjection = Projection(
                value.witnessMaximumSelectedStances, value.witnessMaximumSupportingMemes,
                value.witnessMaximumContextCharacters, value.witnessIncludeRole,
                value.witnessIncludeCertainty, value.witnessIncludeStructure,
                value.witnessIncludeDeity, value.witnessIncludeNarrativeInterpretation,
                value.witnessPromptInstruction);
        }

        public static AuthoritySpeechPolicySnapshot CreateDefault()
        {
            return AuthoritySpeechPolicyBuilder.CreateDefault().Build();
        }

        private static IReadOnlyList<AuthoritySpeechRouteSnapshot> CopyRoutes(
            IList<AuthoritySpeechRouteBuilder> source)
        {
            List<AuthoritySpeechRouteSnapshot> copy = new List<AuthoritySpeechRouteSnapshot>();
            if (source != null)
                for (int i = 0; i < source.Count && copy.Count < 8; i++)
                {
                    AuthoritySpeechRouteSnapshot row = new AuthoritySpeechRouteSnapshot(source[i]);
                    if (row.Complete) copy.Add(row);
                }
            return new ReadOnlyCollection<AuthoritySpeechRouteSnapshot>(copy);
        }

        private static string KnownMode(string value)
        {
            string token = AuthoritySpeechPolicy.SafeToken(value);
            return AuthoritySpeechEvidenceModeTokens.IsKnown(token) ? token : string.Empty;
        }

        private static BeliefContextProjection Projection(
            int stances, int memes, int characters, bool role, bool certainty,
            bool structure, bool deity, bool narrative, string instruction)
        {
            return new BeliefContextProjection
            {
                maximumSelectedStances = AuthoritySpeechPolicy.Clamp(stances, 1, 2, 1),
                maximumSupportingMemes = AuthoritySpeechPolicy.Clamp(memes, 0, 2, 0),
                maximumContextCharacters = AuthoritySpeechPolicy.Clamp(characters, 64, 1024, 320),
                includeRole = role,
                includeCertainty = certainty,
                includeStructure = structure,
                includeDeity = deity,
                includeNarrativeInterpretation = narrative,
                promptInstruction = AuthoritySpeechPolicy.CleanPrompt(instruction, 512)
            };
        }
    }

    /// <summary>Pure exact matching and per-perspective detached evidence creation.</summary>
    internal static class AuthoritySpeechPolicy
    {
        public const string PerspectiveSpeaker = "author";
        public const string PerspectiveTarget = "target";
        public const string PerspectiveParticipant = "participant";
        public const string PerspectiveSpectator = "spectator";

        /// <summary>Finds one unique exact route; missing, inactive, or colliding rows fail closed.</summary>
        public static AuthoritySpeechRouteSnapshot Match(
            string ritualDefName,
            string behaviorWorkerClassName,
            string outcomeWorkerClassName,
            string effectiveGroupDefName,
            string assignedSpeakerRoleId,
            bool ideologyActive,
            bool royaltyActive,
            AuthoritySpeechPolicySnapshot policy)
        {
            if (!ideologyActive || !Complete(policy)) return null;
            AuthoritySpeechRouteSnapshot match = null;
            for (int i = 0; i < policy.routes.Count; i++)
            {
                AuthoritySpeechRouteSnapshot route = policy.routes[i];
                if (route.requiresRoyalty && !royaltyActive) continue;
                if (!Exact(ritualDefName, route.ritualDefName)
                    || !Exact(behaviorWorkerClassName, route.behaviorWorkerClassName)
                    || !Exact(outcomeWorkerClassName, route.outcomeWorkerClassName)
                    || !Exact(effectiveGroupDefName, route.downstreamGroupDefName)
                    || !Exact(assignedSpeakerRoleId, route.speakerRoleId)) continue;
                if (match != null) return null;
                match = route;
            }
            return match;
        }

        /// <summary>Builds one isolated POV row. A topic is only a query; the resolver still must find live doctrine.</summary>
        public static BeliefEventEvidence EvidenceFor(
            string pawnId,
            int eventTick,
            string ritualDefName,
            string pawnLabel,
            string perspective,
            AuthoritySpeechRouteSnapshot route,
            AuthoritySpeechPolicySnapshot policy)
        {
            if (route == null || !route.Complete || !Complete(policy)) return null;
            string mode = EvidenceModeFor(perspective, policy);
            if (mode == AuthoritySpeechEvidenceModeTokens.None || mode.Length == 0) return null;
            bool speaker = mode == AuthoritySpeechEvidenceModeTokens.SpeakerAuthority;
            if (speaker != string.Equals(perspective, PerspectiveSpeaker, StringComparison.Ordinal))
                return null;

            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                pawnId, eventTick, "ritual", ritualDefName, "initiator", pawnLabel,
                policy.evidenceGroupKey);
            evidence.narrative.phase = "completed";
            evidence.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
            evidence.narrative.subjectId = pawnId ?? string.Empty;
            evidence.narrative.beliefTopics.Add(policy.topicToken);
            evidence.projection = speaker
                ? BeliefContextProjection.Copy(policy.speakerProjection)
                : BeliefContextProjection.Copy(policy.witnessProjection);
            return BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence) ? evidence : null;
        }

        private static bool Complete(AuthoritySpeechPolicySnapshot policy)
        {
            return policy != null && policy.enabled && policy.routes.Count > 0
                && policy.evidenceGroupKey.Length > 0 && policy.topicToken.Length > 0
                && AuthoritySpeechEvidenceModeTokens.IsKnown(policy.speakerEvidenceMode)
                && AuthoritySpeechEvidenceModeTokens.IsKnown(policy.targetEvidenceMode)
                && AuthoritySpeechEvidenceModeTokens.IsKnown(policy.participantEvidenceMode)
                && AuthoritySpeechEvidenceModeTokens.IsKnown(policy.spectatorEvidenceMode);
        }

        private static string EvidenceModeFor(string perspective, AuthoritySpeechPolicySnapshot policy)
        {
            if (perspective == PerspectiveSpeaker) return policy.speakerEvidenceMode;
            if (perspective == PerspectiveTarget) return policy.targetEvidenceMode;
            if (perspective == PerspectiveParticipant) return policy.participantEvidenceMode;
            if (perspective == PerspectiveSpectator) return policy.spectatorEvidenceMode;
            return string.Empty;
        }

        private static bool Exact(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left, right, StringComparison.Ordinal);
        }

        internal static string CleanIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 160) return string.Empty;
            for (int i = 0; i < trimmed.Length; i++)
                if (char.IsControl(trimmed[i]) || char.IsWhiteSpace(trimmed[i]) || trimmed[i] == ';')
                    return string.Empty;
            return trimmed;
        }

        internal static string SafeToken(string value)
        {
            string token = CleanIdentity(value);
            if (token.Length == 0 || token.Length > 64) return string.Empty;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    return string.Empty;
            }
            return token;
        }

        internal static string CleanPrompt(string value, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string cleaned = BeliefContextFormatter.WholeWord(value, maximumCharacters);
            return cleaned.IndexOf('\n') >= 0 ? string.Empty : cleaned;
        }

        internal static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }
    }
}
