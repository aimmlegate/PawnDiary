// ImportantMemorySelector.cs — deterministic retrieval over a pawn's important-memory records
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §3.1). No randomness, no cooldowns, no decay, no
// minimum store size: a past record is eligible only when the current event shares a concrete
// participant or an exact subject/entity key, and the ranking is a fixed tier comparison.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Selects at most the line-cap related records for the current event.</summary>
    internal static class ImportantMemorySelector
    {
        private sealed class RankedCandidate
        {
            public ImportantMemoryRecordSnapshot record;
            public KnowledgeCandidateReport report;
        }

        /// <summary>
        /// Runs eligibility + ranking and returns the selected records newest-relevance-first,
        /// with a full per-candidate report for the dev tab (§7). Broad mood/social/body/danger
        /// domains never match by themselves — only shared participants and exact keys do.
        /// </summary>
        public static KnowledgeSelectionResult Select(
            KnowledgeQuery query,
            List<ImportantMemoryRecordSnapshot> records,
            KnowledgePolicySnapshot policy)
        {
            KnowledgeSelectionResult result = new KnowledgeSelectionResult();
            if (query == null || records == null || records.Count == 0)
            {
                return result;
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            List<RankedCandidate> eligible = new List<RankedCandidate>();
            for (int i = 0; i < records.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = records[i];
                KnowledgeCandidateReport report = new KnowledgeCandidateReport();
                if (record == null || string.IsNullOrWhiteSpace(record.recordId))
                {
                    continue;
                }

                report.recordId = record.recordId;
                report.eventKind = record.eventKind ?? string.Empty;
                result.report.Add(report);

                // Self-echo guard: the record deposited by this very event never surfaces on it.
                if (!string.IsNullOrWhiteSpace(record.sourceEventId)
                    && string.Equals(record.sourceEventId, query.eventId, StringComparison.OrdinalIgnoreCase))
                {
                    report.rejectReason = KnowledgeRejectReasons.SelfEcho;
                    continue;
                }

                report.sharedParticipant = SharesParticipant(query.participantIds, record.participants);
                report.sharedSubject = SharesSubjectKey(query.subjectKeys, record.subjectKeys);
                report.sharedTopic = !string.IsNullOrWhiteSpace(record.topicKey)
                    && ContainsIgnoreCase(query.topicKeys, record.topicKey);

                // Eligibility (§3.1): a concrete participant OR an exact subject/entity key.
                // Topic overlap alone is a ranking tier, never an eligibility door.
                if (!report.sharedParticipant && !report.sharedSubject)
                {
                    report.rejectReason = KnowledgeRejectReasons.NoOverlap;
                    continue;
                }

                eligible.Add(new RankedCandidate { record = record, report = report });
            }

            eligible.Sort(CompareCandidates);
            int cap = Math.Max(0, safePolicy.relevantPastMaxLines);
            for (int i = 0; i < eligible.Count; i++)
            {
                if (i < cap)
                {
                    eligible[i].report.selected = true;
                    result.selected.Add(eligible[i].record);
                }
                else
                {
                    eligible[i].report.rejectReason = KnowledgeRejectReasons.OverCap;
                }
            }

            return result;
        }

        /// <summary>
        /// Fixed ranking (§3.1): shared participant, then exact entity key, then topic family,
        /// then newest tick, then record ID ordinal — fully deterministic, stable ties included.
        /// </summary>
        private static int CompareCandidates(RankedCandidate left, RankedCandidate right)
        {
            int participant = right.report.sharedParticipant.CompareTo(left.report.sharedParticipant);
            if (participant != 0)
            {
                return participant;
            }

            int subject = right.report.sharedSubject.CompareTo(left.report.sharedSubject);
            if (subject != 0)
            {
                return subject;
            }

            int topic = right.report.sharedTopic.CompareTo(left.report.sharedTopic);
            if (topic != 0)
            {
                return topic;
            }

            int tick = right.record.tick.CompareTo(left.record.tick);
            return tick != 0
                ? tick
                : string.Compare(left.record.recordId, right.record.recordId, StringComparison.Ordinal);
        }

        private static bool SharesParticipant(List<string> queryIds, List<KnowledgeParticipant> participants)
        {
            if (queryIds == null || participants == null)
            {
                return false;
            }

            for (int i = 0; i < participants.Count; i++)
            {
                KnowledgeParticipant participant = participants[i];
                if (participant == null || string.IsNullOrWhiteSpace(participant.pawnId))
                {
                    continue;
                }

                if (ContainsIgnoreCase(queryIds, participant.pawnId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SharesSubjectKey(List<string> queryKeys, List<string> recordKeys)
        {
            if (queryKeys == null || recordKeys == null)
            {
                return false;
            }

            for (int i = 0; i < recordKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(recordKeys[i])
                    && ContainsIgnoreCase(queryKeys, recordKeys[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIgnoreCase(List<string> values, string target)
        {
            if (values == null || string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            string trimmed = target.Trim();
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && string.Equals(values[i].Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the retrieval query for the current event from the XML-owned extraction rules
        /// (policy.querySubjectKeyRules) plus the event's classified topic families. Shared with
        /// the capture path so query keys and record keys can never drift apart.
        /// </summary>
        public static KnowledgeQuery BuildQuery(
            string eventId,
            string ownerPawnId,
            string otherPawnId,
            int currentTick,
            string gameContext,
            string eventDefName,
            List<ImportantEventRule> rules,
            KnowledgePolicySnapshot policy)
        {
            KnowledgeQuery query = new KnowledgeQuery
            {
                eventId = eventId ?? string.Empty,
                ownerPawnId = ownerPawnId ?? string.Empty,
                currentTick = currentTick
            };

            if (!string.IsNullOrWhiteSpace(otherPawnId)
                && !string.Equals(otherPawnId, ownerPawnId, StringComparison.OrdinalIgnoreCase))
            {
                query.participantIds.Add(otherPawnId.Trim());
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            if (safePolicy.querySubjectKeyRules != null)
            {
                for (int i = 0; i < safePolicy.querySubjectKeyRules.Count; i++)
                {
                    KnowledgeSubjectKeyRule rule = safePolicy.querySubjectKeyRules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.contextKey))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(gameContext, rule.contextKey);
                    if (KnowledgeTokens.IsSentinelValue(value))
                    {
                        continue;
                    }

                    string key = ImportantEventClassifier.ComposeSubjectKey(rule.prefix, value);
                    if (!ContainsIgnoreCase(query.subjectKeys, key))
                    {
                        query.subjectKeys.Add(key);
                    }
                }
            }

            // The current event's own important-event classification supplies the topic families
            // (ranking tier 3) and any rule-declared subject keys — e.g. a new arm-loss event
            // queries "part:Arm" exactly like the record it is about to deposit.
            if (rules != null)
            {
                KnowledgeCaptureSignal probe = new KnowledgeCaptureSignal
                {
                    signal = KnowledgeTokens.SignalEvent,
                    defName = eventDefName ?? string.Empty,
                    gameContext = gameContext ?? string.Empty
                };
                ImportantEventRule match = ImportantEventClassifier.FirstMatch(probe, rules);
                if (match != null)
                {
                    if (!string.IsNullOrWhiteSpace(match.topicKey)
                        && !ContainsIgnoreCase(query.topicKeys, match.topicKey))
                    {
                        query.topicKeys.Add(match.topicKey.Trim());
                    }

                    if (match.subjectKeyRules != null)
                    {
                        for (int i = 0; i < match.subjectKeyRules.Count; i++)
                        {
                            KnowledgeSubjectKeyRule rule = match.subjectKeyRules[i];
                            if (rule == null || string.IsNullOrWhiteSpace(rule.contextKey))
                            {
                                continue;
                            }

                            string value = DiaryContextFields.Value(gameContext, rule.contextKey);
                            if (KnowledgeTokens.IsSentinelValue(value))
                            {
                                continue;
                            }

                            string key = ImportantEventClassifier.ComposeSubjectKey(rule.prefix, value);
                            if (!ContainsIgnoreCase(query.subjectKeys, key))
                            {
                                query.subjectKeys.Add(key);
                            }
                        }
                    }

                    if (match.constantSubjectKeys != null)
                    {
                        for (int i = 0; i < match.constantSubjectKeys.Count; i++)
                        {
                            string constant = match.constantSubjectKeys[i];
                            if (!string.IsNullOrWhiteSpace(constant)
                                && !ContainsIgnoreCase(query.subjectKeys, constant.Trim()))
                            {
                                query.subjectKeys.Add(constant.Trim());
                            }
                        }
                    }
                }
            }

            return query;
        }
    }
}
