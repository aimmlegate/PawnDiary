// Level 2 inbound: turns selected RimTalk conversations into diary entries. This is the impure shell
// around the pure assembler/scorer/overlap/queue/assessment/submission policies:
//   • RecordDisplayedChat(...) is called from the Harmony patch for every displayed chat line. It maps
//     the live RimTalk TalkResponse into a plain ConversationLine and feeds the assembler.
//   • ProcessDueConversations(...) runs on the bridge's tick pass: it flushes conversations that have
//     gone quiet, resolves both pawns, collects recent-event facts, scores locally, and enqueues.
//   • ApplyAcceptedAssessment(...) is called later by the coordinator. Only there does the bridge
//     reserve normal per-day/pair throttle space and submit one pairwise Pawn Diary prompt event.
//
// RimTalk-type isolation: methods that name RimTalk types are [NoInlining] and are only reached after
// the mod's RimTalkActive guard (the patch is only installed when RimTalk is active). The flush/submit
// path names no RimTalk types, so it is plain impure Pawn Diary code.
//
// Save behavior: pending (not-yet-flushed) conversations are DROPPED on save/new game. They are
// seconds of chatter and disposable; queued/in-flight candidates are likewise transient by policy.
// Only successful per-pawn anti-spam timestamps are copied through RimTalkBridgeGameComponent.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Collects displayed RimTalk chat and submits only editorially accepted conversations.
    /// </summary>
    internal static class ConversationTracker
    {
        // In-game ticks per day, used to derive the throttle's day index from the game tick.
        private const int TicksPerDay = 60000;

        private static readonly ConversationAssembler Assembler = new ConversationAssembler();
        private static readonly ThrottlePolicy Throttle = new ThrottlePolicy();

        // pawn load id -> live pawn, so the flush pass can resolve participants back to pawns. Bounded
        // by colonist count; cleared on new game. Entries may go stale (a pawn despawns) — resolution
        // re-checks liveness before use.
        private static readonly Dictionary<string, Pawn> PawnById = new Dictionary<string, Pawn>();

        /// <summary>
        /// Records one displayed RimTalk chat line. Called from the Harmony patch on the main thread.
        /// Skips everything below level 2. Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RecordDisplayedChat(Pawn speaker, TalkResponse talk)
        {
            if (talk == null)
            {
                return;
            }

            // Level 0 is a hard no-flow boundary and Level 1 is context/persona only. Return before
            // reading target/text or logging chat content so neither level captures conversation data.
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(2))
            {
                return;
            }

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            Pawn target = SafeTarget(talk);

            // Per-line developer logging is a Level-2 diagnostic toggle (default off).
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (settings != null && settings.devChatLogging)
            {
                string speakerName = speaker != null ? speaker.LabelShortCap.ToString() : SafeString(talk.Name);
                Log.Message(PawnDiaryRimTalkBridgeMod.LogPrefix + " chat: " + speakerName
                    + " [" + talk.TalkType + "/" + SafeInteraction(talk) + "] "
                    + ContextFormat.CapAtWord(SafeText(talk), 160));
            }

            ConversationLine line = new ConversationLine
            {
                TalkId = GuidToString(talk.Id),
                ParentTalkId = GuidToString(talk.ParentTalkId),
                SpeakerId = speaker != null ? speaker.GetUniqueLoadID() : string.Empty,
                SpeakerName = speaker != null ? speaker.LabelShortCap.ToString() : SafeString(talk.Name),
                TargetId = target != null ? target.GetUniqueLoadID() : string.Empty,
                TargetName = target != null ? target.LabelShortCap.ToString() : SafeString(talk.TargetName),
                Text = SafeText(talk),
                Kind = MapKind(talk.TalkType),
                Social = MapSocial(SafeInteraction(talk)),
                Tick = now
            };

            Assembler.Record(line);

            if (speaker != null)
            {
                PawnById[line.SpeakerId] = speaker;
            }

            if (target != null)
            {
                PawnById[line.TargetId] = target;
            }
        }

        /// <summary>
        /// Flushes quiet conversations, scores them, and offers eligible ones to the bounded queue.
        /// MAIN THREAD ONLY (PawnDiaryApi). Names no RimTalk types.
        /// </summary>
        public static void ProcessDueConversations(int now)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(2))
            {
                // A level change is a real data-flow boundary. Old unfinished/queued chat must not
                // spring back to life if Level 2 is enabled again later.
                Assembler.FlushAll();
                PawnById.Clear();
                RecentDiaryEventCache.Clear();
                ConversationAssessmentCoordinator.DiscardQueuedForDisabledLevel();
                return;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int quietTicks = settings != null ? settings.conversationQuietTicks : 2500;
            bool devLog = settings != null && settings.devChatLogging;
            ConversationAssessmentPolicyDef policy = ConversationAssessmentPolicyDef.Current;
            ConversationCandidatePolicyOptions scoring = policy.CandidateOptions();
            ConversationKeywordLexicon lexicon = policy.KeywordLexicon(
                settings != null ? settings.conversationReactionTermsCsv : string.Empty);
            bool dailyDisabled = settings == null
                || settings.perPawnDailyCap <= 0
                || settings.colonyDailyCap <= 0;

            List<Conversation> due = Assembler.FlushQuiet(now, quietTicks);
            for (int i = 0; i < due.Count; i++)
            {
                Conversation conversation = due[i];
                string subjectId = ArgMaxByLines(conversation, null);
                Pawn subject = ResolveLivePawnForAssessment(subjectId);
                if (subject == null)
                {
                    DevLog(devLog, "ignored conversation " + conversation.RootTalkId
                        + " (subject not spawned/diary-eligible)");
                    continue;
                }

                string partnerId = ArgMaxByLines(conversation, subjectId);
                Pawn partner = ResolveLivePawnForAssessment(partnerId);
                if (partner == null)
                {
                    DevLog(devLog, "ignored conversation " + conversation.RootTalkId
                        + " (partner not spawned/diary-eligible)");
                    continue;
                }

                if (IsPairOnConversationCooldown(subjectId, partnerId, now, policy))
                {
                    DevLog(devLog, "ignored conversation " + conversation.RootTalkId
                        + " (a participant already received a chat event within the rolling cooldown)");
                    continue;
                }

                string pairKey = SharedMemorySelection.PairKey(subjectId, partnerId);
                bool alreadySeen = ConversationAssessmentCoordinator.HasSeen(conversation.RootTalkId);
                bool samePairQueued = ConversationAssessmentCoordinator.HasQueuedPair(pairKey);

                // Run the genuinely cheap/local pass first. Recent snapshot reads can only add a
                // negative overlap penalty, so a conversation that fails here can never be rescued by
                // doing that extra work.
                ConversationCandidateFacts facts = ConversationCandidatePolicy.BuildFacts(
                    conversation,
                    pairKey,
                    lexicon,
                    scoring,
                    0f,
                    alreadySeen,
                    samePairQueued,
                    dailyDisabled);
                ConversationCandidateDecision decision = ConversationCandidatePolicy.Evaluate(facts, scoring);
                if (!decision.EligibleForAssessment)
                {
                    DevLog(devLog, "local scorer ignored " + conversation.RootTalkId + " " + decision.Reason);
                    continue;
                }

                // Only locally nominated candidates pay for recent-event collection. Status callbacks
                // usually seeded these facts already; snapshots enrich pending rows with completed
                // title/summary data before the final overlap penalty is applied.
                List<RecentDiaryEvent> recent = new List<RecentDiaryEvent>();
                float overlap = 0f;
                if (policy.recentEventCount > 0)
                {
                    int fetch = RecentFetchCount(policy.recentEventCount);
                    RecentDiaryEventCache.EnrichForPawn(subject, fetch);
                    RecentDiaryEventCache.EnrichForPawn(partner, fetch);
                    recent = RecentDiaryEventCache.ForPair(
                        subjectId,
                        partnerId,
                        now,
                        policy.recentEventCount,
                        policy.recentEventWindowTicks);
                    overlap = ConversationTextOverlap.StrongestSimilarity(
                        TranscriptText(conversation),
                        RecentEventTexts(recent),
                        policy.overlapMinimumTokenChars,
                        policy.useCharacterTrigramOverlap);
                }

                facts = ConversationCandidatePolicy.BuildFacts(
                    conversation,
                    pairKey,
                    lexicon,
                    scoring,
                    overlap,
                    alreadySeen,
                    samePairQueued,
                    dailyDisabled);
                decision = ConversationCandidatePolicy.Evaluate(facts, scoring);
                if (!decision.EligibleForAssessment)
                {
                    DevLog(devLog, "overlap filter ignored " + conversation.RootTalkId + " " + decision.Reason);
                    continue;
                }

                QueuedConversationCandidate candidate = new QueuedConversationCandidate
                {
                    ConversationId = conversation.RootTalkId,
                    PairKey = pairKey,
                    SubjectId = subjectId,
                    PartnerId = partnerId,
                    FirstTick = conversation.FirstTick,
                    LastTick = conversation.LastTick,
                    Score = decision.Score,
                    ScoreReason = decision.Reason,
                    RecentEventTextOverlap = overlap,
                    Conversation = conversation,
                    RecentEvents = recent
                };
                QueuedConversationCandidate evicted;
                ConversationQueueOfferResult offer = ConversationAssessmentCoordinator.Enqueue(candidate, policy, out evicted);
                DevLog(devLog, "candidate " + conversation.RootTalkId + " " + decision.Reason
                    + " queue=" + offer.ToString().ToLowerInvariant());
                if (evicted != null)
                {
                    DevLog(devLog, "bounded queue ignored weaker candidate " + evicted.ConversationId);
                }
            }
        }

        /// <summary>Drops pending conversations and clears throttle/pawn state on new game load.</summary>
        public static void ResetForNewGame()
        {
            Assembler.FlushAll();
            Throttle.Reset();
            PawnById.Clear();
        }

        /// <summary>Detached rolling-cooldown state for the per-game save component.</summary>
        public static Dictionary<string, int> PawnCooldownSnapshot(int nowTick)
        {
            ConversationAssessmentPolicyDef policy = ConversationAssessmentPolicyDef.Current;
            Throttle.PruneExpiredPawnCooldowns(nowTick, policy.perPawnConversationCooldownTicks);
            return Throttle.PawnCooldownSnapshot();
        }

        /// <summary>Restores saved rolling-cooldown state after FinalizeInit clears static caches.</summary>
        public static void RestorePawnCooldowns(IDictionary<string, int> saved)
        {
            Throttle.RestorePawnCooldowns(saved);
        }

        /// <summary>Cheap pre-assessment gate used by both the tracker and coordinator.</summary>
        internal static bool IsPairOnConversationCooldown(
            string subjectId,
            string partnerId,
            int nowTick,
            ConversationAssessmentPolicyDef policy)
        {
            ConversationAssessmentPolicyDef effective = policy ?? ConversationAssessmentPolicyDef.Current;
            return Throttle.IsEitherPawnOnCooldown(
                subjectId,
                partnerId,
                nowTick,
                effective.perPawnConversationCooldownTicks);
        }

        /// <summary>
        /// Applies one accepted semantic/local assessment. Re-resolves both pawns, reserves the normal
        /// caps only now, submits exactly one pairwise prompt event, and refunds a rejected submission.
        /// </summary>
        public static void ApplyAcceptedAssessment(
            QueuedConversationCandidate candidate,
            ConversationAssessmentResult assessment,
            int now)
        {
            if (candidate == null || assessment == null)
            {
                return;
            }

            Pawn subject = ResolveLivePawnForAssessment(candidate.SubjectId);
            Pawn partner = ResolveLivePawnForAssessment(candidate.PartnerId);
            if (subject == null || partner == null)
            {
                DevLog(IsDevLogging(), "accepted conversation " + candidate.ConversationId
                    + " expired before submission (pawn not spawned/diary-eligible)");
                return;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int transcriptCap = settings != null ? settings.transcriptLineCap : 4;
            ConversationSubmissionPlan plan = ConversationSubmissionPlanner.Build(
                candidate.Conversation,
                transcriptCap,
                assessment);
            if (!plan.ShouldSubmit)
            {
                DevLog(IsDevLogging(), "accepted conversation " + candidate.ConversationId
                    + " produced no valid submission plan");
                return;
            }

            ThrottleLimits limits = new ThrottleLimits
            {
                perPawnDailyCap = settings != null ? settings.perPawnDailyCap : 1,
                colonyDailyCap = settings != null ? settings.colonyDailyCap : 6,
                pairMinGapTicks = settings != null ? settings.pairMinGapTicks : 30000,
                perPawnCooldownTicks = ConversationAssessmentPolicyDef.Current.perPawnConversationCooldownTicks
            };
            int dayIndex = now / TicksPerDay;
            if (!Throttle.TryReserve(candidate.SubjectId, candidate.PartnerId, now, dayIndex, limits))
            {
                DevLog(IsDevLogging(), "accepted conversation " + candidate.ConversationId
                    + " was blocked by the rolling per-pawn cooldown, daily/colony cap, or pair gap");
                return;
            }

            string partnerName = partner.LabelShortCap.ToString();
            ExternalPromptEntryRequest request = new ExternalPromptEntryRequest
            {
                sourceId = BridgeIds.ModId,
                eventKey = BridgeIds.ConversationEventKey,
                subject = subject,
                partner = partner,
                eventLabel = "PawnDiaryRimTalkBridge.Event.ConversationLabel".Translate(),
                summaryText = "PawnDiaryRimTalkBridge.Event.ConversationSummary".Translate(
                    partnerName,
                    candidate.Conversation.Lines.Count),
                promptInstruction = "PawnDiaryRimTalkBridge.Event.ConversationInstruction".Translate(),
                extraContext = plan.ExtraContext,
                dedupKey = plan.DedupKey
                // forceRecord stays false: accepted conversations still respect normal generation budget.
            };

            DiaryEventSubmissionResult result;
            try
            {
                result = PawnDiaryApi.SubmitPromptEntry(request);
            }
            catch (Exception e)
            {
                // The public API is designed to return a rejected result rather than throw, but an
                // unexpected adapter/core mismatch must not leave either pawn cooling down for an
                // event that was never registered.
                Throttle.Release(candidate.SubjectId, candidate.PartnerId, dayIndex, limits);
                Log.ErrorOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                        + " assessed conversation submission failed; throttle refunded: " + e,
                    "PawnDiaryRimTalkBridge.Conversation.SubmitException".GetHashCode());
                return;
            }

            bool recorded = result != null && result.recorded;
            if (!recorded)
            {
                Throttle.Release(candidate.SubjectId, candidate.PartnerId, dayIndex, limits);
            }

            DevLog(IsDevLogging(), "submitted assessed conversation " + candidate.ConversationId
                + " decision=" + assessment.Decision + " reason=" + assessment.Reason
                + " recorded=" + recorded + (recorded ? string.Empty : " throttle_refunded=true"));
        }

        // Returns the participant with the most lines, skipping skipId. First appearance wins ties.
        private static string ArgMaxByLines(Conversation conversation, string skipId)
        {
            List<string> participants = conversation.ParticipantIds();
            string best = string.Empty;
            int bestLines = -1;
            for (int i = 0; i < participants.Count; i++)
            {
                string id = participants[i];
                if (id == skipId)
                {
                    continue;
                }

                int lines = conversation.LineCountFor(id);
                if (lines > bestLines)
                {
                    bestLines = lines;
                    best = id;
                }
            }

            return best;
        }

        /// <summary>MAIN THREAD: resolves a spawned pawn that can own a normal diary POV.</summary>
        internal static Pawn ResolveLivePawnForAssessment(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Pawn pawn;
            if (PawnById.TryGetValue(id, out pawn) && pawn != null && pawn.Spawned
                && PawnDiaryApi.IsDiaryEligible(pawn))
            {
                return pawn;
            }

            return null;
        }

        private static void DevLog(bool enabled, string message)
        {
            if (enabled)
            {
                Log.Message(PawnDiaryRimTalkBridgeMod.LogPrefix + " " + message);
            }
        }

        private static bool IsDevLogging()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return settings != null && settings.devChatLogging;
        }

        private static string TranscriptText(Conversation conversation)
        {
            if (conversation == null || conversation.Lines == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(line.Text);
            }

            return text.ToString();
        }

        private static List<string> RecentEventTexts(List<RecentDiaryEvent> events)
        {
            List<string> texts = new List<string>();
            if (events == null)
            {
                return texts;
            }

            for (int i = 0; i < events.Count; i++)
            {
                RecentDiaryEvent recent = events[i];
                if (recent == null)
                {
                    continue;
                }

                texts.Add((recent.GroupLabel ?? string.Empty) + " "
                    + (recent.Title ?? string.Empty) + " "
                    + (recent.Summary ?? string.Empty));
            }

            return texts;
        }

        private static int RecentFetchCount(int configuredCount)
        {
            if (configuredCount <= 0)
            {
                return 1;
            }

            // Fetch extra completed rows because bridge-authored entries are filtered after the API
            // read. The hard 32-row ceiling is a defensive cost bound, not tuning policy.
            return configuredCount >= 11 ? 32 : configuredCount * 3;
        }

        // --- RimTalk-typed helpers (all [NoInlining], only reached when RimTalk is active) ---

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Pawn SafeTarget(TalkResponse talk)
        {
            try
            {
                return talk.GetTarget();
            }
            catch
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string SafeText(TalkResponse talk)
        {
            try
            {
                return SafeString(talk.GetText());
            }
            catch
            {
                return SafeString(talk.Text);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InteractionType SafeInteraction(TalkResponse talk)
        {
            try
            {
                return talk.GetInteractionType();
            }
            catch
            {
                return InteractionType.None;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static BridgeTalkKind MapKind(TalkType kind)
        {
            switch (kind)
            {
                case TalkType.Urgent: return BridgeTalkKind.Urgent;
                case TalkType.Hediff: return BridgeTalkKind.Hediff;
                case TalkType.LevelUp: return BridgeTalkKind.LevelUp;
                case TalkType.Chitchat: return BridgeTalkKind.Chitchat;
                case TalkType.Event: return BridgeTalkKind.Event;
                case TalkType.QuestOffer: return BridgeTalkKind.QuestOffer;
                case TalkType.QuestEnd: return BridgeTalkKind.QuestEnd;
                case TalkType.Thought: return BridgeTalkKind.Thought;
                case TalkType.User: return BridgeTalkKind.User;
                default: return BridgeTalkKind.Other;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static BridgeSocialKind MapSocial(InteractionType social)
        {
            switch (social)
            {
                case InteractionType.Insult: return BridgeSocialKind.Insult;
                case InteractionType.Slight: return BridgeSocialKind.Slight;
                case InteractionType.Chat: return BridgeSocialKind.Chat;
                case InteractionType.Kind: return BridgeSocialKind.Kind;
                default: return BridgeSocialKind.None;
            }
        }

        private static string GuidToString(Guid guid)
        {
            return guid == Guid.Empty ? string.Empty : guid.ToString();
        }

        private static string SafeString(string value)
        {
            return value ?? string.Empty;
        }
    }
}
