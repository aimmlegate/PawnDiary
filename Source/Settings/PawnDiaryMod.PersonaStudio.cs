// Writing-style Persona Studio settings UI for Pawn Diary. It stays separate from Prompt Studio
// because persona catalog editing mutates a different saved settings surface.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        /// <summary>Draws the editable writing-style catalog as one compact highlighted block.</summary>
        private void DrawPersonaStudio(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.PersonaStudioTitle".Translate());
            DrawPersonaStudioBlock(listing);
        }

        private void DrawPersonaStudioBlock(Listing_Standard listing)
        {
            int total = DiaryPersonas.All.Count;
            int custom = Settings.CustomPersonas().Count;
            int customized = Settings.personaPresets == null ? 0 : Settings.personaPresets.Count(preset => preset != null && !preset.custom);
            float tagPickerHeight = PersonaTagPickerHeight();

            Rect cardRect = listing.GetRect(412f + tagPickerHeight);
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);
            float y = innerRect.y;

            Widgets.LabelFit(
                new Rect(innerRect.x, y, innerRect.width, 24f),
                "PawnDiary.Settings.PersonaStudioSummary".Translate(total, custom, customized));
            y += 30f;

            Rect addRect = new Rect(innerRect.x, y, innerRect.width / 2f - 4f, 30f);
            Rect clearRect = new Rect(innerRect.x + innerRect.width / 2f + 4f, y, innerRect.width / 2f - 4f, 30f);
            if (ButtonTextFit(addRect, "PawnDiary.Settings.AddPersonaPreset".Translate()))
            {
                selectedPersonaKey = Settings.AddCustomPersona();
            }

            if (ButtonTextFit(clearRect, "PawnDiary.Settings.ResetPersonaPresets".Translate()))
            {
                Settings.ResetPersonaPresets();
                selectedPersonaKey = null;
            }

            y += 38f;

            List<DiaryPersonaDef> personas = DiaryPersonas.All
                .OrderBy(PersonaLabelForUi)
                .ToList();
            DiaryPersonaDef selected = SelectedPersonaForSettings();
            DrawPersonaSelector(new Rect(innerRect.x, y, innerRect.width, 28f), personas, selected);
            y += 38f;

            if (selected != null)
            {
                DrawSelectedPersonaFields(innerRect, ref y, selected, tagPickerHeight);
            }
        }

        private void DrawPersonaSelector(Rect rect, List<DiaryPersonaDef> personas, DiaryPersonaDef selected)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PersonaPickerHeader".Translate());
            string selectedLabel = selected == null
                ? "PawnDiary.Persona.DefaultLabel".Translate().ToString()
                : PersonaOptionLabel(selected);
            if (ButtonTextFit(buttonRect, selectedLabel))
            {
                List<FloatMenuOption> options = personas
                    .Select(persona =>
                    {
                        DiaryPersonaDef option = persona;
                        return new FloatMenuOption(PersonaOptionLabel(option), delegate
                        {
                            selectedPersonaKey = option.defName;
                        });
                    })
                    .ToList();

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("PawnDiary.Persona.DefaultLabel".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawSelectedPersonaFields(Rect innerRect, ref float y, DiaryPersonaDef selected, float tagPickerHeight)
        {
            bool custom = Settings.CustomPersonaFor(selected.defName) != null;
            DiaryPersonaDef baseDef = BasePersonaForSettings(selected);
            PersonaPresetConfig overridePreset = custom ? Settings.CustomPersonaFor(selected.defName) : Settings.PersonaOverrideFor(selected.defName);
            string currentLabel = overridePreset?.label ?? baseDef?.label ?? string.Empty;
            string currentRule = overridePreset?.rule ?? baseDef?.rule ?? string.Empty;
            List<string> currentThemes = new List<string>(overridePreset?.themes ?? baseDef?.themes ?? new List<string>());

            Widgets.LabelFit(new Rect(innerRect.x, y, innerRect.width - 132f, 24f), "PawnDiary.Settings.EditingPersona".Translate(PersonaLabelForUi(selected)));
            DrawAccentLabel(
                new Rect(innerRect.xMax - 124f, y, 124f, 24f),
                (custom
                    ? "PawnDiary.Settings.PersonaBadgeCustom"
                    : "PawnDiary.Settings.PersonaBadgeBuiltIn").Translate());

            y += 24f;
            DrawMutedLabel(
                new Rect(innerRect.x, y, innerRect.width, 20f),
                (IsPersonaCustomized(selected.defName)
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());

            y += 24f;
            Rect labelFieldRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            string editedLabel = DrawCompactTextField(labelFieldRect, "PawnDiary.Settings.PersonaLabel".Translate(), currentLabel, 86f);

            y += 34f;
            Rect ruleLabelRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            DrawFieldLabel(ruleLabelRect, "PawnDiary.Settings.PersonaRule".Translate());
            y += 22f;
            Rect ruleRect = new Rect(innerRect.x, y, innerRect.width, PersonaRuleTextAreaHeight);
            string editedRule = Widgets.TextArea(ruleRect, currentRule);

            y += PersonaRuleTextAreaHeight + 8f;
            Rect tagsLabelRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            DrawFieldLabel(tagsLabelRect, "PawnDiary.Settings.PersonaTags".Translate());
            y += 22f;
            List<string> editedThemes = DrawPersonaTagPicker(new Rect(innerRect.x, y, innerRect.width, tagPickerHeight), currentThemes, custom);

            bool changed = !string.Equals(editedLabel, currentLabel, StringComparison.Ordinal)
                || !string.Equals(editedRule, currentRule, StringComparison.Ordinal)
                || !editedThemes.SequenceEqual(currentThemes);
            if (changed)
            {
                if (custom)
                {
                    PersonaPresetConfig customPreset = Settings.CustomPersonaFor(selected.defName);
                    if (customPreset != null)
                    {
                        customPreset.label = editedLabel ?? string.Empty;
                        customPreset.rule = editedRule ?? string.Empty;
                        customPreset.themes = editedThemes;
                        DiaryPersonas.InvalidateCache();
                    }
                }
                else
                {
                    bool matchesDefault = string.Equals(editedLabel, baseDef?.label ?? string.Empty, StringComparison.Ordinal)
                        && string.Equals(editedRule, baseDef?.rule ?? string.Empty, StringComparison.Ordinal)
                        && editedThemes.SequenceEqual(baseDef?.themes ?? new List<string>());
                    if (matchesDefault)
                    {
                        Settings.ResetPersonaOverride(selected.defName);
                    }
                    else
                    {
                        Settings.SetPersonaOverride(
                            selected.defName,
                            editedLabel,
                            editedRule,
                            editedThemes);
                    }
                }
            }

            y += tagPickerHeight + 10f;
            Rect actionRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            if (custom)
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.DeletePersonaPreset".Translate()))
                {
                    Settings.RemoveCustomPersona(selected.defName);
                    selectedPersonaKey = null;
                }
            }
            else
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.RestorePersonaPreset".Translate()))
                {
                    Settings.ResetPersonaOverride(selected.defName);
                }
            }
        }

        private List<string> DrawPersonaTagPicker(Rect rect, List<string> themes, bool requireAtLeastOneTag)
        {
            List<string> selected = themes == null ? new List<string>() : new List<string>(themes);
            float gap = 8f;
            float columnWidth = (rect.width - gap) / 2f;
            float y = rect.y;
            for (int i = 0; i < DiaryPersonas.PredefinedThemeTags.Length; i += 2)
            {
                DrawPersonaTagToggle(new Rect(rect.x, y, columnWidth, PersonaTagRowHeight), DiaryPersonas.PredefinedThemeTags[i], selected, requireAtLeastOneTag);
                if (i + 1 < DiaryPersonas.PredefinedThemeTags.Length)
                {
                    DrawPersonaTagToggle(new Rect(rect.x + columnWidth + gap, y, columnWidth, PersonaTagRowHeight), DiaryPersonas.PredefinedThemeTags[i + 1], selected, requireAtLeastOneTag);
                }

                y += PersonaTagRowHeight + PersonaTagRowGap;
            }

            return selected;
        }

        private static void DrawPersonaTagToggle(Rect rect, string tag, List<string> selected, bool requireAtLeastOneTag)
        {
            bool enabled = selected.Contains(tag);
            bool before = enabled;
            Widgets.CheckboxLabeled(rect, PersonaTagLabel(tag), ref enabled);
            if (enabled == before)
            {
                return;
            }

            if (enabled)
            {
                if (!selected.Contains(tag))
                {
                    selected.Add(tag);
                }
            }
            else
            {
                if (!requireAtLeastOneTag || selected.Count > 1)
                {
                    selected.Remove(tag);
                }
            }
        }

        private DiaryPersonaDef SelectedPersonaForSettings()
        {
            DiaryPersonaDef selected = DiaryPersonas.All.FirstOrDefault(persona => persona.defName == selectedPersonaKey);
            if (selected != null)
            {
                return selected;
            }

            selected = DiaryPersonas.All.FirstOrDefault();
            selectedPersonaKey = selected?.defName;
            return selected;
        }

        private static DiaryPersonaDef BasePersonaForSettings(DiaryPersonaDef effective)
        {
            if (effective == null || string.IsNullOrWhiteSpace(effective.defName))
            {
                return effective;
            }

            return DefDatabase<DiaryPersonaDef>.GetNamedSilentFail(effective.defName) ?? effective;
        }

        private bool IsPersonaCustomized(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return false;
            }

            return Settings.PersonaOverrideFor(defName) != null || Settings.CustomPersonaFor(defName) != null;
        }

        private string PersonaOptionLabel(DiaryPersonaDef persona)
        {
            string label = PersonaLabelForUi(persona);
            return IsPersonaCustomized(persona?.defName) ? label + " *" : label;
        }

        private static string PersonaLabelForUi(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

        private static string PersonaTagLabel(string tag)
        {
            return ("PawnDiary.Settings.PersonaTag." + tag).Translate();
        }

        private static float PersonaTagPickerHeight()
        {
            float rows = Mathf.Ceil(DiaryPersonas.PredefinedThemeTags.Length / 2f);
            return Mathf.Max(PersonaTagRowHeight, (rows * PersonaTagRowHeight) + (Mathf.Max(0f, rows - 1f) * PersonaTagRowGap));
        }
    }
}
