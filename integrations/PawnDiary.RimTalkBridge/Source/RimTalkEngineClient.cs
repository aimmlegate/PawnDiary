// Engine mode (advanced, default OFF): route a conversation entry's generation through RimTalk's own
// configured AI provider instead of Pawn Diary's, so one API key can serve both mods.
//
// Flow (all failure paths fall back to the normal wrapped submit, so a conversation is NEVER lost):
//   1. PreviewPrompt(request) — the exact prompt Pawn Diary would send (main thread).
//   2. Build a RimTalk TalkRequest (Context = system prompt, Prompt = user prompt + a JSON directive).
//   3. AIService.Query<DiaryTextPayload>(req) — async on RimTalk's provider, returns null on any error.
//   4. On the ContinueWith (BACKGROUND) thread: extract plain strings only — NO PawnDiaryApi calls.
//   5. The bridge tick pass drains the queue on the MAIN thread and submits a direct entry (or, on
//      failure, the original wrapped request).
//
// RimTalk-type isolation: the methods that name RimTalk types (AIService, TalkRequest, IJsonData) are
// [NoInlining] and only reached when RimTalk is active. The main-thread drain names none, so it is
// plain Pawn Diary code.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using PawnDiary.Integration;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// The JSON payload RimTalk's provider is asked to return. Mirrors RimTalk's own IJsonData types:
    /// [DataContract] + [DataMember] so RimTalk's DataContractJsonSerializer maps the JSON keys. The
    /// key names here MUST match the keys named in the prompt directive below (case-sensitive).
    /// </summary>
    [DataContract]
    public sealed class DiaryTextPayload : IJsonData
    {
        /// <summary>Short diary title. May be null/empty; the direct entry then stays date-only.</summary>
        [DataMember(Name = "title")]
        public string title { get; set; }

        /// <summary>The first-person diary prose.</summary>
        [DataMember(Name = "text")]
        public string text { get; set; }

        /// <summary>IJsonData contract: RimTalk logs this as the response text.</summary>
        public string GetText()
        {
            return text ?? string.Empty;
        }
    }

    /// <summary>
    /// Bridges a conversation entry to RimTalk's AI provider and delivers the result back to Pawn Diary
    /// on the main thread.
    /// </summary>
    internal static class RimTalkEngineClient
    {
        // English JSON-schema directive appended to the user prompt. CARVE-OUT: this must stay an
        // English machine instruction (a strict JSON schema the model follows), not player-facing
        // prose, so it is intentionally NOT localized. The key names match DiaryTextPayload above.
        private const string JsonSuffix =
            "\n\nReturn ONLY a JSON object, nothing else, of exactly this shape: "
            + "{\"title\": \"a short title\", \"text\": \"the diary entry, first person\"}";

        // Completed engine results waiting to be submitted on the main thread. Concurrent because the
        // producer is a background Task continuation and the consumer is the game tick.
        private static readonly ConcurrentQueue<EngineResult> Results = new ConcurrentQueue<EngineResult>();

        /// <summary>
        /// Attempts to start an engine-backed generation for this conversation entry. Returns true when
        /// an async query was started (the result will arrive via <see cref="DrainResults"/>); returns
        /// false when the caller should submit through the normal path right now (no preview, provider
        /// busy, or any error). MAIN THREAD. Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool TryStartEngineSubmit(ExternalPromptEntryRequest request, Pawn subject, Pawn partner)
        {
            try
            {
                if (request == null)
                {
                    return false;
                }

                // The exact prompt Pawn Diary would use; must be read on the main thread.
                DiaryPromptPreviewSnapshot preview = PawnDiaryApi.PreviewPrompt(request);
                if (preview == null || string.IsNullOrEmpty(preview.userPrompt))
                {
                    return false;
                }

                // Don't pile onto RimTalk when its provider is already working; use the normal path.
                if (AIService.IsBusy())
                {
                    return false;
                }

                string userPrompt = preview.userPrompt + JsonSuffix;
                TalkRequest talkRequest = new TalkRequest(userPrompt, subject, partner, TalkType.Other);
                talkRequest.Context = preview.systemPrompt ?? string.Empty;

                Task<DiaryTextPayload> task = AIService.Query<DiaryTextPayload>(talkRequest);
                if (task == null)
                {
                    return false;
                }

                task.ContinueWith(completed => Enqueue(completed, request, subject, partner));
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " engine submit could not start; using the normal path: " + e,
                    "PawnDiaryRimTalkBridge.Engine.StartFailed".GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Submits every completed engine result. MAIN THREAD ONLY (PawnDiaryApi). Names no RimTalk
        /// types. A successful payload becomes a direct entry; a failed one falls back to the original
        /// wrapped request so the conversation is never dropped.
        /// </summary>
        public static void DrainResults()
        {
            EngineResult result;
            while (Results.TryDequeue(out result))
            {
                if (result == null)
                {
                    continue;
                }

                if (result.Success)
                {
                    // Engine mode writes the subject's entry only (one payload, one POV). The partner
                    // POV, if any, is intentionally not engine-generated in v1 — an accepted limitation
                    // of experimental engine mode.
                    PawnDiaryApi.SubmitDirectEntry(new ExternalDirectEntryRequest
                    {
                        sourceId = BridgeIds.ModId,
                        eventKey = BridgeIds.ConversationEventKey,
                        subject = result.Subject,
                        text = result.Text,
                        title = result.Title,
                        dedupKey = result.OriginalRequest != null ? result.OriginalRequest.dedupKey : null,
                        generateTitleIfMissing = false
                    });
                }
                else if (result.OriginalRequest != null)
                {
                    PawnDiaryApi.SubmitPromptEntry(result.OriginalRequest);
                }
            }
        }

        /// <summary>Discards pending results on new game load.</summary>
        public static void ResetForNewGame()
        {
            EngineResult ignored;
            while (Results.TryDequeue(out ignored))
            {
            }
        }

        // BACKGROUND thread continuation: extract plain strings from the payload and enqueue. Must not
        // call PawnDiaryApi (main-thread only). Names RimTalk types (DiaryTextPayload / Task<T>).
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Enqueue(Task<DiaryTextPayload> task, ExternalPromptEntryRequest request, Pawn subject, Pawn partner)
        {
            EngineResult result = new EngineResult
            {
                OriginalRequest = request,
                Subject = subject,
                Partner = partner
            };

            try
            {
                DiaryTextPayload payload = task != null && task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (payload != null && !string.IsNullOrWhiteSpace(payload.text))
                {
                    result.Success = true;
                    result.Title = payload.title;
                    result.Text = payload.text;
                }
            }
            catch
            {
                result.Success = false;
            }

            Results.Enqueue(result);
        }

        /// <summary>One engine outcome, carried across the background→main-thread boundary as plain data.</summary>
        private sealed class EngineResult
        {
            public ExternalPromptEntryRequest OriginalRequest;
            public Pawn Subject;
            public Pawn Partner;
            public bool Success;
            public string Title;
            public string Text;
        }
    }
}
