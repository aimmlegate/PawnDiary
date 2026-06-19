// Generated direct-speech PlayLog injection. This is an impure RimWorld adapter: it resolves saved
// pawn IDs back to live Pawn objects, creates a fresh PlayLogEntry_Interaction after LLM generation
// succeeds, and remembers the generated display text for the Harmony patch in DiaryPatches.cs.
using System;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// If the opt-in setting is enabled, creates one fresh Social-log row for the first parsed
        /// initiator direct-speech block in a completed main diary entry.
        /// </summary>
        private void TryInjectGeneratedSpeechPlayLogEntry(DiaryEvent diaryEvent, LlmGenerationResult result)
        {
            if (diaryEvent == null
                || result == null
                || !result.success
                || !DiaryEvent.RoleEquals(result.povRole, DiaryEvent.InitiatorRole)
                || PawnDiaryMod.Settings == null
                || !PawnDiaryMod.Settings.injectGeneratedSpeechToPlayLog
                || diaryEvent.HasGeneratedSpeechPlayLogEntry())
            {
                return;
            }

            string speech = DiaryDirectSpeechParser.FirstDirectSpeechBlock(
                result.generatedText,
                SpeechBlockOpenMarker(),
                SpeechBlockCloseMarker());
            if (string.IsNullOrWhiteSpace(speech))
            {
                return;
            }

            // The generated row is intentionally a social interaction row, so require the original
            // event to still resolve to two pawns and an InteractionDef.
            if (diaryEvent.solo
                || string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId)
                || string.IsNullOrWhiteSpace(diaryEvent.interactionDefName))
            {
                return;
            }

            InteractionDef interactionDef = DefDatabase<InteractionDef>.GetNamedSilentFail(diaryEvent.interactionDefName);
            Pawn initiator = FindLivePawnByLoadId(diaryEvent.initiatorPawnId);
            Pawn recipient = FindLivePawnByLoadId(diaryEvent.recipientPawnId);
            if (interactionDef == null || initiator == null || recipient == null)
            {
                return;
            }

            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(interactionDef, initiator, recipient);
            if (entry == null)
            {
                return;
            }

            int playLogEntryId;
            if (GeneratedSpeechPlayLog.TryAdd(entry, speech, out playLogEntryId))
            {
                RememberGeneratedSpeechPlayLogEntry(playLogEntryId, speech);
                diaryEvent.MarkGeneratedSpeechPlayLogEntry(playLogEntryId);
            }
        }

        /// <summary>
        /// Persists the text that our PlayLogEntry_Interaction display patch should return for a
        /// generated speech row.
        /// </summary>
        private void RememberGeneratedSpeechPlayLogEntry(int playLogEntryId, string speech)
        {
            if (playLogEntryId < 0 || string.IsNullOrWhiteSpace(speech))
            {
                return;
            }

            if (generatedSpeechPlayLogTexts == null)
            {
                generatedSpeechPlayLogTexts = new System.Collections.Generic.Dictionary<int, string>();
            }

            generatedSpeechPlayLogTexts[playLogEntryId] = speech.Trim();
        }

        /// <summary>
        /// Returns the generated display text for a synthetic PlayLog interaction row, or empty
        /// string for normal vanilla rows.
        /// </summary>
        public string GeneratedSpeechTextForPlayLogEntry(int playLogEntryId)
        {
            if (playLogEntryId < 0 || generatedSpeechPlayLogTexts == null)
            {
                return string.Empty;
            }

            string text;
            return generatedSpeechPlayLogTexts.TryGetValue(playLogEntryId, out text) ? text ?? string.Empty : string.Empty;
        }

        private static string SpeechBlockOpenMarker()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            return string.IsNullOrWhiteSpace(style?.speechBlockOpenMarker)
                ? DiaryDirectSpeechParser.DefaultOpenMarker
                : style.speechBlockOpenMarker;
        }

        private static string SpeechBlockCloseMarker()
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            return string.IsNullOrWhiteSpace(style?.speechBlockCloseMarker)
                ? DiaryDirectSpeechParser.DefaultCloseMarker
                : style.speechBlockCloseMarker;
        }
    }
}
