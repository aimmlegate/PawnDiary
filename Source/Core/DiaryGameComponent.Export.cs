// Dev-only full-save export for diary data. This is intentionally an edge adapter: it reads the
// saved GameComponent state and writes a UTF-8 text file under RimWorld's save-data folder. Normal
// gameplay never calls it; the settings UI exposes it only when RimWorld Dev Mode is enabled.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string DevExportFolderName = "PawnDiaryExports";

        /// <summary>
        /// Dev-mode helper used by the settings window to write every saved diary page and backing
        /// event record to a timestamped text file. Returns false instead of throwing so the UI can
        /// show a normal RimWorld message when the OS blocks the write.
        /// </summary>
        public bool TryExportAllDiariesForDev(out string filePath, out string error)
        {
            filePath = string.Empty;
            error = string.Empty;

            if (!Prefs.DevMode)
            {
                error = "RimWorld Dev Mode is disabled.";
                return false;
            }

            try
            {
                string exportFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, DevExportFolderName);
                Directory.CreateDirectory(exportFolder);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                filePath = Path.Combine(exportFolder, "PawnDiary-" + stamp + ".txt");
                File.WriteAllText(filePath, BuildAllDiariesExportText(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                filePath = string.Empty;
                return false;
            }
        }

        private string BuildAllDiariesExportText()
        {
            StringBuilder sb = new StringBuilder(32768);
            IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;

            sb.AppendLine("Pawn Diary full export");
            AppendField(sb, "Generated local time", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            AppendField(sb, "Current game tick", Find.TickManager != null ? Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendField(sb, "Current game date", CurrentGameDateForExport());
            AppendField(sb, "Pawn diary records", diaries != null ? diaries.Count.ToString(CultureInfo.InvariantCulture) : "0");
            AppendField(sb, "Saved events", allEvents != null ? allEvents.Count.ToString(CultureInfo.InvariantCulture) : "0");
            AppendField(sb, "Archived pages", archive.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            AppendPawnDiaryExports(sb);
            AppendEventRecordExports(sb, allEvents);
            return sb.ToString();
        }

        private void AppendPawnDiaryExports(StringBuilder sb)
        {
            sb.AppendLine("== Pawn diaries ==");

            if (diaries == null || diaries.Count == 0)
            {
                sb.AppendLine("(no pawn diary records)");
                sb.AppendLine();
                return;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null)
                {
                    continue;
                }

                sb.AppendLine();
                AppendField(sb, "Pawn", SafeText(diary.pawnName));
                AppendField(sb, "Pawn id", SafeText(diary.pawnId));
                AppendField(sb, "Writing style def", SafeText(diary.personaDefName));
                AppendField(sb, "Generation enabled", diary.diaryGenerationEnabled.ToString(CultureInfo.InvariantCulture));
                AppendField(sb, "Unread generated entry", diary.hasUnreadGeneratedEntry.ToString(CultureInfo.InvariantCulture));
                int hotCount = diary.eventIds != null ? diary.eventIds.Count : 0;
                IReadOnlyList<ArchivedDiaryEntry> archivedEntries = archive.EntriesForPawn(diary.pawnId);
                AppendField(sb, "Hot event references", hotCount.ToString(CultureInfo.InvariantCulture));
                AppendField(sb, "Archived pages", archivedEntries.Count.ToString(CultureInfo.InvariantCulture));

                if (hotCount == 0 && archivedEntries.Count == 0)
                {
                    sb.AppendLine("  (no pages)");
                    continue;
                }

                int pageNumber = 1;
                for (int j = 0; j < hotCount; j++)
                {
                    string eventId = diary.eventIds[j];
                    DiaryEvent diaryEvent = events.FindEvent(eventId);
                    DiaryEntryView view = diaryEvent != null ? diaryEvent.ToViewFor(diary.pawnId) : null;

                    sb.AppendLine();
                    sb.Append("  Page ").Append(pageNumber++).AppendLine();
                    if (diaryEvent == null)
                    {
                        AppendField(sb, "    Missing event id", SafeText(eventId));
                        continue;
                    }

                    AppendField(sb, "    Event id", SafeText(diaryEvent.eventId));
                    AppendField(sb, "    Tick", diaryEvent.tick.ToString(CultureInfo.InvariantCulture));
                    AppendField(sb, "    Date", SafeText(diaryEvent.date));
                    AppendField(sb, "    Def", SafeText(diaryEvent.interactionDefName));
                    AppendField(sb, "    Label", SafeText(diaryEvent.interactionLabel));

                    if (view == null)
                    {
                        AppendField(sb, "    POV", "(no view for pawn)");
                        continue;
                    }

                    AppendField(sb, "    POV", SafeText(view.PovRole));
                    AppendField(sb, "    Group", SafeText(view.GroupLabel));
                    AppendField(sb, "    Status", SafeText(view.LlmStatus));
                    AppendField(sb, "    Title", SafeText(view.Title));
                    AppendField(sb, "    Endpoint", SafeText(view.LlmEndpoint));
                    AppendField(sb, "    Model", SafeText(view.LlmModel));
                    AppendField(sb, "    Error", SafeText(view.LlmError));
                    AppendBlock(sb, "    Raw game text", view.Text);
                    AppendBlock(sb, "    Generated diary text", view.GeneratedText);
                    AppendBlock(sb, "    Prompt", view.LlmPrompt);
                    AppendBlock(sb, "    Raw LLM response", view.LlmRawResponse);

                    if (view.LinkedEntry != null)
                    {
                        AppendField(sb, "    Linked pawn", SafeText(view.LinkedEntry.OtherPawnName) + " (" + SafeText(view.LinkedEntry.OtherPawnId) + ")");
                        AppendField(sb, "    Linked role", SafeText(view.LinkedEntry.OtherRole));
                        AppendField(sb, "    Linked title", SafeText(view.LinkedEntry.Title));
                    }
                }

                for (int j = 0; j < archivedEntries.Count; j++)
                {
                    ArchivedDiaryEntry archivedEntry = archivedEntries[j];
                    DiaryEntryView view = archivedEntry?.ToView();
                    if (view == null)
                    {
                        continue;
                    }

                    sb.AppendLine();
                    sb.Append("  Page ").Append(pageNumber++).AppendLine(" (archived)");
                    AppendField(sb, "    Event id", SafeText(view.EventId));
                    AppendField(sb, "    Tick", view.Tick.ToString(CultureInfo.InvariantCulture));
                    AppendField(sb, "    Date", SafeText(view.Date));
                    AppendField(sb, "    Def", SafeText(archivedEntry.interactionDefName));
                    AppendField(sb, "    Label", SafeText(archivedEntry.interactionLabel));
                    AppendField(sb, "    POV", SafeText(view.PovRole));
                    AppendField(sb, "    Group", SafeText(view.GroupLabel));
                    AppendField(sb, "    Status", SafeText(view.LlmStatus));
                    AppendField(sb, "    Title", SafeText(view.Title));
                    AppendField(sb, "    Model", SafeText(view.LlmModel));
                    AppendBlock(sb, "    Raw game text", view.Text);
                    AppendBlock(sb, "    Generated diary text", view.GeneratedText);

                    if (view.LinkedEntry != null)
                    {
                        AppendField(sb, "    Linked pawn", SafeText(view.LinkedEntry.OtherPawnName) + " (" + SafeText(view.LinkedEntry.OtherPawnId) + ")");
                        AppendField(sb, "    Linked role", SafeText(view.LinkedEntry.OtherRole));
                        AppendField(sb, "    Linked title", SafeText(view.LinkedEntry.Title));
                    }
                }
            }

            sb.AppendLine();
        }

        private void AppendEventRecordExports(StringBuilder sb, IReadOnlyList<DiaryEvent> allEvents)
        {
            sb.AppendLine("== Saved event records ==");

            if (allEvents == null || allEvents.Count == 0)
            {
                sb.AppendLine("(no saved events)");
                return;
            }

            for (int i = 0; i < allEvents.Count; i++)
            {
                DiaryEvent diaryEvent = allEvents[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                sb.AppendLine();
                sb.Append("Event ").Append(i + 1).AppendLine();
                AppendField(sb, "  Event id", SafeText(diaryEvent.eventId));
                AppendField(sb, "  Tick", diaryEvent.tick.ToString(CultureInfo.InvariantCulture));
                AppendField(sb, "  Date", SafeText(diaryEvent.date));
                AppendField(sb, "  Def", SafeText(diaryEvent.interactionDefName));
                AppendField(sb, "  PlayLog def", SafeText(diaryEvent.playLogInteractionDefName));
                AppendField(sb, "  Label", SafeText(diaryEvent.interactionLabel));
                AppendField(sb, "  Solo", diaryEvent.solo.ToString(CultureInfo.InvariantCulture));
                AppendField(sb, "  Color cue", SafeText(diaryEvent.colorCue));
                AppendField(sb, "  Mood impact", SafeText(diaryEvent.moodImpact));
                AppendField(sb, "  Generated speech log id", diaryEvent.generatedSpeechPlayLogEntryId.ToString(CultureInfo.InvariantCulture));
                AppendField(sb, "  PlayLog entry ids", JoinIntList(diaryEvent.playLogEntryIds));
                AppendBlock(sb, "  Game context", diaryEvent.gameContext);
                AppendBlock(sb, "  Instruction", diaryEvent.instruction);

                AppendRoleRecord(sb, diaryEvent, DiaryEvent.InitiatorRole);
                AppendRoleRecord(sb, diaryEvent, DiaryEvent.RecipientRole);
                AppendRoleRecord(sb, diaryEvent, DiaryEvent.NeutralRole);
            }
        }

        private static void AppendRoleRecord(StringBuilder sb, DiaryEvent diaryEvent, string role)
        {
            if (diaryEvent == null)
            {
                return;
            }

            string pawnId = RolePawnId(diaryEvent, role);
            string pawnName = RolePawnName(diaryEvent, role);
            string status = RoleStatus(diaryEvent, role);
            string title = diaryEvent.TitleForRole(role);
            string text = diaryEvent.TextForRole(role);
            string generated = diaryEvent.DisplayTextForRole(role);

            if (string.IsNullOrWhiteSpace(pawnId)
                && string.IsNullOrWhiteSpace(pawnName)
                && string.IsNullOrWhiteSpace(status)
                && string.IsNullOrWhiteSpace(title)
                && string.IsNullOrWhiteSpace(text)
                && string.IsNullOrWhiteSpace(generated))
            {
                return;
            }

            sb.Append("  ").Append(role).AppendLine(":");
            AppendField(sb, "    Pawn", SafeText(pawnName) + " (" + SafeText(pawnId) + ")");
            AppendField(sb, "    Status", SafeText(status));
            AppendField(sb, "    Title", SafeText(title));
            AppendField(sb, "    Title status", SafeText(RoleTitleStatus(diaryEvent, role)));
            AppendField(sb, "    Endpoint", SafeText(diaryEvent.LlmEndpointForRole(role)));
            AppendField(sb, "    Model", SafeText(diaryEvent.LlmModelForRole(role)));
            AppendBlock(sb, "    Raw text", text);
            if (!string.Equals(generated, text, StringComparison.Ordinal))
            {
                AppendBlock(sb, "    Display text", generated);
            }
        }

        private static string RolePawnId(DiaryEvent diaryEvent, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.InitiatorRole))
            {
                return diaryEvent.initiatorPawnId;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return diaryEvent.recipientPawnId;
            }

            return string.Empty;
        }

        private static string RolePawnName(DiaryEvent diaryEvent, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.InitiatorRole))
            {
                return diaryEvent.initiatorName;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return diaryEvent.recipientName;
            }

            return string.Empty;
        }

        private static string RoleStatus(DiaryEvent diaryEvent, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.InitiatorRole))
            {
                return diaryEvent.initiatorStatus;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return diaryEvent.recipientStatus;
            }

            return diaryEvent.neutralStatus;
        }

        private static string RoleTitleStatus(DiaryEvent diaryEvent, string role)
        {
            if (DiaryEvent.RoleEquals(role, DiaryEvent.InitiatorRole))
            {
                return diaryEvent.initiatorTitleStatus;
            }

            if (DiaryEvent.RoleEquals(role, DiaryEvent.RecipientRole))
            {
                return diaryEvent.recipientTitleStatus;
            }

            return diaryEvent.neutralTitleStatus;
        }

        private static string CurrentGameDateForExport()
        {
            return Find.TickManager != null
                ? GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero)
                : string.Empty;
        }

        private static string JoinIntList(List<int> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static void AppendField(StringBuilder sb, string label, string value)
        {
            sb.Append(label).Append(": ").Append(value ?? string.Empty).AppendLine();
        }

        private static void AppendBlock(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            sb.Append(label).AppendLine(":");
            using (StringReader reader = new StringReader(value))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    sb.Append("      ").AppendLine(line);
                }
            }
        }

        private static string SafeText(string value)
        {
            return value ?? string.Empty;
        }
    }
}
