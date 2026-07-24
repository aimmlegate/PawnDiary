// ImportantEventClassifier.cs — pure classification of capture signals against the XML-owned
// important-event allowlist (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2). One signal either matches
// exactly one rule (first match in ascending order within its capture channel) or produces
// nothing; a match yields one detached record draft per resolved owner.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings
// references here — the impure listeners build KnowledgeCaptureSignal snapshots and persist the
// returned drafts.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Matches capture signals to important-event rules and drafts per-owner records.</summary>
    internal static class ImportantEventClassifier
    {
        /// <summary>
        /// Classifies one signal. Returns an empty list when no enabled rule of the signal's
        /// channel matches — the closed-list design (§2.1): everything not allowlisted is ignored.
        /// </summary>
        public static List<ImportantMemoryDraft> Classify(
            KnowledgeCaptureSignal signal,
            List<ImportantEventRule> rules,
            KnowledgePolicySnapshot policy)
        {
            List<ImportantMemoryDraft> drafts = new List<ImportantMemoryDraft>();
            if (signal == null || rules == null)
            {
                return drafts;
            }

            ImportantEventRule rule = FirstMatch(signal, rules);
            if (rule == null)
            {
                return drafts;
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            List<string> ownerIds = ResolveOwners(signal, rule);
            for (int i = 0; i < ownerIds.Count; i++)
            {
                string ownerId = ownerIds[i];
                if (string.IsNullOrWhiteSpace(ownerId))
                {
                    continue;
                }

                drafts.Add(BuildDraft(signal, rule, ownerId, safePolicy));
            }

            return drafts;
        }

        /// <summary>First enabled rule of the signal's channel that matches, in ascending
        /// <c>order</c> then defName order — mirrors the interaction-group first-match-wins rule.</summary>
        public static ImportantEventRule FirstMatch(
            KnowledgeCaptureSignal signal, List<ImportantEventRule> rules)
        {
            ImportantEventRule best = null;
            for (int i = 0; i < rules.Count; i++)
            {
                ImportantEventRule rule = rules[i];
                if (rule == null || !rule.enabled
                    || !string.Equals(rule.signal, signal.signal, StringComparison.OrdinalIgnoreCase)
                    || !Matches(signal, rule))
                {
                    continue;
                }

                if (best == null || Compare(rule, best) < 0)
                {
                    best = rule;
                }
            }

            return best;
        }

        private static int Compare(ImportantEventRule left, ImportantEventRule right)
        {
            int order = left.order.CompareTo(right.order);
            return order != 0
                ? order
                : string.Compare(left.defName, right.defName, StringComparison.Ordinal);
        }

        private static bool Matches(KnowledgeCaptureSignal signal, ImportantEventRule rule)
        {
            string defName = signal.defName ?? string.Empty;
            // A row with no name matchers is context-gated only (e.g. "any hediff event with
            // part_kind=missingpart") — its requireContext rows below are the whole gate.
            bool nameMatched = !HasAnyNameMatcher(rule);
            if (rule.matchDefNames != null)
            {
                for (int i = 0; i < rule.matchDefNames.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchDefNames[i])
                        && string.Equals(defName, rule.matchDefNames[i].Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        nameMatched = true;
                        break;
                    }
                }
            }

            if (!nameMatched && rule.matchSuffixes != null)
            {
                string lower = defName.ToLowerInvariant();
                for (int i = 0; i < rule.matchSuffixes.Count; i++)
                {
                    string suffix = rule.matchSuffixes[i];
                    if (!string.IsNullOrWhiteSpace(suffix)
                        && lower.EndsWith(suffix.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        nameMatched = true;
                        break;
                    }
                }
            }

            if (!nameMatched)
            {
                return false;
            }

            // Extra context gates: every row must hold. "key=value" is exact; "key=" (or a bare
            // key) means "present with a meaningful, non-sentinel value".
            if (rule.requireContext != null)
            {
                for (int i = 0; i < rule.requireContext.Count; i++)
                {
                    string row = rule.requireContext[i];
                    if (string.IsNullOrWhiteSpace(row))
                    {
                        continue;
                    }

                    string trimmed = row.Trim();
                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex > 0 && equalsIndex < trimmed.Length - 1)
                    {
                        if (!DiaryContextFields.FieldEquals(signal.gameContext,
                            trimmed.Substring(0, equalsIndex), trimmed.Substring(equalsIndex + 1)))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        string key = equalsIndex > 0 ? trimmed.Substring(0, equalsIndex) : trimmed;
                        string value = DiaryContextFields.Value(signal.gameContext, key);
                        if (KnowledgeTokens.IsSentinelValue(value))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool HasAnyNameMatcher(ImportantEventRule rule)
        {
            if (rule.matchDefNames != null)
            {
                for (int i = 0; i < rule.matchDefNames.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchDefNames[i]))
                    {
                        return true;
                    }
                }
            }

            if (rule.matchSuffixes != null)
            {
                for (int i = 0; i < rule.matchSuffixes.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(rule.matchSuffixes[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> ResolveOwners(KnowledgeCaptureSignal signal, ImportantEventRule rule)
        {
            List<string> owners = new List<string>();
            string token = rule.owners ?? string.Empty;
            if (string.Equals(token, KnowledgeTokens.OwnersProvided, StringComparison.OrdinalIgnoreCase))
            {
                owners.Add(signal.providedOwnerPawnId);
                return owners;
            }

            bool initiator = string.Equals(token, KnowledgeTokens.OwnersInitiator, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, KnowledgeTokens.OwnersBoth, StringComparison.OrdinalIgnoreCase);
            bool recipient = string.Equals(token, KnowledgeTokens.OwnersRecipient, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, KnowledgeTokens.OwnersBoth, StringComparison.OrdinalIgnoreCase);
            if (initiator)
            {
                owners.Add(signal.initiatorPawnId);
            }

            if (recipient)
            {
                owners.Add(signal.recipientPawnId);
            }

            return owners;
        }

        private static ImportantMemoryDraft BuildDraft(
            KnowledgeCaptureSignal signal,
            ImportantEventRule rule,
            string ownerId,
            KnowledgePolicySnapshot policy)
        {
            ImportantMemoryRecordSnapshot record = new ImportantMemoryRecordSnapshot
            {
                ownerPawnId = ownerId,
                sourceEventId = signal.sourceEventId ?? string.Empty,
                eventKind = rule.eventKind ?? string.Empty,
                topicKey = rule.topicKey ?? string.Empty,
                tick = signal.tick,
                dateLabel = (signal.dateLabel ?? string.Empty).Trim()
            };

            AddCounterpartParticipant(record, signal, ownerId);
            if (signal.extraParticipants != null)
            {
                for (int i = 0; i < signal.extraParticipants.Count; i++)
                {
                    KnowledgeParticipant extra = signal.extraParticipants[i];
                    if (extra != null && !string.IsNullOrWhiteSpace(extra.pawnId)
                        && !string.Equals(extra.pawnId, ownerId, StringComparison.OrdinalIgnoreCase))
                    {
                        record.participants.Add(new KnowledgeParticipant
                        {
                            pawnId = extra.pawnId.Trim(),
                            name = (extra.name ?? string.Empty).Trim()
                        });
                    }
                }
            }

            if (rule.subjectKeyRules != null)
            {
                for (int i = 0; i < rule.subjectKeyRules.Count; i++)
                {
                    KnowledgeSubjectKeyRule keyRule = rule.subjectKeyRules[i];
                    if (keyRule == null || string.IsNullOrWhiteSpace(keyRule.contextKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(signal.gameContext, keyRule.contextKey);
                    if (KnowledgeTokens.IsSentinelValue(value))
                    {
                        continue;
                    }

                    string key = ComposeSubjectKey(keyRule.prefix, value);
                    if (!ContainsOrdinalIgnoreCase(record.subjectKeys, key))
                    {
                        record.subjectKeys.Add(key);
                    }
                }
            }

            if (rule.constantSubjectKeys != null)
            {
                for (int i = 0; i < rule.constantSubjectKeys.Count; i++)
                {
                    string constant = rule.constantSubjectKeys[i];
                    if (!string.IsNullOrWhiteSpace(constant)
                        && !ContainsOrdinalIgnoreCase(record.subjectKeys, constant.Trim()))
                    {
                        record.subjectKeys.Add(constant.Trim());
                    }
                }
            }

            if (rule.factKeys != null)
            {
                for (int i = 0; i < rule.factKeys.Count; i++)
                {
                    string factKey = rule.factKeys[i];
                    if (string.IsNullOrWhiteSpace(factKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(signal.gameContext, factKey.Trim());
                    if (!KnowledgeTokens.IsSentinelValue(value))
                    {
                        record.facts.Add(new KnowledgeFact { key = factKey.Trim(), value = value });
                    }
                }
            }

            // Deterministic identity (§2.2): same owner + kind + primary subject + tick collapses
            // to one record no matter how many listeners observed the same gameplay change.
            string primarySubject = record.subjectKeys.Count > 0
                ? record.subjectKeys[0]
                : (record.participants.Count > 0 ? record.participants[0].pawnId : string.Empty);
            record.dedupKey = ownerId + "|" + record.eventKind + "|" + primarySubject + "|" + signal.tick;
            record.recordId = record.dedupKey;

            record.fallbackSummary = ImportantMemoryLineRenderer.Render(
                record, rule.lineTemplate, policy.fallbackSummaryMaxChars);
            return new ImportantMemoryDraft
            {
                ownerPawnId = ownerId,
                matchedRuleDefName = rule.defName ?? string.Empty,
                record = record
            };
        }

        /// <summary>The other diary-event POV becomes the record's first participant.</summary>
        private static void AddCounterpartParticipant(
            ImportantMemoryRecordSnapshot record, KnowledgeCaptureSignal signal, string ownerId)
        {
            string otherId;
            string otherName;
            if (string.Equals(ownerId, signal.initiatorPawnId, StringComparison.OrdinalIgnoreCase))
            {
                otherId = signal.recipientPawnId;
                otherName = signal.recipientName;
            }
            else
            {
                otherId = signal.initiatorPawnId;
                otherName = signal.initiatorName;
            }

            if (!string.IsNullOrWhiteSpace(otherId)
                && !string.Equals(otherId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                record.participants.Add(new KnowledgeParticipant
                {
                    pawnId = otherId.Trim(),
                    name = (otherName ?? string.Empty).Trim()
                });
            }
        }

        /// <summary>"prefix:value" with a blank prefix collapsing to just the value.</summary>
        public static string ComposeSubjectKey(string prefix, string value)
        {
            string cleanValue = (value ?? string.Empty).Trim();
            string cleanPrefix = (prefix ?? string.Empty).Trim();
            return cleanPrefix.Length == 0 ? cleanValue : cleanPrefix + ":" + cleanValue;
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
