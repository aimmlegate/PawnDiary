// Pure first-stage editorial policy for finished RimTalk reply chains. The impure tracker collects
// live game facts, then this file reduces them to deterministic local signals and a ranked score.
// It contains no RimWorld, RimTalk, settings, Def, or translation dependency.
//
// Terms nominate candidates only. Even a high local score never writes a diary entry by itself when
// semantic assessment is enabled; it only earns a place in the bounded assessment queue.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Plain facts describing one completed conversation candidate.</summary>
    public sealed class ConversationCandidateFacts
    {
        public string ConversationId;
        public string PairKey;
        public int FirstTick;
        public int LastTick;
        public int LineCount;
        public int DistinctSpeakerCount;
        public int SpeakerAlternations;
        public int ChargedLineCount;
        public bool HasUserTalk;
        public bool HasEventTalk;
        public int KeywordScore;
        public float RecentEventTextOverlap;

        // Additional plain facts needed for the hard gates and explanations. They stay here instead
        // of leaking queue/settings objects into the pure evaluator.
        public int ParticipantCount;
        public bool HasUsableText;
        public bool HasReciprocalChargedReply;
        public bool IsAnnouncementOnly;
        public bool AlreadyQueuedOrAssessed;
        public bool DailyRecordingDisabled;
        public bool SamePairAlreadyQueued;
        public int KeywordCategoryCount;
    }

    /// <summary>Pure local nomination result plus a stable developer-facing explanation.</summary>
    public sealed class ConversationCandidateDecision
    {
        public bool EligibleForAssessment;
        public int Score;
        public string Reason;
    }

    /// <summary>XML-backed weights and boundaries copied into a plain object by the runtime adapter.</summary>
    public sealed class ConversationCandidatePolicyOptions
    {
        public int CandidateScoreThreshold;
        public int ChargedSocialWeight;
        public int ReciprocalChargedWeight;
        public int UserTalkWeight;
        public int AlternationWeight;
        public int AlternationThreshold;
        public int MediumLengthWeight;
        public int MediumLengthLines;
        public int LongLengthWeight;
        public int LongLengthLines;
        public int FirstKeywordCategoryWeight;
        public int AdditionalKeywordCategoryWeight;
        public int MaxKeywordCategories;
        public int RecentEventOverlapPenalty;
        public float RecentEventOverlapThreshold;
        public int AnnouncementOnlyPenalty;
        public int SamePairQueuedPenalty;
    }

    /// <summary>Localized XML categories plus one bounded category for player-added terms.</summary>
    public sealed class ConversationKeywordLexicon
    {
        public IList<string> DisclosureTerms;
        public IList<string> CommitmentTerms;
        public IList<string> ConflictTerms;
        public IList<string> ReconciliationTerms;
        public IList<string> CustomTerms;
    }

    /// <summary>Builds and scores local candidate facts without touching game state.</summary>
    public static class ConversationCandidatePolicy
    {
        /// <summary>Reduces a plain conversation to facts, including category-capped term matching.</summary>
        public static ConversationCandidateFacts BuildFacts(
            Conversation conversation,
            string pairKey,
            ConversationKeywordLexicon lexicon,
            ConversationCandidatePolicyOptions options,
            float recentEventTextOverlap,
            bool alreadyQueuedOrAssessed,
            bool samePairAlreadyQueued,
            bool dailyRecordingDisabled)
        {
            ConversationCandidateFacts facts = new ConversationCandidateFacts
            {
                ConversationId = conversation != null ? conversation.RootTalkId ?? string.Empty : string.Empty,
                PairKey = pairKey ?? string.Empty,
                FirstTick = conversation != null ? conversation.FirstTick : 0,
                LastTick = conversation != null ? conversation.LastTick : 0,
                RecentEventTextOverlap = recentEventTextOverlap,
                AlreadyQueuedOrAssessed = alreadyQueuedOrAssessed,
                SamePairAlreadyQueued = samePairAlreadyQueued,
                DailyRecordingDisabled = dailyRecordingDisabled
            };

            if (conversation == null || conversation.Lines == null)
            {
                return facts;
            }

            facts.ParticipantCount = conversation.ParticipantIds().Count;
            HashSet<string> speakers = new HashSet<string>(StringComparer.Ordinal);
            List<ConversationLine> usableLines = new List<ConversationLine>();
            string previousSpeaker = null;
            string normalizedTranscript = string.Empty;

            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                usableLines.Add(line);
                facts.LineCount++;
                facts.HasUsableText = true;

                if (!string.IsNullOrEmpty(line.SpeakerId))
                {
                    speakers.Add(line.SpeakerId);
                    if (previousSpeaker != null && previousSpeaker != line.SpeakerId)
                    {
                        facts.SpeakerAlternations++;
                    }

                    previousSpeaker = line.SpeakerId;
                }

                if (IsCharged(line.Social))
                {
                    facts.ChargedLineCount++;
                }

                if (line.Kind == BridgeTalkKind.User)
                {
                    facts.HasUserTalk = true;
                }

                if (IsAnnouncementKind(line.Kind))
                {
                    facts.HasEventTalk = true;
                }

                if (normalizedTranscript.Length > 0)
                {
                    normalizedTranscript += " ";
                }

                normalizedTranscript += UnicodeText.NormalizeForMatching(line.Text);
            }

            facts.DistinctSpeakerCount = speakers.Count;
            facts.HasReciprocalChargedReply = HasReciprocalChargedReply(usableLines);

            int maxCategories = options != null ? options.MaxKeywordCategories : 0;
            facts.KeywordCategoryCount = CountKeywordCategories(normalizedTranscript, lexicon, maxCategories);
            if (facts.KeywordCategoryCount > 0 && options != null)
            {
                facts.KeywordScore = options.FirstKeywordCategoryWeight
                    + (facts.KeywordCategoryCount - 1) * options.AdditionalKeywordCategoryWeight;
            }

            facts.IsAnnouncementOnly = facts.HasEventTalk
                && facts.ChargedLineCount == 0
                && !facts.HasUserTalk
                && facts.KeywordCategoryCount == 0;
            return facts;
        }

        /// <summary>Applies hard rejection and XML-owned scoring in one deterministic pass.</summary>
        public static ConversationCandidateDecision Evaluate(
            ConversationCandidateFacts facts,
            ConversationCandidatePolicyOptions options)
        {
            if (facts == null || options == null)
            {
                return Reject("rejected=missing_facts");
            }

            if (facts.ParticipantCount < 2)
            {
                return Reject("rejected=monologue");
            }

            if (!facts.HasUsableText || facts.LineCount <= 0)
            {
                return Reject("rejected=no_usable_text");
            }

            if (facts.DistinctSpeakerCount < 2)
            {
                return Reject("rejected=one_speaker");
            }

            if (facts.AlreadyQueuedOrAssessed)
            {
                return Reject("rejected=duplicate");
            }

            if (facts.DailyRecordingDisabled)
            {
                return Reject("rejected=daily_cap_zero");
            }

            int score = 0;
            List<string> reasons = new List<string>();

            if (facts.ChargedLineCount > 0)
            {
                Add(ref score, reasons, "charged", options.ChargedSocialWeight);
            }

            if (facts.HasReciprocalChargedReply)
            {
                Add(ref score, reasons, "reciprocal", options.ReciprocalChargedWeight);
            }

            if (facts.HasUserTalk)
            {
                Add(ref score, reasons, "user", options.UserTalkWeight);
            }

            if (facts.SpeakerAlternations >= options.AlternationThreshold)
            {
                Add(ref score, reasons, "alternations", options.AlternationWeight);
            }

            if (facts.LineCount >= options.MediumLengthLines)
            {
                Add(ref score, reasons, "length", options.MediumLengthWeight);
            }

            if (facts.LineCount >= options.LongLengthLines)
            {
                Add(ref score, reasons, "long", options.LongLengthWeight);
            }

            if (facts.KeywordScore != 0)
            {
                Add(ref score, reasons, "keywords", facts.KeywordScore);
            }

            if (facts.RecentEventTextOverlap >= options.RecentEventOverlapThreshold)
            {
                Add(ref score, reasons, "overlap", -Math.Abs(options.RecentEventOverlapPenalty));
            }

            if (facts.IsAnnouncementOnly)
            {
                Add(ref score, reasons, "announcement", -Math.Abs(options.AnnouncementOnlyPenalty));
            }

            if (facts.SamePairAlreadyQueued)
            {
                Add(ref score, reasons, "same_pair", -Math.Abs(options.SamePairQueuedPenalty));
            }

            bool hasPersonalSignal = facts.ChargedLineCount > 0
                || facts.HasUserTalk
                || facts.KeywordCategoryCount > 0;
            string explanation = "score=" + score;
            if (reasons.Count > 0)
            {
                explanation += "; " + string.Join("; ", reasons.ToArray());
            }

            if (!hasPersonalSignal)
            {
                explanation += "; no_personal_signal";
            }

            return new ConversationCandidateDecision
            {
                EligibleForAssessment = hasPersonalSignal && score >= options.CandidateScoreThreshold,
                Score = score,
                Reason = explanation
            };
        }

        /// <summary>Counts matched categories, never term variants, up to the supplied category cap.</summary>
        public static int CountKeywordCategories(
            string normalizedTranscript,
            ConversationKeywordLexicon lexicon,
            int maxCategories)
        {
            if (lexicon == null || string.IsNullOrEmpty(normalizedTranscript) || maxCategories <= 0)
            {
                return 0;
            }

            int matched = 0;
            IList<string>[] categories =
            {
                lexicon.DisclosureTerms,
                lexicon.CommitmentTerms,
                lexicon.ConflictTerms,
                lexicon.ReconciliationTerms,
                lexicon.CustomTerms
            };

            for (int i = 0; i < categories.Length && matched < maxCategories; i++)
            {
                if (CategoryMatches(normalizedTranscript, categories[i]))
                {
                    matched++;
                }
            }

            return matched;
        }

        private static bool CategoryMatches(string normalizedTranscript, IList<string> terms)
        {
            if (terms == null)
            {
                return false;
            }

            for (int i = 0; i < terms.Count; i++)
            {
                if (UnicodeText.ContainsWholeTerm(normalizedTranscript, terms[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasReciprocalChargedReply(List<ConversationLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                ConversationLine charged = lines[i];
                if (!IsCharged(charged.Social) || string.IsNullOrEmpty(charged.SpeakerId))
                {
                    continue;
                }

                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (!string.IsNullOrEmpty(lines[j].SpeakerId)
                        && lines[j].SpeakerId != charged.SpeakerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCharged(BridgeSocialKind social)
        {
            return social == BridgeSocialKind.Insult
                || social == BridgeSocialKind.Slight
                || social == BridgeSocialKind.Kind;
        }

        private static bool IsAnnouncementKind(BridgeTalkKind kind)
        {
            return kind == BridgeTalkKind.Urgent
                || kind == BridgeTalkKind.Event
                || kind == BridgeTalkKind.QuestOffer
                || kind == BridgeTalkKind.QuestEnd;
        }

        private static void Add(ref int score, List<string> reasons, string name, int contribution)
        {
            if (contribution == 0)
            {
                return;
            }

            score += contribution;
            reasons.Add(name + "=" + contribution);
        }

        private static ConversationCandidateDecision Reject(string reason)
        {
            return new ConversationCandidateDecision
            {
                EligibleForAssessment = false,
                Score = 0,
                Reason = reason
            };
        }
    }
}
