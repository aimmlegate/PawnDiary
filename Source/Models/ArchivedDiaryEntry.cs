// Compact, display-only archive row for one pawn's old diary page. A full DiaryEvent carries prompts,
// raw responses, retry state, title state, and multi-POV generation data because it is still active in
// the capture/generation pipeline. ArchivedDiaryEntry deliberately keeps only what the Diary tab needs
// to draw an old page after retention removes the heavy hot event.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable"). This model is persisted by
// DiaryArchiveRepository under the diaryArchiveEntries Scribe key.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One compact archived diary page for one pawn POV. Pair events archive as two rows when both
    /// pawns' POVs age out; each row shares the event id but has its own pawn id and POV role.
    /// </summary>
    public class ArchivedDiaryEntry : IExposable
    {
        public string eventId;
        public List<int> playLogEntryIds = new List<int>();
        public string pawnId;
        public string povRole;
        public int tick;
        public string date;
        public int year = UnknownYear;

        public string text;
        public string generatedText;
        public string status;
        public string llmModel;
        public string title;
        public bool archivedGenerationStale;

        public string groupLabel;
        public string interactionDefName;
        public string interactionLabel;
        public string colorCue;
        public string atmosphereCue;
        public bool important;
        public int staggeredIntensity;
        public bool distortDirectSpeech;
        public string textDecorationFacts;
        public string decorationDomain;
        public string decorationGameContext;
        public bool arrivalDescription;
        public bool deathDescription;

        public string linkedPawnId;
        public string linkedPawnName;
        public string linkedRole;
        public string linkedPreviewText;
        public bool linkedGenerated;
        public string linkedTitle;

        private const int UnknownYear = int.MinValue;

        /// <summary>Stable archive identity: one displayed page per event/pawn/role.</summary>
        public string ArchiveKey
        {
            get { return BuildArchiveKey(eventId, pawnId, povRole); }
        }

        public bool HasGeneratedText
        {
            get { return !string.IsNullOrWhiteSpace(generatedText); }
        }

        /// <summary>
        /// Creates a compact archive row from the same display view the Diary tab uses today. The caller
        /// decides that this row is safe to archive before calling this method.
        /// </summary>
        internal static ArchivedDiaryEntry FromEvent(DiaryEvent diaryEvent, string pawnId, DiaryEntryView view, bool forceFallback)
        {
            if (diaryEvent == null || view == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            DiaryTextDecorationContext decoration = view.TextDecorationContext;
            LinkedEntryView link = view.LinkedEntry;

            // A failed/stale page (forceFallback) keeps no generated prose, and compaction drops the raw
            // prompt the live card used to build its fallback body/title. Bake that SAME fact into text
            // now, while the prompt is still here, so the archived page renders identically afterward
            // (ArchivedGenerationFallbackFact falls back to text once the prompt is gone). Completed
            // pages keep their untouched raw event text.
            string archivedText = forceFallback
                ? DiaryArchiveFallback.ResolveFact(view.LlmPrompt, view.Text)
                : view.Text;

            return new ArchivedDiaryEntry
            {
                eventId = view.EventId,
                playLogEntryIds = diaryEvent.playLogEntryIds == null
                    ? new List<int>()
                    : new List<int>(diaryEvent.playLogEntryIds),
                pawnId = pawnId,
                povRole = view.PovRole,
                tick = view.Tick,
                date = view.Date,
                year = DiarySaveNormalization.ExtractYear(view.Date),
                text = archivedText,
                generatedText = view.GeneratedText,
                status = string.IsNullOrWhiteSpace(view.LlmStatus)
                    ? (string.IsNullOrWhiteSpace(view.GeneratedText) ? DiaryEvent.NotGeneratedStatus : DiaryEvent.CompleteStatus)
                    : view.LlmStatus,
                llmModel = view.LlmModel,
                title = view.Title,
                archivedGenerationStale = forceFallback || view.ArchivedGenerationStale,
                groupLabel = view.GroupLabel,
                interactionDefName = diaryEvent.interactionDefName,
                interactionLabel = diaryEvent.interactionLabel,
                colorCue = view.ColorCue,
                atmosphereCue = view.AtmosphereCue,
                important = view.Important,
                staggeredIntensity = view.StaggeredIntensity,
                distortDirectSpeech = view.DistortDirectSpeech,
                textDecorationFacts = DiaryTextDecorations.SerializePawnFacts(decoration),
                decorationDomain = decoration?.domain ?? string.Empty,
                decorationGameContext = decoration?.gameContext ?? string.Empty,
                arrivalDescription = diaryEvent.IsArrivalDescriptionFor(pawnId),
                deathDescription = diaryEvent.IsDeathDescriptionFor(pawnId),
                linkedPawnId = link?.OtherPawnId ?? string.Empty,
                linkedPawnName = link?.OtherPawnName ?? string.Empty,
                linkedRole = link?.OtherRole ?? string.Empty,
                linkedPreviewText = link?.TruncatedText ?? string.Empty,
                linkedGenerated = link != null && link.Generated,
                linkedTitle = link?.Title ?? string.Empty
            };
        }

        /// <summary>Builds the immutable UI view used by the Diary tab.</summary>
        internal DiaryEntryView ToView()
        {
            LinkedEntryView link = null;
            if (!string.IsNullOrWhiteSpace(linkedPawnId) || !string.IsNullOrWhiteSpace(linkedPreviewText))
            {
                link = new LinkedEntryView(
                    linkedPawnId,
                    linkedPawnName,
                    linkedRole,
                    eventId,
                    linkedPreviewText,
                    linkedGenerated,
                    linkedTitle);
            }

            DiaryTextDecorationContext decoration = new DiaryTextDecorationContext
            {
                povRole = povRole,
                defName = interactionDefName,
                colorCue = colorCue,
                atmosphereCue = atmosphereCue,
                domain = decorationDomain,
                gameContext = decorationGameContext
            };
            DiaryTextDecorations.AddEventTagsFromContext(decoration, decorationGameContext);
            DiaryTextDecorations.AddSerializedPawnFacts(decoration, textDecorationFacts);

            return new DiaryEntryView(
                tick,
                date,
                text,
                generatedText,
                status,
                string.Empty,
                string.Empty,
                llmModel,
                string.Empty,
                eventId,
                povRole,
                groupLabel,
                colorCue,
                atmosphereCue,
                staggeredIntensity,
                distortDirectSpeech,
                important,
                link,
                title,
                false,
                string.Empty,
                decoration,
                archivedGenerationStale,
                true,
                deathDescription ? 1 : (arrivalDescription ? -1 : 0));
        }

        public bool IsArrivalDescriptionFor(string requestedPawnId)
        {
            return arrivalDescription && string.Equals(pawnId, requestedPawnId, StringComparison.Ordinal);
        }

        public bool IsDeathDescriptionFor(string requestedPawnId)
        {
            return deathDescription && string.Equals(pawnId, requestedPawnId, StringComparison.Ordinal);
        }

        public bool MatchesPlayLogEntry(int playLogEntryId)
        {
            return playLogEntryId >= 0 && playLogEntryIds != null && playLogEntryIds.Contains(playLogEntryId);
        }

        public static string BuildArchiveKey(string eventId, string pawnId, string povRole)
        {
            return (eventId ?? string.Empty) + "|" + (pawnId ?? string.Empty) + "|" + (povRole ?? string.Empty);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventId, "eventId");
            Scribe_Collections.Look(ref playLogEntryIds, "playLogEntryIds", LookMode.Value);
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref povRole, "povRole");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref year, "year", UnknownYear);

            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref generatedText, "generatedText");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref llmModel, "llmModel");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref archivedGenerationStale, "archivedGenerationStale", false);

            Scribe_Values.Look(ref groupLabel, "groupLabel");
            Scribe_Values.Look(ref interactionDefName, "interactionDefName");
            Scribe_Values.Look(ref interactionLabel, "interactionLabel");
            Scribe_Values.Look(ref colorCue, "colorCue");
            Scribe_Values.Look(ref atmosphereCue, "atmosphereCue");
            Scribe_Values.Look(ref important, "important", true);
            Scribe_Values.Look(ref staggeredIntensity, "staggeredIntensity", 0);
            Scribe_Values.Look(ref distortDirectSpeech, "distortDirectSpeech", false);
            Scribe_Values.Look(ref textDecorationFacts, "textDecorationFacts");
            Scribe_Values.Look(ref decorationDomain, "decorationDomain");
            Scribe_Values.Look(ref decorationGameContext, "decorationGameContext");
            Scribe_Values.Look(ref arrivalDescription, "arrivalDescription", false);
            Scribe_Values.Look(ref deathDescription, "deathDescription", false);

            Scribe_Values.Look(ref linkedPawnId, "linkedPawnId");
            Scribe_Values.Look(ref linkedPawnName, "linkedPawnName");
            Scribe_Values.Look(ref linkedRole, "linkedRole");
            Scribe_Values.Look(ref linkedPreviewText, "linkedPreviewText");
            Scribe_Values.Look(ref linkedGenerated, "linkedGenerated", false);
            Scribe_Values.Look(ref linkedTitle, "linkedTitle");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeOnLoad();
            }
        }

        // Post-load cleanup. Every field is a plain string/int/bool null-coalesce plus status
        // normalization (DiaryGenerationStatus) and year extraction. All of it delegates to the pure
        // DiarySaveNormalization helpers so the regression-prone default/derive branches can be tested
        // without RimWorld. See DOCUMENTATION.md §9.
        private void NormalizeOnLoad()
        {
            eventId = DiarySaveNormalization.NormalizeString(eventId);
            if (playLogEntryIds == null)
            {
                playLogEntryIds = new List<int>();
            }

            pawnId = DiarySaveNormalization.NormalizeString(pawnId);
            povRole = DiarySaveNormalization.NormalizeString(povRole);
            date = DiarySaveNormalization.NormalizeString(date);
            text = DiarySaveNormalization.NormalizeString(text);
            generatedText = DiarySaveNormalization.NormalizeString(generatedText);
            status = DiaryGenerationStatus.NormalizeLoadedMainStatus(status, generatedText);
            llmModel = DiarySaveNormalization.NormalizeString(llmModel);
            title = DiarySaveNormalization.NormalizeString(title);
            groupLabel = DiarySaveNormalization.NormalizeString(groupLabel);
            interactionDefName = DiarySaveNormalization.NormalizeString(interactionDefName);
            interactionLabel = DiarySaveNormalization.NormalizeString(interactionLabel);
            colorCue = DiarySaveNormalization.NormalizeString(colorCue);
            atmosphereCue = DiarySaveNormalization.NormalizeString(atmosphereCue);
            textDecorationFacts = DiarySaveNormalization.NormalizeString(textDecorationFacts);
            decorationDomain = DiarySaveNormalization.NormalizeString(decorationDomain);
            decorationGameContext = DiarySaveNormalization.NormalizeString(decorationGameContext);
            linkedPawnId = DiarySaveNormalization.NormalizeString(linkedPawnId);
            linkedPawnName = DiarySaveNormalization.NormalizeString(linkedPawnName);
            linkedRole = DiarySaveNormalization.NormalizeString(linkedRole);
            linkedPreviewText = DiarySaveNormalization.NormalizeString(linkedPreviewText);
            linkedTitle = DiarySaveNormalization.NormalizeString(linkedTitle);
            if (year == UnknownYear && !string.IsNullOrWhiteSpace(date))
            {
                year = DiarySaveNormalization.ExtractYear(date);
            }
        }
    }
}
