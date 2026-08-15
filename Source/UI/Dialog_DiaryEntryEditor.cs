// Detached, review-first composer for player-authored diary pages.
//
// The inspect journal and standalone reader both open this Window. Every field, selection, generated
// result, and error below is transient UI state. Context/Full Prompt generation uses only the game
// component's Start/Poll/Cancel facade, and a diary page is created only after the player reviews the
// returned prose and explicitly presses Save. See Dialog_DiaryEntryEditor.Layout.cs for drawing.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Writes a new page directly, generates a reviewable draft, or atomically revises one exact page.
    /// </summary>
    internal sealed partial class Dialog_DiaryEntryEditor : Window
    {
        /// <summary>The transient generation stage. It never becomes part of a saved diary.</summary>
        private enum ComposerDraftStage
        {
            Editing,
            Pending,
            Failed,
            Review
        }

        private readonly DiaryGameComponent component;
        private readonly Pawn createPawn;
        private readonly ManualDiaryEntrySnapshot expectedSnapshot;
        private readonly string pawnId;
        private readonly string pawnDisplayName;
        private readonly string originalTitle;
        private readonly string originalBody;
        private readonly string originalEntryTypeKey;
        private readonly string originalEntryTypeLabel;
        private readonly string originalEntryTypeDescription;
        private readonly bool entryTypeLocked;
        private readonly int titleMaxCharacters;
        private readonly int bodyMaxCharacters;
        // Def-backed catalogs are detached snapshots. Cache them once because Unity may run both the
        // measure and repaint passes several times per frame; rebuilding lists there only creates GC.
        private readonly List<PlayerEntryTypeSnapshot> entryTypes;
        private readonly List<PlayerEntryTemplateSnapshot> promptTemplates;
        private readonly string defaultEntryTypeKey;

        // Direct-write/edit buffers remain separate from generated review text. A player can switch
        // modes without losing either draft, and Back to prompt never silently overwrites their prose.
        private string titleBuffer;
        private string bodyBuffer;
        private string reviewTitleBuffer = string.Empty;
        private string reviewBodyBuffer = string.Empty;
        private string contextSubjectBuffer = string.Empty;
        private string contextInstructionBuffer = string.Empty;
        private string rawSystemPromptBuffer = string.Empty;
        private string rawUserPromptBuffer = string.Empty;

        private string selectedEntryTypeKey;
        // Blank is the synthetic Automatic option. The Start result reports the concrete template used.
        private string selectedTemplateKey = string.Empty;
        private string generatedTemplateKey = string.Empty;
        private string generationError = string.Empty;
        private int draftHandle;
        private PlayerEntryComposerMode selectedMode = PlayerEntryComposerMode.Direct;
        private ComposerDraftStage draftStage = ComposerDraftStage.Editing;
        private Vector2 contentScroll;

        private bool allowImmediateClose;
        private bool confirmationOpen;

        private bool Creating => createPawn != null;
        private bool Reviewing => draftStage == ComposerDraftStage.Review;
        private bool Pending => draftStage == ComposerDraftStage.Pending;

        private Dialog_DiaryEntryEditor(
            DiaryGameComponent component,
            Pawn createPawn,
            ManualDiaryEntrySnapshot expectedSnapshot,
            string pawnId,
            string pawnDisplayName,
            string title,
            string body,
            int titleMaxCharacters,
            int bodyMaxCharacters)
        {
            this.component = component;
            this.createPawn = createPawn;
            this.expectedSnapshot = expectedSnapshot;
            this.pawnId = pawnId ?? string.Empty;
            this.pawnDisplayName = pawnDisplayName ?? string.Empty;
            this.titleMaxCharacters = Math.Max(1, titleMaxCharacters);
            this.bodyMaxCharacters = Math.Max(1, bodyMaxCharacters);

            // Preserve old/generated fields byte-for-byte. A legacy sibling can exceed today's cap; it
            // remains legal while unchanged, including when the player edits only the page category.
            originalTitle = title ?? string.Empty;
            originalBody = body ?? string.Empty;
            titleBuffer = originalTitle;
            bodyBuffer = originalBody;

            originalEntryTypeKey = expectedSnapshot?.EntryTypeKey ?? string.Empty;
            originalEntryTypeLabel = expectedSnapshot?.EntryTypeLabel ?? string.Empty;
            originalEntryTypeDescription = expectedSnapshot?.EntryTypeDescription ?? string.Empty;
            entryTypeLocked = expectedSnapshot != null && expectedSnapshot.EntryTypeLocked;
            entryTypes = DiaryPlayerEntryTypes.ForUi() ?? new List<PlayerEntryTypeSnapshot>();
            promptTemplates = DiaryPlayerPromptTemplates.ForUi()
                ?? new List<PlayerEntryTemplateSnapshot>();
            PlayerEntryTypeSnapshot defaultEntryType = FindEntryType(
                entryTypes,
                PlayerEntryComposerPolicy.PersonalEntryTypeKey);
            if (defaultEntryType == null && entryTypes.Count > 0)
            {
                defaultEntryType = entryTypes[0];
            }
            defaultEntryTypeKey = defaultEntryType?.entryTypeKey
                ?? PlayerEntryComposerPolicy.PersonalEntryTypeKey;
            selectedEntryTypeKey = Creating
                ? defaultEntryTypeKey
                : originalEntryTypeKey;

            forcePause = false;
            draggable = true;
            resizeable = false;
            doCloseX = true;
            // Return belongs to the composer's multiline text areas. Verse.Window otherwise treats
            // it as the dialog Accept key before Widgets.TextArea can insert the newline.
            closeOnAccept = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            onlyOneOfTypeAllowed = true;
        }

        /// <summary>Builds an empty three-mode composer for a supplied live pawn.</summary>
        internal static Dialog_DiaryEntryEditor ForCreate(
            Pawn pawn,
            DiaryGameComponent component,
            int titleMaxCharacters,
            int bodyMaxCharacters)
        {
            return new Dialog_DiaryEntryEditor(
                component,
                pawn,
                null,
                pawn?.GetUniqueLoadID(),
                pawn?.LabelShortCap.ToString(),
                string.Empty,
                string.Empty,
                titleMaxCharacters,
                bodyMaxCharacters);
        }

        /// <summary>Builds a Direct-only detached edit for one exact persisted pawn/event/POV page.</summary>
        internal static Dialog_DiaryEntryEditor ForEdit(
            string pawnDisplayName,
            ManualDiaryEntrySnapshot snapshot,
            DiaryGameComponent component,
            int titleMaxCharacters,
            int bodyMaxCharacters)
        {
            return new Dialog_DiaryEntryEditor(
                component,
                null,
                snapshot,
                snapshot?.PawnId,
                pawnDisplayName,
                snapshot?.Title,
                snapshot?.Body,
                titleMaxCharacters,
                bodyMaxCharacters);
        }

        /// <summary>Responsive preferred size backed by the shared diary UI-style Def.</summary>
        public override Vector2 InitialSize
        {
            get
            {
                DiaryUiStyleDef style = DiaryJournalView.UiStyle;
                float preferredWidth = PositiveOr(style.manualEntryEditorWidth, 820f);
                float preferredHeight = PositiveOr(style.manualEntryEditorHeight, 700f);
                float margin = NonNegativeOr(style.manualEntryEditorScreenMargin, 64f);
                return new Vector2(
                    Mathf.Min(preferredWidth, Mathf.Max(1f, UI.screenWidth - margin)),
                    Mathf.Min(preferredHeight, Mathf.Max(1f, UI.screenHeight - margin)));
            }
        }

        /// <summary>
        /// Polls once per Unity update rather than during immediate-mode drawing, whose layout/repaint
        /// passes can repeat. A terminal result changes only detached window buffers.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!Pending || draftHandle <= 0 || component == null)
            {
                return;
            }

            PlayerEntryDraftPollResult result = component.PollPlayerEntryDraft(draftHandle);
            if (result == null || result.status == PlayerEntryDraftStatus.Pending)
            {
                return;
            }

            draftHandle = 0;
            contentScroll = Vector2.zero;
            if (result.status == PlayerEntryDraftStatus.Succeeded
                && !string.IsNullOrWhiteSpace(result.text))
            {
                reviewTitleBuffer = string.Empty;
                reviewBodyBuffer = result.text;
                generationError = string.Empty;
                draftStage = ComposerDraftStage.Review;
                return;
            }

            generationError = result.status == PlayerEntryDraftStatus.Failed
                ? FormatGenerationFailure(result.error)
                : "PawnDiary.ManualEntry.GenerationUnknown".Translate().Resolve();
            draftStage = ComposerDraftStage.Failed;
        }

        /// <summary>
        /// Intercepts the built-in X and Escape close paths. Empty composers close immediately; dirty or
        /// in-flight work requires confirmation. Explicit successful Save bypasses this guard.
        /// </summary>
        public override void Close(bool doCloseSound = true)
        {
            if (allowImmediateClose || (!Pending && !DraftChanged()))
            {
                CancelActiveDraft();
                base.Close(doCloseSound);
                return;
            }

            if (confirmationOpen)
            {
                return;
            }

            confirmationOpen = true;
            bool cancelPending = Pending;
            string titleKey = cancelPending
                ? "PawnDiary.ManualEntry.CancelPendingTitle"
                : "PawnDiary.ManualEntry.DiscardTitle";
            string bodyKey = cancelPending
                ? "PawnDiary.ManualEntry.CancelPendingPrompt"
                : "PawnDiary.ManualEntry.DiscardPrompt";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                bodyKey.Translate(),
                delegate
                {
                    confirmationOpen = false;
                    CancelActiveDraft();
                    allowImmediateClose = true;
                    base.Close(doCloseSound);
                },
                delegate { confirmationOpen = false; },
                true,
                titleKey.Translate().Resolve()));
        }

        /// <summary>Last-resort cleanup for stack removal paths that do not originate from Close.</summary>
        public override void PostClose()
        {
            CancelActiveDraft();
            base.PostClose();
        }

        /// <summary>Starts Context or Full Prompt generation after local validation.</summary>
        private void StartGeneration()
        {
            string validationTip;
            if (!CanGenerate(out validationTip))
            {
                Reject(validationTip);
                return;
            }

            PlayerEntryDraftStartResult result = component.StartPlayerEntryDraft(
                createPawn,
                BuildGenerationRequest());
            if (result == null || !result.accepted || result.handle <= 0)
            {
                generationError = LocalizedStartError(result?.errorCode);
                draftStage = ComposerDraftStage.Failed;
                contentScroll = Vector2.zero;
                return;
            }

            draftHandle = result.handle;
            if (!string.IsNullOrWhiteSpace(result.entryTypeKey))
            {
                selectedEntryTypeKey = result.entryTypeKey;
            }
            generatedTemplateKey = result.templateKey ?? string.Empty;
            generationError = string.Empty;
            draftStage = ComposerDraftStage.Pending;
            contentScroll = Vector2.zero;
        }

        /// <summary>Builds the plain request handed to the component; no Def or live game object leaks in.</summary>
        private PlayerEntryComposerRequest BuildGenerationRequest()
        {
            return new PlayerEntryComposerRequest
            {
                mode = selectedMode,
                entryTypeKey = selectedEntryTypeKey,
                templateKey = selectedTemplateKey,
                factualSummary = contextSubjectBuffer,
                customInstruction = contextInstructionBuffer,
                systemPrompt = rawSystemPromptBuffer,
                userPrompt = rawUserPromptBuffer,
                laneIndex = -1,
                maxTokens = PlayerEntryComposerPolicy.UseTemplateOrSettingsMaxTokens
            };
        }

        /// <summary>Cancels only the transient request and returns to its intact prompt fields.</summary>
        private void CancelPendingRequest()
        {
            CancelActiveDraft();
            generationError = string.Empty;
            draftStage = ComposerDraftStage.Editing;
            contentScroll = Vector2.zero;
        }

        private void CancelActiveDraft()
        {
            int handle = draftHandle;
            draftHandle = 0;
            if (handle > 0 && component != null)
            {
                component.CancelPlayerEntryDraft(handle);
            }
        }

        private void BackToPrompt()
        {
            generationError = string.Empty;
            draftStage = ComposerDraftStage.Editing;
            contentScroll = Vector2.zero;
        }

        private void SelectMode(PlayerEntryComposerMode mode)
        {
            if (!Creating || Pending || Reviewing || mode == PlayerEntryComposerMode.Review)
            {
                return;
            }

            selectedMode = mode;
            generationError = string.Empty;
            draftStage = ComposerDraftStage.Editing;
            contentScroll = Vector2.zero;
        }

        /// <summary>Commits final prose exactly once. Generated drafts use the ordinary direct-create API.</summary>
        private void Save()
        {
            string validationTip;
            if (!CanSave(out validationTip))
            {
                Reject(validationTip);
                return;
            }

            if (!Creating && !DraftChanged())
            {
                allowImmediateClose = true;
                base.Close();
                return;
            }

            string title = Reviewing ? reviewTitleBuffer : titleBuffer;
            string body = Reviewing ? reviewBodyBuffer : bodyBuffer;
            bool saved;
            string createdEventId = string.Empty;
            if (Creating)
            {
                PlayerEntryTypeSnapshot entryType;
                if (!TryFindEntryType(selectedEntryTypeKey, out entryType))
                {
                    Reject("PawnDiary.ManualEntry.EntryTypeUnavailable".Translate().Resolve());
                    return;
                }

                string localizedLabel = string.IsNullOrWhiteSpace(entryType.label)
                    ? "PawnDiary.ManualEntry.GroupLabel".Translate().Resolve()
                    : entryType.label;
                saved = component.TryCreateManualEntry(
                    createPawn,
                    body,
                    title,
                    localizedLabel,
                    entryType.entryTypeKey,
                    out createdEventId);
            }
            else
            {
                saved = component.TryEditManualEntry(
                    expectedSnapshot,
                    body,
                    title,
                    selectedEntryTypeKey);
            }

            if (!saved)
            {
                Reject("PawnDiary.ManualEntry.SaveFailed".Translate().Resolve());
                return;
            }

            if (Creating && !string.IsNullOrWhiteSpace(createdEventId))
            {
                // A successful create should reveal the new page even if the journal currently has a
                // search, Favorites-only, or tag filter that the unstarred page cannot satisfy.
                DiaryJournalView.RequestScrollToEntry(pawnId, createdEventId, true);
            }

            Messages.Message(
                (Creating
                    ? "PawnDiary.ManualEntry.Created"
                    : "PawnDiary.ManualEntry.Updated").Translate(),
                MessageTypeDefOf.NeutralEvent,
                false);
            allowImmediateClose = true;
            base.Close();
        }

        private bool CanGenerate(out string tip)
        {
            tip = string.Empty;
            if (!Creating || component == null
                || (selectedMode != PlayerEntryComposerMode.Context
                    && selectedMode != PlayerEntryComposerMode.FullPrompt))
            {
                tip = "PawnDiary.ManualEntry.GenerationStartFailed".Translate().Resolve();
                return false;
            }

            if (selectedMode == PlayerEntryComposerMode.Context)
            {
                if (string.IsNullOrWhiteSpace(contextSubjectBuffer))
                {
                    tip = "PawnDiary.ManualEntry.SubjectRequired".Translate().Resolve();
                    return false;
                }
                if (contextSubjectBuffer.Length > PlayerEntryComposerPolicy.ContextSummaryMaxCharacters
                    || contextInstructionBuffer.Length > PlayerEntryComposerPolicy.ContextInstructionMaxCharacters)
                {
                    tip = "PawnDiary.ManualEntry.PromptTooLong".Translate().Resolve();
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(rawUserPromptBuffer))
                {
                    tip = "PawnDiary.ManualEntry.PromptRequired".Translate().Resolve();
                    return false;
                }
                if (rawSystemPromptBuffer.Length > PlayerEntryComposerPolicy.RawPromptMaxCharacters
                    || rawUserPromptBuffer.Length > PlayerEntryComposerPolicy.RawPromptMaxCharacters)
                {
                    tip = "PawnDiary.ManualEntry.PromptTooLong".Translate().Resolve();
                    return false;
                }
            }

            PlayerEntryComposerPlan plan = PlayerEntryComposerPolicy.Plan(
                BuildGenerationRequest(), entryTypes, promptTemplates);
            if (!plan.valid)
            {
                tip = LocalizedStartError(plan.errorCode);
                return false;
            }
            return true;
        }

        private bool CanSave(out string tip)
        {
            tip = string.Empty;
            if (component == null || (Creating && selectedMode != PlayerEntryComposerMode.Direct && !Reviewing))
            {
                tip = "PawnDiary.ManualEntry.SaveFailed".Translate().Resolve();
                return false;
            }

            string title = Reviewing ? reviewTitleBuffer : titleBuffer;
            string body = Reviewing ? reviewBodyBuffer : bodyBuffer;
            if (string.IsNullOrWhiteSpace(body))
            {
                tip = "PawnDiary.ManualEntry.BodyRequired".Translate().Resolve();
                return false;
            }

            bool titleChanged = Creating || !string.Equals(title, originalTitle, StringComparison.Ordinal);
            bool bodyChanged = Creating || !string.Equals(body, originalBody, StringComparison.Ordinal);
            if ((titleChanged && title.Length > titleMaxCharacters)
                || (bodyChanged && body.Length > bodyMaxCharacters))
            {
                tip = "PawnDiary.ManualEntry.TooLong".Translate().Resolve();
                return false;
            }

            if (Creating)
            {
                PlayerEntryTypeSnapshot ignored;
                if (!TryFindEntryType(selectedEntryTypeKey, out ignored))
                {
                    tip = "PawnDiary.ManualEntry.EntryTypeUnavailable".Translate().Resolve();
                    return false;
                }
            }
            else if (entryTypeLocked && !string.Equals(
                selectedEntryTypeKey, originalEntryTypeKey, StringComparison.Ordinal))
            {
                tip = "PawnDiary.ManualEntry.EntryTypeLockedTip".Translate().Resolve();
                return false;
            }

            if (!Creating && !DraftChanged())
            {
                tip = "PawnDiary.ManualEntry.NoChanges".Translate().Resolve();
            }
            return true;
        }

        private bool DraftChanged()
        {
            if (!Creating)
            {
                return !string.Equals(titleBuffer, originalTitle, StringComparison.Ordinal)
                    || !string.Equals(bodyBuffer, originalBody, StringComparison.Ordinal)
                    || !string.Equals(selectedEntryTypeKey, originalEntryTypeKey, StringComparison.Ordinal);
            }

            return titleBuffer.Length > 0
                || bodyBuffer.Length > 0
                || reviewTitleBuffer.Length > 0
                || reviewBodyBuffer.Length > 0
                || contextSubjectBuffer.Length > 0
                || contextInstructionBuffer.Length > 0
                || rawSystemPromptBuffer.Length > 0
                || rawUserPromptBuffer.Length > 0
                || !string.Equals(selectedEntryTypeKey, defaultEntryTypeKey, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(selectedTemplateKey)
                || Pending;
        }

        private bool OriginalTypeOptionAvailable()
        {
            if (Creating)
            {
                return false;
            }

            PlayerEntryTypeSnapshot ignored;
            return !TryFindEntryType(originalEntryTypeKey, out ignored);
        }

        private PlayerEntryTypeSnapshot SelectedEntryType()
        {
            PlayerEntryTypeSnapshot result;
            return TryFindEntryType(selectedEntryTypeKey, out result) ? result : null;
        }

        private string SelectedEntryTypeLabel()
        {
            PlayerEntryTypeSnapshot selected = SelectedEntryType();
            if (selected != null && !string.IsNullOrWhiteSpace(selected.label)) return selected.label;
            if (entryTypeLocked && !string.IsNullOrWhiteSpace(originalEntryTypeLabel))
                return originalEntryTypeLabel;
            return "PawnDiary.ManualEntry.EntryTypeOriginal".Translate().Resolve();
        }

        private string SelectedEntryTypeDescription()
        {
            string description;
            PlayerEntryTypeSnapshot selected = SelectedEntryType();
            if (selected != null)
            {
                description = selected.description ?? string.Empty;
            }
            else if (entryTypeLocked && !string.IsNullOrWhiteSpace(originalEntryTypeDescription))
            {
                description = originalEntryTypeDescription;
            }
            else
            {
                description = "PawnDiary.ManualEntry.EntryTypeOriginalDescription".Translate().Resolve();
            }

            if (!entryTypeLocked) return description;
            string locked = "PawnDiary.ManualEntry.EntryTypeLocked".Translate().Resolve();
            return string.IsNullOrWhiteSpace(description) ? locked : description + "\n" + locked;
        }

        private PlayerEntryTemplateSnapshot SelectedTemplate()
        {
            if (string.IsNullOrWhiteSpace(selectedTemplateKey)) return null;
            for (int i = 0; i < promptTemplates.Count; i++)
            {
                if (string.Equals(promptTemplates[i]?.templateKey, selectedTemplateKey,
                    StringComparison.OrdinalIgnoreCase)) return promptTemplates[i];
            }
            return null;
        }

        private string SelectedTemplateLabel()
        {
            PlayerEntryTemplateSnapshot selected = SelectedTemplate();
            return selected == null
                ? "PawnDiary.ManualEntry.TemplateAutomatic".Translate().Resolve()
                : selected.label;
        }

        private string SelectedTemplateDescription()
        {
            PlayerEntryTemplateSnapshot selected = SelectedTemplate();
            if (selected != null) return selected.description ?? string.Empty;
            return "PawnDiary.ManualEntry.TemplateAutomaticDescription".Translate().Resolve();
        }

        private void ShowEntryTypeMenu()
        {
            // A generated review must retain the category whose event guidance shaped that prose.
            // Back to prompt first if the player wants to choose a different type and regenerate.
            if (entryTypeLocked || Pending || Reviewing) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (OriginalTypeOptionAvailable())
            {
                options.Add(new FloatMenuOption(
                    "PawnDiary.ManualEntry.EntryTypeOriginal".Translate().Resolve(),
                    delegate { selectedEntryTypeKey = originalEntryTypeKey; }));
            }

            for (int i = 0; i < entryTypes.Count; i++)
            {
                PlayerEntryTypeSnapshot row = entryTypes[i];
                if (row == null) continue;
                string key = row.entryTypeKey;
                options.Add(new FloatMenuOption(row.label, delegate { selectedEntryTypeKey = key; }));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowTemplateMenu()
        {
            if (Pending || Reviewing) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "PawnDiary.ManualEntry.TemplateAutomatic".Translate().Resolve(),
                    delegate { selectedTemplateKey = string.Empty; })
            };
            for (int i = 0; i < promptTemplates.Count; i++)
            {
                PlayerEntryTemplateSnapshot row = promptTemplates[i];
                if (row == null) continue;
                string key = row.templateKey;
                options.Add(new FloatMenuOption(row.label, delegate { selectedTemplateKey = key; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private bool TryFindEntryType(string key, out PlayerEntryTypeSnapshot result)
        {
            result = FindEntryType(entryTypes, key);
            return result != null;
        }

        private static PlayerEntryTypeSnapshot FindEntryType(
            List<PlayerEntryTypeSnapshot> types,
            string key)
        {
            if (types == null || string.IsNullOrWhiteSpace(key)) return null;
            for (int i = 0; i < types.Count; i++)
            {
                PlayerEntryTypeSnapshot row = types[i];
                if (row != null && string.Equals(
                    row.entryTypeKey, key, StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }

        private static string LocalizedStartError(string errorCode)
        {
            switch ((errorCode ?? string.Empty).Trim())
            {
                case "blank_context_request":
                    return "PawnDiary.ManualEntry.SubjectRequired".Translate().Resolve();
                case "blank_user_prompt":
                    return "PawnDiary.ManualEntry.PromptRequired".Translate().Resolve();
                case "unknown_template":
                    return "PawnDiary.ManualEntry.TemplateUnavailable".Translate().Resolve();
                case "unknown_entry_type":
                    return "PawnDiary.ManualEntry.EntryTypeUnavailable".Translate().Resolve();
                case "pawn_unavailable":
                    return "PawnDiary.ManualEntry.PawnUnavailable".Translate().Resolve();
                case "completion_rejected":
                    return "PawnDiary.ManualEntry.NoProvider".Translate().Resolve();
                default:
                    return "PawnDiary.ManualEntry.GenerationStartFailed".Translate().Resolve();
            }
        }

        private static string FormatGenerationFailure(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return "PawnDiary.ManualEntry.GenerationUnknown".Translate().Resolve();
            return FormatPlayerTextFrame("PawnDiary.ManualEntry.GenerationFailed", detail.Trim());
        }

        private static void Reject(string text)
        {
            Messages.Message(
                string.IsNullOrWhiteSpace(text)
                    ? "PawnDiary.ManualEntry.SaveFailed".Translate().Resolve()
                    : text,
                MessageTypeDefOf.RejectInput,
                false);
        }

        /// <summary>
        /// Resolves a localized frame without passing player/provider text through Translate(args), whose
        /// grammar resolver can sentence-capitalize text after punctuation.
        /// </summary>
        internal static string FormatPlayerTextFrame(string key, string value)
        {
            string frame = (key ?? string.Empty).Translate().Resolve();
            return frame.Replace("{0}", value ?? string.Empty);
        }

        private static float PositiveOr(float value, float fallback)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : fallback;
        }

        private static float NonNegativeOr(float value, float fallback)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : fallback;
        }
    }
}
