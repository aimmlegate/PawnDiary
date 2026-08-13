// Player-facing Diary profile for one pawn. It keeps the pawn's generation switch, writing style
// (sentence mechanics), and psychotype (outlook/temperament) in one normal-play window. Both voice
// layers have a base picker, a read-only base preview, an editable pawn-specific custom rule, and a
// status panel that identifies which source currently wins. A compact effective preview shows the
// exact draft voice/outlook text without invoking prompt context providers. Developer mode appends
// isolated sections for inspecting cultural lore and editing/removing durable important memories.
//
// RimWorld IMGUI draws this window repeatedly, so editable buffers live as fields and are flushed to
// the diary record only by explicit Save — never during a draw pass. Reset changes the draft, and the
// whole content area scrolls so every section fits on small screens.
//
// New to C#/RimWorld? See AGENTS.md ("Window", "IExposable", "IMGUI").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Modal window for inspecting and editing one pawn's Diary profile. Writes only the pawn-specific
    /// generation switch, custom rules, selected base defs, and pin flags — never global catalog/XML data.
    /// </summary>
    internal sealed partial class Dialog_PawnWritingStyle : Window
    {
        // ---- Writing-style editing state ----
        private string customRuleBuffer;
        private string pendingBaseStyleDefName;
        private readonly string originalBaseStyleDefName;
        private readonly string originalCustomRule;
        private readonly bool originalWritingStylePinned;
        private bool pendingWritingStylePinned;

        // ---- Psychotype editing state ----
        private string customPsychotypeBuffer;
        private string pendingPsychotypeDefName;
        private readonly string originalPsychotypeDefName;
        private readonly string originalCustomPsychotypeRule;
        private readonly bool originalPsychotypePinned;
        private bool pendingPsychotypePinned;

        // ---- Diary-generation draft ----
        // These open-time values keep the visible status stable. A fresh backlog scan is allowed only
        // at the Save boundary for confirmation; never from DoWindowContents, which runs repeatedly.
        private readonly bool originalGenerationEnabled;
        private bool pendingGenerationEnabled;
        private readonly int resumableBacklogCount;

        // ---- External (integration) psychotype-generation state ----
        // Set while we wait for an adapter's Regenerate to finish. The editor buffer captured at click
        // time lets us refresh only if the player did not type meanwhile.
        private bool awaitingRegen;
        private string regenEditorBufferAtStart = string.Empty;

        private readonly Pawn pawn;
        private readonly DiaryGameComponent component;

        private Vector2 contentScroll;
        private Vector2 basePromptScroll;
        private Vector2 customScroll;
        private Vector2 psychotypeBaseScroll;
        private Vector2 psychotypeCustomScroll;
        private Vector2 effectivePreviewScroll;

        // Layout constants (safe font row heights per AGENTS.md / UI lore: Tiny 20, Small 24, Medium 30).
        private const float HeaderHeight = 32f;
        private const float SmallLabelMinimumHeight = 24f;
        private const float LabelBodyGap = 2f;
        private const float ButtonHeight = 30f;
        private const float PromptAreaHeight = 96f;
        private const float SmallPromptHeight = 72f;
        private const float SectionTitleHeight = 24f;
        private const float SectionGap = 12f;
        private const float Padding = 14f;
        private const float FieldGap = 6f;
        private const float ExplanationMinHeight = 40f;
        private const float PsychotypeMinimumPickerWidth = 120f;
        private const float PsychotypeRerollWidth = 90f;
        private const float PsychotypePinWidth = 110f;
        private const float PsychotypeControlGap = 6f;

        public Dialog_PawnWritingStyle(Pawn pawn, DiaryGameComponent component)
        {
            this.pawn = pawn;
            this.component = component;
            forcePause = false;
            draggable = true;
            resizeable = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;

            // Seed the writing-style editors from the saved record.
            customRuleBuffer = component == null ? string.Empty : component.CustomWritingStyleRuleFor(pawn);
            originalCustomRule = customRuleBuffer ?? string.Empty;
            WritingStyleResolution style = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);
            pendingBaseStyleDefName = string.IsNullOrWhiteSpace(style.baseStyleDefName)
                ? (DiaryPersonas.Default?.defName ?? string.Empty)
                : style.baseStyleDefName;
            originalBaseStyleDefName = pendingBaseStyleDefName;
            pendingWritingStylePinned = component != null && component.WritingStylePinnedFor(pawn);
            originalWritingStylePinned = pendingWritingStylePinned;

            // Seed through the display-only resolution. Opening a profile must never roll/backfill a voice
            // stage or materialize a PawnDiaryRecord merely because the player inspected the window.
            customPsychotypeBuffer = component == null ? string.Empty : component.CustomPsychotypeRuleFor(pawn);
            originalCustomPsychotypeRule = customPsychotypeBuffer ?? string.Empty;
            PsychotypeResolution psycho = component == null
                ? PsychotypeResolutionPolicy.Resolve(null, null, null, null)
                : component.ResolvePsychotypeForDisplay(pawn);
            pendingPsychotypeDefName = string.IsNullOrWhiteSpace(psycho.baseTypeDefName)
                ? DiaryPsychotypes.NeutralDefName
                : psycho.baseTypeDefName;
            originalPsychotypeDefName = pendingPsychotypeDefName;
            pendingPsychotypePinned = component != null && component.PsychotypePinnedFor(pawn);
            originalPsychotypePinned = pendingPsychotypePinned;

            originalGenerationEnabled = component == null
                || component.DiaryGenerationEnabledForProfile(pawn);
            pendingGenerationEnabled = originalGenerationEnabled;
            resumableBacklogCount = component == null
                ? 0
                : component.PendingGenerationBacklogCountForProfile(pawn);
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(640f, UI.screenWidth - 64f);
                float height = Mathf.Min(720f, UI.screenHeight - 64f);
                return new Vector2(width, height);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), Title());
            Text.Font = GameFont.Small;

            // Re-resolve every draw so override status stays live (read-only w.r.t. the record).
            WritingStyleResolution savedStyleResolution = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);
            // Display-only read (no EnsureVoiceStage): neither opening nor repainting this profile
            // rolls/backfills a voice stage or creates a diary record.
            PsychotypeResolution savedPsychotypeResolution = component == null
                ? PsychotypeResolutionPolicy.Resolve(null, null, null, null)
                : component.ResolvePsychotypeForDisplay(pawn);
            WritingStyleResolution styleResolution = DraftWritingStyleResolution(savedStyleResolution);
            PsychotypeResolution psychotypeResolution =
                DraftPsychotypeResolution(savedPsychotypeResolution);
            bool showDeveloperKnowledge = Prefs.DevMode;
            LoreMemorySnapshotForDev loreMemory = showDeveloperKnowledge
                && component != null
                ? component.LoreMemoryForDev(pawn)
                : null;
            IReadOnlyList<ImportantMemoryRecord> importantMemories = showDeveloperKnowledge
                && component != null
                ? component.ImportantMemoriesForDev(pawn)
                : null;

            Rect buttonRow = new Rect(inRect.x, inRect.yMax - ButtonHeight - Padding, inRect.width, ButtonHeight);
            Rect scrollOuter = new Rect(
                inRect.x,
                inRect.y + HeaderHeight + FieldGap,
                inRect.width,
                buttonRow.y - FieldGap - (inRect.y + HeaderHeight + FieldGap));

            float innerWidth = Mathf.Max(1f, scrollOuter.width - 16f); // reserve scrollbar width
            float contentHeight = MeasureContentHeight(
                innerWidth,
                styleResolution,
                psychotypeResolution,
                showDeveloperKnowledge,
                loreMemory,
                importantMemories);
            Rect contentRect = new Rect(0f, 0f, innerWidth, contentHeight);

            Widgets.BeginScrollView(scrollOuter, ref contentScroll, contentRect);
            float y = 0f;
            DrawDiarySection(contentRect.x, innerWidth, ref y);
            DrawStyleSection(contentRect.x, innerWidth, ref y, styleResolution);
            DrawPsychotypeSection(contentRect.x, innerWidth, ref y, psychotypeResolution);
            DrawEffectivePreviewSection(
                contentRect.x,
                innerWidth,
                ref y,
                styleResolution,
                psychotypeResolution);
            if (showDeveloperKnowledge)
            {
                DrawLoreMemorySection(contentRect.x, innerWidth, ref y, loreMemory);
                DrawMemorySection(contentRect.x, innerWidth, ref y, importantMemories);
            }
            Widgets.EndScrollView();

            DrawButtons(buttonRow);
        }

        private string Title()
        {
            string name = pawn == null ? string.Empty : pawn.LabelShortCap;
            return FormatProfileFrame("PawnDiary.Profile.EditorTitle", name);
        }

        // ---- Diary generation -------------------------------------------------------------------------

        /// <summary>Draws the draft-only per-pawn generation switch and its saved/backlog status.</summary>
        private void DrawDiarySection(float x, float width, ref float y)
        {
            Widgets.Label(
                new Rect(x, y, width, SectionTitleHeight),
                "PawnDiary.Profile.DiarySectionTitle".Translate());
            y += SectionTitleHeight + FieldGap;

            Rect toggleRect = new Rect(x, y, width, ButtonHeight);
            Widgets.CheckboxLabeled(
                toggleRect,
                "PawnDiary.Profile.GenerationEnabled".Translate(),
                ref pendingGenerationEnabled);
            TooltipHandler.TipRegion(
                toggleRect,
                "PawnDiary.Profile.GenerationEnabledTip".Translate());
            y += ButtonHeight + FieldGap;

            y += DrawMessagePanel(
                new Rect(x, y, width, 0f),
                DiaryGenerationStatusMessage(),
                StatusPanelColor(!pendingGenerationEnabled)) + FieldGap;
        }

        private string DiaryGenerationStatusMessage()
        {
            string status;
            if (pendingGenerationEnabled == originalGenerationEnabled)
            {
                if (pendingGenerationEnabled)
                {
                    status = "PawnDiary.Profile.GenerationRunning".Translate().Resolve();
                }
                else
                {
                    status = "PawnDiary.Profile.GenerationPaused".Translate().Resolve();
                }
            }
            else if (!pendingGenerationEnabled)
            {
                status = "PawnDiary.Profile.GenerationPausePending".Translate().Resolve();
            }
            else if (resumableBacklogCount > 0)
            {
                status = "PawnDiary.Profile.GenerationResumePending".Translate().Resolve();
            }
            else
            {
                status = "PawnDiary.Profile.GenerationResumeDirectPending".Translate().Resolve();
            }

            return status + "\n" + FormatProfileFrame(
                "PawnDiary.Profile.GenerationBacklogCount",
                resumableBacklogCount);
        }

        // ---- Writing-style section --------------------------------------------------------------------

        private void DrawStyleSection(float x, float width, ref float y, WritingStyleResolution resolution)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            Widgets.Label(
                new Rect(x, y, width, SectionTitleHeight),
                "PawnDiary.Profile.VoiceSectionTitle".Translate());
            y += SectionTitleHeight + FieldGap;

            DrawBaseStylePicker(new Rect(x, y, width, ButtonHeight), resolution);
            y += ButtonHeight + FieldGap;

            // Put precedence before the editable fields: players can immediately see whether the base,
            // their pawn-specific text, a health condition, or another mod currently controls the voice.
            y += DrawMessagePanel(new Rect(x, y, width, 0f), WritingStyleStatusMessage(resolution),
                WritingStyleStatusColor(resolution)) + FieldGap;

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                "PawnDiary.WritingStyle.BasePrompt".Translate(),
                BaseStylePromptFor(pendingBaseStyleDefName),
                ref basePromptScroll,
                PromptAreaHeight) + FieldGap;

            string customLabel = WritingStyleCustomLabel();
            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                customLabel,
                customRuleBuffer,
                ref customScroll,
                PromptAreaHeight,
                editable: true,
                editedText: text => customRuleBuffer = ClampInput(text, PlayerWritingStyleText.MaxRuleChars)) + FieldGap;

        }

        private void DrawBaseStylePicker(Rect rect, WritingStyleResolution resolution)
        {
            DiaryPersonaDef selected = DiaryPersonas.Resolve(pendingBaseStyleDefName);
            string selectedLabel = LabelFor(selected);
            if (Widgets.ButtonText(rect, "PawnDiary.WritingStyle.BaseStyle".Translate(selectedLabel)))
            {
                // Only styles for the pawn's current age band so a child never picks an adult style.
                string band = component == null ? DiaryPersonas.StageAdult : component.VoiceBandFor(pawn);
                List<FloatMenuOption> options = DiaryPersonas.CandidatesForStage(band)
                    .OrderBy(persona => LabelFor(persona))
                    .Select(persona =>
                    {
                        DiaryPersonaDef option = persona;
                        return new FloatMenuOption(LabelFor(option), delegate
                        {
                            if (option != null)
                            {
                                pendingBaseStyleDefName = option.defName;
                            }
                        });
                    })
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // ---- Psychotype section -----------------------------------------------------------------------

        private void DrawPsychotypeSection(float x, float width, ref float y, PsychotypeResolution resolution)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, width, SectionTitleHeight), "PawnDiary.Psychotype.SectionTitle".Translate());
            y += SectionTitleHeight + FieldGap;

            // Keep all three controls reachable on a narrow screen. They share one row when the picker
            // still has its useful minimum width, then stack into full-width rows below that threshold.
            int controlRows = DiaryUiPolicy.PsychotypeControlRowCount(
                width,
                PsychotypeMinimumPickerWidth,
                PsychotypeRerollWidth,
                PsychotypePinWidth,
                PsychotypeControlGap);
            Rect pickerRect;
            Rect rerollRect;
            Rect pinRect;
            if (controlRows == 1)
            {
                float pickerWidth = width
                    - PsychotypePinWidth
                    - PsychotypeRerollWidth
                    - PsychotypeControlGap * 2f;
                pickerRect = new Rect(x, y, pickerWidth, ButtonHeight);
                rerollRect = new Rect(
                    pickerRect.xMax + PsychotypeControlGap,
                    y,
                    PsychotypeRerollWidth,
                    ButtonHeight);
                pinRect = new Rect(
                    rerollRect.xMax + PsychotypeControlGap,
                    y,
                    PsychotypePinWidth,
                    ButtonHeight);
            }
            else
            {
                pickerRect = new Rect(x, y, width, ButtonHeight);
                rerollRect = new Rect(
                    x,
                    pickerRect.yMax + PsychotypeControlGap,
                    width,
                    ButtonHeight);
                pinRect = new Rect(
                    x,
                    rerollRect.yMax + PsychotypeControlGap,
                    width,
                    ButtonHeight);
            }

            DrawPsychotypePicker(pickerRect);

            if (Widgets.ButtonText(rerollRect, "PawnDiary.Psychotype.Reroll".Translate()))
            {
                if (component != null)
                {
                    pendingPsychotypeDefName = component.RollPsychotypePreview(pawn);
                    pendingPsychotypePinned = true; // an explicit re-roll pins the result
                }
            }

            TooltipHandler.TipRegion(rerollRect, "PawnDiary.Psychotype.RerollTip".Translate());
            Widgets.CheckboxLabeled(pinRect, "PawnDiary.Psychotype.Pinned".Translate(), ref pendingPsychotypePinned);
            TooltipHandler.TipRegion(pinRect, "PawnDiary.Psychotype.PinnedTip".Translate());
            y += controlRows * ButtonHeight
                + (controlRows - 1) * PsychotypeControlGap
                + FieldGap;

            DrawExternalRegenRow(x, width, ref y);

            y += DrawLabeledScrollText(
                new Rect(x, y, width, SmallPromptHeight),
                "PawnDiary.Psychotype.BaseRule".Translate(),
                DiaryPsychotypes.RuleFor(pendingPsychotypeDefName),
                ref psychotypeBaseScroll,
                SmallPromptHeight) + FieldGap;

            string customLabel = PsychotypeCustomLabel();
            y += DrawLabeledScrollText(
                new Rect(x, y, width, SmallPromptHeight),
                customLabel,
                customPsychotypeBuffer,
                ref psychotypeCustomScroll,
                SmallPromptHeight,
                editable: true,
                // Pin state is decided from the final buffer at Save (see Save), never per keystroke, so
                // typing a custom outlook and then clearing it does not leave the layer pinned. Mirrors
                // the writing-style custom field above.
                editedText: text => customPsychotypeBuffer = ClampInput(text, PsychotypeText.MaxCustomRuleChars))
                + FieldGap;

            string hint = PsychotypeHintMessage(resolution);
            if (hint != null)
            {
                y += DrawMessagePanel(new Rect(x, y, width, 0f), hint,
                    new Color(0.12f, 0.10f, 0.04f, 0.55f)) + FieldGap;
            }
        }

        // When an integration (e.g. an LLM transform) can regenerate this pawn's outlook, show a
        // Regenerate button and a live "generating…" status. The button is disabled while a generation is
        // in flight, and the editable custom buffer refreshes once the newly generated rule lands, so the
        // fresh outlook appears without reopening the window.
        private void DrawExternalRegenRow(float x, float width, ref float y)
        {
            if (pawn == null)
            {
                return;
            }

            if (!Integration.ExternalPsychotypeGenerators.CanReroll(pawn))
            {
                // The adapter/mode may disappear while a request is running. Do not carry a stale wait
                // across a later re-enable and then refresh the editor from an unrelated value.
                awaitingRegen = false;
                return;
            }

            bool busy = Integration.ExternalPsychotypeGenerators.IsBusy(pawn);

            if (awaitingRegen && !busy)
            {
                // Completion (including immediate rejection) ends the wait. Never overwrite text the
                // player typed while the asynchronous request was running.
                if (component != null
                    && string.Equals(customPsychotypeBuffer, regenEditorBufferAtStart,
                        StringComparison.Ordinal))
                {
                    customPsychotypeBuffer = component.CustomPsychotypeRuleFor(pawn);
                }

                awaitingRegen = false;
            }

            const float gap = 6f;
            Rect buttonRect = new Rect(x, y, Mathf.Min(200f, width), ButtonHeight);
            Rect statusRect = new Rect(buttonRect.xMax + gap, y, Mathf.Max(0f, width - buttonRect.width - gap), ButtonHeight);

            if (Widgets.ButtonText(buttonRect, "PawnDiary.Psychotype.RegenerateExternal".Translate(), true, true, !busy))
            {
                regenEditorBufferAtStart = customPsychotypeBuffer ?? string.Empty;
                awaitingRegen = true;
                Integration.ExternalPsychotypeGenerators.Reroll(pawn);
            }

            TooltipHandler.TipRegion(buttonRect, "PawnDiary.Psychotype.RegenerateExternalTip".Translate());

            if (busy)
            {
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(statusRect, "PawnDiary.Psychotype.RegeneratingExternal".Translate());
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }

            y += ButtonHeight + FieldGap;
        }

        private void DrawPsychotypePicker(Rect rect)
        {
            DiaryPsychotypeDef selected = DiaryPsychotypes.Resolve(pendingPsychotypeDefName);
            string selectedLabel = PsychotypeLabelFor(selected);
            if (Widgets.ButtonText(rect, "PawnDiary.Psychotype.Current".Translate(selectedLabel)))
            {
                string band = component == null ? DiaryPersonas.StageAdult : component.VoiceBandFor(pawn);
                List<FloatMenuOption> options = DiaryPsychotypes.PickerDefsFor(band)
                    .Select(type =>
                    {
                        DiaryPsychotypeDef option = type;
                        return new FloatMenuOption(PsychotypeLabelFor(option), delegate
                        {
                            if (option != null)
                            {
                                pendingPsychotypeDefName = option.defName;
                            }
                        });
                    })
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // ---- Effective draft preview -----------------------------------------------------------------

        /// <summary>
        /// Draws the exact effective voice/outlook rules represented by the current draft. It deliberately
        /// avoids pawn-summary, memory, and external context providers: this is a compact persona preview,
        /// not a synthetic event prompt.
        /// </summary>
        private void DrawEffectivePreviewSection(
            float x,
            float width,
            ref float y,
            WritingStyleResolution styleResolution,
            PsychotypeResolution psychotypeResolution)
        {
            y += SectionGap;
            Widgets.DrawLineHorizontal(x, y, width);
            y += FieldGap;

            Widgets.Label(
                new Rect(x, y, width, SectionTitleHeight),
                "PawnDiary.Profile.PreviewSectionTitle".Translate());
            y += SectionTitleHeight + FieldGap;

            string preview = EffectivePreviewText(styleResolution, psychotypeResolution);
            y += DrawLabeledScrollText(
                new Rect(x, y, width, SmallPromptHeight),
                "PawnDiary.Profile.PreviewLabel".Translate(),
                preview,
                ref effectivePreviewScroll,
                SmallPromptHeight) + FieldGap;
        }

        private string EffectivePreviewText(
            WritingStyleResolution styleResolution,
            PsychotypeResolution psychotypeResolution)
        {
            string styleRule = styleResolution?.rule ?? string.Empty;
            string psychotypeRule = psychotypeResolution?.rule ?? string.Empty;
            if (string.IsNullOrWhiteSpace(styleRule))
            {
                styleRule = "PawnDiary.Profile.PreviewNone".Translate().Resolve();
            }

            if (string.IsNullOrWhiteSpace(psychotypeRule))
            {
                psychotypeRule = "PawnDiary.Profile.PreviewNone".Translate().Resolve();
            }

            return FormatProfileFrame(
                "PawnDiary.Profile.PreviewFrame",
                PsychotypeSourceLabel(psychotypeResolution),
                psychotypeRule,
                WritingStyleSourceLabel(styleResolution),
                styleRule);
        }

        private string WritingStyleSourceLabel(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return "PawnDiary.Profile.SourceBase".Translate().Resolve();
            }

            switch (resolution.source)
            {
                case WritingStyleRuleSource.ExternalApiOverride:
                    return string.IsNullOrWhiteSpace(resolution.externalSourceId)
                        ? "PawnDiary.WritingStyle.ExternalSourceLabel".Translate().Resolve()
                        : resolution.externalSourceId;
                case WritingStyleRuleSource.HediffOverride:
                    return string.IsNullOrWhiteSpace(resolution.hediffStyleLabel)
                        ? (resolution.hediffStyleDefName ?? string.Empty)
                        : resolution.hediffStyleLabel;
                case WritingStyleRuleSource.PawnCustom:
                    return "PawnDiary.Profile.SourceCustom".Translate().Resolve();
                default:
                    return "PawnDiary.Profile.SourceBase".Translate().Resolve();
            }
        }

        private string PsychotypeSourceLabel(PsychotypeResolution resolution)
        {
            if (resolution == null)
            {
                return "PawnDiary.Profile.SourceBase".Translate().Resolve();
            }

            switch (resolution.source)
            {
                case PsychotypeRuleSource.ExternalApiOverride:
                    return string.IsNullOrWhiteSpace(resolution.externalSourceId)
                        ? "PawnDiary.Psychotype.ExternalSourceLabel".Translate().Resolve()
                        : resolution.externalSourceId;
                case PsychotypeRuleSource.PawnCustom:
                    return "PawnDiary.Profile.SourceCustom".Translate().Resolve();
                default:
                    return "PawnDiary.Profile.SourceBase".Translate().Resolve();
            }
        }

        private WritingStyleResolution DraftWritingStyleResolution(WritingStyleResolution saved)
        {
            WritingStyleResolution result = WritingStyleResolutionPolicy.Resolve(
                BaseStylePromptFor(pendingBaseStyleDefName),
                PlayerWritingStyleText.CleanRule(customRuleBuffer),
                saved?.hediffStyleDefName,
                saved?.hediffStyleLabel,
                saved?.hediffRule,
                saved?.externalSourceId,
                saved?.externalRule);
            DiaryPersonaDef selected = DiaryPersonas.Resolve(pendingBaseStyleDefName);
            result.baseStyleDefName = selected?.defName ?? string.Empty;
            result.baseStyleLabel = selected?.label ?? string.Empty;
            return result;
        }

        private PsychotypeResolution DraftPsychotypeResolution(PsychotypeResolution saved)
        {
            DiaryPsychotypeDef selected = DiaryPsychotypes.Resolve(pendingPsychotypeDefName);
            string baseRule = DiaryPsychotypes.RuleFor(selected?.defName);
            PsychotypeResolution result = PsychotypeResolutionPolicy.Resolve(
                baseRule,
                PsychotypeText.CleanRule(customPsychotypeBuffer),
                saved?.externalSourceId,
                saved?.externalRule);
            result.baseTypeDefName = selected?.defName ?? DiaryPsychotypes.NeutralDefName;
            result.baseTypeLabel = selected?.label ?? string.Empty;
            if (component != null
                && !component.PsychotypeLayerEnabled
                && result.source != PsychotypeRuleSource.ExternalApiOverride)
            {
                result.rule = string.Empty;
            }

            return result;
        }

        // ---- Shared drawing helpers -------------------------------------------------------------------

        private float DrawLabeledScrollText(
            Rect rect,
            string label,
            string text,
            ref Vector2 scroll,
            float bodyHeight,
            bool editable = false,
            Action<string> editedText = null)
        {
            float labelHeight = SmallLabelHeight(label, rect.width);
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, labelHeight);
            Widgets.Label(labelRect, label);

            Rect bodyRect = new Rect(rect.x, labelRect.yMax + LabelBodyGap, rect.width, bodyHeight);
            Widgets.DrawBoxSolid(bodyRect, new Color(0f, 0f, 0f, 0.25f));

            float innerWidth = Mathf.Max(20f, bodyRect.width - 16f);
            Rect viewRect = new Rect(bodyRect.x, bodyRect.y, innerWidth, bodyHeight);
            float contentHeight = Text.CalcHeight(text ?? string.Empty, viewRect.width);
            Rect contentRect = new Rect(0f, 0f, viewRect.width, Mathf.Max(viewRect.height, contentHeight));

            Widgets.BeginScrollView(viewRect, ref scroll, contentRect);
            Rect textRect = new Rect(contentRect.x, contentRect.y, contentRect.width, contentRect.height);
            if (editable)
            {
                string edited = Widgets.TextArea(textRect, text ?? string.Empty);
                editedText?.Invoke(edited);
            }
            else
            {
                Widgets.Label(textRect, text ?? string.Empty);
            }

            Widgets.EndScrollView();
            return labelRect.height + LabelBodyGap + bodyHeight;
        }

        private float DrawMessagePanel(Rect rect, string message, Color color)
        {
            float textWidth = Mathf.Max(1f, rect.width - Padding * 2f);
            float height = Mathf.Max(
                ExplanationMinHeight,
                Mathf.Ceil(Text.CalcHeight(message ?? string.Empty, textWidth) + Padding * 2f));
            Rect panelRect = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(panelRect, color);
            Widgets.Label(panelRect.ContractedBy(Padding), message);
            return height;
        }

        // Total height of the scrolling content, so the scroll view is sized before drawing.
        private float MeasureContentHeight(
            float width,
            WritingStyleResolution styleResolution,
            PsychotypeResolution psychotypeResolution,
            bool showDeveloperKnowledge,
            LoreMemorySnapshotForDev loreMemory,
            IReadOnlyList<ImportantMemoryRecord> importantMemories)
        {
            float h = 0f;

            // Diary generation section.
            h += SectionTitleHeight + FieldGap;
            h += ButtonHeight + FieldGap;
            h += MessagePanelHeight(DiaryGenerationStatusMessage(), width);

            // Style section.
            h += SectionGap + FieldGap;
            h += SectionTitleHeight + FieldGap;
            h += ButtonHeight + FieldGap;
            h += MessagePanelHeight(WritingStyleStatusMessage(styleResolution), width);
            h += LabeledScrollTextHeight(
                "PawnDiary.WritingStyle.BasePrompt".Translate(),
                width,
                PromptAreaHeight) + FieldGap;
            h += LabeledScrollTextHeight(
                WritingStyleCustomLabel(),
                width,
                PromptAreaHeight) + FieldGap;

            // Psychotype section.
            h += SectionGap + FieldGap; // gap + separator line
            h += SectionTitleHeight + FieldGap;
            int controlRows = DiaryUiPolicy.PsychotypeControlRowCount(
                width,
                PsychotypeMinimumPickerWidth,
                PsychotypeRerollWidth,
                PsychotypePinWidth,
                PsychotypeControlGap);
            h += controlRows * ButtonHeight
                + (controlRows - 1) * PsychotypeControlGap
                + FieldGap;
            if (pawn != null && Integration.ExternalPsychotypeGenerators.CanReroll(pawn))
            {
                h += ButtonHeight + FieldGap; // external Regenerate row
            }

            h += LabeledScrollTextHeight(
                "PawnDiary.Psychotype.BaseRule".Translate(),
                width,
                SmallPromptHeight) + FieldGap;
            h += LabeledScrollTextHeight(
                PsychotypeCustomLabel(),
                width,
                SmallPromptHeight) + FieldGap;
            h += MessagePanelHeight(PsychotypeHintMessage(psychotypeResolution), width);

            // Effective voice/outlook preview.
            h += SectionGap + FieldGap;
            h += SectionTitleHeight + FieldGap;
            h += LabeledScrollTextHeight(
                "PawnDiary.Profile.PreviewLabel".Translate(),
                width,
                SmallPromptHeight) + FieldGap;

            if (showDeveloperKnowledge)
            {
                h += LoreMemorySectionHeight(width, loreMemory);
                h += MemorySectionHeight(width, importantMemories);
            }

            return h;
        }

        private float MessagePanelHeight(string message, float width)
        {
            if (message == null)
            {
                return 0f;
            }

            float textWidth = Mathf.Max(1f, width - Padding * 2f);
            return Mathf.Max(
                ExplanationMinHeight,
                Mathf.Ceil(Text.CalcHeight(message, textWidth) + Padding * 2f)) + FieldGap;
        }

        private static float LabeledScrollTextHeight(string label, float width, float bodyHeight)
        {
            return SmallLabelHeight(label, width) + LabelBodyGap + bodyHeight;
        }

        private static float SmallLabelHeight(string label, float width)
        {
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                return Mathf.Max(
                    SmallLabelMinimumHeight,
                    Mathf.Ceil(Text.CalcHeight(label ?? string.Empty, Mathf.Max(1f, width))));
            }
            finally
            {
                Text.Font = previousFont;
            }
        }

        private string WritingStyleCustomLabel()
        {
            return "PawnDiary.WritingStyle.CustomPrompt".Translate().ToString()
                + "  " + (customRuleBuffer ?? string.Empty).Length
                + "/" + PlayerWritingStyleText.MaxRuleChars;
        }

        private string PsychotypeCustomLabel()
        {
            return "PawnDiary.Psychotype.CustomRule".Translate().ToString()
                + "  " + (customPsychotypeBuffer ?? string.Empty).Length
                + "/" + PsychotypeText.MaxCustomRuleChars;
        }

        private string WritingStyleStatusMessage(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return "PawnDiary.WritingStyle.CurrentBase".Translate(
                    LabelFor(DiaryPersonas.Resolve(pendingBaseStyleDefName)));
            }

            if (resolution.source == WritingStyleRuleSource.ExternalApiOverride)
            {
                string source = string.IsNullOrWhiteSpace(resolution.externalSourceId)
                    ? "PawnDiary.WritingStyle.ExternalSourceLabel".Translate().ToString()
                    : resolution.externalSourceId;
                string message = "PawnDiary.WritingStyle.CurrentExternal".Translate(source);
                if (!string.IsNullOrWhiteSpace(customRuleBuffer))
                {
                    message += "\n" + "PawnDiary.WritingStyle.SavedCustomWaiting".Translate();
                }

                return message;
            }

            if (resolution.source == WritingStyleRuleSource.HediffOverride)
            {
                string label = string.IsNullOrWhiteSpace(resolution.hediffStyleLabel)
                    ? (resolution.hediffStyleDefName ?? string.Empty)
                    : resolution.hediffStyleLabel;
                string message = "PawnDiary.WritingStyle.CurrentHediff".Translate(label);
                if (!string.IsNullOrWhiteSpace(customRuleBuffer))
                {
                    message += "\n" + "PawnDiary.WritingStyle.SavedCustomWaiting".Translate();
                }

                return message;
            }

            if (!string.IsNullOrWhiteSpace(PlayerWritingStyleText.CleanRule(customRuleBuffer)))
            {
                return "PawnDiary.WritingStyle.CurrentCustom".Translate();
            }

            return "PawnDiary.WritingStyle.CurrentBase".Translate(
                LabelFor(DiaryPersonas.Resolve(pendingBaseStyleDefName)));
        }

        private static Color WritingStyleStatusColor(WritingStyleResolution resolution)
        {
            bool overridden = resolution != null
                && (resolution.source == WritingStyleRuleSource.ExternalApiOverride
                    || resolution.source == WritingStyleRuleSource.HediffOverride);
            return StatusPanelColor(overridden);
        }

        // Reuses the dialog's established calm/warning status palette for every normal-play panel.
        private static Color StatusPanelColor(bool warning)
        {
            return warning
                ? new Color(0.22f, 0.14f, 0.03f, 0.8f)
                : new Color(0.05f, 0.18f, 0.10f, 0.7f);
        }

        // The psychotype hint panel: a disabled-layer note, and/or an active external-override explanation.
        private string PsychotypeHintMessage(PsychotypeResolution resolution)
        {
            string message = null;
            if (component != null && !component.PsychotypeLayerEnabled)
            {
                message = "PawnDiary.Psychotype.Disabled".Translate();
            }

            if (resolution != null && resolution.source == PsychotypeRuleSource.ExternalApiOverride)
            {
                string source = string.IsNullOrWhiteSpace(resolution.externalSourceId)
                    ? "PawnDiary.Psychotype.ExternalSourceLabel".Translate().ToString()
                    : resolution.externalSourceId;
                string overrideText = "PawnDiary.Psychotype.OverrideExternal".Translate(source);
                if (PsychotypeResolutionPolicy.CustomSuppressedByOverride(resolution))
                {
                    overrideText += "\n" + "PawnDiary.Psychotype.CustomInactiveDueToOverride".Translate();
                }

                message = message == null ? overrideText : message + "\n" + overrideText;
            }

            return message;
        }

        // ---- Buttons + commit -------------------------------------------------------------------------

        private void DrawButtons(Rect rect)
        {
            float gap = 6f;
            int buttonCount = 4;
            float buttonWidth = (rect.width - gap * (buttonCount - 1)) / buttonCount;

            Rect saveRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect resetRect = new Rect(saveRect.xMax + gap, rect.y, buttonWidth, rect.height);
            Rect loadRect = new Rect(resetRect.xMax + gap, rect.y, buttonWidth, rect.height);
            Rect closeRect = new Rect(loadRect.xMax + gap, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(saveRect, "PawnDiary.WritingStyle.SaveForPawn".Translate()))
            {
                RequestSave();
            }

            if (Widgets.ButtonText(resetRect, "PawnDiary.WritingStyle.ResetToBase".Translate()))
            {
                ResetToBase();
                Messages.Message(
                    "PawnDiary.Profile.VoiceResetDraft".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }

            if (Widgets.ButtonText(loadRect, "PawnDiary.WritingStyle.LoadBasePrompt".Translate()))
            {
                customRuleBuffer = BaseStylePromptFor(pendingBaseStyleDefName);
            }

            if (Widgets.ButtonText(closeRect, "PawnDiary.WritingStyle.Close".Translate()))
            {
                Close();
            }
        }

        /// <summary>
        /// Decides whether re-enabling needs confirmation before applying anything. This ordering is the
        /// atomicity boundary: declining the dialog must leave generation, voice, and outlook untouched.
        /// </summary>
        private void RequestSave()
        {
            // An untouched switch is not permission to overwrite a live API/integration change made
            // while this window was open. Save voice/outlook only in that case, with no fresh backlog
            // scan, confirmation, or generation setter.
            bool generationEdited = pendingGenerationEnabled != originalGenerationEnabled;
            if (!generationEdited)
            {
                CompleteSave(false);
                return;
            }

            // The window's status uses its stable open-time snapshot, but another mod/API may have
            // changed generation since then. Re-read once at the Save boundary so the decision and final
            // setter are relative to the state that actually exists now, never to stale UI state.
            bool saveTimeGenerationEnabled = component != null && pawn != null
                ? component.DiaryGenerationEnabledForProfile(pawn)
                : originalGenerationEnabled;
            int saveTimeBacklogCount = component != null && pawn != null
                ? component.PendingGenerationBacklogCountForProfile(pawn)
                : resumableBacklogCount;
            DiaryPawnProfileGenerationDecision decision =
                DiaryPawnProfilePolicy.DecideGenerationChange(
                    saveTimeGenerationEnabled,
                    pendingGenerationEnabled,
                    saveTimeBacklogCount);
            if (decision != DiaryPawnProfileGenerationDecision.EnableWithConfirmation)
            {
                CompleteSave(true);
                return;
            }

            string name = pawn == null ? string.Empty : pawn.LabelShortCap;
            Dialog_MessageBox confirmation = new Dialog_MessageBox(
                FormatProfileFrame(
                    "PawnDiary.Profile.ResumeConfirm",
                    name,
                    saveTimeBacklogCount),
                "PawnDiary.Profile.ResumeConfirmButton".Translate(),
                delegate { CompleteSave(true); },
                "PawnDiary.Profile.ResumeCancelButton".Translate(),
                null,
                "PawnDiary.Profile.ResumeConfirmTitle".Translate());
            Find.WindowStack.Add(confirmation);
        }

        private void CompleteSave(bool generationEdited)
        {
            if (Save(generationEdited))
            {
                Messages.Message(
                    "PawnDiary.Profile.Saved".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                Close();
                return;
            }

            Messages.Message(
                "PawnDiary.Profile.SaveFailed".Translate(),
                MessageTypeDefOf.RejectInput,
                false);
        }

        private bool Save(bool generationEdited)
        {
            if (component == null || pawn == null)
            {
                return false;
            }

            // Re-read at the actual commit boundary as well. The confirmation may have stayed open while
            // an integration changed generation; comparing against the click-time value could otherwise
            // replay an enable and scan/requeue the same backlog a second time.
            bool commitTimeGenerationEnabled = generationEdited
                ? component.DiaryGenerationEnabledForProfile(pawn)
                : pendingGenerationEnabled;
            bool ok = true;
            string cleanedWritingRule = PlayerWritingStyleText.CleanRule(customRuleBuffer);
            string cleanedPsychotypeRule = PsychotypeText.CleanRule(customPsychotypeBuffer);
            bool writingBaseChanged = !string.Equals(
                pendingBaseStyleDefName,
                originalBaseStyleDefName,
                StringComparison.Ordinal);
            bool psychotypeBaseChanged = !string.Equals(
                pendingPsychotypeDefName,
                originalPsychotypeDefName,
                StringComparison.Ordinal);

            bool writingCustomChanged = !string.Equals(
                cleanedWritingRule,
                originalCustomRule,
                StringComparison.Ordinal);
            bool psychotypeCustomChanged = !string.Equals(
                cleanedPsychotypeRule,
                originalCustomPsychotypeRule,
                StringComparison.Ordinal);

            // A changed base pick or newly edited nonblank rule pins the corresponding layer. A saved,
            // nonblank-but-unpinned custom rule must remain unpinned when Save makes no changes.
            if (writingBaseChanged
                || (writingCustomChanged && !string.IsNullOrWhiteSpace(cleanedWritingRule)))
            {
                pendingWritingStylePinned = true;
            }

            if (psychotypeBaseChanged
                || (psychotypeCustomChanged && !string.IsNullOrWhiteSpace(cleanedPsychotypeRule)))
            {
                pendingPsychotypePinned = true;
            }

            // Writing style. Only write the base style when the player actually changed it: for a pawn
            // with no record yet, SetPersona would create+roll a record and then overwrite the fresh roll
            // with the seeded default, silently discarding the pawn's rolled style.
            if (!string.IsNullOrWhiteSpace(pendingBaseStyleDefName)
                && writingBaseChanged)
            {
                ok &= component.SetPersona(pawn, pendingBaseStyleDefName);
            }

            if (writingCustomChanged)
            {
                ok &= component.SetCustomWritingStyleRule(pawn, cleanedWritingRule);
            }

            if (pendingWritingStylePinned != originalWritingStylePinned)
            {
                ok &= component.SetWritingStylePinned(pawn, pendingWritingStylePinned);
            }

            // Psychotype. Same first-time-roll guard as the writing style: only write the base outlook
            // when the player changed it, so opening + saving unchanged does not replace a freshly rolled
            // outlook with the seeded Neutral default.
            if (!string.IsNullOrWhiteSpace(pendingPsychotypeDefName)
                && psychotypeBaseChanged)
            {
                ok &= component.SetPsychotype(pawn, pendingPsychotypeDefName);
            }

            if (psychotypeCustomChanged)
            {
                ok &= component.SetCustomPsychotypeRule(pawn, cleanedPsychotypeRule);
            }

            if (pendingPsychotypePinned != originalPsychotypePinned)
            {
                ok &= component.SetPsychotypePinned(pawn, pendingPsychotypePinned);
            }

            // Generation is deliberately last. A failed/ineligible voice save must never resume queued
            // LLM work, and an unchanged true value must never requeue work through the existing setter.
            if (ok
                && generationEdited
                && pendingGenerationEnabled != commitTimeGenerationEnabled)
            {
                ok &= component.TrySetDiaryGenerationEnabledForIntegration(
                    pawn,
                    pendingGenerationEnabled);
            }

            return ok;
        }

        internal bool IsFor(Pawn candidate)
        {
            return candidate != null && candidate == pawn;
        }

        // Clears both custom rules and unpins both layers in the DRAFT only. Explicit Save is the sole
        // persistence boundary; closing after Reset must leave the pawn unchanged.
        private void ResetToBase()
        {
            pendingBaseStyleDefName = originalBaseStyleDefName;
            pendingPsychotypeDefName = originalPsychotypeDefName;
            customRuleBuffer = string.Empty;
            customPsychotypeBuffer = string.Empty;
            pendingWritingStylePinned = false;
            pendingPsychotypePinned = false;
        }

        // ---- Small helpers ----------------------------------------------------------------------------

        private static string ClampInput(string text, int maxChars)
        {
            string next = text ?? string.Empty;
            if (next.Length > maxChars)
            {
                next = TextTruncation.SafePrefix(next, maxChars);
            }

            return next;
        }

        private static string LabelFor(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate().ToString();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? (persona.defName ?? string.Empty) : persona.label;
        }

        private static string PsychotypeLabelFor(DiaryPsychotypeDef type)
        {
            if (type == null)
            {
                return "PawnDiary.Psychotype.NeutralLabel".Translate().ToString();
            }

            return string.IsNullOrWhiteSpace(type.label) ? (type.defName ?? string.Empty) : type.label;
        }

        private static string BaseStylePromptFor(string defName)
        {
            return DiaryPersonas.RuleFor(defName);
        }

        private static string FormatProfileFrame(string key, params object[] values)
        {
            string frame = key.Translate().Resolve();
            try
            {
                // Verse's Translate(args) can sentence-case inserted player text (including pawn names).
                // Resolve the argument-free frame, then format the placeholders without altering values.
                return string.Format(frame, values);
            }
            catch (FormatException)
            {
                return frame;
            }
        }

    }
}
