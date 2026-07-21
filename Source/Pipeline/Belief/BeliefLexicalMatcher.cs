// Conservative, deterministic text relevance matching for detached belief facts. This is a final
// fallback after structural identity/correlation has failed; it never infers moral polarity and never
// sees the assembled prompt. Every input is bounded again here because adapters may be mod-authored.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>One candidate's lexical diagnostics. Scores establish relevance only.</summary>
    internal sealed class BeliefLexicalMatch
    {
        public BeliefPreceptFact precept;
        public float confidence;
        public string relevanceSource = string.Empty;
        public string relevanceTier = string.Empty;
        public string matchedIdentity = string.Empty;
        public int distinctiveTokenMatches;
        public int fuzzyTokenMatches;
        public bool phraseMatched;
    }

    /// <summary>Accepted winner plus bounded runner-up diagnostics.</summary>
    internal sealed class BeliefLexicalMatchResult
    {
        public BeliefLexicalMatch winner;
        public float runnerUpConfidence;
        public float runnerUpGap;
        public bool rejectedAsAmbiguous;
        public bool rejectedBelowConfidence;
    }

    /// <summary>Pure guarded lexical fallback with dynamic per-snapshot common-token suppression.</summary>
    internal static class BeliefLexicalMatcher
    {
        private sealed class FieldValue
        {
            public string kind;
            public string phrase;
            public float weight;
            public List<string> tokens;
        }

        private sealed class Document
        {
            public readonly List<FieldValue> fields = new List<FieldValue>();
            public readonly Dictionary<string, float> tokenWeights =
                new Dictionary<string, float>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> tokenKinds =
                new Dictionary<string, string>(StringComparer.Ordinal);
            // Fuzzy matching revisits the same normalized tokens across many candidate comparisons.
            // Cache each token's bigrams once per bounded document instead of allocating per pair.
            public readonly Dictionary<string, Dictionary<string, int>> bigramCounts =
                new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        }

        private sealed class Scored
        {
            public BeliefLexicalMatch match;
            public string issueKey;
            public string stableKey;
        }

        /// <summary>
        /// Returns one winner only when it is independently meaningful, above the XML threshold, and
        /// separated from the next different issue. Close or generic matches deliberately return empty.
        /// </summary>
        public static BeliefLexicalMatchResult Match(
            BeliefEventEvidence evidence,
            BeliefSnapshot snapshot,
            IList<BeliefPreceptFact> candidates,
            BeliefPolicySnapshot policy)
        {
            return Match(evidence, snapshot, candidates, policy, null);
        }

        /// <summary>Reuses resolver-owned evidence expansion while keeping the standalone API intact.</summary>
        internal static BeliefLexicalMatchResult Match(
            BeliefEventEvidence evidence,
            BeliefSnapshot snapshot,
            IList<BeliefPreceptFact> candidates,
            BeliefPolicySnapshot policy,
            ExpandedBeliefEvidence expandedEvidence)
        {
            BeliefLexicalMatchResult result = new BeliefLexicalMatchResult();
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (evidence == null || snapshot == null || candidates == null || !effective.enabled)
                return result;

            List<BeliefPreceptFact> bounded = BoundedCandidates(candidates, effective);
            if (bounded.Count == 0) return result;

            HashSet<string> exclusions = BuildExclusions(snapshot, effective);
            ExpandedBeliefEvidence expanded = expandedEvidence
                ?? BeliefEventEvidencePolicy.Expand(evidence, effective);
            Document eventDocument = BuildEventDocument(evidence, expanded, effective, exclusions);
            if (eventDocument.fields.Count == 0) return result;

            List<Document> candidateDocuments = new List<Document>();
            Dictionary<string, int> documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bounded.Count; i++)
            {
                Document document = BuildBeliefDocument(bounded[i], snapshot, effective, exclusions);
                candidateDocuments.Add(document);
                foreach (string token in document.tokenWeights.Keys)
                {
                    int count;
                    documentFrequency.TryGetValue(token, out count);
                    documentFrequency[token] = count + 1;
                }
            }

            HashSet<string> common = CommonTokens(documentFrequency, bounded.Count, effective);
            List<Scored> scored = new List<Scored>();
            for (int i = 0; i < bounded.Count; i++)
            {
                BeliefLexicalMatch match = Score(
                    eventDocument, candidateDocuments[i], bounded[i], documentFrequency, common, effective);
                if (match == null) continue;
                scored.Add(new Scored
                {
                    match = match,
                    issueKey = IssueKey(bounded[i]),
                    stableKey = StableKey(bounded[i])
                });
            }

            scored.Sort(CompareScored);
            scored = CollapseSameIssue(scored);
            if (scored.Count == 0) return result;

            Scored top = scored[0];
            result.runnerUpConfidence = scored.Count > 1 ? scored[1].match.confidence : 0f;
            result.runnerUpGap = top.match.confidence - result.runnerUpConfidence;
            if (top.match.confidence < effective.minimumLexicalConfidence)
            {
                result.rejectedBelowConfidence = true;
                return result;
            }

            float requiredMargin = top.match.fuzzyTokenMatches > 0 && top.match.distinctiveTokenMatches == 0
                ? effective.fuzzyRunnerUpMargin
                : effective.lexicalRunnerUpMargin;
            if (scored.Count > 1 && result.runnerUpGap < requiredMargin)
            {
                result.rejectedAsAmbiguous = true;
                return result;
            }

            result.winner = top.match;
            return result;
        }

        /// <summary>Bounded normalization shared with tests and the formatter's sanitation checks.</summary>
        public static string Normalize(string value, int maximumCharacters, int maximumTokens)
        {
            if (string.IsNullOrEmpty(value) || maximumCharacters <= 0 || maximumTokens <= 0)
                return string.Empty;

            string bounded = value.Length <= maximumCharacters ? value : value.Substring(0, maximumCharacters);
            try
            {
                bounded = bounded.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(bounded.Length + 8);
            bool insideTag = false;
            bool previousWasLowerOrDigit = false;
            for (int i = 0; i < bounded.Length; i++)
            {
                char current = bounded[i];
                if (current == '<')
                {
                    insideTag = true;
                    AppendSpace(builder);
                    previousWasLowerOrDigit = false;
                    continue;
                }
                if (insideTag)
                {
                    if (current == '>') insideTag = false;
                    continue;
                }
                if (char.IsControl(current) || char.IsWhiteSpace(current) || current == '_')
                {
                    AppendSpace(builder);
                    previousWasLowerOrDigit = false;
                    continue;
                }
                UnicodeCategory category = char.GetUnicodeCategory(current);
                bool letterOrDigit = char.IsLetterOrDigit(current)
                    || category == UnicodeCategory.NonSpacingMark
                    || category == UnicodeCategory.SpacingCombiningMark;
                if (!letterOrDigit)
                {
                    AppendSpace(builder);
                    previousWasLowerOrDigit = false;
                    continue;
                }
                if (char.IsUpper(current) && previousWasLowerOrDigit) AppendSpace(builder);
                builder.Append(char.ToLowerInvariant(current));
                previousWasLowerOrDigit = char.IsLower(current) || char.IsDigit(current);
            }

            string[] raw = builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int count = Math.Min(maximumTokens, raw.Length);
            if (count == 0) return string.Empty;
            builder.Clear();
            for (int i = 0; i < count; i++)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(raw[i]);
            }
            return builder.ToString();
        }

        private static List<BeliefPreceptFact> BoundedCandidates(
            IList<BeliefPreceptFact> candidates,
            BeliefPolicySnapshot policy)
        {
            List<BeliefPreceptFact> values = new List<BeliefPreceptFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                BeliefPreceptFact candidate = candidates[i];
                if (candidate == null || !candidate.visible || SafeId(candidate.defName, policy).Length == 0)
                    continue;
                if (!seen.Add(StableKey(candidate))) continue;
                values.Add(candidate);
            }
            values.Sort((left, right) => string.Compare(StableKey(left), StableKey(right), StringComparison.Ordinal));
            if (values.Count > policy.maximumPreceptCandidates)
                values.RemoveRange(policy.maximumPreceptCandidates, values.Count - policy.maximumPreceptCandidates);
            return values;
        }

        private static HashSet<string> BuildExclusions(BeliefSnapshot snapshot, BeliefPolicySnapshot policy)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < policy.lexicalExclusions.Count; i++)
                AddNormalizedTokens(result, policy.lexicalExclusions[i], policy);
            AddNormalizedTokens(result, snapshot.pawnId, policy);
            AddNormalizedTokens(result, snapshot.ideologyId, policy);
            AddNormalizedTokens(result, snapshot.ideologyName, policy);
            return result;
        }

        private static Document BuildEventDocument(
            BeliefEventEvidence evidence,
            ExpandedBeliefEvidence expanded,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            Document result = new Document();
            NarrativeEvidence narrative = evidence.narrative;
            if (narrative != null)
            {
                AddField(result, "def_name", narrative.sourceDefName, policy.EventFieldWeight("def_name"), policy, exclusions);
                AddField(result, "domain", narrative.sourceDomain, policy.EventFieldWeight("domain"), policy, exclusions);
                AddField(result, "subject_label", narrative.subjectLabel, policy.EventFieldWeight("subject_label"), policy, exclusions);
            }
            AddField(result, "group", evidence.groupKey, policy.EventFieldWeight("group"), policy, exclusions);
            AddIdentifiers(result, evidence.thoughtDefNames, "correlation", policy.EventFieldWeight("correlation"), policy, exclusions);
            AddIdentifiers(result, evidence.historyEventDefNames, "correlation", policy.EventFieldWeight("correlation"), policy, exclusions);
            if (expanded != null)
            {
                for (int i = 0; i < expanded.topics.Count; i++)
                    AddSemanticTopic(result, expanded.topics[i], policy, exclusions);
                for (int i = 0; i < expanded.semanticAliases.Count; i++)
                    AddSemanticTopic(result, expanded.semanticAliases[i], policy, exclusions);
            }
            if (evidence.matchFields != null)
            {
                int cap = Math.Min(evidence.matchFields.Count, 32);
                for (int i = 0; i < cap; i++)
                {
                    BeliefEvidenceTextFact field = evidence.matchFields[i];
                    if (field == null) continue;
                    string kind = NormalizeFieldKind(field.field);
                    AddField(result, kind, field.value, policy.EventFieldWeight(kind), policy, exclusions);
                }
            }
            return result;
        }

        private static Document BuildBeliefDocument(
            BeliefPreceptFact precept,
            BeliefSnapshot snapshot,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            Document result = new Document();
            if (precept.correlations != null)
            {
                int cap = Math.Min(precept.correlations.Count, 64);
                for (int i = 0; i < cap; i++)
                {
                    BeliefCorrelationFact correlation = precept.correlations[i];
                    if (correlation == null) continue;
                    float weight = policy.BeliefFieldWeight("correlation");
                    AddField(result, "correlation", correlation.defName, weight, policy, exclusions);
                    AddField(result, "correlation", correlation.label, weight, policy, exclusions);
                    AddField(result, "correlation", correlation.description, weight, policy, exclusions);
                }
            }
            if (precept.issue != null)
            {
                float weight = policy.BeliefFieldWeight("issue");
                AddField(result, "issue", precept.issue.defName, weight, policy, exclusions);
                AddField(result, "issue", precept.issue.label, weight, policy, exclusions);
                AddField(result, "issue", precept.issue.description, weight, policy, exclusions);
            }
            float preceptWeight = policy.BeliefFieldWeight("precept");
            AddField(result, "precept", precept.defName, preceptWeight, policy, exclusions);
            AddField(result, "precept", precept.displayLabel, preceptWeight, policy, exclusions);
            AddField(result, "precept", precept.description, preceptWeight, policy, exclusions);
            AddMemeFields(result, precept, snapshot, policy, exclusions);
            return result;
        }

        private static void AddMemeFields(
            Document target,
            BeliefPreceptFact precept,
            BeliefSnapshot snapshot,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            if (snapshot.memes == null) return;
            HashSet<string> linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIds(linked, precept.associatedMemeDefNames);
            AddIds(linked, precept.requiredMemeDefNames);
            float weight = policy.BeliefFieldWeight("meme");
            int cap = Math.Min(snapshot.memes.Count, policy.maximumMemeCandidates);
            for (int i = 0; i < cap; i++)
            {
                BeliefMemeFact meme = snapshot.memes[i];
                if (meme == null || !linked.Contains(meme.defName ?? string.Empty)) continue;
                AddField(target, "meme", meme.defName, weight, policy, exclusions);
                AddField(target, "meme", meme.label, weight, policy, exclusions);
                AddField(target, "meme", meme.description, weight, policy, exclusions);
            }
        }

        private static void AddSemanticTopic(
            Document target,
            string topic,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            float weight = policy.EventFieldWeight("semantic_alias");
            AddField(target, "semantic_alias", topic, weight, policy, exclusions);
            for (int i = 0; i < policy.semanticAliases.Count; i++)
            {
                BeliefSemanticAlias group = policy.semanticAliases[i];
                if (!Same(group.topicToken, topic)) continue;
                for (int j = 0; j < group.aliases.Count; j++)
                    AddField(target, "semantic_alias", group.aliases[j], weight, policy, exclusions);
            }
        }

        private static void AddIdentifiers(
            Document target,
            IList<string> values,
            string kind,
            float weight,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            if (values == null) return;
            int cap = Math.Min(values.Count, 32);
            for (int i = 0; i < cap; i++) AddField(target, kind, values[i], weight, policy, exclusions);
        }

        private static void AddField(
            Document target,
            string kind,
            string value,
            float weight,
            BeliefPolicySnapshot policy,
            HashSet<string> exclusions)
        {
            if (weight <= 0f || string.IsNullOrWhiteSpace(value)
                || target.fields.Count >= policy.maximumLexicalFieldsPerDocument) return;
            string phrase = Normalize(value, policy.maximumFieldCharacters, policy.maximumNormalizedTokensPerField);
            if (phrase.Length == 0) return;
            string[] rawTokens = phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> tokens = new List<string>();
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i];
                if (token.Length < 2 || exclusions.Contains(token)) continue;
                if (!target.tokenWeights.ContainsKey(token)
                    && target.tokenWeights.Count >= policy.maximumLexicalTokensPerDocument) continue;
                tokens.Add(token);
                float existing;
                if (!target.tokenWeights.TryGetValue(token, out existing) || weight > existing)
                {
                    target.tokenWeights[token] = weight;
                    target.tokenKinds[token] = kind;
                }
            }
            if (tokens.Count == 0) return;
            target.fields.Add(new FieldValue { kind = kind, phrase = string.Join(" ", tokens), weight = weight, tokens = tokens });
        }

        private static BeliefLexicalMatch Score(
            Document eventDocument,
            Document beliefDocument,
            BeliefPreceptFact precept,
            Dictionary<string, int> documentFrequency,
            HashSet<string> common,
            BeliefPolicySnapshot policy)
        {
            float score = 0f;
            bool phraseMatched = false;
            int exactMatches = 0;
            int fuzzyMatches = 0;
            string bestKind = "precept";
            float bestContribution = 0f;
            string matchedIdentity = string.Empty;

            for (int i = 0; i < eventDocument.fields.Count; i++)
            {
                FieldValue eventField = eventDocument.fields[i];
                if (eventField.tokens.Count < 2) continue;
                for (int j = 0; j < beliefDocument.fields.Count; j++)
                {
                    FieldValue beliefField = beliefDocument.fields[j];
                    if (!string.Equals(eventField.phrase, beliefField.phrase, StringComparison.Ordinal)
                        || !HasDistinctiveToken(eventField.tokens, common)) continue;
                    float contribution = policy.phraseMatchScore * GeometricWeight(eventField.weight, beliefField.weight);
                    score += contribution;
                    phraseMatched = true;
                    if (contribution > bestContribution)
                    {
                        bestContribution = contribution;
                        bestKind = beliefField.kind;
                        matchedIdentity = beliefField.phrase;
                    }
                    break;
                }
            }

            foreach (KeyValuePair<string, float> pair in eventDocument.tokenWeights)
            {
                string token = pair.Key;
                if (common.Contains(token)) continue;
                float beliefWeight;
                if (!beliefDocument.tokenWeights.TryGetValue(token, out beliefWeight)) continue;
                float contribution = policy.tokenMatchScore * GeometricWeight(pair.Value, beliefWeight);
                int frequency;
                documentFrequency.TryGetValue(token, out frequency);
                if (frequency == 1 && token.Length >= policy.uniqueTokenMinimumCharacters)
                    contribution += policy.uniqueTokenBonus;
                score += contribution;
                exactMatches++;
                if (contribution > bestContribution)
                {
                    bestContribution = contribution;
                    bestKind = beliefDocument.tokenKinds[token];
                    matchedIdentity = token;
                }
            }

            if (!phraseMatched && exactMatches == 0)
            {
                HashSet<string> usedBeliefTokens = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, float> eventToken in eventDocument.tokenWeights)
                {
                    if (eventToken.Key.Length < policy.fuzzyTokenMinimumCharacters || common.Contains(eventToken.Key))
                        continue;
                    string bestToken = string.Empty;
                    float bestSimilarity = 0f;
                    foreach (KeyValuePair<string, float> beliefToken in beliefDocument.tokenWeights)
                    {
                        if (beliefToken.Key.Length < policy.fuzzyTokenMinimumCharacters
                            || common.Contains(beliefToken.Key) || usedBeliefTokens.Contains(beliefToken.Key)) continue;
                        float similarity = DiceCoefficient(
                            eventToken.Key, eventDocument, beliefToken.Key, beliefDocument);
                        if (similarity > bestSimilarity)
                        {
                            bestSimilarity = similarity;
                            bestToken = beliefToken.Key;
                        }
                    }
                    if (bestSimilarity < policy.fuzzySimilarityMinimum || bestToken.Length == 0) continue;
                    usedBeliefTokens.Add(bestToken);
                    float contribution = policy.fuzzyMatchScore * bestSimilarity
                        * GeometricWeight(eventToken.Value, beliefDocument.tokenWeights[bestToken]);
                    score += contribution;
                    fuzzyMatches++;
                    if (contribution > bestContribution)
                    {
                        bestContribution = contribution;
                        bestKind = beliefDocument.tokenKinds[bestToken];
                        matchedIdentity = eventToken.Key + "~" + bestToken;
                    }
                }
            }

            bool oneUniqueLongToken = false;
            if (exactMatches == 1)
            {
                foreach (string token in eventDocument.tokenWeights.Keys)
                {
                    int frequency;
                    if (beliefDocument.tokenWeights.ContainsKey(token)
                        && documentFrequency.TryGetValue(token, out frequency)
                        && frequency == 1 && token.Length >= policy.uniqueTokenMinimumCharacters)
                    {
                        oneUniqueLongToken = true;
                        break;
                    }
                }
            }
            bool qualifies = phraseMatched || exactMatches >= policy.minimumDistinctiveTokenMatches
                || oneUniqueLongToken || fuzzyMatches >= policy.fuzzyMinimumDistinctiveMatches;
            if (!qualifies) return null;

            string tier = bestKind == "correlation"
                ? BeliefRelevanceTierTokens.CorrelationText
                : bestKind == "issue" ? BeliefRelevanceTierTokens.IssueText : BeliefRelevanceTierTokens.GeneralText;
            string source = phraseMatched
                ? BeliefRelevanceSourceTokens.LexicalPhrase
                : exactMatches > 0 ? BeliefRelevanceSourceTokens.LexicalTokens : BeliefRelevanceSourceTokens.LexicalFuzzy;
            return new BeliefLexicalMatch
            {
                precept = precept,
                confidence = score,
                relevanceSource = source,
                relevanceTier = tier,
                matchedIdentity = matchedIdentity,
                distinctiveTokenMatches = exactMatches,
                fuzzyTokenMatches = fuzzyMatches,
                phraseMatched = phraseMatched
            };
        }

        private static HashSet<string> CommonTokens(
            Dictionary<string, int> frequency,
            int documentCount,
            BeliefPolicySnapshot policy)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (documentCount <= 0) return result;
            foreach (KeyValuePair<string, int> pair in frequency)
            {
                float fraction = pair.Value / (float)documentCount;
                if (pair.Value >= policy.commonTokenMinimumDocuments
                    && fraction >= policy.commonTokenDocumentFraction)
                    result.Add(pair.Key);
            }
            return result;
        }

        private static bool HasDistinctiveToken(List<string> tokens, HashSet<string> common)
        {
            for (int i = 0; i < tokens.Count; i++) if (!common.Contains(tokens[i])) return true;
            return false;
        }

        private static List<Scored> CollapseSameIssue(List<Scored> source)
        {
            List<Scored> result = new List<Scored>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string key = source[i].issueKey.Length > 0 ? "issue|" + source[i].issueKey : "precept|" + source[i].stableKey;
                if (seen.Add(key)) result.Add(source[i]);
            }
            return result;
        }

        private static int CompareScored(Scored left, Scored right)
        {
            int byScore = right.match.confidence.CompareTo(left.match.confidence);
            return byScore != 0 ? byScore : string.Compare(left.stableKey, right.stableKey, StringComparison.Ordinal);
        }

        private static float DiceCoefficient(
            string left,
            Document leftDocument,
            string right,
            Document rightDocument)
        {
            if (left == right) return 1f;
            if (left.Length < 2 || right.Length < 2) return 0f;
            Dictionary<string, int> leftPairs = BigramsFor(leftDocument, left);
            Dictionary<string, int> rightPairs = BigramsFor(rightDocument, right);
            int intersection = 0;
            foreach (KeyValuePair<string, int> pair in leftPairs)
            {
                int other;
                if (rightPairs.TryGetValue(pair.Key, out other)) intersection += Math.Min(pair.Value, other);
            }
            return (2f * intersection) / (left.Length - 1 + right.Length - 1);
        }

        private static Dictionary<string, int> BigramsFor(Document document, string value)
        {
            Dictionary<string, int> result;
            if (document.bigramCounts.TryGetValue(value, out result)) return result;
            result = Bigrams(value);
            document.bigramCounts[value] = result;
            return result;
        }

        private static Dictionary<string, int> Bigrams(string value)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < value.Length - 1; i++)
            {
                string pair = value.Substring(i, 2);
                int count;
                result.TryGetValue(pair, out count);
                result[pair] = count + 1;
            }
            return result;
        }

        private static void AddNormalizedTokens(HashSet<string> target, string value, BeliefPolicySnapshot policy)
        {
            string normalized = Normalize(value, policy.maximumFieldCharacters, policy.maximumNormalizedTokensPerField);
            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++) target.Add(tokens[i]);
        }

        private static void AddIds(HashSet<string> target, IList<string> values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Count; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) target.Add(values[i].Trim());
        }

        private static string NormalizeFieldKind(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "event_label" || normalized == "subject_label" || normalized == "object_label"
                || normalized == "ingredient_label" || normalized == "body_part_label" || normalized == "hediff_label"
                || normalized == "weapon_label" || normalized == "ritual_label" || normalized == "condition_label"
                || normalized == "correlation") return normalized;
            return "event_label";
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

        private static float GeometricWeight(float left, float right)
        {
            return (float)Math.Sqrt(Math.Max(0f, left) * Math.Max(0f, right));
        }

        private static void AppendSpace(StringBuilder builder)
        {
            if (builder.Length > 0 && builder[builder.Length - 1] != ' ') builder.Append(' ');
        }
    }
}
