// Pure Narrative Continuity adapter for Ideology. The guarded Phase-1 runtime boundary resolves one
// event against one POV's detached live-belief snapshot, then this file admits only a strong result
// and turns it into the shared interpretation candidate. It never reads Pawn, Ideo, Def, settings,
// localization, or any paid-DLC type.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Frozen Phase-1 result for one exact event/POV. Prompt prose is already localized, sanitized,
    /// and bounded on the main thread before this snapshot reaches pure provider code.
    /// </summary>
    internal sealed class IdeologyNarrativeSnapshot
    {
        public bool providerAvailable;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
        public string povPawnId = string.Empty;
        public string ideologyId = string.Empty;
        public string preceptKeyKind = string.Empty;
        public string preceptStableId = string.Empty;
        public string text = string.Empty;
        public NarrativeEvidence sourceEvidence;
        public List<string> topicTokens = new List<string>();
    }

    /// <summary>
    /// Pure formatter for N3-I's localized factual sentence. Runtime code sanitizes the localized
    /// components first; this helper then verifies that a translation actually consumes every required
    /// numbered placeholder and returns only a complete single-line fact. A long described fact falls
    /// back to the complete no-description form instead of cutting a doctrine sentence mid-caveat.
    /// </summary>
    internal static class IdeologyInterpretationFactFormatter
    {
        /// <summary>
        /// Formats one complete described or concise fact after proving the localized format consumes
        /// every supplied component and the resulting single line fits the caller's character cap.
        /// </summary>
        public static string Format(
            string describedFormat,
            string conciseFormat,
            string ideologyName,
            string preceptLabel,
            string preceptDescription,
            int maximumCharacters)
        {
            string ideology = Trim(ideologyName);
            string label = Trim(preceptLabel);
            string description = Trim(preceptDescription);
            if (ideology.Length == 0 || label.Length == 0 || maximumCharacters <= 0)
            {
                return string.Empty;
            }

            if (description.Length > 0)
            {
                string described;
                if (!TryFormat(describedFormat, new[] { ideology, label, description }, out described))
                {
                    return string.Empty;
                }

                if (SafeCompleteFact(described, maximumCharacters))
                {
                    return described.Trim();
                }

                // Only length may use the concise fallback. Unsafe control/newline output remains a
                // hard failure so a malformed translation cannot smuggle a different prompt shape in.
                if (!SafeCompleteFact(described, int.MaxValue))
                {
                    return string.Empty;
                }
            }

            string concise;
            return TryFormat(conciseFormat, new[] { ideology, label }, out concise)
                && SafeCompleteFact(concise, maximumCharacters)
                    ? concise.Trim()
                    : string.Empty;
        }

        private static bool TryFormat(string format, string[] values, out string result)
        {
            result = string.Empty;
            if (string.IsNullOrWhiteSpace(format) || values == null || values.Length == 0)
            {
                return false;
            }

            // Formatting once with private-use sentinels proves every required argument is really used.
            // string.Format itself rejects malformed braces and any out-of-range placeholder index.
            string[] markers = new string[values.Length];
            object[] markerArguments = new object[values.Length];
            object[] actualArguments = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                markers[i] = "\uE000PawnDiaryN3I" + i + "\uE001";
                markerArguments[i] = markers[i];
                actualArguments[i] = values[i];
            }

            try
            {
                string markerResult = string.Format(format, markerArguments);
                for (int i = 0; i < markers.Length; i++)
                {
                    if (markerResult.IndexOf(markers[i], StringComparison.Ordinal) < 0)
                    {
                        return false;
                    }
                }

                result = string.Format(format, actualArguments);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool SafeCompleteFact(string value, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumCharacters)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>
    /// Projects saved N1 Ideology candidate keys back to current-doctrine precept DefNames. This
    /// activates the resolver's XML repetition penalty without another save field: instance keys are
    /// resolved only through the current detached snapshot, so an ideology change naturally resets the
    /// history and never revives a precept that is no longer present.
    /// </summary>
    internal static class IdeologyNarrativeSelectionHistory
    {
        private const int HardInputScanCap = 64;

        /// <summary>
        /// Returns recent current-doctrine precept DefNames recovered from stable saved candidate keys.
        /// Unknown providers, POVs, ideologies, removed instances, and duplicates are ignored.
        /// </summary>
        public static List<string> PreceptDefNames(
            IList<string> recentCandidateKeys,
            BeliefSnapshot currentSnapshot,
            int maximumSelections)
        {
            List<string> result = new List<string>();
            if (recentCandidateKeys == null || currentSnapshot?.precepts == null
                || maximumSelections <= 0 || string.IsNullOrWhiteSpace(currentSnapshot.pawnId)
                || string.IsNullOrWhiteSpace(currentSnapshot.ideologyId))
            {
                return result;
            }

            int resultCap = Math.Min(HardInputScanCap, maximumSelections);
            int scanCap = Math.Min(HardInputScanCap, recentCandidateKeys.Count);
            for (int i = 0; i < scanCap && result.Count < resultCap; i++)
            {
                string[] parts = (recentCandidateKeys[i] ?? string.Empty).Split('|');
                if (parts.Length != 6 || parts[0] != NarrativeProviderTokens.Ideology
                    || parts[1] != NarrativeCategoryTokens.Interpretation
                    || !string.Equals(parts[2], currentSnapshot.pawnId, StringComparison.Ordinal)
                    || !string.Equals(parts[3], currentSnapshot.ideologyId, StringComparison.Ordinal))
                {
                    continue;
                }

                BeliefPreceptFact matched = FindCurrentPrecept(
                    currentSnapshot.precepts, parts[4], parts[5]);
                string defName = (matched?.defName ?? string.Empty).Trim();
                if (defName.Length > 0 && !Contains(result, defName))
                {
                    result.Add(defName);
                }
            }

            return result;
        }

        private static BeliefPreceptFact FindCurrentPrecept(
            IList<BeliefPreceptFact> precepts,
            string keyKind,
            string stableId)
        {
            if ((keyKind != "instance" && keyKind != "def")
                || string.IsNullOrWhiteSpace(stableId))
            {
                return null;
            }

            for (int i = 0; i < precepts.Count; i++)
            {
                BeliefPreceptFact precept = precepts[i];
                string candidate = keyKind == "instance" ? precept?.instanceId : precept?.defName;
                if (string.Equals((candidate ?? string.Empty).Trim(), stableId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return precept;
                }
            }

            return null;
        }

        private static bool Contains(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Pure high-confidence gate and defensive copier for the Phase-1 resolver result.</summary>
    internal static class IdeologyNarrativeSnapshotFactory
    {
        internal const int MaximumNarrativeTextCharacters = 240;
        private const int MaximumTopics = 8;

        /// <summary>
        /// Creates a candidate-ready snapshot only from a visible stance tied to known source-owned
        /// evidence. Structural resolver tiers are authoritative; lexical tiers must repeat the XML
        /// confidence and runner-up checks so hand-built or future results cannot bypass ambiguity.
        /// </summary>
        public static IdeologyNarrativeSnapshot Create(
            BeliefStanceResolution resolution,
            NarrativeEvidence sourceEvidence,
            BeliefPolicySnapshot policy,
            string formattedText)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            ResolvedBeliefStance stance = TopStanceIfUsable(resolution);
            NarrativeEvidence evidence = CopyEvidence(sourceEvidence);
            if (!effective.enabled || stance == null || evidence == null
                || resolution.narrativeCategory != NarrativeCategoryTokens.Interpretation
                || !HighConfidence(stance, effective)
                || !UsableEvidence(evidence)
                || !SafeKeyPart(resolution.ideologyId)
                || !SafePromptText(formattedText))
            {
                return null;
            }

            string preceptId = SafeKeyPart(stance.precept.instanceId)
                ? stance.precept.instanceId.Trim()
                : SafeKeyPart(stance.precept.defName)
                    ? stance.precept.defName.Trim()
                    : string.Empty;
            if (preceptId.Length == 0) return null;

            return new IdeologyNarrativeSnapshot
            {
                providerAvailable = true,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                povPawnId = evidence.povPawnId,
                ideologyId = resolution.ideologyId.Trim(),
                preceptKeyKind = SafeKeyPart(stance.precept.instanceId) ? "instance" : "def",
                preceptStableId = preceptId,
                // SafePromptText already proves this complete fact is inside the hard cap. Do not run
                // it through another truncator: selected narrative facts are atomic units.
                text = formattedText.Trim(),
                sourceEvidence = evidence,
                topicTokens = Topics(resolution.expandedTopicTokens, evidence.beliefTopics)
            };
        }

        /// <summary>
        /// Re-stamps only the canonical page ID after a source prepared its belief result before the
        /// DiaryEvent existed. Tick, POV, role, and every source fact must already match exactly.
        /// </summary>
        public static IdeologyNarrativeSnapshot ForPage(
            IdeologyNarrativeSnapshot source,
            string eventId,
            int eventTick,
            string povPawnId,
            string povRole)
        {
            if (!UsableSnapshot(source) || !SafeKeyPart(eventId) || !SafeKeyPart(povPawnId)
                || string.IsNullOrWhiteSpace(povRole) || source.sourceEvidence.tick != eventTick
                || !string.Equals(source.povPawnId, povPawnId, StringComparison.Ordinal)
                || !string.Equals(source.sourceEvidence.povPawnId, povPawnId, StringComparison.Ordinal)
                || !string.Equals(source.sourceEvidence.povRole, povRole, StringComparison.Ordinal))
            {
                return null;
            }

            NarrativeEvidence evidence = CopyEvidence(source.sourceEvidence);
            evidence.eventId = eventId.Trim();
            return new IdeologyNarrativeSnapshot
            {
                providerAvailable = true,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                povPawnId = source.povPawnId,
                ideologyId = source.ideologyId,
                preceptKeyKind = source.preceptKeyKind,
                preceptStableId = source.preceptStableId,
                text = source.text,
                sourceEvidence = evidence,
                topicTokens = Topics(source.topicTokens, null)
            };
        }

        private static ResolvedBeliefStance TopStanceIfUsable(BeliefStanceResolution resolution)
        {
            if (resolution?.stances == null || resolution.stances.Count == 0) return null;
            ResolvedBeliefStance stance = resolution.stances[0];
            return stance?.precept != null && stance.precept.visible
                && !string.IsNullOrWhiteSpace(stance.precept.displayLabel)
                    ? stance
                    : null;
        }

        private static bool HighConfidence(ResolvedBeliefStance stance, BeliefPolicySnapshot policy)
        {
            string tier = stance.relevanceTier;
            if (tier == BeliefRelevanceTierTokens.SourcePrecept
                || tier == BeliefRelevanceTierTokens.ExactCorrelation
                || tier == BeliefRelevanceTierTokens.DirectIdentity)
            {
                return true;
            }

            bool lexical = tier == BeliefRelevanceTierTokens.CorrelationText
                || tier == BeliefRelevanceTierTokens.IssueText
                || tier == BeliefRelevanceTierTokens.GeneralText;
            return lexical && Finite(stance.confidenceScore) && Finite(stance.runnerUpGap)
                && stance.confidenceScore >= policy.minimumLexicalConfidence
                && stance.runnerUpGap >= policy.lexicalRunnerUpMargin;
        }

        private static bool UsableEvidence(NarrativeEvidence evidence)
        {
            return evidence != null && evidence.pawnCanKnow == true && evidence.tick >= 0
                && SafeEvidenceIdentity(evidence.eventId) && SafeKeyPart(evidence.povPawnId)
                && KnownPovRole(evidence.povRole) && SafeToken(evidence.phase)
                && NarrativeFacetTokens.IsKnown(evidence.facet)
                && NarrativeSubjectKindTokens.IsKnownOrEmpty(evidence.subjectKind)
                && OptionalSafeEvidenceIdentity(evidence.subjectId)
                && OptionalSafeEvidenceIdentity(evidence.arcKey)
                && OptionalSafeEvidenceIdentity(evidence.relatedEventId)
                && SafeKeyPart(evidence.sourceDomain) && SafeKeyPart(evidence.sourceDefName);
        }

        private static bool UsableSnapshot(IdeologyNarrativeSnapshot snapshot)
        {
            return snapshot != null && snapshot.providerAvailable && snapshot.pawnCanKnow
                && snapshot.hasVerifiedPovConnection && SafeKeyPart(snapshot.povPawnId)
                && SafeKeyPart(snapshot.ideologyId) && SafeKeyPart(snapshot.preceptKeyKind)
                && SafeKeyPart(snapshot.preceptStableId) && SafePromptText(snapshot.text)
                && UsableEvidence(snapshot.sourceEvidence);
        }

        private static NarrativeEvidence CopyEvidence(NarrativeEvidence source)
        {
            if (source == null) return null;
            return new NarrativeEvidence
            {
                eventId = Trim(source.eventId),
                tick = source.tick,
                povPawnId = Trim(source.povPawnId),
                povRole = Trim(source.povRole),
                facet = Trim(source.facet),
                phase = Trim(source.phase),
                subjectKind = Trim(source.subjectKind),
                subjectId = Trim(source.subjectId),
                subjectLabel = WholeWord(source.subjectLabel, 240),
                arcKey = Trim(source.arcKey),
                relatedEventId = Trim(source.relatedEventId),
                beliefTopics = Topics(source.beliefTopics, null),
                salience = NarrativeSalienceTokens.IsKnown(source.salience)
                    ? source.salience
                    : NarrativeSalienceTokens.Minor,
                pawnCanKnow = source.pawnCanKnow,
                sourceDomain = Trim(source.sourceDomain),
                sourceDefName = Trim(source.sourceDefName)
            };
        }

        private static List<string> Topics(IEnumerable<string> first, IEnumerable<string> second)
        {
            List<string> result = new List<string>();
            AddTopics(result, first);
            AddTopics(result, second);
            result.Sort(StringComparer.Ordinal);
            if (result.Count > MaximumTopics) result.RemoveRange(MaximumTopics, result.Count - MaximumTopics);
            return result;
        }

        private static void AddTopics(List<string> result, IEnumerable<string> source)
        {
            if (source == null) return;
            foreach (string value in source)
            {
                string token = Trim(value);
                if (!SafeToken(token) || result.Contains(token)) continue;
                result.Add(token);
            }
        }

        private static bool SafePromptText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumNarrativeTextCharacters)
                return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] == '\r' || value[i] == '\n' || char.IsControl(value[i])) return false;
            return true;
        }

        private static bool SafeKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160) return false;
            string cleaned = value.Trim();
            for (int i = 0; i < cleaned.Length; i++)
                if (cleaned[i] == '|' || cleaned[i] == ';' || char.IsControl(cleaned[i])) return false;
            return true;
        }

        private static bool SafeEvidenceIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160) return false;
            string cleaned = value.Trim();
            for (int i = 0; i < cleaned.Length; i++)
                if (char.IsControl(cleaned[i])) return false;
            return true;
        }

        private static bool OptionalSafeEvidenceIdentity(string value)
        {
            return string.IsNullOrWhiteSpace(value) || SafeEvidenceIdentity(value);
        }

        private static bool KnownPovRole(string value)
        {
            return value == "initiator" || value == "recipient";
        }

        private static bool SafeToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-')) return false;
            }
            return true;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string WholeWord(string value, int maximum)
        {
            string cleaned = Trim(value);
            if (cleaned.Length <= maximum) return cleaned;
            int cut = cleaned.LastIndexOf(' ', maximum - 1, maximum);
            return cut > 0 ? cleaned.Substring(0, cut).TrimEnd() : string.Empty;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Pure fixed-list Ideology provider for one exact high-confidence interpretation.</summary>
    internal static class IdeologyNarrativeProvider
    {
        /// <summary>
        /// Returns one candidate only when the normalized selection evidence is the same event, POV,
        /// source, facet, phase, and subject that produced the detached resolver result.
        /// </summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            IdeologyNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            if (!Usable(snapshot) || evidence == null) return result;

            NarrativeEvidence matched = null;
            for (int i = 0; i < evidence.Count; i++)
            {
                if (ExactMatch(evidence[i], snapshot.sourceEvidence))
                {
                    matched = evidence[i];
                    break;
                }
            }
            if (matched == null) return result;

            string key = "ideology|interpretation|" + snapshot.povPawnId + "|"
                + snapshot.ideologyId + "|" + snapshot.preceptKeyKind + "|"
                + snapshot.preceptStableId;
            if (key.Length > 560) return result;

            result.Add(new NarrativeLensCandidate
            {
                candidateKey = key,
                provider = NarrativeProviderTokens.Ideology,
                category = NarrativeCategoryTokens.Interpretation,
                text = snapshot.text,
                facet = matched.facet,
                subjectKind = matched.subjectKind,
                subjectId = matched.subjectId,
                arcKey = matched.arcKey,
                topicTokens = new List<string>(snapshot.topicTokens ?? new List<string>()),
                sourceEventId = matched.eventId,
                sourceTick = matched.tick,
                salience = matched.salience,
                relationship = NarrativeRelationshipTokens.DirectFacet,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            });
            return result;
        }

        private static bool Usable(IdeologyNarrativeSnapshot snapshot)
        {
            return snapshot != null && snapshot.providerAvailable && snapshot.pawnCanKnow
                && snapshot.hasVerifiedPovConnection && snapshot.sourceEvidence != null
                && Safe(snapshot.povPawnId) && Safe(snapshot.ideologyId)
                && (snapshot.preceptKeyKind == "instance" || snapshot.preceptKeyKind == "def")
                && Safe(snapshot.preceptStableId) && SafeText(snapshot.text)
                && SafeEvidence(snapshot.sourceEvidence) && SafeTopics(snapshot.topicTokens)
                && string.Equals(snapshot.povPawnId, snapshot.sourceEvidence.povPawnId,
                    StringComparison.Ordinal);
        }

        private static bool SafeEvidence(NarrativeEvidence evidence)
        {
            return evidence != null && evidence.pawnCanKnow == true && evidence.tick >= 0
                && SafeEvidenceValue(evidence.eventId) && Safe(evidence.povPawnId)
                && (evidence.povRole == "initiator" || evidence.povRole == "recipient")
                && SafeToken(evidence.phase)
                && NarrativeFacetTokens.IsKnown(evidence.facet)
                && NarrativeSubjectKindTokens.IsKnownOrEmpty(evidence.subjectKind)
                && OptionalSafeEvidenceValue(evidence.subjectId)
                && OptionalSafeEvidenceValue(evidence.arcKey)
                && OptionalSafeEvidenceValue(evidence.relatedEventId)
                && Safe(evidence.sourceDomain) && Safe(evidence.sourceDefName);
        }

        private static bool SafeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] == '\r' || value[i] == '\n' || char.IsControl(value[i])) return false;
            return true;
        }

        private static bool SafeTopics(List<string> topics)
        {
            if (topics == null || topics.Count > 8) return false;
            for (int i = 0; i < topics.Count; i++)
            {
                string token = topics[i];
                if (string.IsNullOrWhiteSpace(token) || token.Length > 80) return false;
                for (int j = 0; j < token.Length; j++)
                {
                    char character = token[j];
                    if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-'))
                        return false;
                }
            }
            return true;
        }

        private static bool SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-'))
                    return false;
            }
            return true;
        }

        private static bool ExactMatch(NarrativeEvidence left, NarrativeEvidence right)
        {
            return left != null && right != null && left.pawnCanKnow == true && right.pawnCanKnow == true
                && left.tick == right.tick
                && Same(left.eventId, right.eventId)
                && Same(left.povPawnId, right.povPawnId)
                && Same(left.povRole, right.povRole)
                && Same(left.facet, right.facet)
                && Same(left.phase, right.phase)
                && Same(left.subjectKind, right.subjectKind)
                && Same(left.subjectId, right.subjectId)
                && Same(left.arcKey, right.arcKey)
                && Same(left.relatedEventId, right.relatedEventId)
                && Same(left.sourceDomain, right.sourceDomain)
                && Same(left.sourceDefName, right.sourceDefName);
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        private static bool Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] == '|' || value[i] == ';' || char.IsControl(value[i])) return false;
            return true;
        }

        private static bool SafeEvidenceValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }

        private static bool OptionalSafeEvidenceValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) || SafeEvidenceValue(value);
        }
    }
}
