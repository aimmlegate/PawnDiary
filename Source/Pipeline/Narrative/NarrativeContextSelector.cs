// Pure deterministic selection for the shared Narrative Continuity Layer. It receives only copied
// evidence/candidates/policy and returns only plain DTOs, so unit tests can prove DLC-safe relevance
// without loading RimWorld or calling a provider.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Selects zero to two non-redundant narrative lenses under an XML-copied policy.</summary>
    internal static class NarrativeContextSelector
    {
        private sealed class ScoredCandidate
        {
            public NarrativeLensCandidate candidate;
            public string relationship;
            public float score;
            public List<NarrativeEvidence> matchedEvidence = new List<NarrativeEvidence>();
        }

        /// <summary>
        /// Evaluates candidates in a deterministic order. Empty/malformed input returns an empty selection
        /// rather than disrupting the source-owned page that requested optional continuity context.
        /// </summary>
        public static NarrativeContextSelection Select(NarrativeContextRequest request)
        {
            NarrativeContextSelection result = new NarrativeContextSelection();
            if (request == null)
            {
                result.selectionReasons.Add(NarrativeDiagnosticTokens.NoEvidence);
                return result;
            }

            NarrativePolicySnapshot policy = request.policy ?? NarrativePolicySnapshot.CreateDefault();
            if (!policy.enabled)
            {
                result.selectionReasons.Add(NarrativeDiagnosticTokens.PolicyDisabled);
                return result;
            }

            List<NarrativeEvidence> evidence = UsableEvidence(request.evidence, policy.maxEvidencePerPov);
            if (evidence.Count == 0)
            {
                result.selectionReasons.Add(NarrativeDiagnosticTokens.NoEvidence);
                return result;
            }

            // References belong to the canonical source event, not to a provider's stale live state. They
            // are generated from the authorized event-time evidence even when no lens survives selection.
            AddEvidenceReferences(result.references, evidence);

            List<ScoredCandidate> accepted = new List<ScoredCandidate>();
            List<NarrativeLensCandidate> candidates = request.candidates ?? new List<NarrativeLensCandidate>();
            int candidateCap = Math.Max(0, policy.maxCandidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                NarrativeLensCandidate candidate = candidates[i];
                if (i >= candidateCap)
                {
                    AddDiagnostic(result, candidate, false, NarrativeDiagnosticTokens.CandidateCap,
                        NarrativeRelationshipTokens.None, 0f);
                    continue;
                }

                string rejection;
                ScoredCandidate scored = Evaluate(candidate, evidence, request, policy, out rejection);
                if (scored == null)
                {
                    AddDiagnostic(result, candidate, false, rejection, NarrativeRelationshipTokens.None, 0f);
                    continue;
                }

                accepted.Add(scored);
            }

            accepted.Sort(CompareScoredCandidates);
            SelectComposed(result, accepted, request, policy);
            result.narrativeContext = Format(result.selectedCandidates);
            return result;
        }

        private static ScoredCandidate Evaluate(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            NarrativeContextRequest request,
            NarrativePolicySnapshot policy,
            out string rejection)
        {
            rejection = string.Empty;
            if (candidate == null)
            {
                rejection = NarrativeDiagnosticTokens.EmptyCandidateKey;
                return null;
            }

            if (string.IsNullOrWhiteSpace(candidate.candidateKey))
            {
                rejection = NarrativeDiagnosticTokens.EmptyCandidateKey;
                return null;
            }

            if (string.IsNullOrWhiteSpace(candidate.text))
            {
                rejection = NarrativeDiagnosticTokens.EmptyCandidateText;
                return null;
            }

            if (!candidate.pawnCanKnow)
            {
                rejection = NarrativeDiagnosticTokens.UnknownKnowledge;
                return null;
            }

            if (!NarrativeProviderTokens.IsKnown(candidate.provider))
            {
                rejection = NarrativeDiagnosticTokens.UnknownProvider;
                return null;
            }

            if (!NarrativeFacetTokens.IsKnown(candidate.facet))
            {
                rejection = NarrativeDiagnosticTokens.UnknownFacet;
                return null;
            }

            if (!NarrativeCategoryTokens.IsKnown(candidate.category))
            {
                rejection = NarrativeDiagnosticTokens.UnknownCategory;
                return null;
            }

            if (!candidate.providerAvailable)
            {
                rejection = NarrativeDiagnosticTokens.ProviderUnavailable;
                return null;
            }

            // A zero current tick is an intentionally incomplete synthetic/old-save request. It cannot
            // prove a source is future-dated, so keep the candidate rather than turning missing timing
            // metadata into a false negative. Real adapters always provide the current game tick.
            if (request.currentTick > 0 && candidate.sourceTick > request.currentTick && candidate.sourceTick > 0)
            {
                rejection = NarrativeDiagnosticTokens.FutureSource;
                return null;
            }

            if (candidate.sourceTick > 0 && request.currentTick > 0
                && request.currentTick - candidate.sourceTick > Math.Max(0, policy.maximumCandidateAgeTicks))
            {
                rejection = NarrativeDiagnosticTokens.TooOld;
                return null;
            }

            if (candidate.isPrimaryEventFact)
            {
                rejection = NarrativeDiagnosticTokens.PrimaryFactDuplicate;
                return null;
            }

            if (candidate.relationship == NarrativeRelationshipTokens.Ambient
                && !candidate.hasVerifiedPovConnection)
            {
                rejection = NarrativeDiagnosticTokens.AmbientDisconnected;
                return null;
            }

            if (ConflictsWithExactEvidence(candidate, evidence))
            {
                rejection = NarrativeDiagnosticTokens.SubjectConflict;
                return null;
            }

            List<NarrativeEvidence> matched = new List<NarrativeEvidence>();
            string relationship = RelationshipFor(candidate, evidence, policy, matched);
            if (relationship == NarrativeRelationshipTokens.None)
            {
                rejection = NarrativeDiagnosticTokens.Unrelated;
                return null;
            }

            float score = Score(candidate, relationship, matched, request, policy);
            return new ScoredCandidate
            {
                candidate = candidate,
                relationship = relationship,
                score = score,
                matchedEvidence = matched
            };
        }

        private static List<NarrativeEvidence> UsableEvidence(List<NarrativeEvidence> source, int maximum)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            if (source == null || maximum <= 0)
            {
                return result;
            }

            for (int i = 0; i < source.Count && result.Count < maximum; i++)
            {
                NarrativeEvidence evidence = source[i];
                if (evidence == null || evidence.pawnCanKnow != true
                    || !NarrativeFacetTokens.IsKnown(evidence.facet)
                    || !NarrativeSubjectKindTokens.IsKnownOrEmpty(evidence.subjectKind))
                {
                    continue;
                }

                result.Add(evidence);
            }

            return result;
        }

        private static bool ConflictsWithExactEvidence(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence)
        {
            if (string.IsNullOrWhiteSpace(candidate.subjectKind) || string.IsNullOrWhiteSpace(candidate.subjectId))
            {
                return false;
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence current = evidence[i];
                if (!EqualsOrdinal(candidate.subjectKind, current.subjectKind)
                    || string.IsNullOrWhiteSpace(current.subjectId))
                {
                    continue;
                }

                if (!EqualsOrdinal(candidate.subjectId, current.subjectId)
                    && (candidate.relationship == NarrativeRelationshipTokens.ExactSubject
                        || candidate.relationship == NarrativeRelationshipTokens.ExactArc))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RelationshipFor(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            NarrativePolicySnapshot policy,
            List<NarrativeEvidence> matched)
        {
            if (CollectExactArc(candidate, evidence, matched))
            {
                return NarrativeRelationshipTokens.ExactArc;
            }

            if (CollectExactSubject(candidate, evidence, matched))
            {
                return NarrativeRelationshipTokens.ExactSubject;
            }

            if (CollectTopicAffinity(candidate, evidence, policy, matched))
            {
                return NarrativeRelationshipTokens.DirectTopic;
            }

            if (CollectFacet(candidate, evidence, matched))
            {
                return NarrativeRelationshipTokens.DirectFacet;
            }

            if (candidate.relationship == NarrativeRelationshipTokens.Ambient
                && candidate.hasVerifiedPovConnection)
            {
                return NarrativeRelationshipTokens.Ambient;
            }

            return NarrativeRelationshipTokens.None;
        }

        private static bool CollectExactArc(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            List<NarrativeEvidence> matched)
        {
            if (string.IsNullOrWhiteSpace(candidate.arcKey))
            {
                return false;
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                if (EqualsOrdinal(candidate.arcKey, evidence[i].arcKey))
                {
                    matched.Add(evidence[i]);
                    return true;
                }
            }

            return false;
        }

        private static bool CollectExactSubject(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            List<NarrativeEvidence> matched)
        {
            if (string.IsNullOrWhiteSpace(candidate.subjectKind) || string.IsNullOrWhiteSpace(candidate.subjectId))
            {
                return false;
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                if (EqualsOrdinal(candidate.subjectKind, evidence[i].subjectKind)
                    && EqualsOrdinal(candidate.subjectId, evidence[i].subjectId))
                {
                    matched.Add(evidence[i]);
                    return true;
                }
            }

            return false;
        }

        private static bool CollectTopicAffinity(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            NarrativePolicySnapshot policy,
            List<NarrativeEvidence> matched)
        {
            for (int evidenceIndex = 0; evidenceIndex < evidence.Count; evidenceIndex++)
            {
                NarrativeEvidence current = evidence[evidenceIndex];
                if (TopicsMatch(current.beliefTopics, candidate.topicTokens)
                    || PolicyAffinityMatches(current, candidate, policy))
                {
                    matched.Add(current);
                    return true;
                }
            }

            return false;
        }

        private static bool CollectFacet(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            List<NarrativeEvidence> matched)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                if (EqualsOrdinal(candidate.facet, evidence[i].facet))
                {
                    matched.Add(evidence[i]);
                    return true;
                }
            }

            return false;
        }

        private static float Score(
            NarrativeLensCandidate candidate,
            string relationship,
            List<NarrativeEvidence> matched,
            NarrativeContextRequest request,
            NarrativePolicySnapshot policy)
        {
            float score = TokenScore(policy.relationshipScores, relationship)
                + TokenScore(policy.facetScores, candidate.facet)
                + TokenScore(policy.categoryScores, candidate.category)
                + TokenScore(policy.salienceScores, NormalizeSalience(candidate.salience))
                + TokenScore(policy.providerScores, candidate.provider)
                + AffinityScore(candidate, matched, policy);

            score *= AgeMultiplier(candidate.sourceTick, request.currentTick, policy);
            if (ContainsOrdinal(request.recentSelectedCandidateKeys, candidate.candidateKey))
            {
                score -= relationship == NarrativeRelationshipTokens.ExactArc
                    ? Math.Max(0f, policy.exactArcRepetitionPenalty)
                    : Math.Max(0f, policy.repetitionPenalty);
            }

            return score;
        }

        private static float AgeMultiplier(int sourceTick, int currentTick, NarrativePolicySnapshot policy)
        {
            if (sourceTick <= 0 || currentTick <= 0 || sourceTick >= currentTick)
            {
                return 1f;
            }

            int window = Math.Max(1, policy.ageDecayWindowTicks);
            int age = currentTick - sourceTick;
            if (age <= 0)
            {
                return 1f;
            }

            float floor = Math.Max(0f, Math.Min(1f, policy.ageDecayFloor));
            if (age >= window)
            {
                return floor;
            }

            float progress = (float)age / window;
            return 1f - ((1f - floor) * progress);
        }

        private static float AffinityScore(
            NarrativeLensCandidate candidate,
            List<NarrativeEvidence> evidence,
            NarrativePolicySnapshot policy)
        {
            float score = 0f;
            if (policy.affinityRules == null)
            {
                return score;
            }

            for (int ruleIndex = 0; ruleIndex < policy.affinityRules.Count; ruleIndex++)
            {
                NarrativeAffinityRule rule = policy.affinityRules[ruleIndex];
                if (rule == null || string.IsNullOrWhiteSpace(rule.evidenceToken)
                    || string.IsNullOrWhiteSpace(rule.candidateToken))
                {
                    continue;
                }

                for (int evidenceIndex = 0; evidenceIndex < evidence.Count; evidenceIndex++)
                {
                    if (EvidenceHasToken(evidence[evidenceIndex], rule.evidenceToken)
                        && CandidateHasToken(candidate, rule.candidateToken))
                    {
                        score += rule.score;
                        break;
                    }
                }
            }

            return score;
        }

        private static bool PolicyAffinityMatches(
            NarrativeEvidence evidence,
            NarrativeLensCandidate candidate,
            NarrativePolicySnapshot policy)
        {
            if (policy.affinityRules == null)
            {
                return false;
            }

            for (int i = 0; i < policy.affinityRules.Count; i++)
            {
                NarrativeAffinityRule rule = policy.affinityRules[i];
                if (rule != null && EvidenceHasToken(evidence, rule.evidenceToken)
                    && CandidateHasToken(candidate, rule.candidateToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EvidenceHasToken(NarrativeEvidence evidence, string token)
        {
            return evidence != null && (EqualsOrdinal(evidence.facet, token)
                || ContainsOrdinal(evidence.beliefTopics, token));
        }

        private static bool CandidateHasToken(NarrativeLensCandidate candidate, string token)
        {
            return candidate != null && (EqualsOrdinal(candidate.facet, token)
                || ContainsOrdinal(candidate.topicTokens, token));
        }

        private static void SelectComposed(
            NarrativeContextSelection result,
            List<ScoredCandidate> accepted,
            NarrativeContextRequest request,
            NarrativePolicySnapshot policy)
        {
            NarrativeDetailBudget budget = BudgetFor(policy, request.detailLevel);
            int maxSelected = Math.Max(0, Math.Min(policy.maxSelectedCandidates, budget.maxLenses));
            int characterBudget = request.promptCharacterBudget > 0
                ? request.promptCharacterBudget
                : Math.Max(0, budget.characterBudget);
            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            List<string> selectedRelationships = new List<string>();

            for (int i = 0; i < accepted.Count; i++)
            {
                ScoredCandidate scored = accepted[i];
                NarrativeLensCandidate candidate = scored.candidate;
                string rejection = CompositionRejection(result.selectedCandidates, selectedRelationships,
                    categories, candidate, scored, budget, maxSelected, characterBudget, policy);
                if (!string.IsNullOrEmpty(rejection))
                {
                    AddDiagnostic(result, candidate, false, rejection, scored.relationship, scored.score);
                    continue;
                }

                result.selectedCandidates.Add(candidate);
                selectedRelationships.Add(scored.relationship);
                categories.Add(candidate.category);
                if (candidate.category == NarrativeCategoryTokens.Interpretation)
                {
                    result.selectedInterpretation = true;
                }

                result.selectionReasons.Add(scored.relationship);
                AddDiagnostic(result, candidate, true, NarrativeDiagnosticTokens.Selected,
                    scored.relationship, scored.score);
            }
        }

        private static string CompositionRejection(
            List<NarrativeLensCandidate> selected,
            List<string> selectedRelationships,
            HashSet<string> categories,
            NarrativeLensCandidate candidate,
            ScoredCandidate scored,
            NarrativeDetailBudget budget,
            int maxSelected,
            int characterBudget,
            NarrativePolicySnapshot policy)
        {
            bool specialExactArcPair = selected.Count == 1 && maxSelected < 2
                && budget.allowExactArcPair
                && scored.relationship == NarrativeRelationshipTokens.ExactArc
                && candidate.text.Trim().Length <= Math.Max(0, budget.exactArcPairMaxCharacters)
                && HasOnlyExactArc(selectedRelationships);
            if (selected.Count >= maxSelected && !specialExactArcPair)
            {
                return NarrativeDiagnosticTokens.DetailCap;
            }

            if (categories.Contains(candidate.category))
            {
                return candidate.category == NarrativeCategoryTokens.Interpretation
                    ? NarrativeDiagnosticTokens.InterpretationCap
                    : NarrativeDiagnosticTokens.CategoryCap;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                NarrativeLensCandidate existing = selected[i];
                if (!CategoriesCanCoexist(existing.category, candidate.category, policy))
                {
                    return NarrativeDiagnosticTokens.PairConflict;
                }

                if (TextEquivalent(existing.text, candidate.text)
                    || TopicsRedundant(existing.topicTokens, candidate.topicTokens))
                {
                    return NarrativeDiagnosticTokens.Redundant;
                }
            }

            if (!FitsCharacterBudget(selected, candidate, characterBudget))
            {
                return NarrativeDiagnosticTokens.CharacterBudget;
            }

            return string.Empty;
        }

        private static bool HasOnlyExactArc(List<string> selectedRelationships)
        {
            return selectedRelationships != null && selectedRelationships.Count == 1
                && selectedRelationships[0] == NarrativeRelationshipTokens.ExactArc;
        }

        private static bool CategoriesCanCoexist(string left, string right, NarrativePolicySnapshot policy)
        {
            if (policy.categoryCoexistence != null)
            {
                for (int i = 0; i < policy.categoryCoexistence.Count; i++)
                {
                    NarrativeCategoryCoexistenceRule rule = policy.categoryCoexistence[i];
                    if (rule != null && ((EqualsOrdinal(rule.firstCategory, left)
                            && EqualsOrdinal(rule.secondCategory, right))
                        || (EqualsOrdinal(rule.firstCategory, right)
                            && EqualsOrdinal(rule.secondCategory, left))))
                    {
                        return rule.allowed;
                    }
                }
            }

            return !EqualsOrdinal(left, right);
        }

        private static bool FitsCharacterBudget(
            List<NarrativeLensCandidate> selected,
            NarrativeLensCandidate candidate,
            int characterBudget)
        {
            if (characterBudget <= 0)
            {
                return false;
            }

            int chars = candidate.text == null ? 0 : candidate.text.Trim().Length;
            for (int i = 0; i < selected.Count; i++)
            {
                chars += selected[i].text == null ? 0 : selected[i].text.Trim().Length;
            }

            // Format joins factual units with one newline. Never cut a fact merely to reach the budget.
            chars += selected.Count;
            return chars <= characterBudget;
        }

        private static NarrativeDetailBudget BudgetFor(NarrativePolicySnapshot policy, string detailLevel)
        {
            string normalized = NarrativeDetailLevelTokens.Normalize(detailLevel);
            if (policy.detailBudgets != null)
            {
                for (int i = 0; i < policy.detailBudgets.Count; i++)
                {
                    NarrativeDetailBudget budget = policy.detailBudgets[i];
                    if (budget != null && NarrativeDetailLevelTokens.Normalize(budget.detailLevel) == normalized)
                    {
                        return budget;
                    }
                }
            }

            NarrativePolicySnapshot fallback = NarrativePolicySnapshot.CreateDefault();
            for (int i = 0; i < fallback.detailBudgets.Count; i++)
            {
                if (fallback.detailBudgets[i].detailLevel == normalized)
                {
                    return fallback.detailBudgets[i];
                }
            }

            return new NarrativeDetailBudget();
        }

        private static int CompareScoredCandidates(ScoredCandidate left, ScoredCandidate right)
        {
            int score = right.score.CompareTo(left.score);
            if (score != 0)
            {
                return score;
            }

            int relationship = NarrativeRelationshipTokens.Rank(right.relationship)
                .CompareTo(NarrativeRelationshipTokens.Rank(left.relationship));
            if (relationship != 0)
            {
                return relationship;
            }

            int tick = right.candidate.sourceTick.CompareTo(left.candidate.sourceTick);
            return tick != 0 ? tick : string.Compare(left.candidate.candidateKey, right.candidate.candidateKey,
                StringComparison.Ordinal);
        }

        private static void AddEvidenceReferences(List<NarrativeReference> target, List<NarrativeEvidence> evidence)
        {
            List<NarrativeReference> candidateReferences = new List<NarrativeReference>();
            for (int i = 0; i < evidence.Count; i++)
            {
                candidateReferences.Add(NarrativeReferencePolicy.FromEvidence(evidence[i]));
            }

            List<NarrativeReference> unique = NarrativeReferencePolicy.Unique(candidateReferences);
            for (int i = 0; i < unique.Count; i++)
            {
                target.Add(unique[i]);
            }
        }

        private static string Format(List<NarrativeLensCandidate> selected)
        {
            if (selected == null || selected.Count == 0)
            {
                return string.Empty;
            }

            List<string> facts = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                string text = selected[i] == null ? string.Empty : (selected[i].text ?? string.Empty).Trim();
                if (text.Length > 0)
                {
                    facts.Add(text);
                }
            }

            return string.Join("\n", facts.ToArray());
        }

        private static void AddDiagnostic(
            NarrativeContextSelection result,
            NarrativeLensCandidate candidate,
            bool selected,
            string reason,
            string relationship,
            float score)
        {
            result.diagnostics.Add(new NarrativeCandidateDiagnostic
            {
                candidateKey = candidate == null ? string.Empty : (candidate.candidateKey ?? string.Empty),
                selected = selected,
                reason = reason ?? string.Empty,
                relationship = relationship ?? NarrativeRelationshipTokens.None,
                score = score
            });
        }

        private static float TokenScore(List<NarrativeTokenWeight> values, string token)
        {
            if (values == null)
            {
                return 0f;
            }

            for (int i = 0; i < values.Count; i++)
            {
                NarrativeTokenWeight value = values[i];
                if (value != null && EqualsOrdinal(value.token, token))
                {
                    return value.score;
                }
            }

            return 0f;
        }

        private static bool TopicsMatch(List<string> left, List<string> right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (ContainsOrdinal(right, left[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TopicsRedundant(List<string> left, List<string> right)
        {
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            bool hasUsefulTopic = false;
            for (int i = 0; i < right.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(right[i]))
                {
                    continue;
                }

                hasUsefulTopic = true;
                if (!ContainsOrdinal(left, right[i]))
                {
                    return false;
                }
            }

            // Only the proposed candidate's topics need to be contained in an already selected fact.
            // A new "loss" topic makes [bonding, loss] distinct from [bonding]; the inverse carries no
            // additional topic and is redundant.
            return hasUsefulTopic;
        }

        private static bool TextEquivalent(string left, string right)
        {
            return string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            List<char> result = new List<char>();
            bool previousWhitespace = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char current = trimmed[i];
                if (char.IsWhiteSpace(current))
                {
                    if (!previousWhitespace)
                    {
                        result.Add(' ');
                    }

                    previousWhitespace = true;
                }
                else
                {
                    result.Add(current);
                    previousWhitespace = false;
                }
            }

            return new string(result.ToArray());
        }

        private static string NormalizeSalience(string value)
        {
            return NarrativeSalienceTokens.IsKnown(value) ? value : NarrativeSalienceTokens.Minor;
        }

        private static bool ContainsOrdinal(List<string> values, string target)
        {
            if (values == null || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (EqualsOrdinal(values[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EqualsOrdinal(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }
}
