// Focused pure tests for the bounded RimTalk conversation-assessment funnel. These cover every
// selection boundary without loading RimWorld, RimTalk, Verse, Unity, settings, or Defs.
using System;
using System.Collections.Generic;
using PawnDiaryRimTalkBridge;

namespace RimTalkBridgeLogicTests
{
    internal static class AssessmentTests
    {
        private static Action<bool, string> assert;

        public static void Run(Action<bool, string> assertion)
        {
            assert = assertion;

            CandidateScoring();
            EditablePolicy();
            TextOverlap();
            QueuePolicy();
            BatchFormatter();
            ResponseParser();
            SubmissionPlanning();
        }

        private static void EditablePolicy()
        {
            ConversationReactionTermsValidationResult valid = ConversationReactionTermsEditor.Validate(
                " I promise, Я ОБЕЩАЮ , i PROMISE, forgive me, ", 8, 40);
            assert(valid.IsValid && valid.Terms.Count == 3
                && valid.NormalizedCsv == "I promise, Я ОБЕЩАЮ, forgive me",
                "editable terms: CSV is trimmed and Unicode/case duplicates collapse");

            assert(ConversationReactionTermsEditor.Validate("promise\nforgive", 8, 40).Error
                    == ConversationReactionTermsEditor.ErrorNewline,
                "editable terms: newline-separated input is rejected in favor of commas");
            assert(ConversationReactionTermsEditor.Validate("one, two, three", 2, 40).Error
                    == ConversationReactionTermsEditor.ErrorTooMany,
                "editable terms: configured term count is validated before save");
            assert(ConversationReactionTermsEditor.Validate("toolong", 8, 3).Error
                    == ConversationReactionTermsEditor.ErrorTooLong,
                "editable terms: configured per-term length is validated before save");
            assert(ConversationReactionTermsEditor.Validate("!!!, --", 8, 40).Error
                    == ConversationReactionTermsEditor.ErrorInvalidTerm,
                "editable terms: punctuation-only entries cannot become reaction terms");

            ConversationKeywordLexicon defaults = Lexicon();
            ConversationKeywordLexicon edited = ConversationReactionTermsEditor.ApplyOverride(
                defaults, new[] { "promise", "прости", "новая клятва", "NEW VOW" });
            assert(edited.CommitmentTerms.Count == 1 && edited.ReconciliationTerms.Count == 1
                && edited.DisclosureTerms.Count == 0 && edited.ConflictTerms.Count == 0,
                "editable terms: retained defaults keep their original categories and removals apply");
            assert(edited.CustomTerms.Count == 2,
                "editable terms: additions share one bounded custom category");
            string normalized = UnicodeText.NormalizeForMatching("Новая клятва and new vow");
            assert(ConversationCandidatePolicy.CountKeywordCategories(normalized, edited, 5) == 1,
                "editable terms: many custom additions still contribute only one category");
            assert(ConversationReactionTermsEditor.SameTerms(
                    ConversationReactionTermsEditor.Flatten(defaults),
                    ConversationReactionTermsEditor.Flatten(defaults)),
                "editable terms: default-list comparison is deterministic");

            string defaultPrompt = "default prompt";
            assert(ConversationAssessmentPromptEditor.Resolve(defaultPrompt, "  custom\r\nprompt  ", 100)
                    == "custom\nprompt",
                "editable prompt: nonblank override replaces XML after line-ending cleanup");
            assert(ConversationAssessmentPromptEditor.Resolve(defaultPrompt, "  ", 100) == defaultPrompt,
                "editable prompt: blank override resets to XML default");
            string capped = ConversationAssessmentPromptEditor.Resolve(defaultPrompt, "123456789😀tail", 10);
            assert(capped == "123456789" && !char.IsHighSurrogate(capped[capped.Length - 1]),
                "editable prompt: character cap never splits a UTF-16 surrogate pair");

            string nonBmpLetters = "\U00010400\U00010401\U00010402tail";
            string textElementCapped = UnicodeText.CapTextElements(nonBmpLetters, 3);
            assert(UnicodeText.TextElementCount(textElementCapped) == 3
                && textElementCapped == "\U00010400\U00010401\U00010402",
                "editable terms: total editor cap uses the same Unicode text elements as validation");

            string composed = ConversationAssessmentWireContract.Compose("custom editorial policy");
            assert(composed.StartsWith(ConversationAssessmentWireContract.SystemPromptPrefix)
                && composed.EndsWith("custom editorial policy")
                && composed.IndexOf("\"decision\"", StringComparison.Ordinal) >= 0,
                "editable prompt: code-owned wire contract survives every editorial override");
        }

