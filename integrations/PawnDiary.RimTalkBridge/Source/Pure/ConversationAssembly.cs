// Pure conversation assembly. NO RimWorld / RimTalk usings — this file is file-linked into the pure
// test project and must compile without the game. The impure side (ConversationTracker) maps live
// RimTalk chat into these plain DTOs; everything here is deterministic and testable.
//
// RimTalk emits one "line" per displayed chat message, each carrying its own id and the id of the
// message it replies to (ParentTalkId). There is no explicit "conversation ended" event, so we group
// lines into conversations by reply-chain and treat a gap of silence as the end (FlushQuiet).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Bridge-owned copy of RimTalk's TalkType, so pure code never names a RimTalk enum.</summary>
    public enum BridgeTalkKind
    {
        Urgent, Hediff, LevelUp, Chitchat, Event, QuestOffer, QuestEnd, Thought, User, Other
    }

    /// <summary>Bridge-owned copy of RimTalk's InteractionType (the social flavor of a line).</summary>
    public enum BridgeSocialKind
    {
        None, Insult, Slight, Chat, Kind
    }

    /// <summary>One displayed RimTalk chat line, reduced to plain data.</summary>
    public sealed class ConversationLine
    {
        /// <summary>This line's id (Guid string; "" for an empty Guid).</summary>
        public string TalkId;

        /// <summary>Id of the line this replies to; "" when it opens a conversation.</summary>
        public string ParentTalkId;

        /// <summary>Speaker pawn load id.</summary>
        public string SpeakerId;

        /// <summary>Speaker display name (nickname), for the transcript.</summary>
        public string SpeakerName;

        /// <summary>Target pawn load id; "" for a monologue.</summary>
        public string TargetId;

        /// <summary>Target display name, so a partner can be named even if their pawn ref goes stale.</summary>
        public string TargetName;

        /// <summary>The rendered chat text.</summary>
        public string Text;

        /// <summary>Talk category (urgent, chitchat, event, ...).</summary>
        public BridgeTalkKind Kind;

        /// <summary>Social flavor (insult, kind, ...).</summary>
        public BridgeSocialKind Social;

        /// <summary>Game tick the line was displayed.</summary>
        public int Tick;
    }

    /// <summary>A reply-linked group of lines: one back-and-forth exchange.</summary>
    public sealed class Conversation
    {
        /// <summary>Id of the opening line (or a synthetic id when the opener had none).</summary>
        public string RootTalkId;

        /// <summary>Lines in arrival order.</summary>
        public List<ConversationLine> Lines = new List<ConversationLine>();

        /// <summary>Tick of the first line.</summary>
        public int FirstTick;

        /// <summary>Tick of the most recent line (drives the quiet-window flush).</summary>
        public int LastTick;

        /// <summary>
        /// Distinct participant ids (speakers and targets), in first-appearance order. Blank ids are
        /// skipped. A monologue yields a single id.
        /// </summary>
        public List<string> ParticipantIds()
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < Lines.Count; i++)
            {
                ConversationLine line = Lines[i];
                if (line == null)
                {
                    continue;
                }

                AddDistinct(ids, line.SpeakerId);
                AddDistinct(ids, line.TargetId);
            }

            return ids;
        }

        /// <summary>Number of lines spoken by <paramref name="speakerId"/>.</summary>
        public int LineCountFor(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i] != null && Lines[i].SpeakerId == speakerId)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AddDistinct(List<string> ids, string id)
        {
            if (string.IsNullOrEmpty(id) || ids.Contains(id))
            {
                return;
            }

            ids.Add(id);
        }
    }

    /// <summary>
    /// Groups incoming lines into conversations by reply-chain and hands them back when they go quiet.
    /// Not thread-safe: the impure caller records and flushes from the main thread only.
    /// </summary>
    public sealed class ConversationAssembler
    {
        // A single conversation should never grow without bound. If a reply chain runs past this many
        // lines it is force-flushed so memory and transcript size stay bounded (a runaway chain, e.g. a
        // scripted argument loop, must not accumulate forever between quiet-window passes).
        private const int MaxLinesPerConversation = 64;

        // Every seen line id -> the root id of its conversation, so a reply can find its group.
        private readonly Dictionary<string, string> talkIdToRoot = new Dictionary<string, string>();

        // Active (not yet flushed) conversations by root id.
        private readonly Dictionary<string, Conversation> byRoot = new Dictionary<string, Conversation>();

        // Conversations force-flushed by the runaway cap, waiting to be drained by the next Flush call.
        private readonly List<Conversation> forceFlushed = new List<Conversation>();

        // Counter for synthesizing root ids when an opening line has no id of its own.
        private int syntheticCounter;

        /// <summary>Records one line, joining its reply-chain or starting a new conversation.</summary>
        public void Record(ConversationLine line)
        {
            if (line == null)
            {
                return;
            }

            string talkId = line.TalkId ?? string.Empty;
            string parentId = line.ParentTalkId ?? string.Empty;

            string root;
            if (parentId.Length == 0 || !talkIdToRoot.TryGetValue(parentId, out root))
            {
                // No known parent: this opens a new conversation. Use the line's own id as the root,
                // or a synthetic id when it has none (so it still groups its own future replies... it
                // cannot have any without an id, but this keeps every conversation keyed uniquely).
                root = talkId.Length > 0 ? talkId : "synthetic:" + (++syntheticCounter);
            }

            Conversation conversation;
            if (!byRoot.TryGetValue(root, out conversation))
            {
                conversation = new Conversation
                {
                    RootTalkId = root,
                    FirstTick = line.Tick,
                    LastTick = line.Tick
                };
                byRoot[root] = conversation;
            }

            conversation.Lines.Add(line);
            if (line.Tick < conversation.FirstTick)
            {
                conversation.FirstTick = line.Tick;
            }

            if (line.Tick > conversation.LastTick)
            {
                conversation.LastTick = line.Tick;
            }

            // Map this line's id to the root so replies to it join this conversation.
            if (talkId.Length > 0)
            {
                talkIdToRoot[talkId] = root;
            }

            if (conversation.Lines.Count >= MaxLinesPerConversation)
            {
                RemoveConversation(root, conversation);
                forceFlushed.Add(conversation);
            }
        }

        /// <summary>
        /// Removes and returns every conversation that has been quiet for at least
        /// <paramref name="quietTicks"/> ticks, plus any force-flushed runaway conversations.
        /// </summary>
        public List<Conversation> FlushQuiet(int nowTick, int quietTicks)
        {
            List<Conversation> ready = DrainForceFlushed();

            List<string> quietRoots = null;
            foreach (KeyValuePair<string, Conversation> pair in byRoot)
            {
                if (nowTick - pair.Value.LastTick >= quietTicks)
                {
                    if (quietRoots == null)
                    {
                        quietRoots = new List<string>();
                    }

                    quietRoots.Add(pair.Key);
                }
            }

            if (quietRoots != null)
            {
                for (int i = 0; i < quietRoots.Count; i++)
                {
                    string root = quietRoots[i];
                    Conversation conversation = byRoot[root];
                    RemoveConversation(root, conversation);
                    ready.Add(conversation);
                }
            }

            return ready;
        }

        /// <summary>Removes and returns all conversations, active and force-flushed. Clears all state.</summary>
        public List<Conversation> FlushAll()
        {
            List<Conversation> all = DrainForceFlushed();
            foreach (KeyValuePair<string, Conversation> pair in byRoot)
            {
                all.Add(pair.Value);
            }

            byRoot.Clear();
            talkIdToRoot.Clear();
            return all;
        }

        private List<Conversation> DrainForceFlushed()
        {
            List<Conversation> drained = new List<Conversation>(forceFlushed);
            forceFlushed.Clear();
            return drained;
        }

        // Removes a conversation from the active map AND prunes its line ids from talkIdToRoot, so the
        // index cannot grow unbounded over a long session.
        private void RemoveConversation(string root, Conversation conversation)
        {
            byRoot.Remove(root);
            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line != null && !string.IsNullOrEmpty(line.TalkId))
                {
                    talkIdToRoot.Remove(line.TalkId);
                }
            }
        }
    }
}
