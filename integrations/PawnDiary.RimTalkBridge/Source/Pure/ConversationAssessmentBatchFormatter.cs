// Pure compact formatter for batched editorial assessment. It intentionally excludes pawn summaries,
// personas, surroundings, writing styles, and full diary prose: the assessment only needs short
// transcript evidence and a few recent event labels/summaries.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Plain recent diary-event data cached by the bridge.</summary>
    public sealed class RecentDiaryEvent
    {
        public string EventId;
        public int Tick;
        public string PawnId;
        public string GroupLabel;
        public string Domain;
        public string Title;
        public string Summary;
        public string ExternalSourceId;
    }

    /// <summary>XML-backed formatter limits copied to a pure object by the runtime adapter.</summary>
    public sealed class ConversationAssessmentFormatOptions
    {
        public int MaxCandidates;
        public int TranscriptLines;
        public int LineChars;
        public int InputChars;
        public int RecentEventCount;
    }

    /// <summary>Formatted request plus the alias maps needed for strict response validation.</summary>
    public sealed class ConversationAssessmentBatch
    {
        public string UserText = string.Empty;
        public readonly List<string> CandidateAliases = new List<string>();
        public readonly Dictionary<string, QueuedConversationCandidate> CandidateByAlias =
            new Dictionary<string, QueuedConversationCandidate>(StringComparer.Ordinal);
        public readonly Dictionary<string, RecentDiaryEvent> EventByAlias =
            new Dictionary<string, RecentDiaryEvent>(StringComparer.Ordinal);
        public readonly Dictionary<string, HashSet<string>> AllowedEventAliasesByCandidateAlias =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    }

    /// <summary>Builds deterministic compact batches and drops whole tail candidates on overflow.</summary>
    public static class ConversationAssessmentBatchFormatter
    {
        /// <summary>Formats the strongest prefix that fits all configured limits.</summary>
        public static ConversationAssessmentBatch Format(
            IList<QueuedConversationCandidate> rankedCandidates,
            ConversationAssessmentFormatOptions options)
        {
            ConversationAssessmentBatch empty = new ConversationAssessmentBatch();
            if (rankedCandidates == null || options == null || options.MaxCandidates <= 0
                || options.TranscriptLines <= 0 || options.LineChars <= 0 || options.InputChars <= 0)
            {
                return empty;
            }

            List<QueuedConversationCandidate> usable = new List<QueuedConversationCandidate>();
            for (int i = 0; i < rankedCandidates.Count && usable.Count < options.MaxCandidates; i++)
            {
                QueuedConversationCandidate candidate = rankedCandidates[i];
                if (candidate != null && candidate.Conversation != null
                    && !string.IsNullOrEmpty(candidate.ConversationId))
                {
                    usable.Add(candidate);
                }
            }

            for (int candidateCount = usable.Count; candidateCount >= 1; candidateCount--)
            {
                List<QueuedConversationCandidate> selected = usable.GetRange(0, candidateCount);
                int eventLimit = options.RecentEventCount < 0 ? 0 : options.RecentEventCount;
                ConversationAssessmentBatch batch = Build(selected, options, eventLimit);
                if (batch.UserText.Length <= options.InputChars)
                {
                    return batch;
                }

                // The policy says overflow trims whole candidates first. Only when the strongest
                // candidate alone still cannot fit may its optional oldest event rows be removed.
                if (candidateCount == 1)
                {
                    for (int maxEvents = eventLimit - 1; maxEvents >= 0; maxEvents--)
                    {
                        batch = Build(selected, options, maxEvents);
                        if (batch.UserText.Length <= options.InputChars)
                        {
                            return batch;
                        }
                    }
                }
            }

            return empty;
        }

        private static ConversationAssessmentBatch Build(
            List<QueuedConversationCandidate> selected,
            ConversationAssessmentFormatOptions options,
            int maxEvents)
        {
            ConversationAssessmentBatch batch = new ConversationAssessmentBatch();
            List<RecentDiaryEvent> events = SelectEvents(selected, maxEvents);
            StringBuilder text = new StringBuilder();
            text.Append("events:\n");

            for (int i = 0; i < events.Count; i++)
            {
                string alias = "e" + (i + 1);
                RecentDiaryEvent recent = events[i];
                batch.EventByAlias[alias] = recent;
                text.Append(alias)
                    .Append(" | tick=").Append(recent.Tick)
                    .Append(" | label=").Append(Field(recent.GroupLabel, options.LineChars));
                if (!string.IsNullOrWhiteSpace(recent.Title))
                {
                    text.Append(" | title=").Append(Field(recent.Title, options.LineChars));
                }

                text.Append(" | summary=").Append(Field(recent.Summary, options.LineChars)).Append('\n');
            }

            if (events.Count == 0)
            {
                text.Append("none\n");
            }

            text.Append("\nconversations:\n");
            for (int i = 0; i < selected.Count; i++)
            {
                QueuedConversationCandidate candidate = selected[i];
                string alias = "c" + (i + 1);
                batch.CandidateAliases.Add(alias);
                batch.CandidateByAlias[alias] = candidate;
                batch.AllowedEventAliasesByCandidateAlias[alias] = AllowedEvents(candidate, batch.EventByAlias);

                text.Append(alias)
                    .Append(" | social=").Append(DominantSocial(candidate.Conversation).ToString().ToLowerInvariant())
                    .Append(" | kind=").Append(ConversationContext.DominantKind(candidate.Conversation).ToString().ToLowerInvariant())
                    .Append('\n');

                int added = 0;
                for (int lineIndex = 0; lineIndex < candidate.Conversation.Lines.Count; lineIndex++)
                {
                    if (added >= options.TranscriptLines)
                    {
                        break;
                    }

                    ConversationLine line = candidate.Conversation.Lines[lineIndex];
                    if (line == null || string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    string speaker = string.IsNullOrWhiteSpace(line.SpeakerName) ? "?" : Field(line.SpeakerName, 48);
                    string quote = Field(line.Text, options.LineChars);
                    if (quote.Length == 0)
                    {
                        continue;
                    }

                    text.Append(speaker).Append(": ").Append(quote).Append('\n');
                    added++;
                }

                text.Append('\n');
            }

            batch.UserText = text.ToString().TrimEnd();
            return batch;
        }

        private static List<RecentDiaryEvent> SelectEvents(
            List<QueuedConversationCandidate> candidates,
            int maxEvents)
        {
            Dictionary<string, RecentDiaryEvent> byId = new Dictionary<string, RecentDiaryEvent>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                List<RecentDiaryEvent> recent = candidates[i].RecentEvents;
                if (recent == null)
                {
                    continue;
                }

                for (int j = 0; j < recent.Count; j++)
                {
                    RecentDiaryEvent item = recent[j];
                    if (item == null || string.IsNullOrEmpty(item.EventId))
                    {
                        continue;
                    }

                    RecentDiaryEvent existing;
                    if (!byId.TryGetValue(item.EventId, out existing) || IsRicher(item, existing))
                    {
                        byId[item.EventId] = item;
                    }
                }
            }

            List<RecentDiaryEvent> result = new List<RecentDiaryEvent>(byId.Values);
            result.Sort(delegate(RecentDiaryEvent left, RecentDiaryEvent right)
            {
                int tick = right.Tick.CompareTo(left.Tick);
                return tick != 0 ? tick : string.CompareOrdinal(left.EventId, right.EventId);
            });
            if (result.Count > maxEvents)
            {
                result.RemoveRange(maxEvents, result.Count - maxEvents);
            }

            return result;
        }

        private static HashSet<string> AllowedEvents(
            QueuedConversationCandidate candidate,
            Dictionary<string, RecentDiaryEvent> supplied)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (candidate.RecentEvents == null)
            {
                return ids;
            }

            foreach (KeyValuePair<string, RecentDiaryEvent> pair in supplied)
            {
                for (int i = 0; i < candidate.RecentEvents.Count; i++)
                {
                    RecentDiaryEvent recent = candidate.RecentEvents[i];
                    if (recent != null && recent.EventId == pair.Value.EventId)
                    {
                        ids.Add(pair.Key);
                        break;
                    }
                }
            }

            return ids;
        }

        private static BridgeSocialKind DominantSocial(Conversation conversation)
        {
            if (conversation == null || conversation.Lines == null)
            {
                return BridgeSocialKind.None;
            }

            Dictionary<BridgeSocialKind, int> counts = new Dictionary<BridgeSocialKind, int>();
            BridgeSocialKind best = BridgeSocialKind.None;
            int bestCount = -1;
            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null)
                {
                    continue;
                }

                int count;
                counts.TryGetValue(line.Social, out count);
                count++;
                counts[line.Social] = count;
                if (count > bestCount)
                {
                    best = line.Social;
                    bestCount = count;
                }
            }

            return best;
        }

        private static bool IsRicher(RecentDiaryEvent candidate, RecentDiaryEvent existing)
        {
            int candidateFields = Filled(candidate.Title) + Filled(candidate.Summary) + Filled(candidate.GroupLabel);
            int existingFields = Filled(existing.Title) + Filled(existing.Summary) + Filled(existing.GroupLabel);
            return candidateFields > existingFields || (candidateFields == existingFields && candidate.Tick > existing.Tick);
        }

        private static int Filled(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? 0 : 1;
        }

        private static string Field(string value, int maxChars)
        {
            string cleaned = UnicodeText.CleanOneLine(value).Replace("|", "/");
            return UnicodeText.CapUtf16(cleaned, maxChars);
        }
    }
}
