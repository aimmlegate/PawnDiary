// Psychotype (outlook) catalog editor for the settings Styles tab, the sibling of the writing-style
// Persona Studio (PawnDiaryMod.PersonaStudio.cs). It edits a different saved surface
// (Settings.psychotypePresets) and shows a FAMILY selector where the persona studio shows theme tags.
//
// Manual-only contract: adding a custom psychotype here lets the player hand-pick it from the per-pawn
// voice editor, but customs never auto-roll onto new pawns (DiaryPsychotypes.RollCandidates skips
// them). Editing a BUILT-IN's label/rule/family is an override that applies everywhere, roll included.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class PawnDiaryMod
    {
        // Fixed card height: the block always shows the same fields (no variable-height tag grid), so a
        // constant is enough. Sized to fit summary + buttons + selector + all edit fields + action row.
        private const float PsychotypeStudioCardHeight = 412f;

        // The four adult roll buckets, in a stable order for the family dropdown.
        private static readonly string[] PsychotypeFamilies =
        {
            PsychotypeRollPolicy.FamilyGrounded,
            PsychotypeRollPolicy.FamilyInward,
            PsychotypeRollPolicy.FamilyIntense,
            PsychotypeRollPolicy.FamilyAnxious
        };

        /// <summary>Draws the editable psychotype catalog as one compact highlighted block.</summary>
        private void DrawPsychotypeStudio(Listing_Standard listing)
        {
            SectionTitle(listing, "PawnDiary.Settings.PsychotypeStudioTitle".Translate());
            listing.Label("PawnDiary.Settings.PsychotypeStudioHelp".Translate());
            if (!Settings.enablePsychotypes)
            {
                GUI.color = HintColor;
                listing.Label("PawnDiary.Settings.PsychotypeStudioDisabledHint".Translate());
                GUI.color = Color.white;
            }

            listing.Gap(6f);
            DrawPsychotypeStudioBlock(listing);
        }

        private void DrawPsychotypeStudioBlock(Listing_Standard listing)
        {
            // Editable rows are every non-Neutral psychotype (Neutral is the "off"/save-compat sentinel
            // and must keep its empty rule, so it never appears in the catalog editor).
            List<DiaryPsychotypeDef> types = DiaryPsychotypes.All
                .Where(type => type != null && type.defName != DiaryPsychotypes.NeutralDefName)
                .OrderBy(PsychotypeLabelForUi)
                .ToList();
            int total = types.Count;
            int custom = Settings.psychotypePresets.Customs().Count;
            int customized = Settings.psychotypePresets.presets == null
                ? 0
                : Settings.psychotypePresets.presets.Count(preset => preset != null && !preset.custom);

            Rect cardRect = listing.GetRect(PsychotypeStudioCardHeight);
            Widgets.DrawMenuSection(cardRect);
            Rect innerRect = cardRect.ContractedBy(10f);
            float y = innerRect.y;

            Widgets.LabelFit(
                new Rect(innerRect.x, y, innerRect.width, 24f),
                "PawnDiary.Settings.PsychotypeStudioSummary".Translate(total, custom, customized));
            y += 30f;

            Rect addRect = new Rect(innerRect.x, y, innerRect.width / 2f - 4f, 30f);
            Rect clearRect = new Rect(innerRect.x + innerRect.width / 2f + 4f, y, innerRect.width / 2f - 4f, 30f);
            if (ButtonTextFit(addRect, "PawnDiary.Settings.AddPsychotypePreset".Translate()))
            {
                selectedPsychotypeKey = Settings.psychotypePresets.AddCustom();
            }

            if (ButtonTextFit(clearRect, "PawnDiary.Settings.ResetPsychotypePresets".Translate()))
            {
                Settings.psychotypePresets.ResetAll();
                selectedPsychotypeKey = null;
            }

            y += 38f;

            DiaryPsychotypeDef selected = SelectedPsychotypeForSettings(types);
            DrawPsychotypeSelector(new Rect(innerRect.x, y, innerRect.width, 28f), types, selected);
            y += 38f;

            if (selected != null)
            {
                DrawSelectedPsychotypeFields(innerRect, ref y, selected);
            }
        }

        private void DrawPsychotypeSelector(Rect rect, List<DiaryPsychotypeDef> types, DiaryPsychotypeDef selected)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PsychotypePickerHeader".Translate());
            string selectedLabel = selected == null
                ? "PawnDiary.Psychotype.NeutralLabel".Translate().ToString()
                : PsychotypeOptionLabel(selected);
            if (ButtonTextFit(buttonRect, selectedLabel))
            {
                List<FloatMenuOption> options = types
                    .Select(type =>
                    {
                        DiaryPsychotypeDef option = type;
                        return new FloatMenuOption(PsychotypeOptionLabel(option), delegate
                        {
                            selectedPsychotypeKey = option.defName;
                        });
                    })
                    .ToList();

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("PawnDiary.Psychotype.NeutralLabel".Translate(), null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawSelectedPsychotypeFields(Rect innerRect, ref float y, DiaryPsychotypeDef selected)
        {
            bool custom = Settings.psychotypePresets.CustomFor(selected.defName) != null;
            DiaryPsychotypeDef baseDef = BasePsychotypeForSettings(selected);
            PsychotypePresetConfig overridePreset = custom
                ? Settings.psychotypePresets.CustomFor(selected.defName)
                : Settings.psychotypePresets.OverrideFor(selected.defName);
            string currentLabel = overridePreset?.label ?? baseDef?.label ?? string.Empty;
            string currentRule = overridePreset?.rule ?? baseDef?.rule ?? string.Empty;
            string currentFamily = PsychotypeRollPolicy.NormalizeFamily(overridePreset?.family ?? baseDef?.family);

            Widgets.LabelFit(
                new Rect(innerRect.x, y, innerRect.width - 132f, 24f),
                "PawnDiary.Settings.EditingPsychotype".Translate(PsychotypeLabelForUi(selected)));
            DrawAccentLabel(
                new Rect(innerRect.xMax - 124f, y, 124f, 24f),
                (custom
                    ? "PawnDiary.Settings.PsychotypeBadgeCustom"
                    : "PawnDiary.Settings.PsychotypeBadgeBuiltIn").Translate());

            y += 24f;
            DrawMutedLabel(
                new Rect(innerRect.x, y, innerRect.width, 20f),
                (IsPsychotypeCustomized(selected.defName)
                    ? "PawnDiary.Settings.PromptStatusCustomized"
                    : "PawnDiary.Settings.PromptStatusDefault").Translate());

            y += 24f;
            Rect labelFieldRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            string editedLabel = DrawCompactTextField(labelFieldRect, "PawnDiary.Settings.PsychotypeLabel".Translate(), currentLabel, 86f);

            y += 34f;
            Rect ruleLabelRect = new Rect(innerRect.x, y, innerRect.width, 20f);
            DrawFieldLabel(ruleLabelRect, "PawnDiary.Settings.PsychotypeRule".Translate());
            y += 22f;
            Rect ruleRect = new Rect(innerRect.x, y, innerRect.width, PersonaRuleTextAreaHeight);
            string editedRule = Widgets.TextArea(ruleRect, currentRule);

            // Label/rule write back immediately (the preset is the buffer), mirroring the persona studio.
            bool changed = !string.Equals(editedLabel, currentLabel, StringComparison.Ordinal)
                || !string.Equals(editedRule, currentRule, StringComparison.Ordinal);
            if (changed)
            {
                ApplyPsychotypeEdit(selected, custom, baseDef, editedLabel, editedRule, currentFamily);
            }

            y += PersonaRuleTextAreaHeight + 8f;
            // Family is a discrete FloatMenu pick, so its callback writes the row using the current
            // label/rule (captured at menu-open, which the fields already flushed to the preset).
            DrawPsychotypeFamilySelector(
                new Rect(innerRect.x, y, innerRect.width, 28f),
                currentFamily,
                family => ApplyPsychotypeEdit(selected, custom, baseDef, currentLabel, currentRule, family));

            y += 38f;
            Rect actionRect = new Rect(innerRect.x, y, innerRect.width, 30f);
            if (custom)
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.DeletePsychotypePreset".Translate()))
                {
                    Settings.psychotypePresets.RemoveCustom(selected.defName);
                    selectedPsychotypeKey = null;
                }
            }
            else
            {
                if (ButtonTextFit(actionRect, "PawnDiary.Settings.RestorePsychotypePreset".Translate()))
                {
                    Settings.psychotypePresets.ResetOverride(selected.defName);
                }
            }
        }

        private void DrawPsychotypeFamilySelector(Rect rect, string currentFamily, Action<string> onPick)
        {
            const float labelWidth = 112f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(labelRect.xMax + 6f, rect.y, rect.width - labelWidth - 6f, rect.height);
            Widgets.LabelFit(labelRect, "PawnDiary.Settings.PsychotypeFamily".Translate());
            if (ButtonTextFit(buttonRect, FamilyLabel(currentFamily)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = 0; i < PsychotypeFamilies.Length; i++)
                {
                    string family = PsychotypeFamilies[i];
                    options.Add(new FloatMenuOption(FamilyLabel(family), delegate { onPick?.Invoke(family); }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // Writes an edit through the preset store: for a custom row, mutate it in place; for a built-in,
        // upsert an override — or drop the override when the values match the XML default again.
        private void ApplyPsychotypeEdit(DiaryPsychotypeDef selected, bool custom, DiaryPsychotypeDef baseDef,
            string label, string rule, string family)
        {
            if (custom)
            {
                PsychotypePresetConfig customPreset = Settings.psychotypePresets.CustomFor(selected.defName);
                if (customPreset != null)
                {
                    customPreset.label = label ?? string.Empty;
                    customPreset.rule = rule ?? string.Empty;
                    customPreset.family = PsychotypeRollPolicy.NormalizeFamily(family);
                    DiaryPsychotypes.InvalidateCache();
                }

                return;
            }

            bool matchesDefault = string.Equals(label, baseDef?.label ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(rule, baseDef?.rule ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(
                    PsychotypeRollPolicy.NormalizeFamily(family),
                    PsychotypeRollPolicy.NormalizeFamily(baseDef?.family),
                    StringComparison.Ordinal);
            if (matchesDefault)
            {
                Settings.psychotypePresets.ResetOverride(selected.defName);
            }
            else
            {
                Settings.psychotypePresets.SetOverride(selected.defName, label, rule, family);
            }
        }

        private DiaryPsychotypeDef SelectedPsychotypeForSettings(List<DiaryPsychotypeDef> types)
        {
            if (types == null || types.Count == 0)
            {
                selectedPsychotypeKey = null;
                return null;
            }

            DiaryPsychotypeDef selected = types.FirstOrDefault(type => type.defName == selectedPsychotypeKey);
            if (selected != null)
            {
                return selected;
            }

            selected = types[0];
            selectedPsychotypeKey = selected?.defName;
            return selected;
        }

        private static DiaryPsychotypeDef BasePsychotypeForSettings(DiaryPsychotypeDef effective)
        {
            if (effective == null || string.IsNullOrWhiteSpace(effective.defName))
            {
                return effective;
            }

            return DefDatabase<DiaryPsychotypeDef>.GetNamedSilentFail(effective.defName) ?? effective;
        }

        private bool IsPsychotypeCustomized(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return false;
            }

            return Settings.psychotypePresets.OverrideFor(defName) != null
                || Settings.psychotypePresets.CustomFor(defName) != null;
        }

        private string PsychotypeOptionLabel(DiaryPsychotypeDef type)
        {
            string label = PsychotypeLabelForUi(type);
            return IsPsychotypeCustomized(type?.defName) ? label + " *" : label;
        }

        private static string PsychotypeLabelForUi(DiaryPsychotypeDef type)
        {
            if (type == null)
            {
                return "PawnDiary.Psychotype.NeutralLabel".Translate();
            }

            return string.IsNullOrWhiteSpace(type.label) ? type.defName : type.label;
        }

        private static string FamilyLabel(string family)
        {
            return ("PawnDiary.Settings.PsychotypeFamily." + PsychotypeRollPolicy.NormalizeFamily(family)).Translate();
        }
    }
}
