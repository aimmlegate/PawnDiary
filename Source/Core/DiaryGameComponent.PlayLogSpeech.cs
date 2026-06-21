// Generated direct-speech PlayLog injection. This is an impure RimWorld adapter: it resolves saved
// pawn IDs back to live Pawn objects, creates a fresh PlayLogEntry_Interaction after LLM generation
// succeeds, and remembers the generated display text for the Harmony patch in DiaryPatches.cs.
using System.Collections.Generic;
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
            // event to still resolve to two pawns and an InteractionDef. Combined batches store a
            // synthetic interactionDefName, so prefer the real def kept for injection and only fall
            // back to interactionDefName (covers direct interactions and pre-field saves).
            string rowDefName = string.IsNullOrWhiteSpace(diaryEvent.playLogInteractionDefName)
                ? diaryEvent.interactionDefName
                : diaryEvent.playLogInteractionDefName;
            if (diaryEvent.solo
                || string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId)
                || string.IsNullOrWhiteSpace(rowDefName))
            {
                return;
            }

            InteractionDef interactionDef = DefDatabase<InteractionDef>.GetNamedSilentFail(rowDefName);
            Pawn initiator = FindLivePawnByLoadId(diaryEvent.initiatorPawnId);
            Pawn recipient = FindLivePawnByLoadId(diaryEvent.recipientPawnId);
            if (interactionDef == null || initiator == null || recipient == null)
            {
                // Speech was parsed but the row could not be built. Surface why under Dev Mode so the
                // feature is diagnosable in-game (e.g. a non-InteractionDef def or a despawned pawn).
                if (Prefs.DevMode)
                {
                    Log.Message("[Pawn Diary] Speech parsed but Social-log row skipped (def='" + rowDefName
                        + "' interactionDef=" + (interactionDef != null) + ", initiator=" + (initiator != null)
                        + ", recipient=" + (recipient != null) + ").");
                }

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
                if (Prefs.DevMode)
                {
                    Log.Message("[Pawn Diary] Injected generated speech into Social log (def='" + rowDefName
                        + "', logId=" + playLogEntryId + ").");
                }
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
                generatedSpeechPlayLogTexts = new Dictionary<int, string>();
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

        /// <summary>
        /// True when this game holds at least one generated direct-speech row. The display patch
        /// reads this to skip its per-row lookup entirely in games that never created one.
        /// </summary>
        public bool HasGeneratedSpeechPlayLogTexts
        {
            get { return generatedSpeechPlayLogTexts != null && generatedSpeechPlayLogTexts.Count > 0; }
        }

        /// <summary>
        /// Drops generated-speech state whose PlayLog row RimWorld has already pruned. Called when
        /// loading/saving so stale LogIDs cannot grow the text map or block later re-injection.
        /// </summary>
        private void PruneStaleGeneratedSpeechPlayLogState()
        {
            bool hasTextMap = generatedSpeechPlayLogTexts != null && generatedSpeechPlayLogTexts.Count > 0;
            bool hasEventRows = HasRememberedGeneratedSpeechPlayLogEvents();
            if (!hasTextMap && !hasEventRows)
            {
                return;
            }

            // Without a readable PlayLog we cannot tell which rows are stale, so keep the map as-is
            // rather than risk dropping mappings whose rows are still live.
            List<LogEntry> entries = Find.PlayLog?.AllEntries;
            if (entries == null)
            {
                return;
            }

            HashSet<int> liveLogIds = new HashSet<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                LogEntry entry = entries[i];
                if (entry != null)
                {
                    liveLogIds.Add(entry.LogID);
                }
            }

            if (hasTextMap)
            {
                List<int> stale = null;
                foreach (KeyValuePair<int, string> pair in generatedSpeechPlayLogTexts)
                {
                    if (!liveLogIds.Contains(pair.Key))
                    {
                        (stale ?? (stale = new List<int>())).Add(pair.Key);
                    }
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        generatedSpeechPlayLogTexts.Remove(stale[i]);
                    }
                }
            }

            if (hasEventRows)
            {
                PruneStaleGeneratedSpeechPlayLogEventIds(liveLogIds);
            }
        }

        private bool HasRememberedGeneratedSpeechPlayLogEvents()
        {
            if (diaryEvents == null)
            {
                return false;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i] != null && diaryEvents[i].HasGeneratedSpeechPlayLogEntry())
                {
                    return true;
                }
            }

            return false;
        }

        private void PruneStaleGeneratedSpeechPlayLogEventIds(HashSet<int> liveLogIds)
        {
            if (diaryEvents == null || liveLogIds == null)
            {
                return;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent != null
                    && diaryEvent.HasGeneratedSpeechPlayLogEntry()
                    && !liveLogIds.Contains(diaryEvent.generatedSpeechPlayLogEntryId))
                {
                    diaryEvent.ClearGeneratedSpeechPlayLogEntry();
                }
            }
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