        private static void CandidateScoring()
        {
            ConversationCandidatePolicyOptions options = Options();
            ConversationKeywordLexicon lexicon = Lexicon();

            Conversation monologue = ConversationOf("mono",
                Line("a", "", "I promise", BridgeTalkKind.User, BridgeSocialKind.Kind, 1));
            ConversationCandidateDecision monologueDecision = Decide(monologue, lexicon, options, 0f);
            assert(!monologueDecision.EligibleForAssessment && monologueDecision.Reason == "rejected=monologue",
                "candidate: monologue is hard-rejected");

            Conversation blank = ConversationOf("blank",
                Line("a", "b", "  \n ", BridgeTalkKind.User, BridgeSocialKind.Kind, 1),
                Line("b", "a", "\t", BridgeTalkKind.User, BridgeSocialKind.Kind, 2));
            ConversationCandidateDecision blankDecision = Decide(blank, lexicon, options, 0f);
            assert(!blankDecision.EligibleForAssessment && blankDecision.Reason == "rejected=no_usable_text",
                "candidate: blank conversation is hard-rejected");

            Conversation neutral = ConversationOf("neutral",
                Line("a", "b", "Nice weather.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 1),
                Line("b", "a", "It is.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2));
            ConversationCandidateDecision neutralDecision = Decide(neutral, lexicon, options, 0f);
            assert(!neutralDecision.EligibleForAssessment && neutralDecision.Score == 0,
                "candidate: neutral two-line chat is rejected");

            Conversation longNeutral = ConversationOf("long-neutral",
                Line("a", "b", "one", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 1),
                Line("b", "a", "two", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2),
                Line("a", "b", "three", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 3),
                Line("b", "a", "four", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 4),
                Line("a", "b", "five", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 5),
                Line("b", "a", "six", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 6),
                Line("a", "b", "seven", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 7),
                Line("b", "a", "eight", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 8));
            ConversationCandidateDecision lengthDecision = Decide(longNeutral, lexicon, options, 0f);
            assert(!lengthDecision.EligibleForAssessment && lengthDecision.Score == 4
                && lengthDecision.Reason.EndsWith("no_personal_signal"),
                "candidate: length alone never nominates a conversation");

            Conversation charged = ConversationOf("charged",
                Line("a", "b", "I hate what you did.", BridgeTalkKind.Chitchat, BridgeSocialKind.Insult, 1),
                Line("b", "a", "Then say it to me.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2));
            ConversationCandidateDecision chargedDecision = Decide(charged, lexicon, options, 0f);
            assert(chargedDecision.EligibleForAssessment && chargedDecision.Score == 7,
                "candidate: charged reciprocal exchange scores above neutral");
            assert(chargedDecision.Reason == "score=7; charged=3; reciprocal=2; keywords=2",
                "candidate: explanation output is stable; got " + chargedDecision.Reason);

            Conversation announcement = ConversationOf("announcement",
                Line("a", "b", "Raiders at the wall!", BridgeTalkKind.Event, BridgeSocialKind.None, 1),
                Line("b", "a", "Get inside!", BridgeTalkKind.Urgent, BridgeSocialKind.None, 2));
            ConversationCandidateDecision announcementDecision = Decide(announcement, lexicon, options, 0f);
            assert(!announcementDecision.EligibleForAssessment && announcementDecision.Score == -3,
                "candidate: event/urgent announcement without personal content scores lower");

            Conversation variants = ConversationOf("variants",
                Line("a", "b", "I hate this conflict and we always argue.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 1),
                Line("b", "a", "You PROMISED; I forgive you.", BridgeTalkKind.User, BridgeSocialKind.Chat, 2));
            ConversationCandidateFacts variantFacts = Facts(variants, lexicon, options, 0f);
            assert(variantFacts.KeywordCategoryCount == 3 && variantFacts.KeywordScore == 4,
                "candidate: variants in one category count once and categories accumulate only once");

            ConversationCandidatePolicyOptions cappedOptions = Options();
            cappedOptions.MaxKeywordCategories = 2;
            ConversationCandidateFacts cappedFacts = Facts(variants, lexicon, cappedOptions, 0f);
            assert(cappedFacts.KeywordCategoryCount == 2 && cappedFacts.KeywordScore == 3,
                "candidate: keyword categories obey the configured cap");

            Conversation cyrillic = ConversationOf("ru",
                Line("a", "b", "Я ТЕБЕ ОБЕЩАЮ!", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 1),
                Line("b", "a", "Хорошо.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2));
            ConversationCandidateFacts cyrillicFacts = Facts(cyrillic, lexicon, options, 0f);
            assert(cyrillicFacts.KeywordCategoryCount == 1 && cyrillicFacts.KeywordScore == 2,
                "candidate: Unicode/Cyrillic matching is case-insensitive and punctuation-safe");

            ConversationCandidateFacts duplicateFacts = Facts(charged, lexicon, options, 0f);
            duplicateFacts.AlreadyQueuedOrAssessed = true;
            assert(ConversationCandidatePolicy.Evaluate(duplicateFacts, options).Reason == "rejected=duplicate",
                "candidate: already queued/assessed conversation is hard-rejected");
            duplicateFacts.AlreadyQueuedOrAssessed = false;
            duplicateFacts.DailyRecordingDisabled = true;
            assert(ConversationCandidatePolicy.Evaluate(duplicateFacts, options).Reason == "rejected=daily_cap_zero",
                "candidate: zero daily cap hard-rejects recording");
        }

        private static void TextOverlap()
        {
            float identical = ConversationTextOverlap.Similarity(
                "Raiders attacked the eastern wall.", "Raiders attacked the eastern wall.", 3, true);
            assert(Math.Abs(identical - 1f) < 0.0001f, "overlap: identical event/transcript is 1");

            float partial = ConversationTextOverlap.Similarity(
                "Raiders attacked the eastern wall.", "The eastern wall was repaired.", 3, false);
            assert(partial > 0f && partial < 1f, "overlap: partial text is between zero and one; got " + partial);

            float unrelated = ConversationTextOverlap.Similarity(
                "Raiders attacked the wall.", "We promised to cook dinner.", 3, false);
            assert(unrelated == 0f, "overlap: unrelated text is zero; got " + unrelated);

            float normalized = ConversationTextOverlap.Similarity(
                "RAIDERS, attacked!!!", "raiders attacked", 3, false);
            assert(Math.Abs(normalized - 1f) < 0.0001f, "overlap: punctuation/case normalize");

            assert(ConversationTextOverlap.Similarity("", "anything", 3, true) == 0f
                && ConversationTextOverlap.Similarity(null, null, 3, true) == 0f,
                "overlap: empty values are zero");

            float russian = ConversationTextOverlap.Similarity(
                "Налётчики атаковали восточную стену.", "налётчики атаковали стену", 3, true);
            assert(russian > 0.45f, "overlap: non-Latin/Cyrillic text overlaps; got " + russian);

            float repeated = ConversationTextOverlap.Similarity(
                "raid raid raid wall", "raid wall", 3, false);
            assert(Math.Abs(repeated - 1f) < 0.0001f, "overlap: repeated tokens do not inflate sets");
        }

        private static void QueuePolicy()
        {
            ConversationCandidateQueue queue = new ConversationCandidateQueue();
            QueuedConversationCandidate evicted;
            assert(queue.TryAdd(QueueItem("a", "p1", 4, 10), 2, 2, out evicted) == ConversationQueueOfferResult.Added,
                "queue: first candidate added");
            assert(queue.TryAdd(QueueItem("b", "p2", 5, 20), 2, 2, out evicted) == ConversationQueueOfferResult.Added,
                "queue: second candidate fills bounded capacity");
            assert(queue.TryAdd(QueueItem("c", "p3", 3, 30), 2, 2, out evicted) == ConversationQueueOfferResult.TooWeak
                && queue.Count == 2, "queue: weaker overflow candidate is ignored");

            ConversationQueueOfferResult replace = queue.TryAdd(QueueItem("d", "p4", 9, 40), 2, 2, out evicted);
            assert(replace == ConversationQueueOfferResult.ReplacedWeakest && evicted.ConversationId == "a",
                "queue: stronger candidate replaces weakest");
            assert(queue.TryAdd(QueueItem("d", "p4", 99, 1), 2, 2, out evicted) == ConversationQueueOfferResult.Duplicate,
                "queue: duplicate root id is rejected");

            ConversationCandidateQueue pairs = new ConversationCandidateQueue();
            pairs.TryAdd(QueueItem("p-a", "same", 8, 1), 10, 2, out evicted);
            pairs.TryAdd(QueueItem("p-b", "same", 7, 2), 10, 2, out evicted);
            ConversationQueueOfferResult pairWeak = pairs.TryAdd(QueueItem("p-c", "same", 6, 3), 10, 2, out evicted);
            assert(pairWeak == ConversationQueueOfferResult.PairLimit && pairs.Count == 2,
                "queue: per-pair limit rejects a weaker third candidate");
            ConversationQueueOfferResult pairStrong = pairs.TryAdd(QueueItem("p-d", "same", 10, 4), 10, 2, out evicted);
            assert(pairStrong == ConversationQueueOfferResult.ReplacedWeakest && evicted.ConversationId == "p-b",
                "queue: stronger candidate replaces weakest within a full pair");

            ConversationCandidateQueue ties = new ConversationCandidateQueue();
            ties.TryAdd(QueueItem("z", "x", 5, 10), 5, 5, out evicted);
            ties.TryAdd(QueueItem("a", "y", 5, 10), 5, 5, out evicted);
            ties.TryAdd(QueueItem("old", "q", 5, 5), 5, 5, out evicted);
            List<QueuedConversationCandidate> ranked = ties.PeekRanked(5);
            assert(ranked[0].ConversationId == "old" && ranked[1].ConversationId == "a" && ranked[2].ConversationId == "z",
                "queue: ties order by older first then stable id");
            assert(ties.RemoveExpired(100, 90).Count == 1 && ties.Count == 2,
                "queue: candidates expire by last tick");

            ConversationAssessmentBatchGate gate = new ConversationAssessmentBatchGate();
            assert(gate.CanAttempt(100, 0, 2, 50), "gate: first batch may start");
            gate.MarkStarted(100, 0);
            assert(!gate.CanAttempt(200, 0, 2, 50), "gate: one in-flight batch blocks another");
            gate.MarkFinished();
            assert(gate.CanAttempt(200, 0, 2, 50), "gate: terminal result frees the slot");
            gate.MarkStarted(200, 0);
            gate.MarkFinished();
            assert(!gate.CanAttempt(300, 0, 2, 50), "gate: daily batch cap is enforced");

            ConversationAssessmentBatchGate restoredGate = new ConversationAssessmentBatchGate();
            restoredGate.Restore(gate.Snapshot());
            assert(!restoredGate.InFlight && !restoredGate.CanAttempt(300, 0, 2, 50),
                "gate: save/load preserves the paid daily count but never an in-flight slot");
            assert(gate.CanAttempt(60000, 1, 2, 50), "gate: in-game day rollover resets batch count");
            gate.MarkRejectedAttempt(60000);
            assert(!gate.CanAttempt(60020, 1, 2, 50) && gate.CanAttempt(60050, 1, 2, 50),
                "gate: rejected API attempt retries only after the batch gap");
        }

        private static void BatchFormatter()
        {
            RecentDiaryEvent e1 = Event("event-1", 100, "raid", "Raiders attacked the eastern wall.");
            RecentDiaryEvent e2 = Event("event-2", 90, "insult", "Kim insulted Alex.");
            RecentDiaryEvent e3 = Event("event-3", 80, "break", "A colonist broke down.");
            QueuedConversationCandidate first = QueueItemWithText("root-1", "pair-1", 9, 10,
                "Kim", "Alex", "You left me outside.", "I did what I had to.");
            first.RecentEvents.AddRange(new[] { e1, e2, e3 });
            QueuedConversationCandidate second = QueueItemWithText("root-2", "pair-2", 8, 20,
                "Alex", "Kim", "Raiders!", "Get inside!");
            second.RecentEvents.Add(e1);

            ConversationAssessmentFormatOptions options = FormatOptions();
            options.RecentEventCount = 2;
            ConversationAssessmentBatch batch = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { first, second }, options);
            assert(batch.CandidateByAlias["c1"].ConversationId == "root-1"
                && batch.CandidateByAlias["c2"].ConversationId == "root-2",
                "formatter: c aliases map to actual root ids");
            assert(batch.EventByAlias.Count == 2 && batch.EventByAlias["e1"].EventId == "event-1",
                "formatter: recent event limit and aliases are deterministic");
            assert(batch.AllowedEventAliasesByCandidateAlias["c2"].SetEquals(new[] { "e1" }),
                "formatter: event aliases are validated per candidate");
            assert(batch.UserText.Contains("conversations:") && batch.UserText.Contains("Kim: You left me outside."),
                "formatter: compact transcript emitted");
            assert(!batch.UserText.Contains("persona") && !batch.UserText.Contains("writing style")
                && !batch.UserText.Contains("full diary"),
                "formatter: no persona, style, or full diary prose is included");
            ConversationAssessmentBatch repeat = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { first, second }, options);
            assert(repeat.UserText == batch.UserText
                && repeat.CandidateByAlias["c1"].ConversationId == batch.CandidateByAlias["c1"].ConversationId,
                "formatter: identical ranked input produces deterministic text and aliases");

            ConversationAssessmentFormatOptions oneCandidate = FormatOptions();
            ConversationAssessmentBatch firstOnly = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { first }, oneCandidate);
            ConversationAssessmentFormatOptions trim = FormatOptions();
            trim.InputChars = firstOnly.UserText.Length + 2;
            ConversationAssessmentBatch trimmed = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { first, second }, trim);
            assert(trimmed.CandidateAliases.Count == 1 && trimmed.CandidateByAlias["c1"].ConversationId == "root-1",
                "formatter: overflow drops a whole tail candidate");
            assert(trimmed.UserText.Length <= trim.InputChars, "formatter: hard character cap is obeyed");

            QueuedConversationCandidate emoji = QueueItemWithText("emoji", "pair", 5, 1,
                "A", "B", "123456789😀tail", "reply");
            ConversationAssessmentFormatOptions shortLine = FormatOptions();
            shortLine.LineChars = 10; // the high surrogate starts at UTF-16 position 9
            ConversationAssessmentBatch safe = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { emoji }, shortLine);
            assert(safe.UserText.Length <= shortLine.InputChars && !EndsWithHighSurrogateOnAnyLine(safe.UserText),
                "formatter: UTF-16 caps never split a surrogate pair");

            ConversationAssessmentFormatOptions candidateCap = FormatOptions();
            candidateCap.MaxCandidates = 1;
            ConversationAssessmentBatch capped = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { first, second }, candidateCap);
            assert(capped.CandidateAliases.Count == 1, "formatter: candidate count limit is enforced");

            Conversation lateEvidenceConversation = ConversationOf("late-evidence",
                Line("A", "B", "ordinary one", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 1),
                Line("B", "A", "ordinary two", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2),
                Line("A", "B", "ordinary three", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 3),
                Line("B", "A", "ordinary four", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 4),
                Line("A", "B", "I promise I will stay", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 5));
            QueuedConversationCandidate lateEvidence = new QueuedConversationCandidate
            {
                ConversationId = "late-evidence",
                PairKey = "a|b",
                Conversation = lateEvidenceConversation
            };
            ConversationAssessmentFormatOptions evidenceOptions = FormatOptions();
            evidenceOptions.TranscriptLines = 2;
            ConversationAssessmentBatch evidenceBatch = ConversationAssessmentBatchFormatter.Format(
                new List<QueuedConversationCandidate> { lateEvidence }, evidenceOptions);
            assert(evidenceBatch.UserText.Contains("A: ordinary one")
                && evidenceBatch.UserText.Contains("A: I promise I will stay")
                && !evidenceBatch.UserText.Contains("ordinary two"),
                "formatter: a late keyword line claims a bounded slot while early context is retained");
        }

        private static void ResponseParser()
        {
            ConversationAssessmentBatch batch = ParserBatch();
            string valid = "["
                + "{\"id\":\"c1\",\"decision\":\"related\",\"event\":\"e1\",\"reason\":\"conflict\",\"focus\":\"Kim no longer trusts Alex\"},"
                + "{\"id\":\"c2\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"commitment\",\"focus\":\"Alex promised to stay\"},"
                + "{\"id\":\"c3\",\"decision\":\"ignore\",\"event\":\"\",\"reason\":\"banter\",\"focus\":\"\"}]";
            ConversationAssessmentParseResult parsed = ConversationAssessmentResponseParser.Parse(valid, batch, 200);
            assert(parsed.Success && parsed.Results[0].Decision == "related"
                && parsed.Results[0].EventId == "event-1"
                && parsed.Results[1].Decision == "standalone"
                && parsed.Results[2].Decision == "ignore",
                "parser: valid ignore/related/standalone rows map to actual ids");

            ConversationAssessmentParseResult fenced = ConversationAssessmentResponseParser.Parse(
                "```json\n" + valid + "\n```", batch, 200);
            assert(fenced.Success && fenced.Results[0].Decision == "related",
                "parser: Markdown-fenced JSON array is accepted");

            string unknown = "[{\"id\":\"c99\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"other\",\"focus\":\"x\"}]";
            ConversationAssessmentParseResult unknownParsed = ConversationAssessmentResponseParser.Parse(unknown, batch, 200);
            assert(unknownParsed.Success && AllIgnored(unknownParsed.Results),
                "parser: unknown candidate id cannot create a result");

            string hallucinated = "[{\"id\":\"c1\",\"decision\":\"related\",\"event\":\"e99\",\"reason\":\"conflict\",\"focus\":\"x\"}]";
            assert(ConversationAssessmentResponseParser.Parse(hallucinated, batch, 200).Results[0].Decision == "ignore",
                "parser: hallucinated event alias becomes ignore");
            string contradictory = "[{\"id\":\"c1\",\"decision\":\"standalone\",\"event\":\"e1\",\"reason\":\"conflict\",\"focus\":\"x\"}]";
            assert(ConversationAssessmentResponseParser.Parse(contradictory, batch, 200).Results[0].Decision == "ignore",
                "parser: standalone result cannot smuggle a related-event link");

            string duplicate = "["
                + "{\"id\":\"c1\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"disclosure\",\"focus\":\"first\"},"
                + "{\"id\":\"c1\",\"decision\":\"ignore\",\"event\":\"\",\"reason\":\"echo\",\"focus\":\"\"}]";
            ConversationAssessmentParseResult duplicateParsed = ConversationAssessmentResponseParser.Parse(duplicate, batch, 200);
            assert(duplicateParsed.Results[0].Decision == "standalone" && duplicateParsed.Results[0].Focus == "first",
                "parser: first valid duplicate candidate row wins deterministically");
            assert(duplicateParsed.Results[1].Decision == "ignore" && duplicateParsed.Results[2].Decision == "ignore",
                "parser: missing candidates become ignore");

            assert(!ConversationAssessmentResponseParser.Parse("not json", batch, 200).Success
                && !ConversationAssessmentResponseParser.Parse("[{broken]", batch, 200).Success,
                "parser: malformed output fails conservatively");

            string longFocus = new string('x', 500);
            string overlong = "[{\"id\":\"c1\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"other\",\"focus\":\""
                + longFocus + "\"}]";
            assert(ConversationAssessmentResponseParser.Parse(overlong, batch, 40).Results[0].Focus.Length == 40,
                "parser: overlong focus is capped");

            string badTokens = "["
                + "{\"id\":\"c1\",\"decision\":\"record\",\"event\":\"\",\"reason\":\"other\",\"focus\":\"x\"},"
                + "{\"id\":\"c2\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"invented\",\"focus\":\"x\"}]";
            assert(AllIgnored(ConversationAssessmentResponseParser.Parse(badTokens, batch, 200).Results),
                "parser: unknown decision/reason tokens cannot record");

            string unicode = "[{\"id\":\"c1\",\"decision\":\"standalone\",\"event\":\"\",\"reason\":\"reconciliation\",\"focus\":\"Ким снова доверяет Алексу\"}]";
            assert(ConversationAssessmentResponseParser.Parse(unicode, batch, 200).Results[0].Focus == "Ким снова доверяет Алексу",
                "parser: Unicode focus text is preserved");
        }

        private static void SubmissionPlanning()
        {
            Conversation conversation = ConversationOf("root-42",
                Line("a", "b", "You left me outside.", BridgeTalkKind.Chitchat, BridgeSocialKind.Insult, 1),
                Line("b", "a", "I did what I had to.", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, 2));

            ConversationSubmissionPlan related = ConversationSubmissionPlanner.Build(conversation, 4,
                new ConversationAssessmentResult
                {
                    Decision = "related", EventId = "native-event", Reason = "conflict",
                    Focus = "Kim no longer trusts Alex"
                });
            assert(related.ShouldSubmit && related.ExtraContext.Contains("related_event_id=native-event")
                && related.ExtraContext.Contains("avoid_related_event_recap=true"),
                "submission: related assessment includes actual event id and recap guard");

            ConversationSubmissionPlan standalone = ConversationSubmissionPlanner.Build(conversation, 4,
                new ConversationAssessmentResult
                {
                    Decision = "standalone", EventId = "should-not-leak", Reason = "commitment",
                    Focus = "Alex promised to stay"
                });
            assert(standalone.ShouldSubmit && standalone.RelatedEventId == string.Empty
                && !ContainsPrefix(standalone.ExtraContext, "related_event_id=")
                && !standalone.ExtraContext.Contains("avoid_related_event_recap=true"),
                "submission: standalone omits related event context");

            ConversationSubmissionPlan ignored = ConversationSubmissionPlanner.Build(conversation, 4,
                new ConversationAssessmentResult { Decision = "ignore", Reason = "banter" });
            assert(!ignored.ShouldSubmit, "submission: ignore produces no submission");
            assert(related.DedupKey == "rimtalkbridge|root-42" && standalone.DedupKey == related.DedupKey,
                "submission: frozen root-id dedup key is stable");
            assert(related.ExtraContext.Contains("said_1=a: You left me outside.")
                && related.ExtraContext.Contains("conversation_focus=Kim no longer trusts Alex"),
                "submission: assessment focus supplements rather than replaces transcript evidence");
        }

        private static ConversationCandidateDecision Decide(
            Conversation conversation,
            ConversationKeywordLexicon lexicon,
            ConversationCandidatePolicyOptions options,
            float overlap)
        {
            return ConversationCandidatePolicy.Evaluate(Facts(conversation, lexicon, options, overlap), options);
        }

        private static ConversationCandidateFacts Facts(
            Conversation conversation,
            ConversationKeywordLexicon lexicon,
            ConversationCandidatePolicyOptions options,
            float overlap)
        {
            return ConversationCandidatePolicy.BuildFacts(
                conversation, "a|b", lexicon, options, overlap, false, false, false);
        }

        private static ConversationCandidatePolicyOptions Options()
        {
            return new ConversationCandidatePolicyOptions
            {
                CandidateScoreThreshold = 5,
                ChargedSocialWeight = 3,
                ReciprocalChargedWeight = 2,
                UserTalkWeight = 2,
                AlternationWeight = 2,
                AlternationThreshold = 2,
                MediumLengthWeight = 1,
                MediumLengthLines = 4,
                LongLengthWeight = 1,
                LongLengthLines = 8,
                FirstKeywordCategoryWeight = 2,
                AdditionalKeywordCategoryWeight = 1,
                MaxKeywordCategories = 4,
                RecentEventOverlapPenalty = 2,
                RecentEventOverlapThreshold = 0.45f,
                AnnouncementOnlyPenalty = 3,
                SamePairQueuedPenalty = 2
            };
        }

        private static ConversationKeywordLexicon Lexicon()
        {
            return new ConversationKeywordLexicon
            {
                DisclosureTerms = new[] { "I feel", "мне страшно" },
                CommitmentTerms = new[] { "promise", "promised", "обещаю" },
                ConflictTerms = new[] { "hate", "conflict", "argue" },
                ReconciliationTerms = new[] { "forgive", "прости" }
            };
        }

        private static Conversation ConversationOf(string id, params ConversationLine[] lines)
        {
            Conversation conversation = new Conversation { RootTalkId = id };
            conversation.Lines.AddRange(lines);
            if (lines.Length > 0)
            {
                conversation.FirstTick = lines[0].Tick;
                conversation.LastTick = lines[lines.Length - 1].Tick;
            }

            return conversation;
        }

        private static ConversationLine Line(
            string speaker,
            string target,
            string text,
            BridgeTalkKind kind,
            BridgeSocialKind social,
            int tick)
        {
            return new ConversationLine
            {
                SpeakerId = speaker,
                SpeakerName = speaker,
                TargetId = target,
                TargetName = target,
                Text = text,
                Kind = kind,
                Social = social,
                Tick = tick
            };
        }

        private static QueuedConversationCandidate QueueItem(string id, string pair, int score, int firstTick)
        {
            Conversation conversation = ConversationOf(id,
                Line("a", "b", "hello", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, firstTick),
                Line("b", "a", "reply", BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, firstTick + 1));
            return new QueuedConversationCandidate
            {
                ConversationId = id,
                PairKey = pair,
                SubjectId = "a",
                PartnerId = "b",
                FirstTick = firstTick,
                LastTick = firstTick + 1,
                Score = score,
                Conversation = conversation
            };
        }

        private static QueuedConversationCandidate QueueItemWithText(
            string id, string pair, int score, int firstTick,
            string firstName, string secondName, string firstText, string secondText)
        {
            Conversation conversation = ConversationOf(id,
                Line(firstName, secondName, firstText, BridgeTalkKind.Event, BridgeSocialKind.Insult, firstTick),
                Line(secondName, firstName, secondText, BridgeTalkKind.Chitchat, BridgeSocialKind.Chat, firstTick + 1));
            return new QueuedConversationCandidate
            {
                ConversationId = id,
                PairKey = pair,
                SubjectId = firstName,
                PartnerId = secondName,
                FirstTick = firstTick,
                LastTick = firstTick + 1,
                Score = score,
                Conversation = conversation
            };
        }

        private static RecentDiaryEvent Event(string id, int tick, string label, string summary)
        {
            return new RecentDiaryEvent
            {
                EventId = id,
                Tick = tick,
                PawnId = "a",
                GroupLabel = label,
                Domain = "Interaction",
                Title = label + " title",
                Summary = summary
            };
        }

        private static ConversationAssessmentFormatOptions FormatOptions()
        {
            return new ConversationAssessmentFormatOptions
            {
                MaxCandidates = 6,
                TranscriptLines = 4,
                LineChars = 160,
                InputChars = 3600,
                RecentEventCount = 3,
                KeywordLexicon = Lexicon()
            };
        }

        private static ConversationAssessmentBatch ParserBatch()
        {
            RecentDiaryEvent recent = Event("event-1", 100, "insult", "Kim insulted Alex.");
            List<QueuedConversationCandidate> candidates = new List<QueuedConversationCandidate>();
            for (int i = 1; i <= 3; i++)
            {
                QueuedConversationCandidate candidate = QueueItemWithText(
                    "root-" + i, "pair-" + i, 10 - i, i, "Kim", "Alex", "line " + i, "reply " + i);
                candidate.RecentEvents.Add(recent);
                candidates.Add(candidate);
            }

            return ConversationAssessmentBatchFormatter.Format(candidates, FormatOptions());
        }

        private static bool AllIgnored(List<ConversationAssessmentResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Decision != ConversationAssessmentTokens.Ignore)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsPrefix(List<string> values, string prefix)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EndsWithHighSurrogateOnAnyLine(string value)
        {
            string[] lines = value.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0 && char.IsHighSurrogate(lines[i][lines[i].Length - 1]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
