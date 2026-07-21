// Pure event-relative belief resolver. Structural evidence is evaluated in strict stages before the
// guarded lexical fallback. Candidate strength, impact, repetition, and localized wording can reorder
// only already-relevant live doctrine; none can admit an unrelated precept.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Expanded, deduplicated event vocabulary produced independently of doctrine selection.</summary>
    internal sealed class ExpandedBeliefEvidence
    {
        public readonly List<string> topics = new List<string>();
        public readonly List<string> semanticAliases = new List<string>();
        public readonly List<string> matchedRuleKeys = new List<string>();
    }

    /// <summary>Applies exact XML-owned event-evidence rules without naming or selecting a precept.</summary>
    internal static class BeliefEventEvidencePolicy
    {
        public static ExpandedBeliefEvidence Expand(BeliefEventEvidence evidence, BeliefPolicySnapshot policy)
        {
            ExpandedBeliefEvidence result = new ExpandedBeliefEvidence();
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (evidence == null) return result;
            NarrativeEvidence narrative = evidence.narrative;
            if (narrative != null && narrative.beliefTopics != null)
                AddSafeTokens(result.topics, narrative.beliefTopics, 32);
            AddSafeTokens(result.semanticAliases, evidence.semanticAliasTokens, 32);

            for (int i = 0; i < effective.eventEvidenceRules.Count; i++)
            {
                BeliefEventEvidenceRule rule = effective.eventEvidenceRules[i];
                if (rule == null || !Matches(rule, evidence)) continue;
                AddSafeTokens(result.topics, rule.addTopics, 32);
                AddSafeTokens(result.semanticAliases, rule.addSemanticAliases, 32);
                AddSafeToken(result.matchedRuleKeys, rule.key, 32);
            }
            result.topics.Sort(StringComparer.Ordinal);
            result.semanticAliases.Sort(StringComparer.Ordinal);
            result.matchedRuleKeys.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool Matches(BeliefEventEvidenceRule rule, BeliefEventEvidence evidence)
        {
            // A selectorless XML row must never become an implicit global rule, even if a malformed
            // snapshot reaches this defensive layer without passing through the normal copy filter.
            if (rule == null || !rule.HasSelector) return false;
            NarrativeEvidence narrative = evidence.narrative;
            if (!OptionalSame(rule.sourceDomain, narrative == null ? string.Empty : narrative.sourceDomain)) return false;
            if (!OptionalSame(rule.sourceDefName, narrative == null ? string.Empty : narrative.sourceDefName)) return false;
            if (!OptionalSame(rule.groupKey, evidence.groupKey)) return false;
            if (!OptionalSame(rule.facet, narrative == null ? string.Empty : narrative.facet)) return false;
            if (!OptionalSame(rule.phase, narrative == null ? string.Empty : narrative.phase)) return false;
            if (!OptionalSame(rule.povRole, narrative == null ? string.Empty : narrative.povRole)) return false;
            if (rule.mutationCauseToken.Length > 0
                && !Contains(evidence.mutation == null ? null : evidence.mutation.causeTokens, rule.mutationCauseToken))
                return false;
            return true;
        }

        private static bool OptionalSame(string wanted, string actual)
        {
            return string.IsNullOrWhiteSpace(wanted) || Same(wanted, actual);
        }

        private static bool Contains(IList<string> values, string wanted)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (Same(values[i], wanted)) return true;
            return false;
        }

        private static void AddSafeTokens(List<string> target, IEnumerable<string> values, int cap)
        {
            if (values == null) return;
            foreach (string value in values)
            {
                if (target.Count >= cap) break;
                AddSafeToken(target, value, cap);
            }
        }

        private static void AddSafeToken(List<string> target, string value, int cap)
        {
            if (target.Count >= cap) return;
            string token = (value ?? string.Empty).Trim();
            if (token.Length == 0 || token.Length > 96) return;
            for (int i = 0; i < token.Length; i++)
            {
                char character = token[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-')) return;
            }
            for (int i = 0; i < target.Count; i++) if (Same(target[i], token)) return;
            target.Add(token);
        }

        private static bool Same(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Resolves zero to two event-relative stances from one detached live doctrine snapshot.</summary>
    internal static class EventRelativeStanceResolver
    {
        private sealed class Candidate
        {
            public BeliefPreceptFact precept;
            public BeliefMemeFact meme;
            public string relevanceSource;
            public string relevanceTier;
            public string matchedIdentity;
            public string correctionKey;
            public string valence;
            public string evidenceKey;
            public float baseScore;
            public float confidence;
            public float runnerUpGap;
            public float finalScore;
            public float selectionWeight;
            public uint tie;
        }

        /// <summary>
        /// Applies fail-closed knowledge gating, structural precedence, conservative lexical fallback,
        /// redundancy collapse, and the independent second-slot rule.
        /// </summary>
        public static BeliefStanceResolution Resolve(BeliefResolutionRequest request)
        {
            BeliefStanceResolution empty = new BeliefStanceResolution();
            if (request == null || request.snapshot == null || request.evidence == null) return empty;
            BeliefPolicySnapshot policy = request.policy ?? BeliefPolicySnapshot.CreateDefault();
            BeliefSnapshot snapshot = request.snapshot;
            BeliefEventEvidence evidence = request.evidence;
            if (!policy.enabled || !BeliefResolutionModeTokens.IsKnown(request.mode)
                || !snapshot.ideologyActive || SafeId(snapshot.ideologyId, policy).Length == 0
                || evidence.narrative == null || evidence.narrative.pawnCanKnow != true)
                return empty;

            ExpandedBeliefEvidence expanded = BeliefEventEvidencePolicy.Expand(evidence, policy);
            if (request.mode == BeliefResolutionModeTokens.EventEnrichment
                && !HasUsefulVisibleEvidence(evidence, expanded)) return empty;

            List<BeliefPreceptFact> livePrecepts = NormalizePrecepts(snapshot.precepts, evidence, expanded, policy);
            List<BeliefMemeFact> liveMemes = NormalizeMemes(snapshot.memes, policy);
            List<Candidate> candidates = SourcePreceptCandidates(evidence, livePrecepts, policy);
            if (candidates.Count == 0)
                candidates = ExactCorrelationCandidates(evidence, livePrecepts, policy);

            List<BeliefMemeFact> directlyMatchedMemes = new List<BeliefMemeFact>();
            if (candidates.Count == 0)
                candidates = DirectIdentityCandidates(evidence, livePrecepts, liveMemes, directlyMatchedMemes, policy);
            bool directIdentityAnswered = candidates.Count > 0 || directlyMatchedMemes.Count > 0;
            if (!directIdentityAnswered)
                candidates = ForcedCorrectionCandidates(evidence, expanded, livePrecepts, liveMemes, policy);
            if (!directIdentityAnswered && candidates.Count == 0)
                candidates = LexicalCandidates(evidence, expanded, snapshot, livePrecepts, policy);
            if (!directIdentityAnswered && candidates.Count == 0
                && request.mode == BeliefResolutionModeTokens.QuietReflection)
                candidates = QuietFallbackCandidates(livePrecepts, policy);

            BeliefStanceResolution result = new BeliefStanceResolution();
            result.ideologyId = snapshot.ideologyId ?? string.Empty;
            result.ideologyName = snapshot.ideologyName ?? string.Empty;
            result.roleName = snapshot.roleName ?? string.Empty;
            result.expandedTopicTokens.AddRange(expanded.topics);
            result.mutation = evidence.mutation != null && evidence.mutation.HasUsefulFact ? evidence.mutation : null;
            List<Candidate> selected = Select(candidates, evidence, request, policy);
            for (int i = 0; i < selected.Count; i++) result.stances.Add(ToResolved(selected[i]));

            AddSupportingMemes(result, selected, directlyMatchedMemes, liveMemes, policy);
            if (!result.HasUsefulContext) return empty;

            result.structure = policy.includeStructure ? NormalizeMeme(snapshot.structure, policy) : null;
            result.deity = SelectDeity(snapshot.deities, result.supportingMemes, request.deterministicSeed, policy);
            BeliefCertaintyPolicy.Apply(snapshot.certainty, evidence.mutation, policy, result);
            for (int i = 0; i < selected.Count; i++) AddUnique(result.selectionReasonTokens, selected[i].relevanceSource);
            for (int i = 0; i < expanded.matchedRuleKeys.Count; i++) AddUnique(result.selectionReasonTokens, expanded.matchedRuleKeys[i]);
            return result;
        }

        private static List<Candidate> SourcePreceptCandidates(
            BeliefEventEvidence evidence,
            List<BeliefPreceptFact> precepts,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            string instanceId = SafeId(evidence.sourcePreceptInstanceId, policy);
            if (instanceId.Length > 0)
            {
                for (int i = 0; i < precepts.Count; i++)
                    if (Same(instanceId, precepts[i].instanceId))
                    {
                        result.Add(NewCandidate(precepts[i], null, BeliefRelevanceSourceTokens.SourcePrecept,
                            BeliefRelevanceTierTokens.SourcePrecept, instanceId, "source|" + instanceId,
                            BeliefValenceTokens.Unknown, policy));
                        return result;
                    }
            }

            string defName = SafeId(evidence.sourcePreceptDefName, policy);
            if (defName.Length == 0) return result;
            BeliefPreceptFact unique = null;
            for (int i = 0; i < precepts.Count; i++)
            {
                if (!Same(defName, precepts[i].defName)) continue;
                if (unique != null) return new List<Candidate>();
                unique = precepts[i];
            }
            if (unique != null)
                result.Add(NewCandidate(unique, null, BeliefRelevanceSourceTokens.SourcePrecept,
                    BeliefRelevanceTierTokens.SourcePrecept, defName, "source_def|" + defName,
                    BeliefValenceTokens.Unknown, policy));
            return result;
        }

        private static List<Candidate> ExactCorrelationCandidates(
            BeliefEventEvidence evidence,
            List<BeliefPreceptFact> precepts,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            AddCorrelationCandidates(result, evidence.thoughtDefNames, BeliefCorrelationKindTokens.Thought,
                BeliefRelevanceSourceTokens.ThoughtCorrelation, precepts, policy);
            AddCorrelationCandidates(result, evidence.historyEventDefNames, BeliefCorrelationKindTokens.HistoryEvent,
                BeliefRelevanceSourceTokens.HistoryCorrelation, precepts, policy);
            return result;
        }

        private static void AddCorrelationCandidates(
            List<Candidate> target,
            IList<string> evidenceIds,
            string correlationKind,
            string relevanceSource,
            List<BeliefPreceptFact> precepts,
            BeliefPolicySnapshot policy)
        {
            if (evidenceIds == null) return;
            int evidenceCap = Math.Min(evidenceIds.Count, 32);
            for (int e = 0; e < evidenceCap; e++)
            {
                string evidenceId = SafeId(evidenceIds[e], policy);
                if (evidenceId.Length == 0) continue;
                for (int p = 0; p < precepts.Count; p++)
                {
                    BeliefPreceptFact precept = precepts[p];
                    if (precept.correlations == null) continue;
                    int correlationCap = Math.Min(precept.correlations.Count, 64);
                    for (int c = 0; c < correlationCap; c++)
                    {
                        BeliefCorrelationFact correlation = precept.correlations[c];
                        if (correlation == null || !Same(correlation.kind, correlationKind)
                            || !Same(correlation.defName, evidenceId)) continue;
                        target.Add(NewCandidate(precept, null, relevanceSource,
                            BeliefRelevanceTierTokens.ExactCorrelation, evidenceId,
                            correlationKind + "|" + evidenceId, BeliefValenceTokens.Normalize(correlation.valence), policy));
                    }
                }
            }
        }

        private static List<Candidate> DirectIdentityCandidates(
            BeliefEventEvidence evidence,
            List<BeliefPreceptFact> precepts,
            List<BeliefMemeFact> memes,
            List<BeliefMemeFact> directlyMatchedMemes,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            if (evidence.issueDefNames != null)
            {
                int cap = Math.Min(evidence.issueDefNames.Count, 32);
                for (int e = 0; e < cap; e++)
                {
                    string issue = SafeId(evidence.issueDefNames[e], policy);
                    if (issue.Length == 0) continue;
                    for (int p = 0; p < precepts.Count; p++)
                        if (precepts[p].issue != null && Same(precepts[p].issue.defName, issue))
                            result.Add(NewCandidate(precepts[p], null, BeliefRelevanceSourceTokens.IssueIdentity,
                                BeliefRelevanceTierTokens.DirectIdentity, issue, "issue|" + issue,
                                BeliefValenceTokens.Unknown, policy));
                }
            }
            if (evidence.memeDefNames != null)
            {
                int cap = Math.Min(evidence.memeDefNames.Count, 32);
                for (int e = 0; e < cap; e++)
                {
                    string memeId = SafeId(evidence.memeDefNames[e], policy);
                    if (memeId.Length == 0) continue;
                    BeliefMemeFact meme = FindMeme(memes, memeId);
                    if (meme == null) continue;
                    AddUniqueMeme(directlyMatchedMemes, meme, policy.maximumSupportingMemes);
                    for (int p = 0; p < precepts.Count; p++)
                        if (Contains(precepts[p].associatedMemeDefNames, memeId)
                            || Contains(precepts[p].requiredMemeDefNames, memeId))
                            result.Add(NewCandidate(precepts[p], meme, BeliefRelevanceSourceTokens.MemeAssociation,
                                BeliefRelevanceTierTokens.DirectIdentity, memeId, "meme|" + memeId,
                                BeliefValenceTokens.Unknown, policy));
                }
            }
            return result;
        }

        private static List<Candidate> ForcedCorrectionCandidates(
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded,
            List<BeliefPreceptFact> precepts,
            List<BeliefMemeFact> memes,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            for (int i = 0; i < policy.correlationOverrides.Count; i++)
            {
                BeliefCorrelationCorrection correction = policy.correlationOverrides[i];
                if (correction == null || correction.action != BeliefCorrectionActionTokens.Force
                    || !CorrectionMatches(correction, evidence, expanded)) continue;
                for (int p = 0; p < precepts.Count; p++)
                {
                    BeliefPreceptFact precept = precepts[p];
                    if (!CorrectionTargets(correction, precept)) continue;
                    BeliefMemeFact meme = FindMeme(memes, correction.memeDefName);
                    Candidate candidate = NewCandidate(precept, meme, BeliefRelevanceSourceTokens.Correction,
                        BeliefRelevanceTierTokens.DirectIdentity, correction.key,
                        "correction|" + correction.key, BeliefValenceTokens.Unknown, policy);
                    candidate.correctionKey = correction.key;
                    result.Add(candidate);
                }
            }
            return result;
        }

        private static List<Candidate> LexicalCandidates(
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded,
            BeliefSnapshot snapshot,
            List<BeliefPreceptFact> precepts,
            BeliefPolicySnapshot policy)
        {
            BeliefLexicalMatchResult lexical = BeliefLexicalMatcher.Match(
                evidence, snapshot, precepts, policy, expanded);
            if (lexical.winner == null) return new List<Candidate>();
            Candidate candidate = NewCandidate(lexical.winner.precept, null, lexical.winner.relevanceSource,
                lexical.winner.relevanceTier, lexical.winner.matchedIdentity,
                "lexical|" + lexical.winner.matchedIdentity, BeliefValenceTokens.Unknown, policy);
            candidate.confidence = lexical.winner.confidence;
            candidate.runnerUpGap = lexical.runnerUpGap;
            return new List<Candidate> { candidate };
        }

        private static List<Candidate> QuietFallbackCandidates(
            List<BeliefPreceptFact> precepts,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            for (int i = 0; i < precepts.Count; i++)
            {
                BeliefPreceptFact precept = precepts[i];
                if (precept.impactRank < 2 && !precept.requiredByCurrentMeme) continue;
                result.Add(NewCandidate(precept, null, BeliefRelevanceSourceTokens.QuietFallback,
                    BeliefRelevanceTierTokens.QuietFallback, precept.defName,
                    "quiet|" + StableKey(precept), BeliefValenceTokens.Unknown, policy));
            }
            return result;
        }

        private static List<Candidate> Select(
            List<Candidate> source,
            BeliefEventEvidence evidence,
            BeliefResolutionRequest request,
            BeliefPolicySnapshot policy)
        {
            List<Candidate> result = new List<Candidate>();
            if (source == null || source.Count == 0) return result;
            for (int i = 0; i < source.Count; i++)
            {
                Candidate candidate = source[i];
                candidate.finalScore = candidate.baseScore + StrengthBonus(candidate.precept, policy)
                    + SalienceBonus(evidence.narrative == null ? string.Empty : evidence.narrative.salience, policy)
                    + RoleBonus(candidate.precept, evidence.narrative == null ? string.Empty : evidence.narrative.povRole, policy)
                    - RepetitionPenalty(candidate.precept, request.recentSelectionDefNames, policy);
                candidate.selectionWeight = Math.Max(1f,
                    policy.selectionWeightBase + candidate.finalScore - candidate.baseScore);
                candidate.tie = StableHash(request.deterministicSeed + "|" + StableKey(candidate.precept)
                    + "|" + candidate.evidenceKey);
            }
            source.Sort(CompareCandidates);

            HashSet<string> seenPrecepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Candidate> collapsed = new List<Candidate>();
            for (int i = 0; i < source.Count; i++)
            {
                Candidate candidate = source[i];
                string preceptKey = StableKey(candidate.precept);
                string issueKey = IssueKey(candidate.precept);
                if (!seenPrecepts.Add(preceptKey)) continue;
                if (issueKey.Length > 0 && !seenIssues.Add(issueKey)) continue;
                collapsed.Add(candidate);
            }
            if (collapsed.Count == 0) return result;
            Candidate first = PickWeighted(collapsed, request.deterministicSeed, 0);
            result.Add(first);

            int hardCap = Math.Min(2, policy.maximumSelectedStances);
            if (hardCap < 2) return result;
            bool secondSlotIsDefault = policy.defaultSelectedStances >= 2;
            List<Candidate> secondSlot = new List<Candidate>();
            for (int i = 0; i < collapsed.Count; i++)
            {
                Candidate second = collapsed[i];
                if (ReferenceEquals(second, first)) continue;
                if (!secondSlotIsDefault && second.finalScore < policy.secondSlotMinimumScore) continue;
                if (string.IsNullOrEmpty(second.evidenceKey)
                    || Same(second.evidenceKey, first.evidenceKey)) continue;
                if (IssueKey(second.precept).Length > 0 && Same(IssueKey(second.precept), IssueKey(first.precept)))
                    continue;
                secondSlot.Add(second);
            }
            if (secondSlot.Count > 0) result.Add(PickWeighted(secondSlot, request.deterministicSeed, 1));
            return result;
        }

        private static Candidate PickWeighted(List<Candidate> candidates, int seed, int slot)
        {
            if (candidates.Count == 1) return candidates[0];
            double total = 0d;
            for (int i = 0; i < candidates.Count; i++) total += SelectionWeight(candidates[i]);
            uint hash = StableHash(seed + "|belief-slot|" + slot);
            double unit = (hash & 0x00FFFFFFu) / 16777216d;
            double roll = unit * total;
            double cumulative = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += SelectionWeight(candidates[i]);
                if (roll < cumulative) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private static double SelectionWeight(Candidate candidate)
        {
            // Every candidate is already admitted by the same strongest categorical tier. The fixed
            // baseline keeps weak-but-relevant facts possible; XML strength/repetition bonuses shape
            // deterministic diversity without allowing a lower tier into this lottery.
            return candidate.selectionWeight;
        }

        private static Candidate NewCandidate(
            BeliefPreceptFact precept,
            BeliefMemeFact meme,
            string relevanceSource,
            string relevanceTier,
            string matchedIdentity,
            string evidenceKey,
            string valence,
            BeliefPolicySnapshot policy)
        {
            return new Candidate
            {
                precept = precept,
                meme = meme,
                relevanceSource = relevanceSource,
                relevanceTier = relevanceTier,
                matchedIdentity = matchedIdentity ?? string.Empty,
                evidenceKey = evidenceKey ?? string.Empty,
                valence = BeliefValenceTokens.Normalize(valence),
                baseScore = policy.TierScore(relevanceTier)
            };
        }

        private static ResolvedBeliefStance ToResolved(Candidate value)
        {
            return new ResolvedBeliefStance
            {
                precept = value.precept,
                supportingMeme = value.meme,
                matchedIdentity = value.matchedIdentity,
                correctionKey = value.correctionKey ?? string.Empty,
                relevanceSource = value.relevanceSource,
                relevanceTier = value.relevanceTier,
                score = value.finalScore,
                confidenceScore = value.confidence,
                runnerUpGap = value.runnerUpGap,
                correlationValence = value.valence,
                independentEvidenceKey = value.evidenceKey
            };
        }

        private static List<BeliefPreceptFact> NormalizePrecepts(
            IList<BeliefPreceptFact> source,
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded,
            BeliefPolicySnapshot policy)
        {
            List<BeliefPreceptFact> result = new List<BeliefPreceptFact>();
            if (source == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                BeliefPreceptFact precept = source[i];
                if (precept == null || !precept.visible || SafeId(precept.defName, policy).Length == 0
                    || IsExcluded(precept, evidence, expanded, policy)) continue;
                if (!seen.Add(StableKey(precept))) continue;
                result.Add(precept);
            }
            result.Sort((left, right) => string.Compare(StableKey(left), StableKey(right), StringComparison.Ordinal));
            if (result.Count > policy.maximumPreceptCandidates)
                result.RemoveRange(policy.maximumPreceptCandidates, result.Count - policy.maximumPreceptCandidates);
            return result;
        }

        private static List<BeliefMemeFact> NormalizeMemes(IList<BeliefMemeFact> source, BeliefPolicySnapshot policy)
        {
            List<BeliefMemeFact> result = new List<BeliefMemeFact>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                BeliefMemeFact meme = NormalizeMeme(source[i], policy);
                if (meme != null && !meme.isStructure && FindMeme(result, meme.defName) == null) result.Add(meme);
            }
            result.Sort((left, right) => string.Compare(left.defName, right.defName, StringComparison.Ordinal));
            if (result.Count > policy.maximumMemeCandidates)
                result.RemoveRange(policy.maximumMemeCandidates, result.Count - policy.maximumMemeCandidates);
            return result;
        }

        private static BeliefMemeFact NormalizeMeme(BeliefMemeFact source, BeliefPolicySnapshot policy)
        {
            if (source == null || SafeId(source.defName, policy).Length == 0) return null;
            return source;
        }

        private static bool IsExcluded(
            BeliefPreceptFact precept,
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded,
            BeliefPolicySnapshot policy)
        {
            for (int i = 0; i < policy.correlationOverrides.Count; i++)
            {
                BeliefCorrelationCorrection correction = policy.correlationOverrides[i];
                if (correction != null && correction.action == BeliefCorrectionActionTokens.Exclude
                    && CorrectionMatches(correction, evidence, expanded) && CorrectionTargets(correction, precept))
                    return true;
            }
            return false;
        }

        private static bool CorrectionMatches(
            BeliefCorrelationCorrection correction,
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded)
        {
            NarrativeEvidence narrative = evidence.narrative;
            if (!OptionalSame(correction.sourceDomain, narrative == null ? string.Empty : narrative.sourceDomain)) return false;
            if (!OptionalSame(correction.sourceDefName, narrative == null ? string.Empty : narrative.sourceDefName)) return false;
            if (!OptionalSame(correction.groupKey, evidence.groupKey)) return false;
            if (correction.topicToken.Length > 0 && !Contains(expanded.topics, correction.topicToken)) return false;
            return true;
        }

        private static bool CorrectionTargets(BeliefCorrelationCorrection correction, BeliefPreceptFact precept)
        {
            if (correction.preceptDefName.Length > 0 && !Same(correction.preceptDefName, precept.defName)) return false;
            if (correction.issueDefName.Length > 0
                && (precept.issue == null || !Same(correction.issueDefName, precept.issue.defName))) return false;
            if (correction.memeDefName.Length > 0
                && !Contains(precept.associatedMemeDefNames, correction.memeDefName)
                && !Contains(precept.requiredMemeDefNames, correction.memeDefName)) return false;
            return correction.preceptDefName.Length > 0 || correction.issueDefName.Length > 0 || correction.memeDefName.Length > 0;
        }

        private static void AddSupportingMemes(
            BeliefStanceResolution result,
            List<Candidate> selected,
            List<BeliefMemeFact> direct,
            List<BeliefMemeFact> live,
            BeliefPolicySnapshot policy)
        {
            for (int i = 0; i < direct.Count; i++) AddUniqueMeme(result.supportingMemes, direct[i], policy.maximumSupportingMemes);
            for (int i = 0; i < selected.Count; i++)
            {
                Candidate candidate = selected[i];
                AddUniqueMeme(result.supportingMemes, candidate.meme, policy.maximumSupportingMemes);
                AddLinkedMemes(result.supportingMemes, candidate.precept == null ? null : candidate.precept.requiredMemeDefNames,
                    live, policy.maximumSupportingMemes);
                AddLinkedMemes(result.supportingMemes, candidate.precept == null ? null : candidate.precept.associatedMemeDefNames,
                    live, policy.maximumSupportingMemes);
            }
        }

        private static void AddLinkedMemes(
            List<BeliefMemeFact> target,
            IList<string> ids,
            List<BeliefMemeFact> live,
            int cap)
        {
            if (ids == null) return;
            for (int i = 0; i < ids.Count && target.Count < cap; i++) AddUniqueMeme(target, FindMeme(live, ids[i]), cap);
        }

        private static void AddUniqueMeme(List<BeliefMemeFact> target, BeliefMemeFact value, int cap)
        {
            if (value == null || target.Count >= cap) return;
            for (int i = 0; i < target.Count; i++) if (Same(target[i].defName, value.defName)) return;
            target.Add(value);
        }

        private static BeliefDeityFact SelectDeity(
            IList<BeliefDeityFact> source,
            List<BeliefMemeFact> selectedMemes,
            int seed,
            BeliefPolicySnapshot policy)
        {
            if (source == null) return null;
            int cap = Math.Min(source.Count, policy.maximumDeityCandidates);
            if (policy.includeRelatedDeity)
                for (int m = 0; m < selectedMemes.Count; m++)
                    for (int i = 0; i < cap; i++)
                        if (ValidDeity(source[i], policy) && Same(source[i].relatedMemeDefName, selectedMemes[m].defName))
                            return source[i];
            if (policy.includeKeyDeity)
                for (int i = 0; i < cap; i++) if (ValidDeity(source[i], policy) && source[i].isKeyDeity) return source[i];
            if (!policy.allowDeterministicAlternativeDeity) return null;
            BeliefDeityFact chosen = null;
            uint chosenHash = uint.MaxValue;
            for (int i = 0; i < cap; i++)
            {
                BeliefDeityFact candidate = source[i];
                if (!ValidDeity(candidate, policy)) continue;
                uint hash = StableHash(seed + "|deity|" + candidate.name);
                if (chosen == null || hash < chosenHash)
                {
                    chosen = candidate;
                    chosenHash = hash;
                }
            }
            return chosen;
        }

        private static bool ValidDeity(BeliefDeityFact value, BeliefPolicySnapshot policy)
        {
            return value != null && !string.IsNullOrWhiteSpace(value.name)
                && value.name.Trim().Length <= policy.maximumFieldCharacters;
        }

        private static float StrengthBonus(BeliefPreceptFact precept, BeliefPolicySnapshot policy)
        {
            if (precept == null) return 0f;
            float score = precept.impactRank >= 3 ? policy.highImpactBonus
                : precept.impactRank == 2 ? policy.mediumImpactBonus
                : precept.impactRank == 1 ? policy.lowImpactBonus : 0f;
            if (precept.requiredByCurrentMeme) score += policy.requiredByMemeBonus;
            return score;
        }

        private static float RoleBonus(BeliefPreceptFact precept, string povRole, BeliefPolicySnapshot policy)
        {
            if (precept == null || !precept.proselytizes) return 0f;
            for (int i = 0; i < policy.proselytizingPovRoles.Count; i++)
                if (Same(povRole, policy.proselytizingPovRoles[i])) return policy.proselytizingRoleBonus;
            return 0f;
        }

        private static float SalienceBonus(string salience, BeliefPolicySnapshot policy)
        {
            if (salience == NarrativeSalienceTokens.Terminal) return policy.terminalSalienceBonus;
            if (salience == NarrativeSalienceTokens.Major) return policy.majorSalienceBonus;
            if (salience == NarrativeSalienceTokens.Meaningful) return policy.meaningfulSalienceBonus;
            return 0f;
        }

        private static float RepetitionPenalty(
            BeliefPreceptFact precept,
            IList<string> recent,
            BeliefPolicySnapshot policy)
        {
            if (precept == null || recent == null) return 0f;
            int cap = Math.Min(recent.Count, policy.maximumRecentSelections);
            for (int i = 0; i < cap; i++) if (Same(recent[i], precept.defName)) return policy.recentSelectionPenalty;
            return 0f;
        }

        private static bool HasUsefulVisibleEvidence(BeliefEventEvidence evidence, ExpandedBeliefEvidence expanded)
        {
            return !string.IsNullOrWhiteSpace(evidence.sourcePreceptInstanceId)
                || !string.IsNullOrWhiteSpace(evidence.sourcePreceptDefName)
                || HasAny(evidence.thoughtDefNames) || HasAny(evidence.historyEventDefNames)
                || HasAny(evidence.issueDefNames) || HasAny(evidence.memeDefNames)
                || HasAny(evidence.matchFields) || expanded.topics.Count > 0 || expanded.semanticAliases.Count > 0
                || evidence.mutation != null && evidence.mutation.HasUsefulFact;
        }

        private static bool HasAny<T>(IList<T> values)
        {
            return values != null && values.Count > 0;
        }

        private static int CompareCandidates(Candidate left, Candidate right)
        {
            if (Same(left.evidenceKey, right.evidenceKey))
            {
                int byValence = ValenceRank(right.valence).CompareTo(ValenceRank(left.valence));
                if (byValence != 0) return byValence;
            }
            int byScore = right.finalScore.CompareTo(left.finalScore);
            if (byScore != 0) return byScore;
            int byTie = left.tie.CompareTo(right.tie);
            return byTie != 0 ? byTie : string.Compare(StableKey(left.precept), StableKey(right.precept), StringComparison.Ordinal);
        }

        private static int ValenceRank(string value)
        {
            if (value == BeliefValenceTokens.Negative) return 4;
            if (value == BeliefValenceTokens.Mixed) return 3;
            if (value == BeliefValenceTokens.Positive) return 2;
            if (value == BeliefValenceTokens.Neutral) return 1;
            return 0;
        }

        private static BeliefMemeFact FindMeme(IList<BeliefMemeFact> values, string defName)
        {
            if (values == null || string.IsNullOrWhiteSpace(defName)) return null;
            for (int i = 0; i < values.Count; i++)
                if (values[i] != null && Same(values[i].defName, defName)) return values[i];
            return null;
        }

        private static bool Contains(IList<string> values, string wanted)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (Same(values[i], wanted)) return true;
            return false;
        }

        private static bool OptionalSame(string wanted, string actual)
        {
            return string.IsNullOrWhiteSpace(wanted) || Same(wanted, actual);
        }

        private static string SafeId(string value, BeliefPolicySnapshot policy)
        {
            string result = (value ?? string.Empty).Trim();
            if (result.Length == 0 || result.Length > policy.maximumIdentifierCharacters) return string.Empty;
            for (int i = 0; i < result.Length; i++) if (char.IsControl(result[i])) return string.Empty;
            return result;
        }

        private static string StableKey(BeliefPreceptFact value)
        {
            if (value == null) return string.Empty;
            string instance = (value.instanceId ?? string.Empty).Trim();
            return instance.Length > 0 ? instance : (value.defName ?? string.Empty).Trim();
        }

        private static string IssueKey(BeliefPreceptFact value)
        {
            return value == null || value.issue == null ? string.Empty : (value.issue.defName ?? string.Empty).Trim();
        }

        private static bool Same(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < values.Count; i++) if (Same(values[i], value)) return;
            values.Add(value);
        }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            string text = value ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
