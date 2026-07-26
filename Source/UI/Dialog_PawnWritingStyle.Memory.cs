// Developer-only important-memory section for Dialog_PawnWritingStyle.
//
// At most one record is drawn at a time: the XML policy permits hundreds of memories per pawn, so
// rendering one editor for every row would make RimWorld's repeated IMGUI passes unnecessarily
// expensive. Older/Newer and a lazy FloatMenu provide access to the full list. Persistent changes
// happen only from explicit Save/Remove clicks through DiaryGameComponent's guarded dev endpoints.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_PawnWritingStyle
    {
        private string selectedMemoryRecordId = string.Empty;
        private string memoryEditBuffer = string.Empty;
        private bool memoryEditing;
        private Vector2 memoryTextScroll;

        /// <summary>
        /// Draws the developer-only memory selector and single-record editor. The caller owns the
        /// Prefs.DevMode gate so normal play draws and reserves no part of this section.
        /// </summary>
        private void DrawMemorySection(
            float x,
            float width,
            ref float y,
            IReadOnlyList<ImportantMemoryRecord> memories)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            int memoryCount = ValidMemoryCount(memories);
            string title = FormatMemoryFrame("PawnDiary.Dev.Memory.SectionTitle", memoryCount);
            float titleHeight = SmallLabelHeight(title, width);
            Widgets.Label(new Rect(x, y, width, titleHeight), title);
            y += titleHeight + FieldGap;

            ImportantMemoryRecord selected = EnsureMemorySelection(memories);
            if (selected == null)
            {
                string empty = "PawnDiary.Dev.Memory.Empty".Translate();
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

            string metadata = MemoryMetadata(selected);
            float metadataHeight = SmallLabelHeight(metadata, width);
            Widgets.Label(new Rect(x, y, width, metadataHeight), metadata);
            y += metadataHeight + FieldGap;

            int textLimit = MemoryTextLimit();
            string actualText = RenderedMemoryText(selected, textLimit);
            string shownText = memoryEditing
                ? memoryEditBuffer
                : MemoryTextForDisplay(actualText);
            string label = memoryEditing
                ? MemoryEditLabel(textLimit)
                : "PawnDiary.Dev.Memory.TextLabel".Translate().ToString();

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                label,
                shownText,
                ref memoryTextScroll,
                PromptAreaHeight,
                editable: memoryEditing,
                editedText: memoryEditing
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
        /// developer section or leaves dead space when Dev Mode is toggled while the window is open.
        /// </summary>
        private float MemorySectionHeight(
            float width,
            IReadOnlyList<ImportantMemoryRecord> memories)
        {
            int memoryCount = ValidMemoryCount(memories);
            float height = SectionGap + FieldGap; // gap plus separator line
            height += SmallLabelHeight(
                FormatMemoryFrame("PawnDiary.Dev.Memory.SectionTitle", memoryCount),
                width) + FieldGap;

            ImportantMemoryRecord selected = SelectedMemoryOrNewest(memories);
            if (selected == null)
            {
                return height
                    + SmallLabelHeight("PawnDiary.Dev.Memory.Empty".Translate(), width)
                    + FieldGap;
            }

            height += ButtonHeight + FieldGap;
            height += SmallLabelHeight(MemoryMetadata(selected), width) + FieldGap;
            height += LabeledScrollTextHeight(
                memoryEditing
                    ? MemoryEditLabel(MemoryTextLimit())
                    : "PawnDiary.Dev.Memory.TextLabel".Translate().ToString(),
                width,
                PromptAreaHeight) + FieldGap;
            height += ButtonHeight + FieldGap;
            return height;
        }

        private void DrawMemoryNavigation(
            Rect rect,
            IReadOnlyList<ImportantMemoryRecord> memories,
            ImportantMemoryRecord selected,
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
            bool navigationEnabled = !memoryEditing;

            if (Widgets.ButtonText(
                olderRect,
                "PawnDiary.Dev.Memory.Older".Translate(),
                true,
                true,
                navigationEnabled && olderIndex >= 0))
            {
                SelectMemory(memories[olderIndex]);
            }

            string selector = FormatMemoryFrame(
                "PawnDiary.Dev.Memory.Selector",
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
                "PawnDiary.Dev.Memory.Newer".Translate(),
                true,
                true,
                navigationEnabled && newerIndex >= 0))
            {
                SelectMemory(memories[newerIndex]);
            }
        }

        private void DrawMemoryActions(Rect rect, ImportantMemoryRecord record, string renderedText)
        {
            int buttonCount = memoryEditing ? 3 : 2;
            float buttonWidth = Mathf.Max(
                1f,
                (rect.width - FieldGap * (buttonCount - 1)) / buttonCount);

            Rect first = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect second = new Rect(first.xMax + FieldGap, rect.y, buttonWidth, rect.height);
            Rect third = new Rect(second.xMax + FieldGap, rect.y, buttonWidth, rect.height);

            if (!memoryEditing)
            {
                if (Widgets.ButtonText(first, "PawnDiary.Dev.Memory.Edit".Translate()))
                {
                    memoryEditBuffer = renderedText ?? string.Empty;
                    memoryEditing = true;
                    memoryTextScroll = Vector2.zero;
                }

                if (Widgets.ButtonText(second, "PawnDiary.Dev.Memory.Remove".Translate()))
                {
                    ConfirmMemoryRemoval(record, renderedText);
                }

                return;
            }

            if (Widgets.ButtonText(first, "PawnDiary.Dev.Memory.Save".Translate()))
            {
                if (component != null
                    && component.TrySetImportantMemoryTextForDev(
                        pawn,
                        record.recordId,
                        memoryEditBuffer))
                {
                    memoryEditing = false;
                    memoryEditBuffer = string.Empty;
                    memoryTextScroll = Vector2.zero;
                    Messages.Message(
                        "PawnDiary.Dev.Memory.Saved".Translate(),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                }
                else
                {
                    MemoryOperationFailed();
                }
            }

            if (Widgets.ButtonText(second, "PawnDiary.Dev.Memory.Cancel".Translate()))
            {
                memoryEditing = false;
                memoryEditBuffer = string.Empty;
                memoryTextScroll = Vector2.zero;
            }

            if (Widgets.ButtonText(third, "PawnDiary.Dev.Memory.Remove".Translate()))
            {
                ConfirmMemoryRemoval(record, renderedText);
            }
        }

        private void ConfirmMemoryRemoval(ImportantMemoryRecord record, string renderedText)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.recordId))
            {
                MemoryOperationFailed();
                return;
            }

            string recordId = record.recordId;
            string confirmation = FormatMemoryFrame(
                "PawnDiary.Dev.Memory.RemoveConfirm",
                MemoryTextForDisplay(renderedText));
            Dialog_MessageBox dialog = new Dialog_MessageBox(
                confirmation,
                "PawnDiary.Dev.Memory.Remove".Translate(),
                delegate
                {
                    if (component != null
                        && component.TryRemoveImportantMemoryForDev(pawn, recordId))
                    {
                        if (string.Equals(
                            selectedMemoryRecordId,
                            recordId,
                            StringComparison.Ordinal))
                        {
                            selectedMemoryRecordId = string.Empty;
                        }

                        memoryEditing = false;
                        memoryEditBuffer = string.Empty;
                        memoryTextScroll = Vector2.zero;
                        Messages.Message(
                            "PawnDiary.Dev.Memory.Removed".Translate(),
                            MessageTypeDefOf.NeutralEvent,
                            false);
                    }
                    else
                    {
                        MemoryOperationFailed();
                    }
                },
                "PawnDiary.Dev.Memory.Cancel".Translate(),
                null,
                "PawnDiary.Dev.Memory.RemoveTitle".Translate());
            Find.WindowStack.Add(dialog);
        }

        private void OpenMemoryPicker(IReadOnlyList<ImportantMemoryRecord> memories)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (memories != null)
            {
                for (int i = memories.Count - 1; i >= 0; i--)
                {
                    ImportantMemoryRecord record = memories[i];
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

        private ImportantMemoryRecord EnsureMemorySelection(
            IReadOnlyList<ImportantMemoryRecord> memories)
        {
            ImportantMemoryRecord selected = SelectedMemoryOrNewest(memories);
            if (selected == null)
            {
                selectedMemoryRecordId = string.Empty;
                memoryEditing = false;
                memoryEditBuffer = string.Empty;
                memoryTextScroll = Vector2.zero;
                return null;
            }

            if (!string.Equals(
                selectedMemoryRecordId,
                selected.recordId,
                StringComparison.Ordinal))
            {
                SelectMemory(selected);
            }

            return selected;
        }

        private ImportantMemoryRecord SelectedMemoryOrNewest(
            IReadOnlyList<ImportantMemoryRecord> memories)
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
            IReadOnlyList<ImportantMemoryRecord> memories,
            string recordId)
        {
            int index = FindMemoryIndex(memories, recordId);
            if (index >= 0)
            {
                SelectMemory(memories[index]);
            }
        }

        private void SelectMemory(ImportantMemoryRecord record)
        {
            if (record == null)
            {
                return;
            }

            selectedMemoryRecordId = record.recordId ?? string.Empty;
            memoryEditing = false;
            memoryEditBuffer = string.Empty;
            memoryTextScroll = Vector2.zero;
        }

        private string MemoryMetadata(ImportantMemoryRecord record)
        {
            return FormatMemoryFrame(
                "PawnDiary.Dev.Memory.Metadata",
                MemoryValue(record?.dateLabel),
                MemoryValue(record?.eventKind),
                MemoryValue(record?.topicKey));
        }

        private string MemoryOptionLabel(ImportantMemoryRecord record)
        {
            string rendered = RenderedMemoryText(record, MemoryTextLimit());
            return FormatMemoryFrame(
                "PawnDiary.Dev.Memory.Option",
                MemoryValue(record?.dateLabel),
                MemoryTextForDisplay(rendered));
        }

        private string RenderedMemoryText(ImportantMemoryRecord record, int maxChars)
        {
            if (record == null)
            {
                return string.Empty;
            }

            ImportantEventRule rule = DiaryKnowledgePolicy.RuleForKind(record.eventKind);
            return ImportantMemoryLineRenderer.Render(
                record.ToSnapshot(),
                rule?.lineTemplate,
                maxChars);
        }

        private string MemoryEditLabel(int maxChars)
        {
            return maxChars > 0
                ? FormatMemoryFrame(
                    "PawnDiary.Dev.Memory.EditLabel",
                    (memoryEditBuffer ?? string.Empty).Length,
                    maxChars)
                : FormatMemoryFrame(
                    "PawnDiary.Dev.Memory.EditLabelUnlimited",
                    (memoryEditBuffer ?? string.Empty).Length);
        }

        private int MemoryTextLimit()
        {
            return component == null ? 0 : component.ImportantMemoryTextLimitForDev();
        }

        private static int ValidMemoryCount(IReadOnlyList<ImportantMemoryRecord> memories)
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
            IReadOnlyList<ImportantMemoryRecord> memories,
            string recordId)
        {
            if (memories == null || string.IsNullOrWhiteSpace(recordId))
            {
                return -1;
            }

            for (int i = 0; i < memories.Count; i++)
            {
                ImportantMemoryRecord record = memories[i];
                if (record != null
                    && string.Equals(record.recordId, recordId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindNextMemoryIndex(
            IReadOnlyList<ImportantMemoryRecord> memories,
            int start,
            int step)
        {
            if (memories == null || step == 0)
            {
                return -1;
            }

            for (int i = start; i >= 0 && i < memories.Count; i += step)
            {
                ImportantMemoryRecord record = memories[i];
                if (record != null && !string.IsNullOrWhiteSpace(record.recordId))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int MemoryDisplayPosition(
            IReadOnlyList<ImportantMemoryRecord> memories,
            string selectedRecordId)
        {
            if (memories == null)
            {
                return 0;
            }

            int position = 0;
            for (int i = memories.Count - 1; i >= 0; i--)
            {
                ImportantMemoryRecord record = memories[i];
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
                ? "PawnDiary.Dev.Memory.Unknown".Translate().ToString()
                : value;
        }

        private static string MemoryTextForDisplay(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? "PawnDiary.Dev.Memory.RenderedEmpty".Translate().ToString()
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
                "PawnDiary.Dev.Memory.OperationFailed".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }
    }
}
