// Player-facing dialog for editing one pawn's VOICE from the Diary tab: the writing style (sentence
// mechanics) and the psychotype (outlook/temperament). Both layers are edited here in one window, each
// with a base picker, a read-only base preview, an editable pawn-specific custom rule, and an
// explanation panel when a temporary override (hediff or external API) shadows the player's choice. A
// manual pick / custom edit / re-roll pins the layer so it is not auto-re-rolled when the pawn grows up.
//
// RimWorld IMGUI draws this window repeatedly, so editable buffers live as fields and are only flushed
// to the diary record through explicit Save/Reset button clicks — never during a draw pass. The whole
// content area scrolls so both sections fit on small screens.
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
    /// Modal window for inspecting and editing one pawn's writing style and psychotype. Writes only the
    /// pawn-specific custom rules, selected base defs, and pin flags — never the global catalog or XML Defs.
    /// </summary>
    internal sealed class Dialog_PawnWritingStyle : Window
    {
        // ---- Writing-style editing state ----
        private string customRuleBuffer;
        private string pendingBaseStyleDefName;
        private readonly string originalBaseStyleDefName;
        private bool pendingWritingStylePinned;

        // ---- Psychotype editing state ----
        private string customPsychotypeBuffer;
        private string pendingPsychotypeDefName;
        private bool pendingPsychotypePinned;

        private readonly Pawn pawn;
        private readonly DiaryGameComponent component;

        private Vector2 contentScroll;
        private Vector2 basePromptScroll;
        private Vector2 customScroll;
        private Vector2 effectiveScroll;
        private Vector2 psychotypeBaseScroll;
        private Vector2 psychotypeCustomScroll;

        // Layout constants (font line heights per AGENTS.md / UI lore: Tiny 18, Small 22, Medium 28).
        private const float HeaderHeight = 32f;
        private const float LineHeight = 22f;
        private const float LabelHeight = 20f;
        private const float ButtonHeight = 30f;
        private const float PromptAreaHeight = 96f;
        private const float SmallPromptHeight = 72f;
        private const float SectionTitleHeight = 24f;
        private const float SectionGap = 12f;
        private const float Padding = 14f;
        private const float FieldGap = 6f;
        private const float ExplanationMinHeight = 40f;
        private const float ExplanationMaxHeight = 96f;

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
            WritingStyleResolution style = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);
            pendingBaseStyleDefName = string.IsNullOrWhiteSpace(style.baseStyleDefName)
                ? (DiaryPersonas.Default?.defName ?? string.Empty)
                : style.baseStyleDefName;
            originalBaseStyleDefName = pendingBaseStyleDefName;
            pendingWritingStylePinned = component != null && component.WritingStylePinnedFor(pawn);

            // Seed the psychotype editors from the saved record (resolving ensures a band/backfill first).
            customPsychotypeBuffer = component == null ? string.Empty : component.CustomPsychotypeRuleFor(pawn);
            PsychotypeResolution psycho = component == null
                ? PsychotypeResolutionPolicy.Resolve(null, null, null, null)
                : component.ResolvePsychotypeFor(pawn);
            pendingPsychotypeDefName = string.IsNullOrWhiteSpace(psycho.baseTypeDefName)
                ? DiaryPsychotypes.NeutralDefName
                : psycho.baseTypeDefName;
            pendingPsychotypePinned = component != null && component.PsychotypePinnedFor(pawn);
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
            WritingStyleResolution styleResolution = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);
            // Display-only read (no EnsureVoiceStage): never rolls/mutates during a repaint. The
            // constructor already backfilled via the mutating resolve when the editor opened.
            PsychotypeResolution psychotypeResolution = component == null
                ? PsychotypeResolutionPolicy.Resolve(null, null, null, null)
                : component.ResolvePsychotypeForDisplay(pawn);

            Rect buttonRow = new Rect(inRect.x, inRect.yMax - ButtonHeight - Padding, inRect.width, ButtonHeight);
            Rect scrollOuter = new Rect(
                inRect.x,
                inRect.y + HeaderHeight + FieldGap,
                inRect.width,
                buttonRow.y - FieldGap - (inRect.y + HeaderHeight + FieldGap));

            float innerWidth = scrollOuter.width - 16f; // reserve scrollbar width
            float contentHeight = MeasureContentHeight(innerWidth, styleResolution, psychotypeResolution);
            Rect contentRect = new Rect(0f, 0f, innerWidth, contentHeight);

            Widgets.BeginScrollView(scrollOuter, ref contentScroll, contentRect);
            float y = 0f;
            DrawStyleSection(contentRect.x, innerWidth, ref y, styleResolution);
            DrawPsychotypeSection(contentRect.x, innerWidth, ref y, psychotypeResolution);
            Widgets.EndScrollView();

            DrawButtons(buttonRow);
        }

        private string Title()
        {
            string name = pawn == null ? string.Empty : pawn.LabelShortCap;
            return "PawnDiary.WritingStyle.EditorTitle".Translate(name);
        }

        // ---- Writing-style section --------------------------------------------------------------------

        private void DrawStyleSection(float x, float width, ref float y, WritingStyleResolution resolution)
        {
            DrawBaseStylePicker(new Rect(x, y, width, LineHeight), resolution);
            y += LineHeight + FieldGap;

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                "PawnDiary.WritingStyle.BasePrompt".Translate(),
                BaseStylePromptFor(pendingBaseStyleDefName),
                ref basePromptScroll,
                PromptAreaHeight) + FieldGap;

            string customLabel = "PawnDiary.WritingStyle.CustomPrompt".Translate().ToString()
                + "  " + customRuleBuffer.Length + "/" + PlayerWritingStyleText.MaxRuleChars;
            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                customLabel,
                customRuleBuffer,
                ref customScroll,
                PromptAreaHeight,
                editable: true,
                editedText: text => customRuleBuffer = ClampInput(text, PlayerWritingStyleText.MaxRuleChars)) + FieldGap;

            y += DrawLabeledScrollText(
                new Rect(x, y, width, PromptAreaHeight),
                "PawnDiary.WritingStyle.EffectivePrompt".Translate(),
                EffectiveStylePromptForDisplay(resolution),
                ref effectiveScroll,
                PromptAreaHeight) + FieldGap;

            string overrideMessage = WritingStyleOverrideMessage(resolution);
            if (overrideMessage != null)
            {
                y += DrawMessagePanel(new Rect(x, y, width, 0f), overrideMessage,
                    new Color(0.12f, 0.10f, 0.04f, 0.55f)) + FieldGap;
            }
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
                                pendingWritingStylePinned = true; // a manual pick pins the layer
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

            // Picker + Re-roll + Pin toggle on one row.
            float pinWidth = 110f;
            float rerollWidth = 90f;
            float gap = 6f;
            float pickerWidth = Mathf.Max(120f, width - pinWidth - rerollWidth - gap * 2f);
            Rect pickerRect = new Rect(x, y, pickerWidth, ButtonHeight);
            Rect rerollRect = new Rect(pickerRect.xMax + gap, y, rerollWidth, ButtonHeight);
            Rect pinRect = new Rect(rerollRect.xMax + gap, y, pinWidth, ButtonHeight);
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
            y += ButtonHeight + FieldGap;

            y += DrawLabeledScrollText(
                new Rect(x, y, width, SmallPromptHeight),
                "PawnDiary.Psychotype.BaseRule".Translate(),
                DiaryPsychotypes.RuleFor(pendingPsychotypeDefName),
                ref psychotypeBaseScroll,
                SmallPromptHeight) + FieldGap;

            string customLabel = "PawnDiary.Psychotype.CustomRule".Translate().ToString()
                + "  " + customPsychotypeBuffer.Length + "/" + PsychotypeText.MaxCustomRuleChars;
            y += DrawLabeledScrollText(
                new Rect(x, y, width, SmallPromptHeight),
                customLabel,
                customPsychotypeBuffer,
                ref psychotypeCustomScroll,
                SmallPromptHeight,
                editable: true,
                editedText: text =>
                {
                    string clamped = ClampInput(text, PsychotypeText.MaxCustomRuleChars);
                    if (!string.Equals(clamped, customPsychotypeBuffer, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(clamped))
                    {
                        pendingPsychotypePinned = true; // authoring a custom outlook pins the layer
                    }

                    customPsychotypeBuffer = clamped;
                }) + FieldGap;

            string hint = PsychotypeHintMessage(resolution);
            if (hint != null)
            {
                y += DrawMessagePanel(new Rect(x, y, width, 0f), hint,
                    new Color(0.12f, 0.10f, 0.04f, 0.55f)) + FieldGap;
            }
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
                                pendingPsychotypePinned = true; // a manual pick pins the layer
                            }
                        });
                    })
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
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
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, LabelHeight);
            Widgets.Label(labelRect, label);

            Rect bodyRect = new Rect(rect.x, labelRect.yMax + 2f, rect.width, bodyHeight);
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
            return labelRect.height + 2f + bodyHeight;
        }

        private float DrawMessagePanel(Rect rect, string message, Color color)
        {
            float height = Mathf.Clamp(
                Text.CalcHeight(message, rect.width - Padding * 2f) + Padding * 2f,
                ExplanationMinHeight,
                ExplanationMaxHeight);
            Rect panelRect = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(panelRect, color);
            Widgets.Label(panelRect.ContractedBy(Padding), message);
            return height;
        }

        // Total height of the scrolling content, so the scroll view is sized before drawing.
        private float MeasureContentHeight(float width, WritingStyleResolution styleResolution,
            PsychotypeResolution psychotypeResolution)
        {
            float h = 0f;

            // Style section.
            h += LineHeight + FieldGap;
            h += (LabelHeight + 2f + PromptAreaHeight) + FieldGap;
            h += (LabelHeight + 2f + PromptAreaHeight) + FieldGap;
            h += (LabelHeight + 2f + PromptAreaHeight) + FieldGap;
            h += MessagePanelHeight(WritingStyleOverrideMessage(styleResolution), width);

            // Psychotype section.
            h += SectionGap + FieldGap; // gap + separator line
            h += SectionTitleHeight + FieldGap;
            h += ButtonHeight + FieldGap;
            h += (LabelHeight + 2f + SmallPromptHeight) + FieldGap;
            h += (LabelHeight + 2f + SmallPromptHeight) + FieldGap;
            h += MessagePanelHeight(PsychotypeHintMessage(psychotypeResolution), width);

            return h;
        }

        private float MessagePanelHeight(string message, float width)
        {
            if (message == null)
            {
                return 0f;
            }

            return Mathf.Clamp(
                Text.CalcHeight(message, width - Padding * 2f) + Padding * 2f,
                ExplanationMinHeight,
                ExplanationMaxHeight) + FieldGap;
        }

        private static string WritingStyleOverrideMessage(WritingStyleResolution resolution)
        {
            if (resolution == null
                || (resolution.source != WritingStyleRuleSource.ExternalApiOverride
                    && resolution.source != WritingStyleRuleSource.HediffOverride))
            {
                return null;
            }

            string explanation;
            if (resolution.source == WritingStyleRuleSource.ExternalApiOverride)
            {
                string source = string.IsNullOrWhiteSpace(resolution.externalSourceId)
                    ? "PawnDiary.WritingStyle.ExternalSourceLabel".Translate().ToString()
                    : resolution.externalSourceId;
                explanation = "PawnDiary.WritingStyle.OverrideExternal".Translate(source);
            }
            else
            {
                string label = string.IsNullOrWhiteSpace(resolution.hediffStyleLabel)
                    ? (resolution.hediffStyleDefName ?? string.Empty)
                    : resolution.hediffStyleLabel;
                explanation = "PawnDiary.WritingStyle.OverrideHediff".Translate(label);
            }

            if (WritingStyleResolutionPolicy.CustomSuppressedByOverride(resolution))
            {
                explanation += "\n" + "PawnDiary.WritingStyle.CustomInactiveDueToOverride".Translate();
            }

            return explanation;
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
                if (Save())
                {
                    Messages.Message("PawnDiary.WritingStyle.Saved".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    Close();
                }
                else
                {
                    Messages.Message("PawnDiary.WritingStyle.SaveFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }

            if (Widgets.ButtonText(resetRect, "PawnDiary.WritingStyle.ResetToBase".Translate()))
            {
                ResetToBase();
                Messages.Message("PawnDiary.WritingStyle.Reset".Translate(), MessageTypeDefOf.NeutralEvent, false);
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

        private bool Save()
        {
            if (component == null || pawn == null)
            {
                return false;
            }

            bool ok = true;

            // Writing style.
            if (!string.IsNullOrWhiteSpace(pendingBaseStyleDefName))
            {
                ok &= component.SetPersona(pawn, pendingBaseStyleDefName);
            }

            ok &= component.SetCustomWritingStyleRule(pawn, customRuleBuffer);
            // Changing the base style or authoring a custom rule counts as a manual pick.
            if (!string.Equals(pendingBaseStyleDefName, originalBaseStyleDefName, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(customRuleBuffer))
            {
                pendingWritingStylePinned = true;
            }

            ok &= component.SetWritingStylePinned(pawn, pendingWritingStylePinned);

            // Psychotype.
            if (!string.IsNullOrWhiteSpace(pendingPsychotypeDefName))
            {
                ok &= component.SetPsychotype(pawn, pendingPsychotypeDefName);
            }

            ok &= component.SetCustomPsychotypeRule(pawn, customPsychotypeBuffer);
            ok &= component.SetPsychotypePinned(pawn, pendingPsychotypePinned);

            // Reflect the sanitized saves back into the buffers so a follow-up edit starts clean.
            customRuleBuffer = component.CustomWritingStyleRuleFor(pawn);
            customPsychotypeBuffer = component.CustomPsychotypeRuleFor(pawn);
            return ok;
        }

        internal bool IsFor(Pawn candidate)
        {
            return candidate != null && candidate == pawn;
        }

        // Clears both custom rules, unpins both layers, and repoints the pickers to the current saved
        // (auto-managed) base, so the pawn's voice goes back to being managed automatically.
        private void ResetToBase()
        {
            if (component == null || pawn == null)
            {
                return;
            }

            component.SetCustomWritingStyleRule(pawn, string.Empty);
            component.SetCustomPsychotypeRule(pawn, string.Empty);
            component.SetWritingStylePinned(pawn, false);
            component.SetPsychotypePinned(pawn, false);

            WritingStyleResolution style = component.ResolveWritingStyleFor(pawn);
            pendingBaseStyleDefName = string.IsNullOrWhiteSpace(style.baseStyleDefName)
                ? (DiaryPersonas.Default?.defName ?? string.Empty)
                : style.baseStyleDefName;
            PsychotypeResolution psycho = component.ResolvePsychotypeFor(pawn);
            pendingPsychotypeDefName = string.IsNullOrWhiteSpace(psycho.baseTypeDefName)
                ? DiaryPsychotypes.NeutralDefName
                : psycho.baseTypeDefName;

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

        private string EffectiveStylePromptForDisplay(WritingStyleResolution resolution)
        {
            if (resolution == null)
            {
                return string.Empty;
            }

            if (resolution.source == WritingStyleRuleSource.ExternalApiOverride
                || resolution.source == WritingStyleRuleSource.HediffOverride)
            {
                return resolution.rule;
            }

            string editedCustom = PlayerWritingStyleText.CleanRule(customRuleBuffer);
            if (!string.IsNullOrWhiteSpace(editedCustom))
            {
                return editedCustom;
            }

            return BaseStylePromptFor(pendingBaseStyleDefName);
        }
    }
}
