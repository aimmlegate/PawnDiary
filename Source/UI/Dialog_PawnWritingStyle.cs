// Player-facing dialog for editing one pawn's writing style from the Diary tab. It exposes the base
// style picker, a read-only preview of the selected base prompt, an editable pawn-specific custom
// prompt, an effective-prompt preview, and an explanation panel when a temporary override (hediff or
// external API) shadows the player's choice.
//
// RimWorld IMGUI draws this window repeatedly, so the editable buffer lives as a field and is only
// flushed to the diary record through explicit Save/Reset button clicks — never during a draw pass.
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
    /// Modal window for inspecting and editing one pawn's writing style. Reads the effective
    /// resolution (base / custom / hediff / external override) and writes only the pawn-specific
    /// custom prompt and selected base style — never the global catalog or XML Defs.
    /// </summary>
    internal sealed class Dialog_PawnWritingStyle : Window
    {
        // Editable text buffer. Persisted across draw calls as a field; flushed to the record only on
        // an explicit Save click. Captures the cursor/caret state through Widgets.TextArea's internal
        // text editor keyed by this same string identity.
        private string customRuleBuffer;

        // Selected base style while editing. Defaults to the pawn's saved selection and is committed on
        // Save alongside the custom prompt. Held separately so the dialog can preview changes before
        // they are committed.
        private string pendingBaseStyleDefName;

        private readonly Pawn pawn;
        private readonly DiaryGameComponent component;

        private Vector2 basePromptScroll;
        private Vector2 customScroll;
        private Vector2 effectiveScroll;

        // Layout constants. These match the font line heights called out in AGENTS.md / the UI lore:
        // Tiny 18, Small 22, Medium 28. The dialog uses Small for body text and Medium for the header.
        private const float HeaderHeight = 32f;
        private const float LineHeight = 22f;
        private const float LabelHeight = 20f;
        private const float ButtonHeight = 30f;
        private const float PromptAreaHeight = 96f;
        private const float Padding = 14f;
        private const float FieldGap = 6f;
        private const float ExplanationMinHeight = 40f;
        private const float ExplanationMaxHeight = 96f;

        /// <summary>
        /// Creates the dialog for <paramref name="pawn"/>. Callers must be on the main thread and pass
        /// a non-null pawn + diary component (the Diary tab guarantees both).
        /// </summary>
        public Dialog_PawnWritingStyle(Pawn pawn, DiaryGameComponent component)
        {
            this.pawn = pawn;
            this.component = component;
            forcePause = false;
            draggable = true;
            resizeable = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;

            // Seed the editable buffer from the saved custom prompt (already sanitized on save/load).
            customRuleBuffer = component == null ? string.Empty : component.CustomWritingStyleRuleFor(pawn);

            // Seed the base-style picker from the pawn's current saved selection.
            WritingStyleResolution current = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);
            pendingBaseStyleDefName = string.IsNullOrWhiteSpace(current.baseStyleDefName)
                ? (DiaryPersonas.Default?.defName ?? string.Empty)
                : current.baseStyleDefName;
        }

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(640f, UI.screenWidth - 64f);
                float height = Mathf.Min(560f, UI.screenHeight - 64f);
                return new Vector2(width, height);
            }
        }

        /// <summary>
        /// Draws the dialog contents. Called many times per second by RimWorld's IMGUI loop; this must
        /// be side-effect free with respect to the saved record except inside explicit button handlers.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight), Title());
            Text.Font = GameFont.Small;

            // Re-resolve every draw so override status (e.g. a hediff clearing mid-dialog) stays live.
            // This is read-only with respect to the record; the editable buffer is the field above.
            WritingStyleResolution resolution = component == null
                ? HediffPersonaOverrides.ResolveWritingStyle(null, null, null, null, null)
                : component.ResolveWritingStyleFor(pawn);

            float y = inRect.y + HeaderHeight + FieldGap;
            float contentWidth = inRect.width;

            // Base style picker.
            Rect pickerRect = new Rect(inRect.x, y, contentWidth, LineHeight);
            DrawBaseStylePicker(pickerRect, resolution);
            y += pickerRect.height + FieldGap;

            // Read-only base style prompt preview.
            y += DrawLabeledScrollText(
                new Rect(inRect.x, y, contentWidth, PromptAreaHeight),
                "PawnDiary.WritingStyle.BasePrompt".Translate(),
                BasePromptFor(pendingBaseStyleDefName),
                ref basePromptScroll) + FieldGap;

            // Editable custom prompt. The buffer is mutated in place by the text area; commit happens
            // only on Save.
            y += DrawLabeledScrollText(
                new Rect(inRect.x, y, contentWidth, PromptAreaHeight),
                "PawnDiary.WritingStyle.CustomPrompt".Translate(),
                customRuleBuffer,
                ref customScroll,
                editable: true,
                editedText: text => customRuleBuffer = text ?? string.Empty) + FieldGap;

            // Effective prompt preview (reflects the live override status, not the edited buffer).
            y += DrawLabeledScrollText(
                new Rect(inRect.x, y, contentWidth, PromptAreaHeight),
                "PawnDiary.WritingStyle.EffectivePrompt".Translate(),
                EffectivePromptForDisplay(resolution),
                ref effectiveScroll) + FieldGap;

            // Override explanation panel (only drawn when an override is active or the custom prompt
            // is being shadowed).
            y += DrawOverrideExplanation(new Rect(inRect.x, y, contentWidth, 0f), resolution) + FieldGap;

            // Buttons anchored to the bottom of the window.
            DrawButtons(new Rect(inRect.x, inRect.yMax - ButtonHeight - Padding, contentWidth, ButtonHeight), resolution);
        }

        private string Title()
        {
            string name = pawn == null ? string.Empty : pawn.LabelShortCap;
            return "PawnDiary.WritingStyle.EditorTitle".Translate(name);
        }

        /// <summary>
        /// Draws the base-style picker button and opens a FloatMenu of <see cref="DiaryPersonas.All"/>.
        /// Selecting an option only updates the in-dialog <see cref="pendingBaseStyleDefName"/>; the
        /// record is updated on Save.
        /// </summary>
        private void DrawBaseStylePicker(Rect rect, WritingStyleResolution resolution)
        {
            DiaryPersonaDef selected = DiaryPersonas.Resolve(pendingBaseStyleDefName);
            string selectedLabel = selected == null || string.IsNullOrWhiteSpace(selected.label)
                ? (selected?.defName ?? "PawnDiary.Persona.DefaultLabel".Translate().ToString())
                : selected.label;

            if (Widgets.ButtonText(rect, "PawnDiary.WritingStyle.BaseStyle".Translate(selectedLabel)))
            {
                List<FloatMenuOption> options = DiaryPersonas.All
                    .OrderBy(persona => persona == null ? string.Empty
                        : (string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label))
                    .Select(persona =>
                    {
                        DiaryPersonaDef option = persona;
                        string label = option == null || string.IsNullOrWhiteSpace(option.label)
                            ? (option?.defName ?? string.Empty)
                            : option.label;
                        return new FloatMenuOption(label, delegate
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

        /// <summary>
        /// Draws a label followed by a scrollable read-only or editable text block. Returns the height
        /// actually used (label + text block) so the caller can advance the layout cursor. When
        /// <paramref name="editable"/> is true, the <paramref name="editedText"/> callback receives the
        /// new text on each edit (used to update the in-dialog buffer, not the saved record).
        /// </summary>
        private float DrawLabeledScrollText(
            Rect rect,
            string label,
            string text,
            ref Vector2 scroll,
            bool editable = false,
            Action<string> editedText = null)
        {
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, LabelHeight);
            Widgets.Label(labelRect, label);

            Rect bodyRect = new Rect(rect.x, labelRect.yMax + 2f, rect.width, PromptAreaHeight);
            Widgets.DrawBoxSolid(bodyRect, new Color(0f, 0f, 0f, 0.25f));

            // Reserve scrollbar width so wrapped text does not slide under the grip.
            float innerWidth = Mathf.Max(20f, bodyRect.width - 16f);
            Rect viewRect = new Rect(bodyRect.x, bodyRect.y, innerWidth, PromptAreaHeight);

            float contentHeight = Text.CalcHeight(text ?? string.Empty, viewRect.width);
            Rect contentRect = new Rect(0f, 0f, viewRect.width, Mathf.Max(viewRect.height, contentHeight));

            Widgets.BeginScrollView(viewRect, ref scroll, contentRect);
            Rect textRect = new Rect(contentRect.x, contentRect.y, contentRect.width, contentRect.height);
            if (editable)
            {
                string edited = Widgets.TextArea(textRect, text ?? string.Empty);
                if (editedText != null)
                {
                    editedText(edited);
                }
            }
            else
            {
                Widgets.Label(textRect, text ?? string.Empty);
            }

            Widgets.EndScrollView();
            return labelRect.height + 2f + PromptAreaHeight;
        }

        /// <summary>
        /// Draws the override explanation panel and returns the height used (0 when no override and no
        /// shadowed custom prompt). The panel explains *why* the effective prompt differs from the
        /// base/custom choice, using distinct text for external-API vs hediff overrides.
        /// </summary>
        private float DrawOverrideExplanation(Rect rect, WritingStyleResolution resolution)
        {
            if (resolution == null
                || (resolution.source != WritingStyleRuleSource.ExternalApiOverride
                    && resolution.source != WritingStyleRuleSource.HediffOverride))
            {
                return 0f;
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

            float height = Mathf.Clamp(
                Text.CalcHeight(explanation, rect.width - Padding * 2f) + Padding * 2f,
                ExplanationMinHeight,
                ExplanationMaxHeight);
            Rect panelRect = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(panelRect, new Color(0.12f, 0.10f, 0.04f, 0.55f));
            Widgets.Label(panelRect.ContractedBy(Padding), explanation);
            return height;
        }

        /// <summary>
        /// Draws the action buttons. Save / Reset mutate the record; Load / Close do not. Buttons are
        /// sized to share the row evenly.
        /// </summary>
        private void DrawButtons(Rect rect, WritingStyleResolution resolution)
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
                Save();
                Messages.Message(
                    "PawnDiary.WritingStyle.Saved".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                Close();
            }

            if (Widgets.ButtonText(resetRect, "PawnDiary.WritingStyle.ResetToBase".Translate()))
            {
                ResetToBase();
                Messages.Message(
                    "PawnDiary.WritingStyle.Reset".Translate(),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            }

            if (Widgets.ButtonText(loadRect, "PawnDiary.WritingStyle.LoadBasePrompt".Translate()))
            {
                customRuleBuffer = BasePromptFor(pendingBaseStyleDefName);
            }

            if (Widgets.ButtonText(closeRect, "PawnDiary.WritingStyle.Close".Translate()))
            {
                Close();
            }
        }

        /// <summary>
        /// Commits the in-dialog base style selection and custom prompt to the pawn's diary record.
        /// Sanitization happens inside the setters; a blank custom prompt clears the override.
        /// </summary>
        private void Save()
        {
            if (component == null || pawn == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingBaseStyleDefName))
            {
                component.SetPersona(pawn, pendingBaseStyleDefName);
            }

            component.SetCustomWritingStyleRule(pawn, customRuleBuffer);

            // Reflect the sanitized save back into the buffer so a follow-up edit starts clean.
            customRuleBuffer = component.CustomWritingStyleRuleFor(pawn);
        }

        /// <summary>
        /// Clears the custom prompt and resets the base-style selection to the pawn's current saved
        /// style. Does not close the dialog so the player can see the result.
        /// </summary>
        private void ResetToBase()
        {
            if (component == null || pawn == null)
            {
                return;
            }

            component.SetCustomWritingStyleRule(pawn, string.Empty);
            WritingStyleResolution current = component.ResolveWritingStyleFor(pawn);
            pendingBaseStyleDefName = string.IsNullOrWhiteSpace(current.baseStyleDefName)
                ? (DiaryPersonas.Default?.defName ?? string.Empty)
                : current.baseStyleDefName;
            customRuleBuffer = string.Empty;
        }

        /// <summary>
        /// The base style's prompt-facing rule for the picker preview. Uses DiaryPersonas.RuleFor so it
        /// matches what generation would actually receive for that style (label-prefixed).
        /// </summary>
        private static string BasePromptFor(string defName)
        {
            return DiaryPersonas.RuleFor(defName);
        }

        /// <summary>
        /// The effective prompt text to show in the preview. When no override is active, this reflects
        /// the in-dialog custom buffer (if any) so the player sees what Save would produce; when an
        /// override is active, it shows the override's rule so the player understands the live state.
        /// </summary>
        private string EffectivePromptForDisplay(WritingStyleResolution resolution)
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

            // No live override: preview the edited custom buffer if present, else the selected base.
            string editedCustom = PlayerWritingStyleText.CleanRule(customRuleBuffer);
            if (!string.IsNullOrWhiteSpace(editedCustom))
            {
                return editedCustom;
            }

            return BasePromptFor(pendingBaseStyleDefName);
        }
    }
}
