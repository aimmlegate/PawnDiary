// Player-facing background and stored-memory sections for Dialog_PawnWritingStyle.
//
// At most one record is drawn at a time: the XML policy permits hundreds of memories per pawn, so
// rendering one editor for every row would make RimWorld's repeated IMGUI passes unnecessarily
// expensive. Older/Newer and a lazy FloatMenu provide access to the full list. Persistent changes
// happen only from explicit profile Save, memory Save, or confirmed Remove clicks through guarded
// DiaryGameComponent endpoints. Raw matching metadata remains visible only in Dev Mode.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_PawnWritingStyle
    {
        // Background is part of the main profile draft. Opening/repainting the dialog uses the
        // component's no-create getter; only the profile Save button can create/update/remove it.
        private string backgroundMemoryBuffer = string.Empty;
        private string originalBackgroundMemory = string.Empty;
        private int backgroundMemoryMaxChars;
        private Vector2 backgroundMemoryScroll;

        private string selectedMemoryRecordId = string.Empty;
        private string memoryEditBuffer = string.Empty;
        // The rendered text shown when Edit began. Comparing canonical text against this baseline makes
        // Edit -> Save without changes a true no-op instead of freezing a localized XML template.
        private string memoryEditStartRenderedText = string.Empty;
        private bool memoryEditing;
        private Vector2 memoryTextScroll;
        // One detached editor snapshot per dialog lifetime. Deep-copying hundreds of participant/fact
        // rows on every IMGUI event would be needless work; explicit mutations refresh this cache.
        private IReadOnlyList<ImportantMemoryRecordSnapshot> profileMemorySnapshots =
            new List<ImportantMemoryRecordSnapshot>();

        /// <summary>
        /// Seeds the detached background draft without creating a diary or knowledge state.
        /// </summary>
        private void SeedMemoryDrafts()
        {
            backgroundMemoryBuffer = component == null
                ? string.Empty
                : component.BackgroundMemoryForProfile(pawn);
            backgroundMemoryBuffer = PlayerMemoryPolicy.NormalizePlayerText(
                backgroundMemoryBuffer);
            originalBackgroundMemory = backgroundMemoryBuffer;
            backgroundMemoryMaxChars = component == null
                ? 0
                : component.BackgroundMemoryTextLimitForProfile();
            RefreshMemorySnapshots();
        }

        private void RefreshMemorySnapshots()
        {
            profileMemorySnapshots = component == null
                ? (IReadOnlyList<ImportantMemoryRecordSnapshot>)
                    new List<ImportantMemoryRecordSnapshot>()
                : component.ImportantMemoriesForProfile(pawn);
            if (profileMemorySnapshots == null)
            {
                profileMemorySnapshots = new List<ImportantMemoryRecordSnapshot>();
            }
        }

        /// <summary>Draws the singleton player-authored background-memory draft.</summary>
        private void DrawBackgroundMemorySection(float x, float width, ref float y)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            Widgets.Label(
                new Rect(x, y, width, SectionTitleHeight),
                "PawnDiary.Profile.BackgroundSectionTitle".Translate());
            y += SectionTitleHeight + FieldGap;

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                BackgroundMemoryLabel(),
                backgroundMemoryBuffer,
                ref backgroundMemoryScroll,
                PromptAreaHeight,
                editable: true,
                editedText: text =>
                {
                    backgroundMemoryBuffer = ClampBackgroundDraft(text);
                }) + FieldGap;

            y += DrawMessagePanel(
                new Rect(x, y, width, 0f),
                "PawnDiary.Profile.BackgroundExplanation".Translate(),
                StatusPanelColor(false)) + FieldGap;
        }

        /// <summary>Mirrors the background section so the outer scroll view cannot clip it.</summary>
        private float BackgroundMemorySectionHeight(float width)
        {
            float height = SectionGap + FieldGap;
            height += SectionTitleHeight + FieldGap;
            height += LabeledScrollTextHeight(
                BackgroundMemoryLabel(),
                width,
                PromptAreaHeight) + FieldGap;
            height += MessagePanelHeight(
                "PawnDiary.Profile.BackgroundExplanation".Translate(),
                width);
            return height;
        }

        private string BackgroundMemoryLabel()
        {
            // Count the canonical one-line value, while retaining trailing spaces/newlines in the raw
            // TextArea draft so ordinary multi-word typing is not disrupted between keystrokes.
            int length = PlayerMemoryPolicy.NormalizePlayerText(backgroundMemoryBuffer).Length;
            return backgroundMemoryMaxChars > 0
                ? FormatMemoryFrame(
                    "PawnDiary.Profile.BackgroundEditLabel",
                    length,
                    backgroundMemoryMaxChars)
                : FormatMemoryFrame(
                    "PawnDiary.Profile.BackgroundEditLabelUnlimited",
                    length);
        }

        /// <summary>
        /// Applies the canonical background draft at the profile Save boundary. Equivalent whitespace
        /// edits make no call; a canonical blank delegates to the component's delete plan.
        /// </summary>
        private bool SaveBackgroundMemoryDraft()
        {
            string cleaned = PlayerMemoryPolicy.NormalizePlayerText(backgroundMemoryBuffer);
            if (backgroundMemoryMaxChars > 0 && cleaned.Length > backgroundMemoryMaxChars)
            {
                cleaned = TextTruncation.SafePrefix(cleaned, backgroundMemoryMaxChars);
            }

            if (string.Equals(cleaned, originalBackgroundMemory, StringComparison.Ordinal))
            {
                return true;
            }

            return component != null
                && component.TrySetBackgroundMemoryForProfile(pawn, cleaned);
        }

        private string ClampBackgroundDraft(string text)
        {
            string raw = text ?? string.Empty;
            if (backgroundMemoryMaxChars <= 0)
            {
                return raw;
            }

            string cleaned = PlayerMemoryPolicy.NormalizePlayerText(raw);
            return cleaned.Length <= backgroundMemoryMaxChars
                ? raw
                : TextTruncation.SafePrefix(cleaned, backgroundMemoryMaxChars);
        }

        /// <summary>
        /// Draws the normal-play memory selector and single-record editor. Matching diagnostics are
        /// optional and remain Dev-only; the editable prose and guarded actions are always available.
        /// </summary>
        private void DrawMemorySection(
            float x,
            float width,
            ref float y,
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            bool showDeveloperDiagnostics)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            int memoryCount = ValidMemoryCount(memories);
            string title = FormatMemoryFrame("PawnDiary.Profile.MemorySectionTitle", memoryCount);
            float titleHeight = SmallLabelHeight(title, width);
            Widgets.Label(new Rect(x, y, width, titleHeight), title);
            y += titleHeight + FieldGap;

            y += DrawMessagePanel(
                new Rect(x, y, width, 0f),
                "PawnDiary.Profile.MemoryExplanation".Translate(),
                StatusPanelColor(false)) + FieldGap;

            // Resolve the newest row as a read-only display fallback. Do not repair selection fields in
            // an IMGUI draw pass; only explicit navigation/edit actions may change local UI state.
            ImportantMemoryRecordSnapshot selected = SelectedMemoryOrNewest(memories);
            if (selected == null)
            {
                string empty = "PawnDiary.Profile.MemoryEmpty".Translate();
                float emptyHeight = SmallLabelHeight(empty, width);
                Widgets.Label(new Rect(x, y, width, emptyHeight), empty);
                y += emptyHeight + FieldGap;
                return;
            }

            int selectedIndex = FindMemoryIndex(memories, selected.recordId);
            DrawMemoryNavigation(
                new Rect(x, y, width, ButtonHeight),
                memories,
                selected,
                selectedIndex,
                memoryCount);
            y += ButtonHeight + FieldGap;

            string metadata = showDeveloperDiagnostics
                ? MemoryMetadata(selected)
                : MemoryDate(selected);
            float metadataHeight = SmallLabelHeight(metadata, width);
            Widgets.Label(new Rect(x, y, width, metadataHeight), metadata);
            y += metadataHeight + FieldGap;

            int textLimit = MemoryTextLimit();
            string actualText = RenderedMemoryText(selected, textLimit);
            bool editingSelected = IsEditingMemory(selected);
            string shownText = editingSelected
                ? memoryEditBuffer
                : MemoryTextForDisplay(actualText);
            string label = editingSelected
                ? MemoryEditLabel(textLimit)
                : "PawnDiary.Profile.MemoryTextLabel".Translate().ToString();

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                label,
                shownText,
                ref memoryTextScroll,
                PromptAreaHeight,
                editable: editingSelected,
                editedText: editingSelected
                    ? (Action<string>)(text =>
                    {
                        memoryEditBuffer = textLimit > 0
                            ? ClampInput(text, textLimit)
                            : (text ?? string.Empty);
                    })
                    : null) + FieldGap;

            DrawMemoryActions(new Rect(x, y, width, ButtonHeight), selected, actualText);
            y += ButtonHeight + FieldGap;
        }

        /// <summary>
        /// Mirrors <see cref="DrawMemorySection"/> exactly so the outer scroll view never clips the
        /// normal editor or leaves dead space when Dev Mode diagnostics are toggled.
        /// </summary>
        private float MemorySectionHeight(
            float width,
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            bool showDeveloperDiagnostics)
        {
            int memoryCount = ValidMemoryCount(memories);
            float height = SectionGap + FieldGap; // gap plus separator line
            height += SmallLabelHeight(
                FormatMemoryFrame("PawnDiary.Profile.MemorySectionTitle", memoryCount),
                width) + FieldGap;
            height += MessagePanelHeight(
                "PawnDiary.Profile.MemoryExplanation".Translate(),
                width);

            ImportantMemoryRecordSnapshot selected = SelectedMemoryOrNewest(memories);
            if (selected == null)
            {
                return height
                    + SmallLabelHeight("PawnDiary.Profile.MemoryEmpty".Translate(), width)
                    + FieldGap;
            }

            height += ButtonHeight + FieldGap;
            height += SmallLabelHeight(
                showDeveloperDiagnostics ? MemoryMetadata(selected) : MemoryDate(selected),
                width) + FieldGap;
            height += LabeledScrollTextHeight(
                IsEditingMemory(selected)
                    ? MemoryEditLabel(MemoryTextLimit())
                    : "PawnDiary.Profile.MemoryTextLabel".Translate().ToString(),
                width,
                PromptAreaHeight) + FieldGap;
            height += ButtonHeight + FieldGap;
            return height;
        }

        private void DrawMemoryNavigation(
            Rect rect,
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            ImportantMemoryRecordSnapshot selected,
            int selectedIndex,
            int memoryCount)
        {
            float unit = Mathf.Max(1f, (rect.width - FieldGap * 2f) / 4f);
            Rect olderRect = new Rect(rect.x, rect.y, unit, rect.height);
            Rect pickerRect = new Rect(
                olderRect.xMax + FieldGap,
                rect.y,
                unit * 2f,
                rect.height);
            Rect newerRect = new Rect(
                pickerRect.xMax + FieldGap,
                rect.y,
                unit,
                rect.height);

            int olderIndex = FindNextMemoryIndex(memories, selectedIndex - 1, -1);
            int newerIndex = FindNextMemoryIndex(memories, selectedIndex + 1, 1);
            bool navigationEnabled = !IsEditingMemory(selected);

            if (Widgets.ButtonText(
                olderRect,
                "PawnDiary.Profile.MemoryOlder".Translate(),
                true,
                true,
                navigationEnabled && olderIndex >= 0))
            {
                SelectMemory(memories[olderIndex]);
            }

            string selector = FormatMemoryFrame(
                "PawnDiary.Profile.MemorySelector",
                MemoryDisplayPosition(memories, selected.recordId),
                memoryCount);
            if (Widgets.ButtonText(
                pickerRect,
                selector,
                true,
                true,
                navigationEnabled))
            {
                OpenMemoryPicker(memories);
            }

            if (Widgets.ButtonText(
                newerRect,
                "PawnDiary.Profile.MemoryNewer".Translate(),
                true,
                true,
                navigationEnabled && newerIndex >= 0))
            {
                SelectMemory(memories[newerIndex]);
            }
        }

        private void DrawMemoryActions(
            Rect rect,
            ImportantMemoryRecordSnapshot record,
            string renderedText)
        {
            bool editingSelected = IsEditingMemory(record);
            bool canRemove = DiaryUiPolicy.ShouldOfferMemoryRemove(
                record?.eventKind,
                KnowledgeTokens.EventKindFactionJoined);
            int buttonCount = editingSelected
                ? (canRemove ? 3 : 2)
                : (canRemove ? 2 : 1);
            float buttonWidth = Mathf.Max(
                1f,
                (rect.width - FieldGap * (buttonCount - 1)) / buttonCount);

            Rect first = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect second = new Rect(first.xMax + FieldGap, rect.y, buttonWidth, rect.height);
            Rect third = new Rect(second.xMax + FieldGap, rect.y, buttonWidth, rect.height);

            if (!editingSelected)
            {
                if (Widgets.ButtonText(first, "PawnDiary.Profile.MemoryEdit".Translate()))
                {
                    BeginMemoryEdit(record, renderedText);
                }

                if (canRemove
                    && Widgets.ButtonText(second, "PawnDiary.Profile.MemoryRemove".Translate()))
                {
                    ConfirmMemoryRemoval(record, renderedText);
                }

                return;
            }

            if (Widgets.ButtonText(first, "PawnDiary.Profile.MemorySave".Translate()))
            {
                if (!TrySaveActiveMemoryDraft(showSuccessMessage: true))
                {
                    MemoryOperationFailed();
                }
            }

            if (Widgets.ButtonText(second, "PawnDiary.Profile.MemoryCancel".Translate()))
            {
                EndMemoryEdit();
            }

            if (canRemove
                && Widgets.ButtonText(third, "PawnDiary.Profile.MemoryRemove".Translate()))
            {
                ConfirmMemoryRemoval(record, renderedText);
            }
        }

        private void ConfirmMemoryRemoval(
            ImportantMemoryRecordSnapshot record,
            string renderedText)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.recordId))
            {
                MemoryOperationFailed();
                return;
            }

            // Persistence also guards this lifecycle row. Mirroring that rule here avoids ever opening
            // a confirmation dialog for an action that cannot succeed.
            if (!DiaryUiPolicy.ShouldOfferMemoryRemove(
                record.eventKind,
                KnowledgeTokens.EventKindFactionJoined))
            {
                return;
            }

            string recordId = record.recordId;
            string confirmation = FormatMemoryFrame(
                "PawnDiary.Profile.MemoryRemoveConfirm",
                MemoryTextForDisplay(renderedText));
            Dialog_MessageBox dialog = new Dialog_MessageBox(
                confirmation,
                "PawnDiary.Profile.MemoryRemove".Translate(),
                delegate
                {
                    if (component != null
                        && component.TryRemoveImportantMemoryForProfile(pawn, recordId))
                    {
                        if (string.Equals(
                            selectedMemoryRecordId,
                            recordId,
                            StringComparison.Ordinal))
                        {
                            selectedMemoryRecordId = string.Empty;
                        }

                        EndMemoryEdit();
                        RefreshMemorySnapshots();
                        Messages.Message(
                            "PawnDiary.Profile.MemoryRemoved".Translate(),
                            MessageTypeDefOf.NeutralEvent,
                            false);
                    }
                    else
                    {
                        MemoryOperationFailed();
                    }
                },
                "PawnDiary.Profile.MemoryCancel".Translate(),
                null,
                "PawnDiary.Profile.MemoryRemoveTitle".Translate());
            Find.WindowStack.Add(dialog);
        }

        private void OpenMemoryPicker(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (memories != null)
            {
                for (int i = memories.Count - 1; i >= 0; i--)
                {
                    ImportantMemoryRecordSnapshot record = memories[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.recordId))
                    {
                        continue;
                    }

                    string recordId = record.recordId;
                    string optionText = MemoryOptionLabel(record);
                    options.Add(new FloatMenuOption(optionText, delegate
                    {
                        SelectMemoryById(memories, recordId);
                    }));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private ImportantMemoryRecordSnapshot SelectedMemoryOrNewest(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories)
        {
            int selectedIndex = FindMemoryIndex(memories, selectedMemoryRecordId);
            if (selectedIndex >= 0)
            {
                return memories[selectedIndex];
            }

            int newestIndex = memories == null
                ? -1
                : FindNextMemoryIndex(memories, memories.Count - 1, -1);
            return newestIndex >= 0 ? memories[newestIndex] : null;
        }

        private void SelectMemoryById(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            string recordId)
        {
            int index = FindMemoryIndex(memories, recordId);
            if (index >= 0)
            {
                SelectMemory(memories[index]);
            }
        }

        private void SelectMemory(ImportantMemoryRecordSnapshot record)
        {
            if (record == null)
            {
                return;
            }

            selectedMemoryRecordId = record.recordId ?? string.Empty;
            EndMemoryEdit();
        }

        /// <summary>Starts a detached edit while remembering exactly what the player was shown.</summary>
        private void BeginMemoryEdit(ImportantMemoryRecordSnapshot record, string renderedText)
        {
            SelectMemory(record);
            memoryEditStartRenderedText = renderedText ?? string.Empty;
            memoryEditBuffer = memoryEditStartRenderedText;
            memoryEditing = true;
        }

        /// <summary>
        /// Commits the active detached draft for either the row-level button or the profile footer.
        /// Unchanged canonical text exits edit mode without touching save state, so a translated XML
        /// template stays dynamic. A blank changed draft still reaches the component and clears an
        /// existing override back to that template.
        /// </summary>
        private bool TrySaveActiveMemoryDraft(bool showSuccessMessage)
        {
            if (!memoryEditing)
            {
                return true;
            }

            int recordIndex = FindMemoryIndex(profileMemorySnapshots, selectedMemoryRecordId);
            if (recordIndex < 0 || component == null)
            {
                return false;
            }

            ImportantMemoryRecordSnapshot record = profileMemorySnapshots[recordIndex];
            int textLimit = MemoryTextLimit();
            string initialText = ImportantMemoryLineRenderer.CleanManualOverride(
                memoryEditStartRenderedText,
                textLimit);
            string draftText = ImportantMemoryLineRenderer.CleanManualOverride(
                memoryEditBuffer,
                textLimit);
            bool needsPersistence = DiaryUiPolicy.MemoryDraftNeedsPersistence(
                initialText,
                draftText);
            if (needsPersistence
                && !component.TrySetImportantMemoryTextForProfile(
                    pawn,
                    record.recordId,
                    draftText))
            {
                return false;
            }

            // Refresh after an explicit Save even for a no-op. If gameplay removed the detached row while
            // this dialog was open, the editor exits cleanly and the visible list catches up.
            RefreshMemorySnapshots();
            EndMemoryEdit();
            if (showSuccessMessage)
            {
                Messages.Message(
                    "PawnDiary.Profile.MemorySaved".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }

            return true;
        }

        private void EndMemoryEdit()
        {
            memoryEditing = false;
            memoryEditBuffer = string.Empty;
            memoryEditStartRenderedText = string.Empty;
            memoryTextScroll = Vector2.zero;
        }

        private bool IsEditingMemory(ImportantMemoryRecordSnapshot record)
        {
            return memoryEditing
                && record != null
                && string.Equals(
                    selectedMemoryRecordId,
                    record.recordId,
                    StringComparison.Ordinal);
        }

        private string MemoryMetadata(ImportantMemoryRecordSnapshot record)
        {
            return FormatMemoryFrame(
                "PawnDiary.Dev.Memory.MetadataExtended",
                MemoryValue(record?.dateLabel),
                MemoryValue(record?.eventKind),
                MemoryValue(record?.topicKey),
                MemoryValue(record?.sourceKind),
                MemoryValue(record?.recallScope),
                MemoryValue(record?.recordId));
        }

        private string MemoryDate(ImportantMemoryRecordSnapshot record)
        {
            return FormatMemoryFrame(
                "PawnDiary.Profile.MemoryDate",
                MemoryValue(record?.dateLabel));
        }

        private string MemoryOptionLabel(ImportantMemoryRecordSnapshot record)
        {
            string rendered = RenderedMemoryText(record, MemoryTextLimit());
            return FormatMemoryFrame(
                "PawnDiary.Profile.MemoryOption",
                MemoryValue(record?.dateLabel),
                MemoryTextForDisplay(rendered));
        }

        private string RenderedMemoryText(
            ImportantMemoryRecordSnapshot record,
            int maxChars)
        {
            if (record == null)
            {
                return string.Empty;
            }

            ImportantEventRule rule = DiaryKnowledgePolicy.RuleForKind(record.eventKind);
            return ImportantMemoryLineRenderer.Render(
                record,
                rule?.lineTemplate,
                maxChars);
        }

        private string MemoryEditLabel(int maxChars)
        {
            return maxChars > 0
                ? FormatMemoryFrame(
                    "PawnDiary.Profile.MemoryEditLabel",
                    (memoryEditBuffer ?? string.Empty).Length,
                    maxChars)
                : FormatMemoryFrame(
                    "PawnDiary.Profile.MemoryEditLabelUnlimited",
                    (memoryEditBuffer ?? string.Empty).Length);
        }

        private int MemoryTextLimit()
        {
            return component == null ? 0 : component.ImportantMemoryTextLimitForProfile();
        }

        private static int ValidMemoryCount(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories)
        {
            if (memories == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i] != null
                    && !string.IsNullOrWhiteSpace(memories[i].recordId))
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindMemoryIndex(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            string recordId)
        {
            if (memories == null || string.IsNullOrWhiteSpace(recordId))
            {
                return -1;
            }

            for (int i = 0; i < memories.Count; i++)
            {
                ImportantMemoryRecordSnapshot record = memories[i];
                if (record != null
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindNextMemoryIndex(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            int start,
            int step)
        {
            if (memories == null || step == 0)
            {
                return -1;
            }

            for (int i = start; i >= 0 && i < memories.Count; i += step)
            {
                ImportantMemoryRecordSnapshot record = memories[i];
                if (record != null && !string.IsNullOrWhiteSpace(record.recordId))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int MemoryDisplayPosition(
            IReadOnlyList<ImportantMemoryRecordSnapshot> memories,
            string selectedRecordId)
        {
            if (memories == null)
            {
                return 0;
            }

            int position = 0;
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                ImportantMemoryRecordSnapshot record = memories[i];
                if (record == null || string.IsNullOrWhiteSpace(record.recordId))
                {
                    continue;
                }

                position++;
                if (string.Equals(
                    record.recordId,
                    selectedRecordId,
                    StringComparison.Ordinal))
                {
                    return position;
                }
            }

            return 0;
        }

        private static string MemoryValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "PawnDiary.Profile.MemoryUnknown".Translate().ToString()
                : value;
        }

        private static string MemoryTextForDisplay(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? "PawnDiary.Profile.MemoryRenderedEmpty".Translate().ToString()
                : text;
        }

        private static string FormatMemoryFrame(string key, params object[] values)
        {
            string frame = key.Translate().Resolve();
            try
            {
                // Resolve the localization frame without arguments, then format it ourselves. Verse's
                // Translate(args) sentence-cases inserted text after ':' and can alter an edited memory.
                return string.Format(frame, values);
            }
            catch (FormatException)
            {
                return frame;
            }
        }

        private static void MemoryOperationFailed()
        {
            Messages.Message(
                "PawnDiary.Profile.MemoryOperationFailed".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }
    }
}
