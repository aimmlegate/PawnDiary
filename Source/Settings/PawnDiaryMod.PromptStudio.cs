// Prompt Studio settings UI for Pawn Diary. This partial class edits localized prompt overrides
// without mixing that editor with API-lane controls or async connection state.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        /// <summary>
        /// Draws the compact prompt editor. System prompts and event-source prompts share one
        /// selector so only the selected prompt block is open at a time.
        /// </summary>
        private void DrawPromptStudio(Listing_Standard listing)
        {
            List<DiaryEventPromptDef> eventPromptDefs = EventPromptDefsForSettings();
            int customizedEventTypes = Settings.CustomizedEventPromptCount();
            List<PromptStudioOption> options = PromptStudioOptions(eventPromptDefs);

            Text.Font = GameFont.Medium;
            Rect titleRect = listing.GetRect(Text.LineHeight);
            Rect labelRect = new Rect(titleRect.x, titleRect.y, titleRect.width - 126f, titleRect.height);
            Widgets.Label(labelRect, "PawnDiary.Settings.PromptStudioTitle".Translate());
            Text.Font = GameFont.Small;
            Rect toggleRect = new Rect(titleRect.xMax - 118f, titleRect.y, 118f, Mathf.Min(titleRect.height, 30f));
            string toggleKey = Settings.showPromptStudio ? "PawnDiary.Settings.HidePromptStudio" : "PawnDiary.Settings.ShowPromptStudio";
            if (Widgets.ButtonText(toggleRect, toggleKey.Translate()))
            {
                Settings.showPromptStudio = !Settings.showPromptStudio;
                lastSettingsContentHeight = EstimateSettingsContentHeight();
                settingsScrollPosition.y = 0f;
            }
            listing.GapLine(6f);

            if (!Settings.showPromptStudio)
            {
                listing.Label("PawnDiary.Settings.PromptStudioSummary".Translate(
                    DiaryPromptTemplates.LoadedTemplateCount,
                    eventPromptDefs.Count,
                    customizedEventTypes));
                return;
            }

            if (options.Count == 0)
            {
                listing.Label("PawnDiary.Settings.EventPromptNoneLoaded".Translate());
                return;
            }

            DrawPromptStudioBlock(listing, options, SelectedPromptStudioOption(options));
        }

        private void DrawPromptStudioBlock(Listing_Standard listing, List<PromptStudioOption> options, PromptStudioOption selected)
        {
            Rect cardRect = listing.GetRect(PromptStudioBlockHeight(selected));
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);
            float y = innerRect.y;

            Rect selectorRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            DrawPromptStudioSelector(selectorRect, options, selected);
            y += 34f;

            bool selectedEventPrompt = selected != null && selected.IsEvent;
            Rect statusRect = new Rect(innerRect.x, y, selectedEventPrompt ? innerRect.width - 188f : innerRect.width, 24f);
            DrawMutedLabel(
                statusRect,
                (selected != null && selected.IsCustomized()
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());
            if (selectedEventPrompt)
            {
                Rect resetAllRect = new Rect(innerRect.xMax - 180f, y - 2f, 180f, 28f);
                if (ButtonTextFit(resetAllRect, "PawnDiary.Settings.ResetEventPromptOverrides".Translate()))
                {
                    Settings.ResetAllEventPromptOverrides();
                }
            }
            y += 32f;

            if (selected == null)
            {
                return;
            }

            if (selected.IsEvent)
            {
                DrawSelectedEventPromptEditor(innerRect, ref y, selected.eventPromptDef);
            }
            else
            {
                DrawSelectedSystemPromptEditor(innerRect, ref y, selected);
            }
        }

        private void DrawPromptStudioSelector(Rect rect, List<PromptStudioOption> options, PromptStudioOption selected)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PromptTypePickerHeader".Translate());

            string selectedLabel = selected == null
                ? "PawnDiary.Settings.EventPromptNoneLoaded".Translate().ToString()
                : selected.label;
            if (ButtonTextFit(buttonRect, selectedLabel))
            {
                List<FloatMenuOption> menuOptions = options
                    .Select(option =>
                    {
                        PromptStudioOption captured = option;
                        return new FloatMenuOption(captured.label, delegate
                        {
                            selectedPromptStudioKey = captured.key;
                        });
                    })
                    .ToList();

                Find.WindowStack.Add(new FloatMenu(menuOptions));
            }
        }

        private void DrawSelectedEventPromptEditor(Rect innerRect, ref float y, DiaryEventPromptDef selected)
        {
            string eventKey = EventPromptKeyForSettings(selected);
            string currentPrompt = Settings.EffectiveEventPrompt(eventKey, selected.prompt);
            string currentEnhancement = Settings.EffectiveEventEnhancement(eventKey, selected.enhancement);

            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.EventPromptPromptField".Translate());
            y += 22f;
            Rect promptRect = new Rect(innerRect.x, y, innerRect.width, EventPromptTextAreaHeight);
            string editedPrompt = Widgets.TextArea(promptRect, currentPrompt ?? string.Empty);
            if (!string.Equals(editedPrompt, currentPrompt ?? string.Empty, StringComparison.Ordinal))
            {
                Settings.SetEventPromptOverride(eventKey, editedPrompt, selected.prompt);
            }

            y += EventPromptTextAreaHeight + 8f;
            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.EventPromptEnhancementField".Translate());
            y += 22f;
            Rect enhancementRect = new Rect(innerRect.x, y, innerRect.width, EventPromptTextAreaHeight);
            string editedEnhancement = Widgets.TextArea(enhancementRect, currentEnhancement ?? string.Empty);
            if (!string.Equals(editedEnhancement, currentEnhancement ?? string.Empty, StringComparison.Ordinal))
            {
                Settings.SetEventEnhancementOverride(eventKey, editedEnhancement, selected.enhancement);
            }

            y += EventPromptTextAreaHeight + 10f;
            Rect promptResetRect = new Rect(innerRect.x, y, innerRect.width / 2f - 4f, 30f);
            Rect enhancementResetRect = new Rect(innerRect.x + innerRect.width / 2f + 4f, y, innerRect.width / 2f - 4f, 30f);
            if (ButtonTextFit(promptResetRect, "PawnDiary.Settings.RestoreEventPromptDefault".Translate()))
            {
                Settings.ResetEventPromptOverride(eventKey);
            }

            if (ButtonTextFit(enhancementResetRect, "PawnDiary.Settings.RestoreEventEnhancementDefault".Translate()))
            {
                Settings.ResetEventEnhancementOverride(eventKey);
            }
        }

        private static void DrawSelectedSystemPromptEditor(Rect innerRect, ref float y, PromptStudioOption selected)
        {
            string before = selected.CurrentPrompt() ?? string.Empty;
            DrawFieldLabel(new Rect(innerRect.x, y, innerRect.width, 20f), "PawnDiary.Settings.SystemPromptField".Translate());
            y += 22f;

            Rect promptRect = new Rect(innerRect.x, y, innerRect.width, SystemPromptTextAreaHeight);
            string edited = Widgets.TextArea(promptRect, before);
            if (!string.Equals(edited, before, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(edited)
                    || string.Equals(edited, selected.DefaultPrompt() ?? string.Empty, StringComparison.Ordinal))
                {
                    selected.ResetOverride();
                }
                else
                {
                    selected.SetOverride(edited);
                }
            }

            y += SystemPromptTextAreaHeight + 10f;
            Rect restoreRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            if (ButtonTextFit(restoreRect, "PawnDiary.Settings.RestorePromptDefault".Translate()))
            {
                selected.ResetOverride();
            }
        }

        private List<PromptStudioOption> PromptStudioOptions(List<DiaryEventPromptDef> eventPromptDefs)
        {
            List<PromptStudioOption> options = new List<PromptStudioOption>
            {
                SystemPromptOption(
                    "diary",
                    "PawnDiary.Settings.SystemPromptDiary".Translate(),
                    Settings.EffectiveSystemPrompt,
                    () => DiaryPrompts.Current.systemPrompt,
                    Settings.HasSystemPromptOverride,
                    Settings.SetSystemPromptOverride,
                    Settings.ResetSystemPromptOverride),
                SystemPromptOption(
                    "reflection",
                    "PawnDiary.Settings.SystemPromptReflection".Translate(),
                    Settings.EffectiveReflectionSystemPrompt,
                    () => DiaryPrompts.Current.systemPromptReflection,
                    Settings.HasReflectionSystemPromptOverride,
                    Settings.SetReflectionSystemPromptOverride,
                    Settings.ResetReflectionSystemPromptOverride),
                SystemPromptOption(
                    "neutral",
                    "PawnDiary.Settings.SystemPromptNeutral".Translate(),
                    Settings.EffectiveNeutralSystemPrompt,
                    () => DiaryPrompts.Current.systemPromptNeutral,
                    Settings.HasNeutralSystemPromptOverride,
                    Settings.SetNeutralSystemPromptOverride,
                    Settings.ResetNeutralSystemPromptOverride),
                SystemPromptOption(
                    "title",
                    "PawnDiary.Settings.SystemPromptTitle".Translate(),
                    Settings.EffectiveTitleSystemPrompt,
                    () => DiaryPrompts.Current.titleSystemPrompt,
                    Settings.HasTitleSystemPromptOverride,
                    Settings.SetTitleSystemPromptOverride,
                    Settings.ResetTitleSystemPromptOverride)
            };

            if (eventPromptDefs != null)
            {
                foreach (DiaryEventPromptDef def in eventPromptDefs)
                {
                    string eventKey = EventPromptKeyForSettings(def);
                    options.Add(new PromptStudioOption
                    {
                        key = PromptStudioEventPrefix + eventKey,
                        label = EventPromptOptionLabel(def),
                        eventPromptDef = def,
                        IsCustomized = () => Settings.HasEventPromptOverride(eventKey) || Settings.HasEventEnhancementOverride(eventKey)
                    });
                }
            }

            return options;
        }

        private static PromptStudioOption SystemPromptOption(
            string key,
            string label,
            Func<string> currentPrompt,
            Func<string> defaultPrompt,
            Func<bool> isCustomized,
            Action<string> setOverride,
            Action resetOverride)
        {
            return new PromptStudioOption
            {
                key = PromptStudioSystemPrefix + key,
                label = label,
                CurrentPrompt = currentPrompt,
                DefaultPrompt = defaultPrompt,
                IsCustomized = isCustomized,
                SetOverride = setOverride,
                ResetOverride = resetOverride
            };
        }

        private PromptStudioOption SelectedPromptStudioOption(List<PromptStudioOption> options)
        {
            PromptStudioOption selected = options.FirstOrDefault(option =>
                string.Equals(option.key, selectedPromptStudioKey, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault();
            if (selected != null)
            {
                selectedPromptStudioKey = selected.key;
            }

            return selected;
        }

        private static float PromptStudioBlockHeight(PromptStudioOption option)
        {
            return option != null && option.IsEvent ? 362f : 300f;
        }

        private static List<DiaryEventPromptDef> EventPromptDefsForSettings()
        {
            List<DiaryEventPromptDef> defs = DefDatabase<DiaryEventPromptDef>.AllDefsListForReading;
            if (defs == null)
            {
                return new List<DiaryEventPromptDef>();
            }

            return defs
                .Where(def => def != null)
                .OrderBy(EventPromptLabelForUi)
                .ToList();
        }

        private static string EventPromptOptionLabel(DiaryEventPromptDef def)
        {
            string label = EventPromptLabelForUi(def);
            string key = EventPromptKeyForSettings(def);
            return string.Equals(label, key, StringComparison.OrdinalIgnoreCase)
                ? label
                : label + " (" + key + ")";
        }

        private static string EventPromptLabelForUi(DiaryEventPromptDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string label = def.LabelCap.ToString();
            if (string.IsNullOrWhiteSpace(label) || string.Equals(label, def.defName, StringComparison.Ordinal))
            {
                return EventPromptKeyForSettings(def);
            }

            return label;
        }

        private static string EventPromptKeyForSettings(DiaryEventPromptDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(def.eventType) ? def.defName ?? string.Empty : def.eventType;
        }

        // One selectable row in Prompt Studio. Event rows edit DiaryEventPromptDef prompt fields;
        // system rows edit one saved system-prompt override.
        private sealed class PromptStudioOption
        {
            public string key;
            public string label;
            public DiaryEventPromptDef eventPromptDef;
            public Func<string> CurrentPrompt;
            public Func<string> DefaultPrompt;
            public Func<bool> IsCustomized;
            public Action<string> SetOverride;
            public Action ResetOverride;

            public bool IsEvent
            {
                get { return eventPromptDef != null; }
            }
        }
    }
}
