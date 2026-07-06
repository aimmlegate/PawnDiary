// Level 2 inbound: turns important RimTalk conversations into diary entries. This is the impure shell
// around the pure conversation logic (ConversationAssembler / ImportancePolicy / ThrottlePolicy /
// ConversationContext):
//   • RecordDisplayedChat(...) is called from the Harmony patch for every displayed chat line. It maps
//     the live RimTalk TalkResponse into a plain ConversationLine and feeds the assembler.
//   • ProcessDueConversations(...) runs on the bridge's tick pass: it flushes conversations that have
//     gone quiet, keeps the important ones, resolves the pawns, applies the throttle, and submits.
//
// RimTalk-type isolation: methods that name RimTalk types are [NoInlining] and are only reached after
// the mod's RimTalkActive guard (the patch is only installed when RimTalk is active). The flush/submit
// path names no RimTalk types, so it is plain impure Pawn Diary code.
//
// Save behavior: pending (not-yet-flushed) conversations are DROPPED on save/new game. They are
// seconds of chatter and disposable — unlike core batches, losing them costs nothing (plan Step 5.7).
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
    /// Collects displayed RimTalk chat and submits the important conversations as diary entries.
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

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            Pawn target = SafeTarget(talk);

            // Per-line developer logging works at any level (it is a diagnostic toggle, default off).
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (settings != null && settings.devChatLogging)
            {
                string speakerName = speaker != null ? speaker.LabelShortCap.ToString() : SafeString(talk.Name);
                Log.Message(PawnDiaryRimTalkBridgeMod.LogPrefix + " chat: " + speakerName
                    + " [" + talk.TalkType + "/" + SafeInteraction(talk) + "] "
                    + ContextFormat.CapAtWord(SafeText(talk), 160));
            }

            // Conversation capture itself only runs at level 2.
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(2))
            {
                return;
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
        /// Flushes quiet conversations and submits the important ones. MAIN THREAD ONLY (PawnDiaryApi +
        /// .Translate()). Names no RimTalk types.
        /// </summary>
        public static void ProcessDueConversations(int now)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(2))
            {
                return;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int quietTicks = settings != null ? settings.conversationQuietTicks : 2500;
            int minReplies = settings != null ? settings.minRepliesForImportant : 4;
            int transcriptCap = settings != null ? settings.transcriptLineCap : 4;
            bool devLog = settings != null && settings.devChatLogging;
            int dayIndex = now / TicksPerDay;

            ThrottleLimits limits = new ThrottleLimits
            {
                perPawnDailyCap = settings != null ? settings.perPawnDailyCap : 2,
                colonyDailyCap = settings != null ? settings.colonyDailyCap : 6,
                pairMinGapTicks = settings != null ? settings.pairMinGapTicks : 30000
            };

            List<Conversation> due = Assembler.FlushQuiet(now, quietTicks);
            for (int i = 0; i < due.Count; i++)
            {
                Conversation conversation = due[i];
                string reason = ImportancePolicy.Explain(conversation, minReplies);
                if (reason.Length == 0)
                {
                    DevLog(devLog, "skipped ordinary conversation " + conversation.RootTalkId
                        + " (" + conversation.Lines.Count + " lines)");
                    continue;
                }

                string subjectId = ArgMaxByLines(conversation, null);
                Pawn subject = ResolveLivePawn(subjectId);
                if (subject == null)
                {
                    DevLog(devLog, "dropped important conversation " + conversation.RootTalkId
                        + " (subject not a live spawned pawn)");
                    continue;
                }

                string partnerId = ArgMaxByLines(conversation, subjectId);
                Pawn partner = ResolveLivePawn(partnerId);
                string throttlePartnerId = partner != null ? partnerId : string.Empty;

                if (!Throttle.TryReserve(subjectId, throttlePartnerId, now, dayIndex, limits))
                {
                    DevLog(devLog, "throttled conversation " + conversation.RootTalkId
                        + " (per-pawn/colony cap or pair gap)");
                    continue;
                }

                Submit(conversation, subject, partner, partnerId, transcriptCap, reason, devLog);
            }
        }

        /// <summary>Drops pending conversations and clears throttle/pawn state on new game load.</summary>
        public static void ResetForNewGame()
        {
            Assembler.FlushAll();
            Throttle.Reset();
            PawnById.Clear();
        }

        private static void Submit(
            Conversation conversation,
            Pawn subject,
            Pawn partner,
            string partnerId,
            int transcriptCap,
            string reason,
            bool devLog)
        {
            string partnerName = partner != null ? partner.LabelShortCap.ToString() : NameForParticipant(conversation, partnerId);
            if (string.IsNullOrEmpty(partnerName))
            {
                partnerName = "PawnDiaryRimTalkBridge.Event.SomeonePartner".Translate();
            }

            ExternalPromptEntryRequest request = new ExternalPromptEntryRequest
            {
                sourceId = BridgeIds.ModId,
                eventKey = BridgeIds.ConversationEventKey,
                subject = subject,
                partner = partner,
                eventLabel = "PawnDiaryRimTalkBridge.Event.ConversationLabel".Translate(),
                summaryText = "PawnDiaryRimTalkBridge.Event.ConversationSummary".Translate(partnerName, conversation.Lines.Count),
                promptInstruction = "PawnDiaryRimTalkBridge.Event.ConversationInstruction".Translate(),
                extraContext = ConversationContext.BuildExtraContext(conversation, transcriptCap),
                dedupKey = "rimtalkbridge|" + conversation.RootTalkId
                // forceRecord stays false: respect the external budget and pipeline gates.
            };

            // Engine mode (advanced, off by default): route generation through RimTalk's own AI. When
            // it starts an async query it owns the submission; any miss falls through to the normal path.
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (settings != null && settings.useRimTalkEngine
                && RimTalkEngineClient.TryStartEngineSubmit(request, subject, partner))
            {
                DevLog(devLog, "conversation " + conversation.RootTalkId + " reason=" + reason + " routed to RimTalk engine");
                return;
            }

            DiaryEventSubmissionResult result = PawnDiaryApi.SubmitPromptEntry(request);
            DevLog(devLog, "submitted conversation " + conversation.RootTalkId + " reason=" + reason
                + " recorded=" + (result != null && result.recorded));
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

        private static Pawn ResolveLivePawn(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Pawn pawn;
            if (PawnById.TryGetValue(id, out pawn) && pawn != null && pawn.Spawned)
            {
                return pawn;
            }

            return null;
        }

        // Recovers a display name for a participant from the recorded lines when the live pawn is gone.
        private static string NameForParticipant(Conversation conversation, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null)
                {
                    continue;
                }

                if (line.SpeakerId == id && !string.IsNullOrEmpty(line.SpeakerName))
                {
                    return line.SpeakerName;
                }

                if (line.TargetId == id && !string.IsNullOrEmpty(line.TargetName))
                {
                    return line.TargetName;
                }
            }

            return string.Empty;
        }

        private static void DevLog(bool enabled, string message)
        {
            if (enabled)
            {
                Log.Message(PawnDiaryRimTalkBridgeMod.LogPrefix + " " + message);
            }
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
