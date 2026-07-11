// Impure main-thread coordinator for the bounded RimTalk editorial funnel. It owns the ranked queue,
// one Pawn Diary completion handle, batch/day cadence, recent-event enrichment, conservative failure
// behavior, and application of accepted results. Networking, background tasks, cancellation, lane
// cooldowns, and token/request budgets remain entirely inside PawnDiaryApi.RequestLlmCompletion.
//
// Queued candidates are transient and plain. Before assessment and again before accepted submission,
// live pawns are re-resolved by ConversationTracker. Save/load discards the queue and in-flight maps;
// Pawn Diary's own session cancellation owns the underlying request.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Runs one bounded assessment queue and completion handle for the current game.</summary>
    internal static class ConversationAssessmentCoordinator
    {
        private const int TicksPerDay = 60000;

        private static readonly ConversationCandidateQueue Queue = new ConversationCandidateQueue();
        private static readonly ConversationAssessmentBatchGate BatchGate = new ConversationAssessmentBatchGate();

        private static int activeHandle;
        private static ConversationAssessmentBatch activeBatch;
        private static bool discardActiveResult;

        /// <summary>True after this root id has already received a queue outcome this game.</summary>
        public static bool HasSeen(string conversationId)
        {
            return Queue.HasSeen(conversationId);
        }

        /// <summary>True when the pair already occupies at least one bounded queue slot.</summary>
        public static bool HasQueuedPair(string pairKey)
        {
            return Queue.ContainsPair(pairKey);
        }

        /// <summary>Offers one locally eligible candidate to the bounded rank policy.</summary>
        public static ConversationQueueOfferResult Enqueue(
            QueuedConversationCandidate candidate,
            ConversationAssessmentPolicyDef policy,
            out QueuedConversationCandidate evicted)
        {
            ConversationAssessmentPolicyDef effective = policy ?? ConversationAssessmentPolicyDef.Current;
            ConversationQueueOfferResult result = Queue.TryAdd(
                candidate,
                effective.maxQueuedCandidates,
                effective.maxCandidatesPerPair,
                out evicted);
            return result;
        }

        /// <summary>
        /// Polls the current Pawn Diary handle and applies accepted rows. MAIN THREAD. Called before
        /// TryStartNewBatch so a terminal handle frees the one-in-flight slot in the same bridge pass.
        /// </summary>
        public static void PollAndApply(int nowTick)
        {
            if (activeHandle <= 0)
            {
                return;
            }

            LlmCompletionResult completion = PawnDiaryApi.GetLlmCompletionResult(activeHandle);
            if (completion != null && completion.status == LlmCompletionStatus.Pending)
            {
                return;
            }

            ConversationAssessmentBatch completedBatch = activeBatch;
            bool discard = discardActiveResult;
            activeHandle = 0;
            activeBatch = null;
            discardActiveResult = false;
            BatchGate.MarkFinished();

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            bool shouldApply = !discard
                && PawnDiaryRimTalkBridgeMod.LevelAtLeast(2)
                && settings != null
                && settings.useSemanticConversationAssessment;
            if (!shouldApply)
            {
                DevLog("discarded completed conversation assessment because Level 2/semantic selection is off");
                return;
            }

            if (completion == null || completion.status == LlmCompletionStatus.Unknown)
            {
                DevLog("conversation assessment handle became unknown; batch ignored");
                return;
            }

            if (completion.status == LlmCompletionStatus.Failed)
            {
                DevLog("conversation assessment transport failed; batch dropped without retry");
                return;
            }

            if (completion.status != LlmCompletionStatus.Succeeded
                || string.IsNullOrWhiteSpace(completion.text))
            {
                DevLog("conversation assessment returned a blank/unknown result; batch ignored");
                return;
            }

            ConversationAssessmentPolicyDef policy = ConversationAssessmentPolicyDef.Current;
            ConversationAssessmentParseResult parsed = ConversationAssessmentResponseParser.Parse(
                completion.text,
                completedBatch,
                Math.Max(1, policy.assessmentFocusChars));
            if (!parsed.Success)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                        + " conversation assessment returned malformed output; every candidate in that batch was ignored.",
                    "PawnDiaryRimTalkBridge.Assessment.Malformed".GetHashCode());
                DevLog("malformed conversation assessment: " + parsed.Error);
                return;
            }

            for (int i = 0; i < parsed.Results.Count; i++)
            {
                ConversationAssessmentResult result = parsed.Results[i];
                QueuedConversationCandidate candidate = CandidateFor(completedBatch, result.ConversationId);
                if (candidate == null)
                {
                    continue;
                }

                if (result.Decision == ConversationAssessmentTokens.Ignore)
                {
                    DevLog("assessment ignored " + candidate.ConversationId + " reason=" + result.Reason);
                    continue;
                }

                ConversationTracker.ApplyAcceptedAssessment(candidate, result, nowTick);
            }
        }

        /// <summary>Starts local-only acceptance or one semantic batch when policy/cadence allows.</summary>
        public static void TryStartNewBatch(int nowTick)
        {
            ConversationAssessmentPolicyDef policy = ConversationAssessmentPolicyDef.Current;
            List<QueuedConversationCandidate> expired = Queue.RemoveExpired(nowTick, policy.candidateExpiryTicks);
            if (expired.Count > 0)
            {
                DevLog("expired " + expired.Count + " queued conversation candidate(s)");
            }

            bool levelTwo = PawnDiaryRimTalkBridgeMod.LevelAtLeast(2);
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (activeHandle > 0
                && (!levelTwo || settings == null || !settings.useSemanticConversationAssessment))
            {
                // Remember the off transition even if the player turns the setting back on before the
                // handle becomes terminal. A paid result must never leak past an explicit off choice.
                discardActiveResult = true;
            }

            if (!levelTwo)
            {
                Queue.ClearQueued();
                return;
            }

            if (settings == null || Queue.Count == 0)
            {
                return;
            }

            if (!settings.useSemanticConversationAssessment)
            {
                ProcessLocalOnly(nowTick, policy, policy.KeywordLexicon(settings.conversationReactionTermsCsv));
                return;
            }

            if (activeHandle > 0)
            {
                return;
            }

            string assessmentPrompt = policy.AssessmentPrompt(settings.assessmentPromptOverride);
            if (string.IsNullOrWhiteSpace(assessmentPrompt))
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                        + " conversation assessment policy has no localized system prompt; queued candidates will expire without submission.",
                    "PawnDiaryRimTalkBridge.Assessment.MissingPrompt".GetHashCode());
                return;
            }

            int dayIndex = nowTick / TicksPerDay;
            if (!BatchGate.CanAttempt(nowTick, dayIndex, policy.maxBatchesPerDay, policy.minBatchGapTicks))
            {
                return;
            }

            DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
            if (setup == null || setup.activeLaneCount <= 0)
            {
                // A lane may be configured later. Keep the bounded candidates until their XML expiry.
                return;
            }

            List<QueuedConversationCandidate> ranked = Queue.PeekRanked(policy.maxCandidatesPerBatch);
            List<QueuedConversationCandidate> valid = new List<QueuedConversationCandidate>();
            for (int i = 0; i < ranked.Count; i++)
            {
                QueuedConversationCandidate candidate = ranked[i];
                Pawn subject = ConversationTracker.ResolveLivePawnForAssessment(candidate.SubjectId);
                Pawn partner = ConversationTracker.ResolveLivePawnForAssessment(candidate.PartnerId);
                if (subject == null || partner == null)
                {
                    Queue.Remove(candidate.ConversationId);
                    DevLog("ignored queued conversation " + candidate.ConversationId
                        + " because a pawn is no longer spawned/diary-eligible");
                    continue;
                }

                if (ConversationTracker.IsPairOnConversationCooldown(
                    candidate.SubjectId, candidate.PartnerId, nowTick, policy))
                {
                    Queue.Remove(candidate.ConversationId);
                    DevLog("ignored queued conversation " + candidate.ConversationId
                        + " because a participant entered the rolling chat-event cooldown");
                    continue;
                }

                // Refresh from completed prose immediately before formatting, as well as from status
                // callbacks, so title/summary context is as rich as the public API currently exposes.
                if (policy.recentEventCount > 0)
                {
                    int fetch = RecentFetchCount(policy.recentEventCount);
                    RecentDiaryEventCache.EnrichForPawn(subject, fetch);
                    RecentDiaryEventCache.EnrichForPawn(partner, fetch);
                    candidate.RecentEvents = RecentDiaryEventCache.ForPair(
                        candidate.SubjectId,
                        candidate.PartnerId,
                        nowTick,
                        policy.recentEventCount,
                        policy.recentEventWindowTicks);
                }
                else
                {
                    candidate.RecentEvents = new List<RecentDiaryEvent>();
                }
                valid.Add(candidate);
            }

            if (valid.Count == 0)
            {
                return;
            }

            ConversationAssessmentBatch batch = ConversationAssessmentBatchFormatter.Format(valid, policy.FormatOptions());
            if (batch.CandidateAliases.Count == 0 || string.IsNullOrWhiteSpace(batch.UserText))
            {
                // Defensive bad-policy escape hatch: ignore the strongest candidate so an impossible
                // input cap cannot wedge the queue forever.
                Queue.Remove(valid[0].ConversationId);
                DevLog("ignored conversation " + valid[0].ConversationId + " because no assessment batch fit the XML limits");
                return;
            }

            int handle = PawnDiaryApi.RequestLlmCompletion(new ExternalLlmCompletionRequest
            {
                sourceId = BridgeIds.ModId,
                laneIndex = settings.assessmentLaneIndex,
                systemPrompt = assessmentPrompt,
                userText = batch.UserText,
                maxTokens = policy.assessmentMaxTokens
            });
            if (handle <= 0)
            {
                // Budget/concurrency/configuration can be temporary. Leave every candidate queued and
                // wait the full batch gap before asking the API again.
                BatchGate.MarkRejectedAttempt(nowTick);
                DevLog("assessment request was not admitted; bounded queue retained for retry");
                return;
            }

            for (int i = 0; i < batch.CandidateAliases.Count; i++)
            {
                Queue.Remove(batch.CandidateByAlias[batch.CandidateAliases[i]].ConversationId);
            }

            activeBatch = batch;
            activeHandle = handle;
            discardActiveResult = false;
            BatchGate.MarkStarted(nowTick, dayIndex);
            DevLog("started assessment batch handle=" + handle + " candidates=" + batch.CandidateAliases.Count);
        }

        /// <summary>Drops queued work when Level 2 is disabled but preserves any handle until polled.</summary>
        public static void DiscardQueuedForDisabledLevel()
        {
            Queue.ClearQueued();
            if (activeHandle > 0)
            {
                discardActiveResult = true;
                // Keep only the opaque handle needed to poll/release core state; discard transcripts
                // and alias maps immediately at the no-flow boundary.
                activeBatch = null;
            }
        }

        /// <summary>Clears all transient state on FinalizeInit.</summary>
        public static void ResetForNewGame()
        {
            Queue.Reset();
            BatchGate.Reset();
            activeHandle = 0;
            activeBatch = null;
            discardActiveResult = false;
        }

        private static void ProcessLocalOnly(
            int nowTick,
            ConversationAssessmentPolicyDef policy,
            ConversationKeywordLexicon lexicon)
        {
            List<QueuedConversationCandidate> ranked = Queue.PeekRanked(policy.maxCandidatesPerBatch);
            for (int i = 0; i < ranked.Count; i++)
            {
                QueuedConversationCandidate candidate = ranked[i];
                Queue.Remove(candidate.ConversationId);
                if (ConversationTracker.IsPairOnConversationCooldown(
                    candidate.SubjectId, candidate.PartnerId, nowTick, policy))
                {
                    DevLog("local-only assessment ignored " + candidate.ConversationId
                        + " because a participant is in the rolling chat-event cooldown");
                    continue;
                }

                if (candidate.Score < policy.strictLocalRecordThreshold
                    || candidate.RecentEventTextOverlap >= policy.recentEventOverlapThreshold)
                {
                    DevLog("local-only assessment ignored " + candidate.ConversationId
                        + " score=" + candidate.Score + " overlap=" + candidate.RecentEventTextOverlap);
                    continue;
                }

                ConversationAssessmentResult result = LocalAssessment(candidate, policy, lexicon);
                if (result == null)
                {
                    DevLog("local-only assessment ignored " + candidate.ConversationId + " (no explicit focus line)");
                    continue;
                }

                ConversationTracker.ApplyAcceptedAssessment(candidate, result, nowTick);
            }
        }

        private static ConversationAssessmentResult LocalAssessment(
            QueuedConversationCandidate candidate,
            ConversationAssessmentPolicyDef policy,
            ConversationKeywordLexicon lexicon)
        {
            if (candidate == null || candidate.Conversation == null || candidate.Conversation.Lines == null)
            {
                return null;
            }

            ConversationLine charged = null;
            ConversationLine user = null;
            ConversationLine keyword = null;
            ConversationLine firstUsable = null;
            string chargedReason = "other";
            for (int i = 0; i < candidate.Conversation.Lines.Count; i++)
            {
                ConversationLine line = candidate.Conversation.Lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                if (firstUsable == null)
                {
                    firstUsable = line;
                }

                if (line.Social == BridgeSocialKind.Insult || line.Social == BridgeSocialKind.Slight)
                {
                    charged = line;
                    chargedReason = "conflict";
                    break;
                }

                if (line.Social == BridgeSocialKind.Kind)
                {
                    charged = line;
                    chargedReason = "reconciliation";
                    break;
                }

                if (line.Kind == BridgeTalkKind.User && user == null)
                {
                    user = line;
                }

                if (keyword == null
                    && ConversationCandidatePolicy.CountKeywordCategories(
                        UnicodeText.NormalizeForMatching(line.Text), lexicon, 1) > 0)
                {
                    keyword = line;
                }
            }

            ConversationLine chosen = charged ?? user ?? keyword ?? firstUsable;
            if (chosen == null)
            {
                return null;
            }

            string reason = charged != null
                ? chargedReason
                : (user != null && chosen == user ? "disclosure" : "other");
            string focus = UnicodeText.CapUtf16(
                UnicodeText.CleanOneLine(chosen.Text),
                Math.Max(1, policy.assessmentFocusChars));
            if (focus.Length == 0)
            {
                return null;
            }

            return new ConversationAssessmentResult
            {
                ConversationId = candidate.ConversationId,
                Decision = ConversationAssessmentTokens.Standalone,
                EventId = string.Empty,
                Reason = reason,
                Focus = focus
            };
        }

        private static int RecentFetchCount(int configuredCount)
        {
            if (configuredCount <= 0)
            {
                return 1;
            }

            // Fetch beyond the final count because bridge-authored rows are filtered after reading;
            // 32 is a defensive API-read ceiling independent of tuning.
            return configuredCount >= 11 ? 32 : configuredCount * 3;
        }

        private static QueuedConversationCandidate CandidateFor(
            ConversationAssessmentBatch batch,
            string conversationId)
        {
            if (batch == null || string.IsNullOrEmpty(conversationId))
            {
                return null;
            }

            foreach (KeyValuePair<string, QueuedConversationCandidate> pair in batch.CandidateByAlias)
            {
                if (pair.Value != null && pair.Value.ConversationId == conversationId)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        private static void DevLog(string message)
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (settings != null && settings.devChatLogging)
            {
                Log.Message(PawnDiaryRimTalkBridgeMod.LogPrefix + " " + message);
            }
        }
    }
}
